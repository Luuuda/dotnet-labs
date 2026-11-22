using Ardalis.Specification;
using CatalogService.Bll.Dtos;
using CatalogService.Domain.Entities;

namespace CatalogService.Bll.Specifications;

/// <summary>
/// ДРУГА специфікація з іншими критеріями (розклад сеансів). Працює через той самий
/// GenericRepository.ListAsync / CountAsync (WithSpecification) — без жодних змін у репозиторії.
/// </summary>
public sealed class ScreeningsFilterSpec : Specification<Screening>
{
    public ScreeningsFilterSpec(ScreeningQuery q, int page, int pageSize)
    {
        Query.AsNoTracking().Include(s => s.Movie).Include(s => s.Hall);

        if (!q.IncludeCancelled) Query.Where(s => !s.IsCancelled);
        if (q.From is not null) Query.Where(s => s.StartsAt >= q.From);
        if (q.To is not null) Query.Where(s => s.StartsAt < q.To);
        if (q.MovieId is not null) Query.Where(s => s.MovieId == q.MovieId);
        if (q.HallId is not null) Query.Where(s => s.HallId == q.HallId);
        if (q.Format is not null) Query.Where(s => s.Format == q.Format);
        if (q.MaxPrice is not null) Query.Where(s => s.BasePrice <= q.MaxPrice);

        var desc = q.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        switch (q.SortBy.ToLowerInvariant())
        {
            case "price":
                if (desc) Query.OrderByDescending(s => s.BasePrice).ThenBy(s => s.StartsAt);
                else Query.OrderBy(s => s.BasePrice).ThenBy(s => s.StartsAt);
                break;
            case "movie":
                if (desc) Query.OrderByDescending(s => s.Movie.Title).ThenBy(s => s.StartsAt);
                else Query.OrderBy(s => s.Movie.Title).ThenBy(s => s.StartsAt);
                break;
            default:
                if (desc) Query.OrderByDescending(s => s.StartsAt).ThenBy(s => s.Id);
                else Query.OrderBy(s => s.StartsAt).ThenBy(s => s.Id);
                break;
        }

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}

/// <summary>Сеанс за Id разом із фільмом і залою (для відповіді API).</summary>
public sealed class ScreeningByIdSpec : Specification<Screening>, ISingleResultSpecification<Screening>
{
    public ScreeningByIdSpec(long id) =>
        Query.Where(s => s.Id == id).Include(s => s.Movie).Include(s => s.Hall);
}

/// <summary>Чи є в фільму майбутні активні сеанси (бізнес-правило видалення фільму).</summary>
public sealed class ActiveScreeningsOfMovieSpec : Specification<Screening>
{
    public ActiveScreeningsOfMovieSpec(int movieId, DateTime from) =>
        Query.Where(s => s.MovieId == movieId && !s.IsCancelled && s.StartsAt >= from);
}
