using System.Diagnostics;
using CatalogService.Api.Infrastructure;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Api.Controllers;

/// <summary>Фільми. Контролер не звертається до DbContext — лише до IMovieService.</summary>
[ApiController]
[Route("api/movies")]
[Produces("application/json")]
public sealed class MoviesController(IMovieService movies, ILogger<MoviesController> logger) : ApiControllerBase
{
    /// <summary>
    /// Список фільмів з пагінацією, фільтрацією та сортуванням (виконується в SQL: WHERE / ORDER BY / OFFSET-FETCH).
    /// Метадані — у тілі та в заголовках X-Total-Count і X-Pagination.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<MovieListItemDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<MovieListItemDto>>> GetList(
        [FromQuery] MovieQuery query, [FromServices] IValidator<MovieQuery> validator, CancellationToken ct)
    {
        if (!await IsValidAsync(validator, query, ct)) return ValidationProblem(ModelState);
        var page = await movies.GetListAsync(query, ct);
        AddPaginationHeaders(page);
        return Ok(page);
    }

    /// <summary>Картка фільму: деталі, жанри, найближчі сеанси (Eager Loading — один SQL-запит).</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieDto>> GetById(int id, CancellationToken ct)
        => Ok(await movies.GetByIdAsync(id, ct));

    /// <summary>Додати фільм. Повторне створення з тією самою назвою та роком — 409.</summary>
    [HttpPost]
    [ProducesResponseType<MovieDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> Create(
        CreateMovieDto input, [FromServices] IValidator<CreateMovieDto> validator, CancellationToken ct)
    {
        if (!await IsValidAsync(validator, input, ct)) return ValidationProblem(ModelState);
        var created = await movies.CreateAsync(input, ct);
        Activity.Current?.SetTag("movie.id", created.Id);
        logger.LogInformation("API: фільм {MovieId} «{Title}» додано до каталогу", created.Id, created.Title);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Оновити фільм (потрібен rowVersion з GET; застаріла версія — 409).</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> Update(
        int id, UpdateMovieDto input, [FromServices] IValidator<UpdateMovieDto> validator, CancellationToken ct)
    {
        if (!await IsValidAsync(validator, input, ct)) return ValidationProblem(ModelState);
        return Ok(await movies.UpdateAsync(id, input, ct));
    }

    /// <summary>Видалити фільм (soft delete). Якщо є заплановані сеанси — 409.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await movies.DeleteAsync(id, ct);
        Activity.Current?.SetTag("movie.id", id);
        logger.LogInformation("API: фільм {MovieId} знято з каталогу", id);
        return NoContent();
    }

    /// <summary>Прив'язати жанр (запис у проміжну сутність MovieGenres з AddedAt). Дубль — 409.</summary>
    [HttpPost("{id:int}/genres")]
    [ProducesResponseType<MovieDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<MovieDto>> AddGenre(
        int id, AssignGenreDto input, [FromServices] IValidator<AssignGenreDto> validator, CancellationToken ct)
    {
        if (!await IsValidAsync(validator, input, ct)) return ValidationProblem(ModelState);
        return Ok(await movies.AddGenreAsync(id, input, ct));
    }

    /// <summary>Відв'язати жанр.</summary>
    [HttpDelete("{id:int}/genres/{genreId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveGenre(int id, int genreId, CancellationToken ct)
    {
        await movies.RemoveGenreAsync(id, genreId, ct);
        return NoContent();
    }
}
