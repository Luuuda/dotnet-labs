using BookingService.Dal.Abstractions;
using BookingService.Dal.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.DapperRepositories;

/// <summary>
/// Лаб. 08: таблиця dbo.ProcessedEvents (PRIMARY KEY = EventId). Запис виконується в поточній транзакції
/// Unit of Work, тож «подію оброблено» і зміна копії dbo.Screenings фіксуються або відкочуються разом.
/// </summary>
public sealed class ProcessedEventRepository(SqlConnection connection, Func<SqlTransaction?> transaction, int commandTimeout)
    : RepositoryBase(connection, transaction, commandTimeout), IProcessedEventRepository
{
    public async Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        try
        {
            await Connection.ExecuteAsync(new CommandDefinition(
                "INSERT INTO dbo.ProcessedEvents (EventId, EventType) VALUES (@EventId, @EventType)",
                new { EventId = eventId, EventType = eventType }, Transaction, CommandTimeout, cancellationToken: ct));
            return true;
        }
        catch (SqlException ex) when (ex.Number is 2627 or 2601)
        {
            return false;   // порушення PRIMARY KEY: цей EventId уже оброблено — дубль доставки
        }
    }

    public async Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default)
    {
        await EnsureOpenAsync(ct);
        return await Connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM dbo.ProcessedEvents WHERE EventId = @EventId",
            new { EventId = eventId }, Transaction, CommandTimeout, cancellationToken: ct)) > 0;
    }
}
