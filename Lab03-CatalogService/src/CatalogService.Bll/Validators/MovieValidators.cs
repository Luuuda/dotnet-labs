using CatalogService.Bll.Dtos;
using CatalogService.Domain.Entities;
using FluentValidation;

namespace CatalogService.Bll.Validators;

/// <summary>
/// Складні правила фільму. Прості (Required, StringLength) — уже на DTO атрибутами, тут їх не дублюємо:
/// лише діапазони, набори допустимих значень, формати й перехресні правила.
/// </summary>
public class CreateMovieDtoValidator : AbstractValidator<CreateMovieDto>
{
    public CreateMovieDtoValidator(TimeProvider clock)
    {
        var maxYear = clock.GetUtcNow().Year + 5;

        RuleFor(x => x.Title)
            .Must(t => !string.IsNullOrWhiteSpace(t)).WithMessage("Назва не може складатися лише з пробілів");
        RuleFor(x => x.OriginalTitle)
            .NotEqual(x => x.Title).When(x => x.OriginalTitle is not null)
            .WithMessage("Оригінальну назву вказують, лише якщо вона відрізняється від назви");
        RuleFor(x => x.ReleaseYear)
            .InclusiveBetween(Movie.MinYear, maxYear).WithMessage($"Рік виходу — від {Movie.MinYear} до {maxYear}");
        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(1, Movie.MaxDuration).WithMessage($"Тривалість — від 1 до {Movie.MaxDuration} хв");
        RuleFor(x => x.AgeRating)
            .Must(a => Movie.AgeRatings.Contains(a)).WithMessage($"Віковий рейтинг: {string.Join(", ", Movie.AgeRatings)}");
        RuleFor(x => x.Genres)
            .Must(g => g.Count <= 5).WithMessage("Не більше 5 жанрів")
            .Must(g => g.Distinct(StringComparer.OrdinalIgnoreCase).Count() == g.Count).WithMessage("Жанри повторюються");
        RuleForEach(x => x.Genres)
            .Matches("^[a-z0-9-]+$").WithMessage("Slug жанру: малі латинські літери, цифри, дефіс");
        RuleFor(x => x.Director)
            .NotEmpty().When(x => !string.IsNullOrWhiteSpace(x.Description))
            .WithMessage("Якщо заповнено опис, вкажіть і режисера");
    }
}

public sealed class UpdateMovieDtoValidator : AbstractValidator<UpdateMovieDto>
{
    public UpdateMovieDtoValidator(TimeProvider clock)
    {
        Include(new CreateMovieDtoValidator(clock));
        RuleFor(x => x.RowVersion)
            .Must(BeBase64RowVersion).WithMessage("rowVersion — base64-рядок із 8 байтів (як у відповіді GET)");
    }

    private static bool BeBase64RowVersion(string value)
    {
        var buffer = new Span<byte>(new byte[16]);
        return Convert.TryFromBase64String(value, buffer, out var written) && written == 8;
    }
}

public sealed class AssignGenreDtoValidator : AbstractValidator<AssignGenreDto>
{
    public AssignGenreDtoValidator()
        => RuleFor(x => x.Slug).Matches("^[a-z0-9-]+$").WithMessage("Slug жанру: малі латинські літери, цифри, дефіс");
}

public sealed class MovieQueryValidator : AbstractValidator<MovieQuery>
{
    public static readonly string[] SortFields = ["title", "year", "duration", "rating", "created"];

    public MovieQueryValidator()
    {
        RuleFor(x => x.SortBy).Must(s => SortFields.Contains(s.ToLowerInvariant()))
            .WithMessage($"sortBy: {string.Join(", ", SortFields)}");
        RuleFor(x => x.SortDir).Must(s => s.ToLowerInvariant() is "asc" or "desc").WithMessage("sortDir: asc або desc");
        RuleFor(x => x.AgeRating).Must(a => Movie.AgeRatings.Contains(a!)).When(x => x.AgeRating is not null)
            .WithMessage($"ageRating: {string.Join(", ", Movie.AgeRatings)}");
        RuleFor(x => x.YearTo).GreaterThanOrEqualTo(x => x.YearFrom).When(x => x.YearFrom is not null && x.YearTo is not null)
            .WithMessage("yearTo має бути ≥ yearFrom");
        RuleFor(x => x.MaxDuration).GreaterThanOrEqualTo(x => x.MinDuration).When(x => x.MinDuration is not null && x.MaxDuration is not null)
            .WithMessage("maxDuration має бути ≥ minDuration");
    }
}
