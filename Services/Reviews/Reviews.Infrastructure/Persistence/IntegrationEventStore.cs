using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// Лаб. 08: ідемпотентність споживача на MongoDB.
/// <list type="bullet">
/// <item><c>processedEvents</c>: <c>{ _id: "&lt;EventId&gt;", eventType, processedAt }</c> — унікальність EventId
/// гарантує сам <c>_id</c>; TTL-індекс прибирає записи через 30 днів (повторна доставка настільки старої події
/// неможлива — черга не зберігає повідомлення так довго);</item>
/// <item><c>movieTitleSync</c>: <c>{ _id: movieId, occurredAt }</c> — час останнього застосованого перейменування.</item>
/// </list>
/// </summary>
public sealed class IntegrationEventStore(MongoDbContext context) : IIntegrationEventStore
{
    public const string ProcessedEventsCollection = "processedEvents";
    public const string MovieTitleSyncCollection = "movieTitleSync";
    private const int DuplicateKey = 11000;

    private IMongoCollection<BsonDocument> Processed => context.Database.GetCollection<BsonDocument>(ProcessedEventsCollection);
    private IMongoCollection<BsonDocument> TitleSync => context.Database.GetCollection<BsonDocument>(MovieTitleSyncCollection);

    public async Task<bool> IsProcessedAsync(Guid eventId, CancellationToken ct = default) =>
        await Processed.Find(new BsonDocument("_id", eventId.ToString())).AnyAsync(ct);

    public async Task MarkProcessedAsync(Guid eventId, string eventType, CancellationToken ct = default)
    {
        try
        {
            await Processed.InsertOneAsync(new BsonDocument
            {
                ["_id"] = eventId.ToString(),
                ["eventType"] = eventType,
                ["processedAt"] = DateTime.UtcNow
            }, cancellationToken: ct);
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == DuplicateKey)
        {
            // уже позначено (паралельна або повторна доставка) — результат той самий
        }
    }

    /// <summary>
    /// Один атомарний updateOne з upsert: документ фільму з occurredAt ≤ нового оновлюється; якщо ж є документ з
    /// НОВІШИМ occurredAt, фільтр його не знаходить і upsert пробує вставити той самий _id → duplicate key → false.
    /// </summary>
    public async Task<bool> TryAdvanceMovieTitleAsync(int movieId, DateTime occurredAt, CancellationToken ct = default)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", movieId)
                     & Builders<BsonDocument>.Filter.Lte("occurredAt", occurredAt);
        try
        {
            await TitleSync.UpdateOneAsync(filter, Builders<BsonDocument>.Update.Set("occurredAt", occurredAt),
                new UpdateOptions { IsUpsert = true }, ct);
            return true;
        }
        catch (MongoWriteException ex) when (ex.WriteError.Code == DuplicateKey)
        {
            return false;
        }
    }

    /// <summary>TTL-індекс на processedEvents.processedAt (викликає IndexCreationService).</summary>
    public static Task<string> CreateIndexesAsync(MongoDbContext context, CancellationToken ct) =>
        context.Database.GetCollection<BsonDocument>(ProcessedEventsCollection).Indexes.CreateOneAsync(
            new CreateIndexModel<BsonDocument>(Builders<BsonDocument>.IndexKeys.Ascending("processedAt"),
                new CreateIndexOptions { Name = "ttl_processedAt", ExpireAfter = TimeSpan.FromDays(30) }),
            cancellationToken: ct);
}
