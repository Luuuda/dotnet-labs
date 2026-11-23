using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Infrastructure.Persistence;

namespace Reviews.Infrastructure.Health;

/// <summary>Health check: команда ping до бази (GET /health).</summary>
public sealed class MongoHealthCheck(MongoDbContext context) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext healthContext, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await context.Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: timeout.Token);
            return HealthCheckResult.Healthy($"MongoDB '{context.Database.DatabaseNamespace.DatabaseName}' доступна");
        }
        catch (Exception ex) when (ex is MongoException or TimeoutException or OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("MongoDB недоступна", ex);
        }
    }
}
