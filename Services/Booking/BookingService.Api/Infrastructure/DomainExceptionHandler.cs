using BookingService.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace BookingService.Api.Infrastructure;

/// <summary>
/// ЄДИНЕ місце, де винятки перетворюються на HTTP-статуси (без try/catch у кожній дії).
/// Завершальна ланка ланцюжка «процедура (THROW 50002) → DAL (BusinessConflictException) → BLL → 409».
/// </summary>
public sealed class DomainExceptionHandler(IProblemDetailsService problemDetails, ILogger<DomainExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Ресурс не знайдено"),
            BusinessConflictException => (StatusCodes.Status409Conflict, "Конфлікт бізнес-правила"),
            ValidationException => (StatusCodes.Status400BadRequest, "Помилка валідації"),
            OperationCanceledException when http.RequestAborted.IsCancellationRequested
                => (499, "Запит скасовано клієнтом"),
            _ => (StatusCodes.Status500InternalServerError, "Внутрішня помилка сервера")
        };

        if (status >= 500)
            logger.LogError(exception, "Необроблена помилка під час {Method} {Path}", http.Request.Method, http.Request.Path);
        else
            logger.LogWarning("{Status} під час {Method} {Path}: {Message}", status, http.Request.Method, http.Request.Path, exception.Message);

        var details = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://www.rfc-editor.org/rfc/rfc9110#status.{status}",
            Instance = http.Request.Path,
            // Для 500 не віддаємо клієнту текст винятку (там може бути SQL чи внутрішні деталі)
            Detail = status >= 500 ? "Сталася непередбачена помилка. Повідомте traceId підтримці." : exception.Message
        };
        if (exception is ValidationException { Errors.Count: > 0 } validation)
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
