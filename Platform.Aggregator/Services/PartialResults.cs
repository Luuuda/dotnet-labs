using System.Diagnostics;

namespace Platform.Aggregator.Services;

/// <summary>
/// Обгортка виклику залежного сервісу для часткових відмов: якщо сервіс недоступний (помилка HTTP, тайм-аут,
/// розімкнений Circuit Breaker), повертається default, а в перелік Warnings додається попередження.
/// Скасування запиту клієнтом НЕ ковтається — зупиняються всі вихідні виклики.
/// </summary>
public sealed class PartialResults(ILogger logger, CancellationToken ct)
{
    private readonly List<string> _warnings = [];

    public IReadOnlyList<string> Warnings
    {
        get { lock (_warnings) return _warnings.Distinct().ToList(); }
    }

    public void Warn(string message)
    {
        lock (_warnings) _warnings.Add(message);
    }

    public async Task<T?> SafeAsync<T>(string service, string operation, Func<Task<T?>> call)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await call();
            logger.LogInformation("{Service} {Operation} — відповідь за {ElapsedMs} мс", service, operation, sw.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Service} недоступний: {Operation} не виконано за {ElapsedMs} мс ({ErrorType})",
                service, operation, sw.ElapsedMilliseconds, ex.GetType().Name);
            Warn($"{service} is unavailable: {operation} — дані цього сервісу відсутні у відповіді");
            Activity.Current?.AddEvent(new ActivityEvent("partial-failure",
                tags: new ActivityTagsCollection { ["service"] = service, ["operation"] = operation, ["error.type"] = ex.GetType().Name }));
            return default;
        }
    }
}
