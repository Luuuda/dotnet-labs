using MongoDB.Bson;

namespace Reviews.Domain.Common;

/// <summary>
/// Базова сутність для документів MongoDB: ідентифікатор ObjectId, часові мітки
/// та номер версії для оптимістичної конкурентності.
/// </summary>
public abstract class BaseEntity
{
    public ObjectId Id { get; protected set; } = ObjectId.GenerateNewId();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }

    /// <summary>Номер версії документа. Збільшується репозиторієм під час кожного збереження змін.</summary>
    public int Version { get; protected set; }

    protected void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Викликає репозиторій перед записом: нова версія = очікувана + 1.</summary>
    public void BumpVersion() => Version++;
}
