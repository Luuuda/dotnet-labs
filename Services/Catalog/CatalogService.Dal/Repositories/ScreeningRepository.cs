using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Dal.Repositories;

public sealed class ScreeningRepository(CatalogDbContext context)
    : GenericRepository<Screening>(context), IScreeningRepository
{
    public Task<bool> HasOverlapAsync(int hallId, DateTime startsAt, DateTime endsAt, long? exceptId,
                                      CancellationToken ct = default)
        => Set.AnyAsync(s => s.HallId == hallId && !s.IsCancelled
                             && (exceptId == null || s.Id != exceptId)
                             && s.StartsAt < endsAt && startsAt < s.EndsAt, ct);
}
