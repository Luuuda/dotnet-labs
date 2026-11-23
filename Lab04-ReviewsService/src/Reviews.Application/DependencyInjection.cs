using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Reviews.Application.Common.Behaviors;

namespace Reviews.Application;

public static class DependencyInjection
{
    /// <summary>
    /// MediatR (обробники зі сканування збірки + behaviors), валідатори FluentValidation, AutoMapper.
    /// Порядок AddOpenBehavior = порядок виконання: Logging → ExceptionHandling → Validation → Performance → обробник.
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
            cfg.AddOpenBehavior(typeof(PerformanceBehavior<,>));         // 4 — біля обробника
        });

        services.AddValidatorsFromAssembly(assembly);

        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = licenseKey;
            cfg.AddMaps(new[] { assembly }.Concat(extraMappingAssemblies));   // профілі Application + (з WebApi) профілі HTTP-контрактів
        });
        return services;
    }
}
