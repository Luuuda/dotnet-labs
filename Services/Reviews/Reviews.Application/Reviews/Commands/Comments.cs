using Reviews.Application.Caching;
using AutoMapper;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Reviews.Commands;

/// <summary>Додати коментар — новий елемент вкладеного масиву comments.</summary>
public sealed record AddCommentCommand(string ReviewId, string UserId, string DisplayName, string Text)
    : ICommand<CommentDto>;

public sealed class AddCommentCommandValidator : AbstractValidator<AddCommentCommand>
{
    public AddCommentCommandValidator()
    {
        RuleFor(x => x.ReviewId).MustBeObjectId();
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(Author.MaxNameLength);
        RuleFor(x => x.Text).NotEmpty().MaximumLength(Comment.MaxTextLength);
    }
}

public sealed class AddCommentCommandHandler(IReviewRepository reviews, IMapper mapper, IReviewsCacheInvalidator cache)
    : IRequestHandler<AddCommentCommand, CommentDto>
{
    public async Task<CommentDto> Handle(AddCommentCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct)
            ?? throw new NotFoundException("Відгук", cmd.ReviewId);

        var comment = review.AddComment(new Author(cmd.UserId, cmd.DisplayName), cmd.Text);   // ліміт 50 — у домені
        await reviews.UpdateAsync(review, ct);
        await cache.ReviewChangedAsync(review.Id.ToString(), review.MovieId, "CommentAdded", ct);   // Лаб. 06: синхронно із записом
        return mapper.Map<CommentDto>(comment);
    }
}

/// <summary>Видалити коментар з масиву comments.</summary>
public sealed record RemoveCommentCommand(string ReviewId, string CommentId) : ICommand;

public sealed class RemoveCommentCommandValidator : AbstractValidator<RemoveCommentCommand>
{
    public RemoveCommentCommandValidator()
    {
        RuleFor(x => x.ReviewId).MustBeObjectId();
        RuleFor(x => x.CommentId).MustBeObjectId();
    }
}

public sealed class RemoveCommentCommandHandler(IReviewRepository reviews, IReviewsCacheInvalidator cache) : IRequestHandler<RemoveCommentCommand>
{
    public async Task Handle(RemoveCommentCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.ReviewId), ct)
            ?? throw new NotFoundException("Відгук", cmd.ReviewId);
        var commentId = ObjectId.Parse(cmd.CommentId);
        if (!review.HasComment(commentId))
            throw new NotFoundException("Коментар", cmd.CommentId);

        review.RemoveComment(commentId);
        await reviews.UpdateAsync(review, ct);
        await cache.ReviewChangedAsync(review.Id.ToString(), review.MovieId, "CommentRemoved", ct);
    }
}
