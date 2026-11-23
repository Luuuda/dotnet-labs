using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;
using Reviews.Domain.ValueObjects;

namespace Reviews.Infrastructure.Persistence.Serialization;

/// <summary>Value Object Rating зберігається як звичайне число: <c>rating: 5</c> (структура з Лаб. 01).</summary>
public sealed class RatingSerializer : SerializerBase<Rating>
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Rating value)
        => context.Writer.WriteInt32(value.Value);

    public override Rating Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        var value = reader.CurrentBsonType switch
        {
            BsonType.Int32 => reader.ReadInt32(),
            BsonType.Int64 => (int)reader.ReadInt64(),
            BsonType.Double => (int)reader.ReadDouble(),
            var t => throw new FormatException($"rating: очікувалось число, отримано {t}")
        };
        return new Rating(value);                      // створення через конструктор — з перевіркою інваріанту
    }
}

/// <summary>Value Object ReviewAspects ↔ вкладений документ <c>aspects: { story: 5, acting: 4 }</c>.</summary>
public sealed class ReviewAspectsSerializer : SerializerBase<ReviewAspects>
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, ReviewAspects value)
    {
        var writer = context.Writer;
        writer.WriteStartDocument();
        foreach (var (name, score) in value.Scores)
        {
            writer.WriteName(name);
            writer.WriteInt32(score);
        }
        writer.WriteEndDocument();
    }

    public override ReviewAspects Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var document = BsonDocumentSerializer.Instance.Deserialize(context);
        var scores = document.Elements.ToDictionary(e => e.Name, e => e.Value.ToInt32());
        return new ReviewAspects(scores);                // створення через конструктор — з валідацією
    }
}

/// <summary>Enum ↔ рядок у нижньому регістрі ("open" / "closed"), як у схемі Лаб. 01.</summary>
public sealed class LowercaseEnumSerializer<TEnum> : SerializerBase<TEnum> where TEnum : struct, Enum
{
    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, TEnum value)
        => context.Writer.WriteString(value.ToString().ToLowerInvariant());

    public override TEnum Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var text = context.Reader.ReadString();
        return Enum.TryParse<TEnum>(text, ignoreCase: true, out var value)
            ? value
            : throw new FormatException($"Невідоме значення '{text}' для {typeof(TEnum).Name}");
    }
}
