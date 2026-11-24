using MediatR;
using Reviews.Application.Discussions.Queries;
using Reviews.Application.Reviews.Queries;

namespace Reviews.WebApi.Metrics;

/// <summary>
/// Лаб. 07: раз на 30 с оновлює знімок для gauge-метрик (кількість відгуків, відкриті теми) через запити CQRS.
/// Недоступна MongoDB не зупиняє сервіс: збій логується, gauge показує останній відомий знімок.
/// </summary>
public sealed class ReviewsMetricsSnapshotService(
    IServiceScopeFactory scopes, ReviewsMetrics metrics, ILogger<ReviewsMetricsSnapshotService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var sender = scope.ServiceProvider.GetRequiredService<ISender>();
                var reviews = await sender.Send(new GetReviewsListQuery(PageSize: 1), stoppingToken);
                var open = await sender.Send(new GetDiscussionsListQuery(Status: "open", PageSize: 1), stoppingToken);
                metrics.UpdateSnapshot(reviews.TotalCount, open.TotalCount);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Знімок метрик відгуків не оновлено: {Error}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
