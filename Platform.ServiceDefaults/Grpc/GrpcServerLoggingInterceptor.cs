using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;

namespace Platform.ServiceDefaults.Grpc;

/// <summary>
/// Серверний interceptor (Лаб. 06): логує кожен вхідний gRPC-виклик структурованими властивостями —
/// метод, peer (адреса клієнта), статус, тривалість, CorrelationId з метаданих — і доповнює span сервера.
/// Помилки логуються зі статусом: клієнтські (NotFound, InvalidArgument…) — Warning, серверні — Error зі stack trace.
/// Підключається до всіх сервісів через <see cref="GrpcExtensions.AddPlatformGrpc"/>.
/// </summary>
public sealed class GrpcServerLoggingInterceptor(ILogger<GrpcServerLoggingInterceptor> logger) : Interceptor
{
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request, ServerCallContext context, UnaryServerMethod<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        var correlationId = context.RequestHeaders.GetValue(CorrelationIdMiddleware.HeaderName.ToLowerInvariant());
        Activity.Current?.SetTag("grpc.peer", context.Peer);
        try
        {
            var response = await continuation(request, context);
            logger.LogInformation("gRPC {GrpcMethod} from {GrpcPeer}: {GrpcStatus} in {ElapsedMs} ms (CorrelationId {GrpcCorrelationId})",
                context.Method, context.Peer, StatusCode.OK, sw.ElapsedMilliseconds, correlationId);
            return response;
        }
        catch (RpcException ex)
        {
            Activity.Current?.SetTag("grpc.status", ex.StatusCode.ToString());
            if (IsServerError(ex.StatusCode))
                logger.LogError(ex, "gRPC {GrpcMethod} from {GrpcPeer}: {GrpcStatus} in {ElapsedMs} ms — {GrpcDetail}",
                    context.Method, context.Peer, ex.StatusCode, sw.ElapsedMilliseconds, ex.Status.Detail);
            else
                logger.LogWarning("gRPC {GrpcMethod} from {GrpcPeer}: {GrpcStatus} in {ElapsedMs} ms — {GrpcDetail}",
                    context.Method, context.Peer, ex.StatusCode, sw.ElapsedMilliseconds, ex.Status.Detail);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "gRPC {GrpcMethod} from {GrpcPeer}: необроблений виняток за {ElapsedMs} ms",
                context.Method, context.Peer, sw.ElapsedMilliseconds);
            throw;
        }
    }

    private static bool IsServerError(StatusCode code) =>
        code is StatusCode.Internal or StatusCode.Unknown or StatusCode.DataLoss or StatusCode.Unimplemented;
}
