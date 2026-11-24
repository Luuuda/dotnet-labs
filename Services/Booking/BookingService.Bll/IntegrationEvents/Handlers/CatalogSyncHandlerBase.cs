using BookingService.Bll.Caching;
using BookingService.Dal.Abstractions;
using Microsoft.Extensions.Logging;
using Platform.Messaging;

namespace BookingService.Bll.IntegrationEvents.Handlers;

/// <summary>
/// Спільна схема ідемпотентного обробника Бронювання (Лаб. 08, Частина 3.3):
/// <code>
/// BEGIN TRANSACTION
///   INSERT dbo.ProcessedEvents (EventId)   -- PRIMARY KEY: повторна доставка → помилка 2627 → дубль
///   UPDATE dbo.Screenings …                -- зміна локальної копії
/// COMMIT                                   -- обидві зміни разом або жодної
/// </code>
/// Перевірка «чи вже оброблено» і запис EventId — одна операція (INSERT з унікальним ключем), тому дві паралельні
/// доставки того самого повідомлення не можуть обидві пройти перевірку. Якщо оновлення копії впало, відкочується
/// і запис EventId — повтор обробить подію заново. Після COMMIT інвалідується кеш (Лаб. 06), щоб нові бронювання
/// бачили нову ціну / статус / назву.
/// </summary>
public abstract class CatalogSyncHandlerBase<TEvent>(IUnitOfWork uow, ITwoLevelCache cache, ILogger logger)
    : IIntegrationEventHandler<TEvent> where TEvent : IntegrationEvent
{
    protected IUnitOfWork Uow { get; } = uow;
    protected ILogger Logger { get; } = logger;

    public async Task HandleAsync(TEvent @event, CancellationToken ct)
    {
        var eventType = typeof(TEvent).Name;
        await Uow.BeginTransactionAsync(ct: ct);
        IReadOnlyCollection<long> touched;
        try
        {
            if (!await Uow.ProcessedEvents.TryAddAsync(@event.EventId, eventType, ct))
            {
                await Uow.RollbackAsync(ct);
                Logger.LogInformation("Duplicate {EventType} {EventId} пропущено: подію вже оброблено раніше (CorrelationId {CorrelationId})",
                    eventType, @event.EventId, @event.CorrelationId);
                return;                                                   // споживач підтвердить (ack) — нічого не змінено
            }

            touched = await ApplyAsync(@event, ct);
            await Uow.CommitAsync(ct);
        }
        catch
        {
            await Uow.RollbackAsync(CancellationToken.None);
            throw;                                                        // рішення retry / DLQ приймає споживач
        }

        foreach (var screeningId in touched)
            await cache.InvalidateAsync(eventType, ct, CacheKeys.Screening(screeningId));
        await cache.BumpVersionAsync(CacheKeys.ScreeningsListVersion, eventType, ct);
    }

    /// <summary>Зміна локальної копії в поточній транзакції. Повертає ідентифікатори змінених сеансів (для кешу).</summary>
    protected abstract Task<IReadOnlyCollection<long>> ApplyAsync(TEvent @event, CancellationToken ct);
}
