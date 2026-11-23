using System.Net;
using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

// Лаб. 05: HTTP REST-клієнти. У Лаб. 06 основний транспорт — gRPC (GrpcServiceSources.cs); ці клієнти лишено
// для вимірювання різниці затримок HTTP vs gRPC (GET /api/aggregator/...?transport=http).

/// <summary>
/// Базовий typed-клієнт: GET з десеріалізацією в DTO (System.Text.Json, camelCase) і логуванням тривалості.
/// 404 → null (ресурсу немає), інші помилки → виняток (його обробляє агрегатор як часткову відмову).
/// CorrelationId, service discovery і стійкість додає ServiceDefaults до всіх HttpClient.
/// </summary>
public abstract class ServiceClient(HttpClient http, ILogger logger, string serviceName)
{
    public string ServiceName { get; } = serviceName;

    protected async Task<T?> GetAsync<T>(string path, CancellationToken ct)
    {
        var started = System.Diagnostics.Stopwatch.StartNew();
        logger.LogInformation("Calling {Service} GET {Path}", ServiceName, path);
        using var response = await http.GetAsync(path, ct);
        logger.LogInformation("{Service} responded {StatusCode} in {ElapsedMs} ms", ServiceName, (int)response.StatusCode, started.ElapsedMilliseconds);

        if (response.StatusCode == HttpStatusCode.NotFound) return default;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(ct);
    }
}

/// <summary>Мікросервіс бронювання (Лаб. 02): BaseAddress = https+http://booking-api.</summary>
public sealed class BookingClient(HttpClient http, ILogger<BookingClient> logger) : ServiceClient(http, logger, "booking-api"), IBookingSource
{
    public Task<BookingDto?> GetBookingAsync(long id, CancellationToken ct) => GetAsync<BookingDto>($"/api/bookings/{id}", ct);

    public Task<BookingScreeningDto?> GetScreeningCopyAsync(long id, CancellationToken ct)
        => GetAsync<BookingScreeningDto>($"/api/screenings/{id}", ct);
}

/// <summary>Каталог (Лаб. 03): BaseAddress = https+http://catalog-api.</summary>
public sealed class CatalogClient(HttpClient http, ILogger<CatalogClient> logger) : ServiceClient(http, logger, "catalog-api"), ICatalogSource
{
    public Task<CatalogScreeningDto?> GetScreeningAsync(long id, CancellationToken ct)
        => GetAsync<CatalogScreeningDto>($"/api/screenings/{id}", ct);

    public Task<CatalogMovieDto?> GetMovieAsync(int id, CancellationToken ct) => GetAsync<CatalogMovieDto>($"/api/movies/{id}", ct);

    public Task<PageDto<CatalogScreeningDto>?> GetScreeningsOfMovieAsync(int movieId, CancellationToken ct)
        => GetAsync<PageDto<CatalogScreeningDto>>($"/api/screenings?movieId={movieId}&sortBy=startsAt&pageSize=20", ct);

    /// <summary>REST не має пакетного ендпоінту — N паралельних запитів.</summary>
    public async Task<IReadOnlyList<CatalogMovieDto>> GetMoviesAsync(IReadOnlyCollection<int> ids, CancellationToken ct) =>
        (await Task.WhenAll(ids.Select(id => GetMovieAsync(id, ct)))).OfType<CatalogMovieDto>().ToList();
}

/// <summary>Відгуки (Лаб. 04): BaseAddress = https+http://reviews-api.</summary>
public sealed class ReviewsClient(HttpClient http, ILogger<ReviewsClient> logger) : ServiceClient(http, logger, "reviews-api"), IReviewsSource
{
    public Task<MovieRatingDto?> GetRatingAsync(int movieId, CancellationToken ct)
        => GetAsync<MovieRatingDto>($"/api/movies/{movieId}/rating", ct);

    public Task<PageDto<ReviewDto>?> GetTopReviewsAsync(int movieId, int count, CancellationToken ct)
        => GetAsync<PageDto<ReviewDto>>($"/api/reviews?movieId={movieId}&sortBy=rating&pageSize={count}", ct);

    public async Task<IReadOnlyList<MovieRatingDto>> GetRatingsAsync(IReadOnlyCollection<int> movieIds, CancellationToken ct) =>
        (await Task.WhenAll(movieIds.Select(id => GetRatingAsync(id, ct)))).OfType<MovieRatingDto>().ToList();
}
