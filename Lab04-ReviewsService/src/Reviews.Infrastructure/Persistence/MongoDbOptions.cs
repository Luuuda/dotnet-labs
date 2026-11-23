using System.ComponentModel.DataAnnotations;

namespace Reviews.Infrastructure.Persistence;

/// <summary>Секція "MongoDb" в appsettings.{Environment}.json. Пул і таймаути — параметрами рядка підключення.</summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    [Required] public string ConnectionString { get; set; } = "";
    [Required] public string DatabaseName { get; set; } = "";
}
