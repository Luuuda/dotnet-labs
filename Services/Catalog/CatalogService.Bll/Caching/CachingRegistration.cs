using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CatalogService.Bll.Caching;

public static class CachingRegistration
{
    /// <summary>
    /// Сервіси дворівневого кешу. Самі сховища реєструє Program.cs: AddMemoryCache (L1, зі SizeLimit) і
    /// AddRedisDistributedCache("redis") (L2); без Redis (тести, окремий запуск) — AddDistributedMemoryCache.
    /// </summary>
    public static IServiceCollection AddCatalogCaching(this IServiceCollection services, bool warmUp)
    {
        // Запасні реєстрації (TryAdd): якщо хост не зареєстрував сховища / метрики (unit-тести, консольний запуск),
        // L1 — пам'ять без ліміту, L2 — пам'ять замість Redis. Program.cs реєструє їх раніше, тож вони мають пріоритет.
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CacheMetrics>();
        services.AddSingleton<ITwoLevelCache, TwoLevelCache>();
        if (warmUp) services.AddHostedService<CacheWarmupService>();
        return services;
    }
}
