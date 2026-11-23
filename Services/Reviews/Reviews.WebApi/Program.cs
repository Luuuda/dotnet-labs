using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using MongoDB.Driver.Core.Extensions.DiagnosticSources;
using Reviews.Infrastructure.Persistence;
using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.WebApi.Infrastructure;
using Platform.ServiceDefaults;

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
app.MapDefaultEndpoints();                        // Лаб. 05: /health (self + mongodb), /alive

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
