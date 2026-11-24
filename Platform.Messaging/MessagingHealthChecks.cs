using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Messaging;

/// <summary>
/// Health check RabbitMQ реєструє сама Aspire-інтеграція: builder.AddRabbitMQClient("messaging") додає перевірку
/// «RabbitMQ.Client» (відкрите з'єднання з брокером). Тут вона лише включається в readiness-набір платформи
/// (тег ready, Лаб. 07) зі статусом Degraded: без брокера HTTP API сервісу працює (Каталог зберігає зміни,
/// Бронювання продає квитки), відкладається лише синхронізація копій — події чекають у durable-черзі.
/// </summary>
public static class MessagingHealthChecks
{
    public static IServiceCollection AddRabbitMqToReadiness(this IServiceCollection services, string readyTag,
        HealthStatus failureStatus = HealthStatus.Degraded) =>
        services.Configure<HealthCheckServiceOptions>(options =>
        {
            foreach (var registration in options.Registrations.Where(r => r.Name.StartsWith("RabbitMQ", StringComparison.Ordinal)))
            {
                registration.Tags.Add(readyTag);
                registration.Tags.Add("messaging");
                registration.FailureStatus = failureStatus;
                registration.Timeout = TimeSpan.FromSeconds(3);
            }
        });
}
