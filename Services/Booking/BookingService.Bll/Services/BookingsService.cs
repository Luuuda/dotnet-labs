using System.Data;
using AutoMapper;
using BookingService.Bll.Caching;
using BookingService.Bll.Dtos;
using BookingService.Bll.Validation;
using BookingService.Dal.Abstractions;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging;

namespace BookingService.Bll.Services;

/// <summary>
/// Бізнес-логіка бронювань. Кожна операція, що змінює кілька таблиць, виконується в ОДНІЙ
/// транзакції Unit of Work: або все, або нічого. Допустимість переходів статусу перевіряє
/// процедура dbo.usp_Booking_ChangeStatus з Лаб. 01 (THROW 50002 → BusinessConflictException → 409).
/// </summary>
public sealed class BookingsService(
    IUnitOfWork uow,
    IMapper mapper,
    SeatSelectionValidator seatValidator,
    TimeProvider clock,
    ITwoLevelCache cache,
    ILogger<BookingsService> logger) : IBookingService
{
    private const string Actor = "booking-api";

    /// <summary>
    /// Лаб. 06: картка бронювання через дворівневий кеш (booking:booking:{id}). Вона містить контакти клієнта,
    /// тому TTL дуже короткий (L1 10 с, L2 30 с), а кожна зміна статусу видаляє ключ синхронно з транзакцією.
    /// </summary>
    public async Task<BookingDto> GetAsync(long id, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKeys.Booking(id), CachePolicy.PersonalShort, async c =>
            mapper.Map<BookingDto>(await uow.Bookings.GetWithTicketsAsync(id, c)
                                   ?? throw new NotFoundException($"Бронювання {id} не знайдено")), ct))!;

    public async Task<PagedResult<BookingSummaryDto>> ListByCustomerAsync(
        Guid customerId, string? status, int page, int pageSize, CancellationToken ct)
    {
        Guards.Paging(page, pageSize);
        if (status is not null && !BookingStatus.All.Contains(status))
            throw new ValidationException("status", $"Невідомий статус «{status}». Допустимі: {string.Join(", ", BookingStatus.All)}");

        _ = await uow.Customers.GetByIdAsync(customerId, ct)
            ?? throw new NotFoundException($"Клієнта {customerId} не знайдено");

        var rows = await uow.Bookings.GetPageByCustomerAsync(customerId, status, page, pageSize, ct);
        return new PagedResult<BookingSummaryDto>
        {
            Items = mapper.Map<List<BookingSummaryDto>>(rows),
            Page = page,
            PageSize = pageSize,
            TotalCount = rows.Count > 0 ? rows[0].TotalCount : 0
        };
    }

    public async Task<BookingDto> CreateAsync(CreateBookingDto input, CancellationToken ct)
    {
        using var activity = BookingTelemetry.Source.StartActivity("CreateBooking");
        activity?.SetTag("booking.screening_id", input.ScreeningId);
        activity?.SetTag("booking.seats", input.Seats.Count);
        long bookingId;
        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            // --- бізнес-валідація зі станом БД ---
            var customer = await uow.Customers.GetByIdAsync(input.CustomerId, ct)
                           ?? throw new ValidationException("customerId", $"Клієнта {input.CustomerId} не знайдено");
            var screening = await uow.Screenings.GetByIdAsync(input.ScreeningId, ct)
                            ?? throw new ValidationException("screeningId", $"Сеанс {input.ScreeningId} не знайдено");

            Promotion? promotion = null;
            if (!string.IsNullOrWhiteSpace(input.PromoCode))
                promotion = await uow.Promotions.GetActiveByCodeAsync(input.PromoCode.Trim(), clock.GetUtcNow().UtcDateTime, ct)
                            ?? throw new ValidationException("promoCode", "Промокод недійсний або прострочений");

            var occupied = await uow.Bookings.GetOccupiedSeatsAsync(screening.Id, ct);
            var seats = seatValidator.Validate(screening, input.Seats, occupied.ToList());

            // --- запис агрегату: 4 таблиці, одна транзакція ---
            bookingId = await uow.Bookings.AddAsync(new Booking
            {
                CustomerId = customer.Id,
                PromotionId = promotion?.Id,
                Status = BookingStatus.Submitted,
                CreatedBy = Actor
            }, ct);

            await uow.Bookings.AddDetailsAsync(new BookingDetails
            {
                BookingId = bookingId,
                ContactEmail = customer.Email,
                ContactPhone = input.ContactPhone ?? customer.Phone,
                Channel = input.Channel,
                Comment = input.Comment
            }, ct);

            // Назву фільму, залу, час і ціну беремо з локальної копії Screenings і записуємо в квиток
            // як ЗНІМОК — за політикою дублювання з Лаб. 01 (як ProductName / UnitPrice в OrderItem eShop).
            var percent = promotion?.DiscountPercent ?? 0m;
            await uow.Bookings.AddTicketsAsync(seats.Select(s => new Ticket
            {
                BookingId = bookingId,
                ScreeningId = screening.Id,
                SeatRow = s.Row,
                SeatNumber = s.Seat,
                MovieTitle = screening.MovieTitle,
                HallName = screening.HallName,
                StartsAt = screening.StartsAt,
                UnitPrice = screening.BasePrice,
                Discount = Math.Round(screening.BasePrice * percent / 100m, 2, MidpointRounding.AwayFromZero)
            }), ct);   // місце, яке паралельно встиг зайняти інший запит → 2601 → 409 і відкат усього

            await uow.Bookings.AddStatusHistoryAsync(new BookingStatusChange
            {
                BookingId = bookingId,
                ToStatus = BookingStatus.Submitted,
                ChangedBy = Actor,
                Reason = "Бронювання створено"
            }, ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }

        activity?.SetTag("booking.id", bookingId);
        var created = await GetAsync(bookingId, ct);
        logger.LogInformation("Створено бронювання {BookingId}: сеанс {ScreeningId}, {SeatCount} місць на суму {Amount}, статус {Status}",
            bookingId, input.ScreeningId, input.Seats.Count, created.TotalAmount, created.Status);
        return created;
    }

    public async Task<BookingDto> ConfirmAsync(long id, BookingActionDto? action, CancellationToken ct)
    {
        using var activity = BookingTelemetry.Source.StartActivity("ConfirmBooking");
        activity?.SetTag("booking.id", id);
        var expected = Guards.OptionalRowVersion(action?.RowVersion);
        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var booking = await uow.Bookings.GetWithTicketsAsync(id, ct)
                          ?? throw new NotFoundException($"Бронювання {id} не знайдено");

            // Бізнес-правило: не можна підтвердити місця на сеанс, який Каталог уже скасував
            foreach (var screeningId in booking.Tickets.Where(t => !t.IsCancelled).Select(t => t.ScreeningId).Distinct())
            {
                var screening = await uow.Screenings.GetByIdAsync(screeningId, ct);
                if (screening is null || !screening.IsActive)
                    throw new BusinessConflictException($"Сеанс {screeningId} скасовано — підтвердити місця неможливо");
            }

            // Два кроки статусу в одній транзакції. Якщо бронювання вже підтверджене чи оплачене,
            // процедура відмовить (THROW 50002) → 409, і жоден крок не збережеться.
            if (booking.Status == BookingStatus.Submitted)
                expected = await uow.Bookings.ChangeStatusAsync(id, BookingStatus.AwaitingValidation, Actor,
                                                                "Перевірка місць", expected, ct);
            await uow.Bookings.ChangeStatusAsync(id, BookingStatus.SeatsConfirmed, Actor,
                                                 action?.Reason ?? "Місця підтверджено", expected, ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }

        await cache.InvalidateAsync("BookingConfirmed", ct, CacheKeys.Booking(id));
        logger.LogInformation("Бронювання {BookingId}: місця підтверджено, статус {Status}", id, BookingStatus.SeatsConfirmed);
        return await GetAsync(id, ct);
    }

    public async Task<PaymentResultDto> PayAsync(long id, PayBookingDto input, string? idempotencyKey, CancellationToken ct)
    {
        using var activity = BookingTelemetry.Source.StartActivity("PayBooking");
        activity?.SetTag("booking.id", id);
        activity?.SetTag("payment.amount", input.Amount);
        activity?.SetTag("payment.provider", input.Provider);
        // Ідемпотентність: той самий Id транзакції шлюзу (або заголовок Idempotency-Key) не створить другу оплату
        var key = string.IsNullOrWhiteSpace(input.ExternalTransactionId) ? idempotencyKey : input.ExternalTransactionId;
        if (!string.IsNullOrWhiteSpace(key))
        {
            var existing = await uow.Payments.GetByExternalIdAsync(key, ct);
            if (existing is not null)
            {
                if (existing.BookingId != id)
                    throw new BusinessConflictException("Цей ключ ідемпотентності вже використано для іншого бронювання");
                logger.LogInformation("Повторний запит оплати {BookingId} — повертаю наявну оплату", id);
                return new PaymentResultDto(mapper.Map<PaymentDto>(existing), Replayed: true);
            }
        }

        long paymentId;
        await uow.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct);
        try
        {
            var booking = await uow.Bookings.GetWithTicketsAsync(id, ct)
                          ?? throw new NotFoundException($"Бронювання {id} не знайдено");

            if (input.Amount != booking.TotalAmount)
                throw new ValidationException("amount",
                    $"Сума оплати {input.Amount:0.00} не дорівнює вартості бронювання {booking.TotalAmount:0.00} UAH");

            // Репозиторій 1 (Payments): записуємо оплату
            paymentId = await uow.Payments.AddAsync(new Payment
            {
                BookingId = id,
                Amount = input.Amount,
                Provider = input.Provider,
                ExternalTransactionId = string.IsNullOrWhiteSpace(key) ? null : key,
                Status = PaymentStatus.Succeeded,
                CreatedBy = Actor
            }, ct);

            // Репозиторій 2 (Bookings): процедура переводить у Paid. Якщо бронювання ще не SeatsConfirmed
            // (або вже оплачене), процедура кидає 50002 → відкочується І вставка оплати з кроку 1.
            await uow.Bookings.ChangeStatusAsync(id, BookingStatus.Paid, Actor, "Оплату отримано", null, ct);

            await uow.CommitAsync(ct);
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }

        await cache.InvalidateAsync("BookingPaid", ct, CacheKeys.Booking(id));
        logger.LogInformation("Бронювання {BookingId} оплачено: {Amount} UAH через {Provider}, оплата {PaymentId}, статус {Status}",
            id, input.Amount, input.Provider, paymentId, BookingStatus.Paid);
        var payment = await uow.Payments.GetByIdAsync(paymentId, ct)
                      ?? throw new InvalidOperationException("Оплату не знайдено одразу після збереження");
        return new PaymentResultDto(mapper.Map<PaymentDto>(payment), Replayed: false);
    }

    public Task<BookingDto> IssueAsync(long id, BookingActionDto? action, CancellationToken ct) =>
        ChangeStatusAsync(id, BookingStatus.Issued, action, "Квитки видано", ct);

    public Task<BookingDto> CancelAsync(long id, BookingActionDto? action, CancellationToken ct) =>
        ChangeStatusAsync(id, BookingStatus.Cancelled, action, "Скасовано клієнтом", ct);

    public async Task<IReadOnlyList<PaymentDto>> GetPaymentsAsync(long id, CancellationToken ct)
    {
        _ = await uow.Bookings.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Бронювання {id} не знайдено");
        return mapper.Map<List<PaymentDto>>(await uow.Payments.GetByBookingAsync(id, ct));
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        var booking = await uow.Bookings.GetByIdAsync(id, ct)
                      ?? throw new NotFoundException($"Бронювання {id} не знайдено");
        if (booking.Status != BookingStatus.Cancelled)
            throw new BusinessConflictException($"Видалити можна лише скасоване бронювання (зараз: {booking.Status})");

        await uow.Bookings.DeleteAsync(id, ct);   // soft delete
        await cache.InvalidateAsync("BookingDeleted", ct, CacheKeys.Booking(id));
        logger.LogInformation("Бронювання {BookingId} видалено (soft delete)", id);
    }

    /// <summary>
    /// Одиночний перехід статусу. Процедура сама відкриває транзакцію і блокує рядок (UPDLOCK),
    /// тому окрема транзакція UoW тут не потрібна.
    /// </summary>
    private async Task<BookingDto> ChangeStatusAsync(long id, string status, BookingActionDto? action, string defaultReason, CancellationToken ct)
    {
        using var activity = BookingTelemetry.Source.StartActivity("ChangeBookingStatus");
        activity?.SetTag("booking.id", id);
        activity?.SetTag("booking.status", status);
        var expected = Guards.OptionalRowVersion(action?.RowVersion);
        await uow.Bookings.ChangeStatusAsync(id, status, Actor, action?.Reason ?? defaultReason, expected, ct);
        await cache.InvalidateAsync("BookingStatusChanged:" + status, ct, CacheKeys.Booking(id));
        logger.LogInformation("Бронювання {BookingId} → {Status}", id, status);
        return await GetAsync(id, ct);
    }
}
