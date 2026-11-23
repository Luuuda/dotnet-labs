using CatalogService.Dal.Abstractions;
using CatalogService.Dal.Infrastructure;
using CatalogService.Dal.Seeding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CatalogService.Dal;

public static class DependencyInjection
{
    public static IServiceCollection AddDataAccessLayer(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<AuditSaveChangesInterceptor>();
        services.AddDbContext<CatalogDbContext>((sp, options) => options
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(CatalogDbContext).Assembly.FullName))
            .AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>())
            // soft delete (query filter на Movie) + обов'язкові навігації — свідомий вибір, попередження не потрібне
            .ConfigureWarnings(w => w.Ignore(CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning)));

        // Scoped: один DbContext і один Unit of Work на HTTP-запит
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        return services;
    }

    /// <summary>Застосувати міграції й (за потреби) програмний сідінг. Викликається на старті API.</summary>
    public static async Task MigrateAndSeedAsync(this IServiceProvider services, bool seed, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CatalogService.Dal.Seeding");

        await db.Database.MigrateAsync(ct);
        logger.LogInformation("Міграції застосовано");
        if (seed) await DbInitializer.SeedAsync(db, logger, ct);
    }
}
