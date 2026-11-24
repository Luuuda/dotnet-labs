namespace Platform.Messaging;

/// <summary>
/// Базовий тип інтеграційної події (Лаб. 08). Подія — незмінний record: бізнес-факт, що вже стався,
/// тому назви нащадків у минулому часі (ScreeningPriceChangedEvent, MovieRenamedEvent).
/// <list type="bullet">
/// <item><see cref="EventId"/> — унікальний ідентифікатор; він же MessageId повідомлення і ключ таблиці / колекції
/// оброблених подій у споживачів (ідемпотентність);</item>
/// <item><see cref="OccurredAt"/> — коли подія сталася в сервісі-власнику (UTC); споживачі порівнюють його з часом
/// останньої застосованої зміни, щоб пізня подія не перезаписала свіжіші дані;</item>
/// <item><see cref="CorrelationId"/> — бізнес-ідентифікатор запиту з Лаб. 05 (X-Correlation-Id): за ним у Dashboard
/// знаходяться логи HTTP-запиту, публікації й обробки в інших сервісах;</item>
/// <item><see cref="SchemaVersion"/> — версія схеми події: нові поля додаються, наявні не перейменовуються й не
/// видаляються, тож старі споживачі лишаються сумісними.</item>
/// </list>
/// Подія самодостатня: містить усі дані, потрібні споживачеві, — по додаткові дані до Каталогу не звертаються.
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
    public string? CorrelationId { get; init; }
    public int SchemaVersion { get; init; } = 1;
}
