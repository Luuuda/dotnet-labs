using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Platform.Aggregator.Models;
using BookingProto = BookingService.Grpc;
using CatalogProto = CatalogService.Grpc;
using ReviewsProto = Reviews.Grpc;

namespace Platform.Aggregator.Clients;

/// <summary>
/// Лаб. 06: джерела даних через gRPC. Кожне обгортає типізований клієнт, згенерований з .proto сервісу
/// (GrpcServices="Client"), і зареєстрований через AddGrpcClient з адресою за логічним іменем ресурсу (service discovery).
/// gRPC-відповіді перетворюються на ті самі моделі, що й HTTP-відповіді в Лаб. 05, тож агрегатори не змінилися.
/// NOT_FOUND → null; UNAVAILABLE / DEADLINE_EXCEEDED та інші статуси — RpcException (обробляє PartialResults).
/// </summary>
internal static class GrpcCall
{
    /// <summary>Дедлайн кожного виклику: сервіс, що «завис», не тримає агрегацію довше за 3 с.</summary>
    public static CallOptions Options(CancellationToken ct) => new(deadline: DateTime.UtcNow.AddSeconds(3), cancellationToken: ct);

    public static async Task<T?> OrNullIfNotFound<T>(Func<Task<T>> call) where T : class
    {
        try { return await call(); }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound) { return null; }
    }

    /// <summary>Timestamp → DateTime без зсуву (у сервісах це місцевий час кінотеатру).</summary>
    public static DateTime Local(Timestamp? ts) => ts is null ? default : DateTime.SpecifyKind(ts.ToDateTime(), DateTimeKind.Unspecified);

    public static decimal Amount(long units, int nanos) => units + nanos / 1_000_000_000m;
}

public sealed class GrpcBookingSource(BookingProto.BookingGrpc.BookingGrpcClient client) : IBookingSource
{
    public string ServiceName => "booking-api";

    public Task<BookingDto?> GetBookingAsync(long id, CancellationToken ct) => GrpcCall.OrNullIfNotFound(async () =>
    {
        var b = await client.GetBookingAsync(new BookingProto.GetBookingRequest { Id = id }, GrpcCall.Options(ct));
        return new BookingDto(b.Id, Guid.Empty, b.Status, GrpcCall.Local(b.CreatedAt),
            GrpcCall.Amount(b.TotalAmount.Units, b.TotalAmount.Nanos),
            b.HasContactEmail ? b.ContactEmail : null, b.HasChannel ? b.Channel : null,
            b.Tickets.Select(t => new TicketDto(t.Id, t.ScreeningId, t.SeatRow, t.SeatNumber, t.MovieTitle, t.HallName,
                GrpcCall.Local(t.StartsAt), 0m, 0m, GrpcCall.Amount(t.FinalPrice.Units, t.FinalPrice.Nanos), t.IsCancelled)).ToList());
    });

    public Task<BookingScreeningDto?> GetScreeningCopyAsync(long id, CancellationToken ct) => GrpcCall.OrNullIfNotFound(async () =>
    {
        var s = await client.GetScreeningAsync(new BookingProto.GetScreeningRequest { Id = id }, GrpcCall.Options(ct));
        return new BookingScreeningDto(s.Id, s.MovieId, s.MovieTitle, GrpcCall.Local(s.StartsAt), s.IsActive);
    });
}

public sealed class GrpcCatalogSource(CatalogProto.CatalogGrpc.CatalogGrpcClient client) : ICatalogSource
{
    public string ServiceName => "catalog-api";

    public Task<CatalogScreeningDto?> GetScreeningAsync(long id, CancellationToken ct) => GrpcCall.OrNullIfNotFound(async () =>
        ToDto(await client.GetScreeningAsync(new CatalogProto.GetScreeningRequest { Id = id }, GrpcCall.Options(ct))));

    public Task<CatalogMovieDto?> GetMovieAsync(int id, CancellationToken ct) => GrpcCall.OrNullIfNotFound(async () =>
        ToDto(await client.GetMovieAsync(new CatalogProto.GetMovieRequest { Id = id }, GrpcCall.Options(ct))));

