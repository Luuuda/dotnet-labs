using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

/// <summary>Сідер однієї колекції. Ідемпотентний: якщо колекція вже наповнена — нічого не робить.</summary>
public interface IDataSeeder
{
    string Collection { get; }
    Task<int> SeedAsync(CancellationToken ct = default);
}

internal static class SeedData
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Читає JSON, вбудований у збірку (Seeding/Data/*.json).</summary>
    public static List<T> Load<T>(string fileName)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resource = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + fileName, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        return JsonSerializer.Deserialize<List<T>>(stream, Json) ?? [];
    }
}

public sealed class ReviewSeeder(MongoDbContext context, ILogger<ReviewSeeder> logger) : IDataSeeder
{
    public string Collection => MongoDbContext.ReviewsCollection;

    private sealed record AuthorSeed(string UserId, string DisplayName);
    private sealed record CommentSeed(AuthorSeed Author, string Text);
    private sealed record ReviewSeed(int MovieId, string MovieTitle, AuthorSeed Author, int Rating, string? Text,
        Dictionary<string, int>? Aspects, bool VerifiedViewer, List<CommentSeed>? Comments);

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        if (await context.Reviews.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Сідінг {Collection}: колекція вже наповнена — пропущено", Collection);
            return 0;
        }

        // Документи створюються через доменні конструктори й методи — seed-дані проходять ті самі перевірки
        var reviews = SeedData.Load<ReviewSeed>("reviews.json").Select(s =>
        {
            var review = new Review(s.MovieId, s.MovieTitle, new Author(s.Author.UserId, s.Author.DisplayName),
                new Rating(s.Rating), s.Text, s.Aspects is { Count: > 0 } a ? new ReviewAspects(a) : null, s.VerifiedViewer);
            foreach (var c in s.Comments ?? [])
                review.AddComment(new Author(c.Author.UserId, c.Author.DisplayName), c.Text);
            return review;
        }).ToList();

        await context.Reviews.InsertManyAsync(reviews, cancellationToken: ct);   // одна bulk-операція
        logger.LogInformation("Сідінг {Collection}: додано {Count} документів", Collection, reviews.Count);
        return reviews.Count;
    }
}

public sealed class DiscussionSeeder(MongoDbContext context, ILogger<DiscussionSeeder> logger) : IDataSeeder
{
    public string Collection => MongoDbContext.DiscussionsCollection;

    private sealed record AuthorSeed(string UserId, string DisplayName);
    private sealed record DiscussionSeed(int MovieId, string MovieTitle, string Title, AuthorSeed CreatedBy,
        List<string>? Tags, string? ClosedReason);

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        if (await context.Discussions.EstimatedDocumentCountAsync(cancellationToken: ct) > 0)
        {
            logger.LogInformation("Сідінг {Collection}: колекція вже наповнена — пропущено", Collection);
            return 0;
        }

        var discussions = SeedData.Load<DiscussionSeed>("discussions.json").Select(s =>
        {
            var discussion = new Discussion(s.MovieId, s.MovieTitle, s.Title,
                new Author(s.CreatedBy.UserId, s.CreatedBy.DisplayName), s.Tags);
            if (s.ClosedReason is not null) discussion.Close(s.ClosedReason);
            return discussion;
        }).ToList();

        await context.Discussions.InsertManyAsync(discussions, cancellationToken: ct);
        logger.LogInformation("Сідінг {Collection}: додано {Count} документів", Collection, discussions.Count);
        return discussions.Count;
    }
}

/// <summary>
/// Запускає всі IDataSeeder під час старту (після IndexCreationService). Вимикається налаштуванням
/// Seeding:Enabled = false (Production).
/// </summary>
public sealed class DataSeedingService(IServiceProvider services, IConfiguration configuration,
    ILogger<DataSeedingService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        if (!configuration.GetValue("Seeding:Enabled", true))
        {
            logger.LogInformation("Сідінг вимкнено (Seeding:Enabled = false)");
            return;
        }

        using var scope = services.CreateScope();
        foreach (var seeder in scope.ServiceProvider.GetServices<IDataSeeder>())
        {
            try
            {
                await seeder.SeedAsync(ct);
            }
            catch (Exception ex) when (ex is MongoException or TimeoutException)
            {
                logger.LogError(ex, "Сідінг {Collection} не виконано", seeder.Collection);
            }
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
