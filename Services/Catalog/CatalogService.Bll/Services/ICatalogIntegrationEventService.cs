using CatalogService.Bll.Dtos;
using CatalogService.Domain.Entities;

namespace CatalogService.Bll.Services;

/// <summary>
/// Лаб. 08: публікація інтеграційних подій Каталогу (як CatalogIntegrationEventService в eShop). Бізнес-сервіси
/// викликають ці методи ЛИШЕ після успішного SaveChangesAsync. Реалізація — CatalogService.Api/IntegrationEvents:
/// там події (record-и), exchange / routing keys і IEventPublisher (RabbitMQ). BLL про брокер нічого не знає.
/// </summary>
public interface ICatalogIntegrationEventService
{
    /// <summary>ScreeningScheduledEvent (screening.scheduled).</summary>
    Task PublishScreeningScheduledAsync(Screening screening, Movie movie, Hall hall, CancellationToken ct);

    /// <summary>ScreeningPriceChangedEvent (screening.price_changed).</summary>
    Task PublishScreeningPriceChangedAsync(ScreeningDto screening, decimal oldPrice, CancellationToken ct);

    /// <summary>ScreeningCancelledEvent (screening.cancelled).</summary>
    Task PublishScreeningCancelledAsync(ScreeningDto screening, CancellationToken ct);

    /// <summary>MovieRenamedEvent (movie.renamed).</summary>
    Task PublishMovieRenamedAsync(int movieId, string oldTitle, string newTitle, string? originalTitle, CancellationToken ct);
}
