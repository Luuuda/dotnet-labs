using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Models;
using Reviews.Infrastructure.Persistence.ReadModels;

namespace Reviews.Infrastructure.Persistence.Queries;

/// <summary>
/// Сторона читання: aggregation pipeline ($text / $match → $sort → $skip → $limit → $project),
/// з БД вибираються лише потрібні поля (у списку — без масиву comments), результат одразу перетворюється на DTO.
/// </summary>
public sealed class ReviewQueries(MongoDbContext context) : IReviewQueries
{
    private static readonly FilterDefinitionBuilder<ReviewDocument> F = Builders<ReviewDocument>.Filter;
    private static readonly SortDefinitionBuilder<ReviewDocument> S = Builders<ReviewDocument>.Sort;
    private static readonly ProjectionDefinition<ReviewDocument, ReviewDocument> ListProjection =
        new BsonDocumentProjectionDefinition<ReviewDocument, ReviewDocument>(new BsonDocument("comments", 0));

    private IMongoCollection<ReviewDocument> Reviews => context.ReviewDocuments;

    public async Task<ReviewDetailsDto?> GetByIdAsync(ObjectId id, CancellationToken ct)
    {
        var doc = await Reviews.Find(F.Eq(d => d.Id, id)).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDetailsDto(doc);
    }

    public async Task<PagedResult<ReviewDto>> GetListAsync(ReviewFilter filter, int page, int pageSize, CancellationToken ct)
    {
        var match = BuildFilter(filter);
        var total = await Reviews.CountDocumentsAsync(match, cancellationToken: ct);

        var docs = await Reviews.Aggregate()
            .Match(match)                                   // $match (з $text, якщо є пошук — першою стадією)
            .Sort(BuildSort(filter.SortBy))                 // сортування підкріплене індексами ix_movie_*
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .Project(ListProjection)                        // $project: без вкладених коментарів
            .ToListAsync(ct);

        return new PagedResult<ReviewDto>(docs.Select(ToDto).ToList(), total, page, pageSize);
    }

    public async Task<CursorPage<ReviewDto>> GetFeedAsync(int? movieId, ObjectId? after, int limit, CancellationToken ct)
    {
        var filter = F.Empty;
        if (movieId is { } m) filter &= F.Eq(d => d.MovieId, m);
        if (after is { } cursor) filter &= F.Lt(d => d.Id, cursor);   // ObjectId зростає з часом створення

        var docs = await Reviews.Find(filter)
            .Sort(S.Descending(d => d.Id))
            .Limit(limit + 1)                                // +1 — щоб знати, чи є наступна сторінка
            .Project(ListProjection)
            .ToListAsync(ct);

        var hasMore = docs.Count > limit;
        var items = docs.Take(limit).Select(ToDto).ToList();
        return new CursorPage<ReviewDto>(items, hasMore ? items[^1].Id : null, hasMore);
    }

