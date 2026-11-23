using AutoMapper;
using FluentValidation;
using MediatR;
using MongoDB.Bson;
using Reviews.Application.Common.Exceptions;
using Reviews.Application.Common.Messaging;
using Reviews.Application.Common.Models;
using Reviews.Application.Common.Validation;
using Reviews.Domain.Entities;
using Reviews.Domain.Exceptions;
using Reviews.Domain.Interfaces;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Discussions.Commands;

// ---------- створити тему ----------
public sealed record CreateDiscussionCommand(
    int MovieId, string MovieTitle, string Title, string UserId, string DisplayName,
    IReadOnlyList<string>? Tags = null) : ICommand<DiscussionDto>;

public sealed class CreateDiscussionCommandValidator : AbstractValidator<CreateDiscussionCommand>
{
    public CreateDiscussionCommandValidator()
    {
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.MovieTitle).NotEmpty().MaximumLength(Review.MaxTitleLength);
        RuleFor(x => x.Title).NotEmpty().Length(Discussion.MinTitleLength, Discussion.MaxTitleLength);
        RuleFor(x => x.UserId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(Author.MaxNameLength);
        RuleFor(x => x.Tags).Must(t => t is null || t.Count <= Discussion.MaxTags)
            .WithMessage($"Не більше {Discussion.MaxTags} тегів");
        RuleForEach(x => x.Tags).NotEmpty().MaximumLength(30);
    }
}

public sealed class CreateDiscussionCommandHandler(IDiscussionRepository discussions, IMapper mapper)
    : IRequestHandler<CreateDiscussionCommand, DiscussionDto>
{
    public async Task<DiscussionDto> Handle(CreateDiscussionCommand cmd, CancellationToken ct)
    {
        var discussion = new Discussion(cmd.MovieId, cmd.MovieTitle, cmd.Title,
            new Author(cmd.UserId, cmd.DisplayName), cmd.Tags);
        await discussions.AddAsync(discussion, ct);
        return mapper.Map<DiscussionDto>(discussion);
    }
}

// ---------- закрити / відкрити тему ----------
public sealed record CloseDiscussionCommand(string Id, string? Reason, int? ExpectedVersion) : ICommand<DiscussionDto>;
public sealed record ReopenDiscussionCommand(string Id, int? ExpectedVersion) : ICommand<DiscussionDto>;

public sealed class CloseDiscussionCommandValidator : AbstractValidator<CloseDiscussionCommand>
{
    public CloseDiscussionCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.Reason).MaximumLength(500);
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class ReopenDiscussionCommandValidator : AbstractValidator<ReopenDiscussionCommand>
{
    public ReopenDiscussionCommandValidator()
    {
        RuleFor(x => x.Id).MustBeObjectId();
        RuleFor(x => x.ExpectedVersion).GreaterThanOrEqualTo(0);
    }
}

public sealed class DiscussionStatusHandlers(IDiscussionRepository discussions, IMapper mapper)
    : IRequestHandler<CloseDiscussionCommand, DiscussionDto>, IRequestHandler<ReopenDiscussionCommand, DiscussionDto>
{
    public Task<DiscussionDto> Handle(CloseDiscussionCommand cmd, CancellationToken ct)
        => ChangeAsync(cmd.Id, cmd.ExpectedVersion, d => d.Close(cmd.Reason), ct);

    public Task<DiscussionDto> Handle(ReopenDiscussionCommand cmd, CancellationToken ct)
        => ChangeAsync(cmd.Id, cmd.ExpectedVersion, d => d.Reopen(), ct);

    private async Task<DiscussionDto> ChangeAsync(string id, int? expectedVersion, Action<Discussion> change, CancellationToken ct)
    {
        var discussion = await discussions.GetByIdAsync(ObjectId.Parse(id), ct)
            ?? throw new NotFoundException("Тема", id);
        if (expectedVersion is { } expected && expected != discussion.Version)
            throw new ConflictException($"Тему {id} уже змінено: очікувана версія {expected}, поточна {discussion.Version}");

        change(discussion);                               // DomainException, якщо перехід неможливий
        await discussions.UpdateAsync(discussion, ct);
        return mapper.Map<DiscussionDto>(discussion);
    }
}
