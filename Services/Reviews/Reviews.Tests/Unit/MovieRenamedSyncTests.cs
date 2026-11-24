using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Movies.Commands;
using Reviews.Domain.Interfaces;

namespace Reviews.Tests.Unit;

/// <summary>Лаб. 08: оновлення денормалізованої назви фільму подією MovieRenamed — ідемпотентно й без «старих» подій.</summary>
public sealed class MovieRenamedSyncTests
{
    private readonly Mock<IReviewRepository> _reviews = new();
    private readonly Mock<IDiscussionRepository> _discussions = new();
    private readonly Mock<IIntegrationEventStore> _events = new();

    private SyncMovieTitleCommandHandler Handler() =>
        new(_reviews.Object, _discussions.Object, _events.Object, TestCache.Invalidator(),
            NullLogger<SyncMovieTitleCommandHandler>.Instance);

    private static SyncMovieTitleCommand Command(DateTime? occurredAt = null) =>
        new(Guid.NewGuid(), MovieId: 5, NewTitle: "Мавка. Лісова пісня", OccurredAt: occurredAt ?? DateTime.UtcNow);

    [Fact]
    public async Task New_event_updates_all_reviews_and_discussions_then_marks_processed()
    {
        var cmd = Command();
        var order = new List<string>();
        _events.Setup(e => e.TryAdvanceMovieTitleAsync(5, cmd.OccurredAt, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _reviews.Setup(r => r.RenameMovieAsync(5, "Мавка. Лісова пісня", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("reviews")).ReturnsAsync(12);
        _discussions.Setup(d => d.RenameMovieAsync(5, "Мавка. Лісова пісня", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("discussions")).ReturnsAsync(3);
        _events.Setup(e => e.MarkProcessedAsync(cmd.EventId, "MovieRenamedEvent", It.IsAny<CancellationToken>()))
            .Callback(() => order.Add("processed")).Returns(Task.CompletedTask);

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.Equal((12L, 3L, false), (result.ReviewsUpdated, result.DiscussionsUpdated, result.Duplicate));
        Assert.Equal(["reviews", "discussions", "processed"], order);   // EventId — лише після успішного оновлення
    }

    [Fact]
    public async Task Duplicate_event_changes_nothing()
    {
        var cmd = Command();
        _events.Setup(e => e.IsProcessedAsync(cmd.EventId, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.True(result.Duplicate);
        _reviews.Verify(r => r.RenameMovieAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _discussions.Verify(d => d.RenameMovieAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Older_event_than_already_applied_rename_is_skipped()
    {
        var cmd = Command(DateTime.UtcNow.AddMinutes(-10));
        _events.Setup(e => e.TryAdvanceMovieTitleAsync(5, cmd.OccurredAt, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await Handler().Handle(cmd, CancellationToken.None);

        Assert.True(result.Stale);
        _reviews.Verify(r => r.RenameMovieAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _events.Verify(e => e.MarkProcessedAsync(cmd.EventId, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failed_update_does_not_mark_event_processed()
    {
        var cmd = Command();
        _events.Setup(e => e.TryAdvanceMovieTitleAsync(5, cmd.OccurredAt, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _reviews.Setup(r => r.RenameMovieAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("MongoDB не відповідає"));

        await Assert.ThrowsAsync<TimeoutException>(() => Handler().Handle(cmd, CancellationToken.None));

        _events.Verify(e => e.MarkProcessedAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
