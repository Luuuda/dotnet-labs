using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();          // логування, трасування, service discovery, CorrelationIdHandler для всіх HttpClient

// --- Лаб. 06: типізовані gRPC-клієнти (основний транспорт) -----------------------------------------------------
// Адреса — логічне ім'я ресурсу; "_grpc." вибирає іменований endpoint "grpc" сервісу (окремий порт Kestrel лише з HTTP/2).
// Клієнти створює та сама фабрика HttpClient, тож діють налаштування ServiceDefaults: service discovery,
// resilience і CorrelationIdHandler (X-Correlation-Id потрапляє в метадані кожного gRPC-виклику).
builder.Services.AddSingleton<GrpcClientLoggingInterceptor>();
builder.Services.AddGrpcClient<BookingService.Grpc.BookingGrpc.BookingGrpcClient>(o => o.Address = new("http://_grpc.booking-api"))
    .AddInterceptor<GrpcClientLoggingInterceptor>();
builder.Services.AddGrpcClient<CatalogService.Grpc.CatalogGrpc.CatalogGrpcClient>(o => o.Address = new("http://_grpc.catalog-api"))
    .AddInterceptor<GrpcClientLoggingInterceptor>();
builder.Services.AddGrpcClient<Reviews.Grpc.ReviewsGrpc.ReviewsGrpcClient>(o => o.Address = new("http://_grpc.reviews-api"))
    .AddInterceptor<GrpcClientLoggingInterceptor>();
builder.Services.AddKeyedScoped<IBookingSource, GrpcBookingSource>(Transports.Grpc);
builder.Services.AddKeyedScoped<ICatalogSource, GrpcCatalogSource>(Transports.Grpc);
builder.Services.AddKeyedScoped<IReviewsSource, GrpcReviewsSource>(Transports.Grpc);

// Typed HttpClients з Лаб. 05 — лише для порівняння затримок HTTP vs gRPC (?transport=http)
builder.Services.AddHttpClient<BookingClient>(c => c.BaseAddress = new("https+http://booking-api"));
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new("https+http://catalog-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"));
builder.Services.AddKeyedTransient<IBookingSource>(Transports.Http, (sp, _) => sp.GetRequiredService<BookingClient>());
builder.Services.AddKeyedTransient<ICatalogSource>(Transports.Http, (sp, _) => sp.GetRequiredService<CatalogClient>());
builder.Services.AddKeyedTransient<IReviewsSource>(Transports.Http, (sp, _) => sp.GetRequiredService<ReviewsClient>());
builder.Services.AddScoped<ServiceSourcesFactory>();

// --- Лаб. 06: кеш агрегованих відповідей у Redis (короткий TTL, часткові відповіді не кешуються) ----------------
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
builder.Services.AddSingleton<AggregateCache>();

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
