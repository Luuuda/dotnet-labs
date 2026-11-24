using System.Data;

namespace BookingService.Dal.Abstractions;

/// <summary>
/// Unit of Work: ОДНЕ з'єднання на HTTP-запит (реєструється як Scoped) і одна транзакція,
/// спільна для всіх репозиторіїв. Аналог IUnitOfWork з eShop (Ordering.Domain/SeedWork),
/// тільки реалізований вручну на ADO.NET.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    ICustomerRepository Customers { get; }
    IScreeningRepository Screenings { get; }
    IBookingRepository Bookings { get; }
    IPaymentRepository Payments { get; }
    IPromotionRepository Promotions { get; }
    /// <summary>Лаб. 08: оброблені інтеграційні події (ідемпотентність споживача).</summary>
    IProcessedEventRepository ProcessedEvents { get; }

    bool HasActiveTransaction { get; }

    Task BeginTransactionAsync(IsolationLevel level = IsolationLevel.ReadCommitted, CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
