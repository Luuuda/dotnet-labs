using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Reviews.Application.Caching;

/// <summary>
/// Статистика L1 у health check (/health): кількість записів, зайнятий розмір, попадання / промахи, hit ratio.
/// Потребує MemoryCacheOptions.TrackStatistics = true (див. Program.cs).
/// </summary>
public sealed class CacheStatisticsHealthCheck(IMemoryCache memory) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (memory is not MemoryCache cache || cache.GetCurrentStatistics() is not { } stats)
            return Task.FromResult(HealthCheckResult.Healthy("Статистика кешу вимкнена"));

        var lookups = stats.TotalHits + stats.TotalMisses;
        var data = new Dictionary<string, object>
        {
            ["entries"] = stats.CurrentEntryCount,
            ["sizeUnits"] = stats.CurrentEstimatedSize ?? 0,
            ["hits"] = stats.TotalHits,
            ["misses"] = stats.TotalMisses,
            ["hitRatio"] = lookups == 0 ? 0 : Math.Round((double)stats.TotalHits / lookups, 3)
        };
        return Task.FromResult(HealthCheckResult.Healthy("L1 (IMemoryCache) сервісу відгуків", data));
    }
}
