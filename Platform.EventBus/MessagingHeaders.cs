namespace Platform.EventBus;

/// <summary>Назви заголовків повідомлень (RabbitMQ headers) — однакові в publisher і consumer.</summary>
public static class MessagingHeaders
{
    /// <summary>Бізнес-трасування (Лаб. 05): той самий CorrelationId, що й у HTTP-заголовку X-Correlation-Id.</summary>
    public const string CorrelationId = "x-correlation-id";
    /// <summary>Номер повтору обробки; збільшується під час повторної публікації копії повідомлення.</summary>
    public const string RetryCount = "x-retry-count";
    /// <summary>Версія схеми події.</summary>
    public const string SchemaVersion = "x-schema-version";
    /// <summary>Коли подія сталася в сервісі-власнику (ISO 8601).</summary>
    public const string OccurredAt = "x-occurred-at";
    /// <summary>Остання помилка обробки (додається до копії перед повтором) — для розбору повідомлень у DLQ.</summary>
    public const string LastError = "x-last-error";
}
