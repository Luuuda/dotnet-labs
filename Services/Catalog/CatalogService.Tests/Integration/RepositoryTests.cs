using CatalogService.Bll.Dtos;
using CatalogService.Bll.Specifications;
using CatalogService.Dal.Repositories;
using CatalogService.Dal.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatalogService.Tests.Integration;

[Collection("database")]
public sealed class RepositoryTests
{
    [DbFact]
    public async Task Eager_loading_executes_one_sql_query()
    {
        await TestDatabase.EnsureReadyAsync();
        int id;
        await using (var lookup = TestDatabase.CreateContext())
            id = await lookup.Movies.Where(m => m.Title == "Дюна: Частина друга").Select(m => m.Id).FirstAsync();

        var counter = new CommandCounter();
        await using var db = TestDatabase.CreateContext(counter);
        var movie = await new MovieRepository(db).GetWithDetailsAsync(id, DateTime.MinValue);

        Assert.NotNull(movie!.Details);
        Assert.NotEmpty(movie.MovieGenres);
        Assert.NotEmpty(movie.Screenings);
        Assert.Single(counter.Commands);                    // деталі + жанри + сеанси — ОДИН запит з JOIN
    }

    [DbFact]
    public async Task Explicit_loading_executes_two_sql_queries()
    {
        await TestDatabase.EnsureReadyAsync();
        var counter = new CommandCounter();
        await using var db = TestDatabase.CreateContext(counter);

        var genre = await new GenreRepository(db).GetWithMoviesAsync(1);   // Фантастика

        Assert.NotEmpty(genre!.MovieGenres);
        Assert.Equal(2, counter.Commands.Count);            // 1) жанр  2) його фільми — окремим запитом
    }

    [DbFact]
    public async Task Many_to_many_query_filters_by_join_entity_field()
    {
        await TestDatabase.EnsureReadyAsync();
        await using var db = TestDatabase.CreateContext();

        var rows = await new MovieGenreRepository(db).GetRecentlyAddedAsync("sci-fi", new DateTime(2026, 9, 1));

        Assert.Contains(rows, r => r.Title == "Інтерстеллар" && r.IsPrimary);
        Assert.Equal(rows.OrderByDescending(r => r.AddedAt).Select(r => r.MovieId), rows.Select(r => r.MovieId));
    }

    [DbFact]
    public async Task Seeding_is_idempotent()
    {
        await TestDatabase.EnsureReadyAsync();
        await using var db = TestDatabase.CreateContext();
        var movies = await db.Movies.IgnoreQueryFilters().CountAsync();
        var screenings = await db.Screenings.CountAsync();

        await DbInitializer.SeedAsync(db, NullLogger.Instance);   // повторний запуск

        Assert.Equal(movies, await db.Movies.IgnoreQueryFilters().CountAsync());
        Assert.Equal(screenings, await db.Screenings.CountAsync());
        Assert.Equal(7, await db.Genres.CountAsync());             // HasData
    }

    [DbFact]
    public async Task Second_specification_works_through_the_same_generic_repository()
    {
        await TestDatabase.EnsureReadyAsync();
        await using var db = TestDatabase.CreateContext();
        var repository = new ScreeningRepository(db);
        var spec = new ScreeningsFilterSpec(new ScreeningQuery { Format = "IMAX", SortBy = "price", SortDir = "desc" }, 1, 50);

        var items = await repository.ListAsync(spec);
        var total = await repository.CountAsync(spec);

        Assert.NotEmpty(items);
        Assert.All(items, s => Assert.Equal("IMAX", s.Format));
        Assert.Equal(items.OrderByDescending(s => s.BasePrice).Select(s => s.Id), items.Select(s => s.Id));
        Assert.Equal(await db.Screenings.CountAsync(s => s.Format == "IMAX" && !s.IsCancelled), total);
    }
}
