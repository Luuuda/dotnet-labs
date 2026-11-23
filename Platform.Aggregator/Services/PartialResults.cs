using System.Diagnostics;
using Grpc.Core;

namespace Platform.Aggregator.Services;

/// <summary>
/// Обгортка виклику залежного сервісу для часткових відмов: якщо сервіс недоступний (помилка HTTP, RpcException Unavailable / DeadlineExceeded, тайм-аут,
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
        catch (RpcException ex) when (ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded && !ct.IsCancellationRequested)
        {
            // Лаб. 06: gRPC-сервіс недоступний або не встиг до дедлайну → попередження, а не 500
            logger.LogWarning("{Service} недоступний (gRPC {GrpcStatus}): {Operation} не виконано за {ElapsedMs} мс",
                service, ex.StatusCode, operation, sw.ElapsedMilliseconds);
            return Fail<T>(service, operation, ex.StatusCode.ToString());
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "{Service} недоступний: {Operation} не виконано за {ElapsedMs} мс ({ErrorType})",
                service, operation, sw.ElapsedMilliseconds, ex.GetType().Name);
            return Fail<T>(service, operation, ex.GetType().Name);
        }
    }

    private T? Fail<T>(string service, string operation, string errorType)
    {
        Warn($"{service} is unavailable: {operation} — дані цього сервісу відсутні у відповіді");
        Activity.Current?.AddEvent(new ActivityEvent("partial-failure",
            tags: new ActivityTagsCollection { ["service"] = service, ["operation"] = operation, ["error.type"] = errorType }));
        return default;
    }
}
