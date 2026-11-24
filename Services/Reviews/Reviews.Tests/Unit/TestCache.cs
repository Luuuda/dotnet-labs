using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Reviews.Application.Caching;

namespace Reviews.Tests.Unit;

/// <summary>Справжній дворівневий кеш для unit-тестів: L1 — MemoryCache, L2 — MemoryDistributedCache замість Redis.</summary>
public static class TestCache
{
    public static TwoLevelCache Create(IMemoryCache? memory = null, IDistributedCache? distributed = null) =>
        new(memory ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 }),
            distributed ?? new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            new CacheMetrics(new TestMeterFactory()),
            NullLogger<TwoLevelCache>.Instance,
            TimeProvider.System);

    public static IReviewsCacheInvalidator Invalidator(ITwoLevelCache? cache = null) =>
        new ReviewsCacheInvalidator(cache ?? Create(), NullLogger<ReviewsCacheInvalidator>.Instance);

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options);
        public void Dispose() { }
    }
}