    public async Task<PageDto<CatalogScreeningDto>?> GetScreeningsOfMovieAsync(int movieId, CancellationToken ct)
    {
        var reply = await client.GetUpcomingScreeningsAsync(
            new CatalogProto.GetUpcomingScreeningsRequest { MovieId = movieId, Limit = 20 }, GrpcCall.Options(ct));
        var items = reply.Screenings.Select(ToDto).ToList();
        return new PageDto<CatalogScreeningDto>(items, items.Count, 1, 20);
    }

    public async Task<IReadOnlyList<CatalogMovieDto>> GetMoviesAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var request = new CatalogProto.GetMoviesByIdsRequest();
        request.Ids.AddRange(ids);
        var reply = await client.GetMoviesByIdsAsync(request, GrpcCall.Options(ct));   // один виклик на всі фільми
        return reply.Movies.Select(ToDto).ToList();
    }

    private static CatalogMovieDto ToDto(CatalogProto.MovieReply m) => new(m.Id, m.Title, m.HasOriginalTitle ? m.OriginalTitle : null,
        (short)m.ReleaseYear, (short)m.DurationMinutes, m.AgeRating,
        m.HasDescription || m.HasDirector
            ? new CatalogMovieDetailsDto(m.HasDescription ? m.Description : null, m.HasDirector ? m.Director : null, m.HasCountry ? m.Country : null)
            : null,
        m.Genres.Select(g => new CatalogGenreDto(g.Id, g.Name, g.Slug, g.IsPrimary)).ToList());

    private static CatalogScreeningDto ToDto(CatalogProto.ScreeningReply s) => new(
        s.Id, s.MovieId, s.MovieTitle, s.HallId, s.HallName, GrpcCall.Local(s.StartsAt), GrpcCall.Local(s.EndsAt),
        s.Format, s.Language, GrpcCall.Amount(s.BasePrice.Units, s.BasePrice.Nanos), s.IsCancelled);
}

public sealed class GrpcReviewsSource(ReviewsProto.ReviewsGrpc.ReviewsGrpcClient client) : IReviewsSource
{
    public string ServiceName => "reviews-api";

    public Task<MovieRatingDto?> GetRatingAsync(int movieId, CancellationToken ct) => GrpcCall.OrNullIfNotFound(async () =>
    {
        var r = await client.GetMovieRatingAsync(new ReviewsProto.GetMovieRatingRequest { MovieId = movieId }, GrpcCall.Options(ct));
        return new MovieRatingDto(r.MovieId, r.MovieTitle, r.Average, r.Count,
            r.Distribution.ToDictionary(p => p.Key.ToString(), p => p.Value), r.VerifiedCount);
    });

    public async Task<IReadOnlyList<MovieRatingDto>> GetRatingsAsync(IReadOnlyCollection<int> movieIds, CancellationToken ct)
    {
        var request = new ReviewsProto.GetRatingsForMoviesRequest();
        request.MovieIds.AddRange(movieIds);
        var reply = await client.GetRatingsForMoviesAsync(request, GrpcCall.Options(ct));   // один виклик на всі фільми
        return reply.Ratings.Select(r => new MovieRatingDto(r.MovieId, r.MovieTitle, r.Average, r.Count,
            r.Distribution.ToDictionary(p => p.Key.ToString(), p => p.Value), r.VerifiedCount)).ToList();
    }

    public Task<PageDto<ReviewDto>?> GetTopReviewsAsync(int movieId, int count, CancellationToken ct) => GrpcCall.OrNullIfNotFound(async () =>
    {
        var reply = await client.GetTopReviewsAsync(
            new ReviewsProto.GetTopReviewsRequest { MovieId = movieId, Limit = count }, GrpcCall.Options(ct));
        var items = reply.Reviews.Select(r => new ReviewDto(r.Id, r.MovieId, new ReviewAuthorDto("", r.AuthorName), r.Rating,
            r.HasText ? r.Text : null, r.VerifiedViewer, r.CommentsCount,
            DateTime.SpecifyKind(r.CreatedAt.ToDateTime(), DateTimeKind.Utc))).ToList();
        return new PageDto<ReviewDto>(items, reply.TotalCount, 1, count);
    });
}
