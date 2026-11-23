using System.Reflection;
using System.Text;
using CatalogService.Api.Infrastructure;
using CatalogService.Bll;
using Serilog;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Рядок підключення: appsettings / змінна оточення ConnectionStrings__CatalogDb (так його передає Aspire)
var connectionString = builder.Configuration.GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException("Connection string 'CatalogDb' is missing");

// Serilog: структуровані логи, sinks і рівні — у appsettings.json (розділ "Serilog").
// SQL-команди EF Core (Microsoft.EntityFrameworkCore.Database.Command) логуються на рівні Information.
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services.AddBusinessLogicLayer(connectionString,
    builder.Configuration["AutoMapper:LicenseKey"] is { Length: > 0 } key ? key : null);

builder.Services.AddControllers();

builder.Services.AddProblemDetails(o => o.CustomizeProblemDetails = ctx =>
{
    ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    if (ctx.HttpContext.Items[LogContextMiddleware.CorrelationItemKey] is string correlationId)
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

app.UseMiddleware<LogContextMiddleware>();        // CorrelationId, UserId, RequestPath → у кожен запис логу
app.UseSerilogRequestLogging();
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
app.MapControllers();

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
