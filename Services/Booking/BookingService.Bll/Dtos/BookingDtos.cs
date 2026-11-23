using System.ComponentModel.DataAnnotations;
using BookingService.Bll.Validation;

namespace BookingService.Bll.Dtos;

public sealed record TicketDto
{
    public long Id { get; init; }
    public long ScreeningId { get; init; }
    public int SeatRow { get; init; }
    public int SeatNumber { get; init; }
    /// <summary>Знімок назви фільму на момент бронювання (не оновлюється).</summary>
    public string MovieTitle { get; init; } = "";
    public string HallName { get; init; } = "";
    public DateTime StartsAt { get; init; }
    /// <summary>Знімок ціни на момент бронювання (не оновлюється).</summary>
    public decimal UnitPrice { get; init; }
    public decimal Discount { get; init; }
    public decimal FinalPrice { get; init; }
    public bool IsCancelled { get; init; }
}

public sealed record BookingStatusChangeDto
{
    public string? FromStatus { get; init; }
    public string ToStatus { get; init; } = "";
    public DateTime ChangedAt { get; init; }
    public string ChangedBy { get; init; } = "";
    public string? Reason { get; init; }
}

public sealed record BookingDto
{
    public long Id { get; init; }
    public Guid CustomerId { get; init; }
    public int? PromotionId { get; init; }
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public string? ContactEmail { get; init; }
    public string? ContactPhone { get; init; }
    public string? Channel { get; init; }
    public string? Comment { get; init; }
    public decimal TotalAmount { get; init; }
    public string RowVersion { get; init; } = "";
    public IReadOnlyList<TicketDto> Tickets { get; init; } = [];
    public IReadOnlyList<BookingStatusChangeDto> History { get; init; } = [];
}

public sealed record BookingSummaryDto
{
    public long BookingId { get; init; }
    public string Status { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public string? PromoCode { get; init; }
    public int TicketsCount { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal PaidAmount { get; init; }
    public DateTime? FirstStartsAt { get; init; }
}

/// <summary>Створення бронювання. Модельна валідація — атрибутами; бізнес-валідація (місця, промокод) — у сервісі.</summary>
public sealed record CreateBookingDto
{
    [NotEmptyGuid]
    public Guid CustomerId { get; init; }

    [Range(1, long.MaxValue)]
    public long ScreeningId { get; init; }

    [Required, MinLength(1), MaxLength(10, ErrorMessage = "За одне бронювання — не більше 10 місць")]
    public List<SeatDto> Seats { get; init; } = [];

    [StringLength(30)]
    public string? PromoCode { get; init; }

    [RegularExpression(@"^\+[0-9]{7,15}$", ErrorMessage = "Телефон у форматі +380XXXXXXXXX")]
    public string? ContactPhone { get; init; }

    [RegularExpression("^(Web|Mobile|BoxOffice)$", ErrorMessage = "Канал: Web, Mobile або BoxOffice")]
    public string Channel { get; init; } = "Web";

    [StringLength(500)]
    public string? Comment { get; init; }
}

/// <summary>Необов'язкове тіло для дій над статусом (confirm / issue / cancel).</summary>
public sealed record BookingActionDto
{
    [StringLength(300)]
    public string? Reason { get; init; }

    /// <summary>Очікувана версія рядка (base64) — для оптимістичної перевірки; можна не передавати.</summary>
    public string? RowVersion { get; init; }
}

public sealed record PayBookingDto
{
    [Range(typeof(decimal), "0.01", "1000000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    public decimal Amount { get; init; }

    [Required, RegularExpression("^(Card|ApplePay|GooglePay|Cash)$", ErrorMessage = "Provider: Card, ApplePay, GooglePay або Cash")]
    public string Provider { get; init; } = "Card";

    /// <summary>Id транзакції платіжного шлюзу. Якщо не задано — береться заголовок Idempotency-Key.</summary>
    [StringLength(100)]
    public string? ExternalTransactionId { get; init; }
}

public sealed record PaymentDto
{
    public long Id { get; init; }
    public long BookingId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "";
    public string Provider { get; init; } = "";
    public string? ExternalTransactionId { get; init; }
    public string Status { get; init; } = "";
    public DateTime PaidAt { get; init; }
}

/// <summary>Результат оплати: Replayed = true, якщо це повтор уже обробленого запиту (ідемпотентність).</summary>
public sealed record PaymentResultDto(PaymentDto Payment, bool Replayed);
