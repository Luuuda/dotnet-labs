using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace Platform.ServiceDefaults;

/// <summary>
/// Наскрізний ідентифікатор звернення. Створюється один раз на вході в систему (у Gateway) і передається
/// всіма сервісами далі. На відміну від TraceId (технічний ідентифікатор OpenTelemetry), CorrelationId —
/// бізнес-ідентифікатор, який клієнт отримує в заголовку відповіді й може назвати підтримці.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-Id";
    public const string TraceHeaderName = "X-Trace-Id";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var generated = false;
        if (!context.Request.Headers.TryGetValue(HeaderName, out var value) || string.IsNullOrWhiteSpace(value))
        {
            value = Guid.NewGuid().ToString();
            context.Request.Headers[HeaderName] = value;      // далі його побачать YARP, HttpClient і код сервісу
            generated = true;
        }

        var correlationId = value.ToString();
        context.Items[ItemKey] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        var traceId = Activity.Current?.TraceId.ToString();
        // Заголовки ставимо перед відправкою відповіді і ПЕРЕЗАПИСУЄМО: у Gateway YARP копіює такі самі заголовки
        // з відповіді сервісу, і без перезапису клієнт отримав би значення двічі («id, id»).
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            if (traceId is not null) context.Response.Headers[TraceHeaderName] = traceId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path.Value))
        {
            if (generated)
                logger.LogInformation("Згенеровано новий CorrelationId {CorrelationId} для {Method} {Path}",
                    correlationId, context.Request.Method, context.Request.Path);

            var sw = Stopwatch.StartNew();
            logger.LogInformation("Request started {Method} {Path}", context.Request.Method, context.Request.Path);
            try
            {
                await next(context);
            }
            finally
            {
                logger.LogInformation("Request finished {Method} {Path} with {StatusCode} in {ElapsedMs} ms",
                    context.Request.Method, context.Request.Path, context.Response.StatusCode, sw.ElapsedMilliseconds);
            }
        }
    }
}

/// <summary>Додає CorrelationId поточного вхідного запиту до вихідних HTTP-запитів (Aggregator → мікросервіси).</summary>
public sealed class CorrelationIdHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var id = accessor.HttpContext?.Request.Headers[CorrelationIdMiddleware.HeaderName].ToString();
        if (!string.IsNullOrEmpty(id) && !request.Headers.Contains(CorrelationIdMiddleware.HeaderName))
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.HeaderName, id);
        return base.SendAsync(request, ct);
    }
}
