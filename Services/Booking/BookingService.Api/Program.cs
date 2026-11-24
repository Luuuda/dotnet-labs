using System.Diagnostics;
using System.Reflection;
using System.Text;
using BookingService.Api.Grpc;
using BookingService.Api.HealthChecks;
using BookingService.Api.Infrastructure;
using BookingService.Api.Messaging;
using BookingService.Api.Metrics;
using BookingService.Bll.Caching;
using BookingService.Bll;
using Platform.Messaging;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Grpc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using static Microsoft.Extensions.Hosting.Extensions;

Console.OutputEncoding = Encoding.UTF8;   // українські повідомлення в консолі без «кракозябр»

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Лаб. 05: Serilog, OpenTelemetry, health checks, service discovery (Platform.ServiceDefaults)

// --- Крок A: конфігурація -------------------------------------------------------------
// Рядок підключення — в appsettings.json; перевизначається змінною оточення
// ConnectionStrings__CinemaBookingDb (саме так його передає .NET Aspire).
var connectionString = builder.Configuration.GetConnectionString("CinemaBookingDb")
    ?? throw new InvalidOperationException("Connection string 'CinemaBookingDb' is missing");
var commandTimeout = builder.Configuration.GetValue("Database:CommandTimeoutSeconds", 30);
var autoMapperKey = builder.Configuration["AutoMapper:LicenseKey"];

// --- DI: BLL (а під ним DAL з Unit of Work — Scoped, одне з'єднання на запит) -----------
builder.Services.AddBusinessLogicLayer(connectionString, commandTimeout,
    string.IsNullOrWhiteSpace(autoMapperKey) ? null : autoMapperKey);

// --- Лаб. 06: дворівневий кеш ----------------------------------------------------------------
// L1 — пам'ять процесу з обмеженням розміру (Size запису = КБ JSON; разом ≈ 5 МБ), витіснення 20 % за пріоритетом.
builder.Services.AddMemoryCache(o =>
{
    o.SizeLimit = 5_000;
    o.CompactionPercentage = 0.2;
    o.TrackStatistics = true;
});
// L2 — Redis (ConnectionStrings:redis від AppHost); без Redis — кеш у пам'яті (тести, окремий запуск)
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
    // SQL Server: легкий SELECT 1, таймаут 5 с; недоступна база → Unhealthy (сервіс без неї не працює)
    .AddSqlServer(
        connectionString: healthConnectionString,
        healthQuery: "SELECT 1;",
        name: "booking-db",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["database", "sql", HealthTags.Ready],
        timeout: TimeSpan.FromSeconds(5))
    // власна перевірка (HealthChecks/): схема Лаб. 01 розгорнута — таблиці й процедури на місці
    .AddCheck<BookingSchemaHealthCheck>("booking-db-schema", HealthStatus.Unhealthy,
        ["database", HealthTags.Ready, HealthTags.Startup], TimeSpan.FromSeconds(5))
    // статистика L1-кешу (Лаб. 06) — лише інформативно, без тегу ready
    .AddCheck<CacheStatisticsHealthCheck>("booking-cache", tags: ["cache"]);
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

// --- Лаб. 08: RabbitMQ — споживач подій Каталогу (Messaging/CatalogEventsConsumer) ------------------------------
// IConnection реєструє Aspire-інтеграція: рядок ConnectionStrings:messaging передає AppHost (WithReference(rabbitmq)).
// Трасування повідомлень робить Platform.Messaging (span-и Producer / Consumer, W3C traceparent у заголовках),
// тому вбудоване трасування клієнта вимкнено — без дубльованих span-ів. Health check реєструє Aspire.
// Без брокера (тести, окремий запуск) споживач не реєструється.
if (builder.Configuration.GetConnectionString("messaging") is { Length: > 0 })
{
    builder.AddRabbitMQClient("messaging", settings => settings.DisableTracing = true);   // IConnection + health check «RabbitMQ.Client»
    builder.Services.AddHostedService<CatalogEventsConsumer>();
    builder.Services.AddRabbitMqToReadiness(HealthTags.Ready);   // перевірку Aspire — у /health/ready зі статусом Degraded
}

// --- Лаб. 07: бізнес-метрики (Metrics/, Meter «BookingService.Api») ------------------------------------------------
builder.Services.AddSingleton<BookingMetrics>();
builder.Services.AddHostedService<BookingMetricsSnapshotService>();

// --- Лаб. 06: gRPC: окремий порт Kestrel лише з HTTP/2 (appsettings.json, Kestrel:Endpoints:Grpc) ---------
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcServerLoggingInterceptor>();          // серверний interceptor логування (Platform.ServiceDefaults/Grpc)
    options.EnableDetailedErrors = builder.Environment.IsDevelopment(); // деталі винятків у відповіді — лише в Development
});

builder.Services.AddControllers();

// --- Помилки: доменні винятки → HTTP-статуси, формат ProblemDetails (RFC 9457) ----------
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
    if (ctx.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)   // CorrelationId з ServiceDefaults
        ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
});
builder.Services.AddExceptionHandler<DomainExceptionHandler>();

// --- OpenAPI / Swagger ------------------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new() { Title = "Cinema Booking API", Version = "v1",
        Description = "Мікросервіс бронювання квитків (Лаб. 02): DAL на ADO.NET + Dapper, BLL з Unit of Work, Web API." });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseCorrelationId();                         // Лаб. 05: X-Correlation-Id з Gateway (або новий) → LogContext, відповідь, лог початку/кінця запиту
app.UseExceptionHandler();                      // доменні винятки → 404 / 409 / 400 (DomainExceptionHandler)
app.UseStatusCodePages();                       // порожні 4xx/5xx теж повертаються як ProblemDetails

// Порожня база (контейнер Aspire) → застосувати скрипти Лаб. 01. Для наявної бази вимкнено.
if (app.Configuration.GetValue("Database:ApplyScriptsOnStartup", false))
    await app.Services.InitializeDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.DocumentTitle = "Cinema Booking API");
}

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGrpcService<BookingGrpcService>();      // Лаб. 06: gRPC-контракт booking.proto
app.MapGrpcHealthChecksService();               // Лаб. 07: grpc.health.v1.Health на gRPC-порту
app.MapDefaultEndpoints();                      // Лаб. 07: /health/live, /health/ready, /health/startup, /health (ServiceDefaults)
app.MapControllers();

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
