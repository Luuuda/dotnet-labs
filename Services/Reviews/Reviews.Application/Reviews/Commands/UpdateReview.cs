using Reviews.Application.Caching;
using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands;

/// <summary>
/// Редагувати відгук. ExpectedVersion — версія, яку клієнт бачив (ETag / If-Match або поле version);
/// якщо документ уже змінили — 409.
/// </summary>
public sealed record UpdateReviewCommand(
    string Id,
    int Rating,
    string? Text,
    IReadOnlyDictionary<string, int>? Aspects,
    int? ExpectedVersion) : ICommand<ReviewDetailsDto>;

public sealed class UpdateReviewCommandValidator : AbstractValidator<UpdateReviewCommand>
{
    public UpdateReviewCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max).WithMessage("Оцінка має бути від 1 до 5");
        RuleFor(x => x.Text).NotEmpty().WithMessage("Текст відгуку обов'язковий").MaximumLength(Review.MaxTextLength);
        RuleFor(x => x.Aspects).ValidAspects();
        RuleFor(x => x.ExpectedVersion)
            .NotNull().WithMessage("Потрібна версія документа: заголовок If-Match (ETag з GET) або поле version")
            .GreaterThanOrEqualTo(0);
    }
}

public sealed class UpdateReviewCommandHandler(IReviewRepository reviews, IMapper mapper, ILogger<UpdateReviewCommandHandler> logger,
    IReviewsCacheInvalidator cache)
    : IRequestHandler<UpdateReviewCommand, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(UpdateReviewCommand cmd, CancellationToken ct)
    {
        var id = ObjectId.Parse(cmd.Id);
        var review = await reviews.GetByIdAsync(id, ct) ?? throw new NotFoundException("Відгук", cmd.Id);

        if (review.Version != cmd.ExpectedVersion)
            throw new ConflictException(
                $"Відгук {cmd.Id} уже змінено: очікувана версія {cmd.ExpectedVersion}, поточна {review.Version}. Отримайте актуальну версію (GET) і повторіть.");

        review.Edit(new Rating(cmd.Rating), cmd.Text,
            cmd.Aspects is { Count: > 0 } aspects ? new ReviewAspects(aspects) : null);

        await reviews.UpdateAsync(review, ct);          // ReplaceOne з фільтром { _id, version } — захист від гонки
        await cache.ReviewChangedAsync(review.Id.ToString(), review.MovieId, "ReviewUpdated", ct);   // Лаб. 06
        logger.LogInformation("Відгук {ReviewId} оновлено: оцінка {Rating}, версія {Version}", review.Id, review.Rating.Value, review.Version);
        return mapper.Map<ReviewDetailsDto>(review);
    }
}
