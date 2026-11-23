using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;

namespace Reviews.Application.Discussions.Queries;

public sealed record GetDiscussionByIdQuery(string Id) : IQuery<DiscussionDto>;

public sealed class GetDiscussionByIdQueryValidator : AbstractValidator<GetDiscussionByIdQuery>
{
    public GetDiscussionByIdQueryValidator() => RuleFor(x => x.Id).MustBeObjectId();
}

public sealed class GetDiscussionByIdQueryHandler(IDiscussionQueries queries) : IRequestHandler<GetDiscussionByIdQuery, DiscussionDto>
{
    public async Task<DiscussionDto> Handle(GetDiscussionByIdQuery q, CancellationToken ct)
        => await queries.GetByIdAsync(ObjectId.Parse(q.Id), ct) ?? throw new NotFoundException("Тема", q.Id);
}

public sealed record GetDiscussionsListQuery(int? MovieId = null, string? Status = null, string? Tag = null,
    int Page = 1, int PageSize = Paging.DefaultPageSize) : IQuery<PagedResult<DiscussionDto>>;

public sealed class GetDiscussionsListQueryValidator : AbstractValidator<GetDiscussionsListQuery>
{
    public GetDiscussionsListQueryValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.Status).Must(s => s is null or "open" or "closed").WithMessage("Status: open або closed");
        RuleFor(x => x.Tag).MaximumLength(30);
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize);
    }
}

public sealed class GetDiscussionsListQueryHandler(IDiscussionQueries queries)
    : IRequestHandler<GetDiscussionsListQuery, PagedResult<DiscussionDto>>
{
    public Task<PagedResult<DiscussionDto>> Handle(GetDiscussionsListQuery q, CancellationToken ct)
        => queries.GetListAsync(q.MovieId, q.Status, q.Tag?.Trim().ToLowerInvariant(), q.Page,
                                Math.Clamp(q.PageSize, 1, Paging.MaxPageSize), ct);
}
