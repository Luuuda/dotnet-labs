using System.Diagnostics;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace Platform.ApiGateway;

/// <summary>
/// Логує кожен запит, що пройшов через Gateway: метод, шлях, IP клієнта, обраний маршрут і кластер,
/// адресу сервісу (після service discovery), статус, тривалість і розмір відповіді.
/// Якщо сервіс недоступний — тип помилки YARP (connection refused, timeout) з деталями.
/// CorrelationId потрапляє в кожен запис з LogContext (CorrelationIdMiddleware стоїть раніше).
/// </summary>
public sealed class GatewayLoggingMiddleware(RequestDelegate next, ILogger<GatewayLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var sw = Stopwatch.StartNew();
        await next(context);
        sw.Stop();

        var proxy = context.Features.Get<IReverseProxyFeature>();
        if (proxy is null) return;                               // /health, / — обробляє сам Gateway

        var route = proxy.Route.Config.RouteId;
        var cluster = proxy.Cluster.Config.ClusterId;
        var destination = proxy.ProxiedDestination?.Model.Config.Address;
        var error = context.Features.Get<IForwarderErrorFeature>();

        if (error is not null)
        {
            logger.LogWarning(error.Exception,
                "{Method} {Path} -> route {Route} / cluster {Cluster} ({Destination}): сервіс недоступний — {ProxyError}, {StatusCode} за {ElapsedMs} мс",
                context.Request.Method, context.Request.Path, route, cluster, destination, error.Error,
                context.Response.StatusCode, sw.ElapsedMilliseconds);
            return;
        }

        logger.LogInformation(
            "{Method} {Path} від {ClientIp} -> route {Route} / cluster {Cluster} ({Destination}): {StatusCode} за {ElapsedMs} мс, {ResponseBytes} байт",
            context.Request.Method, $"{context.Request.Path}{context.Request.QueryString}", context.Connection.RemoteIpAddress?.ToString(),
            route, cluster, destination, context.Response.StatusCode, sw.ElapsedMilliseconds,
            context.Response.ContentLength);
    }
}
