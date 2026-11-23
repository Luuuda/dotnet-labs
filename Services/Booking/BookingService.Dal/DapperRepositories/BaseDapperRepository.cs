using BookingService.Dal.Abstractions;
using BookingService.Dal.Infrastructure;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.DapperRepositories;

/// <summary>
/// Спільний CRUD для всіх Dapper-репозиторіїв — написаний ОДИН раз.
/// Конкретні репозиторії задають лише ім'я таблиці, SQL вставки та свої специфічні запити.
/// </summary>
/// <remarks>
/// Ім'я таблиці підставляється в текст запиту, але це КОНСТАНТА з коду (передається в конструктор
/// нащадком), а не вхідні дані користувача — тому така підстановка безпечна.
/// Усі значення (Id тощо) йдуть лише параметрами. Dapper не відкриває й не закриває з'єднання
/// сам — він лише додає методи-розширення до SqlConnection, який належить Unit of Work.
/// </remarks>
public abstract class BaseDapperRepository<T> : RepositoryBase, IGenericRepository<T> where T : class
{
    protected BaseDapperRepository(SqlConnection connection, Func<SqlTransaction?> transaction,
                                   int commandTimeout, string tableName)
        : base(connection, transaction, commandTimeout)
    {
        TableName = tableName;
    }

    /// <summary>Ім'я таблиці, наприклад «dbo.Bookings» — константа з коду.</summary>
    protected string TableName { get; }

    /// <summary>Перелік стовпців для SELECT (за замовчуванням усі).</summary>
    protected virtual string SelectColumns => "*";

    /// <summary>Додаткова умова «живих» записів для soft delete, наприклад «IsDeleted = 0».</summary>
    protected virtual string? ActiveFilter => null;

    /// <summary>INSERT ... OUTPUT INSERTED.Id VALUES (...) — свій для кожної таблиці.</summary>
    protected abstract string InsertSql { get; }

    /// <summary>Видалення за замовчуванням — фізичне; нащадок може замінити на soft delete.</summary>
    protected virtual string DeleteSql => $"DELETE FROM {TableName} WHERE Id = @Id";

    private string Where(string condition) =>
        ActiveFilter is null ? $"WHERE {condition}" : $"WHERE {condition} AND {ActiveFilter}";

    public virtual Task<T?> GetByIdAsync(long id, CancellationToken ct = default) => ExecuteAsync(
        () => Connection.QuerySingleOrDefaultAsync<T>(
            Command($"SELECT {SelectColumns} FROM {TableName} {Where("Id = @Id")}", new { Id = id }, ct)), ct);

    public virtual Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        var sql = ActiveFilter is null
            ? $"SELECT {SelectColumns} FROM {TableName} ORDER BY Id"
            : $"SELECT {SelectColumns} FROM {TableName} WHERE {ActiveFilter} ORDER BY Id";
        return (IReadOnlyList<T>)(await Connection.QueryAsync<T>(Command(sql, null, ct))).AsList();
    }, ct);

    public virtual Task<long> AddAsync(T entity, CancellationToken ct = default) => ExecuteAsync(
        // Dapper бере значення параметрів (@CustomerId, @Status, ...) з властивостей самої моделі
        () => Connection.ExecuteScalarAsync<long>(Command(InsertSql, entity, ct)), ct);

    public virtual Task<bool> DeleteAsync(long id, CancellationToken ct = default) => ExecuteAsync(
        async () => await Connection.ExecuteAsync(Command(DeleteSql, new { Id = id }, ct)) > 0, ct);

    /// <summary>
    /// CommandDefinition: SQL + параметри + ПОТОЧНА транзакція UoW + таймаут + CancellationToken
    /// (скасування HTTP-запиту перериває й запит до SQL Server).
    /// </summary>
    protected CommandDefinition Command(string sql, object? parameters, CancellationToken ct,
                                        System.Data.CommandType? commandType = null) =>
        new(sql, parameters, Transaction, CommandTimeout, commandType, cancellationToken: ct);
}
