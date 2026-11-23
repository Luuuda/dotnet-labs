using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CatalogService.Bll.Caching;

/// <summary>
/// Прогрівання кешу (додаткове завдання): довідники й перша сторінка афіші потрапляють у L1/L2 одразу після старту
/// і оновлюються раз на <see cref="Period"/>, тож перші запити користувачів не йдуть у БД.
/// </summary>
public sealed class CacheWarmupService(IServiceScopeFactory scopes, ILogger<CacheWarmupService> logger) : BackgroundService
{
    private static readonly TimeSpan Period = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);           // дати застосунку завершити міграції
        using var timer = new PeriodicTimer(Period);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var genres = await scope.ServiceProvider.GetRequiredService<IGenreService>().GetAllAsync(stoppingToken);
                var halls = await scope.ServiceProvider.GetRequiredService<IGenreService>().GetHallsAsync(stoppingToken);
                var movies = await scope.ServiceProvider.GetRequiredService<IMovieService>()
                    .GetListAsync(new MovieQuery { Page = 1, PageSize = 20 }, stoppingToken);
                logger.LogInformation("Cache warm-up: жанрів {GenresCount}, залів {HallsCount}, фільмів на першій сторінці {MoviesCount}",
                    genres.Count, halls.Count, movies.Items.Count);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Cache warm-up не вдалося — дані завантажаться при першому запиті");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
