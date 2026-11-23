using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;

namespace Reviews.Infrastructure.Persistence;

/// <summary>Секція "MongoDb" в appsettings.{Environment}.json. Пул і таймаути — параметрами рядка підключення.</summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    [Required] public string ConnectionString { get; set; } = "";
    [Required] public string DatabaseName { get; set; } = "";

    /// <summary>Додаткове налаштування клієнта (Лаб. 05: підписка на трасування задається у WebApi).</summary>
    public Action<MongoClientSettings>? ConfigureClientSettings { get; set; }
}
