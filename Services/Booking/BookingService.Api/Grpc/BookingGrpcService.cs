using BookingService.Bll.Dtos;
using BookingService.Bll.Services;
using BookingService.Domain.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace BookingService.Api.Grpc;

/// <summary>
/// gRPC-адаптер над BLL бронювання (Лаб. 06): ті самі IBookingService / IScreeningService (з кешем), що й у контролерах;
/// DTO → повідомлення booking.proto, доменні винятки → RpcException з відповідним StatusCode.
/// </summary>
public sealed class BookingGrpcService(IBookingService bookings, IScreeningService screenings, ILogger<BookingGrpcService> logger)
    : BookingService.Grpc.BookingGrpc.BookingGrpcBase
{
    private const int MaxBatch = 100;

    public override Task<BookingService.Grpc.BookingReply> GetBooking(BookingService.Grpc.GetBookingRequest request, ServerCallContext context)
    {
        RequirePositive(request.Id);
        return Run(context, async ct => ToReply(await bookings.GetAsync(request.Id, ct)));
    }

    public override Task<BookingService.Grpc.ScreeningCopyReply> GetScreening(BookingService.Grpc.GetScreeningRequest request, ServerCallContext context)
    {
        RequirePositive(request.Id);
        return Run(context, async ct => ToReply(await screenings.GetAsync(request.Id, ct)));
    }

    public override Task<BookingService.Grpc.ScreeningCopiesReply> GetScreeningsByIds(BookingService.Grpc.GetScreeningsByIdsRequest request, ServerCallContext context)
    {
        if (request.Ids.Count is 0 or > MaxBatch)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"ids: від 1 до {MaxBatch} значень"));
        foreach (var id in request.Ids) RequirePositive(id);
        return Run(context, async ct =>
        {
            var reply = new BookingService.Grpc.ScreeningCopiesReply();
            reply.Screenings.AddRange((await screenings.GetByIdsAsync(request.Ids, ct)).Select(ToReply));
            return reply;
        });
    }

    private async Task<T> Run<T>(ServerCallContext context, Func<CancellationToken, Task<T>> action)
    {
        try
        {
            return await action(context.CancellationToken);
        }
        catch (NotFoundException ex) { throw new RpcException(new Status(StatusCode.NotFound, ex.Message)); }
        catch (ValidationException ex) { throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message)); }
        catch (BusinessConflictException ex) { throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message)); }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Виклик скасовано клієнтом"));
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            logger.LogError(ex, "gRPC {Method}: необроблена помилка", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Внутрішня помилка сервісу бронювання"));
        }
    }

    private static void RequirePositive(long id)
    {
        if (id <= 0) throw new RpcException(new Status(StatusCode.InvalidArgument, "Id має бути додатним"));
    }

    internal static BookingService.Grpc.BookingReply ToReply(BookingDto b)
    {
        var reply = new BookingService.Grpc.BookingReply
        {
            Id = b.Id, Status = b.Status, CreatedAt = ToTimestamp(b.CreatedAt), TotalAmount = ToMoney(b.TotalAmount)
        };
        if (b.Channel is not null) reply.Channel = b.Channel;
        if (b.ContactEmail is not null) reply.ContactEmail = b.ContactEmail;
        reply.Tickets.AddRange(b.Tickets.Select(t => new BookingService.Grpc.TicketReply
        {
            Id = t.Id, ScreeningId = t.ScreeningId, SeatRow = t.SeatRow, SeatNumber = t.SeatNumber,
            MovieTitle = t.MovieTitle, HallName = t.HallName, StartsAt = ToTimestamp(t.StartsAt),
            FinalPrice = ToMoney(t.FinalPrice), IsCancelled = t.IsCancelled
        }));
        return reply;
    }

    internal static BookingService.Grpc.ScreeningCopyReply ToReply(ScreeningDto s) => new()
    {
        Id = s.Id, MovieId = s.MovieId, MovieTitle = s.MovieTitle, HallId = s.HallId, HallName = s.HallName,
        RowsCount = s.RowsCount, SeatsPerRow = s.SeatsPerRow, StartsAt = ToTimestamp(s.StartsAt),
        BasePrice = ToMoney(s.BasePrice), IsActive = s.IsActive
    };

    internal static BookingService.Grpc.Money ToMoney(decimal amount)
    {
        var units = decimal.Truncate(amount);
        return new BookingService.Grpc.Money { Units = (long)units, Nanos = (int)((amount - units) * 1_000_000_000m), CurrencyCode = "UAH" };
    }

    internal static Timestamp ToTimestamp(DateTime value) => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
