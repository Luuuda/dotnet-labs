using Platform.Messaging;

namespace BookingService.Bll.IntegrationEvents;

// Лаб. 08: контракти подій Каталогу, на які підписане Бронювання. Кожен сервіс тримає власну копію контракту
// (як в eShop: IntegrationEvents у кожному сервісі) — спільної бібліотеки контрактів, що зв'язала б сервіси
// одним релізом, немає. Збігатися мають лише назва типу (BasicProperties.Type) і JSON-поля (camelCase);
// поля, які Бронюванню не потрібні, можна не оголошувати — вони ігноруються під час десеріалізації.

/// <summary>Exchange і шаблони routing key, на які підписана черга Бронювання.</summary>
public static class CatalogEventsSubscription
{
    public const string Exchange = "catalog-events";
    /// <summary>Черга Бронювання (durable): x-dead-letter-exchange = catalog-events.dlx, DLQ — booking.catalog-sync.dlq.</summary>
    public const string Queue = "booking.catalog-sync";
    /// <summary>screening.* — scheduled / price_changed / cancelled; movie.* — renamed.</summary>
    public static readonly string[] BindingKeys = ["screening.*", "movie.*"];
}

public sealed record ScreeningScheduledEvent(
    long ScreeningId, int MovieId, string MovieTitle, int HallId, string HallName, int RowsCount, int SeatsPerRow,
    DateTime StartsAt, string Format, decimal BasePrice) : IntegrationEvent;

public sealed record ScreeningPriceChangedEvent(
    long ScreeningId, int MovieId, string MovieTitle, decimal OldPrice, decimal NewPrice) : IntegrationEvent;

public sealed record ScreeningCancelledEvent(
    long ScreeningId, int MovieId, string MovieTitle, DateTime StartsAt) : IntegrationEvent;

public sealed record MovieRenamedEvent(
    int MovieId, string OldTitle, string NewTitle, string? OriginalTitle) : IntegrationEvent;
