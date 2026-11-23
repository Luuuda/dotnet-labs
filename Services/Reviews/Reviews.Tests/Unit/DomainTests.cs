using MongoDB.Bson;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Tests.Unit;

/// <summary>Бізнес-правила перевіряє сама сутність — без бази й без сервісів.</summary>
public sealed class DomainTests
{
    private static readonly Author Olena = new("u-1", "Олена");

    [Fact]
    public void Review_with_rating_10_throws_DomainException_without_database()
    {
        var ex = Assert.Throws<DomainException>(() => new Review(3, "Дюна", Olena, 10, "Текст"));
        Assert.Contains("від 1 до 5", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void Rating_outside_1_5_is_rejected(int value) => Assert.Throws<DomainException>(() => new Rating(value));

    [Fact]
    public void Value_objects_are_compared_by_value()
    {
        Assert.Equal(new Author("u-1", "Олена"), new Author(" u-1 ", "Олена "));
        Assert.NotEqual(new Author("u-1", "Олена"), new Author("u-2", "Олена"));
        Assert.True(new Rating(5) == new Rating(5));
        Assert.Equal(new ReviewAspects(new Dictionary<string, int> { ["story"] = 5, ["acting"] = 4 }),
                     new ReviewAspects(new Dictionary<string, int> { ["ACTING"] = 4, ["story"] = 5 }));
    }

    [Fact]
    public void Author_requires_name_and_id()
    {
        Assert.Throws<DomainException>(() => new Author("u-1", " "));
        Assert.Throws<DomainException>(() => new Author("", "Олена"));
    }

    [Fact]
    public void Aspects_validate_names_and_scores()
    {
        Assert.Throws<DomainException>(() => new ReviewAspects(new Dictionary<string, int> { ["story"] = 7 }));
        Assert.Throws<DomainException>(() => new ReviewAspects(new Dictionary<string, int> { ["сюжет"] = 4 }));
        Assert.Throws<DomainException>(() => new ReviewAspects(new Dictionary<string, int>()));
    }

    [Fact]
    public void Comments_are_embedded_counted_and_limited()
    {
        var review = new Review(3, "Дюна", Olena, 5, "Текст");
        var comment = review.AddComment(new Author("u-2", "Андрій"), "Згоден!");

        Assert.Equal(1, review.CommentsCount);
        Assert.NotEqual(ObjectId.Empty, comment.Id);
        Assert.NotNull(review.UpdatedAt);
        Assert.Throws<DomainException>(() => review.AddComment(Olena, "   "));

        for (var i = 1; i < Review.MaxComments; i++) review.AddComment(Olena, $"#{i}");
        Assert.Throws<DomainException>(() => review.AddComment(Olena, "51-й"));

        review.RemoveComment(comment.Id);
        Assert.Equal(Review.MaxComments - 1, review.CommentsCount);
        Assert.Throws<DomainException>(() => review.RemoveComment(ObjectId.GenerateNewId()));
    }

    [Fact]
    public void Collections_are_not_mutable_from_outside()
    {
        var review = new Review(3, "Дюна", Olena, 5, "Текст");
        Assert.IsAssignableFrom<IReadOnlyCollection<Comment>>(review.Comments);
        Assert.False(review.Comments is List<Comment>);
        Assert.All(typeof(Review).GetProperties(), p => Assert.True(p.SetMethod is null || !p.SetMethod.IsPublic,
            $"{p.Name} має публічний setter"));
    }

    [Fact]
    public void Discussion_cannot_be_closed_twice_and_closed_topic_is_read_only()
    {
        var discussion = new Discussion(3, "Дюна", "Теорії про третю частину", Olena, ["Книга", "книга"]);
        Assert.Single(discussion.Tags);

        discussion.Close("Офтоп");
        Assert.Equal(DiscussionStatus.Closed, discussion.Status);
        Assert.Throws<DomainException>(() => discussion.Close(null));
        Assert.Throws<DomainException>(() => discussion.Rename("Нова назва"));

        discussion.Reopen();
        Assert.Null(discussion.ClosedAt);
    }

    [Fact]
    public void Version_is_bumped_only_by_repository_call()
    {
        var review = new Review(3, "Дюна", Olena, 5, "Текст");
        review.Edit(new Rating(4), "Інший текст", null);
        Assert.Equal(0, review.Version);
        review.BumpVersion();
        Assert.Equal(1, review.Version);
    }
}
