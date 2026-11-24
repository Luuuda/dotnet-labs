namespace Platform.Aggregator.Models;

// Моделі відповідей мікросервісів — лише ті поля, що потрібні агрегатору (System.Text.Json ігнорує решту).

// ---------- booking-api (Лаб. 02) ----------
public sealed record BookingDto(
    long Id, Guid CustomerId, string Status, DateTime CreatedAt, decimal TotalAmount,
    string? ContactEmail, string? Channel, IReadOnlyList<TicketDto> Tickets);

public sealed record TicketDto(
    long Id, long ScreeningId, int SeatRow, int SeatNumber, string MovieTitle, string HallName,
    DateTime StartsAt, decimal UnitPrice, decimal Discount, decimal FinalPrice, bool IsCancelled);

/// <summary>Локальна копія сеансу в сервісі бронювання (той самий Id, що в Каталозі).</summary>
public sealed record BookingScreeningDto(long Id, int MovieId, string MovieTitle, DateTime StartsAt, bool IsActive);

// ---------- catalog-api (Лаб. 03) ----------
public sealed record CatalogScreeningDto(
    long Id, int MovieId, string MovieTitle, int HallId, string HallName, DateTime StartsAt, DateTime EndsAt,
    string Format, string Language, decimal BasePrice, bool IsCancelled);

public sealed record CatalogMovieDto(
    int Id, string Title, string? OriginalTitle, short ReleaseYear, short DurationMinutes, string AgeRating,
    CatalogMovieDetailsDto? Details, IReadOnlyList<CatalogGenreDto>? Genres);

public sealed record CatalogMovieDetailsDto(string? Description, string? Director, string? Country);

public sealed record CatalogGenreDto(int GenreId, string Name, string Slug, bool IsPrimary);

public sealed record PageDto<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize);

// ---------- reviews-api (Лаб. 04) ----------
public sealed record MovieRatingDto(int MovieId, string MovieTitle, double Average, int Count,
    IReadOnlyDictionary<string, int> Distribution, int VerifiedCount);

public sealed record ReviewAuthorDto(string UserId, string DisplayName);

public sealed record ReviewDto(string Id, int MovieId, ReviewAuthorDto Author, int Rating, string? Text,
    bool VerifiedViewer, int CommentsCount, DateTime CreatedAt);