    public async Task<MovieRatingDto?> GetMovieRatingAsync(int movieId, CancellationToken ct)
    {
        BsonDocument CountOf(int score) => new("$sum",
            new BsonDocument("$cond", new BsonArray { new BsonDocument("$eq", new BsonArray { "$rating", score }), 1, 0 }));

        var pipeline = new[]
        {
            new BsonDocument("$match", new BsonDocument("movieId", movieId)),
            new BsonDocument("$group", new BsonDocument
            {
                { "_id", "$movieId" },
                { "movieTitle", new BsonDocument("$last", "$movieTitle") },
                { "average", new BsonDocument("$avg", "$rating") },
                { "count", new BsonDocument("$sum", 1) },
                { "verified", new BsonDocument("$sum", new BsonDocument("$cond", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$verifiedViewer", false }), 1, 0 })) },
                { "r1", CountOf(1) }, { "r2", CountOf(2) }, { "r3", CountOf(3) }, { "r4", CountOf(4) }, { "r5", CountOf(5) }
            })
        };

        using var cursor = await context.RawReviews.AggregateAsync<BsonDocument>(pipeline, cancellationToken: ct);
        var result = await cursor.FirstOrDefaultAsync(ct);
        if (result is null) return null;

        var distribution = Enumerable.Range(1, 5).ToDictionary(i => i.ToString(), i => result[$"r{i}"].ToInt32());
        return new MovieRatingDto(movieId, result["movieTitle"].AsString,
            Math.Round(result["average"].ToDouble(), 2), result["count"].ToInt32(), distribution, result["verified"].ToInt32());
    }

    private static FilterDefinition<ReviewDocument> BuildFilter(ReviewFilter f)
    {
        var filter = F.Empty;
        if (!string.IsNullOrWhiteSpace(f.Search))
            filter &= F.Text(f.Search, new TextSearchOptions { CaseSensitive = false });   // текстовий індекс tx_text
        if (f.MovieId is { } movieId) filter &= F.Eq(d => d.MovieId, movieId);
        if (!string.IsNullOrWhiteSpace(f.UserId)) filter &= F.Eq(d => d.Author.UserId, f.UserId);
        if (f.MinRating is { } min) filter &= F.Gte(d => d.Rating, min);
        if (f.MaxRating is { } max) filter &= F.Lte(d => d.Rating, max);
        if (f.VerifiedOnly == true) filter &= F.Eq(d => d.VerifiedViewer, true);
        return filter;
    }

    private static SortDefinition<ReviewDocument> BuildSort(string sortBy) => sortBy switch
    {
        "oldest" => S.Ascending(d => d.CreatedAt).Ascending(d => d.Id),
        "rating" => S.Descending(d => d.Rating).Descending(d => d.CreatedAt).Descending(d => d.Id),
        "rating_asc" => S.Ascending(d => d.Rating).Descending(d => d.CreatedAt).Descending(d => d.Id),
        "comments" => S.Descending(d => d.CommentsCount).Descending(d => d.CreatedAt).Descending(d => d.Id),
        _ => S.Descending(d => d.CreatedAt).Descending(d => d.Id)
    };

    private static AuthorDto ToDto(AuthorDocument a) => new(a.UserId, a.DisplayName);

    internal static ReviewDto ToDto(ReviewDocument d) => new()
    {
        Id = d.Id.ToString(),
        MovieId = d.MovieId,
        MovieTitle = d.MovieTitle,
        Author = ToDto(d.Author),
        Rating = d.Rating,
        Text = d.Text,
        Aspects = d.Aspects,
        VerifiedViewer = d.VerifiedViewer,
        CommentsCount = d.CommentsCount > 0 ? d.CommentsCount : d.Comments?.Count ?? 0,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        Version = d.Version
    };

    private static ReviewDetailsDto ToDetailsDto(ReviewDocument d) => new()
    {
        Id = d.Id.ToString(),
        MovieId = d.MovieId,
        MovieTitle = d.MovieTitle,
        Author = ToDto(d.Author),
        Rating = d.Rating,
        Text = d.Text,
        Aspects = d.Aspects,
        VerifiedViewer = d.VerifiedViewer,
        CommentsCount = d.Comments?.Count ?? 0,
        Comments = (d.Comments ?? []).OrderBy(c => c.CreatedAt)
            .Select(c => new CommentDto { Id = c.Id.ToString(), Author = ToDto(c.Author), Text = c.Text, CreatedAt = c.CreatedAt })
            .ToList(),
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        Version = d.Version
    };
}

public sealed class DiscussionQueries(MongoDbContext context) : IDiscussionQueries
{
    private static readonly FilterDefinitionBuilder<DiscussionDocument> F = Builders<DiscussionDocument>.Filter;

    public async Task<DiscussionDto?> GetByIdAsync(ObjectId id, CancellationToken ct)
    {
        var doc = await context.DiscussionDocuments.Find(F.Eq(d => d.Id, id)).FirstOrDefaultAsync(ct);
        return doc is null ? null : ToDto(doc);
    }

    public async Task<PagedResult<DiscussionDto>> GetListAsync(int? movieId, string? status, string? tag,
        int page, int pageSize, CancellationToken ct)
    {
        var filter = F.Empty;
        if (movieId is { } m) filter &= F.Eq(d => d.MovieId, m);
        if (!string.IsNullOrWhiteSpace(status)) filter &= F.Eq(d => d.Status, status);
        if (!string.IsNullOrWhiteSpace(tag)) filter &= F.AnyEq(d => d.Tags, tag);

        var collection = context.DiscussionDocuments;
        var total = await collection.CountDocumentsAsync(filter, cancellationToken: ct);
        var docs = await collection.Aggregate()
            .Match(filter)
            .Sort(Builders<DiscussionDocument>.Sort.Descending(d => d.LastActivityAt).Descending(d => d.Id))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync(ct);

        return new PagedResult<DiscussionDto>(docs.Select(ToDto).ToList(), total, page, pageSize);
    }

    private static DiscussionDto ToDto(DiscussionDocument d) => new()
    {
        Id = d.Id.ToString(),
        MovieId = d.MovieId,
        MovieTitle = d.MovieTitle,
        Title = d.Title,
        CreatedBy = new AuthorDto(d.CreatedBy.UserId, d.CreatedBy.DisplayName),
        Status = d.Status,
        Tags = d.Tags ?? [],
        PostsCount = d.PostsCount,
        CreatedAt = d.CreatedAt,
        LastActivityAt = d.LastActivityAt,
        ClosedAt = d.ClosedAt,
        ClosedReason = d.ClosedReason,
        Version = d.Version
    };
}
