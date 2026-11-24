using System.Diagnostics;
using System.Reflection;
using System.Text;
using CatalogService.Api.Grpc;
using CatalogService.Api.HealthChecks;
using CatalogService.Api.IntegrationEvents;
using CatalogService.Api.Infrastructure;
using CatalogService.Api.Metrics;
using CatalogService.Bll.Caching;
using CatalogService.Bll;
using CatalogService.Bll.Services;
using Platform.EventBus;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Grpc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using static Microsoft.Extensions.Hosting.Extensions;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Лаб. 05: Serilog, OpenTelemetry (у т. ч. SQL), health checks, service discovery

// Рядок підключення: appsettings / змінна оточення ConnectionStrings__CatalogDb (так його передає Aspire)
var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException("Connection string 'CatalogDb' is missing");

// --- Лаб. 06: дворівневий кеш ----------------------------------------------------------------
// L1 — пам'ять процесу з обмеженням розміру (одиниця Size = 1 КБ серіалізованого DTO, разом ≈ 10 МБ);
// при досягненні ліміту витісняються 20 % записів, спершу з нижчим пріоритетом (Low → Normal → High).
builder.Services.AddMemoryCache(o =>
{
    o.SizeLimit = 10_000;
    o.CompactionPercentage = 0.2;
    o.ExpirationScanFrequency = TimeSpan.FromSeconds(30);
    o.TrackStatistics = true;                                       // статистика для health check «catalog-cache»
});
// L2 — Redis через Aspire-інтеграцію (IDistributedCache + трасування). Рядок підключення
// ConnectionStrings:redis передає AppHost; без нього (тести, самостійний запуск) — кеш у пам'яті замість Redis.
var redisConfigured = builder.Configuration.GetConnectionString("redis") is { Length: > 0 };
if (redisConfigured)
    builder.AddRedisDistributedCache("redis",
        // Лаб. 07: перевірку Redis реєструємо самі зі статусом Degraded; вбудована перевірка Aspire дала б Unhealthy
        configureSettings: settings => settings.DisableHealthChecks = true,
        configureOptions: o =>
    {
        o.AbortOnConnectFail = false;           // недоступний Redis не валить старт сервісу
        o.ConnectTimeout = 2000;                // збій кешу коштує ≤ 2 с, далі L2 пропускається на 30 с (TwoLevelCache)
        o.AsyncTimeout = 1000;
        o.SyncTimeout = 1000;
        o.UseIPv4Loopback();                    // localhost → 127.0.0.1: на Windows спроба ::1 додає ~2 с до з'єднання
        // Лаб. 07: graceful degradation — коли Redis недоступний, команди падають одразу (FailFast), а не чекають
        // у черзі AsyncTimeout; TwoLevelCache / AggregateCache ловлять помилку й читають з БД без затримки.
        o.BacklogPolicy = StackExchange.Redis.BacklogPolicy.FailFast;
    });
else
    builder.Services.AddDistributedMemoryCache();

// --- Лаб. 07: health checks (теги розподіляють їх між /health/live, /health/ready і /health/startup) ---------------
// Окреме з'єднання для перевірок: Connect Timeout 3 с, щоб недоступний SQL Server давав Unhealthy за секунди,
// а не за 15 с тайм-ауту підключення з рядка сервісу (readiness має відповідати швидше, ніж його опитують).
var healthConnectionString = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 3 }.ConnectionString;
var healthChecks = builder.Services.AddHealthChecks()
    // SQL Server: легкий SELECT 1, таймаут 5 с; недоступна база → Unhealthy (каталог без неї не працює)
    .AddSqlServer(
        connectionString: healthConnectionString,
        healthQuery: "SELECT 1;",
        name: "catalog-db",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["database", "sql", HealthTags.Ready],
        timeout: TimeSpan.FromSeconds(5))
    // власна перевірка (HealthChecks/): усі міграції EF Core застосовано
    .AddCheck<CatalogMigrationsHealthCheck>("catalog-db-migrations", HealthStatus.Unhealthy,
        ["database", HealthTags.Ready, HealthTags.Startup], TimeSpan.FromSeconds(5))
    // статистика L1-кешу (Лаб. 06) — лише інформативно, без тегу ready
    .AddCheck<CacheStatisticsHealthCheck>("catalog-cache", tags: ["cache"]);
