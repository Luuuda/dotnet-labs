namespace Reviews.Application.Common.Models;

/// <summary>Сторінка результатів (skip/limit) з метаданими пагінації.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, long TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

/// <summary>
/// Курсорна сторінка: наступна сторінка починається ПІСЛЯ останнього отриманого документа
/// (за _id), без skip — швидкість не залежить від «номера сторінки».
/// </summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);

public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}
