using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;

namespace CatalogService.Tests.Unit;

/// <summary>Бізнес-правила, які перевіряє САМА сутність (без сервісу і без бази).</summary>
public sealed class EntityRulesTests
{
    private static readonly Hall Imax = new("IMAX", "IMAX", 14, 20);
    private static readonly Hall Standard = new("Зала 1", "Standard", 10, 12);

    [Fact]
    public void Movie_rejects_invalid_year_duration_and_age_rating()
    {
        Assert.Throws<DomainException>(() => new Movie("Тест", null, 1800, 100, "12+"));
        Assert.Throws<DomainException>(() => new Movie("Тест", null, 2024, 0, "12+"));
        Assert.Throws<DomainException>(() => new Movie("Тест", null, 2024, 100, "21+"));
        Assert.Throws<DomainException>(() => new Movie("   ", null, 2024, 100, "12+"));
    }

    [Fact]
    public void Movie_allows_only_one_primary_genre_and_no_duplicates()
    {
        var movie = new Movie("Дюна", null, 2024, 166, "12+");
        var scifi = new Genre("Фантастика", "sci-fi");
        var drama = new Genre("Драма", "drama");

        movie.AddGenre(scifi, isPrimary: true, DateTime.UtcNow);
        movie.AddGenre(drama, isPrimary: true, DateTime.UtcNow);   // новий основний «знімає» прапорець зі старого

        Assert.Single(movie.MovieGenres, mg => mg.IsPrimary);
        Assert.True(movie.MovieGenres.Single(mg => mg.Genre == drama).IsPrimary);
        Assert.Throws<DomainException>(() => movie.AddGenre(scifi, false, DateTime.UtcNow));
    }

    [Fact]
    public void Screening_price_must_be_positive_and_end_is_computed()
    {
        var movie = new Movie("Дюна", null, 2024, 166, "12+");
        Assert.Throws<DomainException>(() => new Screening(movie, Standard, DateTime.UtcNow.AddDays(1), "2D", "uk", 0));

        var screening = new Screening(movie, Standard, new DateTime(2026, 12, 10, 18, 0, 0), "2D", "uk", 150);
        Assert.Equal(new DateTime(2026, 12, 10, 21, 1, 0), screening.EndsAt);   // 166 хв + 15 хв прибирання
    }

    [Fact]
    public void Imax_format_only_in_imax_hall()
    {
        var movie = new Movie("Дюна", null, 2024, 166, "12+");
        Assert.Throws<DomainException>(() => new Screening(movie, Standard, DateTime.UtcNow.AddDays(1), "IMAX", "uk", 200));
        Assert.Equal("IMAX", new Screening(movie, Imax, DateTime.UtcNow.AddDays(1), "IMAX", "uk", 200).Format);
    }

    [Fact]
    public void Cancelled_screening_cannot_change_price_or_be_cancelled_again()
    {
        var screening = new Screening(new Movie("Дюна", null, 2024, 166, "12+"), Standard,
                                      DateTime.UtcNow.AddDays(1), "2D", "uk", 150);
        screening.ChangePrice(180);
        Assert.Equal(180, screening.BasePrice);

        screening.Cancel();
        Assert.Throws<DomainException>(() => screening.ChangePrice(200));
        Assert.Throws<DomainException>(() => screening.Cancel());
    }
}
