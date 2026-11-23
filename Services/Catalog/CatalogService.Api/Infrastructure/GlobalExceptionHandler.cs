using CatalogService.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CatalogService.Api.Infrastructure;

/// <summary>
/// Централізована обробка: КОЖЕН виняток логується ОДИН раз — тут — і повертається як ProblemDetails.
/// Мапінг: EntityNotFoundException → 404, ValidationException → 400, ConflictException / DomainException → 409.
/// Деталі винятку (stack trace) — лише в Development; у Production — коротке безпечне повідомлення.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails, IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            EntityNotFoundException => (StatusCodes.Status404NotFound, "Ресурс не знайдено"),
            ValidationException => (StatusCodes.Status400BadRequest, "Помилка валідації"),
            ConflictException => (StatusCodes.Status409Conflict, "Конфлікт"),
            DomainException => (StatusCodes.Status409Conflict, "Порушено бізнес-правило"),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested => (499, "Запит скасовано клієнтом"),
            _ => (StatusCodes.Status500InternalServerError, "Внутрішня помилка сервера")
        };

        if (status >= 500)
            logger.LogError(exception, "Запит {Method} {Path} завершився помилкою", http.Request.Method, http.Request.Path);
        else
            logger.LogWarning("Запит {Method} {Path} → {Status}: {Reason}", http.Request.Method, http.Request.Path, status, exception.Message);

        var details = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://www.rfc-editor.org/rfc/rfc9110#status.{status}",
            Instance = http.Request.Path,
            Detail = status < 500 ? exception.Message
                   : environment.IsDevelopment() ? exception.ToString()
                   : "Сталася непередбачена помилка. Повідомте traceId підтримці."
        };
        if (exception is ValidationException validation)
            details.Extensions["errors"] = validation.Errors;

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = details,
            Exception = exception
        });
    }
}
