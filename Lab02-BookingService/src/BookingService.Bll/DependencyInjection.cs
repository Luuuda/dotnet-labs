using BookingService.Bll.Mapping;
using BookingService.Bll.Services;
using BookingService.Bll.Validation;
using BookingService.Dal;
using BookingService.Dal.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace BookingService.Bll;

public static class DependencyInjection
{
    /// <summary>
    /// Реєстрація BLL (і DAL під нею). API викликає лише цей метод — шар Web/API бачить тільки BLL.
    /// </summary>
    public static IServiceCollection AddBusinessLogicLayer(this IServiceCollection services,
        string connectionString, int commandTimeoutSeconds, string? autoMapperLicenseKey)
    {
        services.AddDataAccessLayer(connectionString, commandTimeoutSeconds);

        // Профілі AutoMapper — централізовано (той самий набір перевіряє тест AssertConfigurationIsValid)
        services.AddAutoMapper(cfg =>
        {
            cfg.LicenseKey = autoMapperLicenseKey;
            MappingConfiguration.Configure(cfg);
        });

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<SeatSelectionValidator>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IScreeningService, ScreeningService>();
        services.AddScoped<IBookingService, BookingsService>();
        return services;
    }

    /// <summary>Підготовка порожньої бази (контейнер Aspire) скриптами Лаб. 01.</summary>
    public static Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default) =>
        services.GetRequiredService<DatabaseBootstrapper>().EnsureSchemaAsync(ct);
}
