using AutoMapper;
using CatalogService.Bll.Caching;
using CatalogService.Bll.Dtos;
using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;

namespace CatalogService.Bll.Services;

/// <summary>Довідники. Лаб. 06: жанри й зали — найчастіше читані й майже незмінні дані, кешуються з absolute expiration.</summary>
public sealed class GenreService(IUnitOfWork uow, IMapper mapper, TimeProvider clock, ITwoLevelCache cache) : IGenreService
{
    public async Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct) =>
        (await cache.GetOrCreateAsync<List<GenreDto>>(CacheKeys.Genres, CachePolicy.Reference,
            async c => mapper.Map<List<GenreDto>>((await uow.Genres.GetAllAsync(c)).OrderBy(g => g.Name)), ct))!;

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
        (await cache.GetOrCreateAsync<List<HallDto>>(CacheKeys.Halls, CachePolicy.Reference,
            async c => mapper.Map<List<HallDto>>((await uow.Halls.GetAllAsync(c)).OrderBy(h => h.Id)), ct))!;
}
