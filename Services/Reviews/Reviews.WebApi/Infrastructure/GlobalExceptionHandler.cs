using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Reviews.Application.Common.Behaviors;
using Reviews.Application.Common.Exceptions;
using Reviews.Domain.Exceptions;

namespace Reviews.WebApi.Infrastructure;

/// <summary>
/// Глобальний обробник: доменні, прикладні й MongoDB-винятки → ProblemDetails з правильним статусом.
/// Деталі необробленої помилки (тип, текст) показуються лише в Development.
/// </summary>
public sealed class GlobalExceptionHandler(
    IProblemDetailsService problemDetails, IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            NotFoundException => (StatusCodes.Status404NotFound, "Ресурс не знайдено"),
            ValidationException => (StatusCodes.Status400BadRequest, "Помилка валідації"),
            DomainException => (StatusCodes.Status400BadRequest, "Порушення бізнес-правила"),
            ConflictException => (StatusCodes.Status409Conflict, "Конфлікт версій"),
            MongoWriteException { WriteError.Category: ServerErrorCategory.DuplicateKey }
                => (StatusCodes.Status409Conflict, "Дублікат: порушено унікальний індекс"),
            MongoBulkWriteException bulk when bulk.WriteErrors.Any(e => e.Category == ServerErrorCategory.DuplicateKey)
                => (StatusCodes.Status409Conflict, "Дублікат: порушено унікальний індекс"),
            MongoWriteException => (StatusCodes.Status400BadRequest, "Документ відхилено базою даних"),
            MongoConnectionException or TimeoutException
                => (StatusCodes.Status503ServiceUnavailable, "MongoDB тимчасово недоступна"),
            _ => (StatusCodes.Status500InternalServerError, "Внутрішня помилка сервера")
        };

        // Винятки з MediatR уже залоговано в ExceptionHandlingBehavior — не дублюємо
        if (!exception.Data.Contains(ExceptionHandlingBehavior<object, object>.LoggedKey))
        {
            if (status >= 500) logger.LogError(exception, "Необроблений виняток: {Path}", http.Request.Path);
            else logger.LogWarning("{ExceptionType} → {Status}: {Message}", exception.GetType().Name, status, exception.Message);
        }

        var problem = exception is ValidationException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value)) { Title = title }
            : new ProblemDetails { Title = title };

        problem.Status = status;
        problem.Instance = http.Request.Path;
        problem.Detail = status switch
        {
            < 500 => exception.Message,
            503 => "Сервіс бази даних не відповідає. Спробуйте пізніше.",
            _ when environment.IsDevelopment() => $"{exception.GetType().Name}: {exception.Message}",
            _ => "Сталася непередбачена помилка. Зверніться до підтримки, вказавши traceId."
        };
        if (exception is MongoWriteException or MongoBulkWriteException && status == 409)
            problem.Detail = "Користувач уже залишив відгук на цей фільм (індекс ux_author_movie).";

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = http,
            ProblemDetails = problem,
            Exception = exception
        });
    }
}
