using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Caching;
using Reviews.Application.Common.Behaviors;

namespace Reviews.Application;

public static class DependencyInjection
{
    /// <summary>
    /// MediatR (обробники зі сканування збірки + behaviors), валідатори FluentValidation, AutoMapper.
    /// Порядок AddOpenBehavior = порядок виконання: Logging → ExceptionHandling → Validation → Caching → Performance → обробник.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration config,
        params Assembly[] extraMappingAssemblies)
    {
        var assembly = typeof(DependencyInjection).Assembly;
        var licenseKey = config["LuckyPenny:LicenseKey"] is { Length: > 0 } key ? key : null;   // один ключ на MediatR і AutoMapper

        services.AddMediatR(cfg =>
        {
            cfg.LicenseKey = licenseKey;
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));             // 1 — зовнішній
            cfg.AddOpenBehavior(typeof(ExceptionHandlingBehavior<,>));   // 2
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));          // 3
            cfg.AddOpenBehavior(typeof(CachingBehavior<,>));             // 4 — Лаб. 06: L1 → L2 → обробник (лише ICacheableQuery)
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));         // 5 — біля обробника
        });

        services.AddValidatorsFromAssembly(assembly);
        services.AddReviewsCaching();                                  // Лаб. 06: ITwoLevelCache, метрики, інвалідація

        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = licenseKey;
            cfg.AddMaps(new[] { assembly }.Concat(extraMappingAssemblies));   // профілі Application + (з WebApi) профілі HTTP-контрактів
        });
        return services;
    }
}
