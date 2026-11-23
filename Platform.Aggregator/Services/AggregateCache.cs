using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace Platform.Aggregator.Services;

/// <summary>
/// Лаб. 06: кеш агрегованих відповідей у Redis (IDistributedCache, AddRedisDistributedCache("redis")).
/// <list type="bullet">
/// <item>ключ — з параметрів запиту: aggregator:booking:{id}:full, aggregator:movie:{id}:full;</item>
/// <item>кеш перевіряється ДО паралельних gRPC-викликів;</item>
/// <item>TTL 30–45 с — коротший за кеші сервісів (там 2–10 хв), щоб збірка не виглядала «свіжішою» за свої джерела;</item>
/// <item>часткові відповіді (є Warnings) НЕ кешуються — інакше збій одного сервісу «застиг» би на весь TTL;</item>
/// <item>збій Redis не ламає запит: агрегатор просто йде до сервісів.</item>
/// </list>
/// </summary>
public sealed class AggregateCache
{
    public const string MeterName = "Cinema.Aggregator.Cache";
    public static readonly TimeSpan BookingTtl = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan MovieTtl = TimeSpan.FromSeconds(45);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IDistributedCache _cache;
    private readonly ILogger<AggregateCache> _logger;
    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private long _disabledUntilTicks;                                  // після збою Redis — 30 с без кешу, без очікування тайм-аутів

    public AggregateCache(IDistributedCache cache, IMeterFactory meters, ILogger<AggregateCache> logger)
    {
        _cache = cache;
        _logger = logger;
        var meter = meters.Create(MeterName);
        _hits = meter.CreateCounter<long>("cache.hits", "{hit}", "Попадання в кеш агрегованих відповідей");
        _misses = meter.CreateCounter<long>("cache.misses", "{miss}", "Промахи кешу агрегованих відповідей");
    }

    public static string BookingKey(long id) => $"aggregator:booking:{id}:full";
    public static string MovieKey(int id) => $"aggregator:movie:{id}:full";
    public static string BoardKey(IEnumerable<int> ids) => $"aggregator:board:{string.Join('-', ids.Order())}";

    /// <summary>Cache-aside для агрегованої відповіді. <paramref name="bypass"/> — Cache-Control: no-cache або HTTP-транспорт.</summary>
    public async Task<(T? Value, bool FromCache)> GetOrBuildAsync<T>(string key, TimeSpan ttl, bool bypass,
        Func<Task<T?>> build, Func<T, bool> isComplete, CancellationToken ct) where T : class
    {
        if (!bypass && await TryGetAsync<T>(key, ct) is { } cached)
        {
            _hits.Add(1, new KeyValuePair<string, object?>("cache.level", "L2"));
            Activity.Current?.SetTag("cache.result", "hit");
            _logger.LogInformation("Aggregate cache hit {CacheKey}", key);
            return (cached, true);
        }

        _misses.Add(1, new KeyValuePair<string, object?>("cache.level", "L2"));
        Activity.Current?.SetTag("cache.result", bypass ? "bypass" : "miss");
        var value = await build();                                   // паралельні gRPC-виклики
        if (value is null) return (null, false);

        if (bypass) return (value, false);
        if (!isComplete(value))
        {
            _logger.LogInformation("Aggregate cache skip {CacheKey}: часткова відповідь не кешується", key);
            return (value, false);
        }
        await TrySetAsync(key, value, ttl, ct);
        _logger.LogInformation("Aggregate cache set {CacheKey} на {TtlSeconds} с", key, ttl.TotalSeconds);
        return (value, false);
    }

    private bool RedisSkipped => DateTime.UtcNow.Ticks < Interlocked.Read(ref _disabledUntilTicks);
    private void RedisFailed() => Interlocked.Exchange(ref _disabledUntilTicks, DateTime.UtcNow.AddSeconds(30).Ticks);

    private async Task<T?> TryGetAsync<T>(string key, CancellationToken ct) where T : class
    {
        if (RedisSkipped) return null;
        try
        {
            var bytes = await _cache.GetAsync(key, ct);
            return bytes is null ? null : JsonSerializer.Deserialize<T>(bytes, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RedisFailed();
            _logger.LogWarning(ex, "Redis недоступний — агрегую без кешу ({CacheKey})", key);
            return null;
        }
    }

    private async Task TrySetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct)
    {
        if (RedisSkipped) return;
        try
        {
            await _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, Json),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            RedisFailed();
            _logger.LogWarning(ex, "Redis недоступний — відповідь не закешовано ({CacheKey})", key);
        }
    }
}
