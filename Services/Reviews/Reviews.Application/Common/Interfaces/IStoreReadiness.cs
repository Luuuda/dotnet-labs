namespace Reviews.Application.Common.Interfaces;

/// <summary>Результат перевірки готовності сховища: чи готове, опис і назви знайдених індексів.</summary>
public sealed record StoreReadiness(bool IsReady, string Description, IReadOnlyList<string> Indexes);

/// <summary>
/// Лаб. 07: готовність сховища відгуків для health check (WebApi/HealthChecks). Як і IReviewQueries, приховує
/// MongoDB за інтерфейсом: WebApi не звертається до колекцій напряму (правило ArchitectureTests з Лаб. 04).
/// </summary>
public interface IStoreReadiness
{
    /// <summary>Чи створено обов'язкові індекси (зокрема унікальний ux_author_movie). Легкий запит listIndexes.</summary>
    Task<StoreReadiness> CheckIndexesAsync(CancellationToken ct);
}
