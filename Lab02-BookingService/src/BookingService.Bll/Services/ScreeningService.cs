using AutoMapper;
using BookingService.Bll.Dtos;
using BookingService.Dal.Abstractions;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;
using Microsoft.Extensions.Logging;

namespace BookingService.Bll.Services;

/// <summary>
/// Локальна копія сеансів Каталогу. Зміна сеансу тут НЕ змінює вже продані квитки:
/// MovieTitle / UnitPrice у Tickets — знімок (політика дублювання Лаб. 01).
/// </summary>
public sealed class ScreeningService(IUnitOfWork uow, IMapper mapper, ILogger<ScreeningService> logger) : IScreeningService
{
    public async Task<IReadOnlyList<ScreeningDto>> ListAsync(DateTime? from, bool onlyActive, CancellationToken ct)
    {
        var items = await uow.Screenings.GetUpcomingAsync(from ?? new DateTime(2000, 1, 1), onlyActive, ct);
        return mapper.Map<List<ScreeningDto>>(items);
    }

    public async Task<ScreeningDto> GetAsync(long id, CancellationToken ct) =>
        mapper.Map<ScreeningDto>(await Load(id, ct));

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
        logger.LogInformation("Додано сеанс {ScreeningId}", input.Id);
        return await GetAsync(input.Id, ct);
    }

    public async Task<ScreeningDto> UpdateAsync(long id, UpsertScreeningDto input, CancellationToken ct)
    {
        var screening = await Load(id, ct);
        mapper.Map(input, screening);
        await uow.Screenings.UpdateAsync(screening, ct);
        logger.LogInformation("Оновлено сеанс {ScreeningId}", id);
        return await GetAsync(id, ct);
    }

    public async Task DeleteAsync(long id, CancellationToken ct)
    {
        await Load(id, ct);
        // Якщо на сеанс уже є квитки, FK_Tickets_Screenings не дасть видалити → 547 → 409
        await uow.Screenings.DeleteAsync(id, ct);
        logger.LogInformation("Видалено сеанс {ScreeningId}", id);
    }

    private async Task<Screening> Load(long id, CancellationToken ct) =>
        await uow.Screenings.GetByIdAsync(id, ct) ?? throw new NotFoundException($"Сеанс {id} не знайдено");
}
