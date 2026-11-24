using BookingService.Bll.IntegrationEvents;
using BookingService.Bll.IntegrationEvents.Handlers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.Messaging;

namespace BookingService.Tests.Unit;

/// <summary>Лаб. 08: ідемпотентні обробники подій Каталогу — EventId і зміна копії в одній транзакції.</summary>
public sealed class CatalogSyncHandlersTests
{
    private readonly UnitOfWorkMock _db = new();
    private readonly List<string> _calls = [];

    public CatalogSyncHandlersTests()
    {
        _db.Uow.Setup(u => u.BeginTransactionAsync(It.IsAny<System.Data.IsolationLevel>(), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("begin")).Returns(Task.CompletedTask);
        _db.Uow.Setup(u => u.CommitAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("commit")).Returns(Task.CompletedTask);
        _db.Uow.Setup(u => u.RollbackAsync(It.IsAny<CancellationToken>())).Callback(() => _calls.Add("rollback")).Returns(Task.CompletedTask);
    }

    private ScreeningPriceChangedHandler PriceHandler() =>
        new(_db.Uow.Object, TestCache.Create(), NullLogger<ScreeningPriceChangedHandler>.Instance);

    private static ScreeningPriceChangedEvent PriceChanged(decimal price = 250m) =>
        new(ScreeningId: 42, MovieId: 1, MovieTitle: "Довбуш", OldPrice: 200m, NewPrice: price);

    [Fact]
    public async Task New_event_records_EventId_and_updates_copy_in_one_transaction()
    {
        var e = PriceChanged();
        _db.ProcessedEvents.Setup(p => p.TryAddAsync(e.EventId, nameof(ScreeningPriceChangedEvent), It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("processed")).ReturnsAsync(true);
        _db.Screenings.Setup(s => s.ChangePriceFromCatalogAsync(42, 250m, e.OccurredAt, It.IsAny<CancellationToken>()))
            .Callback(() => _calls.Add("update")).ReturnsAsync(1);

        await PriceHandler().HandleAsync(e, CancellationToken.None);

        Assert.Equal(["begin", "processed", "update", "commit"], _calls);
    }

    [Fact]
    public async Task Duplicate_event_changes_nothing()
    {
        var e = PriceChanged();
        _db.ProcessedEvents.Setup(p => p.TryAddAsync(e.EventId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await PriceHandler().HandleAsync(e, CancellationToken.None);   // без винятку → споживач зробить ack

        _db.Screenings.Verify(s => s.ChangePriceFromCatalogAsync(It.IsAny<long>(), It.IsAny<decimal>(), It.IsAny<DateTime>(),
            It.IsAny<CancellationToken>()), Times.Never);
        Assert.Equal(["begin", "rollback"], _calls);
    }

    [Fact]
    public async Task Same_event_twice_applies_once()
    {
        var e = PriceChanged();
        var seen = new HashSet<Guid>();
        _db.ProcessedEvents.Setup(p => p.TryAddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, string _, CancellationToken _) => seen.Add(id));   // як PRIMARY KEY

        await PriceHandler().HandleAsync(e, CancellationToken.None);
        await PriceHandler().HandleAsync(e, CancellationToken.None);

        _db.Screenings.Verify(s => s.ChangePriceFromCatalogAsync(42, 250m, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Failure_rolls_back_EventId_too_so_retry_can_process_again()
    {
        var e = PriceChanged();
        _db.ProcessedEvents.Setup(p => p.TryAddAsync(e.EventId, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _db.Screenings.Setup(s => s.ChangePriceFromCatalogAsync(It.IsAny<long>(), It.IsAny<decimal>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("SQL Server не відповідає"));

        await Assert.ThrowsAsync<TimeoutException>(() => PriceHandler().HandleAsync(e, CancellationToken.None));

        Assert.Equal(["begin", "rollback"], _calls);
    }

    [Fact]
    public async Task Invalid_price_is_permanent_error()
    {
        _db.ProcessedEvents.Setup(p => p.TryAddAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<PermanentMessageException>(() => PriceHandler().HandleAsync(PriceChanged(price: 0m), CancellationToken.None));
        Assert.Contains("rollback", _calls);
    }

    [Fact]
    public async Task MovieRenamed_updates_title_of_all_screenings_of_the_movie()
    {
        var e = new MovieRenamedEvent(MovieId: 3, OldTitle: "Мавка", NewTitle: "Мавка. Лісова пісня", OriginalTitle: null);
        _db.ProcessedEvents.Setup(p => p.TryAddAsync(e.EventId, nameof(MovieRenamedEvent), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _db.Screenings.Setup(s => s.RenameMovieFromCatalogAsync(3, "Мавка. Лісова пісня", e.OccurredAt, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        _db.Screenings.Setup(s => s.GetIdsByMovieAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync([10L, 11L, 12L, 13L]);

        await new MovieRenamedHandler(_db.Uow.Object, TestCache.Create(), NullLogger<MovieRenamedHandler>.Instance)
            .HandleAsync(e, CancellationToken.None);

        _db.Screenings.Verify(s => s.RenameMovieFromCatalogAsync(3, "Мавка. Лісова пісня", e.OccurredAt, It.IsAny<CancellationToken>()), Times.Once);
        _db.Uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
