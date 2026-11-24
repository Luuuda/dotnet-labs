using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;

namespace CatalogService.Api.Metrics;

/// <summary>
/// Лаб. 07: раз на 30 с оновлює знімок для gauge-метрик каталогу (найближчі сеанси, фільми в каталозі).
/// Запити йдуть через BLL з кешем Лаб. 06, тож навантаження на БД мінімальне; збій бази лише логується.
/// </summary>
public sealed class CatalogMetricsSnapshotService(
    IServiceScopeFactory scopes, CatalogMetrics metrics, ILogger<CatalogMetricsSnapshotService> logger) : BackgroundService
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
                var screenings = scope.ServiceProvider.GetRequiredService<IScreeningService>();
                var movies = scope.ServiceProvider.GetRequiredService<IMovieService>();
                var upcoming = await screenings.GetListAsync(new ScreeningQuery { From = DateTime.UtcNow, PageSize = 1 }, stoppingToken);
                var active = await movies.GetListAsync(new MovieQuery { PageSize = 1 }, stoppingToken);
                metrics.UpdateSnapshot(upcoming.TotalCount, active.TotalCount);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning("Знімок метрик каталогу не оновлено: {Error}", ex.Message);
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
