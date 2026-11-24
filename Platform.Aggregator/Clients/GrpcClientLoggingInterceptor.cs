using System.Diagnostics;
using Grpc.Core;
using Grpc.Core.Interceptors;

namespace Platform.Aggregator.Clients;

/// <summary>
/// Клієнтський interceptor (Лаб. 06): логує кожен вихідний gRPC-виклик Aggregator — метод, статус, тривалість.
/// CorrelationId окремо додавати не треба: gRPC-клієнти створює та сама фабрика HttpClient, і CorrelationIdHandler
/// з ServiceDefaults кладе X-Correlation-Id у заголовки HTTP/2 — тобто в метадані gRPC-виклику.
/// </summary>
public sealed class GrpcClientLoggingInterceptor(ILogger<GrpcClientLoggingInterceptor> logger) : Interceptor
{
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context, AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        var sw = Stopwatch.StartNew();
        var call = continuation(request, context);
        return new AsyncUnaryCall<TResponse>(LogAsync(call.ResponseAsync, context.Method.FullName, sw),
            call.ResponseHeadersAsync, call.GetStatus, call.GetTrailers, call.Dispose);
    }

    private async Task<TResponse> LogAsync<TResponse>(Task<TResponse> response, string method, Stopwatch sw)
    {
        try
        {
            var result = await response;
            // успішні виклики вже логує PartialResults (сервіс, операція, тривалість) — тут лише Trace, щоб не дублювати
            logger.LogTrace("gRPC call {GrpcMethod}: {GrpcStatus} in {ElapsedMs} ms", method, StatusCode.OK, sw.ElapsedMilliseconds);
            return result;
        }
        catch (RpcException ex)
        {
            logger.LogWarning("gRPC call {GrpcMethod}: {GrpcStatus} in {ElapsedMs} ms — {GrpcDetail}",
                method, ex.StatusCode, sw.ElapsedMilliseconds, ex.Status.Detail);
            throw;
        }
    }
}
