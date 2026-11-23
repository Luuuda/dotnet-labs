using System.Diagnostics;
using System.Reflection;
using System.Text;
using CatalogService.Api.Grpc;
using CatalogService.Api.Infrastructure;
using CatalogService.Bll.Caching;
using CatalogService.Bll;
using Platform.ServiceDefaults;
using Platform.ServiceDefaults.Grpc;

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
// L2 — Redis через Aspire-інтеграцію (IDistributedCache + health check + трасування). Рядок підключення
// ConnectionStrings:redis передає AppHost; без нього (тести, самостійний запуск) — кеш у пам'яті замість Redis.
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 })
    builder.AddRedisDistributedCache("redis", configureOptions: o =>
    {
        o.AbortOnConnectFail = false;           // недоступний Redis не валить старт сервісу
        o.ConnectTimeout = 2000;                // збій кешу коштує ≤ 2 с, далі L2 пропускається на 30 с (TwoLevelCache)
        o.AsyncTimeout = 1000;
        o.SyncTimeout = 1000;
        o.UseIPv4Loopback();                    // localhost → 127.0.0.1: на Windows спроба ::1 додає ~2 с до з'єднання
    });
else
    builder.Services.AddDistributedMemoryCache();
builder.Services.AddHealthChecks().AddCheck<CacheStatisticsHealthCheck>("catalog-cache");

builder.Services.AddBusinessLogicLayer(connectionString,
    builder.Configuration["AutoMapper:LicenseKey"] is { Length: > 0 } key ? key : null,
    cacheWarmUp: builder.Configuration.GetValue("Cache:WarmUp", true));

// --- Лаб. 06: gRPC (AddGrpc + серверний interceptor логування з ServiceDefaults) ---------------
// REST і gRPC слухають різні порти Kestrel (appsettings.json): 5280 — HTTP/1.1 для Gateway, 5281 — лише HTTP/2 для gRPC без TLS.
builder.AddPlatformGrpc();

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
app.MapDefaultEndpoints();                        // Лаб. 05: /health, /alive
app.MapControllers();

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
