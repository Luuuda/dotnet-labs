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

    // Лаб. 07: окремі endpoints для оркестратора (Aspire зараз, Kubernetes у Лаб. 10)
    public const string LivenessEndpointPath = "/health/live";      // лише процес (тег live), < 100 мс
    public const string ReadinessEndpointPath = "/health/ready";    // залежності (тег ready), JSON з деталями
    public const string StartupEndpointPath = "/health/startup";    // ініціалізація (тег startup): міграції, індекси

    /// <summary>Теги health checks, за якими перевірки розподіляються між endpoints (Predicate у MapHealthChecks).</summary>
    public static class HealthTags
    {
        public const string Live = "live";
        public const string Ready = "ready";
        public const string Startup = "startup";
    }

    /// <summary>Джерела власних spans бізнес-операцій: Cinema.Booking, Cinema.Catalog, Cinema.Reviews, Cinema.Aggregator…</summary>
    public const string BusinessActivitySources = "Cinema.*";

    /// <summary>
    /// Лаб. 06: Meter-и метрик кешу (лічильники cache.hits / cache.misses з тегом cache.level = L1 | L2).
    /// Кожен сервіс створює свій Meter через IMeterFactory у папці Caching/; тут вони підключаються до експорту OTLP.
    /// </summary>
    public static readonly string[] CacheMeters =
        ["Cinema.Catalog.Cache", "Cinema.Booking.Cache", "Cinema.Reviews.Cache", "Cinema.Aggregator.Cache"];

    /// <summary>
    /// Лаб. 07: Meter-и бізнес-метрик. Назва Meter = назва сервісу (проєкту); клас метрик кожного сервісу лежить у
    /// &lt;Сервіс&gt;.Api/Metrics/ і створює Meter через IMeterFactory.
    /// </summary>
    public static readonly string[] BusinessMeters =
        ["BookingService.Api", "CatalogService.Api", "Reviews.WebApi", "Platform.Aggregator"];

    /// <summary>
    /// Лаб. 07: Meter, через який Polly (Microsoft.Extensions.Http.Resilience) публікує resilience.polly.strategy.events
    /// (OnRetry, OnCircuitOpened, OnCircuitHalfOpened, OnCircuitClosed, OnTimeout), resilience.polly.pipeline.duration
    /// і resilience.polly.strategy.attempt.duration.
    /// </summary>
    public const string PollyMeter = "Polly";

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
            // Лаб. 07: Standard Resilience Handler для ВСІХ HttpClient і gRPC-клієнтів (вони створюються тією самою
            // фабрикою): rate limiter → загальний таймаут → retry → circuit breaker → таймаут спроби.
            // Значення за замовчуванням (Microsoft Learn): 3 повтори від 2 с з jitter, breaker 10 % при ≥ 100 запитах
            // за 30 с, таймаут спроби 10 с, загальний 30 с; за потреби — секція HttpClientResilience в appsettings.
            // Клієнти з іншою критичністю (Aggregator) спершу викликають RemoveAllResilienceHandlers() і додають
            // власний обробник — другий AddStandardResilienceHandler() поверх цього нашарував би повтори й таймаути.
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
            // Лаб. 07: події resilience — повтор (Warning), розмикання ланцюга (Error), повернення в Closed (Information)
            .MinimumLevel.Override("Polly", LogEventLevel.Information)
            // клієнт gRPC: службові Debug-записи (Sending/Reading message) приховано, оцінку повторів — лишено
            .MinimumLevel.Override("Grpc", LogEventLevel.Information)
            .MinimumLevel.Override("Grpc.Net.Client.Internal.Retry", LogEventLevel.Debug)
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
                .AddMeter(BusinessMeters)                    // Лаб. 07: бізнес-метрики сервісів (<Сервіс>.Api/Metrics)
                .AddMeter(PollyMeter)                        // Лаб. 07: події resilience (retry, circuit breaker, timeout)
                .AddMeter(GrpcMeters)
                .AddMeter("Platform.Messaging"))             // Лаб. 08: опубліковані / оброблені події, час обробки
            .WithTracing(tracing => tracing
                .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
                .AddSource(builder.Environment.ApplicationName)
                .AddSource(BusinessActivitySources)
                .AddSource(MongoActivitySource)
                // Лаб. 08: span-и публікації (Producer) і обробки (Consumer) повідомлень RabbitMQ (Platform.EventBus/TracePropagation),
                // зв'язані через W3C traceparent у заголовках: HTTP → Publish → Consume → DB в одному trace
                .AddSource("Platform.Messaging")
                .AddAspNetCoreInstrumentation(options =>
                {
                    options.RecordException = true;
                    // health checks (HTTP і gRPC Health Checking Protocol) не засмічують трасування
                    options.Filter = context => !IsHealthProbe(context.Request.Path);
                })
                .AddHttpClientInstrumentation(options =>
                {
                    options.RecordException = true;
                    options.FilterHttpRequestMessage = request => !IsHealthProbe(request.RequestUri?.AbsolutePath);
                })
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

    /// <summary>
    /// Перевірка "self" (тег live) — єдина, що потрапляє в liveness: відповідає миттєво й не звертається до баз.
    /// Перевірки залежностей (SQL Server, MongoDB, Redis, gRPC) кожен сервіс додає у своєму Program.cs з тегом ready.
    /// </summary>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder) where TBuilder : IHostApplicationBuilder
    {
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("Процес відповідає"), [HealthTags.Live]);
        return builder;
    }

    /// <summary>
    /// Лаб. 07: endpoints стану. На відміну від шаблону Aspire (лише Development), вони публікуються в УСІХ
    /// середовищах: їх опитують Aspire (WithHttpHealthCheck) і Kubernetes (probes у Лаб. 10). Відповіді не містять
    /// секретів — лише назви перевірок, статуси, описи й тривалість.
    /// <list type="bullet">
    /// <item>/health/live — liveness: лише тег live (процес живий), без зовнішніх залежностей → збій = перезапуск;</item>
    /// <item>/health/ready — readiness: тег ready (бази, Redis, gRPC-залежності) з JSON-деталями кожної перевірки →
    /// збій = виключення з балансування. Degraded (Redis недоступний) лишається 200: сервіс працює без кешу;</item>
    /// <item>/health/startup — тег startup (міграції, індекси): для сервісів із повільним стартом;</item>
    /// <item>/health — усі перевірки з деталями (його опитує Aspire Dashboard), /alive — синонім liveness з шаблону.</item>
    /// </list>
    /// </summary>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        app.MapHealthChecks(LivenessEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthTags.Live),
            ResponseWriter = HealthResponseWriter.WriteAsync
        });
        app.MapHealthChecks(ReadinessEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthTags.Ready),
            ResponseWriter = HealthResponseWriter.WriteAsync
        });
        app.MapHealthChecks(StartupEndpointPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(HealthTags.Startup),
            ResponseWriter = HealthResponseWriter.WriteAsync
        });
        app.MapHealthChecks(HealthEndpointPath, new HealthCheckOptions { ResponseWriter = HealthResponseWriter.WriteAsync });
        app.MapHealthChecks(AlivenessEndpointPath, new HealthCheckOptions { Predicate = check => check.Tags.Contains(HealthTags.Live) });
        return app;
    }

    /// <summary>Запити health checks: /health*, /alive і gRPC Health Checking Protocol (grpc.health.v1.Health).</summary>
    public static bool IsHealthProbe(string? path) =>
        path is not null
        && (path.StartsWith(HealthEndpointPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(AlivenessEndpointPath, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("/grpc.health.v1.Health/", StringComparison.OrdinalIgnoreCase));

    /// <summary>CorrelationId: читається із заголовка або генерується, кладеться в LogContext і відповідь (Частина 4.2).</summary>
    public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
