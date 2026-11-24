using Microsoft.Extensions.Logging;
using Platform.Messaging;

namespace CatalogService.Bll.IntegrationEvents;

/// <summary>
/// Лаб. 08: обгортка публікатора для BLL Каталогу. Події публікуються вже ПІСЛЯ SaveChangesAsync, тобто зміна
/// в БД зафіксована. Якщо брокер у цей момент недоступний, клієнт не має отримати 500 на успішну операцію
/// (він повторив би запит і отримав 409 / дубль) — тому помилка публікації логується з усіма ідентифікаторами
/// (EventId, тип, routing key, CorrelationId) на рівні Error і не прокидається далі.
/// <para>Обмеження: таку подію втрачено — копії в Бронюванні / Відгуках лишаються старими до наступної зміни.
/// Гарантію «зміна ⇔ подія» дав би Transactional Outbox (подія пишеться в таблицю outbox у тій самій транзакції,
/// що й зміна, а окремий фоновий процес публікує її) — описано в README (Лаб. 08) як наступний крок.</para>
/// </summary>
public sealed class AfterCommitEventPublisher(IEventPublisher inner, ILogger<AfterCommitEventPublisher> logger) : IEventPublisher
{
    public async Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct = default)
        where T : IntegrationEvent
    {
        try
        {
            await inner.PublishAsync(@event, exchange, routingKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Подію {EventType} {EventId} ({Exchange}/{RoutingKey}) НЕ опубліковано: зміну в БД збережено, копії інших сервісів не оновляться (CorrelationId {CorrelationId})",
                typeof(T).Name, @event.EventId, exchange, routingKey, @event.CorrelationId);
        }
    }
}
