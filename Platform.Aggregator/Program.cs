using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Platform.Aggregator.Clients;
using Platform.Aggregator.Services;
using Platform.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();          // логування, трасування, service discovery, CorrelationIdHandler для всіх HttpClient

// Typed HttpClients: адреса — логічне ім'я ресурсу з AppHost ("https+http" = HTTPS, якщо є, інакше HTTP)
builder.Services.AddHttpClient<BookingClient>(c => c.BaseAddress = new("https+http://booking-api"));
builder.Services.AddHttpClient<CatalogClient>(c => c.BaseAddress = new("https+http://catalog-api"));
builder.Services.AddHttpClient<ReviewsClient>(c => c.BaseAddress = new("https+http://reviews-api"));

builder.Services.AddScoped<BookingCardAggregator>();
builder.Services.AddScoped<MovieCardAggregator>();

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
        Description = "Aggregator Pattern (Лаб. 05): картка бронювання і сторінка фільму з трьох мікросервісів." });
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
