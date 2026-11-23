using CatalogService.Bll.Dtos;
using CatalogService.Bll.Validators;

namespace CatalogService.Tests.Unit;

public sealed class ValidatorsTests
{
    private readonly CreateMovieDtoValidator _movie = new(TimeProvider.System);

    [Fact]
    public void Valid_movie_passes()
    {
        var result = _movie.Validate(new CreateMovieDto
        {
            Title = "Тіні забутих предків", ReleaseYear = 1965, DurationMinutes = 97, AgeRating = "12+",
            Genres = ["drama", "historical"]
        });
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Invalid_movie_reports_errors_per_field()
    {
        var result = _movie.Validate(new CreateMovieDto
        {
            Title = " ", ReleaseYear = 1700, DurationMinutes = 0, AgeRating = "21+", Genres = ["Sci Fi", "Sci Fi"]
        });

        var fields = result.Errors.Select(e => e.PropertyName).ToHashSet();
        Assert.Contains("Title", fields);
        Assert.Contains("ReleaseYear", fields);
        Assert.Contains("DurationMinutes", fields);
        Assert.Contains("AgeRating", fields);
        Assert.Contains("Genres", fields);
    }

    [Fact]
    public void Unknown_sort_field_is_rejected()
    {
        var result = new MovieQueryValidator().Validate(new MovieQuery { SortBy = "price; DROP TABLE Movies" });
        Assert.False(result.IsValid);
    }

    [Fact]
    public void Screening_in_the_past_is_rejected()
    {
        var result = new CreateScreeningDtoValidator(TimeProvider.System).Validate(new CreateScreeningDto
        {
            MovieId = 1, HallId = 1, StartsAt = new DateTime(2020, 1, 1, 18, 0, 0), Format = "2D", BasePrice = 100
        });
        Assert.Contains(result.Errors, e => e.PropertyName == "StartsAt");
    }
}
