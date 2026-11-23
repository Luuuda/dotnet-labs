using MediatR;

namespace Reviews.Application.Caching;

/// <summary>
/// Pipeline behavior кешування (Лаб. 06): для запитів <see cref="ICacheableQuery"/> — L1 → L2 → обробник (MongoDB).
/// Стоїть ПІСЛЯ ValidationBehavior, тож невалідні запити не кешуються; винятки (NotFound) теж не кешуються.
/// Команди сюди не потрапляють: вони інвалідують кеш через <see cref="IReviewsCacheInvalidator"/>.
/// </summary>
public sealed class CachingBehavior<TRequest, TResponse>(ITwoLevelCache cache) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not ICacheableQuery { IsCacheable: true } cacheable) return await next(ct);

        var version = cacheable.VersionKey is { } versionKey ? await cache.GetVersionAsync(versionKey, ct) : null;
        return (await cache.GetOrCreateAsync(cacheable.GetCacheKey(version), cacheable.CachePolicy, async _ => await next(ct), ct))!;
    }
}
