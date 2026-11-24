using AutoMapper;
using CatalogService.Bll.Caching;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.IntegrationEvents;
using CatalogService.Bll.Specifications;
using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Platform.Messaging;

namespace CatalogService.Bll.Services;

/// <summary>
/// Розклад. Лаб. 06: сеанс і сторінки розкладу читаються через дворівневий кеш, запис інвалідує їх синхронно.
/// Лаб. 08: кожна операція запису після успішного SaveChangesAsync публікує інтеграційну подію в catalog-events
/// (ScreeningScheduled / ScreeningPriceChanged / ScreeningCancelled). Якщо збереження не вдалося (виняток, 409),
/// до публікації виконання не доходить — подія про зміну, якої немає, не з'явиться.
/// </summary>
public sealed class ScreeningService(IUnitOfWork uow, IMapper mapper, ITwoLevelCache cache, IEventPublisher events,
    ILogger<ScreeningService> logger) : IScreeningService
{
    public async Task<PagedResult<ScreeningDto>> GetListAsync(ScreeningQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var version = await cache.GetVersionAsync(CacheKeys.ScreeningsListVersion, ct);
        return (await cache.GetOrCreateAsync(CacheKeys.ScreeningsList(version, CacheKeyHash.Of(query)), CachePolicy.List, async c =>
        {
            var spec = new ScreeningsFilterSpec(query, page, pageSize);
            var total = await uow.Screenings.CountAsync(spec, c);
            var items = await uow.Screenings.ListAsync(spec, c);
            return new PagedResult<ScreeningDto>(mapper.Map<List<ScreeningDto>>(items), total, page, pageSize);
        }, ct))!;
    }

    /// <summary>Сеанс: catalog:screening:{id}.</summary>
    public async Task<ScreeningDto> GetByIdAsync(long id, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKeys.Screening(id), CachePolicy.Card, async c =>
            mapper.Map<ScreeningDto>(await uow.Screenings.FirstOrDefaultAsync(new ScreeningByIdSpec(id), c)
                                     ?? throw EntityNotFoundException.For<Screening>(id)), ct))!;

    public async Task<IReadOnlyList<ScreeningDto>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var result = new List<ScreeningDto>(ids.Count);
        foreach (var id in ids.Distinct())
        {
            try { result.Add(await GetByIdAsync(id, ct)); }
            catch (EntityNotFoundException) { /* відсутні сеанси не повертаються */ }
        }
        return result;
    }

    public async Task<ScreeningDto> CreateAsync(CreateScreeningDto input, CancellationToken ct)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("ScheduleScreening");
        activity?.SetTag("movie.id", input.MovieId);
        activity?.SetTag("hall.id", input.HallId);
        var movie = await uow.Movies.GetByIdAsync(input.MovieId, ct)
                    ?? throw new ValidationException("movieId", $"Фільм {input.MovieId} не знайдено");
        var hall = await uow.Halls.GetByIdAsync(input.HallId, ct)
                   ?? throw new ValidationException("hallId", $"Залу {input.HallId} не знайдено");

        // Правила самої сутності (ціна > 0, формат, IMAX лише в IMAX-залі) перевіряє конструктор Screening
        var screening = new Screening(movie, hall, input.StartsAt, input.Format, input.Language, input.BasePrice);

        // Правило, якому потрібні інші дані з БД: зала вільна на весь час сеансу
        if (await uow.Screenings.HasOverlapAsync(hall.Id, screening.StartsAt, screening.EndsAt, null, ct))
            throw new ConflictException($"Зала «{hall.Name}» зайнята в цей час ({screening.StartsAt:dd.MM HH:mm}–{screening.EndsAt:HH:mm})");

        await uow.Screenings.AddAsync(screening, ct);
        await uow.SaveChangesAsync(ct);                                     // 1) зміни збережено
        await InvalidateAsync(null, movie.Id, "ScreeningScheduled", ct);   // у картці фільму — найближчі сеанси
        activity?.SetTag("screening.id", screening.Id);
        logger.LogInformation("Заплановано сеанс {ScreeningId}: фільм {MovieId}, зала {HallId}, {StartsAt}, ціна {Price}",
            screening.Id, movie.Id, hall.Id, screening.StartsAt, screening.BasePrice);

        await events.PublishAsync(                                          // 2) лише тоді — подія
            new ScreeningScheduledEvent(screening.Id, movie.Id, movie.Title, hall.Id, hall.Name, hall.RowsCount,
                hall.SeatsPerRow, screening.StartsAt, screening.Format, screening.BasePrice)
            { CorrelationId = CorrelationContext.Current },
            CatalogEvents.Exchange, CatalogEvents.RoutingKeys.ScreeningScheduled, ct);
        return await GetByIdAsync(screening.Id, ct);
    }

    public async Task<ScreeningDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("ChangeScreeningPrice");
        activity?.SetTag("screening.id", id);
        activity?.SetTag("screening.price", input.BasePrice);
        var screening = await uow.Screenings.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Screening>(id);
        var oldPrice = screening.BasePrice;
        screening.ChangePrice(input.BasePrice);                 // скасований сеанс → DomainException → 409
        await uow.SaveChangesAsync(ct);                         // 1) зміни збережено
        await InvalidateAsync(id, screening.MovieId, "ScreeningPriceChanged", ct);
        logger.LogInformation("Сеанс {ScreeningId}: нова ціна {Price}", id, input.BasePrice);

        var changed = await GetByIdAsync(id, ct);
        if (oldPrice != changed.BasePrice)                      // 2) лише тоді — подія (і лише якщо ціна справді змінилася)
            await events.PublishAsync(
                new ScreeningPriceChangedEvent(id, changed.MovieId, changed.MovieTitle, oldPrice, changed.BasePrice)
                { CorrelationId = CorrelationContext.Current },
                CatalogEvents.Exchange, CatalogEvents.RoutingKeys.ScreeningPriceChanged, ct);
        return changed;
    }

    public async Task CancelAsync(long id, CancellationToken ct)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("CancelScreening");
        activity?.SetTag("screening.id", id);
        var screening = await uow.Screenings.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Screening>(id);
        screening.Cancel();                                     // повторне скасування → DomainException → 409
        await uow.SaveChangesAsync(ct);                         // 1) зміни збережено
        await InvalidateAsync(id, screening.MovieId, "ScreeningCancelled", ct);
        logger.LogInformation("Сеанс {ScreeningId} скасовано, статус {Status}", id, "Cancelled");

        var cancelled = await GetByIdAsync(id, ct);             // 2) лише тоді — подія
        await events.PublishAsync(
            new ScreeningCancelledEvent(id, cancelled.MovieId, cancelled.MovieTitle, cancelled.StartsAt)
            { CorrelationId = CorrelationContext.Current },
            CatalogEvents.Exchange, CatalogEvents.RoutingKeys.ScreeningCancelled, ct);
    }

    /// <summary>Синхронно із записом: сеанс, картка його фільму і всі сторінки розкладу.</summary>
    private async Task InvalidateAsync(long? screeningId, int movieId, string reason, CancellationToken ct)
    {
        var keys = screeningId is { } id ? new[] { CacheKeys.Screening(id), CacheKeys.Movie(movieId) } : new[] { CacheKeys.Movie(movieId) };
        await cache.InvalidateAsync(reason, ct, keys);
        await cache.BumpVersionAsync(CacheKeys.ScreeningsListVersion, reason, ct);
    }
}
