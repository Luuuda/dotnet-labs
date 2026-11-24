using MediatR;
using Platform.Messaging;
using Reviews.Application.Movies.Commands;
using AppValidationException = Reviews.Application.Common.Exceptions.ValidationException;

namespace Reviews.WebApi.IntegrationEvents;

/// <summary>
/// Лаб. 08: обробник події MovieRenamed. Перетворює подію на команду Application (CQRS / MediatR) — уся логіка
/// й ідемпотентність у <see cref="SyncMovieTitleCommandHandler"/>, а WebApi лише адаптує транспорт.
/// Невалідні дані події (порожня назва) — постійна помилка: повідомлення одразу йде в DLQ, без повторів.
/// </summary>
public sealed class MovieRenamedEventHandler(ISender sender, ILogger<MovieRenamedEventHandler> logger)
    : IIntegrationEventHandler<MovieRenamedEvent>
{
    public async Task HandleAsync(MovieRenamedEvent @event, CancellationToken ct)
    {
        try
        {
            var result = await sender.Send(
                new SyncMovieTitleCommand(@event.EventId, @event.MovieId, @event.NewTitle, @event.OccurredAt), ct);
            logger.LogInformation(
                "MovieRenamedEvent {EventId}: фільм {MovieId} «{OldTitle}» → «{NewTitle}», відгуків {ReviewsUpdated}, обговорень {DiscussionsUpdated}, дубль {Duplicate}, застаріла {Stale}",
                @event.EventId, @event.MovieId, @event.OldTitle, @event.NewTitle,
                result.ReviewsUpdated, result.DiscussionsUpdated, result.Duplicate, result.Stale);
        }
        catch (AppValidationException ex)
        {
            throw new PermanentMessageException($"Невалідна подія MovieRenamedEvent {@event.EventId}: {ex.Message}", ex);
        }
    }
}
