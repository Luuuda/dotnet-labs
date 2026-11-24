using Microsoft.Extensions.Caching.Memory;

namespace BookingService.Bll.Caching;

/// <summary>TTL на двох рівнях; L1 завжди коротший за L2.</summary>
public sealed record CachePolicy(string Name, TimeSpan L1Absolute, TimeSpan? L1Sliding, TimeSpan L2Absolute,
    CacheItemPriority Priority = CacheItemPriority.Normal)
{
    /// <summary>Локальні копії сеансів: змінюються лише синхронізацією з Каталогом — довгий TTL.</summary>
    public static readonly CachePolicy Reference = new("reference",
        L1Absolute: TimeSpan.FromMinutes(5), L1Sliding: TimeSpan.FromMinutes(1), L2Absolute: TimeSpan.FromMinutes(30), CacheItemPriority.High);

    /// <summary>
    /// Картка бронювання: персональні дані (email, телефон) і статус, що часто змінюється, — дуже короткий TTL.
    /// Кешується лише для серії повторних читань одного бронювання (Aggregator, екран оплати).
    /// </summary>
    public static readonly CachePolicy PersonalShort = new("personal",
        L1Absolute: TimeSpan.FromSeconds(10), L1Sliding: null, L2Absolute: TimeSpan.FromSeconds(30), CacheItemPriority.Low);

    public static readonly CachePolicy List = new("list",
        L1Absolute: TimeSpan.FromSeconds(30), L1Sliding: null, L2Absolute: TimeSpan.FromMinutes(2), CacheItemPriority.Low);

    public static readonly CachePolicy Version = new("version",
        L1Absolute: TimeSpan.FromSeconds(5), L1Sliding: null, L2Absolute: TimeSpan.FromHours(2), CacheItemPriority.High);
}
