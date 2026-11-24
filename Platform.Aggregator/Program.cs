using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Grpc.Health.V1;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Platform.Aggregator.Clients;
using Platform.Aggregator.HealthChecks;
using Platform.Aggregator.Metrics;
using Platform.Aggregator.Resilience;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults;
using StackExchange.Redis;
using static Microsoft.Extensions.Hosting.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();          // логування, трасування, service discovery, CorrelationIdHandler для всіх HttpClient

// --- Лаб. 06–07: типізовані gRPC-клієнти (основний транспорт) + політики стійкості ------------------------------
// Адреса — логічне ім'я ресурсу; "_grpc." вибирає іменований endpoint "grpc" сервісу (окремий порт Kestrel лише з HTTP/2).
// Клієнти створює та сама фабрика HttpClient, тож діють налаштування ServiceDefaults: service discovery,
// CorrelationIdHandler і Standard Resilience Handler з ConfigureHttpClientDefaults.
//
// Лаб. 07: критичність сервісів різна (Resilience/ServicePolicies.cs, README 8.2), тому для КОЖНОГО клієнта:
//   1) RemoveAllResilienceHandlers() — прибрати стандартний обробник ServiceDefaults (без нашарування);
//   2) AddStandardResilienceHandler().ApplyPolicy(...) — власні retry / circuit breaker / таймаути;
//   3) для gRPC — ConfigureChannel(ServiceConfig з RetryPolicy): повтори за gRPC-статусами Unavailable /
//      ResourceExhausted. HTTP-обробник їх не бачить (gRPC-статус приходить у трейлерах з HTTP 200), тому на
//      HTTP-рівні gRPC-клієнт повторює лише таймаут спроби, а решту дають таймаути й circuit breaker.
builder.Services.AddSingleton<GrpcClientLoggingInterceptor>();

builder.Services.AddGrpcClient<BookingService.Grpc.BookingGrpc.BookingGrpcClient>(o => o.Address = new("http://_grpc.booking-api"))
    .ConfigureChannel(channel => channel.ServiceConfig = ServicePolicy.Booking.GrpcServiceConfig())
    .AddInterceptor<GrpcClientLoggingInterceptor>()
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler()
    .ApplyPolicy(ServicePolicy.Booking, "grpc");

builder.Services.AddGrpcClient<CatalogService.Grpc.CatalogGrpc.CatalogGrpcClient>(o => o.Address = new("http://_grpc.catalog-api"))
    .ConfigureChannel(channel => channel.ServiceConfig = ServicePolicy.Catalog.GrpcServiceConfig())
    .AddInterceptor<GrpcClientLoggingInterceptor>()
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler()
    .ApplyPolicy(ServicePolicy.Catalog, "grpc");

builder.Services.AddGrpcClient<Reviews.Grpc.ReviewsGrpc.ReviewsGrpcClient>(o => o.Address = new("http://_grpc.reviews-api"))
    .ConfigureChannel(channel => channel.ServiceConfig = ServicePolicy.Reviews.GrpcServiceConfig())
    .AddInterceptor<GrpcClientLoggingInterceptor>()
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler()
    .ApplyPolicy(ServicePolicy.Reviews, "grpc");

builder.Services.AddKeyedScoped<IBookingSource, GrpcBookingSource>(Transports.Grpc);
builder.Services.AddKeyedScoped<ICatalogSource, GrpcCatalogSource>(Transports.Grpc);
builder.Services.AddKeyedScoped<IReviewsSource, GrpcReviewsSource>(Transports.Grpc);

// Typed HttpClients з Лаб. 05 (?transport=http) — ті самі політики; тут працюють і HTTP-повтори (лише GET).
builder.Services.AddHttpClient<BookingClient>(c => c.BaseAddress = new("https+http://booking-api"))
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler()
    .ApplyPolicy(ServicePolicy.Booking, "http");
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new("https+http://catalog-api"))
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler()
    .ApplyPolicy(ServicePolicy.Catalog, "http");
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"))
    .RemoveAllResilienceHandlers()
    .AddStandardResilienceHandler()
    .ApplyPolicy(ServicePolicy.Reviews, "http");
