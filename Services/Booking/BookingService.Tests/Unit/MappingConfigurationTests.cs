using AutoMapper;
using BookingService.Bll.Dtos;
using BookingService.Bll.Mapping;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace BookingService.Tests.Unit;

public sealed class MappingConfigurationTests
{
    internal static IMapper CreateMapper() =>
        new MapperConfiguration(MappingConfiguration.Configure, NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void AutoMapper_configuration_is_valid()
    {
        var configuration = new MapperConfiguration(MappingConfiguration.Configure, NullLoggerFactory.Instance);
        configuration.AssertConfigurationIsValid();
    }

    [Fact]
    public void Booking_maps_details_total_and_rowversion()
    {
        var booking = new Booking
        {
            Id = 5,
            Status = BookingStatus.Submitted,
            RowVersion = [0, 0, 0, 0, 0, 0, 7, 209],
            Details = new BookingDetails { ContactEmail = "a@b.ua", Channel = "Web" },
            Tickets =
            [
                new Ticket { UnitPrice = 250, Discount = 50 },
                new Ticket { UnitPrice = 250, Discount = 0, IsCancelled = true }
            ]
        };

        var dto = CreateMapper().Map<BookingDto>(booking);

        Assert.Equal("a@b.ua", dto.ContactEmail);
        Assert.Equal(200m, dto.TotalAmount);            // скасований квиток не враховується
        Assert.Equal(200m, dto.Tickets[0].FinalPrice);
        Assert.Equal(Convert.ToBase64String(booking.RowVersion), dto.RowVersion);
    }
}
