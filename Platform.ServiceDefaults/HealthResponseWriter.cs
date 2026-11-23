using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Platform.ServiceDefaults;

/// <summary>Відповідь /health у JSON: загальний стан і стан кожної перевірки (self, бази даних…).</summary>
internal static class HealthResponseWriter
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json; charset=utf-8";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            totalMs = (long)report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                ms = (long)e.Value.Duration.TotalMilliseconds
            })
        }, Json));
    }
}
