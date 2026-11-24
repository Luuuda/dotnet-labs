using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CatalogService.Bll.Dtos;
using CatalogService.Tests.Integration;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public ApiFactory()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString)) return;
        Environment.SetEnvironmentVariable("ConnectionStrings__CatalogDb", TestDatabase.ConnectionString);
        Environment.SetEnvironmentVariable("Database__SeedOnStartup", "true");
        Environment.SetEnvironmentVariable("Cache__WarmUp", "false");   // Лаб. 06: тести самі керують станом кешу
    }
}

/// <summary>Перевірки з розділу «Перед здачею перевірте себе» — через HTTP.</summary>
[Collection("database")]
public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [DbFact]
    public async Task Page_2_returns_total_count_of_all_rows_not_of_the_page()
    {
        await TestDatabase.EnsureReadyAsync();
        await using var db = TestDatabase.CreateContext();
        var all = await db.Movies.CountAsync();

        var response = await _client.GetAsync("/api/movies?page=2&pageSize=5");
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(all, page.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page.GetProperty("page").GetInt32());
        Assert.True(page.GetProperty("items").GetArrayLength() <= 5);
        Assert.Equal(all.ToString(), response.Headers.GetValues("X-Total-Count").Single());
    }

    [DbFact]
    public async Task Post_with_empty_title_returns_400_with_errors_per_field()
    {
        var response = await _client.PostAsJsonAsync("/api/movies",
            new { title = "", releaseYear = 1700, durationMinutes = 0, ageRating = "21+" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name.ToLowerInvariant()).ToList();
        Assert.Contains("title", fields);
    }

    [DbFact]
    public async Task Fluent_validation_errors_are_returned_as_validation_problem_details()
    {
        var response = await _client.PostAsJsonAsync("/api/movies",
            new { title = "Валідний", releaseYear = 1700, durationMinutes = 0, ageRating = "21+" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        var fields = problem.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToList();
        Assert.Contains("ReleaseYear", fields);
        Assert.Contains("DurationMinutes", fields);
        Assert.Contains("AgeRating", fields);
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [DbFact]
    public async Task Create_returns_201_with_location_and_duplicate_returns_409()
    {
        var movie = new { title = $"Тестовий фільм {Guid.NewGuid():N}", releaseYear = 2025, durationMinutes = 100,
                          ageRating = "12+", genres = new[] { "drama" } };

        var created = await _client.PostAsJsonAsync("/api/movies", movie);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync(created.Headers.Location)).StatusCode);

        var duplicate = await _client.PostAsJsonAsync("/api/movies", movie);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("application/problem+json", duplicate.Content.Headers.ContentType?.MediaType);

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync(created.Headers.Location)).StatusCode);
    }

    [DbFact]
    public async Task Unknown_id_returns_404_problem_details()
    {
        var response = await _client.GetAsync("/api/movies/987654");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [DbFact]
    public async Task Filtering_and_sorting_are_applied()
    {
        var page = await _client.GetFromJsonAsync<JsonElement>("/api/movies?genre=sci-fi&sortBy=year&sortDir=desc");
        var years = page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("releaseYear").GetInt32()).ToList();

        Assert.NotEmpty(years);
        Assert.Equal(years.OrderByDescending(y => y), years);
    }

    [DbFact]
    public async Task Invalid_sort_field_returns_400()
        => Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/movies?sortBy=password")).StatusCode);

    [DbFact]
    public async Task Cancelling_screening_twice_returns_409()
    {
        var movies = await _client.GetFromJsonAsync<PagedResult<MovieListItemDto>>("/api/movies?search=Інтерстеллар");
        var start = DateTime.UtcNow.Date.AddDays(30 + Random.Shared.Next(300)).AddHours(9);
        var created = await _client.PostAsJsonAsync("/api/screenings", new
        {
            movieId = movies!.Items[0].Id, hallId = 1, startsAt = start, format = "2D", language = "українська", basePrice = 150
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var screening = await created.Content.ReadFromJsonAsync<ScreeningDto>();

        Assert.Equal(HttpStatusCode.NoContent, (await _client.PostAsync($"/api/screenings/{screening!.Id}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await _client.PostAsync($"/api/screenings/{screening.Id}/cancel", null)).StatusCode);
    }

    [DbFact]
    public async Task Swagger_is_available()
        => Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
}
