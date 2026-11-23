using CatalogService.Dal;
using CatalogService.Dal.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;

namespace CatalogService.Tests.Integration;

/// <summary>
/// Тести проти справжнього SQL Server. Рядок підключення — змінна CATALOG_TEST_DB
/// (окрема тестова база; створюється міграціями). Без неї тести пропускаються.
/// </summary>
public static class TestDatabase
{
    private static readonly SemaphoreSlim Lock = new(1, 1);
    private static bool _ready;

    public static string? ConnectionString => Environment.GetEnvironmentVariable("CATALOG_TEST_DB");

    public static CatalogDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<CatalogDbContext>()
            .UseSqlServer(ConnectionString)
            .AddInterceptors(interceptors)
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning))
            .Options);

    /// <summary>Міграції + сідінг один раз на тестовий прогін.</summary>
    public static async Task EnsureReadyAsync()
    {
        if (_ready) return;
        await Lock.WaitAsync();
        try
        {
            if (_ready) return;
            await using var db = CreateContext();
            await db.Database.MigrateAsync();
            await DbInitializer.SeedAsync(db, NullLogger.Instance);
            _ready = true;
        }
        finally { Lock.Release(); }
    }
}

public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
            Skip = "Змінна CATALOG_TEST_DB не задана — тест проти БД пропущено";
    }
}

/// <summary>Усі тести з БД — в одній колекції (послідовно), щоб лічильники записів не «плавали».</summary>
[CollectionDefinition("database")]
public sealed class DatabaseCollection;

/// <summary>Рахує SQL-команди, які EF Core відправив у базу.</summary>
public sealed class CommandCounter : DbCommandInterceptor
{
    public List<string> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
    {
        Commands.Add(command.CommandText);
        return base.ReaderExecutingAsync(command, eventData, result, ct);
    }
}
