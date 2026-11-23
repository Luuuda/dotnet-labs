using BookingService.Dal.Abstractions;
using BookingService.Dal.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BookingService.Dal;

public static class DependencyInjection
{
    /// <summary>
    /// Реєстрація DAL. Unit of Work — Scoped: один екземпляр і одне з'єднання на HTTP-запит.
    /// Репозиторії окремо не реєструються: їх створює й роздає Unit of Work.
    /// </summary>
    public static IServiceCollection AddDataAccessLayer(this IServiceCollection services,
        string connectionString, int commandTimeoutSeconds = 30)
    {
        services.AddScoped<IUnitOfWork>(sp =>
            new UnitOfWork(connectionString, commandTimeoutSeconds, sp.GetService<ILogger<UnitOfWork>>()));
        services.AddSingleton(sp =>
            new DatabaseBootstrapper(connectionString, sp.GetRequiredService<ILogger<DatabaseBootstrapper>>()));
        return services;
    }
}
