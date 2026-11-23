namespace BookingService.Dal.Abstractions;

/// <summary>
/// Узагальнений CRUD-контракт. Конкретні інтерфейси успадковують його й додають лише
/// вузькоспеціалізовані запити (JOIN, агрегації, виклики процедур).
/// Методи повертають моделі — ніколи SqlDataReader, DataTable чи «сирий» SQL.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task<long> AddAsync(T entity, CancellationToken ct = default);
    Task<bool> DeleteAsync(long id, CancellationToken ct = default);
}
