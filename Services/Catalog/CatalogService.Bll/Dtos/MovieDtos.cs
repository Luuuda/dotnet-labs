using System.ComponentModel.DataAnnotations;

namespace CatalogService.Bll.Dtos;

/// <summary>Параметри списку фільмів: пагінація, фільтри, сортування (усе виконується в SQL).</summary>
public sealed class MovieQuery
{
    /// <summary>Номер сторінки (з 1).</summary>
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    /// <summary>Розмір сторінки (за замовчуванням 20, максимум 100 — більше значення обрізається).</summary>
    [Range(1, int.MaxValue)] public int PageSize { get; init; } = Paging.DefaultPageSize;
    /// <summary>Пошук у назві або оригінальній назві.</summary>
    [StringLength(100)] public string? Search { get; init; }
    /// <summary>Slug жанру, наприклад sci-fi.</summary>
    [StringLength(60)] public string? Genre { get; init; }
    public int? YearFrom { get; init; }
    public int? YearTo { get; init; }
    /// <summary>0+, 6+, 12+, 16+, 18+</summary>
    public string? AgeRating { get; init; }
    public int? MinDuration { get; init; }
    public int? MaxDuration { get; init; }
    /// <summary>title | year | duration | rating | created</summary>
    public string SortBy { get; init; } = "title";
    /// <summary>asc | desc</summary>
    public string SortDir { get; init; } = "asc";
}

public sealed record MovieListItemDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string? OriginalTitle { get; init; }
    public short ReleaseYear { get; init; }
    public short DurationMinutes { get; init; }
    public string AgeRating { get; init; } = "";
    public decimal? AverageRating { get; init; }
    public int RatingsCount { get; init; }
    public string? PrimaryGenre { get; init; }
    public IReadOnlyList<string> Genres { get; init; } = [];
}

public sealed record MovieDetailsDto
{
    public string Description { get; init; } = "";
    public string? Director { get; init; }
    public string? Country { get; init; }
    public string? Language { get; init; }
    public string? PosterUrl { get; init; }
    public string? TrailerUrl { get; init; }
}

public sealed record MovieGenreDto
{
    public int GenreId { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public bool IsPrimary { get; init; }
    public DateTime AddedAt { get; init; }
}

public sealed record ScreeningShortDto
{
    public long Id { get; init; }
    public DateTime StartsAt { get; init; }
    public string HallName { get; init; } = "";
    public string Format { get; init; } = "";
    public decimal BasePrice { get; init; }
}

/// <summary>Картка фільму (Eager Loading: деталі + жанри + найближчі сеанси одним запитом).</summary>
public sealed record MovieDto
{
    public int Id { get; init; }
    public string Title { get; init; } = "";
    public string? OriginalTitle { get; init; }
    public short ReleaseYear { get; init; }
    public short DurationMinutes { get; init; }
    public string AgeRating { get; init; } = "";
    public decimal? AverageRating { get; init; }
    public int RatingsCount { get; init; }
    public MovieDetailsDto? Details { get; init; }
    public IReadOnlyList<MovieGenreDto> Genres { get; init; } = [];
    public IReadOnlyList<ScreeningShortDto> UpcomingScreenings { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    /// <summary>Версія рядка (base64) — передається назад у PUT.</summary>
    public string RowVersion { get; init; } = "";
}

/// <summary>
/// Створення фільму. DataAnnotations — лише прості правила (обов'язковість, довжина);
/// діапазони, допустимі значення та перехресні правила — у CreateMovieDtoValidator (FluentValidation).
/// </summary>
public record CreateMovieDto
{
    [Required, StringLength(200)] public string Title { get; init; } = "";
    [StringLength(200)] public string? OriginalTitle { get; init; }
    public int ReleaseYear { get; init; }
    public int DurationMinutes { get; init; }
    [Required] public string AgeRating { get; init; } = "";
    [StringLength(4000)] public string? Description { get; init; }
    [StringLength(200)] public string? Director { get; init; }
    [StringLength(100)] public string? Country { get; init; }
    [StringLength(50)] public string? Language { get; init; }
    /// <summary>Slug-и жанрів; перший — основний жанр.</summary>
    public List<string> Genres { get; init; } = [];
}

public sealed record UpdateMovieDto : CreateMovieDto
{
    /// <summary>RowVersion з GET (base64) — інакше 409 при одночасному редагуванні.</summary>
    [Required] public string RowVersion { get; init; } = "";
}

public sealed record AssignGenreDto
{
    [Required, StringLength(60)] public string Slug { get; init; } = "";
    public bool IsPrimary { get; init; }
}
