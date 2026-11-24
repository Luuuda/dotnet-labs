using Platform.Aggregator.Models;

namespace Platform.Aggregator.Clients;

// Лаб. 06: агрегатори працюють з цими абстракціями, а не з транспортом. Основна реалізація — gRPC
// (GrpcServiceSources.cs); HTTP-клієнти з Лаб. 05 (ServiceClients.cs) лишено для порівняння затримок (?transport=http).
// null — ресурсу немає (HTTP 404 / gRPC NOT_FOUND); інші помилки — виняток, його перетворює на попередження PartialResults.

public interface IBookingSource
{
    string ServiceName { get; }
    Task<BookingDto?> GetBookingAsync(long id, CancellationToken ct);
    Task<BookingScreeningDto?> GetScreeningCopyAsync(long id, CancellationToken ct);
}

public interface ICatalogSource
{
    string ServiceName { get; }
    Task<CatalogScreeningDto?> GetScreeningAsync(long id, CancellationToken ct);
    Task<CatalogMovieDto?> GetMovieAsync(int id, CancellationToken ct);
    Task<PageDto<CatalogScreeningDto>?> GetScreeningsOfMovieAsync(int movieId, CancellationToken ct);
    /// <summary>Кілька фільмів: gRPC — один пакетний виклик GetMoviesByIds; REST — паралельні GET на кожен Id.</summary>
    Task<IReadOnlyList<CatalogMovieDto>> GetMoviesAsync(IReadOnlyCollection<int> ids, CancellationToken ct);
}

public interface IReviewsSource
{
    string ServiceName { get; }
    Task<MovieRatingDto?> GetRatingAsync(int movieId, CancellationToken ct);
    Task<PageDto<ReviewDto>?> GetTopReviewsAsync(int movieId, int count, CancellationToken ct);
    /// <summary>Рейтинги кількох фільмів: gRPC — GetRatingsForMovies; REST — паралельні GET на кожен фільм.</summary>
    Task<IReadOnlyList<MovieRatingDto>> GetRatingsAsync(IReadOnlyCollection<int> movieIds, CancellationToken ct);
}

/// <summary>Набір джерел одного транспорту.</summary>
public sealed record ServiceSources(string Transport, IBookingSource Bookings, ICatalogSource Catalog, IReviewsSource Reviews);

public static class Transports
{
    public const string Grpc = "grpc";
    public const string Http = "http";
}

/// <summary>Вибирає джерела за транспортом (keyed services): gRPC — за замовчуванням, HTTP — для порівняння з Лаб. 05.</summary>
public sealed class ServiceSourcesFactory(IServiceProvider services)
{
    public ServiceSources Get(string? transport)
    {
        var key = string.Equals(transport, Transports.Http, StringComparison.OrdinalIgnoreCase) ? Transports.Http : Transports.Grpc;
        return new ServiceSources(key,
            services.GetRequiredKeyedService<IBookingSource>(key),
            services.GetRequiredKeyedService<ICatalogSource>(key),
            services.GetRequiredKeyedService<IReviewsSource>(key));
    }
}
