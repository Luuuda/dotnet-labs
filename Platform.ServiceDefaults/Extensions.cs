using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Platform.ServiceDefaults;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Compact;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Наскрізні налаштування, які підключає КОЖЕН сервіс платформи одним викликом <c>builder.AddServiceDefaults()</c>.
/// Структура — як у шаблоні aspire-servicedefaults та eShop.ServiceDefaults, доповнена Serilog і CorrelationId.
/// </summary>
public static class Extensions
{
    public const string HealthEndpointPath = "/health";
    public const string AlivenessEndpointPath = "/alive";

    /// <summary>Джерела власних spans бізнес-операцій: Cinema.Booking, Cinema.Catalog, Cinema.Reviews, Cinema.Aggregator…</summary>
    public const string BusinessActivitySources = "Cinema.*";

    /// <summary>
    /// Лаб. 06: Meter-и метрик кешу (лічильники cache.hits / cache.misses з тегом cache.level = L1 | L2).
    /// Кожен сервіс створює свій Meter через IMeterFactory у папці Caching/; тут вони підключаються до експорту OTLP.
    /// </summary>
    public static readonly string[] CacheMeters =
        ["Cinema.Catalog.Cache", "Cinema.Booking.Cache", "Cinema.Reviews.Cache", "Cinema.Aggregator.Cache"];

    /// <summary>Вбудовані метрики gRPC (тривалість і кількість викликів сервера та клієнта).</summary>
    public static readonly string[] GrpcMeters = ["Grpc.AspNetCore.Server", "Grpc.Net.Client"];

    /// <summary>ActivitySource драйвера MongoDB (пакет MongoDB.Driver.Core.Extensions.DiagnosticSources).</summary>
    public const string MongoActivitySource = "MongoDB.Driver.Core.Extensions.DiagnosticSources";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.ConfigureSerilog();
        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();
        builder.Services.AddHostedService<Platform.ServiceDefaults.DistributedCacheWarmup>();   // Лаб. 06: з'єднання з Redis — на старті

        builder.Services.AddServiceDiscovery();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddTransient<CorrelationIdHandler>();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retry + Circuit Breaker + Timeout (детально — Лаб. 07). Параметри можна змінити секцією
            // HttpClientResilience в appsettings сервісу (так Aggregator скорочує очікування недоступних сервісів).
            http.AddStandardResilienceHandler(options =>
                builder.Configuration.GetSection("HttpClientResilience").Bind(options));
            http.AddServiceDiscovery();                                 // http://booking-api → реальна адреса від Aspire
            http.AddHttpMessageHandler<CorrelationIdHandler>();         // X-Correlation-Id у кожен вихідний запит
        });

        return builder;
    }

    /// <summary>
    /// Serilog — основний провайдер логування: кожен запис — об'єкт з полями (ServiceName, Environment, MachineName,
    /// ThreadId, CorrelationId з LogContext, TraceId/SpanId з Activity). Консоль — CompactJson;
    /// writeToProviders: true передає записи провайдеру OpenTelemetry → вкладка Structured logs у Dashboard.
    /// </summary>
    public static TBuilder ConfigureSerilog<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Лишаємо лише провайдери, яким Serilog передає записи (OpenTelemetry додається нижче),
        // інакше стандартний Console-провайдер дублював би вивід.
        builder.Logging.ClearProviders();

        var isDevelopment = builder.Environment.IsDevelopment();
        builder.Services.AddSerilog((services, cfg) => cfg
            .MinimumLevel.Is(isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information)
            // фільтрація шумних логів інфраструктури
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            // оброблені доменні винятки (404/409/400) логують самі IExceptionHandler-и, без дубля рівня Error від middleware
            .MinimumLevel.Override("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .MinimumLevel.Override("Polly", LogEventLevel.Warning)
            .MinimumLevel.Override("Yarp", LogEventLevel.Warning)
            .MinimumLevel.Override("LuckyPennySoftware", LogEventLevel.Warning)
            .Enrich.FromLogContext()                                                    // CorrelationId і властивості запиту
            .Enrich.WithProperty("ServiceName", builder.Environment.ApplicationName)
            .Enrich.WithProperty("Environment", builder.Environment.EnvironmentName)
            .Enrich.WithEnvironmentName()
            .Enrich.WithMachineName()
            .Enrich.WithThreadId()
            .WriteTo.Console(new CompactJsonFormatter()),                               // JSON у вкладці Console logs
            writeToProviders: true);                                                    // → OpenTelemetry → Structured logs

        return builder;
    }

    /// <summary>
    /// Трасування: вхідні запити ASP.NET Core, вихідні HttpClient, SqlClient (Dapper з Лаб. 02 і EF Core з Лаб. 03),
    /// MongoDB (Лаб. 04) і власні spans бізнес-операцій. Метрики ASP.NET Core / HttpClient / runtime.
    /// Експорт — OTLP в Aspire Dashboard (адресу задає Aspire змінною OTEL_EXPORTER_OTLP_ENDPOINT).
    /// </summary>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        // 100 % у Development, у Production — частка з конфігурації (OpenTelemetry:SamplingRatio, за замовчуванням 25 %)
        var samplingRatio = builder.Environment.IsDevelopment()
            ? 1.0
            : builder.Configuration.GetValue("OpenTelemetry:SamplingRatio", 0.25);

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddAttributes(
            [
                new("deployment.environment", builder.Environment.EnvironmentName),
                new("service.namespace", "cinema")
            ]))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddMeter(BusinessActivitySources)
                .AddMeter(CacheMeters)                       // Лаб. 06: метрики попадань / промахів кешу
                .AddMeter(GrpcMeters))
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(BusinessActivitySources)
                .AddSource(MongoActivitySource)
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    // health checks не засмічують трасування
                    options.Filter = context =>
                        !context.Request.Path.StartsWithSegments(HealthEndpointPath)
                        && !context.Request.Path.StartsWithSegments(AlivenessEndpointPath);
                })
                .AddHttpClientInstrumentation(options => options.RecordException = true)
                .AddSqlClientInstrumentation());

        builder.AddOpenTelemetryExporters();
        return builder;
    }

    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        // Батчевий експорт (за замовчуванням) не блокує обробку запитів
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        return builder;
    }

    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);
        return builder;
    }

    /// <summary>
    /// /health — готовність (усі перевірки, включно з перевірками баз, які зареєстрував сервіс),
    /// /alive — лише «процес живий» (тег live).
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = r => r.Tags.Contains("live") });
        return app;
    }

    /// <summary>CorrelationId: читається із заголовка або генерується, кладеться в LogContext і відповідь (Частина 4.2).</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
