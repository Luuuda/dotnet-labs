using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Application.Common.Interfaces;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// Лаб. 07: перевірка індексів колекції reviews (їх створює IndexCreationService на старті). Без унікального
/// ux_author_movie один глядач міг би залишити два відгуки на фільм, тож сервіс ще не готовий приймати запити.
/// </summary>
public sealed class MongoStoreReadiness(MongoDbContext context) : IStoreReadiness
{
    public const string RequiredIndex = "ux_author_movie";

    public async Task<StoreReadiness> CheckIndexesAsync(CancellationToken ct)
    {
        using var cursor = await context.RawReviews.Indexes.ListAsync(ct);
        var names = (await cursor.ToListAsync(ct))
            .Select(i => i.GetValue("name", BsonNull.Value).ToString() ?? "")
            .ToList();
        return names.Contains(RequiredIndex)
            ? new StoreReadiness(true, $"Індекси колекції reviews на місці ({names.Count})", names)
            : new StoreReadiness(false, $"Немає індексу {RequiredIndex}: IndexCreationService ще не відпрацював", names);
    }
}
