using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

/// <summary>Агрегувальні ендпоінти: одна відповідь замість кількох запитів клієнта до різних сервісів.</summary>
[ApiController]
[Route("api/aggregator")]
[Produces("application/json")]
public sealed class AggregatorController(BookingCardAggregator bookingCards, MovieCardAggregator movieCards) : ControllerBase
{
    /// <summary>
    /// Повна картка бронювання: квитки (booking-api), актуальні сеанси й фільми (catalog-api), рейтинг і відгуки (reviews-api).
    /// Якщо catalog-api чи reviews-api недоступний — 200 з наявними даними і переліком Warnings.
    /// </summary>
    [HttpGet("bookings/{id:long}/full")]
    [ProducesResponseType<BookingCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BookingCardDto>> GetBookingCard(long id, CancellationToken ct)
        => await bookingCards.GetAsync(id, ct) is { } card
            ? Ok(card)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Бронювання не знайдено", detail: $"Бронювання {id} не існує");

    /// <summary>Сторінка фільму: опис і розклад (catalog-api) + рейтинг і відгуки (reviews-api) — паралельно.</summary>
    [HttpGet("movies/{movieId:int}/full")]
    [ProducesResponseType<MovieCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieCardDto>> GetMovieCard(int movieId, CancellationToken ct)
        => await movieCards.GetAsync(movieId, ct) is { } card
            ? Ok(card)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Фільм не знайдено", detail: $"Фільму {movieId} немає в каталозі");
}
