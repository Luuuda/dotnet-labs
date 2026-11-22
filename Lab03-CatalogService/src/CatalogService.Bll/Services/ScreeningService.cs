using AutoMapper;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.Specifications;
using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace CatalogService.Bll.Services;

public sealed class ScreeningService(IUnitOfWork uow, IMapper mapper, ILogger<ScreeningService> logger) : IScreeningService
{
    public async Task<PagedResult<ScreeningDto>> GetListAsync(ScreeningQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var spec = new ScreeningsFilterSpec(query, page, pageSize);
        var total = await uow.Screenings.CountAsync(spec, ct);
        var items = await uow.Screenings.ListAsync(spec, ct);
        return new PagedResult<ScreeningDto>(mapper.Map<List<ScreeningDto>>(items), total, page, pageSize);
    }

    public async Task<ScreeningDto> GetByIdAsync(long id, CancellationToken ct) =>
        mapper.Map<ScreeningDto>(await uow.Screenings.FirstOrDefaultAsync(new ScreeningByIdSpec(id), ct)
                                 ?? throw EntityNotFoundException.For<Screening>(id));

    public async Task<ScreeningDto> CreateAsync(CreateScreeningDto input, CancellationToken ct)
    {
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
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Заплановано сеанс {ScreeningId}: фільм {MovieId}, зала {HallId}", screening.Id, movie.Id, hall.Id);
        return await GetByIdAsync(screening.Id, ct);
    }

    public async Task<ScreeningDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct)
    {
        var screening = await uow.Screenings.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Screening>(id);
        screening.ChangePrice(input.BasePrice);                 // скасований сеанс → DomainException → 409
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Сеанс {ScreeningId}: нова ціна {Price}", id, input.BasePrice);
        return await GetByIdAsync(id, ct);
    }

    public async Task CancelAsync(long id, CancellationToken ct)
    {
        var screening = await uow.Screenings.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Screening>(id);
        screening.Cancel();                                     // повторне скасування → DomainException → 409
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Сеанс {ScreeningId} скасовано", id);
    }
}
