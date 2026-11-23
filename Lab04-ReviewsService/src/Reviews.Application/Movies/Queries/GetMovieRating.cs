using FluentValidation;
using MediatR;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;

namespace Reviews.Application.Movies.Queries;

/// <summary>Рейтинг фільму, обчислений aggregation pipeline за відгуками.</summary>
public sealed record GetMovieRatingQuery(int MovieId) : IQuery<MovieRatingDto>;

public sealed class GetMovieRatingQueryValidator : AbstractValidator<GetMovieRatingQuery>
{
    public GetMovieRatingQueryValidator() => RuleFor(x => x.MovieId).GreaterThan(0);
}

public sealed class GetMovieRatingQueryHandler(IReviewQueries queries) : IRequestHandler<GetMovieRatingQuery, MovieRatingDto>
{
    public async Task<MovieRatingDto> Handle(GetMovieRatingQuery q, CancellationToken ct)
        => await queries.GetMovieRatingAsync(q.MovieId, ct)
           ?? throw new NotFoundException("Відгуки на фільм", q.MovieId);
}
