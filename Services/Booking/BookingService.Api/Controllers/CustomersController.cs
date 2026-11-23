using BookingService.Bll.Dtos;
using BookingService.Bll.Services;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Api.Controllers;

/// <summary>Клієнти (глядачі). Тонкий адаптер: жодного SQL і жодних бізнес-рішень — лише виклик сервісу.</summary>
[ApiController]
[Route("api/customers")]
[Produces("application/json")]
public sealed class CustomersController(ICustomerService customers, IBookingService bookings) : ControllerBase
{
    /// <summary>Список клієнтів з пошуком за email/ім'ям і пагінацією.</summary>
    [HttpGet]
    [ProducesResponseType<PagedResult<CustomerDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await customers.ListAsync(search, page, pageSize, ct));

    /// <summary>Клієнт за Id.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDto>> GetById(Guid id, CancellationToken ct)
        => Ok(await customers.GetAsync(id, ct));

    /// <summary>Створити клієнта.</summary>
    [HttpPost]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Create(CreateCustomerDto input, CancellationToken ct)
    {
        var created = await customers.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Оновити клієнта. Потрібен rowVersion з GET — інакше 409 (оптимістична конкурентність).</summary>
    [HttpPut("{id:guid}")]
    [ProducesResponseType<CustomerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, UpdateCustomerDto input, CancellationToken ct)
        => Ok(await customers.UpdateAsync(id, input, ct));

    /// <summary>Видалити клієнта (soft delete — історія бронювань зберігається).</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await customers.DeleteAsync(id, ct);
        return NoContent();
    }

    /// <summary>Бронювання клієнта з фільтром за статусом і пагінацією (процедура usp_Bookings_GetByCustomer).</summary>
    [HttpGet("{id:guid}/bookings")]
    [ProducesResponseType<PagedResult<BookingSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PagedResult<BookingSummaryDto>>> Bookings(
        Guid id, [FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => Ok(await bookings.ListByCustomerAsync(id, status, page, pageSize, ct));
}
