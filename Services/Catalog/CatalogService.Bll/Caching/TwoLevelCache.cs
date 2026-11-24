using System.Diagnostics;
using System.Text;
using System.Text.Json;
using CatalogService.Bll.Services;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CatalogService.Bll.Caching;

/// <summary>
/// Ручна реалізація дворівневого кешу (порівняння з HybridCache — у README Лаб. 06).
/// <list type="bullet">
/// <item>L1 — <see cref="IMemoryCache"/> процесу: найшвидший, але свій у кожного екземпляра; коротший TTL.</item>
/// <item>L2 — <see cref="IDistributedCache"/> поверх Redis (Aspire AddRedisDistributedCache): спільний для реплік; DTO у JSON (System.Text.Json).</item>
/// <item>Збій Redis не ламає запит: помилка логується, рахується в cache.errors, і запит іде в БД. Після збою L2
/// пропускається на <see cref="L2RetryAfter"/>, щоб кожен запит не чекав тайм-ауту з'єднання.</item>
/// </list>
/// </summary>
public sealed class TwoLevelCache(
    IMemoryCache memory,
    IDistributedCache distributed,
    CacheMetrics metrics,
    ILogger<TwoLevelCache> logger,
    TimeProvider clock) : ITwoLevelCache
{
    public static readonly TimeSpan L2RetryAfter = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private long _l2DisabledUntilTicks;

    public async Task<T?> GetOrCreateAsync<T>(string key, CachePolicy policy, Func<CancellationToken, Task<T?>> factory,
        CancellationToken ct) where T : class
    {
        using var activity = CatalogTelemetry.Source.StartActivity("cache get " + policy.Name);
        activity?.SetTag("cache.key", key);
        var sw = Stopwatch.StartNew();

        // ---- L1: пам'ять процесу ----
        if (memory.TryGetValue(key, out T? fromMemory) && fromMemory is not null)
        {
            metrics.Hit("L1", policy.Name);
            metrics.Duration("L1", "get", sw.Elapsed.TotalMilliseconds);
            activity?.SetTag("cache.result", "hit-L1");
            logger.LogInformation("Cache hit {CacheLevel} {CacheKey} ({CachePolicy}, L1 TTL {L1TtlSeconds} с)",
                "L1", key, policy.Name, policy.L1Absolute.TotalSeconds);
            return fromMemory;
        }
        metrics.Miss("L1", policy.Name);

        // ---- L2: Redis ----
        var bytes = await TryL2Async("get", key, () => distributed.GetAsync(key, ct));
        if (bytes is not null && JsonSerializer.Deserialize<T>(bytes, Json) is { } fromRedis)
        {
            SetL1(key, fromRedis, policy, bytes.Length);                  // заповнюємо L1 на зворотному шляху
            metrics.Hit("L2", policy.Name);
            metrics.Duration("L2", "get", sw.Elapsed.TotalMilliseconds);
            activity?.SetTag("cache.result", "hit-L2");
            logger.LogInformation("Cache hit {CacheLevel} {CacheKey} ({CachePolicy}, {SizeBytes} байт) → записано в L1",
                "L2", key, policy.Name, bytes.Length);
            return fromRedis;
        }
        metrics.Miss("L2", policy.Name);
        activity?.SetTag("cache.result", "miss");
        logger.LogInformation("Cache miss {CacheKey} ({CachePolicy}) → читання з БД", key, policy.Name);

        // ---- БД ----
        var value = await factory(ct);
        if (value is null) return null;                                    // «не знайдено» не кешуємо

        var payload = JsonSerializer.SerializeToUtf8Bytes(value, Json);
        await TryL2Async<object?>("set", key, async () =>
        {
            await distributed.SetAsync(key, payload,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = policy.L2Absolute }, ct);
            return null;
        });
        SetL1(key, value, policy, payload.Length);
        metrics.Duration("db", "get", sw.Elapsed.TotalMilliseconds);
        logger.LogInformation("Cache set {CacheKey} ({CachePolicy}): {SizeBytes} байт, L1 {L1TtlSeconds} с, L2 {L2TtlSeconds} с",
            key, policy.Name, payload.Length, policy.L1Absolute.TotalSeconds, policy.L2Absolute.TotalSeconds);
        return value;
    }

    public async Task InvalidateAsync(string reason, CancellationToken ct, params string[] keys)
    {
        if (keys.Length == 0) return;
        foreach (var key in keys) memory.Remove(key);
        foreach (var key in keys)
            await TryL2Async<object?>("remove", key, async () => { await distributed.RemoveAsync(key, ct); return null; });

        metrics.Invalidated(keys.Length, reason);
        logger.LogInformation("Cache invalidated {CacheKeys}, reason {Reason}", keys, reason);
    }

    public async Task<string> GetVersionAsync(string versionKey, CancellationToken ct)
    {
        if (memory.TryGetValue(versionKey, out string? cached) && cached is not null) return cached;
        var bytes = await TryL2Async("get", versionKey, () => distributed.GetAsync(versionKey, ct));
        var version = bytes is null ? "0" : Encoding.UTF8.GetString(bytes);
        SetL1(versionKey, version, CachePolicy.Version, version.Length);
        return version;
    }

    public async Task BumpVersionAsync(string versionKey, string reason, CancellationToken ct)
    {
        var version = clock.GetUtcNow().UtcTicks.ToString();
        await TryL2Async<object?>("set", versionKey, async () =>
        {
            await distributed.SetAsync(versionKey, Encoding.UTF8.GetBytes(version),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CachePolicy.Version.L2Absolute }, ct);
            return null;
        });
        SetL1(versionKey, version, CachePolicy.Version, version.Length);
        metrics.Invalidated(1, reason);
        logger.LogInformation("Cache invalidated {CacheKeys}, reason {Reason}: нова версія списків {Version}",
            new[] { versionKey }, reason, version);
    }

    private void SetL1<T>(string key, T value, CachePolicy policy, int sizeBytes)
    {
        var options = new MemoryCacheEntryOptions
        {
            Size = Math.Max(1, sizeBytes / 1024),                             // одиниці SizeLimit — кілобайти
            AbsoluteExpirationRelativeToNow = policy.L1Absolute,
            SlidingExpiration = policy.L1Sliding,
            Priority = policy.Priority
        };
        options.RegisterPostEvictionCallback(OnEvicted);
        memory.Set(key, value, options);
    }

    private void OnEvicted(object key, object? value, EvictionReason reason, object? state)
    {
        if (reason is EvictionReason.Replaced or EvictionReason.Removed) return;   // заміна та інвалідація — не витіснення
        metrics.Evicted(reason.ToString());
        logger.LogDebug("Cache evicted {CacheKey} з L1: {EvictionReason}", key, reason);
    }

    /// <summary>Операція з Redis, збій якої не провалює запит.</summary>
    private async Task<TResult?> TryL2Async<TResult>(string operation, string key, Func<Task<TResult?>> action)
    {
        if (clock.GetUtcNow().UtcTicks < Interlocked.Read(ref _l2DisabledUntilTicks)) return default;
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            metrics.Duration("L2", operation, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Interlocked.Exchange(ref _l2DisabledUntilTicks, clock.GetUtcNow().Add(L2RetryAfter).UtcTicks);
            metrics.Error(operation);
            logger.LogWarning(ex, "Redis недоступний ({CacheOperation} {CacheKey}, {ElapsedMs} мс): працюю з L1 і БД, повтор через {RetryAfterSeconds} с",
                operation, key, sw.ElapsedMilliseconds, L2RetryAfter.TotalSeconds);
            return default;
        }
    }
}
