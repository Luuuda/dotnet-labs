using System.Diagnostics;
using CatalogService.Api.Infrastructure;
using CatalogService.Api.Metrics;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Api.Controllers;

/// <summary>Розклад сеансів. Лаб. 07: операції запису — з метриками (screenings.*, catalog.operation.duration).</summary>
[ApiController]
[Route("api/screenings")]
[Produces("application/json")]
public sealed class ScreeningsController(IScreeningService screenings, CatalogMetrics metrics, ILogger<ScreeningsController> logger) : ApiControllerBase
{
    /// <summary>Розклад з фільтрами (дата, фільм, зала, формат, ціна), сортуванням і пагінацією — друга специфікація.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<ScreeningDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ScreeningDto>>> GetList(
        [FromQuery] ScreeningQuery query, [FromServices] IValidator<ScreeningQuery> validator, CancellationToken ct)
    {
        if (!await IsValidAsync(validator, query, ct)) return ValidationProblem(ModelState);
        var page = await screenings.GetListAsync(query, ct);
        AddPaginationHeaders(page);
        return Ok(page);
    }

    /// <summary>Сеанс за Id.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<ScreeningDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScreeningDto>> GetById(long id, CancellationToken ct)
        => Ok(await screenings.GetByIdAsync(id, ct));

    /// <summary>Запланувати сеанс. Зала зайнята в цей час — 409.</summary>
    [HttpPost]
    [ProducesResponseType<ScreeningDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScreeningDto>> Create(
        CreateScreeningDto input, [FromServices] IValidator<CreateScreeningDto> validator, CancellationToken ct)
    {
        if (!await IsValidAsync(validator, input, ct)) return ValidationProblem(ModelState);
        var created = await metrics.MeasureAsync("screening_schedule", () => screenings.CreateAsync(input, ct));
        metrics.ScreeningScheduled(created.Format);
        Activity.Current?.SetTag("screening.id", created.Id);
        logger.LogInformation("API: сеанс {ScreeningId} фільму {MovieId} заплановано на {StartsAt}", created.Id, created.MovieId, created.StartsAt);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Змінити ціну (метод сутності Screening.ChangePrice). Для скасованого сеансу — 409.</summary>
    [HttpPut("{id:long}/price")]
    [ProducesResponseType<ScreeningDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScreeningDto>> ChangePrice(
        long id, ChangePriceDto input, [FromServices] IValidator<ChangePriceDto> validator, CancellationToken ct)
    {
        if (!await IsValidAsync(validator, input, ct)) return ValidationProblem(ModelState);
        var changed = await metrics.MeasureAsync("screening_price_change", () => screenings.ChangePriceAsync(id, input, ct));
        metrics.ScreeningPriceChanged(changed.Format);
        return Ok(changed);
    }

    /// <summary>Скасувати сеанс. Повторне скасування — 409.</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        await metrics.MeasureAsync("screening_cancel", () => screenings.CancelAsync(id, ct));
        metrics.ScreeningCancelled();
        Activity.Current?.SetTag("screening.id", id);
        logger.LogInformation("API: сеанс {ScreeningId} скасовано", id);
        return NoContent();
    }
}
