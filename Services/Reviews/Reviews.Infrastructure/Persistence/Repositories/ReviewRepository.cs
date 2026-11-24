using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;

namespace Reviews.Infrastructure.Persistence.Repositories;

public sealed class ReviewRepository(MongoDbContext context)
    : MongoRepository<Review>(context.Reviews), IReviewRepository
{
    public Task<bool> ExistsAsync(int movieId, string userId, CancellationToken ct = default)
        => Collection.Find(Filter.Eq(r => r.MovieId, movieId) & Filter.Eq(r => r.Author.UserId, userId))
                     .AnyAsync(ct);                           // покривається унікальним індексом ux_author_movie

    public async Task<long> RenameMovieAsync(int movieId, string movieTitle, CancellationToken ct = default)
    {
        var update = Builders<Review>.Update
            .Set(r => r.MovieTitle, movieTitle)
            .Set(r => r.UpdatedAt, DateTime.UtcNow)
            .Inc(r => r.Version, 1);                          // масова зміна теж змінює версію документів
        var result = await Collection.UpdateManyAsync(
            Filter.Eq(r => r.MovieId, movieId) & Filter.Ne(r => r.MovieTitle, movieTitle), update, cancellationToken: ct);
        return result.ModifiedCount;
    }
}

public sealed class DiscussionRepository(MongoDbContext context)
    : MongoRepository<Discussion>(context.Discussions), IDiscussionRepository
{
    /// <summary>Лаб. 08: updateMany за movieId; документи, де назва вже нова, не змінюються (повтор — no-op).</summary>
    public async Task<long> RenameMovieAsync(int movieId, string movieTitle, CancellationToken ct = default)
    {
        var update = Builders<Discussion>.Update
            .Set(d => d.MovieTitle, movieTitle)
            .Set(d => d.UpdatedAt, DateTime.UtcNow)
            .Inc(d => d.Version, 1);
        var result = await Collection.UpdateManyAsync(
            Builders<Discussion>.Filter.Eq(d => d.MovieId, movieId) & Builders<Discussion>.Filter.Ne(d => d.MovieTitle, movieTitle),
            update, cancellationToken: ct);
        return result.ModifiedCount;
    }
}
