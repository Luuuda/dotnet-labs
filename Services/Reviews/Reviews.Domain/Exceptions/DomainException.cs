namespace Reviews.Domain.Exceptions;

/// <summary>Порушення бізнес-правила домену (HTTP 400).</summary>
public class DomainException(string message) : Exception(message);

/// <summary>
/// Конфлікт стану: документ змінив хтось інший (не збіглася версія) або порушено унікальність (HTTP 409).
/// Кидається реалізаціями репозиторіїв, тому оголошений у домені разом з їхніми інтерфейсами.
/// </summary>
public class ConflictException(string message, Exception? inner = null) : Exception(message, inner);
