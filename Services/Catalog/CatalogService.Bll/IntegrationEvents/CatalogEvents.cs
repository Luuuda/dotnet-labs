namespace CatalogService.Bll.IntegrationEvents;

/// <summary>
/// Лаб. 08: контракт подій Каталогу в RabbitMQ — exchange і routing keys. Каталог — власник фільмів і сеансів;
/// інші сервіси тримають їхні копії (політика дублювання Лаб. 01) і оновлюють їх цими подіями.
/// Publisher не знає, хто підписаний: Бронювання слухає screening.* і movie.*, Відгуки — movie.*.
/// </summary>
public static class CatalogEvents
{
    /// <summary>Durable topic exchange подій Каталогу.</summary>
    public const string Exchange = "catalog-events";

    public static class RoutingKeys
    {
        public const string ScreeningScheduled = "screening.scheduled";
        public const string ScreeningPriceChanged = "screening.price_changed";
        public const string ScreeningCancelled = "screening.cancelled";
        public const string MovieRenamed = "movie.renamed";
    }
}
