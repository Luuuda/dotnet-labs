using BookingService.Dal.Abstractions;
using BookingService.Domain.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.DapperRepositories;

public sealed class PromotionRepository(SqlConnection connection, Func<SqlTransaction?> transaction, int commandTimeout)
    : BaseDapperRepository<Promotion>(connection, transaction, commandTimeout, "dbo.Promotions"), IPromotionRepository
{
    protected override string SelectColumns => "Id, Code, Title, DiscountPercent, ValidFrom, ValidTo, IsActive";

    protected override string InsertSql => """
        INSERT INTO dbo.Promotions (Code, Title, DiscountPercent, ValidFrom, ValidTo, IsActive)
        OUTPUT INSERTED.Id
        VALUES (@Code, @Title, @DiscountPercent, @ValidFrom, @ValidTo, @IsActive)
        """;

    public Task<Promotion?> GetActiveByCodeAsync(string code, DateTime at, CancellationToken ct = default) => ExecuteAsync(
        () => Connection.QuerySingleOrDefaultAsync<Promotion>(Command($"""
            SELECT {SelectColumns} FROM dbo.Promotions
            WHERE Code = @Code AND IsActive = 1 AND @At >= ValidFrom AND @At < ValidTo
            """, new { Code = code, At = at }, ct)), ct);
}
