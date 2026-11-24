namespace Platform.Aggregator.Models;

// Композитні DTO під бізнес-сценарії (а не копії структур окремих сервісів).

/// <summary>
/// Картка бронювання: що купив глядач (booking-api) + актуальний стан сеансів і фільмів (catalog-api)
/// + що про ці фільми кажуть інші глядачі (reviews-api). Warnings — які джерела не відповіли.
/// </summary>
public sealed record BookingCardDto(
    BookingSummary Booking,
    IReadOnlyList<SessionCard> Sessions,
    IReadOnlyList<string> Warnings);

public sealed record BookingSummary(
    long Id, string Status, DateTime CreatedAt, decimal TotalAmount, int TicketsCount,
    string? ContactEmail, string? Channel);

/// <summary>Один сеанс у бронюванні: квитки покупця + дані каталогу й відгуків про фільм.</summary>
public sealed record SessionCard(
    long ScreeningId,
    string MovieTitle,
    DateTime StartsAt,
    string HallName,
    IReadOnlyList<string> Seats,
    decimal PaidAmount,
    ScreeningLiveInfo? Live,          // catalog-api: формат, мова, поточна ціна, чи не скасовано
    MovieInfo? Movie,                 // catalog-api: опис фільму
    MovieOpinion? Opinion);           // reviews-api: рейтинг і найкращі відгуки

public sealed record ScreeningLiveInfo(string Format, string Language, decimal CurrentPrice, bool IsCancelled, DateTime EndsAt);

public sealed record MovieInfo(
    int Id, string Title, string? OriginalTitle, short ReleaseYear, short DurationMinutes, string AgeRating,
    string? Director, string? Description, IReadOnlyList<string> Genres);

public sealed record MovieOpinion(double? AverageRating, int ReviewsCount, IReadOnlyList<ReviewSnippet> TopReviews);

public sealed record ReviewSnippet(string Author, int Rating, string? Text, bool VerifiedViewer, DateTime CreatedAt);

/// <summary>Сторінка фільму: опис і розклад (catalog-api) + рейтинг і відгуки (reviews-api) — усе паралельно.</summary>
public sealed record MovieCardDto(
    int MovieId,
    MovieInfo? Movie,
    IReadOnlyList<UpcomingScreening> Screenings,
    MovieOpinion? Opinion,
    IReadOnlyList<string> Warnings);

public sealed record UpcomingScreening(long Id, DateTime StartsAt, string HallName, string Format, string Language, decimal Price);

/// <summary>
/// Лаб. 06: афіша — кілька фільмів з рейтингами одним запитом. Через gRPC це два пакетні виклики
/// (GetMoviesByIds + GetRatingsForMovies), через REST — по одному запиту на кожен фільм до кожного сервісу.
/// </summary>
public sealed record PosterBoardDto(IReadOnlyList<MovieTile> Movies, IReadOnlyList<string> Warnings);

public sealed record MovieTile(int Id, string Title, short ReleaseYear, short DurationMinutes, string AgeRating,
    IReadOnlyList<string> Genres, double? AverageRating, int ReviewsCount);
