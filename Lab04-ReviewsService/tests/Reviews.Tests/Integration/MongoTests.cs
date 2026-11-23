using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence.Queries;
using Reviews.Infrastructure.Persistence.Repositories;
using Reviews.Infrastructure.Seeding;

namespace Reviews.Tests.Integration;

[Collection("mongo")]
public sealed class MongoTests(MongoFixture fixture)
{
    private static Author NewAuthor() => new($"test-{Guid.NewGuid():N}", "Тестовий глядач");

    [MongoFact]
    public async Task Document_has_lab01_structure_camelCase_nested_author_and_comments_without_dotnet_internals()
    {
        var repository = new ReviewRepository(fixture.Context);
        var review = new Review(7, "Інтерстеллар", NewAuthor(), new Rating(5), "Космос!",
            new ReviewAspects(new Dictionary<string, int> { ["visuals"] = 5 }));
        review.AddComment(new Author("u-2", "Андрій"), "Згоден");
        await repository.AddAsync(review);

        var raw = await fixture.Context.RawReviews.Find(new BsonDocument("_id", review.Id)).SingleAsync();
        var names = raw.Names.ToList();

        Assert.Equal("_id", names[0]);
        Assert.Equal(
            new[] { "_id", "aspects", "author", "comments", "commentsCount", "createdAt", "movieId", "movieTitle",
                    "rating", "text", "updatedAt", "verifiedViewer", "version" },
            names.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(BsonType.Int32, raw["rating"].BsonType);                       // VO Rating → просте число
        Assert.Equal("Тестовий глядач", raw["author"]["displayName"].AsString);     // VO Author → вкладений документ
        var comment = raw["comments"][0].AsBsonDocument;
        Assert.Equal(new[] { "_id", "author", "text", "createdAt" }, comment.Names.ToArray());
        Assert.Equal(BsonType.ObjectId, comment["_id"].BsonType);
        Assert.DoesNotContain(names, n => n.StartsWith('_') && n != "_id");          // жодних _comments, _t, _extraElements
    }

    [MongoFact]
    public async Task Document_round_trips_through_value_object_constructors()
    {
        var repository = new ReviewRepository(fixture.Context);
        var review = new Review(7, "Інтерстеллар", NewAuthor(), new Rating(4), "Текст");
        review.AddComment(new Author("u-3", "Марія"), "Коментар");
        await repository.AddAsync(review);

        var loaded = await repository.GetByIdAsync(review.Id);

        var untouched = new Review(7, "Інтерстеллар", NewAuthor(), new Rating(5), "Без змін");
        await repository.AddAsync(untouched);
        var raw = await fixture.Context.RawReviews.Find(new BsonDocument("_id", untouched.Id)).SingleAsync();
        Assert.False(raw.Contains("updatedAt"));                                     // null-поля не пишуться
        Assert.False(raw.Contains("aspects"));

        Assert.NotNull(loaded);
        Assert.Equal(review.Author, loaded.Author);
        Assert.Equal(new Rating(4), loaded.Rating);
        Assert.Equal("Марія", Assert.Single(loaded.Comments).Author.DisplayName);
        Assert.Equal(1, loaded.CommentsCount);
    }

    [MongoFact]
    public async Task Flexible_schema_fields_from_lab01_survive_an_update()
    {
        var id = ObjectId.GenerateNewId();
        await fixture.Context.RawReviews.InsertOneAsync(new BsonDocument
        {
            { "_id", id }, { "movieId", 6 }, { "movieTitle", "Захар Беркут" },
            { "author", new BsonDocument { { "userId", $"legacy-{id}" }, { "displayName", "Тарас" } } },
            { "rating", 4 }, { "spoiler", true }, { "tags", new BsonArray { "саундтрек" } },
            { "createdAt", DateTime.UtcNow }                                     // без version — як у скриптах Лаб. 01
        });

        var repository = new ReviewRepository(fixture.Context);
        var review = (await repository.GetByIdAsync(id))!;
        Assert.Equal(0, review.Version);
        Assert.True(review.AdditionalFields.ContainsKey("spoiler"));

        review.Edit(new Rating(5), "Передумав — це шедевр", null);
        await repository.UpdateAsync(review);

        var raw = await fixture.Context.RawReviews.Find(new BsonDocument("_id", id)).SingleAsync();
        Assert.True(raw["spoiler"].AsBoolean);
        Assert.Equal("саундтрек", raw["tags"][0].AsString);
        Assert.Equal(1, raw["version"].AsInt32);
        Assert.Equal(5, raw["rating"].AsInt32);
    }

    [MongoFact]
    public async Task Concurrent_update_with_old_version_is_conflict()
    {
        var repository = new ReviewRepository(fixture.Context);
        var review = new Review(7, "Інтерстеллар", NewAuthor(), 3, "Текст");
        await repository.AddAsync(review);

        var first = (await repository.GetByIdAsync(review.Id))!;
        var second = (await repository.GetByIdAsync(review.Id))!;

        first.Edit(new Rating(4), "Перший редактор", null);
        await repository.UpdateAsync(first);                                       // версія 0 → 1

        second.Edit(new Rating(1), "Другий редактор", null);
        await Assert.ThrowsAsync<ConflictException>(() => repository.UpdateAsync(second));   // очікував 0, а в базі 1

        var stored = (await repository.GetByIdAsync(review.Id))!;
        Assert.Equal("Перший редактор", stored.Text);
        Assert.Equal(1, stored.Version);
    }

    [MongoFact]
    public async Task Unique_index_rejects_second_review_of_same_user_on_same_movie()
    {
        var repository = new ReviewRepository(fixture.Context);
        var author = NewAuthor();
        await repository.AddAsync(new Review(5, "Головоломка 2", author, 4, "Перший"));

        Assert.True(await repository.ExistsAsync(5, author.UserId));
        var ex = await Assert.ThrowsAsync<MongoWriteException>(() =>
            repository.AddAsync(new Review(5, "Головоломка 2", author, 2, "Другий")));
        Assert.Equal(ServerErrorCategory.DuplicateKey, ex.WriteError.Category);
    }

    [MongoFact]
    public async Task Indexes_are_created_and_recreation_is_safe()
    {
        await new IndexCreationService(fixture.Context, NullLogger<IndexCreationService>.Instance).CreateAsync();   // вдруге

        var names = (await (await fixture.Context.Reviews.Indexes.ListAsync()).ToListAsync())
            .Select(i => i["name"].AsString).ToList();
        Assert.Equal(new[] { "_id_", "ix_movie_createdAt", "ux_author_movie", "tx_text", "ix_movie_rating" }, names.ToArray());

        var discussionIndexes = (await (await fixture.Context.Discussions.Indexes.ListAsync()).ToListAsync())
            .Select(i => i["name"].AsString).ToList();
        Assert.Contains("ix_movie_activity", discussionIndexes);
    }

    [MongoFact]
    public async Task Seeding_is_idempotent()
    {
        var before = await fixture.Context.Reviews.CountDocumentsAsync(FilterDefinition<Review>.Empty);
        Assert.Equal(0, await new ReviewSeeder(fixture.Context, NullLogger<ReviewSeeder>.Instance).SeedAsync());
        Assert.Equal(0, await new DiscussionSeeder(fixture.Context, NullLogger<DiscussionSeeder>.Instance).SeedAsync());
        Assert.Equal(before, await fixture.Context.Reviews.CountDocumentsAsync(FilterDefinition<Review>.Empty));
    }

    [MongoFact]
    public async Task Queries_filter_sort_page_and_search_in_database()
    {
        var queries = new ReviewQueries(fixture.Context);

        var dune = await queries.GetListAsync(new ReviewFilter(MovieId: 3, SortBy: "rating"), page: 1, pageSize: 2, default);
        Assert.Equal(3, dune.TotalCount);                                          // з сідінгу: 3 відгуки на «Дюну»
        Assert.Equal(2, dune.Items.Count);
        Assert.True(dune.HasNext);
        Assert.True(dune.Items[0].Rating >= dune.Items[1].Rating);

        var found = await queries.GetListAsync(new ReviewFilter(Search: "IMAX"), 1, 10, default);
        Assert.Contains(found.Items, r => r.Text!.Contains("IMAX"));

        var details = await queries.GetByIdAsync(ObjectId.Parse(dune.Items[0].Id), default);
        Assert.NotNull(details);
    }

    [MongoFact]
    public async Task Cursor_feed_pages_without_skip()
    {
        var queries = new ReviewQueries(fixture.Context);
        var first = await queries.GetFeedAsync(null, null, 3, default);
        var second = await queries.GetFeedAsync(null, ObjectId.Parse(first.NextCursor!), 3, default);

        Assert.True(first.HasMore);
        Assert.Empty(first.Items.Select(i => i.Id).Intersect(second.Items.Select(i => i.Id)));
        Assert.True(string.CompareOrdinal(first.Items[^1].Id, second.Items[0].Id) > 0);
    }

    [MongoFact]
    public async Task Movie_rating_is_computed_by_aggregation_pipeline()
    {
        var rating = await new ReviewQueries(fixture.Context).GetMovieRatingAsync(1, default);   // «Довбуш»: 5 і 4
        Assert.NotNull(rating);
        Assert.Equal(2, rating.Count);
        Assert.Equal(4.5, rating.Average);
        Assert.Equal(1, rating.Distribution["5"]);
        Assert.Null(await new ReviewQueries(fixture.Context).GetMovieRatingAsync(999, default));
    }

    [MongoFact]
    public async Task Discussion_status_is_stored_as_lowercase_string()
    {
        var repository = new DiscussionRepository(fixture.Context);
        var discussion = new Discussion(2, "Оппенгеймер", "Найкраща сцена фільму", NewAuthor(), ["сцени"]);
        discussion.Close("Обговорення завершено");
        await repository.AddAsync(discussion);

        var raw = await fixture.Context.Database.GetCollection<BsonDocument>("discussions")
            .Find(new BsonDocument("_id", discussion.Id)).SingleAsync();
        Assert.Equal("closed", raw["status"].AsString);
        Assert.Equal("сцени", raw["tags"][0].AsString);

        var list = await new DiscussionQueries(fixture.Context).GetListAsync(2, "closed", "сцени", 1, 10, default);
        Assert.Contains(list.Items, d => d.Id == discussion.Id.ToString());
    }
}
