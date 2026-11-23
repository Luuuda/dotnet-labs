using CatalogService.Bll.Dtos;
using CatalogService.Bll.Specifications;
using CatalogService.Domain.Entities;

namespace CatalogService.Tests.Unit;

/// <summary>Специфікація не залежить від EF — її критерії, сортування й пагінацію можна перевірити in-memory.</summary>
public sealed class SpecificationTests
{
    private static List<Movie> Movies()
    {
        var scifi = new Genre("Фантастика", "sci-fi");
        var drama = new Genre("Драма", "drama");
        var dune = new Movie("Дюна: Частина друга", "Dune: Part Two", 2024, 166, "12+");
        dune.AddGenre(scifi, true, DateTime.UtcNow);
        var interstellar = new Movie("Інтерстеллар", "Interstellar", 2014, 169, "12+");
        interstellar.AddGenre(scifi, true, DateTime.UtcNow);
        var dovbush = new Movie("Довбуш", null, 2023, 115, "12+");
        dovbush.AddGenre(drama, true, DateTime.UtcNow);
        var mavka = new Movie("Мавка. Лісова пісня", null, 2023, 99, "0+");
        return [dune, interstellar, dovbush, mavka];
    }

    [Fact]
    public void Filters_by_genre_and_sorts_by_year_desc()
    {
        var spec = new MoviesFilterSpec(new MovieQuery { Genre = "sci-fi", SortBy = "year", SortDir = "desc" }, 1, 20);
        var result = spec.Evaluate(Movies()).ToList();
        Assert.Equal(new[] { "Дюна: Частина друга", "Інтерстеллар" }, result.Select(m => m.Title));
    }

    [Fact]
    public void Search_matches_original_title_and_paging_skips()
    {
        Assert.Single(new MoviesFilterSpec(new MovieQuery { Search = "Interstellar" }, 1, 20).Evaluate(Movies()));

        var page2 = new MoviesFilterSpec(new MovieQuery { SortBy = "duration" }, page: 2, pageSize: 2).Evaluate(Movies()).ToList();
        Assert.Equal(new[] { "Дюна: Частина друга", "Інтерстеллар" }, page2.Select(m => m.Title));   // 99, 115 | 166, 169
    }

    [Fact]
    public void Age_and_duration_filters_combine()
    {
        var result = new MoviesFilterSpec(new MovieQuery { AgeRating = "12+", MaxDuration = 120 }, 1, 20).Evaluate(Movies());
        Assert.Equal("Довбуш", Assert.Single(result).Title);
    }
}
