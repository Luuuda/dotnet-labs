namespace Reviews.Application.Caching;

/// <summary>
/// Запит MediatR, відповідь на який кешує <see cref="CachingBehavior{TRequest,TResponse}"/> (cache-aside у pipeline,
/// без змін в обробниках). Якщо <see cref="VersionKey"/> задано, ключ містить поточну версію групи —
/// так одним записом інвалідуються всі сторінки списку.
/// </summary>
public interface ICacheableQuery
{
    /// <summary>false — саме цей екземпляр запиту не кешується (наприклад, список без фільтра за фільмом).</summary>
    bool IsCacheable => true;
    CachePolicy CachePolicy { get; }
    string? VersionKey { get; }
    string GetCacheKey(string? version);
}
