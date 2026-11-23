using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Reviews.Application;
using Reviews.Infrastructure;
using Reviews.WebApi.Infrastructure;
using Serilog;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(args);

// Serilog: структуровані логи (рівні й sinks — розділ "Serilog" в appsettings)
builder.Host.UseSerilog((ctx, services, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

// Кожен шар реєструє свої залежності сам
builder.Services
    .AddApplication(builder.Configuration, typeof(Program).Assembly)   // MediatR + behaviors, валідатори, AutoMapper
    .AddInfrastructure(builder.Configuration);                          // MongoDB, репозиторії, Queries, індекси, сідінг, health

builder.Services.AddControllers(o =>
        // уся валідація — у pipeline MediatR (ValidationBehavior), а не неявними [Required] від MVC
        o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true)
    .AddJsonOptions(o => o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull);

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

app.UseMiddleware<LogContextMiddleware>();        // CorrelationId, UserId, RequestPath у кожен запис логу
app.UseSerilogRequestLogging();
app.UseExceptionHandler();                        // винятки → ProblemDetails (GlobalExceptionHandler)
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI(o => o.DocumentTitle = "Cinema Reviews API");

app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapControllers();
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (ctx, report) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new { name = e.Key, status = e.Value.Status.ToString(), e.Value.Description })
        });
    }
});

app.Run();

/// <summary>Потрібен для API-тестів через WebApplicationFactory&lt;Program&gt;.</summary>
public partial class Program;
