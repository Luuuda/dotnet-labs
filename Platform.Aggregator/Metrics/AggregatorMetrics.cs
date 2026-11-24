using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Platform.Aggregator.Metrics;

/// <summary>
/// Лаб. 07: бізнес-метрики Aggregator. Meter «Platform.Aggregator» створюється через IMeterFactory і підключений
/// в ServiceDefaults (BusinessMeters). Клас — Singleton.
/// <para>Теги лише з малою кількістю значень:</para>
/// <list type="bullet">
/// <item>aggregator.endpoint — booking_card | movie_card | poster_board;</item>
/// <item>aggregator.transport — grpc | http;</item>
/// <item>aggregator.result — complete | partial | not_found | failed;</item>
/// <item>cache.result — hit | miss.</item>
/// </list>
/// Ідентифікатори бронювань і фільмів у теги не потрапляють: кожне значення тегу — окремий часовий ряд.
/// </summary>
public sealed class AggregatorMetrics
{
    public const string MeterName = "Platform.Aggregator";

    private readonly Counter<long> _responses;
    private readonly Counter<long> _partialResponses;
    private readonly Histogram<double> _duration;

    public AggregatorMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _responses = meter.CreateCounter<long>("aggregator.responses", "{response}",
            "Агреговані відповіді за результатом (complete / partial / not_found / failed)");
        _partialResponses = meter.CreateCounter<long>("aggregator.partial_responses", "{response}",
            "Часткові відповіді: залежний сервіс недоступний, дані без нього (graceful degradation)");
        _duration = meter.CreateHistogram<double>("aggregator.request.duration", "ms",
            "Тривалість агрегації — для всіх запитів, включно з невдалими");
    }

    public void Record(string endpoint, string transport, string result, bool fromCache, double elapsedMs)
    {
        var tags = new TagList
        {
            { "aggregator.endpoint", endpoint },
            { "aggregator.transport", transport },
            { "aggregator.result", result },
            { "cache.result", fromCache ? "hit" : "miss" }
        };
        _responses.Add(1, tags);
        _duration.Record(elapsedMs, tags);
        if (result == "partial")
            _partialResponses.Add(1, new KeyValuePair<string, object?>("aggregator.endpoint", endpoint));
    }
}
