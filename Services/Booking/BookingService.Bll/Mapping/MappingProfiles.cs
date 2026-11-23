using AutoMapper;
using BookingService.Bll.Dtos;
using BookingService.Domain.Models;

namespace BookingService.Bll.Mapping;

/// <summary>Усі профілі зареєстровано в одному місці — і в DI, і в тесті AssertConfigurationIsValid.</summary>
public static class MappingConfiguration
{
    public static void Configure(IMapperConfigurationExpression cfg)
    {
        cfg.AddProfile<CommonProfile>();
        cfg.AddProfile<CustomerProfile>();
        cfg.AddProfile<ScreeningProfile>();
        cfg.AddProfile<BookingProfile>();
    }
}

public sealed class CommonProfile : Profile
{
    public CommonProfile()
    {
        // rowversion (byte[8]) у JSON передається як base64-рядок
        CreateMap<byte[], string>().ConvertUsing(bytes => Convert.ToBase64String(bytes));
    }
}

public sealed class CustomerProfile : Profile
{
    public CustomerProfile()
    {
        CreateMap<Customer, CustomerDto>();
    }
}

public sealed class ScreeningProfile : Profile
{
    public ScreeningProfile()
    {
        CreateMap<Screening, ScreeningDto>();
        CreateMap<CreateScreeningDto, Screening>()
            .ForMember(d => d.SyncedAt, o => o.Ignore());
        CreateMap<UpsertScreeningDto, Screening>()
            .ForMember(d => d.Id, o => o.Ignore())
            .ForMember(d => d.SyncedAt, o => o.Ignore());
    }
}

public sealed class BookingProfile : Profile
{
    public BookingProfile()
    {
        CreateMap<Ticket, TicketDto>()
            .ForMember(d => d.FinalPrice, o => o.MapFrom(s => s.UnitPrice - s.Discount));
        CreateMap<BookingStatusChange, BookingStatusChangeDto>();
        CreateMap<Booking, BookingDto>()
            .ForMember(d => d.ContactEmail, o => o.MapFrom(s => s.Details!.ContactEmail))
            .ForMember(d => d.ContactPhone, o => o.MapFrom(s => s.Details!.ContactPhone))
            .ForMember(d => d.Channel, o => o.MapFrom(s => s.Details!.Channel))
            .ForMember(d => d.Comment, o => o.MapFrom(s => s.Details!.Comment));
        CreateMap<BookingSummary, BookingSummaryDto>();
        CreateMap<Payment, PaymentDto>();
    }
}
