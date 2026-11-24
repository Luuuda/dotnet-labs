using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Metrics;
using Platform.Aggregator.Models;
using Platform.Aggregator.Services;

namespace Platform.Aggregator.Controllers;

/// <summary>
/// Агрегувальні ендпоінти: одна відповідь замість кількох запитів клієнта до різних сервісів.
/// Для зовнішніх клієнтів (через Gateway) Aggregator лишається HTTP-ендпоінтом; до мікросервісів він ходить по gRPC.
/// </summary>
[ApiController]
[Route("api/aggregator")]
[Produces("application/json")]
public sealed class AggregatorController(ServiceSourcesFactory sourcesFactory, AggregateCache cache, ILoggerFactory loggers,
    AggregatorMetrics metrics) : ControllerBase
{
    /// <summary>
    /// Повна картка бронювання: квитки (booking-api), актуальні сеанси й фільми (catalog-api), рейтинг і відгуки (reviews-api).
    /// Якщо catalog-api чи reviews-api недоступний — 200 з наявними даними і переліком Warnings.
    /// </summary>
    /// <param name="id">Id бронювання.</param>
    /// <param name="transport">grpc (за замовчуванням) або http — транспорт Лаб. 05, для порівняння затримок.</param>
    /// <param name="ct">Скасування запиту клієнтом.</param>
    [HttpGet("bookings/{id:long}/full")]
    [ProducesResponseType<BookingCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BookingCardDto>> GetBookingCard(long id, [FromQuery] string? transport, CancellationToken ct)
    {
        var sources = sourcesFactory.Get(transport);
        var aggregator = new BookingCardAggregator(sources, loggers.CreateLogger<BookingCardAggregator>());
        var (card, fromCache) = await MeasureAsync("booking_card", sources,
            () => cache.GetOrBuildAsync(AggregateCache.BookingKey(id), AggregateCache.BookingTtl,
                Bypass(sources), () => aggregator.GetAsync(id, ct), c => c.Warnings.Count == 0, ct),
            c => c.Warnings.Count);
        SetHeaders(sources, fromCache);
        return card is not null
            ? Ok(card)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Бронювання не знайдено", detail: $"Бронювання {id} не існує");
    }

    /// <summary>Сторінка фільму: опис і розклад (catalog-api) + рейтинг і відгуки (reviews-api) — паралельно.</summary>
    /// <param name="movieId">Id фільму.</param>
    /// <param name="transport">grpc (за замовчуванням) або http.</param>
    /// <param name="ct">Скасування запиту клієнтом.</param>
    [HttpGet("movies/{movieId:int}/full")]
    [ProducesResponseType<MovieCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MovieCardDto>> GetMovieCard(int movieId, [FromQuery] string? transport, CancellationToken ct)
    {
        var sources = sourcesFactory.Get(transport);
        var aggregator = new MovieCardAggregator(sources, loggers.CreateLogger<MovieCardAggregator>());
        var (card, fromCache) = await MeasureAsync("movie_card", sources,
            () => cache.GetOrBuildAsync(AggregateCache.MovieKey(movieId), AggregateCache.MovieTtl,
                Bypass(sources), () => aggregator.GetAsync(movieId, ct), c => c.Warnings.Count == 0, ct),
            c => c.Warnings.Count);
        SetHeaders(sources, fromCache);
        return card is not null
            ? Ok(card)
            : Problem(statusCode: StatusCodes.Status404NotFound, title: "Фільм не знайдено", detail: $"Фільму {movieId} немає в каталозі");
    }

    /// <summary>
    /// Афіша: кілька фільмів з рейтингами (Лаб. 06). Через gRPC — два пакетні виклики паралельно,
    /// через REST (?transport=http) — по запиту на кожен фільм до кожного сервісу.
    /// </summary>
    /// <param name="ids">Id фільмів через кому, до 20.</param>
    /// <param name="transport">grpc (за замовчуванням) або http.</param>
    /// <param name="ct">Скасування запиту клієнтом.</param>
    [HttpGet("movies/board")]
    [ProducesResponseType<PosterBoardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PosterBoardDto>> GetPosterBoard([FromQuery] string ids, [FromQuery] string? transport, CancellationToken ct)
    {
        var movieIds = (ids ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => int.TryParse(x, out var id) ? id : 0).Distinct().ToList();
        if (movieIds.Count is 0 or > PosterBoardAggregator.MaxMovies || movieIds.Any(id => id <= 0))
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Некоректний перелік фільмів",
                detail: $"ids: від 1 до {PosterBoardAggregator.MaxMovies} додатних Id через кому");

        var sources = sourcesFactory.Get(transport);
        var aggregator = new PosterBoardAggregator(sources, loggers.CreateLogger<PosterBoardAggregator>());
        var (board, fromCache) = await MeasureAsync("poster_board", sources,
            () => cache.GetOrBuildAsync(AggregateCache.BoardKey(movieIds), AggregateCache.BookingTtl,
                Bypass(sources), async () => await aggregator.GetAsync(movieIds, ct), b => b.Warnings.Count == 0, ct),
            b => b.Warnings.Count);
        SetHeaders(sources, fromCache);
        return Ok(board);
    }

    /// <summary>
    /// Лаб. 07: тривалість і результат агрегації — у метрики (aggregator.responses, aggregator.request.duration).
    /// Записується для ВСІХ виконань, включно з винятками (503 від обов'язкового сервісу, скасування).
    /// </summary>
    private async Task<(T? Value, bool FromCache)> MeasureAsync<T>(string endpoint, ServiceSources sources,
        Func<Task<(T? Value, bool FromCache)>> action, Func<T, int> warnings) where T : class
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            var outcome = result.Value is null ? "not_found" : warnings(result.Value) > 0 ? "partial" : "complete";
            metrics.Record(endpoint, sources.Transport, outcome, result.FromCache, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch
        {
            metrics.Record(endpoint, sources.Transport, "failed", fromCache: false, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    /// <summary>Кеш пропускається для HTTP-транспорту (вимірювання) і на запит клієнта Cache-Control: no-cache.</summary>
    private bool Bypass(ServiceSources sources) =>
        sources.Transport == Transports.Http ||
        Request.Headers.CacheControl.ToString().Contains("no-cache", StringComparison.OrdinalIgnoreCase);

    private void SetHeaders(ServiceSources sources, bool fromCache)
    {
        Response.Headers["X-Aggregator-Transport"] = sources.Transport;
        Response.Headers["X-Cache"] = fromCache ? "HIT" : "MISS";
    }
}
