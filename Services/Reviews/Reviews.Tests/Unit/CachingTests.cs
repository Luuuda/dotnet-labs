using MediatR;
using Reviews.Application.Caching;
using Reviews.Application.Movies.Queries;
using Reviews.Application.Common.Models;

namespace Reviews.Tests.Unit;

/// <summary>Лаб. 06: CachingBehavior (cache-aside у pipeline MediatR) та інвалідація при записі.</summary>
public sealed class CachingTests
{
    private static MovieRatingDto Rating(double avg) => new(3, "Дюна", avg, 2, new Dictionary<string, int> { ["5"] = 1 }, 1);

    [Fact]
    public async Task Repeated_rating_query_does_not_reach_the_handler()
    {
        var cache = TestCache.Create();
        var behavior = new CachingBehavior<GetMovieRatingQuery, MovieRatingDto>(cache);
        var calls = 0;
        RequestHandlerDelegate<MovieRatingDto> handler = _ => { calls++; return Task.FromResult(Rating(4.5)); };

        await behavior.Handle(new GetMovieRatingQuery(3), handler, CancellationToken.None);
        var second = await behavior.Handle(new GetMovieRatingQuery(3), handler, CancellationToken.None);

        Assert.Equal(1, calls);
        Assert.Equal(4.5, second.Average);
    }

    [Fact]
    public async Task New_review_invalidates_rating_of_its_movie()
    {
        var cache = TestCache.Create();
        var behavior = new CachingBehavior<GetMovieRatingQuery, MovieRatingDto>(cache);
        await behavior.Handle(new GetMovieRatingQuery(3), _ => Task.FromResult(Rating(4.5)), CancellationToken.None);

        await TestCache.Invalidator(cache).MovieChangedAsync(3, "ReviewCreated", CancellationToken.None);
        var fresh = await behavior.Handle(new GetMovieRatingQuery(3), _ => Task.FromResult(Rating(3.0)), CancellationToken.None);

        Assert.Equal(3.0, fresh.Average);
    }

    [Fact]
    public void Keys_are_predictable_and_prefixed_by_service()
    {
        Assert.Equal("reviews:movie:3:rating", CacheKeys.MovieRating(3));
        Assert.StartsWith("reviews:movie:3:list:v", CacheKeys.List(3, "1", "abc"));
        Assert.True(CachePolicy.Rating.L1Absolute < CachePolicy.Rating.L2Absolute);
    }
}
