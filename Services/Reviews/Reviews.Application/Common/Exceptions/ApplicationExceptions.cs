using FluentValidation.Results;

namespace Reviews.Application.Common.Exceptions;

/// <summary>Ресурс не знайдено (HTTP 404).</summary>
public sealed class NotFoundException(string resource, object key)
    : Exception($"{resource} з ідентифікатором '{key}' не знайдено")
{
    public string Resource { get; } = resource;
    public object Key { get; } = key;
}

/// <summary>Помилки валідації запиту, зібрані всіма валідаторами разом (HTTP 400).</summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IEnumerable<ValidationFailure> failures)
        : base("Запит не пройшов валідацію")
    {
        Errors = failures
            .GroupBy(f => string.IsNullOrEmpty(f.PropertyName) ? "request" : f.PropertyName, f => f.ErrorMessage)
            .ToDictionary(g => g.Key, g => g.Distinct().ToArray());
    }
}
