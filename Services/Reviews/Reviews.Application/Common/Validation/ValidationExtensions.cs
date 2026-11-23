using FluentValidation;
using MongoDB.Bson;

namespace Reviews.Application.Common.Validation;

public static class ValidationExtensions
{
    /// <summary>MongoDB-специфічне правило: рядок має бути коректним 24-символьним ObjectId.</summary>
    public static IRuleBuilderOptions<T, string?> MustBeObjectId<T>(this IRuleBuilder<T, string?> rule)
        => rule.NotEmpty().WithMessage("{PropertyName} обов'язковий")
               .Must(id => ObjectId.TryParse(id, out _))
               .WithMessage("{PropertyName} має бути коректним ObjectId (24 шістнадцяткові символи)");

    public static IRuleBuilderOptions<T, IReadOnlyDictionary<string, int>?> ValidAspects<T>(
        this IRuleBuilder<T, IReadOnlyDictionary<string, int>?> rule)
        => rule.Must(a => a is null || a.Count <= 10)
               .WithMessage("Не більше 10 аспектів (порожній набір = без аспектів)")
               .Must(a => a is null || a.All(kv => kv.Key.Length is > 0 and <= 20 && kv.Key.All(char.IsAsciiLetter)))
               .WithMessage("Назви аспектів — латинськими літерами, до 20 символів (story, acting, visuals, sound…)")
               .Must(a => a is null || a.Values.All(v => v is >= 1 and <= 5))
               .WithMessage("Оцінка кожного аспекту — від 1 до 5");
}
