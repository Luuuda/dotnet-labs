using BookingService.Bll.Caching;
using BookingService.Dal.Abstractions;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging;
using Platform.Messaging;

namespace BookingService.Bll.IntegrationEvents.Handlers;

/// <summary>Новий сеанс у Каталозі → копія в dbo.Screenings (з розміром зали — для перевірки місць).</summary>
public sealed class ScreeningScheduledHandler(IUnitOfWork uow, ITwoLevelCache cache, ILogger<ScreeningScheduledHandler> logger)
    : CatalogSyncHandlerBase<ScreeningScheduledEvent>(uow, cache, logger)
{
    protected override async Task<IReadOnlyCollection<long>> ApplyAsync(ScreeningScheduledEvent e, CancellationToken ct)
    {
        if (e.ScreeningId <= 0 || e.RowsCount <= 0 || e.SeatsPerRow <= 0 || e.BasePrice <= 0)
            throw new PermanentMessageException($"Некоректні дані сеансу {e.ScreeningId} у {nameof(ScreeningScheduledEvent)}");

        var rows = await Uow.Screenings.UpsertFromCatalogAsync(new Screening
        {
            Id = e.ScreeningId, MovieId = e.MovieId, MovieTitle = e.MovieTitle, HallId = e.HallId, HallName = e.HallName,
            RowsCount = e.RowsCount, SeatsPerRow = e.SeatsPerRow, StartsAt = e.StartsAt, BasePrice = e.BasePrice, IsActive = true
        }, e.OccurredAt, ct);
        Logger.LogInformation("Копію сеансу {ScreeningId} додано (фільм {MovieId}, {StartsAt}, ціна {Price}); рядків: {Rows}",
            e.ScreeningId, e.MovieId, e.StartsAt, e.BasePrice, rows);
        return [e.ScreeningId];
    }
}
