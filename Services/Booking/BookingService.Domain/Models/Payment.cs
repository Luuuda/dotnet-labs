namespace BookingService.Domain.Models;

/// <summary>Оплата (dbo.Payments). Фінансовий запис — без каскадного видалення.</summary>
public sealed class Payment
{
    public long Id { get; set; }
    public long BookingId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "UAH";
    public string Provider { get; set; } = "";
    /// <summary>Id транзакції платіжного шлюзу — водночас ключ ідемпотентності.</summary>
    public string? ExternalTransactionId { get; set; }
    public string Status { get; set; } = PaymentStatus.Succeeded;
    public DateTime PaidAt { get; set; }
    public string CreatedBy { get; set; } = "";
}
