namespace Reviews.Application.Common.Interfaces;

/// <summary>
/// Лаб. 08: облік оброблених інтеграційних подій (ідемпотентність споживача). Реалізація — Infrastructure
/// (колекції MongoDB processedEvents з _id = EventId і movieTitleSync). Application про брокер нічого не знає.
/// </summary>
public interface IIntegrationEventStore
{
    /// <summary>Чи подію з цим EventId уже оброблено (повторна доставка).</summary>
    Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct = default);

    /// <summary>Позначає подію обробленою; повторна позначка (той самий EventId) — без помилки.</summary>
    Task MarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct = default);

    /// <summary>
    /// Захист від подій не в тому порядку: true, якщо для фільму ще не застосовано НОВІШОГО перейменування
    /// (час останнього застосованого ≤ <paramref name="occurredAt"/>; той самий час — повтор тієї ж події).
    /// Атомарно запам'ятовує <paramref name="occurredAt"/> як час останнього перейменування.
    /// </summary>
    Task<bool> TryAdvanceMovieTitleAsync(int movieId, DateTime occurredAt, CancellationToken ct = default);
}
