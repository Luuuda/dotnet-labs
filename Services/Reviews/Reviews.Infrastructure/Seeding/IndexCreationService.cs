using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Seeding;

/// <summary>
/// Створює індекси під реальні запити під час старту (Indexes.CreateManyAsync). Повторне створення безпечне:
/// MongoDB не дублює наявний індекс. Імена й параметри збігаються з 01_schema.js з Лаб. 01, тож сервіс
/// можна запускати і на базі, розгорнутій скриптами.
/// </summary>
public sealed class IndexCreationService(MongoDbContext context, ILogger<IndexCreationService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        try
        {
            await CreateAsync(ct);
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException)
        {
            // База недоступна — API все одно стартує; /health покаже проблему, запити отримають 503
            logger.LogError(ex, "Не вдалося створити індекси MongoDB");
        }
    }

    public async Task CreateAsync(CancellationToken ct = default)
    {
        var review = Builders<Review>.IndexKeys;
        var reviewIndexes = new[]
        {
            // «відгуки фільму, нові зверху» + курсорна пагінація
            new CreateIndexModel<Review>(review.Ascending(r => r.MovieId).Descending(r => r.CreatedAt),
                new CreateIndexOptions { Name = "ix_movie_createdAt" }),
            // «один користувач — один відгук на фільм» + «мої відгуки»
            new CreateIndexModel<Review>(review.Ascending("author.userId").Ascending(r => r.MovieId),
                new CreateIndexOptions { Name = "ux_author_movie", Unique = true }),
            // повнотекстовий пошук (без стемінгу — тексти українською)
            new CreateIndexModel<Review>(review.Text(r => r.Text),
                new CreateIndexOptions { Name = "tx_text", DefaultLanguage = "none" }),
            // «найкращі/найгірші відгуки фільму»
            new CreateIndexModel<Review>(review.Ascending(r => r.MovieId).Descending("rating"),
                new CreateIndexOptions { Name = "ix_movie_rating" })
        };
        var created = await context.Reviews.Indexes.CreateManyAsync(reviewIndexes, ct);

        var discussion = Builders<Discussion>.IndexKeys;
        var discussionIndexes = new[]
        {
            // «теми фільму, найактивніші зверху»
            new CreateIndexModel<Discussion>(discussion.Ascending(d => d.MovieId).Descending(d => d.LastActivityAt),
                new CreateIndexOptions { Name = "ix_movie_activity" }),
            // фільтр за тегом (multikey-індекс по масиву)
            new CreateIndexModel<Discussion>(discussion.Ascending("tags"),
                new CreateIndexOptions { Name = "ix_tags" })
        };
        created = created.Concat(await context.Discussions.Indexes.CreateManyAsync(discussionIndexes, ct)).ToList();

        logger.LogInformation("Індекси MongoDB перевірено/створено: {Indexes}", created);
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
}
