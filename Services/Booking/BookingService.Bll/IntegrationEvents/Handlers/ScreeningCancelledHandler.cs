using BookingService.Bll.Caching;
using BookingService.Dal.Abstractions;
using Microsoft.Extensions.Logging;

namespace BookingService.Bll.IntegrationEvents.Handlers;

/// <summary>Сеанс скасовано в Каталозі → IsActive = 0 у копії: нові бронювання на нього відхиляються.</summary>
public sealed class ScreeningCancelledHandler(IUnitOfWork uow, ITwoLevelCache cache, ILogger<ScreeningCancelledHandler> logger)
    : CatalogSyncHandlerBase<ScreeningCancelledEvent>(uow, cache, logger)
{
    protected override async Task<IReadOnlyCollection<long>> ApplyAsync(ScreeningCancelledEvent e, CancellationToken ct)
    {
        var rows = await Uow.Screenings.DeactivateFromCatalogAsync(e.ScreeningId, e.OccurredAt, ct);
        Logger.LogInformation("Сеанс {ScreeningId} ({MovieTitle}, {StartsAt}) деактивовано в копії; рядків: {Rows}",
            e.ScreeningId, e.MovieTitle, e.StartsAt, rows);
        return [e.ScreeningId];
    }
}
