using BookingService.Bll.Dtos;
using BookingService.Bll.Validation;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;

namespace BookingService.Tests.Unit;

public sealed class SeatSelectionValidatorTests
{
    private readonly SeatSelectionValidator _validator = new();
    private static readonly Screening Vip = new() { Id = 4, HallName = "VIP", RowsCount = 5, SeatsPerRow = 8, IsActive = true };
    private static readonly (int, int)[] NoneOccupied = [];

    [Fact]
    public void Seat_outside_hall_is_validation_error() =>
        Assert.Throws<ValidationException>(() =>
            _validator.Validate(Vip, [new SeatDto { Row = 6, Seat = 1 }], NoneOccupied));

    [Fact]
    public void Duplicate_seats_are_validation_error() =>
        Assert.Throws<ValidationException>(() =>
            _validator.Validate(Vip, [new SeatDto { Row = 1, Seat = 1 }, new SeatDto { Row = 1, Seat = 1 }], NoneOccupied));

    [Fact]
    public void Cancelled_screening_is_conflict() =>
        Assert.Throws<BusinessConflictException>(() =>
            _validator.Validate(new Screening { Id = 8, RowsCount = 5, SeatsPerRow = 8, IsActive = false },
                                [new SeatDto { Row = 1, Seat = 1 }], NoneOccupied));

    [Fact]
    public void Free_seats_pass()
    {
        var seats = _validator.Validate(Vip, [new SeatDto { Row = 2, Seat = 3 }], [(2, 4)]);
        var seat = Assert.Single(seats);
        Assert.Equal(2, seat.Row);
        Assert.Equal(3, seat.Seat);
    }
}
