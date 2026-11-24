using System.Text.RegularExpressions;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Domain.ValueObjects;

/// <summary>
/// Оцінки окремих аспектів фільму (story, acting, visuals, sound…), кожна від 1 до 5.
/// Гнучкий набір ключів — як поле <c>aspects</c> у документі з Лаб. 01. Незмінний.
/// </summary>
public sealed partial class ReviewAspects : ValueObject
{
    public const int MaxAspects = 10;
    public static readonly IReadOnlyList<string> Known = ["story", "acting", "visuals", "sound", "music"];

    private readonly SortedDictionary<string, int> _scores;

    public IReadOnlyDictionary<string, int> Scores => _scores;

    public ReviewAspects(IReadOnlyDictionary<string, int> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);
        if (scores.Count == 0)
            throw new DomainException("Потрібно оцінити хоча б один аспект");
        if (scores.Count > MaxAspects)
            throw new DomainException($"Не більше {MaxAspects} аспектів");

        _scores = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (key, value) in scores)
        {
            var name = key.Trim().ToLowerInvariant();
            if (!KeyPattern().IsMatch(name))
                throw new DomainException($"Некоректна назва аспекту '{key}' (лише латинські літери, до 20 символів)");
            if (value is < Rating.Min or > Rating.Max)
                throw new DomainException($"Оцінка аспекту '{name}' має бути від {Rating.Min} до {Rating.Max}");
            if (!_scores.TryAdd(name, value))
                throw new DomainException($"Аспект '{name}' вказано двічі");
        }
    }

    public double Average => _scores.Values.Average();

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        foreach (var (key, value) in _scores)
        {
            yield return key;
            yield return value;
        }
    }

    [GeneratedRegex("^[a-z]{1,20}$")]
    private static partial Regex KeyPattern();
}
