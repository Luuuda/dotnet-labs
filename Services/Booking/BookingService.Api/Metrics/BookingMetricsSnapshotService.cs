using BookingService.Bll.Services;

namespace BookingService.Api.Metrics;

/// <summary>
/// Лаб. 07: раз на 15 с читає з БД кількість бронювань за статусом і передає знімок у <see cref="BookingMetrics"/>.
/// Callback ObservableGauge виконується під час збору метрик і має бути миттєвим, тому запит до БД винесено сюди.
/// Недоступна база не зупиняє сервіс: збій логується, gauge показує останній відомий знімок.
/// </summary>
public sealed class BookingMetricsSnapshotService(
    IServiceScopeFactory scopes, BookingMetrics metrics, ILogger<BookingMetricsSnapshotService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var counts = await scope.ServiceProvider.GetRequiredService<IBookingService>().GetStatusCountsAsync(stoppingToken);
                metrics.UpdateSnapshot(counts);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Знімок метрик бронювань не оновлено: {Error}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
