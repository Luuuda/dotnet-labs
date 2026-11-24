using BookingService.Dal.Abstractions;
using Moq;

namespace BookingService.Tests.Unit;

/// <summary>Unit of Work з мок-репозиторіями — BLL тестується без бази.</summary>
internal sealed class UnitOfWorkMock
{
    public Mock<IUnitOfWork> Uow { get; } = new();
    public Mock<ICustomerRepository> Customers { get; } = new();
    public Mock<IScreeningRepository> Screenings { get; } = new();
    public Mock<IBookingRepository> Bookings { get; } = new();
    public Mock<IPaymentRepository> Payments { get; } = new();
    public Mock<IPromotionRepository> Promotions { get; } = new();
    public Mock<IProcessedEventRepository> ProcessedEvents { get; } = new();

    public UnitOfWorkMock()
    {
        Uow.SetupGet(u => u.Customers).Returns(Customers.Object);
        Uow.SetupGet(u => u.Screenings).Returns(Screenings.Object);
        Uow.SetupGet(u => u.Bookings).Returns(Bookings.Object);
        Uow.SetupGet(u => u.Payments).Returns(Payments.Object);
        Uow.SetupGet(u => u.Promotions).Returns(Promotions.Object);
        Uow.SetupGet(u => u.ProcessedEvents).Returns(ProcessedEvents.Object);
    }
}
