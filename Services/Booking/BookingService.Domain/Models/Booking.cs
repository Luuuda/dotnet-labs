namespace BookingService.Domain.Models;

/// <summary>
/// Корінь агрегату «Бронювання» (dbo.Bookings) — аналог Order з eShop.
/// Квитки (Tickets), деталі (BookingDetails, зв'язок 1:1) та історія статусів
/// живуть лише в межах агрегату й змінюються через нього.
/// </summary>
public sealed class Booking
{
    public long Id { get; set; }
    public Guid CustomerId { get; set; }
    public int? PromotionId { get; set; }
    public string Status { get; set; } = BookingStatus.Submitted;
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = "";
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public byte[] RowVersion { get; set; } = [];

    public BookingDetails? Details { get; set; }
    public List<Ticket> Tickets { get; set; } = [];
    public List<BookingStatusChange> History { get; set; } = [];

    /// <summary>Сума до сплати: ціна-знімок мінус знижка, лише по нескасованих квитках.</summary>
    public decimal TotalAmount => Tickets.Where(t => !t.IsCancelled).Sum(t => t.UnitPrice - t.Discount);
}

/// <summary>dbo.BookingDetails — зв'язок 1:1 з Bookings (FK = PK).</summary>
public sealed class BookingDetails
{
    public long BookingId { get; set; }
    public string ContactEmail { get; set; } = "";
    public string? ContactPhone { get; set; }
    public string Channel { get; set; } = "Web";
    public string? Comment { get; set; }
}

/// <summary>
/// dbo.Tickets — проміжна таблиця M:N Bookings ↔ Screenings з власними полями.
/// MovieTitle, HallName, StartsAt, UnitPrice — ЗНІМОК на момент бронювання (як OrderItem в eShop).
/// </summary>
public sealed class Ticket
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public long ScreeningId { get; set; }
    public int SeatRow { get; set; }
    public int SeatNumber { get; set; }
    public string MovieTitle { get; set; } = "";
    public string HallName { get; set; } = "";
    public DateTime StartsAt { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Discount { get; set; }
    public bool IsCancelled { get; set; }
}

/// <summary>Рядок журналу dbo.BookingStatusHistory.</summary>
public sealed class BookingStatusChange
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public DateTime ChangedAt { get; set; }
    public string ChangedBy { get; set; } = "";
    public string? Reason { get; set; }
}

/// <summary>Рядок зі списку бронювань (dbo.vw_BookingSummary / usp_Bookings_GetByCustomer).</summary>
public sealed class BookingSummary
{
    public long BookingId { get; set; }
    public string Status { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public string? PromoCode { get; set; }
    public int TicketsCount { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public DateTime? FirstStartsAt { get; set; }
    public int TotalCount { get; set; }
}