if (redisConfigured)
    // Redis — необов'язкова залежність: PING, таймаут 3 с; недоступний → Degraded, а не Unhealthy
    healthChecks.AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(),
        name: "redis-cache",
        failureStatus: HealthStatus.Degraded,
        tags: ["cache", "redis", HealthTags.Ready],
        timeout: TimeSpan.FromSeconds(3));

// gRPC Health Checking Protocol (grpc.health.v1.Health): відповідає за тими самими перевірками з тегом ready.
// Його опитує Aggregator (HealthChecks/GrpcDependencyHealthCheck). Кеш вимкнено — статус обчислюється на запит.
builder.Services.AddGrpcHealthChecks(o =>
{
    o.Services.Map("", check => check.Tags.Contains(HealthTags.Ready));
    o.UseHealthChecksCache = false;
});

// --- Лаб. 08: RabbitMQ — публікація інтеграційних подій Каталогу (IEventPublisher, exchange catalog-events) --------
// IConnection реєструє Aspire-інтеграція (ConnectionStrings:messaging від AppHost); один канал публікації з publisher
// confirms на сервіс (Singleton). Трасування публікації — Platform.EventBus (span Producer + traceparent).
// Без брокера (тести, окремий запуск) події лише логуються (LoggingOnlyEventPublisher).
if (builder.Configuration.GetConnectionString("messaging") is { Length: > 0 })
{
    builder.AddRabbitMQClient("messaging", settings => settings.DisableTracing = true);   // IConnection + health check «RabbitMQ.Client»
    builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
    builder.Services.AddRabbitMqToReadiness(HealthTags.Ready);   // перевірку Aspire — у /health/ready зі статусом Degraded
}
else
    builder.Services.AddSingleton<IEventPublisher, LoggingOnlyEventPublisher>();
// Publisher подій Каталогу (IntegrationEvents/): бізнес-сервіси BLL викликають його після SaveChangesAsync
builder.Services.AddSingleton<ICatalogIntegrationEventService, CatalogIntegrationEventService>();

// --- Лаб. 07: бізнес-метрики (Metrics/, Meter «CatalogService.Api») ------------------------------------------------
builder.Services.AddSingleton<CatalogMetrics>();
builder.Services.AddHostedService<CatalogMetricsSnapshotService>();

builder.Services.AddBusinessLogicLayer(connectionString,
    builder.Configuration["AutoMapper:LicenseKey"] is { Length: > 0 } key ? key : null,
    cacheWarmUp: builder.Configuration.GetValue("Cache:WarmUp", true));

// --- Лаб. 06: gRPC (AddGrpc + серверний interceptor логування; MapGrpcService — нижче) -----------
// REST і gRPC слухають різні порти Kestrel (appsettings.json): 5280 — HTTP/1.1 для Gateway, 5281 — лише HTTP/2 для gRPC без TLS.
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcServerLoggingInterceptor>();          // серверний interceptor логування (Platform.ServiceDefaults/Grpc)
    options.EnableDetailedErrors = builder.Environment.IsDevelopment(); // деталі винятків у відповіді — лише в Development
});

builder.Services.AddControllers();

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
    if (ctx.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)   // CorrelationId з ServiceDefaults
        ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
});
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Cinema Catalog API", Version = "v1",
        Description = "Мікросервіс каталогу фільмів і розкладу (Лаб. 03): EF Core Code-First, Repository / Unit of Work, специфікації, пагінація." });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseCorrelationId();                           // Лаб. 05: X-Correlation-Id з Gateway (або новий) → LogContext, відповідь, лог початку/кінця запиту
app.UseExceptionHandler();                        // винятки → ProblemDetails (GlobalExceptionHandler)
app.UseStatusCodePages();

// Міграції + програмний сідінг на старті. Сідінг вимикається в Production (Database:SeedOnStartup).
if (app.Configuration.GetValue("Database:MigrateOnStartup", true))
    await app.Services.InitializeDatabaseAsync(
        seed: app.Configuration.GetValue("Database:SeedOnStartup", false) && !app.Environment.IsProduction());

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Cinema Catalog API");
}

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGrpcService<CatalogGrpcService>();         // Лаб. 06: gRPC-контракт catalog.proto
app.MapGrpcHealthChecksService();                 // Лаб. 07: grpc.health.v1.Health на gRPC-порту
app.MapDefaultEndpoints();                        // Лаб. 07: /health/live, /health/ready, /health/startup, /health (ServiceDefaults)
app.MapControllers();

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
