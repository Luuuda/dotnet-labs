using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Reviews.WebApi.Metrics;

/// <summary>
/// Лаб. 07: бізнес-метрики сервісу відгуків (OpenTelemetry Meter API). Meter «Reviews.WebApi» створюється через
/// IMeterFactory і підключений в Platform.ServiceDefaults (BusinessMeters). Клас реєструється як Singleton.
/// <list type="bullet">
/// <item>Counter reviews.created {review} — тег review.rating (1–5);</item>
/// <item>Counter reviews.updated / reviews.deleted / reviews.comments.added / discussions.created / discussions.closed;</item>
/// <item>Histogram reviews.operation.duration (ms) — теги reviews.operation, operation.result (success | failure);</item>
/// <item>ObservableGauge reviews.total і discussions.open — знімок раз на 30 с.</item>
/// </list>
/// Теги лише з малою кількістю значень: оцінка 1–5, назва операції, результат. Id користувачів, фільмів і документів
/// у теги не потрапляють.
/// </summary>
public sealed class ReviewsMetrics
{
    public const string MeterName = "Reviews.WebApi";

    private readonly Counter<long> _reviewsCreated;
    private readonly Counter<long> _reviewsUpdated;
    private readonly Counter<long> _reviewsDeleted;
    private readonly Counter<long> _commentsAdded;
    private readonly Counter<long> _discussionsCreated;
    private readonly Counter<long> _discussionsClosed;
    private readonly Histogram<double> _operationDuration;

    private long _totalReviews;
    private long _openDiscussions;

    public ReviewsMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _reviewsCreated = meter.CreateCounter<long>("reviews.created", "{review}", "Опубліковані відгуки");
        _reviewsUpdated = meter.CreateCounter<long>("reviews.updated", "{review}", "Відредаговані відгуки");
        _reviewsDeleted = meter.CreateCounter<long>("reviews.deleted", "{review}", "Видалені відгуки");
        _commentsAdded = meter.CreateCounter<long>("reviews.comments.added", "{comment}", "Додані коментарі до відгуків");
        _discussionsCreated = meter.CreateCounter<long>("discussions.created", "{discussion}", "Створені теми обговорень");
        _discussionsClosed = meter.CreateCounter<long>("discussions.closed", "{discussion}", "Закриті теми обговорень");
        _operationDuration = meter.CreateHistogram<double>("reviews.operation.duration", "ms",
            "Тривалість команд сервісу відгуків (успішних і невдалих)");

        meter.CreateObservableGauge("reviews.total", () => Interlocked.Read(ref _totalReviews),
            "{review}", "Усього відгуків у базі");
        meter.CreateObservableGauge("discussions.open", () => Interlocked.Read(ref _openDiscussions),
            "{discussion}", "Відкриті теми обговорень");
    }

    public void ReviewCreated(int rating) =>
        _reviewsCreated.Add(1, new KeyValuePair<string, object?>("review.rating", rating));

    public void ReviewUpdated() => _reviewsUpdated.Add(1);
    public void ReviewDeleted() => _reviewsDeleted.Add(1);
    public void CommentAdded() => _commentsAdded.Add(1);
    public void DiscussionCreated() => _discussionsCreated.Add(1);
    public void DiscussionClosed() => _discussionsClosed.Add(1);

    public void RecordDuration(string operation, bool success, double elapsedMs) =>
        _operationDuration.Record(elapsedMs,
            new KeyValuePair<string, object?>("reviews.operation", operation),
            new KeyValuePair<string, object?>("operation.result", success ? "success" : "failure"));

    /// <summary>Виконує команду й записує тривалість і для успіху, і для винятку (валідація, 404, 409).</summary>
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

    public void UpdateSnapshot(long totalReviews, long openDiscussions)
    {
        Interlocked.Exchange(ref _totalReviews, totalReviews);
        Interlocked.Exchange(ref _openDiscussions, openDiscussions);
    }
}
