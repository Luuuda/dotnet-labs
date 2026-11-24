using System.Diagnostics;
using System.Text;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Platform.EventBus;

/// <summary>
/// Передача контексту трасування через брокер (Лаб. 08, Частина 4). Без неї обробка події в іншому сервісі
/// виглядала б у Dashboard окремим trace, не пов'язаним з HTTP-запитом, що її спричинив.
/// <list type="bullet">
/// <item>Під час публікації: span типу Producer «&lt;exchange&gt; publish» + запис його контексту в заголовки
/// повідомлення у форматі W3C (traceparent, tracestate, baggage) через пропагатор OpenTelemetry;</item>
/// <item>У споживачі: контекст читається із заголовків (значення приходять як byte[]), і span типу Consumer
/// «&lt;черга&gt; process» стає нащадком span публікації — так утворюється наскрізний trace
/// HTTP → Publish → Consume → DB.</item>
/// </list>
/// ActivitySource «Platform.Messaging» (назва з методички) підключено в Platform.ServiceDefaults: .AddSource("Platform.Messaging").
/// </summary>
public static class TracePropagation
{
    public const string SourceName = "Platform.Messaging";
    public static readonly ActivitySource Source = new(SourceName);

    private static TextMapPropagator Propagator => Propagators.DefaultTextMapPropagator;

    /// <summary>Span публікації (ActivityKind.Producer) з тегами messaging.* за семантичними конвенціями OpenTelemetry.</summary>
    public static Activity? StartPublishActivity(string exchange, string routingKey, string eventType, Guid eventId)
    {
        var activity = Source.StartActivity($"{exchange} publish", ActivityKind.Producer);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "publish");
        activity?.SetTag("messaging.destination.name", exchange);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", routingKey);
        activity?.SetTag("messaging.message.id", eventId.ToString());
        activity?.SetTag("messaging.message.type", eventType);
        return activity;
    }

    /// <summary>Записує поточний контекст трасування й baggage у заголовки повідомлення (W3C traceparent).</summary>
    public static void Inject(IBasicProperties props)
    {
        if (Activity.Current is not { } activity) return;
        props.Headers ??= new Dictionary<string, object?>();
        Propagator.Inject(new PropagationContext(activity.Context, Baggage.Current), props.Headers,
            static (headers, key, value) => headers[key] = value);
    }

    /// <summary>
    /// Відновлює контекст із заголовків і починає span обробки (ActivityKind.Consumer) — нащадок span публікації.
    /// Повертає також відновлений baggage (у ньому CorrelationId).
    /// </summary>
    public static Activity? StartConsumerActivity(BasicDeliverEventArgs ea, string queue)
    {
        var parent = Propagator.Extract(default, ea.BasicProperties.Headers, static (headers, key) =>
            headers is not null && headers.TryGetValue(key, out var value) ? value switch
            {
                byte[] bytes => [Encoding.UTF8.GetString(bytes)],
                string text => [text],
                _ => []
            } : []);
        Baggage.Current = parent.Baggage;

        var activity = Source.StartActivity($"{queue} process", ActivityKind.Consumer, parent.ActivityContext);
        activity?.SetTag("messaging.system", "rabbitmq");
        activity?.SetTag("messaging.operation.type", "process");
        activity?.SetTag("messaging.destination.name", queue);
        activity?.SetTag("messaging.rabbitmq.destination.routing_key", ea.RoutingKey);
        activity?.SetTag("messaging.message.id", ea.BasicProperties.MessageId);
        activity?.SetTag("messaging.message.type", ea.BasicProperties.Type);
        activity?.SetTag("messaging.rabbitmq.message.delivery_tag", (long)ea.DeliveryTag);
        return activity;
    }
}
