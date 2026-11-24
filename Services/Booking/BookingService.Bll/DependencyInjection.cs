using BookingService.Bll.Caching;
using BookingService.Bll.IntegrationEvents;
using BookingService.Bll.IntegrationEvents.Handlers;
using BookingService.Bll.Mapping;
using BookingService.Bll.Services;
using BookingService.Bll.Validation;
using BookingService.Dal;
using BookingService.Dal.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Platform.Messaging;

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
        services.AddBookingCaching();                         // Лаб. 06: ITwoLevelCache (L1 пам'ять → L2 Redis), метрики
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IScreeningService, ScreeningService>();
        services.AddScoped<IBookingService, BookingsService>();

        // Лаб. 08: по одному обробнику на тип події з Каталогу (Scoped: свій Unit of Work на кожне повідомлення)
        services.AddScoped<IIntegrationEventHandler<ScreeningScheduledEvent>, ScreeningScheduledHandler>();
        services.AddScoped<IIntegrationEventHandler<ScreeningPriceChangedEvent>, ScreeningPriceChangedHandler>();
        services.AddScoped<IIntegrationEventHandler<ScreeningCancelledEvent>, ScreeningCancelledHandler>();
        services.AddScoped<IIntegrationEventHandler<MovieRenamedEvent>, MovieRenamedHandler>();
        return services;
    }

    /// <summary>Підготовка порожньої бази (контейнер Aspire) скриптами Лаб. 01.</summary>
    public static Task InitializeDatabaseAsync(this IServiceProvider services, CancellationToken ct = default) =>
        services.GetRequiredService<DatabaseBootstrapper>().EnsureSchemaAsync(ct);
}
