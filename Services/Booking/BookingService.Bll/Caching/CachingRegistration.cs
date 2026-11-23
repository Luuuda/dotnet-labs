using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BookingService.Bll.Caching;

public static class CachingRegistration
{
    /// <summary>Дворівневий кеш. Сховища (AddMemoryCache зі SizeLimit, AddRedisDistributedCache) реєструє Program.cs.</summary>
    public static IServiceCollection AddBookingCaching(this IServiceCollection services)
    {
        // Запасні реєстрації (TryAdd): якщо хост не зареєстрував сховища / метрики (unit-тести, консольний запуск),
        // L1 — пам'ять без ліміту, L2 — пам'ять замість Redis. Program.cs реєструє їх раніше, тож вони мають пріоритет.
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddMetrics();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<CacheMetrics>();
        services.AddSingleton<ITwoLevelCache, TwoLevelCache>();
        return services;
    }
}
