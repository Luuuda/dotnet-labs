using MongoDB.Bson;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Common.Interfaces;

/// <summary>Критерії вибірки відгуків — усе виконується засобами MongoDB ($match, $text, sort, skip/limit).</summary>
public sealed record ReviewFilter(
    int? MovieId = null,
    string? UserId = null,
    int? MinRating = null,
    int? MaxRating = null,
    bool? VerifiedOnly = null,
    string? Search = null,
    string SortBy = "newest");

/// <summary>
/// Читання відгуків (сторона Query у CQRS), як IOrderQueries в eShop.
/// Обробники запитів звертаються сюди, а не до IMongoCollection; реалізація в Infrastructure
/// проєктує документи одразу в DTO і вибирає з БД лише потрібні поля.
/// </summary>
public interface IReviewQueries
{
    Task<ReviewDetailsDto?> GetByIdAsync(ObjectId id, CancellationToken ct);
    Task<PagedResult<ReviewDto>> GetListAsync(ReviewFilter filter, int page, int pageSize, CancellationToken ct);
    Task<CursorPage<ReviewDto>> GetFeedAsync(int? movieId, ObjectId? after, int limit, CancellationToken ct);
    Task<MovieRatingDto?> GetMovieRatingAsync(int movieId, CancellationToken ct);
}

public interface IDiscussionQueries
{
    Task<DiscussionDto?> GetByIdAsync(ObjectId id, CancellationToken ct);
    Task<PagedResult<DiscussionDto>> GetListAsync(int? movieId, string? status, string? tag, int page, int pageSize, CancellationToken ct);
}
