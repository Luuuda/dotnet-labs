using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using MediatR;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Models;
using Reviews.Application.Movies.Queries;
using Reviews.Application.Reviews.Queries;
using Reviews.Domain.Exceptions;

namespace Reviews.WebApi.Grpc;

/// <summary>
/// gRPC-адаптер над Application (Лаб. 06): надсилає ті самі запити MediatR, що й REST-контролери, тож проходить
/// увесь pipeline (Logging → Validation → Caching → …) і користується тим самим кешем L1/L2.
/// Винятки Application/Domain → RpcException з відповідним StatusCode.
/// </summary>
public sealed class ReviewsGrpcService(ISender sender, ILogger<ReviewsGrpcService> logger) : global::Reviews.Grpc.ReviewsGrpc.ReviewsGrpcBase
{
    private const int MaxBatch = 50;

    public override Task<global::Reviews.Grpc.MovieRatingReply> GetMovieRating(global::Reviews.Grpc.GetMovieRatingRequest request, ServerCallContext context)
    {
        RequirePositive(request.MovieId, "movie_id");
        return Run(context, async ct => ToReply(await sender.Send(new GetMovieRatingQuery(request.MovieId), ct)));
    }

    public override Task<global::Reviews.Grpc.ReviewsReply> GetTopReviews(global::Reviews.Grpc.GetTopReviewsRequest request, ServerCallContext context)
    {
        RequirePositive(request.MovieId, "movie_id");
        if (request.Limit is < 0 or > 20)
            throw new RpcException(new Status(StatusCode.InvalidArgument, "limit має бути від 0 до 20"));
        return Run(context, async ct =>
        {
            var page = await sender.Send(new GetReviewsListQuery(MovieId: request.MovieId, SortBy: "rating",
                PageSize: request.Limit == 0 ? 3 : request.Limit), ct);
            var reply = new global::Reviews.Grpc.ReviewsReply { TotalCount = page.TotalCount };
            reply.Reviews.AddRange(page.Items.Select(ToReply));
            return reply;
        });
    }

    public override Task<global::Reviews.Grpc.MovieRatingsReply> GetRatingsForMovies(global::Reviews.Grpc.GetRatingsForMoviesRequest request, ServerCallContext context)
    {
        if (request.MovieIds.Count is 0 or > MaxBatch)
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"movie_ids: від 1 до {MaxBatch} значень"));
        foreach (var id in request.MovieIds) RequirePositive(id, "movie_ids");
        return Run(context, async ct =>
        {
            var reply = new global::Reviews.Grpc.MovieRatingsReply();
            foreach (var movieId in request.MovieIds.Distinct())
            {
                try { reply.Ratings.Add(ToReply(await sender.Send(new GetMovieRatingQuery(movieId), ct))); }
                catch (NotFoundException) { /* фільм без відгуків пропускаємо */ }
            }
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
        catch (Application.Common.Exceptions.ValidationException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument,
                string.Join("; ", ex.Errors.SelectMany(e => e.Value.Select(v => $"{e.Key}: {v}")))));
        }
        catch (ConflictException ex) { throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message)); }
        catch (DomainException ex) { throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message)); }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw new RpcException(new Status(StatusCode.Cancelled, "Виклик скасовано клієнтом"));
        }
        catch (Exception ex) when (ex is not RpcException)
        {
            logger.LogError(ex, "gRPC {Method}: необроблена помилка", context.Method);
            throw new RpcException(new Status(StatusCode.Internal, "Внутрішня помилка сервісу відгуків"));
        }
    }

    private static void RequirePositive(int id, string field)
    {
        if (id <= 0) throw new RpcException(new Status(StatusCode.InvalidArgument, $"{field}: Id фільму має бути додатним"));
    }

    internal static global::Reviews.Grpc.MovieRatingReply ToReply(MovieRatingDto r)
    {
        var reply = new global::Reviews.Grpc.MovieRatingReply
        {
            MovieId = r.MovieId, MovieTitle = r.MovieTitle, Average = r.Average, Count = r.Count, VerifiedCount = r.VerifiedCount
        };
        foreach (var (stars, count) in r.Distribution)
            if (int.TryParse(stars, out var s)) reply.Distribution[s] = count;
        return reply;
    }

    internal static global::Reviews.Grpc.ReviewReply ToReply(ReviewDto r)
    {
        var reply = new global::Reviews.Grpc.ReviewReply
        {
            Id = r.Id, MovieId = r.MovieId, AuthorName = r.Author.DisplayName, Rating = r.Rating,
            VerifiedViewer = r.VerifiedViewer, CommentsCount = r.CommentsCount,
            CreatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc))
        };
        if (r.Text is not null) reply.Text = r.Text;
        return reply;
    }
}
