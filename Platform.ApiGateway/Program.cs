using Platform.ApiGateway;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();                       // Serilog, OpenTelemetry, health checks, service discovery

// YARP: маршрути й кластери — у секції ReverseProxy (appsettings.json); адреси http://<ім'я-ресурсу>
// перетворює на реальні service discovery Aspire.
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddServiceDiscoveryDestinationResolver();

var app = builder.Build();

app.UseCorrelationId();                             // ДО MapReverseProxy: генерує X-Correlation-Id на вході в систему
app.UseMiddleware<GatewayLoggingMiddleware>();       // маршрут, кластер, статус, тривалість, помилки сервісів

app.MapDefaultEndpoints();                          // /health, /alive

app.MapGet("/", (IConfiguration config) => Results.Ok(new
{
    service = "Cinema API Gateway",
    routes = config.GetSection("ReverseProxy:Routes").GetChildren()
        .Select(r => new { route = r.Key, path = r["Match:Path"], cluster = r["ClusterId"] })
})).ExcludeFromDescription();

app.MapReverseProxy();

app.Run();
