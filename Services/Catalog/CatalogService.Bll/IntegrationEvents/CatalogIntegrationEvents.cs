using Platform.Messaging;

namespace CatalogService.Bll.IntegrationEvents;

// Лаб. 08: інтеграційні події Каталогу — незмінні record-и з назвами в минулому часі. Кожна самодостатня:
// містить усе, що потрібно споживачам для оновлення своїх копій, тож до Каталогу по додаткові дані не звертаються.
// EventId, OccurredAt, CorrelationId і SchemaVersion — з базового IntegrationEvent (Platform.Messaging).
// Правило сумісності: нові поля додаються, наявні не перейменовуються й не видаляються.

/// <summary>
/// Заплановано новий сеанс (routing key screening.scheduled). Бронювання додає сеанс у свою копію dbo.Screenings
/// разом з розміром зали — інакше на сеанс неможливо забронювати місця.
/// </summary>
public sealed record ScreeningScheduledEvent(
    long ScreeningId,
    int MovieId,
    string MovieTitle,
    int HallId,
    string HallName,
    int RowsCount,
    int SeatsPerRow,
    DateTime StartsAt,
    string Format,
    decimal BasePrice) : IntegrationEvent;

/// <summary>
/// Змінилася базова ціна сеансу (screening.price_changed). Бронювання оновлює BasePrice у копії dbo.Screenings
/// для майбутніх бронювань; ціни в уже оформлених квитках (Tickets.UnitPrice) — знімок, вони не змінюються.
/// </summary>
public sealed record ScreeningPriceChangedEvent(
    long ScreeningId,
    int MovieId,
    string MovieTitle,
    decimal OldPrice,
    decimal NewPrice) : IntegrationEvent;

/// <summary>Сеанс скасовано (screening.cancelled): у копії Бронювання IsActive = 0, нові бронювання неможливі.</summary>
public sealed record ScreeningCancelledEvent(
    long ScreeningId,
    int MovieId,
    string MovieTitle,
    DateTime StartsAt) : IntegrationEvent;

/// <summary>
/// Фільм перейменовано (movie.renamed). Бронювання оновлює MovieTitle в усіх сеансах фільму,
/// Відгуки — movieTitle в усіх відгуках і обговореннях про нього.
/// </summary>
public sealed record MovieRenamedEvent(
    int MovieId,
    string OldTitle,
    string NewTitle,
    string? OriginalTitle) : IntegrationEvent;
