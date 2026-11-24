using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;

namespace Reviews.Infrastructure.Persistence.Repositories;

/// <summary>
/// Спільна логіка запису для документів з версією (оптимістична конкурентність):
/// ReplaceOne / DeleteOne застосовуються лише до документа з ОЧІКУВАНОЮ версією.
/// </summary>
public abstract class MongoRepository<T>(IMongoCollection<T> collection) where T : BaseEntity
{
    protected IMongoCollection<T> Collection { get; } = collection;
    protected static FilterDefinitionBuilder<T> Filter => Builders<T>.Filter;

    public async Task<T?> GetByIdAsync(ObjectId id, CancellationToken ct = default)
        => await Collection.Find(Filter.Eq(e => e.Id, id)).FirstOrDefaultAsync(ct);

    public Task AddAsync(T entity, CancellationToken ct = default)
        => Collection.InsertOneAsync(entity, cancellationToken: ct);

    public async Task UpdateAsync(T entity, CancellationToken ct = default)
    {
        var expectedVersion = entity.Version;
        var filter = Filter.Eq(e => e.Id, entity.Id) & VersionIs(expectedVersion);
        entity.BumpVersion();

        var result = await Collection.ReplaceOneAsync(filter, entity, cancellationToken: ct);
        if (result.MatchedCount == 0)                     // хтось змінив (або видалив) документ раніше
            throw new ConflictException(
                $"{typeof(T).Name} {entity.Id} змінено іншим запитом (очікувана версія {expectedVersion}). Отримайте актуальну версію і повторіть.");
    }

    public async Task DeleteAsync(T entity, CancellationToken ct = default)
    {
        var result = await Collection.DeleteOneAsync(Filter.Eq(e => e.Id, entity.Id) & VersionIs(entity.Version), ct);
        if (result.DeletedCount == 0)
            throw new ConflictException($"{typeof(T).Name} {entity.Id} змінено іншим запитом — видалення скасовано");
    }

    /// <summary>Документи, створені скриптами Лаб. 01, не мають поля version — для них це «версія 0».</summary>
    private static FilterDefinition<T> VersionIs(int version)
        => version == 0
            ? Filter.Eq(e => e.Version, 0) | Filter.Exists(e => e.Version, false)
            : Filter.Eq(e => e.Version, version);
}
