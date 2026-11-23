using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Dal.Repositories;

/// <summary>EAGER LOADING: картка фільму з деталями, жанрами й найближчими сеансами — ОДНИМ SQL-запитом.</summary>
public sealed class MovieRepository(CatalogDbContext context) : GenericRepository<Movie>(context), IMovieRepository
{
    public Task<Movie?> GetWithDetailsAsync(int id, DateTime screeningsFrom, CancellationToken ct = default)
        => Set.Include(m => m.Details)                                              // 1:1
              .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)              // M:N
              .Include(m => m.Screenings                                            // 1:N (filtered include)
                  .Where(s => !s.IsCancelled && s.StartsAt >= screeningsFrom)
                  .OrderBy(s => s.StartsAt))
                  .ThenInclude(s => s.Hall)
              .AsSingleQuery()                                                      // явно: один запит з JOIN
              .FirstOrDefaultAsync(m => m.Id == id, ct);

    public Task<bool> ExistsAsync(string title, int releaseYear, int? exceptId, CancellationToken ct = default)
        => Set.AnyAsync(m => m.Title == title && m.ReleaseYear == releaseYear
                             && (exceptId == null || m.Id != exceptId), ct);

    public void SetExpectedVersion(Movie movie, byte[] rowVersion)
        => Context.Entry(movie).Property(m => m.RowVersion).OriginalValue = rowVersion;
}
