using System.Data;
using BookingService.Bll.Dtos;
using BookingService.Bll.Services;
using BookingService.Bll.Validation;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookingService.Tests.Unit;

public sealed class BookingsServiceTests
{
    private readonly UnitOfWorkMock _m = new();
    private readonly BookingsService _service;

    private static readonly Guid CustomerId = Guid.Parse("6f1c2a10-0000-4000-8000-000000000001");

    private static readonly Screening Imax = new()
    {
        Id = 7, MovieId = 3, MovieTitle = "Дюна: Частина друга", HallId = 2, HallName = "IMAX",
        RowsCount = 14, SeatsPerRow = 20, StartsAt = new DateTime(2026, 12, 7, 18, 0, 0), BasePrice = 250m, IsActive = true
    };

    public BookingsServiceTests()
    {
        _service = new BookingsService(_m.Uow.Object, MappingConfigurationTests.CreateMapper(),
            new SeatSelectionValidator(), TimeProvider.System, TestCache.Create(), NullLogger<BookingsService>.Instance);
    }

    [Fact]
    public async Task Confirm_nonexistent_booking_throws_NotFound_and_rolls_back()
    {
        _m.Bookings.Setup(b => b.GetWithTicketsAsync(404, It.IsAny<CancellationToken>())).ReturnsAsync((Booking?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => _service.ConfirmAsync(404, null, CancellationToken.None));

        _m.Uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _m.Uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_takes_title_and_price_from_screening_as_snapshot_and_commits()
    {
        List<Ticket>? saved = null;
        _m.Customers.Setup(c => c.GetByIdAsync(CustomerId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Customer { Id = CustomerId, Email = "olena@example.com" });
        _m.Screenings.Setup(s => s.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Imax);
        _m.Bookings.Setup(b => b.GetOccupiedSeatsAsync(7, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<(int, int)> { (1, 1) });
        _m.Bookings.Setup(b => b.AddAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>())).ReturnsAsync(42);
        _m.Bookings.Setup(b => b.AddTicketsAsync(It.IsAny<IEnumerable<Ticket>>(), It.IsAny<CancellationToken>()))
                   .Callback<IEnumerable<Ticket>, CancellationToken>((t, _) => saved = t.ToList())
                   .Returns(Task.CompletedTask);
        _m.Bookings.Setup(b => b.GetWithTicketsAsync(42, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Booking { Id = 42, CustomerId = CustomerId });

        var result = await _service.CreateAsync(new CreateBookingDto
        {
            CustomerId = CustomerId,
            ScreeningId = 7,
            Seats = [new SeatDto { Row = 5, Seat = 10 }, new SeatDto { Row = 5, Seat = 11 }]
        }, CancellationToken.None);

        Assert.Equal(42, result.Id);
        Assert.NotNull(saved);
        Assert.Equal(2, saved!.Count);
        Assert.All(saved, t =>
        {
            Assert.Equal(42, t.BookingId);
            Assert.Equal("Дюна: Частина друга", t.MovieTitle);   // знімок назви
            Assert.Equal(250m, t.UnitPrice);                     // знімок ціни
        });
        _m.Uow.Verify(u => u.BeginTransactionAsync(IsolationLevel.ReadCommitted, It.IsAny<CancellationToken>()), Times.Once);
        _m.Uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        _m.Uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_with_occupied_seat_throws_conflict_and_writes_nothing()
    {
        _m.Customers.Setup(c => c.GetByIdAsync(CustomerId, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new Customer { Id = CustomerId, Email = "olena@example.com" });
        _m.Screenings.Setup(s => s.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(Imax);
        _m.Bookings.Setup(b => b.GetOccupiedSeatsAsync(7, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new List<(int, int)> { (5, 10) });

        await Assert.ThrowsAsync<BusinessConflictException>(() => _service.CreateAsync(new CreateBookingDto
        {
            CustomerId = CustomerId,
            ScreeningId = 7,
            Seats = [new SeatDto { Row = 5, Seat = 10 }]
        }, CancellationToken.None));

        _m.Bookings.Verify(b => b.AddAsync(It.IsAny<Booking>(), It.IsAny<CancellationToken>()), Times.Never);
        _m.Uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pay_with_wrong_amount_throws_validation_and_does_not_insert_payment()
    {
        _m.Bookings.Setup(b => b.GetWithTicketsAsync(5, It.IsAny<CancellationToken>())).ReturnsAsync(new Booking
        {
            Id = 5, Status = BookingStatus.SeatsConfirmed,
            Tickets = [new Ticket { UnitPrice = 220m }, new Ticket { UnitPrice = 220m }]
        });

        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.PayAsync(5, new PayBookingDto { Amount = 1m, Provider = "Card" }, null, CancellationToken.None));

        _m.Payments.Verify(p => p.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Never);
        _m.Uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pay_rolls_back_payment_when_status_change_in_second_repository_fails()
    {
        _m.Bookings.Setup(b => b.GetWithTicketsAsync(3, It.IsAny<CancellationToken>())).ReturnsAsync(new Booking
        {
            Id = 3, Status = BookingStatus.Submitted, Tickets = [new Ticket { UnitPrice = 160m }]
        });
        _m.Payments.Setup(p => p.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>())).ReturnsAsync(99);
        _m.Bookings.Setup(b => b.ChangeStatusAsync(3, BookingStatus.Paid, It.IsAny<string>(), It.IsAny<string?>(),
                                                   It.IsAny<byte[]?>(), It.IsAny<CancellationToken>()))
                   .ThrowsAsync(new BusinessConflictException("Недопустимий перехід статусу: Submitted -> Paid"));

        await Assert.ThrowsAsync<BusinessConflictException>(() =>
            _service.PayAsync(3, new PayBookingDto { Amount = 160m, Provider = "Card" }, null, CancellationToken.None));

        _m.Payments.Verify(p => p.AddAsync(It.IsAny<Payment>(), It.IsAny<CancellationToken>()), Times.Once);
        _m.Uow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);   // перший крок відкочується
        _m.Uow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Pay_replay_with_same_idempotency_key_returns_existing_payment()
    {
        _m.Payments.Setup(p => p.GetByExternalIdAsync("TX-1", It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new Payment { Id = 10, BookingId = 5, Amount = 440m, ExternalTransactionId = "TX-1" });

        var result = await _service.PayAsync(5, new PayBookingDto { Amount = 440m, Provider = "Card" }, "TX-1", CancellationToken.None);

        Assert.True(result.Replayed);
        Assert.Equal(10, result.Payment.Id);
        _m.Uow.Verify(u => u.BeginTransactionAsync(It.IsAny<IsolationLevel>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task List_with_unknown_status_throws_validation()
    {
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.ListByCustomerAsync(CustomerId, "Shipped", 1, 10, CancellationToken.None));
    }
}
