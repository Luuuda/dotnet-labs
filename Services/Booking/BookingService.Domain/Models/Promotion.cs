namespace BookingService.Domain.Models;

/// <summary>Промокод (dbo.Promotions).</summary>
public sealed class Promotion
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal DiscountPercent { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime ValidTo { get; set; }
    public bool IsActive { get; set; }
}
