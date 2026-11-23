using System.Diagnostics;
using System.Reflection;
using System.Text;
using CatalogService.Api.Infrastructure;
using CatalogService.Bll;
using Platform.ServiceDefaults;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();   // Лаб. 05: Serilog, OpenTelemetry (у т. ч. SQL), health checks, service discovery

// Рядок підключення: appsettings / змінна оточення ConnectionStrings__CatalogDb (так його передає Aspire)
var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException("Connection string 'CatalogDb' is missing");

builder.Services.AddBusinessLogicLayer(connectionString,
    builder.Configuration["AutoMapper:LicenseKey"] is { Length: > 0 } key ? key : null);

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
app.MapDefaultEndpoints();                        // Лаб. 05: /health, /alive
app.MapControllers();

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
