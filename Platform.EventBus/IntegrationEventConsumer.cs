using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

/// <summary>
/// Базовий споживач інтеграційних подій (Лаб. 08, Частина 3). Сервіс-споживач успадковує його як BackgroundService,
/// у своєму ExecuteAsync створює канал, оголошує топологію, ставить BasicQos(prefetch 10) і підписує
/// AsyncEventingBasicConsumer з autoAck: false, а кожне повідомлення передає в <see cref="HandleDeliveryAsync"/>.
/// <para>Рішення щодо повідомлення (кожне логується):</para>
/// <list type="table">
/// <item><term>успіх</term><description>BasicAck — лише ПІСЛЯ того, як обробник зберіг зміни і EventId;</description></item>
/// <item><term>дубль</term><description>обробник побачив, що EventId уже оброблено → нічого не змінює → BasicAck;</description></item>
/// <item><term>некоректний JSON, невідомий тип, несумісна версія, <see cref="PermanentMessageException"/></term>
/// <description>BasicNack(requeue: false) одразу → DLX → DLQ: повтор нічого не змінить;</description></item>
/// <item><term>тимчасова помилка (БД, мережа)</term><description>копія публікується знову в ту саму чергу із
/// заголовком x-retry-count + 1 (після паузи 1 с, 2 с, 4 с), оригінал підтверджується (ack); після
/// <see cref="MaxRetries"/> повторів — BasicNack(requeue: false) → DLQ.</description></item>
/// </list>
/// Чому не BasicNack(requeue: true): повідомлення миттєво повернулося б у голову черги і оброблялося б нескінченно
/// (poison message), без лічильника спроб і без паузи. Копія з лічильником дає скінченну кількість повторів.
/// Якщо процес впаде між публікацією копії і ack оригіналу, повідомлення прийде двічі — це покриває ідемпотентність.
/// </summary>
public abstract class IntegrationEventConsumer(IServiceProvider services, ILogger logger) : BackgroundService
{
    /// <summary>Максимальна кількість повторів обробки, після якої повідомлення йде в DLQ.</summary>
    public const int MaxRetries = 3;
    /// <summary>Скільки непідтверджених повідомлень брокер віддає споживачу одночасно (BasicQos).</summary>
    protected const ushort PrefetchCount = 10;
    /// <summary>Найвища версія схеми подій, яку розуміє цей споживач.</summary>
    protected virtual int MaxSupportedSchemaVersion => 1;

    private readonly Dictionary<string, Func<IServiceProvider, ReadOnlyMemory<byte>, CancellationToken, Task>> _handlers = new();

    /// <summary>Спільне з'єднання (Aspire: AddRabbitMQClient). Береться з DI під час підписки — усередині циклу повторів.</summary>
    protected IConnection Connection => services.GetRequiredService<IConnection>();
    protected ILogger Logger { get; } = logger;

