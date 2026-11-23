using BookingService.Dal.Abstractions;
using BookingService.Domain.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.DapperRepositories;

/// <summary>
/// Сеанси — локальна копія довідника Каталогу (політика дублювання Лаб. 01).
/// Id не IDENTITY: він приходить із сервісу Каталогу, тому вставляється явно.
/// </summary>
public sealed class ScreeningRepository(SqlConnection connection, Func<SqlTransaction?> transaction, int commandTimeout)
    : BaseDapperRepository<Screening>(connection, transaction, commandTimeout, "dbo.Screenings"), IScreeningRepository
{
    protected override string SelectColumns =>
        "Id, MovieId, MovieTitle, HallId, HallName, RowsCount, SeatsPerRow, StartsAt, BasePrice, IsActive, SyncedAt";

    protected override string InsertSql => """
        INSERT INTO dbo.Screenings (Id, MovieId, MovieTitle, HallId, HallName, RowsCount, SeatsPerRow, StartsAt, BasePrice, IsActive)
        OUTPUT INSERTED.Id
        VALUES (@Id, @MovieId, @MovieTitle, @HallId, @HallName, @RowsCount, @SeatsPerRow, @StartsAt, @BasePrice, @IsActive)
        """;

    public Task<IReadOnlyList<Screening>> GetUpcomingAsync(DateTime from, bool onlyActive, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var sql = $"""
                SELECT {SelectColumns} FROM dbo.Screenings
                WHERE StartsAt >= @From AND (@OnlyActive = 0 OR IsActive = 1)
                ORDER BY StartsAt, Id
                """;
            var rows = await Connection.QueryAsync<Screening>(Command(sql, new { From = from, OnlyActive = onlyActive }, ct));
            return (IReadOnlyList<Screening>)rows.AsList();
        }, ct);

    public Task<bool> UpdateAsync(Screening s, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        const string sql = """
            UPDATE dbo.Screenings
            SET MovieId = @MovieId, MovieTitle = @MovieTitle, HallId = @HallId, HallName = @HallName,
                RowsCount = @RowsCount, SeatsPerRow = @SeatsPerRow, StartsAt = @StartsAt,
                BasePrice = @BasePrice, IsActive = @IsActive, SyncedAt = SYSUTCDATETIME()
            WHERE Id = @Id
            """;
        return await Connection.ExecuteAsync(Command(sql, s, ct)) > 0;
    }, ct);
}
