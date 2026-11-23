using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using Reviews.Domain.Common;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;
using Reviews.Infrastructure.Persistence.Serialization;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// BSON-мапінг доменних класів БЕЗ атрибутів у домені. Реєструється один раз під час старту,
/// до першого звернення до БД (повторний виклик нічого не робить).
/// </summary>
public static class MongoMappings
{
    private static readonly object Sync = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Sync)
        {
            if (_registered) return;

            // camelCase для всіх наших типів (movieId, createdAt, author.displayName…);
            // зайві поля документа не ламають десеріалізацію (гнучка схема).
            ConventionRegistry.Register("reviews-conventions",
                new ConventionPack
                {
                    new CamelCaseElementNameConvention(),
                    new IgnoreExtraElementsConvention(true)
                },
                type => type.Namespace?.StartsWith("Reviews.", StringComparison.Ordinal) == true);

            // Value Objects, що зберігаються як прості значення
            BsonSerializer.RegisterSerializer(new RatingSerializer());
            BsonSerializer.RegisterSerializer(new ReviewAspectsSerializer());

            // Сутності мапляться ЯВНО (без AutoMap): AutoMap сприйняв би публічні конструктори з валідацією
            // як «creator» для десеріалізації. Так документ відновлюється через приватний конструктор,
            // а імена полів — camelCase, як у документах з Лаб. 01.
            BsonClassMap.RegisterClassMap<BaseEntity>(cm =>
            {
                cm.MapIdMember(e => e.Id);                                                // _id
                cm.MapMember(e => e.CreatedAt).SetElementName("createdAt");
                cm.MapMember(e => e.UpdatedAt).SetElementName("updatedAt").SetIgnoreIfNull(true);
                cm.MapMember(e => e.Version).SetElementName("version");
            });

            // Незмінний Value Object: створюється через конструктор (з валідацією), а не через setter-и
            BsonClassMap.RegisterClassMap<Author>(cm =>
            {
                cm.MapProperty(a => a.UserId).SetElementName("userId");
                cm.MapProperty(a => a.DisplayName).SetElementName("displayName");
                cm.MapCreator(a => new Author(a.UserId, a.DisplayName));
                cm.SetIgnoreExtraElements(true);
            });

            BsonClassMap.RegisterClassMap<Comment>(cm =>
            {
                cm.MapIdMember(c => c.Id);                                                // comments[]._id
                cm.MapMember(c => c.Author).SetElementName("author");
                cm.MapMember(c => c.Text).SetElementName("text");
                cm.MapMember(c => c.CreatedAt).SetElementName("createdAt");
                cm.SetIgnoreExtraElements(true);
            });

            BsonClassMap.RegisterClassMap<Review>(cm =>
            {
                cm.MapMember(r => r.MovieId).SetElementName("movieId");
                cm.MapMember(r => r.MovieTitle).SetElementName("movieTitle");
                cm.MapMember(r => r.Author).SetElementName("author");
                cm.MapMember(r => r.Rating).SetElementName("rating");                    // RatingSerializer → число
                cm.MapMember(r => r.Text).SetElementName("text").SetIgnoreIfNull(true);
                cm.MapMember(r => r.Aspects).SetElementName("aspects").SetIgnoreIfNull(true);
                cm.MapMember(r => r.VerifiedViewer).SetElementName("verifiedViewer");
                cm.MapField("_comments").SetElementName("comments");                     // приватне поле → вкладений масив
                cm.MapMember(r => r.CommentsCount).SetElementName("commentsCount");
                cm.MapExtraElementsField("_extraElements");                              // невідомі поля гнучкої схеми зберігаються як є
            });

            BsonClassMap.RegisterClassMap<Discussion>(cm =>
            {
                cm.MapMember(d => d.MovieId).SetElementName("movieId");
                cm.MapMember(d => d.MovieTitle).SetElementName("movieTitle");
                cm.MapMember(d => d.Title).SetElementName("title");
                cm.MapMember(d => d.CreatedBy).SetElementName("createdBy");
                cm.MapMember(d => d.Status).SetElementName("status")
                  .SetSerializer(new LowercaseEnumSerializer<DiscussionStatus>());       // "open" / "closed"
                cm.MapField("_tags").SetElementName("tags");
                cm.MapMember(d => d.PostsCount).SetElementName("postsCount");
                cm.MapMember(d => d.LastActivityAt).SetElementName("lastActivityAt");
                cm.MapMember(d => d.ClosedAt).SetElementName("closedAt").SetIgnoreIfNull(true);
                cm.MapMember(d => d.ClosedReason).SetElementName("closedReason").SetIgnoreIfNull(true);
                cm.MapExtraElementsField("_extraElements");
            });

            _registered = true;
        }
    }
}
