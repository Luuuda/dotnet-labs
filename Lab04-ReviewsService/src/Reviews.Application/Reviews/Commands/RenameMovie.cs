using FluentValidation;
using MediatR;
using Reviews.Application.Common.Messaging;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Reviews.Commands;

/// <summary>
/// Синхронізація денормалізованої назви фільму (реакція на подію MovieRenamed з Каталогу).
/// Повертає кількість оновлених відгуків (updateMany за індексом movieId).
/// </summary>
public sealed record RenameMovieCommand(int MovieId, string MovieTitle) : ICommand<long>;

public sealed class RenameMovieCommandValidator : AbstractValidator<RenameMovieCommand>
{
    public RenameMovieCommandValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MovieTitle).NotEmpty().MaximumLength(Review.MaxTitleLength);
    }
}

public sealed class RenameMovieCommandHandler(IReviewRepository reviews) : IRequestHandler<RenameMovieCommand, long>
{
    public Task<long> Handle(RenameMovieCommand cmd, CancellationToken ct)
        => reviews.RenameMovieAsync(cmd.MovieId, cmd.MovieTitle.Trim(), ct);
}
