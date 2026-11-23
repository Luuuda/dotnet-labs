using CatalogService.Domain.Exceptions;

namespace CatalogService.Domain.Entities;

/// <summary>
/// Фільм — корінь агрегату (аналог CatalogItem з eShop). Бізнес-правила реалізовано МЕТОДАМИ сутності,
/// а не відкритими setter-ами: стан змінюється лише через конструктор і методи, які перевіряють інваріанти.
/// </summary>
public sealed class Movie : AuditableEntity
{
    public const int TitleMaxLength = 200;
    public const short MinYear = 1895;
    public const short MaxYear = 2100;
    public const short MaxDuration = 600;
    public static readonly string[] AgeRatings = ["0+", "6+", "12+", "16+", "18+"];

    public int Id { get; private set; }
    public string Title { get; private set; } = "";
    public string? OriginalTitle { get; private set; }
    public short ReleaseYear { get; private set; }
    public short DurationMinutes { get; private set; }
    public string AgeRating { get; private set; } = "0+";

    /// <summary>Копія агрегату з сервісу Відгуків (політика дублювання Лаб. 01) — оновлюється подією.</summary>
    public decimal? AverageRating { get; private set; }
    public int RatingsCount { get; private set; }

    public bool IsDeleted { get; private set; }
    /// <summary>rowversion — картку фільму редагують кілька контент-менеджерів (оптимістична конкурентність).</summary>
    public byte[] RowVersion { get; private set; } = [];

    public MovieDetails? Details { get; private set; }                 // 1:1
    public List<MovieGenre> MovieGenres { get; private set; } = [];     // M:N через проміжну сутність
    public List<Screening> Screenings { get; private set; } = [];       // 1:N

    private Movie() { }   // для EF Core

    public Movie(string title, string? originalTitle, int releaseYear, int durationMinutes, string ageRating)
    {
        Rename(title, originalTitle);
        ChangeInfo(releaseYear, durationMinutes, ageRating);
    }

    public void Rename(string title, string? originalTitle)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Назва фільму обов'язкова");
        if (title.Trim().Length > TitleMaxLength) throw new DomainException($"Назва — не довше {TitleMaxLength} символів");
        Title = title.Trim();
        OriginalTitle = string.IsNullOrWhiteSpace(originalTitle) ? null : originalTitle.Trim();
    }

    public void ChangeInfo(int releaseYear, int durationMinutes, string ageRating)
    {
        if (releaseYear is < MinYear or > MaxYear) throw new DomainException($"Рік виходу — від {MinYear} до {MaxYear}");
        if (durationMinutes is < 1 or > MaxDuration) throw new DomainException($"Тривалість — від 1 до {MaxDuration} хв");
        if (!AgeRatings.Contains(ageRating)) throw new DomainException($"Віковий рейтинг має бути одним із: {string.Join(", ", AgeRatings)}");
        ReleaseYear = (short)releaseYear;
        DurationMinutes = (short)durationMinutes;
        AgeRating = ageRating;
    }

    public void SetDetails(string description, string? director, string? country, string? language,
                           string? posterUrl = null, string? trailerUrl = null)
    {
        if (Details is null)
            Details = new MovieDetails(description, director, country, language, posterUrl, trailerUrl);
        else
            Details.Update(description, director, country, language, posterUrl, trailerUrl);
    }

    /// <summary>Прив'язати жанр. Правила: без дублів; основний жанр — лише один.</summary>
    public MovieGenre AddGenre(Genre genre, bool isPrimary, DateTime addedAt)
    {
        if (MovieGenres.Any(mg => mg.GenreId == genre.Id && genre.Id != 0 || ReferenceEquals(mg.Genre, genre)))
            throw new DomainException($"Жанр «{genre.Name}» уже прив'язано до фільму «{Title}»");

        if (isPrimary)
            foreach (var other in MovieGenres) other.SetPrimary(false);

        var link = new MovieGenre(this, genre, isPrimary, addedAt);
        MovieGenres.Add(link);
        return link;
    }

    public void RemoveGenre(int genreId)
    {
        var link = MovieGenres.FirstOrDefault(mg => mg.GenreId == genreId)
                   ?? throw new DomainException($"Жанр {genreId} не прив'язано до фільму «{Title}»");
        MovieGenres.Remove(link);
    }

    /// <summary>Оновлення копії рейтингу з сервісу Відгуків (подія MovieRatingChanged).</summary>
    public void UpdateRating(decimal? average, int count)
    {
        if (count < 0) throw new DomainException("Кількість оцінок не може бути від'ємною");
        if (average is < 1 or > 5) throw new DomainException("Середня оцінка — від 1 до 5");
        AverageRating = count == 0 ? null : average;
        RatingsCount = count;
    }

    public void Delete()
    {
        if (IsDeleted) throw new DomainException($"Фільм «{Title}» уже видалено");
        IsDeleted = true;
    }
}
