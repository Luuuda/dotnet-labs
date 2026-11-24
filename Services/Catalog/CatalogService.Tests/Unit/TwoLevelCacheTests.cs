using CatalogService.Bll.Caching;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;

namespace CatalogService.Tests.Unit;

/// <summary>Лаб. 06: поведінка дворівневого кешу L1 (пам'ять) → L2 (Redis) → БД.</summary>
public sealed class TwoLevelCacheTests
{
    private sealed record Item(int Id, string Name);

    private readonly MemoryCache _l1 = new(new MemoryCacheOptions { SizeLimit = 10_000 });
    private readonly MemoryDistributedCache _l2 = new(Options.Create(new MemoryDistributedCacheOptions()));

    [Fact]
    public async Task Second_read_is_served_from_L1_without_calling_the_database()
    {
        var cache = TestCache.Create(_l1, _l2);
        var dbCalls = 0;
        Task<Item?> Db(CancellationToken _) { dbCalls++; return Task.FromResult<Item?>(new Item(1, "Дюна")); }

        var first = await cache.GetOrCreateAsync(CacheKeys.Movie(1), CachePolicy.Card, Db, CancellationToken.None);
        var second = await cache.GetOrCreateAsync(CacheKeys.Movie(1), CachePolicy.Card, Db, CancellationToken.None);

        Assert.Equal(1, dbCalls);
        Assert.Equal(first, second);
        Assert.NotNull(await _l2.GetAsync(CacheKeys.Movie(1)));          // і в Redis теж записано
    }

    [Fact]
    public async Task L2_hit_fills_L1_on_the_way_back()
    {
        var other = TestCache.Create(new MemoryCache(new MemoryCacheOptions { SizeLimit = 10_000 }), _l2);   // «інша репліка»
        await other.GetOrCreateAsync(CacheKeys.Movie(2), CachePolicy.Card, _ => Task.FromResult<Item?>(new Item(2, "Мавка")), CancellationToken.None);

        var cache = TestCache.Create(_l1, _l2);
        var value = await cache.GetOrCreateAsync<Item>(CacheKeys.Movie(2), CachePolicy.Card,
            _ => throw new InvalidOperationException("БД не повинна викликатися"), CancellationToken.None);

        Assert.Equal("Мавка", value!.Name);
        Assert.True(_l1.TryGetValue(CacheKeys.Movie(2), out _));          // L1 заповнено з L2
    }

    [Fact]
    public async Task Invalidation_removes_key_from_both_levels()
    {
        var cache = TestCache.Create(_l1, _l2);
        await cache.GetOrCreateAsync(CacheKeys.Movie(3), CachePolicy.Card, _ => Task.FromResult<Item?>(new Item(3, "Старе")), CancellationToken.None);

        await cache.InvalidateAsync("MovieUpdated", CancellationToken.None, CacheKeys.Movie(3));
        var fresh = await cache.GetOrCreateAsync(CacheKeys.Movie(3), CachePolicy.Card, _ => Task.FromResult<Item?>(new Item(3, "Нове")), CancellationToken.None);

        Assert.Equal("Нове", fresh!.Name);
    }

    [Fact]
    public async Task Redis_failure_does_not_fail_the_request()
    {
        var broken = new Mock<IDistributedCache>();
        broken.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException("redis down"));
        broken.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
              .ThrowsAsync(new TimeoutException("redis down"));
        var cache = TestCache.Create(_l1, broken.Object);

        var value = await cache.GetOrCreateAsync(CacheKeys.Movie(4), CachePolicy.Card, _ => Task.FromResult<Item?>(new Item(4, "З БД")), CancellationToken.None);

        Assert.Equal("З БД", value!.Name);
    }

    [Fact]
    public async Task Not_found_is_not_cached()
    {
        var cache = TestCache.Create(_l1, _l2);
        await cache.GetOrCreateAsync<Item>(CacheKeys.Movie(404), CachePolicy.Card, _ => Task.FromResult<Item?>(null), CancellationToken.None);
        Assert.False(_l1.TryGetValue(CacheKeys.Movie(404), out _));
        Assert.Null(await _l2.GetAsync(CacheKeys.Movie(404)));
    }

    [Fact]
    public async Task Version_bump_makes_old_list_pages_unreachable()
    {
        var cache = TestCache.Create(_l1, _l2);
        var before = await cache.GetVersionAsync(CacheKeys.MoviesListVersion, CancellationToken.None);
        await cache.BumpVersionAsync(CacheKeys.MoviesListVersion, "MovieCreated", CancellationToken.None);
        var after = await cache.GetVersionAsync(CacheKeys.MoviesListVersion, CancellationToken.None);

        Assert.NotEqual(CacheKeys.MoviesList(before, "q"), CacheKeys.MoviesList(after, "q"));
    }

    [Theory]
    [MemberData(nameof(Policies))]
    public void L1_lives_shorter_than_L2(CachePolicy policy) => Assert.True(policy.L1Absolute < policy.L2Absolute);

    public static TheoryData<CachePolicy> Policies => [CachePolicy.Reference, CachePolicy.Card, CachePolicy.List, CachePolicy.Version];
}
