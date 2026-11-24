using RabbitMQ.Client;

namespace Platform.Messaging;

/// <summary>
/// Декларативна топологія RabbitMQ (Лаб. 08, Частина 1.3). Оголошується в коді під час старту застосунку:
/// повторне оголошення з тими самими параметрами безпечне, тому кожен сервіс оголошує те, що йому потрібно,
/// і порядок запуску сервісів не важливий. Параметри черг вручну в Management UI не змінюються.
/// <code>
///                         ┌─ binding screening.* / movie.* ─► booking.catalog-sync ──(nack / ліміт повторів)─┐
/// catalog-events (topic) ─┤                                                                                    ├─► catalog-events.dlx (direct)
///                         └─ binding movie.*  ────────────────► reviews.catalog-sync ──(nack / ліміт повторів)─┘      │ routing key = ім'я черги
///                                                                          booking.catalog-sync.dlq ◄───────────────┤
///                                                                          reviews.catalog-sync.dlq ◄───────────────┘
/// </code>
/// DLX — direct, а не fanout: кожна черга передає в DLX своє ім'я як x-dead-letter-routing-key, тож відхилене
/// повідомлення потрапляє лише в DLQ «своєї» черги, а не в DLQ усіх споживачів.
/// </summary>
public static class EventTopology
{
    /// <summary>Суфікс dead letter exchange: catalog-events → catalog-events.dlx.</summary>
    public const string DeadLetterExchangeSuffix = ".dlx";
    /// <summary>Суфікс черги необроблених повідомлень: booking.catalog-sync → booking.catalog-sync.dlq.</summary>
    public const string DeadLetterQueueSuffix = ".dlq";

    /// <summary>Durable topic exchange — гнучка маршрутизація за routing key (* — одне слово, # — будь-яка кількість).</summary>
    public static Task DeclareExchangeAsync(IChannel channel, string exchange, CancellationToken ct) =>
        channel.ExchangeDeclareAsync(exchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

    /// <summary>
    /// Черга сервісу-споживача з bindings до exchange, dead letter exchange і DLQ:
    /// <list type="number">
    /// <item>topic exchange <paramref name="exchange"/> (durable);</item>
    /// <item>dead letter exchange «&lt;exchange&gt;.dlx» (durable, direct);</item>
    /// <item>durable-черга <paramref name="queue"/> з аргументами x-dead-letter-exchange і x-dead-letter-routing-key;</item>
    /// <item>bindings черги до exchange за шаблонами <paramref name="bindingKeys"/>;</item>
    /// <item>durable-черга «&lt;queue&gt;.dlq», прив'язана до DLX ключем = ім'я черги.</item>
    /// </list>
    /// </summary>
    public static async Task DeclareConsumerQueueAsync(IChannel channel, string exchange, string queue,
        IReadOnlyCollection<string> bindingKeys, CancellationToken ct)
    {
        var deadLetterExchange = exchange + DeadLetterExchangeSuffix;
        var deadLetterQueue = queue + DeadLetterQueueSuffix;

        await DeclareExchangeAsync(channel, exchange, ct);
        await channel.ExchangeDeclareAsync(deadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = deadLetterExchange,     // nack без requeue → DLX
                ["x-dead-letter-routing-key"] = queue                // → лише DLQ цієї черги
            }, cancellationToken: ct);
        foreach (var key in bindingKeys)
            await channel.QueueBindAsync(queue, exchange, routingKey: key, cancellationToken: ct);

        await channel.QueueDeclareAsync(deadLetterQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: ct);
        await channel.QueueBindAsync(deadLetterQueue, deadLetterExchange, routingKey: queue, cancellationToken: ct);
    }
}
