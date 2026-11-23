namespace CatalogService.Bll.Dtos;

/// <summary>
/// Контракт відповіді для колекцій. Метадані — у ТІЛІ відповіді (items, totalCount, page, pageSize,
/// totalPages, hasNext, hasPrevious) і дублюються в заголовках X-Total-Count та X-Pagination (для кешування / проксі).
/// </summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
    public bool HasNext => Page < TotalPages;
    public bool HasPrevious => Page > 1;
}

/// <summary>Спільні правила пагінації: page — з 1, pageSize — за замовчуванням 20, максимум 100.</summary>
public static class Paging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public static (int Page, int PageSize) Normalize(int page, int pageSize)
        => (Math.Max(1, page), Math.Clamp(pageSize, 1, MaxPageSize));
}
