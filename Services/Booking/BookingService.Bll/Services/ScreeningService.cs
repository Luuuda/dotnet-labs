using AutoMapper;
using BookingService.Bll.Caching;
using BookingService.Bll.Dtos;
using BookingService.Dal.Abstractions;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging;

namespace BookingService.Bll.Services;

/// <summary>
/// Локальна копія сеансів Каталогу. Зміна сеансу тут НЕ змінює вже продані квитки:
/// MovieTitle / UnitPrice у Tickets — знімок (політика дублювання Лаб. 01).
/// Лаб. 06: копії сеансів — довідникові дані, які читає кожне бронювання й Aggregator, тож вони кешуються на двох
/// рівнях (booking:screening:{id}); додавання, оновлення й видалення інвалідують ключ і версію списків.
/// </summary>
public sealed class ScreeningService(IUnitOfWork uow, IMapper mapper, ITwoLevelCache cache, ILogger<ScreeningService> logger) : IScreeningService
{
    public async Task<IReadOnlyList<ScreeningDto>> ListAsync(DateTime? from, bool onlyActive, CancellationToken ct)
    {
        var version = await cache.GetVersionAsync(CacheKeys.ScreeningsListVersion, ct);
        return (await cache.GetOrCreateAsync(CacheKeys.ScreeningsList(version, from, onlyActive), CachePolicy.List, async c =>
            mapper.Map<List<ScreeningDto>>(await uow.Screenings.GetUpcomingAsync(from ?? new DateTime(2000, 1, 1), onlyActive, c)), ct))!;
    }

    public async Task<ScreeningDto> GetAsync(long id, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKeys.Screening(id), CachePolicy.Reference,
            async c => mapper.Map<ScreeningDto>(await Load(id, c)), ct))!;

    public async Task<IReadOnlyList<ScreeningDto>> GetByIdsAsync(IReadOnlyCollection<long> ids, CancellationToken ct)
    {
        var result = new List<ScreeningDto>(ids.Count);
        foreach (var id in ids.Distinct())
        {
            try { result.Add(await GetAsync(id, ct)); }
            catch (NotFoundException) { /* відсутні сеанси не повертаються */ }
        }
        return result;
    }

    public async Task<SeatMapDto> GetSeatMapAsync(long id, CancellationToken ct)
    {
        var screening = await Load(id, ct);
        var occupied = await uow.Bookings.GetOccupiedSeatsAsync(id, ct);
        return new SeatMapDto
        {
            ScreeningId = id,
            RowsCount = screening.RowsCount,
            SeatsPerRow = screening.SeatsPerRow,
            FreeCount = screening.RowsCount * screening.SeatsPerRow - occupied.Count,
            Occupied = occupied.Select(s => new SeatDto { Row = s.Row, Seat = s.Seat }).ToList()
        };
    }

    public async Task<ScreeningDto> CreateAsync(CreateScreeningDto input, CancellationToken ct)
    {
        if (await uow.Screenings.GetByIdAsync(input.Id, ct) is not null)
            throw new BusinessConflictException($"Сеанс {input.Id} уже є в локальній копії");

        await uow.Screenings.AddAsync(mapper.Map<Screening>(input), ct);
        await InvalidateAsync(input.Id, "ScreeningAdded", ct);
        logger.LogInformation("Додано сеанс {ScreeningId}", input.Id);
        return await GetAsync(input.Id, ct);
    }

    public async Task<ScreeningDto> UpdateAsync(long id, UpsertScreeningDto input, CancellationToken ct)
    {
        var screening = await Load(id, ct);
        mapper.Map(input, screening);
        await uow.Screenings.UpdateAsync(screening, ct);
        await InvalidateAsync(id, "ScreeningUpdated", ct);
        logger.LogInformation("Оновлено сеанс {ScreeningId}", id);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await Load(id, ct);
        // Якщо на сеанс уже є квитки, FK_Tickets_Screenings не дасть видалити → 547 → 409
        await uow.Screenings.DeleteAsync(id, ct);
        await InvalidateAsync(id, "ScreeningDeleted", ct);
        logger.LogInformation("Видалено сеанс {ScreeningId}", id);
    }

    private async Task InvalidateAsync(long id, string reason, CancellationToken ct)
    {
        await cache.InvalidateAsync(reason, ct, CacheKeys.Screening(id));
        await cache.BumpVersionAsync(CacheKeys.ScreeningsListVersion, reason, ct);
    }

    private async Task<Screening> Load(long id, CancellationToken ct) =>
        await uow.Screenings.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Сеанс {id} не знайдено");
}
