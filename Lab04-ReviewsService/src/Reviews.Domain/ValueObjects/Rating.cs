using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>Оцінка фільму — ціле число від 1 до 5. Зберігається в документі як звичайне число <c>rating</c>.</summary>
public sealed class Rating : ValueObject
{
    public const int Min = 1;
    public const int Max = 5;

    public int Value { get; }

    public Rating(int value)
    {
        if (value is < Min or > Max)
            throw new DomainException($"Оцінка має бути від {Min} до {Max}, отримано {value}");
        Value = value;
    }

    public bool IsPositive => Value >= 4;

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => $"{Value}/{Max}";
}
