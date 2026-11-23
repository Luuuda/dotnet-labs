using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;

namespace Reviews.Application.Reviews.Queries;

// ---------- відгук за Id ----------
public sealed record GetReviewByIdQuery(string Id) : IQuery<ReviewDetailsDto>;

public sealed class GetReviewByIdQueryValidator : AbstractValidator<GetReviewByIdQuery>
{
    public GetReviewByIdQueryValidator() => RuleFor(x => x.Id).MustBeObjectId();
}

public sealed class GetReviewByIdQueryHandler(IReviewQueries queries) : IRequestHandler<GetReviewByIdQuery, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(GetReviewByIdQuery q, CancellationToken ct)
        => await queries.GetByIdAsync(ObjectId.Parse(q.Id), ct) ?? throw new NotFoundException("Відгук", q.Id);
}

// ---------- список з фільтрами, пошуком, сортуванням і skip/limit ----------
public sealed record GetReviewsListQuery(
    int? MovieId = null,
    string? UserId = null,
    int? MinRating = null,
    int? MaxRating = null,
    bool? VerifiedOnly = null,
    string? Search = null,
    string? SortBy = null,
    int Page = 1,
    int PageSize = Paging.DefaultPageSize) : IQuery<PagedResult<ReviewDto>>;

public sealed class GetReviewsListQueryValidator : AbstractValidator<GetReviewsListQuery>
{
    public static readonly string[] SortFields = ["newest", "oldest", "rating", "rating_asc", "comments"];

    public GetReviewsListQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MinRating).InclusiveBetween(1, 5);
        RuleFor(x => x.MaxRating).InclusiveBetween(1, 5);
        RuleFor(x => x).Must(x => x.MinRating is null || x.MaxRating is null || x.MinRating <= x.MaxRating)
            .OverridePropertyName("MinRating").WithMessage("MinRating не може бути більшим за MaxRating");
        RuleFor(x => x.Search).MaximumLength(100);
        RuleFor(x => x.SortBy)
            .Must(s => s is null || SortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"Сортування лише за: {string.Join(", ", SortFields)}");
    }
}

public sealed class GetReviewsListQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewsListQuery, PagedResult<ReviewDto>>
{
    public Task<PagedResult<ReviewDto>> Handle(GetReviewsListQuery q, CancellationToken ct)
        => queries.GetListAsync(
            new ReviewFilter(q.MovieId, q.UserId, q.MinRating, q.MaxRating, q.VerifiedOnly,
                             string.IsNullOrWhiteSpace(q.Search) ? null : q.Search.Trim(),
                             (q.SortBy ?? "newest").ToLowerInvariant()),
            q.Page, Math.Clamp(q.PageSize, 1, Paging.MaxPageSize), ct);
}

// ---------- курсорна стрічка ----------
public sealed record GetReviewsFeedQuery(int? MovieId = null, string? After = null, int Limit = Paging.DefaultPageSize)
    : IQuery<CursorPage<ReviewDto>>;

public sealed class GetReviewsFeedQueryValidator : AbstractValidator<GetReviewsFeedQuery>
{
    public GetReviewsFeedQueryValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.Limit).InclusiveBetween(1, Paging.MaxPageSize);
        RuleFor(x => x.After).MustBeObjectId().When(x => x.After is not null);
    }
}

public sealed class GetReviewsFeedQueryHandler(IReviewQueries queries)
    : IRequestHandler<GetReviewsFeedQuery, CursorPage<ReviewDto>>
{
    public Task<CursorPage<ReviewDto>> Handle(GetReviewsFeedQuery q, CancellationToken ct)
        => queries.GetFeedAsync(q.MovieId, q.After is null ? null : ObjectId.Parse(q.After), q.Limit, ct);
}
