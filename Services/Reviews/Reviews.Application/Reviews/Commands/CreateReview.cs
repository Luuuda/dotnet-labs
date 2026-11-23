using Reviews.Application.Caching;
using System.Diagnostics;
using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands;

/// <summary>Створити відгук. userId — Customers.Id з сервісу Бронювання, movieId — Movies.Id з Каталогу.</summary>
public sealed record CreateReviewCommand(
    int MovieId,
    string MovieTitle,
    string UserId,
    string DisplayName,
    int Rating,
    string? Text,
    IReadOnlyDictionary<string, int>? Aspects = null,
    bool VerifiedViewer = false) : ICommand<ReviewDetailsDto>;

public sealed class CreateReviewCommandValidator : AbstractValidator<CreateReviewCommand>
{
    public CreateReviewCommandValidator(IReviewRepository reviews)
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MovieTitle).NotEmpty().MaximumLength(Review.MaxTitleLength);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(Author.MaxNameLength);
        RuleFor(x => x.Rating).InclusiveBetween(Rating.Min, Rating.Max)
            .WithMessage("Оцінка має бути від 1 до 5");
        RuleFor(x => x.Text).NotEmpty().WithMessage("Текст відгуку обов'язковий")
            .MaximumLength(Review.MaxTextLength);
        RuleFor(x => x.Aspects).ValidAspects();

        // Асинхронне правило із запитом до MongoDB: «один користувач — один відгук на фільм».
        // Виконується, лише якщо прості правила пройшли (немає сенсу йти в базу з некоректними даними).
        RuleFor(x => x)
            .MustAsync(async (cmd, ct) => !await reviews.ExistsAsync(cmd.MovieId, cmd.UserId, ct))
            .OverridePropertyName("UserId")
            .WithMessage(cmd => $"Користувач {cmd.UserId} уже залишив відгук на фільм {cmd.MovieId}")
            .When(x => x.MovieId > 0 && !string.IsNullOrWhiteSpace(x.UserId) && x.Rating is >= 1 and <= 5,
                  ApplyConditionTo.CurrentValidator);
    }
}

public sealed class CreateReviewCommandHandler(IReviewRepository reviews, IMapper mapper,
    ILogger<CreateReviewCommandHandler> logger, IReviewsCacheInvalidator cache) : IRequestHandler<CreateReviewCommand, ReviewDetailsDto>
{
    public async Task<ReviewDetailsDto> Handle(CreateReviewCommand cmd, CancellationToken ct)
    {
        var review = new Review(
            cmd.MovieId, cmd.MovieTitle,
            new Author(cmd.UserId, cmd.DisplayName),
            new Rating(cmd.Rating),
            cmd.Text,
            cmd.Aspects is { Count: > 0 } aspects ? new ReviewAspects(aspects) : null,
            cmd.VerifiedViewer);

        Activity.Current?.SetTag("review.movie_id", cmd.MovieId);
        Activity.Current?.SetTag("review.rating", cmd.Rating);
        await reviews.AddAsync(review, ct);   // унікальний індекс (movieId, author.userId) — страховка від гонки
        await cache.MovieChangedAsync(review.MovieId, "ReviewCreated", ct);   // Лаб. 06: рейтинг і списки фільму застаріли
        logger.LogInformation("Створено відгук {ReviewId} на фільм {MovieId} від {UserId}: оцінка {Rating}, перевірений глядач {VerifiedViewer}",
            review.Id, review.MovieId, review.Author.UserId, review.Rating.Value, review.VerifiedViewer);
        return mapper.Map<ReviewDetailsDto>(review);
    }
}
