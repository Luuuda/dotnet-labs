namespace BookingService.Domain.Models;

/// <summary>
/// Сеанс (dbo.Screenings) — ЛОКАЛЬНА КОПІЯ довідника з сервісу Каталогу (Лаб. 01, політика дублювання).
/// Id той самий, що в CinemaCatalog; PK без IDENTITY.
/// </summary>
public sealed class Screening
{
    public long Id { get; set; }
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = "";
    public int HallId { get; set; }
    public string HallName { get; set; } = "";
    public int RowsCount { get; set; }
    public int SeatsPerRow { get; set; }
    public DateTime StartsAt { get; set; }
    public decimal BasePrice { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime SyncedAt { get; set; }

    public bool Contains(int row, int seat) => row >= 1 && row <= RowsCount && seat >= 1 && seat <= SeatsPerRow;
}
