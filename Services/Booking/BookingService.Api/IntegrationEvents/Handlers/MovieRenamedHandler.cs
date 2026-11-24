using BookingService.Bll.Caching;
using BookingService.Dal.Abstractions;
using Microsoft.Extensions.Logging;
using Platform.EventBus;

namespace BookingService.Api.IntegrationEvents.Handlers;

/// <summary>Фільм перейменовано → MovieTitle в усіх його сеансах копії (назва в оформлених квитках — знімок).</summary>
public sealed class MovieRenamedHandler(IUnitOfWork uow, ITwoLevelCache cache, ILogger<MovieRenamedHandler> logger)
    : CatalogSyncHandlerBase<MovieRenamedEvent>(uow, cache, logger)
{
    protected override async Task<IReadOnlyCollection<long>> ApplyAsync(MovieRenamedEvent e, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.NewTitle))
            throw new PermanentMessageException($"Порожня назва в {nameof(MovieRenamedEvent)} для фільму {e.MovieId}");

        var rows = await Uow.Screenings.RenameMovieFromCatalogAsync(e.MovieId, e.NewTitle, e.OccurredAt, ct);
        Logger.LogInformation("Фільм {MovieId}: «{OldTitle}» → «{NewTitle}» у {Rows} сеансах копії",
            e.MovieId, e.OldTitle, e.NewTitle, rows);
        return await Uow.Screenings.GetIdsByMovieAsync(e.MovieId, ct);
    }
}
