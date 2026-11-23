using Ardalis.Specification;
using CatalogService.Bll.Dtos;
using CatalogService.Domain.Entities;

namespace CatalogService.Bll.Specifications;

/// <summary>
/// Специфікація списку фільмів: критерії (Where), Include, сортування й пагінація в одному класі.
/// Не залежить від EF Core — тестується окремо (in-memory Evaluate). Репозиторій лише застосовує її
/// через WithSpecification(spec), і EF будує ОДИН SQL з WHERE / ORDER BY / OFFSET-FETCH.
/// </summary>
public sealed class MoviesFilterSpec : Specification<Movie>
{
    public MoviesFilterSpec(MovieQuery q, int page, int pageSize)
    {
        Query.AsNoTracking()
             .Include(m => m.MovieGenres).ThenInclude(mg => mg.Genre)
             .AsSplitQuery();   // жанри — окремим запитом, щоб пагінація рахувала фільми, а не рядки JOIN

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var search = q.Search.Trim();
            Query.Where(m => m.Title.Contains(search) || (m.OriginalTitle != null && m.OriginalTitle.Contains(search)));
        }
        if (!string.IsNullOrWhiteSpace(q.Genre))
            Query.Where(m => m.MovieGenres.Any(mg => mg.Genre.Slug == q.Genre));
        if (q.YearFrom is not null) Query.Where(m => m.ReleaseYear >= q.YearFrom);
        if (q.YearTo is not null) Query.Where(m => m.ReleaseYear <= q.YearTo);
        if (q.AgeRating is not null) Query.Where(m => m.AgeRating == q.AgeRating);
        if (q.MinDuration is not null) Query.Where(m => m.DurationMinutes >= q.MinDuration);
        if (q.MaxDuration is not null) Query.Where(m => m.DurationMinutes <= q.MaxDuration);

        // сортування — лише за дозволеними полями (перевіряє MovieQueryValidator); Id — для стабільної пагінації
        var desc = q.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase);
        switch (q.SortBy.ToLowerInvariant())
        {
            case "year":
                if (desc) Query.OrderByDescending(m => m.ReleaseYear).ThenBy(m => m.Id);
                else Query.OrderBy(m => m.ReleaseYear).ThenBy(m => m.Id);
                break;
            case "duration":
                if (desc) Query.OrderByDescending(m => m.DurationMinutes).ThenBy(m => m.Id);
                else Query.OrderBy(m => m.DurationMinutes).ThenBy(m => m.Id);
                break;
            case "rating":
                if (desc) Query.OrderByDescending(m => m.AverageRating).ThenBy(m => m.Id);
                else Query.OrderBy(m => m.AverageRating).ThenBy(m => m.Id);
                break;
            case "created":
                if (desc) Query.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Id);
                else Query.OrderBy(m => m.CreatedAt).ThenBy(m => m.Id);
                break;
            default:
                if (desc) Query.OrderByDescending(m => m.Title).ThenBy(m => m.Id);
                else Query.OrderBy(m => m.Title).ThenBy(m => m.Id);
                break;
        }

        Query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
