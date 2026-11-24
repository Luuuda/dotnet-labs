using System.Text;
using System.Text.Json;
using BookingService.Api.IntegrationEvents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Platform.EventBus;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace BookingService.Tests.Unit;

/// <summary>
/// Лаб. 08: рішення споживача щодо повідомлення — ack після успіху, nack(requeue: false) → DLQ для некоректних
/// повідомлень, повтор із x-retry-count для тимчасових помилок і DLQ після вичерпання повторів.
/// </summary>
public sealed class IntegrationEventConsumerTests
{
    private const string Queue = "booking.catalog-sync";
    private readonly Mock<IChannel> _channel = new();
    private readonly FakeHandler _handler = new();

    private sealed class FakeHandler : IIntegrationEventHandler<ScreeningCancelledEvent>
    {
        public int Calls { get; private set; }
        public Exception? Throw { get; set; }
        public Task HandleAsync(ScreeningCancelledEvent @event, CancellationToken ct)
        {
            Calls++;
            return Throw is null ? Task.CompletedTask : Task.FromException(Throw);
        }
    }

    private sealed class TestConsumer : IntegrationEventConsumer
    {
        public TestConsumer(IServiceProvider services) : base(services, NullLogger.Instance) => Handle<ScreeningCancelledEvent>();
        protected override TimeSpan RetryDelay(int attempt) => TimeSpan.Zero;
        protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.CompletedTask;
        public Task DeliverAsync(IChannel channel, BasicDeliverEventArgs ea) => HandleDeliveryAsync(channel, Queue, ea, CancellationToken.None);
    }

    private TestConsumer CreateConsumer()
    {
        var services = new ServiceCollection();
        services.AddScoped<IIntegrationEventHandler<ScreeningCancelledEvent>>(_ => _handler);
        return new TestConsumer(services.BuildServiceProvider());
    }

    private static BasicDeliverEventArgs Delivery(string type, byte[] body, int? retryCount = null, int schemaVersion = 1)
    {
        var headers = new Dictionary<string, object?>
        {
            [MessagingHeaders.CorrelationId] = Encoding.UTF8.GetBytes("corr-1"),
            [MessagingHeaders.SchemaVersion] = schemaVersion
        };
        if (retryCount is { } r) headers[MessagingHeaders.RetryCount] = r;
        var props = new BasicProperties { Type = type, MessageId = Guid.NewGuid().ToString(), Headers = headers };
        return new BasicDeliverEventArgs("ctag", deliveryTag: 7, redelivered: false, "catalog-events", "screening.cancelled",
            props, body);
    }

    private static byte[] ValidBody() => JsonSerializer.SerializeToUtf8Bytes(
        new ScreeningCancelledEvent(42, 1, "Довбуш", DateTime.UtcNow.AddDays(1)), MessagingJson.Options);

    private void VerifyAcked(Times times) =>
        _channel.Verify(c => c.BasicAckAsync(7, false, It.IsAny<CancellationToken>()), times);

    private void VerifyDeadLettered(Times times) =>
        _channel.Verify(c => c.BasicNackAsync(7, false, false, It.IsAny<CancellationToken>()), times);

    private void VerifyRepublished(Times times) =>
        _channel.Verify(c => c.BasicPublishAsync("", Queue, false, It.IsAny<BasicProperties>(),
            It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task Success_is_acked_after_handler()
    {
        await CreateConsumer().DeliverAsync(_channel.Object, Delivery(nameof(ScreeningCancelledEvent), ValidBody()));

        Assert.Equal(1, _handler.Calls);
        VerifyAcked(Times.Once());
        VerifyDeadLettered(Times.Never());
    }

    [Fact]
    public void Body_is_camelCase_json()
    {
        var json = Encoding.UTF8.GetString(ValidBody());
        Assert.Contains("\"screeningId\":42", json);
        Assert.Contains("\"eventId\":", json);
        Assert.Contains("\"occurredAt\":", json);
    }

    [Fact]
    public async Task Malformed_json_goes_to_dlq_without_retry()
    {
        await CreateConsumer().DeliverAsync(_channel.Object, Delivery(nameof(ScreeningCancelledEvent), "{ не json"u8.ToArray()));

        Assert.Equal(0, _handler.Calls);
        VerifyDeadLettered(Times.Once());
        VerifyRepublished(Times.Never());
        VerifyAcked(Times.Never());
    }

    [Fact]
    public async Task Unknown_type_goes_to_dlq()
    {
        await CreateConsumer().DeliverAsync(_channel.Object, Delivery("HallDemolishedEvent", "{}"u8.ToArray()));
        VerifyDeadLettered(Times.Once());
    }

    [Fact]
    public async Task Incompatible_schema_version_goes_to_dlq()
    {
        await CreateConsumer().DeliverAsync(_channel.Object, Delivery(nameof(ScreeningCancelledEvent), ValidBody(), schemaVersion: 2));
        Assert.Equal(0, _handler.Calls);
        VerifyDeadLettered(Times.Once());
    }

    [Fact]
    public async Task Transient_error_republishes_copy_with_incremented_retry_count_and_acks_original()
    {
        _handler.Throw = new TimeoutException("SQL Server не відповідає");
        BasicProperties? copy = null;
        _channel.Setup(c => c.BasicPublishAsync("", Queue, false, It.IsAny<BasicProperties>(),
                It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, bool _, BasicProperties p, ReadOnlyMemory<byte> _, CancellationToken _) => copy = p)
            .Returns(ValueTask.CompletedTask);

        await CreateConsumer().DeliverAsync(_channel.Object, Delivery(nameof(ScreeningCancelledEvent), ValidBody(), retryCount: 1));

        VerifyRepublished(Times.Once());
        VerifyAcked(Times.Once());
        VerifyDeadLettered(Times.Never());
        Assert.NotNull(copy);
        Assert.Equal(2, copy!.Headers![MessagingHeaders.RetryCount]);
        Assert.True(copy.Persistent);
    }

    [Fact]
    public async Task After_max_retries_message_goes_to_dlq()
    {
        _handler.Throw = new TimeoutException("SQL Server не відповідає");

        await CreateConsumer().DeliverAsync(_channel.Object,
            Delivery(nameof(ScreeningCancelledEvent), ValidBody(), retryCount: IntegrationEventConsumer.MaxRetries));

        VerifyDeadLettered(Times.Once());
        VerifyRepublished(Times.Never());
    }

    [Fact]
    public async Task Permanent_error_from_handler_goes_to_dlq_without_retry()
    {
        _handler.Throw = new PermanentMessageException("Некоректні дані");

        await CreateConsumer().DeliverAsync(_channel.Object, Delivery(nameof(ScreeningCancelledEvent), ValidBody()));

        VerifyDeadLettered(Times.Once());
        VerifyRepublished(Times.Never());
    }
}
