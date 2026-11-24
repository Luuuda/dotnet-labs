using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using CatalogService.Domain.Exceptions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace CatalogService.Api.Grpc;

/// <summary>
/// gRPC-адаптер над BLL каталогу (Лаб. 06) — як контролер, лише інший протокол: валідує вхід, викликає ті самі
/// IMovieService / IScreeningService (з дворівневим кешем), перетворює DTO на повідомлення, а доменні винятки —
/// на RpcException з правильним StatusCode (NotFound, InvalidArgument), а не Unknown / Internal.
/// </summary>
public sealed class CatalogGrpcService(IMovieService movies, IScreeningService screenings, ILogger<CatalogGrpcService> logger)
    : CatalogService.Grpc.CatalogGrpc.CatalogGrpcBase
{
    private const int MaxBatch = 100;

    public override Task<CatalogService.Grpc.MovieReply> GetMovie(CatalogService.Grpc.GetMovieRequest request, ServerCallContext context)
    {
        RequirePositive(request.Id, "id");
        return Run(context, async ct => GrpcMapping.ToReply(await movies.GetByIdAsync(request.Id, ct)));
    }

    public override Task<CatalogService.Grpc.MoviesReply> GetMoviesByIds(CatalogService.Grpc.GetMoviesByIdsRequest request, ServerCallContext context)
    {
        RequireBatch(request.Ids.Count);
        foreach (var id in request.Ids) RequirePositive(id, "ids");
        return Run(context, async ct =>
        {
            var reply = new CatalogService.Grpc.MoviesReply();
            reply.Movies.AddRange((await movies.GetByIdsAsync(request.Ids, ct)).Select(GrpcMapping.ToReply));
            return reply;
        });
    }

    public override Task<CatalogService.Grpc.ScreeningReply> GetScreening(CatalogService.Grpc.GetScreeningRequest request, ServerCallContext context)
    {
        RequirePositive(request.Id, "id");
        return Run(context, async ct => GrpcMapping.ToReply(await screenings.GetByIdAsync(request.Id, ct)));
    }

    public override Task<CatalogService.Grpc.ScreeningsReply> GetScreeningsByIds(CatalogService.Grpc.GetScreeningsByIdsRequest request, ServerCallContext context)
    {
        RequireBatch(request.Ids.Count);
        foreach (var id in request.Ids) RequirePositive(id, "ids");
        return Run(context, async ct =>
        {
            var reply = new CatalogService.Grpc.ScreeningsReply();
            reply.Screenings.AddRange((await screenings.GetByIdsAsync(request.Ids, ct)).Select(GrpcMapping.ToReply));
            return reply;
        });
    }

    public override Task<CatalogService.Grpc.ScreeningsReply> GetUpcomingScreenings(CatalogService.Grpc.GetUpcomingScreeningsRequest request, ServerCallContext context)
    {
        RequirePositive(request.MovieId, "movie_id");
        if (request.Limit is < 0 or > MaxBatch)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"limit має бути від 0 до {MaxBatch}"));
        return Run(context, async ct =>
        {
            var page = await screenings.GetListAsync(new ScreeningQuery
            {
                MovieId = request.MovieId, PageSize = request.Limit == 0 ? 20 : request.Limit, SortBy = "startsAt"
            }, ct);
            var reply = new CatalogService.Grpc.ScreeningsReply();
            reply.Screenings.AddRange(page.Items.Select(GrpcMapping.ToReply));
            return reply;
        });
    }

    /// <summary>Виклик BLL зі скасуванням з ServerCallContext і перетворенням доменних винятків на gRPC-статуси.</summary>
    private async Task<T> Run<T>(ServerCallContext context, Func<CancellationToken, Task<T>> action)
    {
        try
        {
            return await action(context.CancellationToken);
        }
        catch (EntityNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.NotFound, ex.Message));
        }
        catch (ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (ConflictException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (DomainException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Виклик скасовано клієнтом"));
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            logger.LogError(ex, "gRPC {Method}: необроблена помилка", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Внутрішня помилка каталогу"));
        }
    }

    private static void RequirePositive(long id, string field)
    {
        if (id <= 0) throw new RpcException(new Status(StatusCode.InvalidArgument, $"{field}: Id має бути додатним"));
    }

    private static void RequireBatch(int count)
    {
        if (count is 0 or > MaxBatch)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"ids: від 1 до {MaxBatch} значень"));
    }
}

/// <summary>DTO BLL → повідомлення gRPC. Гроші — Money (units + nanos), час — Timestamp.</summary>
public static class GrpcMapping
{
    public static CatalogService.Grpc.MovieReply ToReply(MovieDto m)
    {
        var reply = new CatalogService.Grpc.MovieReply
        {
            Id = m.Id, Title = m.Title, ReleaseYear = m.ReleaseYear, DurationMinutes = m.DurationMinutes,
            AgeRating = m.AgeRating, RatingsCount = m.RatingsCount
        };
        if (m.OriginalTitle is not null) reply.OriginalTitle = m.OriginalTitle;
        if (m.AverageRating is { } avg) reply.AverageRating = (double)avg;
        if (m.Details is { } d)
        {
            reply.Description = d.Description;
            if (d.Director is not null) reply.Director = d.Director;
            if (d.Country is not null) reply.Country = d.Country;
            if (d.Language is not null) reply.Language = d.Language;
        }
        reply.Genres.AddRange(m.Genres.Select(g => new CatalogService.Grpc.GenreReply
            { Id = g.GenreId, Name = g.Name, Slug = g.Slug, IsPrimary = g.IsPrimary }));
        return reply;
    }

    public static CatalogService.Grpc.ScreeningReply ToReply(ScreeningDto s) => new()
    {
        Id = s.Id, MovieId = s.MovieId, MovieTitle = s.MovieTitle, HallId = s.HallId, HallName = s.HallName,
        StartsAt = ToTimestamp(s.StartsAt), EndsAt = ToTimestamp(s.EndsAt), Format = s.Format, Language = s.Language,
        BasePrice = ToMoney(s.BasePrice), IsCancelled = s.IsCancelled
    };

    public static CatalogService.Grpc.Money ToMoney(decimal amount)
    {
        var units = decimal.Truncate(amount);
        return new CatalogService.Grpc.Money
        {
            Units = (long)units, Nanos = (int)((amount - units) * 1_000_000_000m), CurrencyCode = "UAH"
        };
    }

    /// <summary>Час у базі — місцевий час кінотеатру без зсуву; Timestamp вимагає UTC, тож передаємо «як є».</summary>
    public static Timestamp ToTimestamp(DateTime value) => Timestamp.FromDateTime(DateTime.SpecifyKind(value, DateTimeKind.Utc));
}
