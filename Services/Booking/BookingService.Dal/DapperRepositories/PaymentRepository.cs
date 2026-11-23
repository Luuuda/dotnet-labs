using BookingService.Dal.Abstractions;
using BookingService.Domain.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.DapperRepositories;

/// <summary>Оплати. Видалення фінансових записів через API не передбачено (FK без каскаду).</summary>
public sealed class PaymentRepository(SqlConnection connection, Func<SqlTransaction?> transaction, int commandTimeout)
    : BaseDapperRepository<Payment>(connection, transaction, commandTimeout, "dbo.Payments"), IPaymentRepository
{
    protected override string SelectColumns =>
        "Id, BookingId, Amount, Currency, Provider, ExternalTransactionId, Status, PaidAt, CreatedBy";

    protected override string InsertSql => """
        INSERT INTO dbo.Payments (BookingId, Amount, Currency, Provider, ExternalTransactionId, Status, CreatedBy)
        OUTPUT INSERTED.Id
        VALUES (@BookingId, @Amount, @Currency, @Provider, @ExternalTransactionId, @Status, @CreatedBy)
        """;

    public Task<Payment?> GetByExternalIdAsync(string externalTransactionId, CancellationToken ct = default) => ExecuteAsync(
        () => Connection.QuerySingleOrDefaultAsync<Payment>(Command(
            $"SELECT {SelectColumns} FROM dbo.Payments WHERE ExternalTransactionId = @ExternalTransactionId",
            new { ExternalTransactionId = externalTransactionId }, ct)), ct);

    public Task<IReadOnlyList<Payment>> GetByBookingAsync(long bookingId, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        var rows = await Connection.QueryAsync<Payment>(Command(
            $"SELECT {SelectColumns} FROM dbo.Payments WHERE BookingId = @BookingId ORDER BY PaidAt, Id",
            new { BookingId = bookingId }, ct));
        return (IReadOnlyList<Payment>)rows.AsList();
    }, ct);
}
