using OpenTelemetry;

namespace Platform.Messaging;

/// <summary>
/// Поточний CorrelationId (Лаб. 05) без залежності від HttpContext: middleware CorrelationId кладе його в
/// OpenTelemetry Baggage, а споживач відновлює baggage із заголовків повідомлення. Тому той самий ідентифікатор
/// доступний і в HTTP-запиті (BLL Каталогу під час публікації), і в обробнику події в іншому сервісі.
/// </summary>
public static class CorrelationContext
{
    public const string BaggageKey = "correlation.id";

    public static string? Current => Baggage.GetBaggage(BaggageKey);

    public static void Set(string? correlationId)
    {
        if (!string.IsNullOrWhiteSpace(correlationId))
            Baggage.SetBaggage(BaggageKey, correlationId);
    }
}
