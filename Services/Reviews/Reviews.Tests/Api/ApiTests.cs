using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using MongoDB.Bson;
using Reviews.Tests.Integration;

namespace Reviews.Tests.Api;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string DatabaseName = "cinema_reviews_api_tests";

    public ApiFactory()
    {
        if (string.IsNullOrWhiteSpace(MongoFixture.ConnectionString)) return;
        MongoFixture.CreateContext(DatabaseName).Client.DropDatabase(DatabaseName);
        Environment.SetEnvironmentVariable("MongoDb__ConnectionString", MongoFixture.ConnectionString);
        Environment.SetEnvironmentVariable("MongoDb__DatabaseName", DatabaseName);
        Environment.SetEnvironmentVariable("Seeding__Enabled", "true");
    }
}

/// <summary>Перевірки з розділу «Перед здачею перевірте себе» — через HTTP.</summary>
[Collection("mongo")]
public sealed class ApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private static object NewReview(int rating = 5, string? userId = null) => new
    {
        movieId = 3, movieTitle = "Дюна: Частина друга",
        userId = userId ?? $"api-{Guid.NewGuid():N}", displayName = "Тестовий глядач",
        rating, text = "Неймовірний звук і масштаб", aspects = new { sound = 5 }
    };

    [MongoFact]
    public async Task Post_returns_201_with_string_objectid_in_location_and_etag()
    {
        var response = await _client.PostAsJsonAsync("/api/reviews", NewReview());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = response.Headers.Location!.ToString().Split('/').Last();
        Assert.True(ObjectId.TryParse(id, out _));
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(id, body.GetProperty("id").GetString());
        Assert.Equal("\"0\"", response.Headers.ETag!.Tag);

        var get = await _client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
    }

    [MongoFact]
    public async Task Rating_10_returns_400_with_errors_before_handler()
    {
        var response = await _client.PostAsJsonAsync("/api/reviews", NewReview(rating: 10));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(problem.GetProperty("errors").TryGetProperty("Rating", out _));
        Assert.True(problem.TryGetProperty("traceId", out _));
    }

    [MongoFact]
    public async Task Second_review_of_same_user_is_rejected_not_500()
    {
        var userId = $"dup-{Guid.NewGuid():N}";
        Assert.Equal(HttpStatusCode.Created, (await _client.PostAsJsonAsync("/api/reviews", NewReview(userId: userId))).StatusCode);

        var duplicate = await _client.PostAsJsonAsync("/api/reviews", NewReview(rating: 3, userId: userId));

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);            // асинхронне правило валідатора
        var problem = await duplicate.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("уже залишив відгук", problem.GetProperty("errors").GetProperty("UserId")[0].GetString());
    }

    [MongoFact]
    public async Task Update_with_stale_if_match_returns_409_and_fresh_one_200()
    {
        var created = await _client.PostAsJsonAsync("/api/reviews", NewReview());
        var location = created.Headers.Location!;

        var ok = new HttpRequestMessage(HttpMethod.Put, location) { Content = JsonContent.Create(new { rating = 4, text = "Оновлено" }) };
        ok.Headers.IfMatch.ParseAdd("\"0\"");
        var updated = await _client.SendAsync(ok);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("\"1\"", updated.Headers.ETag!.Tag);

        var stale = new HttpRequestMessage(HttpMethod.Put, location) { Content = JsonContent.Create(new { rating = 2, text = "Застаріла версія" }) };
        stale.Headers.IfMatch.ParseAdd("\"0\"");
        var conflict = await _client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("application/problem+json", conflict.Content.Headers.ContentType?.MediaType);

        var missingVersion = await _client.PutAsJsonAsync(location, new { rating = 2, text = "Без версії" });
        Assert.Equal(HttpStatusCode.BadRequest, missingVersion.StatusCode);
    }

    [MongoFact]
    public async Task Unknown_id_is_404_and_invalid_id_is_400()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync($"/api/reviews/{ObjectId.GenerateNewId()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/reviews/not-an-objectid")).StatusCode);
    }

    [MongoFact]
    public async Task Comments_are_added_and_removed()
    {
        var created = await _client.PostAsJsonAsync("/api/reviews", NewReview());
        var location = created.Headers.Location!.ToString();

        var comment = await _client.PostAsJsonAsync($"{location}/comments", new { userId = "u-9", displayName = "Ігор", text = "Згоден!" });
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);
        var commentId = (await comment.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();

        var review = await _client.GetFromJsonAsync<JsonElement>(location);
        Assert.Equal(1, review.GetProperty("commentsCount").GetInt32());

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"{location}/comments/{commentId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"{location}/comments/{commentId}")).StatusCode);
    }

    [MongoFact]
    public async Task List_returns_pagination_metadata()
    {
        var page = await _client.GetFromJsonAsync<JsonElement>("/api/reviews?movieId=3&page=1&pageSize=2&sortBy=rating");
        Assert.True(page.GetProperty("totalCount").GetInt64() >= 3);
        Assert.Equal(2, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("hasNext").GetBoolean());

        Assert.Equal(HttpStatusCode.BadRequest, (await _client.GetAsync("/api/reviews?sortBy=password")).StatusCode);
    }

    [MongoFact]
    public async Task Closing_discussion_twice_is_400_domain_rule()
    {
        var created = await _client.PostAsJsonAsync("/api/discussions", new
        {
            movieId = 7, movieTitle = "Інтерстеллар", title = "Що за кадром фіналу?", userId = "u-1", displayName = "Олена", tags = new[] { "фінал" }
        });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!.ToString();

        Assert.Equal(HttpStatusCode.OK, (await _client.PostAsJsonAsync($"{location}/close", new { reason = "Вичерпано" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _client.PostAsJsonAsync($"{location}/close", new { reason = "Ще раз" })).StatusCode);
    }

    [MongoFact]
    public async Task Movie_rating_and_health_and_swagger_work()
    {
        var rating = await _client.GetFromJsonAsync<JsonElement>("/api/movies/1/rating");
        Assert.Equal(1, rating.GetProperty("movieId").GetInt32());

        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }
}
