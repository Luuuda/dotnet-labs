namespace CatalogService.Domain.Entities;

/// <summary>
/// Аудитні поля (Лаб. 01, крок 6). Заповнюються автоматично інтерсептором DAL під час SaveChanges —
/// сервісам не потрібно про них пам'ятати.
/// </summary>
public abstract class AuditableEntity
{
    public DateTime CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = "system";
    public DateTime? UpdatedAt { get; private set; }
    public string? UpdatedBy { get; private set; }

    public void MarkCreated(DateTime at, string by)
    {
        CreatedAt = at;
        CreatedBy = by;
    }

    public void MarkUpdated(DateTime at, string by)
    {
        UpdatedAt = at;
        UpdatedBy = by;
    }
}
