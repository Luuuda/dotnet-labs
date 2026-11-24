using Grpc.Core;
using Grpc.Health.V1;
using Grpc.Net.ClientFactory;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.Aggregator.HealthChecks;

/// <summary>
/// Лаб. 07: перевірка gRPC-залежності Aggregator через стандартний gRPC Health Checking Protocol
/// (grpc.health.v1.Health/Check). Сервіс відповідає за своїми перевірками з тегом ready (база, Redis), тож
/// NOT_SERVING означає «процес живий, але його база недоступна».
/// <list type="bullet">
/// <item>короткий дедлайн 2 с — недоступність виявляється швидко, а не за таймаутом за замовчуванням;</item>
/// <item>RpcException (Unavailable, DeadlineExceeded…) → статус збою реєстрації (Unhealthy / Degraded), збій логується;</item>
/// <item>перевірка ідемпотентна й без побічних ефектів: лише читання статусу.</item>
/// </list>
/// Клієнт Health створюється за іменем (<see cref="ClientName"/>) без resilience-обробників: повтори зробили б
/// перевірку повільною, а її результат має відображати стан «тут і зараз».
/// </summary>
public sealed class GrpcDependencyHealthCheck(
    GrpcClientFactory clientFactory, ILogger<GrpcDependencyHealthCheck> logger, string service) : IHealthCheck
{
    public static readonly TimeSpan Deadline = TimeSpan.FromSeconds(2);

    public static string ClientName(string service) => $"{service}-health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var client = clientFactory.CreateClient<Health.HealthClient>(ClientName(service));
        try
        {
            var reply = await client.CheckAsync(new HealthCheckRequest(),
                deadline: DateTime.UtcNow.Add(Deadline), cancellationToken: ct);

            return reply.Status == HealthCheckResponse.Types.ServingStatus.Serving
                ? HealthCheckResult.Healthy($"{service}: SERVING (gRPC health)")
                : new HealthCheckResult(context.Registration.FailureStatus, $"{service}: {reply.Status} (gRPC health)");
        }
        catch (RpcException ex)
        {
            logger.LogWarning("Health check {Service}: gRPC {GrpcStatus} — {GrpcDetail}", service, ex.StatusCode, ex.Status.Detail);
            return new HealthCheckResult(context.Registration.FailureStatus,
                $"{service} недоступний: gRPC {ex.StatusCode}", ex,
                new Dictionary<string, object> { ["grpcStatus"] = ex.StatusCode.ToString() });
        }
    }
}
