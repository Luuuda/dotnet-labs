using System.Data;
using BookingService.Dal.Abstractions;
using BookingService.Domain.Models;
using Dapper;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.DapperRepositories;

/// <summary>
/// Бронювання — корінь агрегату. Спільний CRUD успадковано з <see cref="BaseDapperRepository{T}"/>;
/// тут лише специфічне: multi-mapping, виклики процедур з Лаб. 01, вставка частин агрегату.
/// </summary>
public sealed class BookingRepository(SqlConnection connection, Func<SqlTransaction?> transaction, int commandTimeout)
    : BaseDapperRepository<Booking>(connection, transaction, commandTimeout, "dbo.Bookings"), IBookingRepository
{
    protected override string SelectColumns =>
        "Id, CustomerId, PromotionId, Status, CreatedAt, CreatedBy, UpdatedAt, UpdatedBy, IsDeleted, RowVersion";

    protected override string? ActiveFilter => "IsDeleted = 0";

    protected override string InsertSql => """
        INSERT INTO dbo.Bookings (CustomerId, PromotionId, Status, CreatedBy)
        OUTPUT INSERTED.Id
        VALUES (@CustomerId, @PromotionId, @Status, @CreatedBy)
        """;

    /// <summary>Бронювання з історією не видаляються фізично — лише soft delete.</summary>
    protected override string DeleteSql => """
        UPDATE dbo.Bookings SET IsDeleted = 1, UpdatedAt = SYSUTCDATETIME()
        WHERE Id = @Id AND IsDeleted = 0
        """;

    public Task<Booking?> GetWithTicketsAsync(long bookingId, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        // Один запит → кожен рядок JOIN містить Booking + BookingDetails + один Ticket.
        // splitOn: з «BookingId» починається BookingDetails, з (наступного) «Id» — Ticket.
        const string sql = """
            SELECT b.Id, b.CustomerId, b.PromotionId, b.Status, b.CreatedAt, b.CreatedBy,
                   b.UpdatedAt, b.UpdatedBy, b.IsDeleted, b.RowVersion,
                   d.BookingId, d.ContactEmail, d.ContactPhone, d.Channel, d.Comment,
                   t.Id, t.ScreeningId, t.SeatRow, t.SeatNumber, t.MovieTitle, t.HallName,
                   t.StartsAt, t.UnitPrice, t.Discount, t.IsCancelled
            FROM dbo.Bookings b
            LEFT JOIN dbo.BookingDetails d ON d.BookingId = b.Id
            LEFT JOIN dbo.Tickets t        ON t.BookingId = b.Id
            WHERE b.Id = @BookingId AND b.IsDeleted = 0
            ORDER BY t.SeatRow, t.SeatNumber
            """;

        Booking? result = null;
        await Connection.QueryAsync<Booking, BookingDetails?, Ticket?, Booking>(
            Command(sql, new { BookingId = bookingId }, ct),
            (booking, details, ticket) =>
            {
                result ??= booking;
                result.Details ??= details;
                if (ticket is not null)
                {
                    ticket.BookingId = result.Id;
                    result.Tickets.Add(ticket);
                }
                return result;
            },
            splitOn: "BookingId,Id");

        if (result is null) return null;

        var history = await Connection.QueryAsync<BookingStatusChange>(Command("""
            SELECT Id, BookingId, FromStatus, ToStatus, ChangedAt, ChangedBy, Reason
            FROM dbo.BookingStatusHistory WHERE BookingId = @BookingId ORDER BY ChangedAt, Id
            """, new { BookingId = bookingId }, ct));
        result.History = history.AsList();
        return result;
    }, ct);

    public Task<IReadOnlyList<BookingSummary>> GetPageByCustomerAsync(
        Guid customerId, string? status, int page, int pageSize, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        // Процедура з Лаб. 01: фільтр за статусом + OFFSET/FETCH + TotalCount
        var rows = await Connection.QueryAsync<BookingSummary>(Command(
            "dbo.usp_Bookings_GetByCustomer",
            new { CustomerId = customerId, Status = status, PageNumber = page, PageSize = pageSize },
            ct, CommandType.StoredProcedure));
        return (IReadOnlyList<BookingSummary>)rows.AsList();
    }, ct);

    public Task<IReadOnlyList<(int Row, int Seat)>> GetOccupiedSeatsAsync(long screeningId, CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var rows = await Connection.QueryAsync<OccupiedSeat>(Command("""
                SELECT SeatRow, SeatNumber FROM dbo.Tickets
                WHERE ScreeningId = @ScreeningId AND IsCancelled = 0
                ORDER BY SeatRow, SeatNumber
                """, new { ScreeningId = screeningId }, ct));
            return (IReadOnlyList<(int, int)>)rows.Select(r => (r.SeatRow, r.SeatNumber)).ToList();
        }, ct);

    public Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken ct = default) =>
        ExecuteAsync(async () =>
        {
            var rows = await Connection.QueryAsync<(string Status, int Count)>(Command("""
                SELECT Status, COUNT(*) AS [Count] FROM dbo.Bookings
                WHERE IsDeleted = 0
                GROUP BY Status
                """, null, ct));
            return (IReadOnlyDictionary<string, int>)rows.ToDictionary(r => r.Status, r => r.Count);
        }, ct);

    public Task AddDetailsAsync(BookingDetails details, CancellationToken ct = default) => ExecuteAsync(
        () => Connection.ExecuteAsync(Command("""
            INSERT INTO dbo.BookingDetails (BookingId, ContactEmail, ContactPhone, Channel, Comment)
            VALUES (@BookingId, @ContactEmail, @ContactPhone, @Channel, @Comment)
            """, details, ct)), ct);

    public Task AddTicketsAsync(IEnumerable<Ticket> tickets, CancellationToken ct = default) => ExecuteAsync(
        // Колекція як параметр → Dapper виконує INSERT для кожного елемента (у тій самій транзакції)
        () => Connection.ExecuteAsync(Command("""
            INSERT INTO dbo.Tickets (BookingId, ScreeningId, SeatRow, SeatNumber, MovieTitle, HallName, StartsAt, UnitPrice, Discount)
            VALUES (@BookingId, @ScreeningId, @SeatRow, @SeatNumber, @MovieTitle, @HallName, @StartsAt, @UnitPrice, @Discount)
            """, tickets.ToList(), ct)), ct);

    public Task AddStatusHistoryAsync(BookingStatusChange change, CancellationToken ct = default) => ExecuteAsync(
        () => Connection.ExecuteAsync(Command("""
            INSERT INTO dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedBy, Reason)
            VALUES (@BookingId, @FromStatus, @ToStatus, @ChangedBy, @Reason)
            """, change, ct)), ct);

    public Task<byte[]> ChangeStatusAsync(long bookingId, string newStatus, string changedBy, string? reason,
                                          byte[]? expectedRowVersion, CancellationToken ct = default) => ExecuteAsync(async () =>
    {
        // dbo.usp_Booking_ChangeStatus (Лаб. 01): явна транзакція, UPDLOCK, перевірка переходу,
        // THROW 50002 на недопустимому переході → BusinessConflictException (див. SqlErrorTranslator).
        var row = await Connection.QuerySingleAsync<StatusRow>(Command(
            "dbo.usp_Booking_ChangeStatus",
            new
            {
                BookingId = bookingId,
                NewStatus = newStatus,
                ChangedBy = changedBy,
                Reason = reason,
                ExpectedRowVersion = expectedRowVersion
            },
            ct, CommandType.StoredProcedure));
        return row.RowVersion;
    }, ct);

    private sealed class OccupiedSeat
    {
        public int SeatRow { get; set; }
        public int SeatNumber { get; set; }
    }

    private sealed class StatusRow
    {
        public long BookingId { get; set; }
        public string Status { get; set; } = "";
        public byte[] RowVersion { get; set; } = [];
    }
}
