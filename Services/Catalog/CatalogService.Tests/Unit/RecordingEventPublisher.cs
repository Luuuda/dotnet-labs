using Platform.Messaging;

namespace CatalogService.Tests.Unit;

/// <summary>Лаб. 08: IEventPublisher для unit-тестів — запам'ятовує опубліковані події замість RabbitMQ.</summary>
public sealed class RecordingEventPublisher : IEventPublisher
{
    public sealed record Message(IntegrationEvent Event, string Exchange, string RoutingKey);

    public List<Message> Published { get; } = [];
    public Action<IntegrationEvent>? OnPublish { get; set; }

    public Task PublishAsync<T>(T @event, string exchange, string routingKey, CancellationToken ct = default)
        where T : IntegrationEvent
    {
        OnPublish?.Invoke(@event);
        Published.Add(new Message(@event, exchange, routingKey));
        return Task.CompletedTask;
    }
}
