using System.Data;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.Infrastructure;

/// <summary>
/// Спільна основа всіх репозиторіїв. Репозиторій отримує НЕ рядок підключення, а з'єднання
/// і доступ до ПОТОЧНОЇ транзакції Unit of Work — тому всі репозиторії однієї бізнес-операції
/// працюють через одне з'єднання та одну транзакцію.
/// </summary>
public abstract class RepositoryBase
{
    protected readonly SqlConnection Connection;
    private readonly Func<SqlTransaction?> _currentTransaction;

    protected RepositoryBase(SqlConnection connection, Func<SqlTransaction?> currentTransaction, int commandTimeoutSeconds)
    {
        Connection = connection;
        _currentTransaction = currentTransaction;
        CommandTimeout = commandTimeoutSeconds;
    }

    /// <summary>Поточна транзакція Unit of Work (або null, якщо її не розпочато).</summary>
    protected SqlTransaction? Transaction => _currentTransaction();

    /// <summary>Таймаут команди в секундах (з конфігурації Database:CommandTimeoutSeconds).</summary>
    protected int CommandTimeout { get; }

    protected async Task EnsureOpenAsync(CancellationToken ct)
    {
        if (Connection.State != ConnectionState.Open)
            await Connection.OpenAsync(ct);
    }

    /// <summary>
    /// Виконує звернення до БД: відкриває з'єднання за потреби і перетворює
    /// <see cref="SqlException"/> на доменний виняток (сирий текст помилки SQL не виходить за межі DAL).
    /// </summary>
    protected async Task<T> ExecuteAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        try
        {
            await EnsureOpenAsync(ct);
            return await action();
        }
        catch (SqlException ex)
        {
            throw SqlErrorTranslator.Translate(ex);
        }
    }

    protected async Task ExecuteAsync(Func<Task> action, CancellationToken ct)
        => await ExecuteAsync(async () => { await action(); return true; }, ct);
}
