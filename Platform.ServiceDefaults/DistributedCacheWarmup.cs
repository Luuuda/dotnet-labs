using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults;

/// <summary>
/// Лаб. 06: з'єднання з Redis відкривається ліниво, і перше підключення займає 1–2 с. Щоб цю затримку не отримав
/// перший запит користувача (і не спрацював тайм-аут Aggregator), сервіс робить пробне звернення до L2 під час старту.
/// Якщо IDistributedCache не зареєстровано (Gateway) або Redis недоступний — старт триває як звичайно.
/// </summary>
public sealed class DistributedCacheWarmup(IServiceProvider services, ILogger<DistributedCacheWarmup> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (services.GetService<IDistributedCache>() is not { } cache) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await cache.GetAsync("platform:warmup", cancellationToken);
            logger.LogInformation("Distributed cache готовий за {ElapsedMs} мс", sw.ElapsedMilliseconds);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning("Distributed cache недоступний на старті ({ErrorType}) — сервіс працюватиме з L1 і БД", ex.GetType().Name);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
