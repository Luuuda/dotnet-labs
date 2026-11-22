using BookingService.Bll.Dtos;
using BookingService.Bll.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace BookingService.Api.Controllers;

/// <summary>
/// Бронювання. Контролер лише перекладає HTTP ↔ виклики сервісу; статуси помилок (404/409/400)
/// формує глобальний DomainExceptionHandler.
/// </summary>
[ApiController]
[Route("api/bookings")]
[Produces("application/json")]
public sealed class BookingsController(IBookingService bookings) : ControllerBase
{
    /// <summary>Бронювання з квитками та історією статусів.</summary>
    [HttpGet("{id:long}")]
    [ProducesResponseType<BookingDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BookingDto>> GetById(long id, CancellationToken ct)
        => Ok(await bookings.GetAsync(id, ct));

    /// <summary>
    /// Створити бронювання (одна транзакція: Bookings + BookingDetails + Tickets + History).
    /// Ціна й назва фільму записуються в квиток як знімок.
    /// </summary>
    [HttpPost]
    [ProducesResponseType<BookingDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<BookingDto>> Create(CreateBookingDto input, CancellationToken ct)
    {
        var created = await bookings.CreateAsync(input, ct);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    /// <summary>Підтвердити місця (Submitted → AwaitingValidation → SeatsConfirmed). Повторне підтвердження — 409.</summary>
    [HttpPost("{id:long}/confirm")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingActionDto? action, CancellationToken ct)
    {
        await bookings.ConfirmAsync(id, action, ct);
        return NoContent();
    }

    /// <summary>
    /// Оплатити (одна транзакція з двох репозиторіїв: Payments + Bookings → Paid).
    /// Повторний запит з тим самим Idempotency-Key / externalTransactionId повертає ту саму оплату (200).
    /// </summary>
    [HttpPost("{id:long}/pay")]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<PaymentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PaymentDto>> Pay(long id, PayBookingDto input,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey, CancellationToken ct)
    {
        var result = await bookings.PayAsync(id, input, idempotencyKey, ct);
        return result.Replayed
            ? Ok(result.Payment)
            : CreatedAtAction(nameof(Payments), new { id }, result.Payment);
    }

    /// <summary>Видати квитки (Paid → Issued). Для неоплаченого бронювання — 409.</summary>
    [HttpPost("{id:long}/issue")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Issue(long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingActionDto? action, CancellationToken ct)
    {
        await bookings.IssueAsync(id, action, ct);
        return NoContent();
    }

    /// <summary>Скасувати (лише до оплати); місця звільняються.</summary>
    [HttpPost("{id:long}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(long id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] BookingActionDto? action, CancellationToken ct)
    {
        await bookings.CancelAsync(id, action, ct);
        return NoContent();
    }

    /// <summary>Оплати бронювання.</summary>
    [HttpGet("{id:long}/payments")]
    [ProducesResponseType<IReadOnlyList<PaymentDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<PaymentDto>>> Payments(long id, CancellationToken ct)
        => Ok(await bookings.GetPaymentsAsync(id, ct));

    /// <summary>Видалити (soft delete) — лише скасоване бронювання, інакше 409.</summary>
    [HttpDelete("{id:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(long id, CancellationToken ct)
    {
        await bookings.DeleteAsync(id, ct);
        return NoContent();
    }
}
