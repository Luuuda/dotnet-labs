using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Reviews.Infrastructure.Persistence;
using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.Application.Caching;
using Reviews.WebApi.Grpc;
using Reviews.WebApi.HealthChecks;
using Reviews.WebApi.Infrastructure;
using Reviews.WebApi.Metrics;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Grpc;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;
using static Microsoft.Extensions.Hosting.Extensions;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Лаб. 05: Serilog, OpenTelemetry, health checks, service discovery

// Лаб. 05: рядок підключення від Aspire (ConnectionStrings:ReviewsDb) → секція MongoDb, яку читає Infrastructure.
// Без Aspire лишаються резервні значення з appsettings.json.
if (builder.Configuration.GetConnectionString("ReviewsDb") is { Length: > 0 } reviewsDb)
{
    builder.Configuration["MongoDb:ConnectionString"] = reviewsDb;
    builder.Configuration["MongoDb:DatabaseName"] = MongoUrl.Create(reviewsDb).DatabaseName ?? "cinema_reviews";
}

// --- Лаб. 06: дворівневий кеш (CachingBehavior у pipeline MediatR) --------------------------------
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
var healthChecks = builder.Services.AddHealthChecks()
    // MongoDB: команда ping через клієнт, яким користується Infrastructure; таймаут 5 с; недоступна → Unhealthy
    .AddMongoDb(
        clientFactory: sp => sp.GetRequiredService<MongoDbContext>().Client,
        databaseNameFactory: sp => sp.GetRequiredService<MongoDbContext>().Database.DatabaseNamespace.DatabaseName,
        name: "reviews-db",
        failureStatus: HealthStatus.Unhealthy,
        tags: ["database", "mongodb", HealthTags.Ready],
        timeout: TimeSpan.FromSeconds(5))
    // власна перевірка (HealthChecks/): індекси Лаб. 04 створено (унікальність «один відгук на фільм»)
    .AddCheck<MongoIndexesHealthCheck>("reviews-db-indexes", HealthStatus.Unhealthy,
        ["database", HealthTags.Ready, HealthTags.Startup], TimeSpan.FromSeconds(5))
    // статистика L1-кешу (Лаб. 06) — лише інформативно, без тегу ready
    .AddCheck<CacheStatisticsHealthCheck>("reviews-cache", tags: ["cache"]);
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

// --- Лаб. 07: бізнес-метрики (Metrics/, Meter «Reviews.WebApi») ----------------------------------------------------
builder.Services.AddSingleton<ReviewsMetrics>();
builder.Services.AddHostedService<ReviewsMetricsSnapshotService>();

// --- Лаб. 06: gRPC: окремий порт Kestrel лише з HTTP/2 (appsettings.json, Kestrel:Endpoints:Grpc) ---------
builder.Services.AddGrpc(options =>
{
    options.Interceptors.Add<GrpcServerLoggingInterceptor>();          // серверний interceptor логування (Platform.ServiceDefaults/Grpc)
    options.EnableDetailedErrors = builder.Environment.IsDevelopment(); // деталі винятків у відповіді — лише в Development
});

// Кожен шар реєструє свої залежності сам
builder.Services
    .AddApplication(builder.Configuration, typeof(Program).Assembly)   // MediatR + behaviors, валідатори, AutoMapper
    .AddInfrastructure(builder.Configuration);                          // MongoDB, репозиторії, Queries, індекси, сідінг, health

// Лаб. 05: spans команд MongoDB (find, insert, aggregate…) у трасуванні OpenTelemetry
builder.Services.PostConfigure<MongoDbOptions>(o => o.ConfigureClientSettings = settings =>
    settings.ClusterConfigurator = cluster => cluster.Subscribe(
        new DiagnosticsActivityEventSubscriber(new InstrumentationOptions { CaptureCommandText = true })));

builder.Services.AddControllers(o =>
        // уся валідація — у pipeline MediatR (ValidationBehavior), а не неявними [Required] від MVC
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

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
    options.SwaggerDoc("v1", new()
    {
        Title = "Cinema Reviews API",
        Version = "v1",
        Description = "Мікросервіс відгуків і обговорень (Лаб. 04): Clean Architecture, CQRS + MediatR, Pipeline Behaviors, MongoDB."
    });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) options.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseCorrelationId();                           // Лаб. 05: X-Correlation-Id з Gateway (або новий) → LogContext, відповідь, лог початку/кінця запиту
app.UseExceptionHandler();                        // винятки → ProblemDetails (GlobalExceptionHandler)
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI(o => o.DocumentTitle = "Cinema Reviews API");

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapGrpcService<ReviewsGrpcService>();         // Лаб. 06: gRPC-контракт reviews.proto
app.MapGrpcHealthChecksService();                 // Лаб. 07: grpc.health.v1.Health на gRPC-порту
app.MapDefaultEndpoints();                        // Лаб. 07: /health/live, /health/ready, /health/startup, /health (ServiceDefaults)

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
