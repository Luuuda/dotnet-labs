using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Reviews.Infrastructure.Persistence;
using Reviews.Infrastructure.Seeding;

namespace Reviews.Tests.Integration;

/// <summary>
/// Тести проти справжньої MongoDB. Рядок підключення — змінна REVIEWS_TEST_MONGO
/// (наприклад, mongodb://localhost:27017). Без неї тести пропускаються.
/// Кожен прогін працює з окремою базою cinema_reviews_tests, яка створюється з нуля.
/// </summary>
public sealed class MongoFixture : IAsyncLifetime
{
    public const string DatabaseName = "cinema_reviews_tests";
    public static string? ConnectionString => Environment.GetEnvironmentVariable("REVIEWS_TEST_MONGO");

    public MongoDbContext Context { get; private set; } = null!;

    public static MongoDbContext CreateContext(string database) =>
        new(Options.Create(new MongoDbOptions { ConnectionString = ConnectionString!, DatabaseName = database }));

    public async Task InitializeAsync()
    {
        if (string.IsNullOrWhiteSpace(ConnectionString)) return;
        Context = CreateContext(DatabaseName);
        await Context.Client.DropDatabaseAsync(DatabaseName);
        await new IndexCreationService(Context, NullLogger<IndexCreationService>.Instance).CreateAsync();
        await new ReviewSeeder(Context, NullLogger<ReviewSeeder>.Instance).SeedAsync();
        await new DiscussionSeeder(Context, NullLogger<DiscussionSeeder>.Instance).SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;
}

public sealed class MongoFactAttribute : FactAttribute
{
    public MongoFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(MongoFixture.ConnectionString))
            Skip = "Змінна REVIEWS_TEST_MONGO не задана — тест проти MongoDB пропущено";
    }
}

[CollectionDefinition("mongo")]
public sealed class MongoCollection : ICollectionFixture<MongoFixture>;
