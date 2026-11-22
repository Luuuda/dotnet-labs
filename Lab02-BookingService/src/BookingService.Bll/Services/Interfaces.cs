using BookingService.Bll.Dtos;

namespace BookingService.Bll.Services;

public interface ICustomerService
{
    Task<CustomerDto> GetAsync(Guid id, CancellationToken ct);
    Task<PagedResult<CustomerDto>> ListAsync(string? search, int page, int pageSize, CancellationToken ct);
    Task<CustomerDto> CreateAsync(CreateCustomerDto input, CancellationToken ct);
    Task<CustomerDto> UpdateAsync(Guid id, UpdateCustomerDto input, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public interface IScreeningService
{
    Task<IReadOnlyList<ScreeningDto>> ListAsync(DateTime? from, bool onlyActive, CancellationToken ct);
    Task<ScreeningDto> GetAsync(long id, CancellationToken ct);
    Task<SeatMapDto> GetSeatMapAsync(long id, CancellationToken ct);
    Task<ScreeningDto> CreateAsync(CreateScreeningDto input, CancellationToken ct);
    Task<ScreeningDto> UpdateAsync(long id, UpsertScreeningDto input, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
}

public interface IBookingService
{
    Task<BookingDto> GetAsync(long id, CancellationToken ct);
    Task<PagedResult<BookingSummaryDto>> ListByCustomerAsync(Guid customerId, string? status, int page, int pageSize, CancellationToken ct);
    /// <summary>Транзакція: Bookings + BookingDetails + Tickets (знімок ціни) + History.</summary>
    Task<BookingDto> CreateAsync(CreateBookingDto input, CancellationToken ct);
    /// <summary>Підтвердити місця: Submitted → AwaitingValidation → SeatsConfirmed в одній транзакції.</summary>
    Task<BookingDto> ConfirmAsync(long id, BookingActionDto? action, CancellationToken ct);
    /// <summary>Транзакція з двох репозиторіїв: Payments (вставка) + Bookings (процедура зміни статусу → Paid).</summary>
    Task<PaymentResultDto> PayAsync(long id, PayBookingDto input, string? idempotencyKey, CancellationToken ct);
    Task<BookingDto> IssueAsync(long id, BookingActionDto? action, CancellationToken ct);
    Task<BookingDto> CancelAsync(long id, BookingActionDto? action, CancellationToken ct);
    Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(long id, CancellationToken ct);
    Task DeleteAsync(long id, CancellationToken ct);
}
