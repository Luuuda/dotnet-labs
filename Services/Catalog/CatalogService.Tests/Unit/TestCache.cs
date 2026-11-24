using System.Diagnostics.Metrics;
using CatalogService.Bll.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CatalogService.Tests.Unit;

/// <summary>Справжній дворівневий кеш для unit-тестів: L1 — MemoryCache, L2 — MemoryDistributedCache замість Redis.</summary>
public static class TestCache
{
    public static TwoLevelCache Create(IMemoryCache? memory = null, IDistributedCache? distributed = null) =>
        new(memory ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 }),
            distributed ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            new CacheMetrics(new TestMeterFactory()),
            NullLogger<TwoLevelCache>.Instance,
            TimeProvider.System);

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
