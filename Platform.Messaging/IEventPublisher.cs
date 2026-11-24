namespace Platform.Messaging;

/// <summary>
/// Публікація інтеграційних подій. Викликається ЛИШЕ після успішного збереження змін у БД:
/// якщо транзакція не вдалася, подія не публікується (споживачі не дізнаються про зміну, якої немає).
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Серіалізує подію в JSON і публікує її в exchange з routing key як persistent-повідомлення
    /// (MessageId = EventId, Type = назва події, заголовок x-correlation-id, W3C traceparent).
    /// Повертається після підтвердження брокером (publisher confirms).
    /// </summary>
    Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct = default)
        where T : IntegrationEvent;
}
