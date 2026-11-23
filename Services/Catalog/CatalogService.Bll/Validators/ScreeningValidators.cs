using CatalogService.Bll.Dtos;
using CatalogService.Domain.Entities;
using FluentValidation;

namespace CatalogService.Bll.Validators;

public sealed class CreateScreeningDtoValidator : AbstractValidator<CreateScreeningDto>
{
    public CreateScreeningDtoValidator(TimeProvider clock)
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.HallId).GreaterThan(0);
        RuleFor(x => x.StartsAt)
            .Must(d => d > clock.GetUtcNow().UtcDateTime).WithMessage("Сеанс можна запланувати лише на майбутнє")
            .Must(d => d.Minute % 5 == 0 && d.Second == 0).WithMessage("Час початку — кратний 5 хвилинам");
        RuleFor(x => x.Format)
            .Must(f => Screening.Formats.Contains(f)).WithMessage($"format: {string.Join(", ", Screening.Formats)}");
        RuleFor(x => x.BasePrice).GreaterThan(0).LessThanOrEqualTo(10_000);
    }
}

public sealed class ChangePriceDtoValidator : AbstractValidator<ChangePriceDto>
{
    public ChangePriceDtoValidator() => RuleFor(x => x.BasePrice).GreaterThan(0).LessThanOrEqualTo(10_000);
}

public sealed class ScreeningQueryValidator : AbstractValidator<ScreeningQuery>
{
    public static readonly string[] SortFields = ["startsat", "price", "movie"];

    public ScreeningQueryValidator()
    {
        RuleFor(x => x.SortBy).Must(s => SortFields.Contains(s.ToLowerInvariant())).WithMessage("sortBy: startsAt, price, movie");
        RuleFor(x => x.SortDir).Must(s => s.ToLowerInvariant() is "asc" or "desc").WithMessage("sortDir: asc або desc");
        RuleFor(x => x.Format).Must(f => Screening.Formats.Contains(f!)).When(x => x.Format is not null)
            .WithMessage($"format: {string.Join(", ", Screening.Formats)}");
        RuleFor(x => x.To).GreaterThan(x => x.From).When(x => x.From is not null && x.To is not null)
            .WithMessage("to має бути пізніше за from");
        RuleFor(x => x.MaxPrice).GreaterThan(0).When(x => x.MaxPrice is not null);
    }
}
