using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>
/// Автор відгуку, коментаря чи теми. Знімок даних користувача з сервісу Бронювання
/// (userId = Customers.Id, displayName — ім'я на момент запису). Незмінний.
/// </summary>
public sealed class Author : ValueObject
{
    public const int MaxNameLength = 100;

    public string UserId { get; }
    public string DisplayName { get; }

    public Author(string userId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new DomainException("Ідентифікатор автора обов'язковий");
        if (string.IsNullOrWhiteSpace(displayName))
            throw new DomainException("Ім'я автора обов'язкове");
        if (displayName.Trim().Length > MaxNameLength)
            throw new DomainException($"Ім'я автора довше за {MaxNameLength} символів");

        UserId = userId.Trim();
        DisplayName = displayName.Trim();
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return UserId;
        yield return DisplayName;
    }

    public override string ToString() => $"{DisplayName} ({UserId})";
}