    /// <summary>Затримка перед повтором: 1 с, 2 с, 4 с (експоненційно). Тести можуть зменшити.</summary>
    protected virtual TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt - 1));

    /// <summary>
    /// Реєструє обробник типу події: BasicProperties.Type = назва класу події (так її ставить publisher).
    /// Обробник береться з нового DI-scope на кожне повідомлення.
    /// </summary>
    protected void Handle<TEvent>() where TEvent : IntegrationEvent =>
        _handlers[typeof(TEvent).Name] = async (services, body, ct) =>
        {
            var @event = JsonSerializer.Deserialize<TEvent>(body.Span, MessagingJson.Options)
                         ?? throw new JsonException("Порожнє тіло повідомлення");
            if (@event.EventId == Guid.Empty) throw new JsonException("Подія без eventId");
            await services.GetRequiredService<IIntegrationEventHandler<TEvent>>().HandleAsync(@event, ct);
        };

    /// <summary>Повна обробка одного повідомлення: трасування, логування, вибір обробника, ack / retry / nack.</summary>
    protected async Task HandleDeliveryAsync(IChannel channel, string queue, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var props = ea.BasicProperties;
        var eventType = props.Type ?? "(без типу)";
        var messageId = props.MessageId;
        var correlationId = ReadHeader(props, MessagingHeaders.CorrelationId);
        var retryCount = ReadRetryCount(props);

        using var activity = TracePropagation.StartConsumerActivity(ea, queue);   // нащадок span публікації
        CorrelationContext.Set(correlationId);
        activity?.SetTag("correlation.id", correlationId);
        activity?.SetTag("messaging.rabbitmq.retry_count", retryCount);
        using var scope = Logger.BeginScope(new Dictionary<string, object?>
        {
            ["EventId"] = messageId, ["EventType"] = eventType, ["CorrelationId"] = correlationId,
            ["Queue"] = queue, ["RetryCount"] = retryCount
        });
        var started = Stopwatch.GetTimestamp();
        Logger.LogInformation("Received {EventType} {EventId} з черги {Queue} (спроба {Attempt}, CorrelationId {CorrelationId})",
            eventType, messageId, queue, retryCount + 1, correlationId);

        string outcome;
        try
        {
            if (!_handlers.TryGetValue(eventType, out var handler))
                throw new PermanentMessageException($"Невідомий тип події «{eventType}»");
            var schemaVersion = int.TryParse(ReadHeader(props, MessagingHeaders.SchemaVersion), out var v) ? v : 1;
            if (schemaVersion > MaxSupportedSchemaVersion)
                throw new PermanentMessageException(
                    $"Несумісна версія схеми {eventType}: {schemaVersion} (підтримується до {MaxSupportedSchemaVersion})");

            await using (var serviceScope = services.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope())
                await handler(serviceScope.ServiceProvider, ea.Body, ct);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);     // ack — лише після успішного збереження
            outcome = "processed";
            Logger.LogInformation("Acked {EventType} {EventId}: оброблено за {ElapsedMs:F0} мс",
                eventType, messageId, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        catch (Exception ex) when (ex is JsonException or PermanentMessageException)
        {
            // Постійна помилка: некоректний JSON, невідомий тип, несумісна версія → одразу DLQ без повторів
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Logger.LogError(ex, "Nack {EventType} {EventId} → DLQ {Queue}.dlq: постійна помилка, повтор не допоможе (CorrelationId {CorrelationId})",
                eventType, messageId, queue, correlationId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            outcome = "dead_letter";
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Зупинка сервісу: повідомлення не підтверджене — брокер віддасть його знову після перепідключення
            Logger.LogWarning("Обробку {EventType} {EventId} перервано зупинкою сервісу — повідомлення повернеться в чергу",
                eventType, messageId);
            throw;
        }
        catch (Exception ex) when (retryCount < MaxRetries)
        {
            // Тимчасова помилка: копія з x-retry-count + 1 у ту саму чергу, оригінал — ack
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            var attempt = retryCount + 1;
            var delay = RetryDelay(attempt);
            Logger.LogWarning(ex, "Retry {Attempt}/{MaxRetries} для {EventType} {EventId} через {DelayMs} мс (CorrelationId {CorrelationId})",
                attempt, MaxRetries, eventType, messageId, delay.TotalMilliseconds, correlationId);
            await Task.Delay(delay, ct);
            await RepublishForRetryAsync(channel, queue, ea, attempt, ex, ct);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
            outcome = "retry";
        }
        catch (Exception ex)
        {
            // Ліміт повторів вичерпано → DLQ
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            Logger.LogError(ex, "Nack {EventType} {EventId} → DLQ {Queue}.dlq: вичерпано {MaxRetries} повтори (CorrelationId {CorrelationId})",
                eventType, messageId, queue, MaxRetries, correlationId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, ct);
            outcome = "dead_letter";
        }

        var tags = new TagList { { "messaging.destination.name", queue }, { "messaging.message.type", eventType }, { "outcome", outcome } };
        MessagingMetrics.Consumed.Add(1, tags);
        MessagingMetrics.ProcessingDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
    }

    /// <summary>
    /// Моніторинг глибини DLQ: окремий канал, QueueDeclarePassive раз на 15 с → gauge messaging.dlq.depth;
    /// коли в DLQ з'являються нові повідомлення — Warning у лог. Працює, доки сервіс не зупинено (stoppingToken).
    /// </summary>
    protected async Task MonitorDeadLetterQueueAsync(string queue, CancellationToken stoppingToken)
    {
        var deadLetterQueue = queue + EventTopology.DeadLetterQueueSuffix;
        await using var channel = await Connection.CreateChannelAsync(cancellationToken: stoppingToken);
        long last = -1;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var depth = (long)await channel.MessageCountAsync(deadLetterQueue, stoppingToken);
                MessagingMetrics.SetDeadLetterDepth(deadLetterQueue, depth);
                if (depth > last && depth > 0)
                    Logger.LogWarning("У {DeadLetterQueue} {Depth} необроблених повідомлень — потрібен розбір (Management UI → Get messages)",
                        deadLetterQueue, depth);
                last = depth;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Logger.LogWarning(ex, "Не вдалося прочитати глибину {DeadLetterQueue}", deadLetterQueue);
            }
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
        }
    }

    /// <summary>
    /// Копія повідомлення для повтору: те саме тіло й властивості (MessageId = EventId не змінюється — ідемпотентність
    /// працює й для повторів), x-retry-count + 1, текст останньої помилки. Публікується через default exchange ("")
    /// прямо в чергу цього споживача — не в catalog-events, щоб повтор не отримали інші сервіси.
    /// </summary>
    private static async Task RepublishForRetryAsync(IChannel channel, string queue, BasicDeliverEventArgs ea,
        int attempt, Exception error, CancellationToken ct)
    {
        var source = ea.BasicProperties;
        var headers = source.Headers is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source.Headers);
        headers[MessagingHeaders.RetryCount] = attempt;
        headers[MessagingHeaders.LastError] = Truncate($"{error.GetType().Name}: {error.Message}", 500);

        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = source.ContentType,
            ContentEncoding = source.ContentEncoding,
            MessageId = source.MessageId,
            Type = source.Type,
            Timestamp = source.Timestamp,
            Headers = headers
        };
        await channel.BasicPublishAsync(exchange: "", routingKey: queue, mandatory: false, props, ea.Body.ToArray(), ct);
    }

    private static int ReadRetryCount(IReadOnlyBasicProperties props) =>
        props.Headers is not null && props.Headers.TryGetValue(MessagingHeaders.RetryCount, out var value)
            ? value switch { int i => i, long l => (int)l, byte[] b when int.TryParse(Encoding.UTF8.GetString(b), out var n) => n, _ => 0 }
            : 0;

    private static string? ReadHeader(IReadOnlyBasicProperties props, string name) =>
        props.Headers is not null && props.Headers.TryGetValue(name, out var value)
            ? value switch { byte[] b => Encoding.UTF8.GetString(b), null => null, _ => value.ToString() }
            : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
