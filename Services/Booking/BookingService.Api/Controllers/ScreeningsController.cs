using BookingService.Bll.Dtos;
using BookingService.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Api.Controllers;

/// <summary>Сеанси — локальна копія довідника Каталогу.</summary>
[ApiController]
[Route("api/screenings")]
[Produces("application/json")]
public sealed class ScreeningsController(IScreeningService screenings) : ControllerBase
{
    /// <summary>Сеанси, що починаються не раніше <paramref name="from"/>.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ScreeningDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ScreeningDto>>> List(
        [FromQuery] DateTime? from, [FromQuery] bool onlyActive = true, CancellationToken ct = default)
        => Ok(await screenings.ListAsync(from, onlyActive, ct));

    /// <summary>Сеанс за Id.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<ScreeningDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScreeningDto>> GetById(long id, CancellationToken ct)
        => Ok(await screenings.GetAsync(id, ct));

    /// <summary>Карта зали: зайняті місця та кількість вільних.</summary>
    [HttpGet("{id:long}/seats")]
    [ProducesResponseType<SeatMapDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SeatMapDto>> Seats(long id, CancellationToken ct)
        => Ok(await screenings.GetSeatMapAsync(id, ct));

    /// <summary>Додати сеанс у локальну копію (Id — той самий, що в Каталозі).</summary>
    [HttpPost]
    [ProducesResponseType<ScreeningDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ScreeningDto>> Create(CreateScreeningDto input, CancellationToken ct)
    {
        var created = await screenings.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Оновити копію сеансу (імітація події ScreeningChanged з Каталогу).</summary>
    [HttpPut("{id:long}")]
    [ProducesResponseType<ScreeningDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScreeningDto>> Update(long id, UpsertScreeningDto input, CancellationToken ct)
        => Ok(await screenings.UpdateAsync(id, input, ct));

    /// <summary>Видалити сеанс. Якщо на нього є квитки — 409.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await screenings.DeleteAsync(id, ct);
        return NoContent();
    }
}
