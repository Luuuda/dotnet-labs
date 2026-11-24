using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using CatalogService.Domain.Entities;
using Platform.EventBus;

namespace CatalogService.Api.IntegrationEvents;

/// <summary>
/// Лаб. 08: publisher подій Каталогу. Створює незмінну подію (EventId, OccurredAt, CorrelationId поточного запиту)
/// і публікує її через <see cref="IEventPublisher"/> в exchange catalog-events з потрібним routing key.
/// <para>Виклик іде вже ПІСЛЯ SaveChangesAsync, тобто зміна в БД зафіксована. Якщо брокер у цей момент недоступний,
/// клієнт не має отримати 500 на успішну операцію (він повторив би запит і отримав 409 / дубль), тому помилка
/// публікації логується з усіма ідентифікаторами (EventId, тип, routing key, CorrelationId) на рівні Error і далі
/// не прокидається. Таку подію втрачено — гарантію «зміна ⇔ подія» дав би Transactional Outbox (README, Лаб. 08).</para>
/// </summary>
public sealed class CatalogIntegrationEventService(IEventPublisher publisher, ILogger<CatalogIntegrationEventService> logger)
    : ICatalogIntegrationEventService
{
    public Task PublishScreeningScheduledAsync(Screening screening, Movie movie, Hall hall, CancellationToken ct) =>
        PublishAsync(new ScreeningScheduledEvent(screening.Id, movie.Id, movie.Title, hall.Id, hall.Name, hall.RowsCount,
                hall.SeatsPerRow, screening.StartsAt, screening.Format, screening.BasePrice) { CorrelationId = CorrelationContext.Current },
            CatalogEvents.RoutingKeys.ScreeningScheduled, ct);

    public Task PublishScreeningPriceChangedAsync(ScreeningDto screening, decimal oldPrice, CancellationToken ct) =>
        PublishAsync(new ScreeningPriceChangedEvent(screening.Id, screening.MovieId, screening.MovieTitle, oldPrice, screening.BasePrice)
            { CorrelationId = CorrelationContext.Current },
            CatalogEvents.RoutingKeys.ScreeningPriceChanged, ct);

    public Task PublishScreeningCancelledAsync(ScreeningDto screening, CancellationToken ct) =>
        PublishAsync(new ScreeningCancelledEvent(screening.Id, screening.MovieId, screening.MovieTitle, screening.StartsAt)
            { CorrelationId = CorrelationContext.Current },
            CatalogEvents.RoutingKeys.ScreeningCancelled, ct);

    public Task PublishMovieRenamedAsync(int movieId, string oldTitle, string newTitle, string? originalTitle, CancellationToken ct) =>
        PublishAsync(new MovieRenamedEvent(movieId, oldTitle, newTitle, originalTitle) { CorrelationId = CorrelationContext.Current },
            CatalogEvents.RoutingKeys.MovieRenamed, ct);

    private async Task PublishAsync<T>(T @event, string routingKey, CancellationToken ct) where T : IntegrationEvent
    {
        try
        {
            await publisher.PublishAsync(@event, CatalogEvents.Exchange, routingKey, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Подію {EventType} {EventId} ({Exchange}/{RoutingKey}) НЕ опубліковано: зміну в БД збережено, копії інших сервісів не оновляться (CorrelationId {CorrelationId})",
                typeof(T).Name, @event.EventId, CatalogEvents.Exchange, routingKey, @event.CorrelationId);
        }
    }
}
