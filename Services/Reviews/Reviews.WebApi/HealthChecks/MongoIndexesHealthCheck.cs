using Microsoft.Extensions.Diagnostics.HealthChecks;
using Reviews.Application.Common.Interfaces;

namespace Reviews.WebApi.HealthChecks;

/// <summary>
/// Лаб. 07: власна перевірка готовності MongoDB — індекси Лаб. 04 створено. Доступність сервера (ping) перевіряє
/// AddMongoDb; ця перевірка — що IndexCreationService відпрацював (унікальний ux_author_movie: один відгук глядача
/// на фільм). Сховище — за інтерфейсом <see cref="IStoreReadiness"/>, тож WebApi не знає про колекції MongoDB.
/// Теги: ready і startup.
/// </summary>
public sealed class MongoIndexesHealthCheck(IServiceScopeFactory scopes) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var readiness = await scope.ServiceProvider.GetRequiredService<IStoreReadiness>().CheckIndexesAsync(ct);
            var data = new Dictionary<string, object> { ["indexes"] = string.Join(", ", readiness.Indexes) };
            return readiness.IsReady
                ? HealthCheckResult.Healthy(readiness.Description, data)
                : new HealthCheckResult(context.Registration.FailureStatus, readiness.Description, data: data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "MongoDB недоступна для перевірки індексів", ex);
        }
    }
}
