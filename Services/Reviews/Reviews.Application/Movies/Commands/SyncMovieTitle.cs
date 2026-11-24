using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Reviews.Application.Caching;
using Reviews.Application.Common.Interfaces;
using Reviews.Application.Common.Messaging;
using Reviews.Domain.Entities;
using Reviews.Domain.Interfaces;

namespace Reviews.Application.Movies.Commands;

/// <summary>
/// Лаб. 08: реакція на подію MovieRenamed з Каталогу — нова назва фільму в УСІХ відгуках і обговореннях
/// про нього (денормалізоване поле movieTitle, політика дублювання Лаб. 01). Результат — скільки документів змінено.
/// </summary>
public sealed record SyncMovieTitleCommand(Guid EventId, int MovieId, string NewTitle, DateTime OccurredAt)
    : ICommand<SyncMovieTitleResult>;

public sealed record SyncMovieTitleResult(bool Duplicate, bool Stale, long ReviewsUpdated, long DiscussionsUpdated);

public sealed class SyncMovieTitleCommandValidator : AbstractValidator<SyncMovieTitleCommand>
{
    public SyncMovieTitleCommandValidator()
    {
        RuleFor(x => x.EventId).NotEmpty();
        RuleFor(x => x.MovieId).GreaterThan(0);
        RuleFor(x => x.NewTitle).NotEmpty().MaximumLength(Review.MaxTitleLength);
    }
}

/// <summary>
/// Ідемпотентність без транзакцій: MongoDB у платформі — standalone (single node), тому багатодокументних
/// транзакцій немає. Порядок кроків підібрано так, щоб збій на будь-якому з них був безпечним:
/// <list type="number">
/// <item>EventId уже в processedEvents → дубль, нічого не робимо;</item>
/// <item>для фільму вже застосовано новіше перейменування → застаріла подія, лише позначаємо її обробленою;</item>
/// <item>updateMany у reviews і discussions — сама операція ідемпотентна: фільтр «movieTitle ≠ нова назва»,
/// повтор не змінює жодного документа;</item>
/// <item>лише після успіху — запис EventId у processedEvents (унікальний _id).</item>
/// </list>
/// Якщо сервіс упаде між кроками 3 і 4, повідомлення не буде підтверджене (ack), прийде знову, і повтор
/// кроку 3 нічого не зламає. У Бронюванні (SQL Server) те саме зроблено однією транзакцією.
/// </summary>
public sealed class SyncMovieTitleCommandHandler(
    IReviewRepository reviews,
    IDiscussionRepository discussions,
    IIntegrationEventStore events,
    IReviewsCacheInvalidator cache,
    ILogger<SyncMovieTitleCommandHandler> logger) : IRequestHandler<SyncMovieTitleCommand, SyncMovieTitleResult>
{
    private const string EventType = "MovieRenamedEvent";

    public async Task<SyncMovieTitleResult> Handle(SyncMovieTitleCommand cmd, CancellationToken ct)
    {
        if (await events.IsProcessedAsync(cmd.EventId, ct))
        {
            logger.LogInformation("Duplicate {EventType} {EventId} пропущено: подію вже оброблено раніше", EventType, cmd.EventId);
            return new SyncMovieTitleResult(Duplicate: true, Stale: false, 0, 0);
        }

        if (!await events.TryAdvanceMovieTitleAsync(cmd.MovieId, cmd.OccurredAt, ct))
        {
            logger.LogWarning("Застаріла {EventType} {EventId} для фільму {MovieId} ({OccurredAt:O}): уже застосовано новішу назву",
                EventType, cmd.EventId, cmd.MovieId, cmd.OccurredAt);
            await events.MarkProcessedAsync(cmd.EventId, EventType, ct);
            return new SyncMovieTitleResult(Duplicate: false, Stale: true, 0, 0);
        }

        var title = cmd.NewTitle.Trim();
        var reviewsUpdated = await reviews.RenameMovieAsync(cmd.MovieId, title, ct);
        var discussionsUpdated = await discussions.RenameMovieAsync(cmd.MovieId, title, ct);
        await events.MarkProcessedAsync(cmd.EventId, EventType, ct);
        await cache.MovieChangedAsync(cmd.MovieId, "MovieRenamed", ct);       // movieTitle є в рейтингу й списках

        logger.LogInformation("Фільм {MovieId} → «{NewTitle}»: оновлено {ReviewsUpdated} відгуків і {DiscussionsUpdated} обговорень",
            cmd.MovieId, title, reviewsUpdated, discussionsUpdated);
        return new SyncMovieTitleResult(false, false, reviewsUpdated, discussionsUpdated);
    }
}