builder.Services.AddKeyedTransient<IBookingSource>(Transports.Http, (sp, _) => sp.GetRequiredService<BookingClient>());
builder.Services.AddKeyedTransient<ICatalogSource>(Transports.Http, (sp, _) => sp.GetRequiredService<CatalogClient>());
builder.Services.AddKeyedTransient<IReviewsSource>(Transports.Http, (sp, _) => sp.GetRequiredService<ReviewsClient>());
builder.Services.AddScoped<ServiceSourcesFactory>();

// --- Лаб. 06: кеш агрегованих відповідей у Redis (короткий TTL, часткові відповіді не кешуються) ----------------
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 })
    builder.AddRedisDistributedCache("redis",
        // Лаб. 07: власна перевірка Redis нижче (Degraded); вбудована перевірка Aspire зробила б сервіс Unhealthy
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
builder.Services.AddSingleton<AggregateCache>();

// --- Лаб. 07: бізнес-метрики (Metrics/AggregatorMetrics.cs, Meter «Platform.Aggregator») --------------------------
builder.Services.AddSingleton<AggregatorMetrics>();

// --- Лаб. 07: health checks залежностей (тег ready → /health/ready) ------------------------------------------------
// gRPC Health Checking Protocol кожного сервісу: окремий клієнт Health без resilience-обробників і з дедлайном 2 с.
// Статус збою відповідає критичності: booking-api і catalog-api — Unhealthy (картку не зібрати),
// reviews-api — Degraded (Aggregator повертає часткову відповідь без рейтингу).
foreach (var service in new[] { "booking-api", "catalog-api", "reviews-api" })
    builder.Services.AddGrpcClient<Health.HealthClient>(GrpcDependencyHealthCheck.ClientName(service),
            o => o.Address = new($"http://_grpc.{service}"))
        .RemoveAllResilienceHandlers();

var healthChecks = builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<GrpcDependencyHealthCheck>("booking-api", HealthStatus.Unhealthy,
        ["grpc", "downstream", HealthTags.Ready], TimeSpan.FromSeconds(3), "booking-api")
    .AddTypeActivatedCheck<GrpcDependencyHealthCheck>("catalog-api", HealthStatus.Unhealthy,
        ["grpc", "downstream", HealthTags.Ready], TimeSpan.FromSeconds(3), "catalog-api")
    .AddTypeActivatedCheck<GrpcDependencyHealthCheck>("reviews-api", HealthStatus.Degraded,
        ["grpc", "downstream", HealthTags.Ready], TimeSpan.FromSeconds(3), "reviews-api");
// Redis — необов'язкова залежність (кеш агрегованих відповідей): недоступність → Degraded, а не Unhealthy
if (builder.Configuration.GetConnectionString("redis") is { Length: > 0 })
    healthChecks.AddRedis(sp => sp.GetRequiredService<IConnectionMultiplexer>(),
        name: "redis-cache",
        failureStatus: HealthStatus.Degraded,
        tags: ["cache", "redis", HealthTags.Ready],
        timeout: TimeSpan.FromSeconds(3));

// JSON відповіді: camelCase (за замовчуванням), null-значення не серіалізуються
builder.Services.AddControllers()
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = System.Diagnostics.Activity.Current?.TraceId.ToString() ?? ctx.HttpContext.TraceIdentifier;
    if (ctx.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
        ctx.ProblemDetails.Extensions["correlationId"] = correlationId;
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new() { Title = "Cinema Aggregator", Version = "v1",
        Description = "Aggregator Pattern (Лаб. 05–06): картка бронювання і сторінка фільму з трьох мікросервісів; до сервісів — gRPC, агреговані відповіді кешуються в Redis." });
    var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
    if (File.Exists(xml)) o.IncludeXmlComments(xml);
});

var app = builder.Build();

app.UseCorrelationId();
app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    // обов'язковий сервіс (booking-api) недоступний → 503 ProblemDetails, а не 500
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var problem = error is RequiredServiceUnavailableException unavailable
        ? new ProblemDetails { Status = 503, Title = "Залежний сервіс недоступний", Detail = unavailable.Message }
        : new ProblemDetails { Status = 500, Title = "Внутрішня помилка агрегатора" };
    context.Response.StatusCode = problem.Status!.Value;
    await context.RequestServices.GetRequiredService<IProblemDetailsService>()
        .WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem, Exception = error });
}));

app.UseSwagger();
app.UseSwaggerUI(o => o.DocumentTitle = "Cinema Aggregator");

app.MapDefaultEndpoints();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();

app.Run();

