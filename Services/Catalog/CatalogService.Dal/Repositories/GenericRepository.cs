using Ardalis.Specification;
using Ardalis.Specification.EntityFrameworkCore;
using CatalogService.Dal.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Dal.Repositories;

/// <summary>
/// Async Generic Repository на EF Core. Специфікацію репозиторій лише ІНТЕРПРЕТУЄ через
/// WithSpecification(spec) — тому нова специфікація з іншими критеріями працює без змін у репозиторії.
/// </summary>
public class GenericRepository<T>(CatalogDbContext context) : IGenericRepository<T> where T : class
{
    protected readonly CatalogDbContext Context = context;
    protected readonly DbSet<T> Set = context.Set<T>();

    public virtual async Task<T?> GetByIdAsync(object id, CancellationToken ct = default)
        => await Set.FindAsync([id], ct);

    public virtual async Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default)
        => await Set.AsNoTracking().ToListAsync(ct);

    public virtual async Task AddAsync(T entity, CancellationToken ct = default)
        => await Set.AddAsync(entity, ct);

    public virtual void Update(T entity) => Set.Update(entity);

    public virtual void Delete(T entity) => Set.Remove(entity);

    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Context.SaveChangesAsync(ct);

    // ---- Specification pattern ----------------------------------------------------------

    public async Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken ct = default)
        => await Set.WithSpecification(specification).ToListAsync(ct);

    public Task<int> CountAsync(ISpecification<T> specification, CancellationToken ct = default)
        // evaluateCriteriaOnly: лише Where — без Include, OrderBy, Skip/Take → SELECT COUNT(*) у БД
        => SpecificationEvaluator.Default.GetQuery(Set.AsQueryable(), specification, evaluateCriteriaOnly: true)
                                         .CountAsync(ct);

    public Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken ct = default)
        => SpecificationEvaluator.Default.GetQuery(Set.AsQueryable(), specification, evaluateCriteriaOnly: true)
                                         .AnyAsync(ct);

    public Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken ct = default)
        => Set.WithSpecification(specification).FirstOrDefaultAsync(ct);
}
