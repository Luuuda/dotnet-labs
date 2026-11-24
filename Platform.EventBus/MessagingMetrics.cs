using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Platform.EventBus;

/// <summary>
/// Метрики обміну повідомленнями (Meter «Platform.Messaging», підключено в ServiceDefaults через AddMeter):
/// скільки подій опубліковано і чим закінчилась обробка (ack / retry / dead_letter / duplicate), час обробки.
/// </summary>
public static class MessagingMetrics
{
    public const string MeterName = "Platform.Messaging";
    private static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> Published =
        Meter.CreateCounter<long>("messaging.events.published", "{event}", "Опубліковані інтеграційні події");
    public static readonly Counter<long> Consumed =
        Meter.CreateCounter<long>("messaging.events.consumed", "{event}",
            "Отримані повідомлення за результатом: processed, retry, dead_letter");
    public static readonly Histogram<double> ProcessingDuration =
        Meter.CreateHistogram<double>("messaging.process.duration", "ms", "Час обробки повідомлення споживачем");

    /// <summary>
    /// Глибина DLQ (gauge, як ObservableGauge з Лаб. 07): асинхронні збої «тихі» — клієнта, який побачив би помилку,
    /// немає, тому кількість повідомлень у &lt;черга&gt;.dlq видно в Dashboard і на неї можна ставити сповіщення.
    /// Значення оновлює споживач (IntegrationEventConsumer.MonitorDeadLetterQueueAsync) раз на 15 с.
    /// </summary>
    private static readonly ConcurrentDictionary<string, long> DeadLetterDepth = new();

    public static readonly ObservableGauge<long> DeadLetterGauge = Meter.CreateObservableGauge(
        "messaging.dlq.depth",
        () => DeadLetterDepth.Select(kv => new Measurement<long>(kv.Value, new KeyValuePair<string, object?>("messaging.destination.name", kv.Key))),
        "{message}", "Кількість повідомлень у Dead Letter Queue");

    public static void SetDeadLetterDepth(string deadLetterQueue, long depth) => DeadLetterDepth[deadLetterQueue] = depth;
}
