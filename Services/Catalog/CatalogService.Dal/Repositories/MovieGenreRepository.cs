using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Dal.Repositories;

/// <summary>
/// LINQ to Entities по проміжній сутності M:N. Фільтр за ВЛАСНИМ полем зв'язку (AddedAt),
/// навігація до обох кінців (Genre, Movie) і проєкція — усе транслюється в один SQL на рівні БД.
/// </summary>
public sealed class MovieGenreRepository(CatalogDbContext context)
    : GenericRepository<MovieGenre>(context), IMovieGenreRepository
{
    public async Task<IReadOnlyList<GenreMovieRow>> GetRecentlyAddedAsync(
        string genreSlug, DateTime since, CancellationToken ct = default)
        => await Set
            .Where(mg => mg.Genre.Slug == genreSlug && mg.AddedAt >= since)
            .OrderByDescending(mg => mg.AddedAt)
            .Select(mg => new GenreMovieRow(mg.MovieId, mg.Movie.Title, mg.Movie.ReleaseYear,
                                            mg.Movie.AgeRating, mg.AddedAt, mg.IsPrimary))
            .AsNoTracking()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<GenreStatsRow>> GetGenreStatsAsync(CancellationToken ct = default)
        => await Context.Genres
            .Select(g => new GenreStatsRow(
                g.Id, g.Name, g.Slug,
                g.MovieGenres.Count(mg => !mg.Movie.IsDeleted),
                g.MovieGenres.Count(mg => mg.IsPrimary && !mg.Movie.IsDeleted)))
            .OrderByDescending(s => s.MoviesCount).ThenBy(s => s.Name)
            .AsNoTracking()
            .ToListAsync(ct);
}
