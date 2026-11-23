using System.Diagnostics;

namespace Reviews.Application.Common.Behaviors;

/// <summary>
/// Власні spans сервісу відгуків (Лаб. 05): по одному на кожну команду/запит MediatR (LoggingBehavior)
/// і на ключові бізнес-операції. Platform.ServiceDefaults підписується на джерела «Cinema.*».
/// </summary>
public static class ReviewsTelemetry
{
    public static readonly ActivitySource Source = new("Cinema.Reviews");
}
