using System.Data;
using BookingService.Dal.Abstractions;
using BookingService.Dal.Infrastructure;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.AdoNet;

/// <summary>
/// Репозиторій клієнтів на ЧИСТОМУ ADO.NET (Microsoft.Data.SqlClient): SqlCommand, SqlParameter,
/// SqlDataReader, процедура з OUTPUT-параметром — без сторонніх бібліотек.
/// Свідомо НЕ успадковує Generic Repository: ключ — GUID, і тут показано «ручну» роботу,
/// яку в Dapper-репозиторіях бере на себе бібліотека.
/// Усі значення передаються ЛИШЕ як параметри — жодної конкатенації вхідних даних у SQL.
/// </summary>
public sealed class CustomerRepository(SqlConnection connection, Func<SqlTransaction?> transaction, int commandTimeout)
    : RepositoryBase(connection, transaction, commandTimeout), ICustomerRepository
{
    private const string Columns =
        "Id, Email, FullName, Phone, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    public Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        const string sql = $"SELECT {Columns} FROM dbo.Customers WHERE Id = @Id AND IsDeleted = 0";

        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;

        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }, ct);

    public Task<(IReadOnlyList<Customer> Items, int Total)> GetPageAsync(
        string? search, int offset, int limit, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        // Пошук за email або ім'ям. Шаблон LIKE будується з ЕКРАНОВАНОГО значення і йде параметром.
        const string sql = $"""
            SELECT {Columns}, COUNT(*) OVER () AS TotalCount
            FROM dbo.Customers
            WHERE IsDeleted = 0
              AND (@Search IS NULL OR Email LIKE @Search ESCAPE '\' OR FullName LIKE @Search ESCAPE '\')
            ORDER BY FullName, Id
            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY
            """;

        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Search", SqlDbType.NVarChar, 260).Value =
            string.IsNullOrWhiteSpace(search) ? DBNull.Value : $"%{EscapeLike(search.Trim())}%";
        command.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        command.Parameters.Add("@Limit", SqlDbType.Int).Value = limit;

        var items = new List<Customer>();
        var total = 0;
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            items.Add(Map(reader));
            total = reader.GetInt32(reader.GetOrdinal("TotalCount"));
        }
        return ((IReadOnlyList<Customer>)items, total);
    }, ct);

    public Task<bool> EmailExistsAsync(string email, Guid? exceptId, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        const string sql = """
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Customers
                WHERE Email = @Email AND IsDeleted = 0 AND (@ExceptId IS NULL OR Id <> @ExceptId)
            ) THEN 1 ELSE 0 END
            """;

        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = email;
        command.Parameters.Add("@ExceptId", SqlDbType.UniqueIdentifier).Value = (object?)exceptId ?? DBNull.Value;

        return (int)(await command.ExecuteScalarAsync(ct))! == 1;
    }, ct);

    public Task<Guid> AddAsync(Customer customer, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        // Виклик збережуваної процедури з Лаб. 01: CommandType.StoredProcedure + OUTPUT-параметр.
        await using var command = CreateCommand("dbo.usp_Customer_Upsert");
        command.CommandType = CommandType.StoredProcedure;

        var id = command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier);
        id.Direction = ParameterDirection.InputOutput;
        id.Value = DBNull.Value;                                   // NULL → процедура створює нового клієнта
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = customer.FullName;
        command.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = (object?)customer.Phone ?? DBNull.Value;
        command.Parameters.Add("@ChangedBy", SqlDbType.NVarChar, 100).Value = customer.CreatedBy;

        await command.ExecuteNonQueryAsync(ct);
        return (Guid)id.Value;
    }, ct);

    public Task<byte[]> UpdateAsync(Customer customer, byte[] expectedRowVersion, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        // Оптимістична конкурентність: рядок оновлюється, лише якщо RowVersion не змінився.
        const string sql = """
            UPDATE dbo.Customers
            SET Email = @Email, FullName = @FullName, Phone = @Phone,
                UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
            OUTPUT INSERTED.RowVersion
            WHERE Id = @Id AND IsDeleted = 0 AND RowVersion = @RowVersion
            """;

        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = customer.Id;
        command.Parameters.Add("@Email", SqlDbType.NVarChar, 256).Value = customer.Email;
        command.Parameters.Add("@FullName", SqlDbType.NVarChar, 200).Value = customer.FullName;
        command.Parameters.Add("@Phone", SqlDbType.VarChar, 20).Value = (object?)customer.Phone ?? DBNull.Value;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value = customer.UpdatedBy ?? "booking-api";
        command.Parameters.Add("@RowVersion", SqlDbType.Timestamp).Value = expectedRowVersion;

        if (await command.ExecuteScalarAsync(ct) is byte[] newVersion)
            return newVersion;

        // 0 рядків: або клієнта немає, або його вже змінив хтось інший
        if (await GetByIdAsync(customer.Id, ct) is null)
            throw new NotFoundException($"Клієнта {customer.Id} не знайдено");
        throw new BusinessConflictException("Клієнта вже змінив інший користувач — оновіть дані й повторіть спробу");
    }, ct);

    public Task<bool> SoftDeleteAsync(Guid id, string changedBy, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        const string sql = """
            UPDATE dbo.Customers
            SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @UpdatedBy
            WHERE Id = @Id AND IsDeleted = 0
            """;

        await using var command = CreateCommand(sql);
        command.Parameters.Add("@Id", SqlDbType.UniqueIdentifier).Value = id;
        command.Parameters.Add("@UpdatedBy", SqlDbType.NVarChar, 100).Value = changedBy;

        return await command.ExecuteNonQueryAsync(ct) > 0;
    }, ct);

    /// <summary>Команда на СПІЛЬНОМУ з'єднанні та в ПОТОЧНІЙ транзакції Unit of Work.</summary>
    private SqlCommand CreateCommand(string sql) =>
        new(sql, Connection, Transaction) { CommandTimeout = CommandTimeout };

    /// <summary>Ручне читання рядка SqlDataReader у модель (у Dapper цю роботу робить бібліотека).</summary>
    private static Customer Map(SqlDataReader r) => new()
    {
        Id = r.GetGuid(r.GetOrdinal("Id")),
        Email = r.GetString(r.GetOrdinal("Email")),
        FullName = r.GetString(r.GetOrdinal("FullName")),
        Phone = r.IsDBNull(r.GetOrdinal("Phone")) ? null : r.GetString(r.GetOrdinal("Phone")),
        CreatedAt = r.GetDateTime(r.GetOrdinal("CreatedAt")),
        CreatedBy = r.GetString(r.GetOrdinal("CreatedBy")),
        UpdatedAt = r.IsDBNull(r.GetOrdinal("UpdatedAt")) ? null : r.GetDateTime(r.GetOrdinal("UpdatedAt")),
        UpdatedBy = r.IsDBNull(r.GetOrdinal("UpdatedBy")) ? null : r.GetString(r.GetOrdinal("UpdatedBy")),
        IsDeleted = r.GetBoolean(r.GetOrdinal("IsDeleted")),
        RowVersion = (byte[])r["RowVersion"]
    };

    /// <summary>Екранування спецсимволів LIKE (%, _, [) — щоб пошук «50%» шукав саме «50%».</summary>
    private static string EscapeLike(string value) =>
        value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_").Replace("[", @"\[");
}
