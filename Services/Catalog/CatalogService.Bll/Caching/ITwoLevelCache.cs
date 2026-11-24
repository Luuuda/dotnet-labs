namespace CatalogService.Bll.Caching;

/// <summary>Дворівневий кеш каталогу: L1 — IMemoryCache, L2 — IDistributedCache (Redis), далі — база даних.</summary>
public interface ITwoLevelCache
{
    /// <summary>
    /// Cache-aside: L1 → L2 → <paramref name="factory"/> (БД). Значення, знайдене на нижчому рівні, записується
    /// у вищі рівні на зворотному шляху. null від фабрики не кешується.
    /// </summary>
    Task<T?> GetOrCreateAsync<T>(string key, CachePolicy policy, Func<CancellationToken, Task<T?>> factory, CancellationToken ct)
        where T : class;

    /// <summary>Видаляє ключі з обох рівнів синхронно з операцією запису; причина потрапляє в лог і метрику.</summary>
    Task InvalidateAsync(string reason, CancellationToken ct, params string[] keys);

    /// <summary>Поточна версія групи списків (див. <see cref="CacheKeys.MoviesListVersion"/>).</summary>
    Task<string> GetVersionAsync(string versionKey, CancellationToken ct);

    /// <summary>Нова версія групи списків: усі сторінки зі старою версією перестають використовуватися.</summary>
    Task BumpVersionAsync(string versionKey, string reason, CancellationToken ct);
}
