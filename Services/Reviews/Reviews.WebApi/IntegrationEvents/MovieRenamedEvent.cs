using Platform.Messaging;

namespace Reviews.WebApi.IntegrationEvents;

/// <summary>Лаб. 08: exchange і шаблони routing key, на які підписана черга Відгуків.</summary>
public static class CatalogEventsSubscription
{
    public const string Exchange = "catalog-events";
    /// <summary>Черга Відгуків (durable): x-dead-letter-exchange = catalog-events.dlx, DLQ — reviews.catalog-sync.dlq.</summary>
    public const string Queue = "reviews.catalog-sync";
    /// <summary>Відгукам потрібні лише події фільмів; події сеансів у цю чергу не потрапляють.</summary>
    public static readonly string[] BindingKeys = ["movie.*"];
}

/// <summary>
/// Лаб. 08: копія контракту події Каталогу «фільм перейменовано» (routing key movie.renamed). Збігатися з Каталогом
/// мають назва типу (BasicProperties.Type) і JSON-поля; EventId / OccurredAt / CorrelationId — з IntegrationEvent.
/// </summary>
public sealed record MovieRenamedEvent(int MovieId, string OldTitle, string NewTitle, string? OriginalTitle) : IntegrationEvent;
