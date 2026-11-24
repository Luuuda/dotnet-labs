using BookingService.Bll.Caching;
using BookingService.Dal.Abstractions;
using Microsoft.Extensions.Logging;
using Platform.EventBus;

namespace BookingService.Api.IntegrationEvents.Handlers;

/// <summary>
/// Нова ціна сеансу → BasePrice копії (для МАЙБУТНІХ бронювань). Ціни в уже оформлених квитках
/// (dbo.Tickets.UnitPrice) — знімок на момент покупки, їх подія не змінює.
/// </summary>
public sealed class ScreeningPriceChangedHandler(IUnitOfWork uow, ITwoLevelCache cache, ILogger<ScreeningPriceChangedHandler> logger)
    : CatalogSyncHandlerBase<ScreeningPriceChangedEvent>(uow, cache, logger)
{
    protected override async Task<IReadOnlyCollection<long>> ApplyAsync(ScreeningPriceChangedEvent e, CancellationToken ct)
    {
        if (e.NewPrice <= 0)
            throw new PermanentMessageException($"Некоректна ціна {e.NewPrice} у {nameof(ScreeningPriceChangedEvent)} для сеансу {e.ScreeningId}");

        var rows = await Uow.Screenings.ChangePriceFromCatalogAsync(e.ScreeningId, e.NewPrice, e.OccurredAt, ct);
        if (rows == 0)
            Logger.LogWarning("Ціну сеансу {ScreeningId} не змінено: сеансу немає в копії або вже застосовано новішу зміну", e.ScreeningId);
        else
            Logger.LogInformation("Сеанс {ScreeningId}: ціна {OldPrice} → {NewPrice} (оформлені квитки не змінюються)",
                e.ScreeningId, e.OldPrice, e.NewPrice);
        return [e.ScreeningId];
    }
}
