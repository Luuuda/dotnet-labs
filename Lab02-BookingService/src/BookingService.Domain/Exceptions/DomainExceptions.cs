namespace BookingService.Domain.Exceptions;

/// <summary>
/// Базовий доменний виняток. Оголошено в Domain, щоб його бачили і DAL (перетворення SqlException),
/// і BLL (бізнес-правила). Глобальний обробник у API перетворює їх на HTTP-статуси.
/// </summary>
public abstract class DomainException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Ресурс не знайдено → 404.</summary>
public sealed class NotFoundException(string message, Exception? inner = null) : DomainException(message, inner);

/// <summary>
/// Порушення бізнес-правила або конфлікт стану → 409:
/// недопустимий перехід статусу (THROW 50002), зайняте місце, порушення UNIQUE (2601/2627),
/// конфлікт rowversion.
/// </summary>
public sealed class BusinessConflictException(string message, Exception? inner = null) : DomainException(message, inner);

/// <summary>Некоректні дані, що виявлено бізнес-валідацією (з урахуванням стану БД) → 400.</summary>
public sealed class ValidationException : DomainException
{
    public ValidationException(string message) : base(message) { }

    public ValidationException(string field, string message) : base(message)
        => Errors = new Dictionary<string, string[]> { [field] = [message] };

    /// <summary>Помилки по полях — потрапляють у ProblemDetails.errors.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]>();
}
