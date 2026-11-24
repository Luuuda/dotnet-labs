using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Common.Exceptions;
using Reviews.Domain.Exceptions;
using ValidationException = Reviews.Application.Common.Exceptions.ValidationException;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// 2-й behavior: централізоване логування винятків на рівні MediatR — один раз, з назвою запиту.
/// Очікувані (валідація, домен, 404, 409) — Warning, решта — Error. HTTP-статус обирає вже глобальний
/// обробник у WebApi; позначка <see cref="LoggedKey"/> не дає йому залогувати виняток вдруге.
/// </summary>
public sealed class ExceptionHandlingBehavior<TRequest, TResponse>(ILogger<ExceptionHandlingBehavior<TRequest, TResponse>> logger)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public const string LoggedKey = "Reviews.Logged";

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        try
        {
            return await next();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var name = typeof(TRequest).Name;
            switch (ex)
            {
                case ValidationException v:
                    logger.LogWarning("Запит {RequestName} відхилено валідацією ДО виклику обробника: {@Errors}", name, v.Errors);
                    break;
                case NotFoundException or DomainException or ConflictException:
                    logger.LogWarning("Запит {RequestName}: {ExceptionType} — {Message}", name, ex.GetType().Name, ex.Message);
                    break;
                default:
                    logger.LogError(ex, "Необроблений виняток у {RequestName}", name);
                    break;
            }
            ex.Data[LoggedKey] = true;
            throw;
        }
    }
}
