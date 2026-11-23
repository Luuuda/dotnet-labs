using AutoMapper;
using CatalogService.Bll.Dtos;
using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;

namespace CatalogService.Bll.Services;

public sealed class GenreService(IUnitOfWork uow, IMapper mapper, TimeProvider clock) : IGenreService
{
    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct) =>
        mapper.Map<List<GenreDto>>((await uow.Genres.GetAllAsync(ct)).OrderBy(g => g.Name));

    /// <summary>Explicit Loading: жанр — першим запитом, його фільми — другим.</summary>
    public async Task<GenreWithMoviesDto> GetWithMoviesAsync(int id, CancellationToken ct) =>
        mapper.Map<GenreWithMoviesDto>(await uow.Genres.GetWithMoviesAsync(id, ct)
                                       ?? throw EntityNotFoundException.For<Genre>(id));

    /// <summary>LINQ to Entities по проміжній сутності M:N (фільтр за AddedAt).</summary>
    public async Task<IReadOnlyList<RecentGenreMovieDto>> GetRecentlyAddedAsync(string slug, DateTime? since, CancellationToken ct)
    {
        _ = await uow.Genres.GetBySlugAsync(slug, ct) ?? throw new EntityNotFoundException($"Жанр «{slug}» не знайдено");
        var from = since ?? clock.GetUtcNow().UtcDateTime.AddDays(-90);
        return mapper.Map<List<RecentGenreMovieDto>>(await uow.MovieGenres.GetRecentlyAddedAsync(slug, from, ct));
    }

    public async Task<IReadOnlyList<GenreStatsDto>> GetStatsAsync(CancellationToken ct) =>
        mapper.Map<List<GenreStatsDto>>(await uow.MovieGenres.GetGenreStatsAsync(ct));

    public async Task<IReadOnlyList<HallDto>> GetHallsAsync(CancellationToken ct) =>
        mapper.Map<List<HallDto>>((await uow.Halls.GetAllAsync(ct)).OrderBy(h => h.Id));
}
