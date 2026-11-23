using CatalogService.Api.Infrastructure;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Api.Controllers;

/// <summary>Розклад сеансів.</summary>
[ApiController]
[Route("api/screenings")]
[Produces("application/json")]
public sealed class ScreeningsController(IScreeningService screenings) : ApiControllerBase
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
        var created = await screenings.CreateAsync(input, ct);
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
        return Ok(await screenings.ChangePriceAsync(id, input, ct));
    }

    /// <summary>Скасувати сеанс. Повторне скасування — 409.</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id, CancellationToken ct)
    {
        await screenings.CancelAsync(id, ct);
        return NoContent();
    }
}
