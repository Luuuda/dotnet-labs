namespace BookingService.Domain.Models;

/// <summary>
/// Статуси бронювання (за зразком OrderStatus з eShop):
/// Submitted → AwaitingValidation → SeatsConfirmed → Paid → Issued; Cancelled — лише до оплати.
/// Допустимість переходу перевіряє процедура dbo.usp_Booking_ChangeStatus з Лаб. 01 (THROW 50002).
/// </summary>
public static class BookingStatus
{
    public const string Submitted = "Submitted";
    public const string AwaitingValidation = "AwaitingValidation";
    public const string SeatsConfirmed = "SeatsConfirmed";
    public const string Paid = "Paid";
    public const string Issued = "Issued";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlyList<string> All =
        [Submitted, AwaitingValidation, SeatsConfirmed, Paid, Issued, Cancelled];
}

public static class PaymentStatus
{
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
    public const string Refunded = "Refunded";
}
