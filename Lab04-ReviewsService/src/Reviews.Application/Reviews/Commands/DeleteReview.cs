using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Reviews.Commands;

/// <summary>Видалити відгук. ExpectedVersion необов'язкова: якщо передано — видаляється лише ця версія.</summary>
public sealed record DeleteReviewCommand(string Id, int? ExpectedVersion = null) : ICommand;

public sealed class DeleteReviewCommandValidator : AbstractValidator<DeleteReviewCommand>
{
    public DeleteReviewCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class DeleteReviewCommandHandler(IReviewRepository reviews) : IRequestHandler<DeleteReviewCommand>
{
    public async Task Handle(DeleteReviewCommand cmd, CancellationToken ct)
    {
        var review = await reviews.GetByIdAsync(ObjectId.Parse(cmd.Id), ct)
            ?? throw new NotFoundException("Відгук", cmd.Id);

        if (cmd.ExpectedVersion is { } expected && expected != review.Version)
            throw new ConflictException($"Відгук {cmd.Id} уже змінено: очікувана версія {expected}, поточна {review.Version}");

        await reviews.DeleteAsync(review, ct);
    }
}
