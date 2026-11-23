using FluentValidation;
using MediatR;
using ValidationException = Reviews.Application.Common.Exceptions.ValidationException;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// 3-й behavior: виконує ВСІ валідатори запиту (включно з асинхронними правилами із запитом до MongoDB),
/// збирає помилки разом і кидає ValidationException — обробник при цьому не викликається.
/// </summary>
public sealed class ValidationBehavior<TRequest, TResponse>(IEnumerable<IValidator<TRequest>> validators)
    : IPipelineBehavior<TRequest, TResponse> where TRequest : notnull
{
    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (!validators.Any())
            return await next();

        var context = new ValidationContext<TRequest>(request);
        var results = new List<FluentValidation.Results.ValidationResult>();
        foreach (var validator in validators)                       // послідовно: валідатори можуть ходити в БД
            results.Add(await validator.ValidateAsync(context, ct));

        var failures = results.SelectMany(r => r.Errors).Where(f => f is not null).ToList();
        if (failures.Count > 0)
            throw new ValidationException(failures);                // обробник НЕ викликається

        return await next();
    }
}
