using Serilog.Context;

namespace Reviews.WebApi.Infrastructure;

/// <summary>
/// Додає до КОЖНОГО запису логу запиту структуровані властивості CorrelationId, UserId, RequestPath.
/// CorrelationId береться із заголовка X-Correlation-ID (або генерується) і повертається у відповіді.
/// </summary>
public sealed class LogContextMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string CorrelationItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : context.TraceIdentifier;
        context.Items[CorrelationItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        var userId = context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : "anonymous";

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("RequestPath", context.Request.Path.Value))
            await next(context);
    }
}
