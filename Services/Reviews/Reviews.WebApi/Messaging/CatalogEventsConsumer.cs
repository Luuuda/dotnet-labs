using Reviews.WebApi.IntegrationEvents;
using Platform.Messaging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Reviews.WebApi.Messaging;

/// <summary>
/// Лаб. 08: споживач подій Каталогу у Відгуках — BackgroundService, що живе весь час роботи сервісу.
/// <list type="number">
/// <item>створює канал на спільному IConnection (Aspire: AddRabbitMQClient("messaging"));</item>
/// <item>оголошує свою топологію: catalog-events (topic) → reviews.catalog-sync (movie.*),
/// x-dead-letter-exchange catalog-events.dlx → reviews.catalog-sync.dlq;</item>
/// <item>BasicQos(prefetchCount: 10) — не більше 10 непідтверджених повідомлень одночасно;</item>
/// <item>AsyncEventingBasicConsumer + BasicConsumeAsync(autoAck: false) — підтвердження вручну, після обробки;</item>
/// <item>кожне повідомлення: DI-scope → MovieRenamedEventHandler → команда SyncMovieTitle (MediatR) →
/// ack / retry / nack у DLQ (IntegrationEventConsumer).</item>
/// </list>
/// Зупинка — через stoppingToken: споживання скасовується, непідтверджені повідомлення брокер віддасть знову.
/// Якщо Відгуки не працюють, події накопичуються в durable-черзі й обробляються після запуску.
/// </summary>
public sealed class CatalogEventsConsumer(IServiceProvider services, ILogger<CatalogEventsConsumer> logger)
    : IntegrationEventConsumer(services, logger)
{
    private const string Queue = CatalogEventsSubscription.Queue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Один обробник на тип події (BasicProperties.Type → IIntegrationEventHandler<T> з DI-scope)
        Handle<MovieRenamedEvent>();

        // Брокер може бути ще недоступний (або перезапускатися): помилка підписки не валить сервіс —
        // повтор через 5 с. Обрив уже встановленого з'єднання відновлює сам клієнт (automatic recovery).
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAsync(stoppingToken);
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                Logger.LogError(ex, "Споживач {Consumer}: не вдалося підписатися на {Queue}, повтор через 5 с",
                    nameof(CatalogEventsConsumer), Queue);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        // Канал з publisher confirms: через нього ж публікуються копії для повтору (x-retry-count)
        await using var channel = await Connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        await EventTopology.DeclareConsumerQueueAsync(channel, CatalogEventsSubscription.Exchange, Queue,
            CatalogEventsSubscription.BindingKeys, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: PrefetchCount, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleDeliveryAsync(channel, Queue, ea, stoppingToken);
        var consumerTag = await channel.BasicConsumeAsync(Queue, autoAck: false, consumer, stoppingToken);
        Logger.LogInformation("Споживач {Consumer} слухає чергу {Queue} (prefetch {Prefetch}, bindings {Bindings})",
            nameof(CatalogEventsConsumer), Queue, PrefetchCount, string.Join(", ", CatalogEventsSubscription.BindingKeys));

        try
        {
            await MonitorDeadLetterQueueAsync(Queue, stoppingToken);   // gauge messaging.dlq.depth, поки сервіс працює
        }
        catch (OperationCanceledException)
        {
            Logger.LogInformation("Споживач {Consumer} зупиняється", nameof(CatalogEventsConsumer));
            if (channel.IsOpen) await channel.BasicCancelAsync(consumerTag, noWait: false, CancellationToken.None);
        }
    }
}
