using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Api.Controllers;

/// <summary>Жанри та зали (довідники).</summary>
[ApiController]
[Produces("application/json")]
public sealed class GenresController(IGenreService genres) : ControllerBase
{
    /// <summary>Усі жанри (статичний сідінг HasData).</summary>
    [HttpGet("api/genres")]
    [ProducesResponseType<IReadOnlyList<GenreDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreDto>>> GetAll(CancellationToken ct)
        => Ok(await genres.GetAllAsync(ct));

    /// <summary>Жанр з фільмами — Explicit Loading (два SQL-запити: жанр, потім його фільми).</summary>
    [HttpGet("api/genres/{id:int}/movies")]
    [ProducesResponseType<GenreWithMoviesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GenreWithMoviesDto>> GetWithMovies(int id, CancellationToken ct)
        => Ok(await genres.GetWithMoviesAsync(id, ct));

    /// <summary>Нещодавно додані до жанру фільми — LINQ по проміжній сутності M:N (фільтр за AddedAt).</summary>
    [HttpGet("api/genres/{slug}/recent")]
    [ProducesResponseType<IReadOnlyList<RecentGenreMovieDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<RecentGenreMovieDto>>> GetRecentlyAdded(
        string slug, [FromQuery] DateTime? since, CancellationToken ct)
        => Ok(await genres.GetRecentlyAddedAsync(slug, since, ct));

    /// <summary>Скільки фільмів у кожному жанрі (GROUP BY у БД).</summary>
    [HttpGet("api/genres/stats")]
    [ProducesResponseType<IReadOnlyList<GenreStatsDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GenreStatsDto>>> GetStats(CancellationToken ct)
        => Ok(await genres.GetStatsAsync(ct));

    /// <summary>Зали кінотеатру (статичний сідінг HasData).</summary>
    [HttpGet("api/halls")]
    [ProducesResponseType<IReadOnlyList<HallDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<HallDto>>> GetHalls(CancellationToken ct)
        => Ok(await genres.GetHallsAsync(ct));
}
