using System.Reflection;
using BookingService.Api.Infrastructure;
using BookingService.Bll;
using System.Text;
using Serilog;

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

builder.Services.AddControllers();

// --- Помилки: доменні винятки → HTTP-статуси, формат ProblemDetails (RFC 9457) ----------
builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    if (ctx.HttpContext.Items[CorrelationIdMiddleware.ItemKey] is string correlationId)
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

app.UseCorrelationId();                         // Лаб. 05: X-Correlation-Id з Gateway (або новий) → LogContext і відповідь
app.UseMiddleware<CorrelationIdMiddleware>();   // CorrelationId — у кожен запис логу цього запиту
app.UseSerilogRequestLogging();                 // один рядок на запит з ФІНАЛЬНИМ статусом (після обробника)
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
app.MapDefaultEndpoints();                      // Лаб. 05: /health, /alive
app.MapControllers();

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
