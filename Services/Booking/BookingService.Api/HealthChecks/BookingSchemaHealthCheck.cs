using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace BookingService.Api.HealthChecks;

/// <summary>
/// Лаб. 07: власна перевірка готовності схеми бази бронювання. Доступність SQL Server (SELECT 1) перевіряє
/// AddSqlServer; ця перевірка — що в базі є об'єкти Лаб. 01, без яких сервіс не працює: таблиця dbo.Bookings і
/// процедура зміни статусу dbo.usp_Booking_ChangeStatus (порожня база до застосування скриптів db/p1 → Unhealthy).
/// Запит легкий: два виклики OBJECT_ID без читання даних. Теги: ready і startup.
/// </summary>
public sealed class BookingSchemaHealthCheck(IConfiguration configuration) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var connectionString = new SqlConnectionStringBuilder(configuration.GetConnectionString("CinemaBookingDb"))
                { ConnectTimeout = 3 }.ConnectionString;                     // недоступний сервер → відповідь за секунди
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync(ct);
            await using var command = new SqlCommand(
                "SELECT CASE WHEN OBJECT_ID(N'dbo.Bookings', N'U') IS NOT NULL " +
                "AND OBJECT_ID(N'dbo.usp_Booking_ChangeStatus', N'P') IS NOT NULL THEN 1 ELSE 0 END", connection);
            var ready = (int)(await command.ExecuteScalarAsync(ct) ?? 0) == 1;

            return ready
                ? HealthCheckResult.Healthy("Схема Лаб. 01 на місці: dbo.Bookings, dbo.usp_Booking_ChangeStatus")
                : new HealthCheckResult(context.Registration.FailureStatus,
                    "Схема бази не розгорнута: немає dbo.Bookings або dbo.usp_Booking_ChangeStatus (скрипти db/p1)");
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "SQL Server недоступний для перевірки схеми", ex);
        }
    }
}
