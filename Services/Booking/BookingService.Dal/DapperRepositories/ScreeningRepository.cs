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

    // ---------------- Лаб. 08: копія сеансів оновлюється подіями з Каталогу ----------------
    // Порядок подій не гарантований (повтор після тимчасової помилки йде в кінець черги), тому для ціни і назви
    // зберігається час останньої застосованої зміни (PriceSyncedAt / TitleSyncedAt = OccurredAt події): умова
    // «… IS NULL OR … < @OccurredAt» відкидає застарілу подію, і вона не перезапише свіжішу ціну чи назву.
    // SyncedAt — коли копію востаннє оновлено. Tickets не змінюються: ціна й назва в оформлених бронюваннях —
    // знімок на момент покупки (політика дублювання Лаб. 01). Кожен метод повертає кількість змінених рядків.

    /// <summary>Новий сеанс: вставка, якщо його ще немає (сеанс створюється один раз; існуючий не змінюється).</summary>
    public Task<int> UpsertFromCatalogAsync(Screening s, DateTime occurredAt, CancellationToken ct = default) =>
        ExecuteAsync(() => Connection.ExecuteAsync(Command("""
            MERGE dbo.Screenings WITH (HOLDLOCK) AS t
            USING (SELECT @Id AS Id) AS src ON t.Id = src.Id
            WHEN NOT MATCHED THEN
                INSERT (Id, MovieId, MovieTitle, HallId, HallName, RowsCount, SeatsPerRow, StartsAt, BasePrice, IsActive,
                        SyncedAt, PriceSyncedAt, TitleSyncedAt)
                VALUES (@Id, @MovieId, @MovieTitle, @HallId, @HallName, @RowsCount, @SeatsPerRow, @StartsAt, @BasePrice, 1,
                        SYSUTCDATETIME(), @OccurredAt, @OccurredAt);
            """, new
            {
                s.Id, s.MovieId, s.MovieTitle, s.HallId, s.HallName, s.RowsCount, s.SeatsPerRow, s.StartsAt,
                s.BasePrice, OccurredAt = occurredAt
            }, ct)), ct);

    /// <summary>Нова базова ціна — лише для майбутніх бронювань; застаріла подія (старша за застосовану) ігнорується.</summary>
    public Task<int> ChangePriceFromCatalogAsync(long screeningId, decimal newPrice, DateTime occurredAt, CancellationToken ct = default) =>
        ExecuteAsync(() => Connection.ExecuteAsync(Command("""
            UPDATE dbo.Screenings
            SET BasePrice = @NewPrice, PriceSyncedAt = @OccurredAt, SyncedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND (PriceSyncedAt IS NULL OR PriceSyncedAt < @OccurredAt)
            """, new { Id = screeningId, NewPrice = newPrice, OccurredAt = occurredAt }, ct)), ct);

    /// <summary>Скасування — кінцевий стан, тому застосовується завжди (повторне — без змін).</summary>
    public Task<int> DeactivateFromCatalogAsync(long screeningId, DateTime occurredAt, CancellationToken ct = default) =>
        ExecuteAsync(() => Connection.ExecuteAsync(Command("""
            UPDATE dbo.Screenings SET IsActive = 0, SyncedAt = SYSUTCDATETIME()
            WHERE Id = @Id AND IsActive = 1
            """, new { Id = screeningId }, ct)), ct);

    /// <summary>Нова назва фільму в усіх його сеансах; застаріла подія ігнорується.</summary>
    public Task<int> RenameMovieFromCatalogAsync(int movieId, string movieTitle, DateTime occurredAt, CancellationToken ct = default) =>
        ExecuteAsync(() => Connection.ExecuteAsync(Command("""
            UPDATE dbo.Screenings
            SET MovieTitle = @MovieTitle, TitleSyncedAt = @OccurredAt, SyncedAt = SYSUTCDATETIME()
            WHERE MovieId = @MovieId AND (TitleSyncedAt IS NULL OR TitleSyncedAt < @OccurredAt)
            """, new { MovieId = movieId, MovieTitle = movieTitle, OccurredAt = occurredAt }, ct)), ct);

    public Task<IReadOnlyList<long>> GetIdsByMovieAsync(int movieId, CancellationToken ct = default) =>
        ExecuteAsync(async () => (IReadOnlyList<long>)(await Connection.QueryAsync<long>(Command(
            "SELECT Id FROM dbo.Screenings WHERE MovieId = @MovieId", new { MovieId = movieId }, ct))).AsList(), ct);
}
