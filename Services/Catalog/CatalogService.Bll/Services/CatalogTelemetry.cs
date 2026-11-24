using System.Diagnostics;

namespace CatalogService.Bll.Services;

/// <summary>Власні spans бізнес-операцій каталогу (Лаб. 05); ServiceDefaults підписується на «Cinema.*».</summary>
public static class CatalogTelemetry
{
    public static readonly ActivitySource Source = new("Cinema.Catalog");
}
