using CatalogService.Dal;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CatalogService.Api.HealthChecks;

/// <summary>
/// Лаб. 07: власна перевірка готовності бази каталогу — усі міграції EF Core застосовано. Доступність SQL Server
/// (SELECT 1) перевіряє AddSqlServer; ця перевірка ловить ситуацію «база доступна, але схема застаріла» (новий
/// реліз без MigrateOnStartup). Легкий запит: читання таблиці __EFMigrationsHistory. Теги: ready і startup.
/// </summary>
public sealed class CatalogMigrationsHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CatalogDbContext>();
            // окремий DbContext перевірки: Connect Timeout 3 с — недоступний SQL Server → Unhealthy за секунди, а не за 15 с
            db.Database.SetConnectionString(new SqlConnectionStringBuilder(db.Database.GetConnectionString()) { ConnectTimeout = 3 }.ConnectionString);
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy("Усі міграції EF Core застосовано")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    $"Не застосовано міграцій: {pending.Count}",
                    data: new Dictionary<string, object> { ["pending"] = string.Join(", ", pending) });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Не вдалося перевірити міграції: SQL Server недоступний", ex);
        }
    }
}
