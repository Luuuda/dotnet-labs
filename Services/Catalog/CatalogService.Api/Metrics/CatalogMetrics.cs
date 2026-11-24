using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Caching.Memory;

namespace CatalogService.Api.Metrics;

/// <summary>
/// Лаб. 07: бізнес-метрики каталогу (OpenTelemetry Meter API). Meter «CatalogService.Api» створюється через IMeterFactory
/// і підключений в Platform.ServiceDefaults (BusinessMeters). Клас реєструється як Singleton.
/// <list type="bullet">
/// <item>Counter movies.created / movies.updated (тег movie.change: details | genres) / movies.deleted {movie};</item>
/// <item>Counter screenings.scheduled і screenings.price_changed (тег screening.format: 2D | 3D | IMAX | 4DX),
/// screenings.cancelled;</item>
/// <item>Histogram catalog.operation.duration (ms) — теги catalog.operation, operation.result (success | failure);</item>
/// <item>ObservableGauge catalog.screenings.upcoming, catalog.movies.active — знімок раз на 30 с;</item>
/// <item>ObservableGauge catalog.cache.entries — кількість записів у L1 (IMemoryCache) на момент збору.</item>
/// </list>
/// Теги мають малу кількість значень; Id фільмів і сеансів у теги не потрапляють.
/// </summary>
public sealed class CatalogMetrics
{
    public const string MeterName = "CatalogService.Api";

    private readonly Counter<long> _moviesCreated;
    private readonly Counter<long> _moviesUpdated;
    private readonly Counter<long> _moviesDeleted;
    private readonly Counter<long> _screeningsScheduled;
    private readonly Counter<long> _pricesChanged;
    private readonly Counter<long> _screeningsCancelled;
    private readonly Histogram<double> _operationDuration;

    private long _upcomingScreenings;
    private long _activeMovies;

    public CatalogMetrics(IMeterFactory meterFactory, IMemoryCache memoryCache)
    {
        var meter = meterFactory.Create(MeterName);

        _moviesCreated = meter.CreateCounter<long>("movies.created", "{movie}", "Фільми, додані до каталогу");
        _moviesUpdated = meter.CreateCounter<long>("movies.updated", "{movie}", "Оновлення карток фільмів (дані, жанри)");
        _moviesDeleted = meter.CreateCounter<long>("movies.deleted", "{movie}", "Фільми, зняті з каталогу");
        _screeningsScheduled = meter.CreateCounter<long>("screenings.scheduled", "{screening}", "Заплановані сеанси");
        _pricesChanged = meter.CreateCounter<long>("screenings.price_changed", "{screening}", "Зміни ціни сеансу");
        _screeningsCancelled = meter.CreateCounter<long>("screenings.cancelled", "{screening}", "Скасовані сеанси");
        _operationDuration = meter.CreateHistogram<double>("catalog.operation.duration", "ms",
            "Тривалість операцій каталогу (успішних і невдалих)");

        meter.CreateObservableGauge("catalog.screenings.upcoming", () => Interlocked.Read(ref _upcomingScreenings),
            "{screening}", "Сеанси, що ще попереду (не скасовані)");
        meter.CreateObservableGauge("catalog.movies.active", () => Interlocked.Read(ref _activeMovies),
            "{movie}", "Фільми в каталозі");
        meter.CreateObservableGauge("catalog.cache.entries",
            () => memoryCache.GetCurrentStatistics()?.CurrentEntryCount ?? 0,
            "{entry}", "Записи в L1-кеші (IMemoryCache) на момент збору");
    }

    public void MovieCreated() => _moviesCreated.Add(1);
    public void MovieUpdated(string change) => _moviesUpdated.Add(1, new KeyValuePair<string, object?>("movie.change", change));
    public void MovieDeleted() => _moviesDeleted.Add(1);

    public void ScreeningScheduled(string format) =>
        _screeningsScheduled.Add(1, new KeyValuePair<string, object?>("screening.format", format));

    public void ScreeningPriceChanged(string format) =>
        _pricesChanged.Add(1, new KeyValuePair<string, object?>("screening.format", format));

    public void ScreeningCancelled() => _screeningsCancelled.Add(1);

    public void RecordDuration(string operation, bool success, double elapsedMs) =>
        _operationDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("catalog.operation", operation),
            new KeyValuePair<string, object?>("operation.result", success ? "success" : "failure"));

    /// <summary>Виконує операцію й записує тривалість і для успіху, і для винятку (404, 409, 400).</summary>
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            RecordDuration(operation, success: true, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch
        {
            RecordDuration(operation, success: false, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    public async Task MeasureAsync(string operation, Func<Task> action) =>
        await MeasureAsync<bool>(operation, async () => { await action(); return true; });

    public void UpdateSnapshot(long upcomingScreenings, long activeMovies)
    {
        Interlocked.Exchange(ref _upcomingScreenings, upcomingScreenings);
        Interlocked.Exchange(ref _activeMovies, activeMovies);
    }
}
