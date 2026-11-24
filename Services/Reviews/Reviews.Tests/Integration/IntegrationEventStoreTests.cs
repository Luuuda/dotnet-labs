using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence;
using Reviews.Infrastructure.Persistence.Repositories;

namespace Reviews.Tests.Integration;

/// <summary>Лаб. 08: ідемпотентність споживача MovieRenamed на справжній MongoDB.</summary>
[Collection("mongo")]
public sealed class IntegrationEventStoreTests(MongoFixture fixture)
{
    [MongoFact]
    public async Task EventId_is_recorded_once_and_second_mark_is_harmless()
    {
        var store = new IntegrationEventStore(fixture.Context);
        var eventId = Guid.NewGuid();

        Assert.False(await store.IsProcessedAsync(eventId));
        await store.MarkProcessedAsync(eventId, "MovieRenamedEvent");
        await store.MarkProcessedAsync(eventId, "MovieRenamedEvent");       // повторна доставка — без помилки

        Assert.True(await store.IsProcessedAsync(eventId));
    }

    [MongoFact]
    public async Task Older_rename_is_rejected_newer_and_same_time_are_accepted()
    {
        var store = new IntegrationEventStore(fixture.Context);
        var movieId = Random.Shared.Next(100_000, 900_000);
        var t = DateTime.UtcNow;

        Assert.True(await store.TryAdvanceMovieTitleAsync(movieId, t));
        Assert.True(await store.TryAdvanceMovieTitleAsync(movieId, t));                 // повтор тієї ж події
        Assert.False(await store.TryAdvanceMovieTitleAsync(movieId, t.AddSeconds(-5))); // застаріла
        Assert.True(await store.TryAdvanceMovieTitleAsync(movieId, t.AddSeconds(5)));   // новіша
    }

    [MongoFact]
    public async Task UpdateMany_renames_movie_in_all_reviews_and_discussions_and_repeat_changes_nothing()
    {
        var movieId = Random.Shared.Next(100_000, 900_000);
        var reviews = new ReviewRepository(fixture.Context);
        var discussions = new DiscussionRepository(fixture.Context);
        for (var i = 0; i < 3; i++)
            await reviews.AddAsync(new Review(movieId, "Стара назва", new Author($"u-{Guid.NewGuid():N}", "Глядач"), new Rating(4), null));
        await discussions.AddAsync(new Discussion(movieId, "Стара назва", "Обговорення фіналу", new Author("u-1", "Глядач")));

        Assert.Equal(3, await reviews.RenameMovieAsync(movieId, "Нова назва"));
        Assert.Equal(1, await discussions.RenameMovieAsync(movieId, "Нова назва"));
        Assert.Equal(0, await reviews.RenameMovieAsync(movieId, "Нова назва"));         // ідемпотентно

        var titles = await fixture.Context.Reviews.Find(r => r.MovieId == movieId).Project(r => r.MovieTitle).ToListAsync();
        Assert.All(titles, t => Assert.Equal("Нова назва", t));
    }
}
