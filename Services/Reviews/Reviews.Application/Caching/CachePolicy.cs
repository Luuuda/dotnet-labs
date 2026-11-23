using Microsoft.Extensions.Caching.Memory;

namespace Reviews.Application.Caching;

/// <summary>TTL на двох рівнях; L1 завжди коротший за L2.</summary>
public sealed record CachePolicy(string Name, TimeSpan L1Absolute, TimeSpan? L1Sliding, TimeSpan L2Absolute,
    CacheItemPriority Priority = CacheItemPriority.Normal)
{
    /// <summary>Рейтинг фільму: читається на кожній картці фільму й бронювання — sliding у пам'яті.</summary>
    public static readonly CachePolicy Rating = new("rating",
        L1Absolute: TimeSpan.FromMinutes(1), L1Sliding: TimeSpan.FromSeconds(30), L2Absolute: TimeSpan.FromMinutes(5), CacheItemPriority.High);

    /// <summary>Відгук з коментарями: коментарі додаються часто — короткий TTL.</summary>
    public static readonly CachePolicy Review = new("review",
        L1Absolute: TimeSpan.FromSeconds(30), L1Sliding: null, L2Absolute: TimeSpan.FromMinutes(2));

    /// <summary>Сторінки списків відгуків фільму.</summary>
    public static readonly CachePolicy List = new("list",
        L1Absolute: TimeSpan.FromSeconds(30), L1Sliding: null, L2Absolute: TimeSpan.FromMinutes(2), CacheItemPriority.Low);

    public static readonly CachePolicy Version = new("version",
        L1Absolute: TimeSpan.FromSeconds(5), L1Sliding: null, L2Absolute: TimeSpan.FromHours(2), CacheItemPriority.High);
}
