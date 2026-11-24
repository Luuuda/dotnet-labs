using System.Diagnostics.Metrics;

namespace CatalogService.Bll.Caching;

/// <summary>
/// Метрики кешу каталогу (Meter «Cinema.Catalog.Cache», зареєстрований у Platform.ServiceDefaults).
/// Частку попадань (hit ratio) Dashboard рахує як cache.hits / (cache.hits + cache.misses) окремо для L1 і L2.
/// </summary>
public sealed class CacheMetrics
{
    public const string MeterName = "Cinema.Catalog.Cache";

    private readonly Counter<long> _hits;
    private readonly Counter<long> _misses;
    private readonly Counter<long> _invalidations;
    private readonly Counter<long> _evictions;
    private readonly Counter<long> _errors;
    private readonly Histogram<double> _duration;

    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _hits = meter.CreateCounter<long>("cache.hits", "{hit}", "Попадання в кеш (тег cache.level = L1 | L2)");
        _misses = meter.CreateCounter<long>("cache.misses", "{miss}", "Промахи кешу (тег cache.level = L1 | L2)");
        _invalidations = meter.CreateCounter<long>("cache.invalidations", "{key}", "Ключі, видалені при записі");
        _evictions = meter.CreateCounter<long>("cache.evictions", "{entry}", "Витіснення з пам'яті (TTL, SizeLimit)");
        _errors = meter.CreateCounter<long>("cache.errors", "{error}", "Збої Redis, через які запит пішов у БД");
        _duration = meter.CreateHistogram<double>("cache.operation.duration", "ms", "Тривалість операцій кешу");
    }

    public void Hit(string level, string policy) => _hits.Add(1, Tags(level, policy));
    public void Miss(string level, string policy) => _misses.Add(1, Tags(level, policy));
    public void Invalidated(int keys, string reason) => _invalidations.Add(keys, new KeyValuePair<string, object?>("cache.reason", reason));
    public void Evicted(string reason) => _evictions.Add(1, new KeyValuePair<string, object?>("cache.eviction_reason", reason));
    public void Error(string operation) => _errors.Add(1, new KeyValuePair<string, object?>("cache.operation", operation));

    public void Duration(string level, string operation, double ms) => _duration.Record(ms,
        new KeyValuePair<string, object?>("cache.level", level), new KeyValuePair<string, object?>("cache.operation", operation));

    private static KeyValuePair<string, object?>[] Tags(string level, string policy) =>
        [new("cache.level", level), new("cache.policy", policy)];
}
