using System.Diagnostics;
using System.Diagnostics.Metrics;
using BookingService.Domain.Models;

namespace BookingService.Api.Metrics;

/// <summary>
/// Лаб. 07: бізнес-метрики сервісу бронювання (OpenTelemetry Meter API). Meter «BookingService.Api» створюється через
/// IMeterFactory і підключений в Platform.ServiceDefaults (BusinessMeters) → експорт OTLP в Aspire Dashboard.
/// Клас реєструється як Singleton.
/// <para>Інструменти:</para>
/// <list type="bullet">
/// <item>Counter bookings.created {booking} — теги booking.status, booking.channel;</item>
/// <item>Counter bookings.confirmed / bookings.paid (тег payment.provider) / bookings.issued / bookings.cancelled;</item>
/// <item>Histogram bookings.operation.duration (ms) — теги booking.operation, operation.result (success | failure),
/// записується для ВСІХ виконань, включно з невдалими;</item>
/// <item>ObservableGauge bookings.current {booking} — поточна кількість за статусом (тег booking.status);</item>
/// <item>ObservableGauge bookings.awaiting_validation / bookings.awaiting_payment — знімок черг процесу.</item>
/// </list>
/// Теги мають малу кількість значень (6 статусів, 4 провайдери, 3 канали, 7 операцій). Id бронювань і клієнтів у теги
/// НЕ потрапляють: кожна унікальна комбінація тегів — окремий часовий ряд.
/// </summary>
public sealed class BookingMetrics
{
    public const string MeterName = "BookingService.Api";

    private readonly Counter<long> _created;
    private readonly Counter<long> _confirmed;
    private readonly Counter<long> _paid;
    private readonly Counter<long> _issued;
    private readonly Counter<long> _cancelled;
    private readonly Histogram<double> _operationDuration;

    // Знімок для gauge: оновлює BookingMetricsSnapshotService, callback лише читає (без запитів до БД під час збору)
    private volatile IReadOnlyDictionary<string, int> _byStatus = new Dictionary<string, int>();

    public BookingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _created = meter.CreateCounter<long>("bookings.created", "{booking}", "Створені бронювання");
        _confirmed = meter.CreateCounter<long>("bookings.confirmed", "{booking}", "Підтверджені місця (SeatsConfirmed)");
        _paid = meter.CreateCounter<long>("bookings.paid", "{booking}", "Оплачені бронювання (без повторних запитів з тим самим ключем)");
        _issued = meter.CreateCounter<long>("bookings.issued", "{booking}", "Видані квитки");
        _cancelled = meter.CreateCounter<long>("bookings.cancelled", "{booking}", "Скасовані бронювання");
        _operationDuration = meter.CreateHistogram<double>("bookings.operation.duration", "ms",
            "Тривалість бізнес-операцій бронювання (успішних і невдалих)");

        meter.CreateObservableGauge("bookings.current", ObserveByStatus, "{booking}",
            "Поточна кількість бронювань за статусом (знімок раз на 15 с)");
        meter.CreateObservableGauge("bookings.awaiting_validation",
            () => _byStatus.GetValueOrDefault(BookingStatus.AwaitingValidation), "{booking}",
            "Бронювання, що чекають підтвердження місць");
        meter.CreateObservableGauge("bookings.awaiting_payment",
            () => _byStatus.GetValueOrDefault(BookingStatus.SeatsConfirmed), "{booking}",
            "Місця підтверджено, оплати ще немає");
    }

    public void BookingCreated(string status, string? channel) =>
        _created.Add(1, new KeyValuePair<string, object?>("booking.status", status),
                        new KeyValuePair<string, object?>("booking.channel", channel ?? "Unknown"));

    public void BookingConfirmed() => _confirmed.Add(1);

    public void BookingPaid(string provider) =>
        _paid.Add(1, new KeyValuePair<string, object?>("payment.provider", provider));

    public void BookingIssued() => _issued.Add(1);

    public void BookingCancelled() => _cancelled.Add(1);

    public void RecordDuration(string operation, bool success, double elapsedMs) =>
        _operationDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("booking.operation", operation),
            new KeyValuePair<string, object?>("operation.result", success ? "success" : "failure"));

    /// <summary>
    /// Виконує операцію й записує її тривалість в гістограму в будь-якому разі — і для успіху, і для винятку
    /// (409 недопустимий перехід, 404, 400): інакше розподіл затримок показував би лише «щасливі» виконання.
    /// </summary>
    public async Task<T> MeasureAsync<T>(string operation, Func<Task<T>> action)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var result = await action();
            RecordDuration(operation, success: true, sw.Elapsed.TotalMilliseconds);
            return result;
        }
        catch
        {
            RecordDuration(operation, success: false, sw.Elapsed.TotalMilliseconds);
            throw;
        }
    }

    public async Task MeasureAsync(string operation, Func<Task> action) =>
        await MeasureAsync<bool>(operation, async () => { await action(); return true; });

    public void UpdateSnapshot(IReadOnlyDictionary<string, int> byStatus) => _byStatus = byStatus;

    private IEnumerable<Measurement<int>> ObserveByStatus() =>
        _byStatus.Select(kv => new Measurement<int>(kv.Value, new KeyValuePair<string, object?>("booking.status", kv.Key)));
}
