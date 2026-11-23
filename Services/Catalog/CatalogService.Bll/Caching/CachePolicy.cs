namespace CatalogService.Bll.Caching;

/// <summary>
/// Час життя запису на двох рівнях. Правило: L1 (пам'ять процесу) живе КОРОТШЕ за L2 (Redis), інакше застаріла
/// копія в пам'яті одного екземпляра пережила б оновлення в Redis.
/// </summary>
/// <param name="Name">Назва політики — тег метрик і логів.</param>
/// <param name="L1Absolute">Абсолютний TTL у пам'яті.</param>
/// <param name="L1Sliding">Ковзний TTL у пам'яті (для часто запитуваних даних); null — лише абсолютний.</param>
/// <param name="L2Absolute">Абсолютний TTL у Redis.</param>
/// <param name="Priority">Пріоритет при витісненні з пам'яті, коли досягнуто SizeLimit.</param>
public sealed record CachePolicy(
    string Name,
    TimeSpan L1Absolute,
    TimeSpan? L1Sliding,
    TimeSpan L2Absolute,
    Microsoft.Extensions.Caching.Memory.CacheItemPriority Priority = Microsoft.Extensions.Caching.Memory.CacheItemPriority.Normal)
{
    /// <summary>Довідники (жанри, зали): змінюються рідко — absolute expiration, високий пріоритет.</summary>
    public static readonly CachePolicy Reference = new("reference",
        L1Absolute: TimeSpan.FromMinutes(10), L1Sliding: null, L2Absolute: TimeSpan.FromHours(1),
        Microsoft.Extensions.Caching.Memory.CacheItemPriority.High);

    /// <summary>Картка фільму / сеанс: читають дуже часто — sliding у пам'яті, обмежений абсолютним TTL.</summary>
    public static readonly CachePolicy Card = new("card",
        L1Absolute: TimeSpan.FromMinutes(2), L1Sliding: TimeSpan.FromSeconds(45), L2Absolute: TimeSpan.FromMinutes(10));

    /// <summary>Сторінки списків: багато варіантів фільтрів — короткий TTL, низький пріоритет.</summary>
    public static readonly CachePolicy List = new("list",
        L1Absolute: TimeSpan.FromSeconds(30), L1Sliding: null, L2Absolute: TimeSpan.FromMinutes(2),
        Microsoft.Extensions.Caching.Memory.CacheItemPriority.Low);

    /// <summary>Ключі-версії списків: короткий L1, щоб репліки швидко побачили нову версію з Redis.</summary>
    public static readonly CachePolicy Version = new("version",
        L1Absolute: TimeSpan.FromSeconds(5), L1Sliding: null, L2Absolute: TimeSpan.FromHours(2),
        Microsoft.Extensions.Caching.Memory.CacheItemPriority.High);
}
