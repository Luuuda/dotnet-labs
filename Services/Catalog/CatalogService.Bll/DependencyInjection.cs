using CatalogService.Bll.Mapping;
using CatalogService.Bll.Services;
using CatalogService.Bll.Validators;
using CatalogService.Dal;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CatalogService.Bll;

public static class DependencyInjection
{
    /// <summary>Реєстрація BLL (і DAL під ним). Web/API бачить лише цей метод і сервіси.</summary>
    public static IServiceCollection AddBusinessLogicLayer(this IServiceCollection services,
        string connectionString, string? autoMapperLicenseKey)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddDataAccessLayer(connectionString);

        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = autoMapperLicenseKey;
            MappingConfiguration.Configure(cfg);
        });

        // Валідатори FluentValidation реєструються в DI і викликаються ЯВНО (FluentValidation.AspNetCore застарів)
        services.AddValidatorsFromAssemblyContaining<CreateMovieDtoValidator>();

        services.AddScoped<IMovieService, MovieService>();
        services.AddScoped<IGenreService, GenreService>();
        services.AddScoped<IScreeningService, ScreeningService>();
        return services;
    }

    public static Task InitializeDatabaseAsync(this IServiceProvider services, bool seed, CancellationToken ct = default)
        => services.MigrateAndSeedAsync(seed, ct);
}
