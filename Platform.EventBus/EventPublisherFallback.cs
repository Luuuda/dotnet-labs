using Microsoft.Extensions.Logging;

namespace Platform.EventBus;

/// <summary>
/// Публікатор для запуску без брокера (unit / API-тести, окремий запуск сервісу без AppHost): подія лише
/// логуються. В AppHost рядок підключення ConnectionStrings:messaging є завжди, і реєструється
/// <see cref="RabbitMqEventPublisher"/>.
/// </summary>
public sealed class LoggingOnlyEventPublisher(ILogger<LoggingOnlyEventPublisher> logger) : IEventPublisher
{
    public Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct = default)
        where T : IntegrationEvent
    {
        logger.LogWarning("RabbitMQ не налаштовано: подію {EventType} {EventId} ({Exchange}/{RoutingKey}) не опубліковано",
            typeof(T).Name, @event.EventId, exchange, routingKey);
        return Task.CompletedTask;
    }
}
