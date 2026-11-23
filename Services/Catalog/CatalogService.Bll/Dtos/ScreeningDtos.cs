using System.ComponentModel.DataAnnotations;

namespace CatalogService.Bll.Dtos;

/// <summary>Параметри розкладу: друга специфікація з іншими критеріями (дата, фільм, зала, формат, ціна).</summary>
public sealed class ScreeningQuery
{
    [Range(1, int.MaxValue)] public int Page { get; init; } = 1;
    [Range(1, int.MaxValue)] public int PageSize { get; init; } = Paging.DefaultPageSize;
    public DateTime? From { get; init; }
    public DateTime? To { get; init; }
    public int? MovieId { get; init; }
    public int? HallId { get; init; }
    /// <summary>2D | 3D | IMAX | 4DX</summary>
    public string? Format { get; init; }
    public decimal? MaxPrice { get; init; }
    public bool IncludeCancelled { get; init; }
    /// <summary>startsAt | price | movie</summary>
    public string SortBy { get; init; } = "startsAt";
    /// <summary>asc | desc</summary>
    public string SortDir { get; init; } = "asc";
}

public sealed record ScreeningDto
{
    public long Id { get; init; }
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public int HallId { get; init; }
    public string HallName { get; init; } = "";
    public DateTime StartsAt { get; init; }
    public DateTime EndsAt { get; init; }
    public string Format { get; init; } = "";
    public string Language { get; init; } = "";
    public decimal BasePrice { get; init; }
    public bool IsCancelled { get; init; }
    public string RowVersion { get; init; } = "";
}

public sealed record CreateScreeningDto
{
    public int MovieId { get; init; }
    public int HallId { get; init; }
    public DateTime StartsAt { get; init; }
    [Required] public string Format { get; init; } = "2D";
    [StringLength(30)] public string Language { get; init; } = "українська";
    public decimal BasePrice { get; init; }
}

public sealed record ChangePriceDto
{
    public decimal BasePrice { get; init; }
}
