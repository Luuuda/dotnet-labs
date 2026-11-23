using System.ComponentModel.DataAnnotations;

namespace BookingService.Bll.Dtos;

public sealed record ScreeningDto
{
    public long Id { get; init; }
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public int HallId { get; init; }
    public string HallName { get; init; } = "";
    public int RowsCount { get; init; }
    public int SeatsPerRow { get; init; }
    public DateTime StartsAt { get; init; }
    public decimal BasePrice { get; init; }
    public bool IsActive { get; init; }
    public DateTime SyncedAt { get; init; }
}

/// <summary>
/// Дані сеансу з Каталогу. У продуктивній системі приходять подією ScreeningScheduled/ScreeningChanged
/// (Лаб. 08); тут — через API, щоб можна було наповнити локальну копію вручну.
/// </summary>
public record UpsertScreeningDto
{
    [Range(1, int.MaxValue)] public int MovieId { get; init; }
    [Required, StringLength(200)] public string MovieTitle { get; init; } = "";
    [Range(1, int.MaxValue)] public int HallId { get; init; }
    [Required, StringLength(100)] public string HallName { get; init; } = "";
    [Range(1, 50)] public int RowsCount { get; init; }
    [Range(1, 60)] public int SeatsPerRow { get; init; }
    public DateTime StartsAt { get; init; }
    [Range(typeof(decimal), "0.01", "100000", ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)] public decimal BasePrice { get; init; }
    public bool IsActive { get; init; } = true;
}

public sealed record CreateScreeningDto : UpsertScreeningDto
{
    /// <summary>Той самий Id, що в CinemaCatalog (локальна копія, не IDENTITY).</summary>
    [Range(1, long.MaxValue)] public long Id { get; init; }
}

public sealed record SeatDto
{
    [Range(1, 50)] public int Row { get; init; }
    [Range(1, 60)] public int Seat { get; init; }
}

public sealed record SeatMapDto
{
    public long ScreeningId { get; init; }
    public int RowsCount { get; init; }
    public int SeatsPerRow { get; init; }
    public int FreeCount { get; init; }
    public IReadOnlyList<SeatDto> Occupied { get; init; } = [];
}
