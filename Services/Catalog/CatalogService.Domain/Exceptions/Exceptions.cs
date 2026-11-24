namespace CatalogService.Domain.Exceptions;

/// <summary>Порушення правила, яке перевіряє САМА сутність (ціна > 0, повторне скасування…) → 409.</summary>
public class DomainException(string message) : Exception(message);

/// <summary>Сутність не знайдено → 404.</summary>
public sealed class EntityNotFoundException(string message) : Exception(message)
{
    public static EntityNotFoundException For<T>(object id) => new($"{typeof(T).Name} з Id {id} не знайдено");
}

/// <summary>Конфлікт зі станом БД (дубль, перетин сеансів, конкурентне редагування) → 409.</summary>
public sealed class ConflictException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Помилка валідації (FluentValidation або бізнес-валідація в сервісі) → 400.</summary>
public sealed class ValidationException : Exception
{
    public ValidationException(IDictionary<string, string[]> errors)
        : base("Дані не пройшли валідацію") => Errors = new Dictionary<string, string[]>(errors);

    public ValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
