using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Platform.Messaging;

/// <summary>
/// Публікація інтеграційних подій у RabbitMQ (RabbitMQ.Client 7.x).
/// <list type="bullet">
/// <item>одне <see cref="IConnection"/> на застосунок (реєструє Aspire: AddRabbitMQClient("messaging")); реєструється
/// як Singleton — один канал публікації на весь сервіс;
/// для публікації — окремий канал з publisher confirms: PublishAsync повертається, лише коли брокер
/// підтвердив, що прийняв повідомлення (durable exchange + persistent-повідомлення → переживе перезапуск);</item>
/// <item>тіло — JSON (System.Text.Json, camelCase, <see cref="MessagingJson"/>);</item>
/// <item>BasicProperties: Persistent, ContentType application/json, MessageId = EventId, Type = назва події,
/// Timestamp; заголовки x-correlation-id, x-schema-version, x-occurred-at і контекст трасування (W3C traceparent);</item>
/// <item>кожна публікація логується структуровано: тип події, EventId, exchange, routing key, CorrelationId.</item>
/// </list>
/// Exchange оголошується перед першою публікацією (durable topic) — повторне оголошення безпечне.
/// </summary>
public sealed class RabbitMqEventPublisher(IServiceProvider services, ILogger<RabbitMqEventPublisher> logger)
    : IEventPublisher, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock = new(1, 1);          // IChannel не потокобезпечний для одночасної публікації
    private readonly HashSet<string> _declaredExchanges = [];
    private IChannel? _channel;

    public async Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct = default)
        where T : IntegrationEvent
    {
        var eventType = typeof(T).Name;
        using var activity = TracePropagation.StartPublishActivity(exchange, routingKey, eventType, @event.EventId);
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = @event.EventId, ["EventType"] = eventType, ["CorrelationId"] = @event.CorrelationId
        });

        var props = new BasicProperties
        {
            Persistent = true,                                      // повідомлення пишеться на диск брокера
            ContentType = "application/json",
            ContentEncoding = "utf-8",
            MessageId = @event.EventId.ToString(),                  // EventId — ключ ідемпотентності у споживачів
            Type = eventType,                                       // за ним споживач вибирає обробник
            Timestamp = new AmqpTimestamp(new DateTimeOffset(@event.OccurredAt).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?>
            {
                [MessagingHeaders.CorrelationId] = @event.CorrelationId,
                [MessagingHeaders.SchemaVersion] = @event.SchemaVersion,
                [MessagingHeaders.OccurredAt] = @event.OccurredAt.ToString("O", CultureInfo.InvariantCulture)
            }
        };
        CorrelationContext.Set(@event.CorrelationId);               // CorrelationId їде і в baggage (для споживача)
        TracePropagation.Inject(props);                             // traceparent / tracestate / baggage
        var body = JsonSerializer.SerializeToUtf8Bytes(@event, MessagingJson.Options);

        await _lock.WaitAsync(ct);
        try
        {
            var channel = await GetChannelAsync(ct);
            if (_declaredExchanges.Add(exchange))
                await EventTopology.DeclareExchangeAsync(channel, exchange, ct);
            await channel.BasicPublishAsync(exchange, routingKey, mandatory: false, props, body, ct);
        }
        catch (Exception ex)
        {
            _declaredExchanges.Remove(exchange);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Не вдалося опублікувати {EventType} {EventId} у {Exchange}/{RoutingKey} (CorrelationId {CorrelationId})",
                eventType, @event.EventId, exchange, routingKey, @event.CorrelationId);
            throw;
        }
        finally
        {
            _lock.Release();
        }

        MessagingMetrics.Published.Add(1, new KeyValuePair<string, object?>("messaging.message.type", eventType));
        logger.LogInformation("Published {EventType} {EventId} to {Exchange}/{RoutingKey} ({BodyBytes} байт, CorrelationId {CorrelationId})",
            eventType, @event.EventId, exchange, routingKey, body.Length, @event.CorrelationId);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel is { IsOpen: true }) return _channel;
        if (_channel is not null) await _channel.DisposeAsync();
        _declaredExchanges.Clear();
        // IConnection береться з DI лише тут: недоступний брокер не ламає створення сервісів, що публікують події
        var connection = services.GetRequiredService<IConnection>();
        _channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        return _channel;
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null) await _channel.DisposeAsync();
        _lock.Dispose();
    }
}
