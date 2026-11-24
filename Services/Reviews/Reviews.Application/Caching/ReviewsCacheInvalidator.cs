using Microsoft.Extensions.Logging;

namespace Reviews.Application.Caching;

/// <summary>Інвалідація кешу відгуків після запису — викликається обробниками команд одразу після збереження в MongoDB.</summary>
public interface IReviewsCacheInvalidator
{
    /// <summary>Рейтинг і всі сторінки відгуків фільму (новий / змінений / видалений відгук, перейменування фільму).</summary>
    Task MovieChangedAsync(int movieId, string reason, CancellationToken ct);

    /// <summary>Сам відгук + дані його фільму.</summary>
    Task ReviewChangedAsync(string reviewId, int movieId, string reason, CancellationToken ct);
}

public sealed class ReviewsCacheInvalidator(ITwoLevelCache cache, ILogger<ReviewsCacheInvalidator> logger) : IReviewsCacheInvalidator
{
    public async Task MovieChangedAsync(int movieId, string reason, CancellationToken ct)
    {
        await cache.InvalidateAsync(reason, ct, CacheKeys.MovieRating(movieId));
        await cache.BumpVersionAsync(CacheKeys.ListVersion(movieId), reason, ct);
        logger.LogDebug("Кеш фільму {MovieId} інвалідовано: {Reason}", movieId, reason);
    }

    public async Task ReviewChangedAsync(string reviewId, int movieId, string reason, CancellationToken ct)
    {
        await cache.InvalidateAsync(reason, ct, CacheKeys.Review(reviewId));
        await MovieChangedAsync(movieId, reason, ct);
    }
}
