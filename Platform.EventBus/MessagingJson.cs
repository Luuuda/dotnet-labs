using System.Text.Json;
using System.Text.Json.Serialization;

namespace Platform.EventBus;

/// <summary>
/// Єдиний формат тіла повідомлень для publisher і consumer: System.Text.Json, camelCase, enum — рядками,
/// дати — ISO 8601 (UTC). JSON можуть читати сервіси на будь-якій мові, не лише .NET.
/// Невідомі поля ігноруються — так новіша версія події з додатковими полями сумісна зі старим споживачем.
/// </summary>
public static class MessagingJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip
    };
}
