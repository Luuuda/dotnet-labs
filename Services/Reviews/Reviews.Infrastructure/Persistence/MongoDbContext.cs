using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Reviews.Domain.Entities;
using Reviews.Infrastructure.Persistence.ReadModels;

namespace Reviews.Infrastructure.Persistence;

/// <summary>
/// Точка доступу до MongoDB. Singleton: MongoClient потокобезпечний і тримає пул з'єднань
/// (maxPoolSize, serverSelectionTimeoutMS задаються в рядку підключення).
/// Колекції сутностей — для запису (репозиторії), колекції read-моделей — для читання (Queries).
/// </summary>
public sealed class MongoDbContext
{
    public const string ReviewsCollection = "reviews";
    public const string DiscussionsCollection = "discussions";

    public MongoDbContext(IOptions<MongoDbOptions> options)
    {
        MongoMappings.Register();                        // мапінг має бути зареєстрований до першого звернення до БД

        var settings = MongoClientSettings.FromConnectionString(options.Value.ConnectionString);
        options.Value.ConfigureClientSettings?.Invoke(settings);
        settings.ApplicationName = "cinema-reviews-api";
        Client = new MongoClient(settings);
        Database = Client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    // запис (доменні сутності)
    public IMongoCollection<Review> Reviews => Database.GetCollection<Review>(ReviewsCollection);
    public IMongoCollection<Discussion> Discussions => Database.GetCollection<Discussion>(DiscussionsCollection);

    // читання (плоскі read-моделі тієї самої колекції — CQRS)
    public IMongoCollection<ReviewDocument> ReviewDocuments => Database.GetCollection<ReviewDocument>(ReviewsCollection);
    public IMongoCollection<DiscussionDocument> DiscussionDocuments => Database.GetCollection<DiscussionDocument>(DiscussionsCollection);
    public IMongoCollection<BsonDocument> RawReviews => Database.GetCollection<BsonDocument>(ReviewsCollection);
}
