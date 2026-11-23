using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Dal.Repositories;

/// <summary>
/// EXPLICIT LOADING: жанр завантажується першим запитом, а його фільми — ДРУГИМ, окремим запитом,
/// лише тоді, коли вони справді потрібні (у логах SQL видно два SELECT).
/// </summary>
public sealed class GenreRepository(CatalogDbContext context) : GenericRepository<Genre>(context), IGenreRepository
{
    public async Task<Genre?> GetWithMoviesAsync(int id, CancellationToken ct = default)
    {
        var genre = await Set.FirstOrDefaultAsync(g => g.Id == id, ct);                     // 1-й запит
        if (genre is null) return null;

        await Context.Entry(genre)
            .Collection(g => g.MovieGenres)
            .Query()
            .Include(mg => mg.Movie)
            .OrderByDescending(mg => mg.AddedAt)
            .LoadAsync(ct);                                                                 // 2-й запит
        return genre;
    }

    public Task<Genre?> GetBySlugAsync(string slug, CancellationToken ct = default)
        => Set.FirstOrDefaultAsync(g => g.Slug == slug, ct);   // tracked: жанр прив'язується до фільму
}
