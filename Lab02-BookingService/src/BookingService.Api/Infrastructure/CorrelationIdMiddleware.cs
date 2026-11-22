using Serilog.Context;

namespace BookingService.Api.Infrastructure;

/// <summary>
/// Кореляційний ідентифікатор: береться із заголовка X-Correlation-ID (або генерується),
/// повертається у відповіді та додається до КОЖНОГО запису логу цього запиту.
/// У лог не потрапляють тіла запитів — отже, жодних паролів чи номерів карток.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string ItemKey = "CorrelationId";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value.ToString()
            : context.TraceIdentifier;

        context.Items[ItemKey] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
            await next(context);
    }
}
