using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// 4-й (найближчий до обробника) behavior: вимірює час САМОГО обробника (разом із запитами до MongoDB).
/// Запис «обробник виконано» з'являється лише тоді, коли обробник справді викликався.
/// Повільні (довші за Performance:SlowRequestMs, 500 мс за замовчуванням) — попередження.
/// </summary>
public sealed class PerformanceBehavior<TRequest, TResponse>(
    ILogger<PerformanceBehavior<TRequest, TResponse>> logger, IConfiguration configuration)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    private readonly int _thresholdMs = configuration.GetValue("Performance:SlowRequestMs", 500);

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var response = await next();
        sw.Stop();

        var name = typeof(TRequest).Name;
        if (sw.ElapsedMilliseconds > _thresholdMs)
            logger.LogWarning("Повільний обробник {RequestName}: {ElapsedMs} мс (поріг {ThresholdMs} мс) {@Request}",
                name, sw.ElapsedMilliseconds, _thresholdMs, request);
        else
            logger.LogInformation("Обробник {RequestName} виконано за {ElapsedMs} мс", name, sw.ElapsedMilliseconds);
        return response;
    }
}
