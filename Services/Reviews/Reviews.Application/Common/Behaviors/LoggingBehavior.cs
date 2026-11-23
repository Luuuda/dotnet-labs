using System.Diagnostics;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// 1-й (зовнішній) behavior: структурований лог кожної команди/запиту — що надійшло і чим завершилось.
/// Якщо запит відхилено валідацією, тут видно «→ отримано» і «✗ відхилено», а запису обробника немає.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(ILogger<LoggingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        var name = typeof(TRequest).Name;
        logger.LogInformation("MediatR → {RequestName} отримано {@Request}", name, request);
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await next();
            logger.LogInformation("MediatR ← {RequestName} виконано за {ElapsedMs} мс; відповідь: {ResponseType}",
                name, sw.ElapsedMilliseconds, response?.GetType().Name ?? "—");
            return response;
        }
        catch (Exception ex)
        {
            logger.LogInformation("MediatR ✗ {RequestName} завершено з {ExceptionType} за {ElapsedMs} мс",
                name, ex.GetType().Name, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
