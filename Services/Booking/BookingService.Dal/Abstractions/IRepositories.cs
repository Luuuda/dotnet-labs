using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;

namespace BookingService.Dal.Abstractions;

/// <summary>
/// Клієнти — репозиторій на ЧИСТОМУ ADO.NET. Свідомо НЕ успадковує <see cref="IGenericRepository{T}"/>:
/// ключ клієнта — GUID (а не long), а сам репозиторій показує ручну роботу з SqlCommand,
/// SqlParameter і SqlDataReader без сторонніх бібліотек.
/// </summary>
public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<Customer> Items, int Total)> GetPageAsync(string? search, int offset, int limit, CancellationToken ct = default);
    Task<bool> EmailExistsAsync(string email, Guid? exceptId, CancellationToken ct = default);
    /// <summary>Створення через процедуру dbo.usp_Customer_Upsert (OUTPUT-параметр @Id).</summary>
    Task<Guid> AddAsync(Customer customer, CancellationToken ct = default);
    /// <summary>Оновлення з перевіркою rowversion; повертає нову версію рядка.</summary>
    Task<byte[]> UpdateAsync(Customer customer, byte[] expectedRowVersion, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(Guid id, string changedBy, CancellationToken ct = default);
}

/// <summary>Сеанси — локальна копія довідника Каталогу (Dapper).</summary>
public interface IScreeningRepository : IGenericRepository<Screening>
{
    Task<IReadOnlyList<Screening>> GetUpcomingAsync(DateTime from, bool onlyActive, CancellationToken ct = default);
    /// <summary>Оновлення копії за подією з Каталогу (ScreeningChanged / MovieRenamed).</summary>
    Task<bool> UpdateAsync(Screening screening, CancellationToken ct = default);

    // --- Лаб. 08: оновлення копії подіями з Каталогу. Кожен метод застосовує зміну, лише якщо подія НОВІША
    // за останню застосовану (SyncedAt < @OccurredAt): пізня подія не перезапише свіжіші дані (out-of-order).
    // Знімки в Tickets (MovieTitle, UnitPrice) ці методи не чіпають — це ціна на момент покупки.

    /// <summary>Новий сеанс → вставка копії (якщо його ще немає). Повертає число змінених рядків.</summary>
    Task<int> UpsertFromCatalogAsync(Screening screening, DateTime occurredAt, CancellationToken ct = default);
    /// <summary>Нова базова ціна сеансу (на майбутні бронювання).</summary>
    Task<int> ChangePriceFromCatalogAsync(long screeningId, decimal newPrice, DateTime occurredAt, CancellationToken ct = default);
    /// <summary>Сеанс скасовано в Каталозі → IsActive = 0 (нові бронювання на нього неможливі).</summary>
    Task<int> DeactivateFromCatalogAsync(long screeningId, DateTime occurredAt, CancellationToken ct = default);
    /// <summary>Фільм перейменовано → нова назва в усіх його сеансах.</summary>
    Task<int> RenameMovieFromCatalogAsync(int movieId, string movieTitle, DateTime occurredAt, CancellationToken ct = default);
    /// <summary>Ідентифікатори сеансів фільму в копії (для інвалідації кешу після перейменування).</summary>
    Task<IReadOnlyList<long>> GetIdsByMovieAsync(int movieId, CancellationToken ct = default);
}

/// <summary>
/// Лаб. 08: оброблені інтеграційні події (dbo.ProcessedEvents, PRIMARY KEY = EventId) — ідемпотентність споживача.
/// </summary>
public interface IProcessedEventRepository
{
    /// <summary>
    /// Записує EventId у поточній транзакції Unit of Work. false — подію вже оброблено (порушення PRIMARY KEY, 2627).
    /// </summary>
    Task<bool> TryAddAsync(Guid eventId, string eventType, CancellationToken ct = default);
    /// <summary>Чи оброблено подію (для діагностики й тестів).</summary>
    Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default);
}

/// <summary>Бронювання — корінь агрегату (Dapper: multi-mapping, процедури).</summary>
public interface IBookingRepository : IGenericRepository<Booking>
{
    /// <summary>Бронювання з деталями та квитками одним запитом (multi-mapping) + історія статусів.</summary>
    Task<Booking?> GetWithTicketsAsync(long bookingId, CancellationToken ct = default);
    /// <summary>Пошук бронювань клієнта з пагінацією — dbo.usp_Bookings_GetByCustomer.</summary>
    Task<IReadOnlyList<BookingSummary>> GetPageByCustomerAsync(Guid customerId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<(int Row, int Seat)>> GetOccupiedSeatsAsync(long screeningId, CancellationToken ct = default);
    /// <summary>Лаб. 07: кількість активних бронювань у кожному статусі (для gauge-метрик). Один легкий GROUP BY.</summary>
    Task<IReadOnlyDictionary<string, int>> CountByStatusAsync(CancellationToken ct = default);
    Task AddDetailsAsync(BookingDetails details, CancellationToken ct = default);
    Task AddTicketsAsync(IEnumerable<Ticket> tickets, CancellationToken ct = default);
    Task AddStatusHistoryAsync(BookingStatusChange change, CancellationToken ct = default);
    /// <summary>
    /// Зміна статусу через dbo.usp_Booking_ChangeStatus з Лаб. 01 (CommandType.StoredProcedure).
    /// Недопустимий перехід → THROW 50002 → <see cref="BusinessConflictException"/>.
    /// </summary>
    Task<byte[]> ChangeStatusAsync(long bookingId, string newStatus, string changedBy, string? reason,
                                   byte[]? expectedRowVersion, CancellationToken ct = default);
}

/// <summary>Оплати (Dapper).</summary>
public interface IPaymentRepository : IGenericRepository<Payment>
{
    Task<Payment?> GetByExternalIdAsync(string externalTransactionId, CancellationToken ct = default);
    Task<IReadOnlyList<Payment>> GetByBookingAsync(long bookingId, CancellationToken ct = default);
}

/// <summary>Промокоди (Dapper).</summary>
public interface IPromotionRepository : IGenericRepository<Promotion>
{
    Task<Promotion?> GetActiveByCodeAsync(string code, DateTime at, CancellationToken ct = default);
}
