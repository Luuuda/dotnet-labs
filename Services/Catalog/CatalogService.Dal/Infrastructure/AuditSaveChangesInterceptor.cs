using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CatalogService.Dal.Infrastructure;

/// <summary>Автоматично заповнює CreatedAt/CreatedBy/UpdatedAt/UpdatedBy перед збереженням.</summary>
public sealed class AuditSaveChangesInterceptor(TimeProvider clock) : SaveChangesInterceptor
{
    public const string Actor = "catalog-api";

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (eventData.Context is not null)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            foreach (var entry in eventData.Context.ChangeTracker.Entries<AuditableEntity>())
            {
                if (entry.State == EntityState.Added) entry.Entity.MarkCreated(now, Actor);
                else if (entry.State == EntityState.Modified) entry.Entity.MarkUpdated(now, Actor);
            }
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}
