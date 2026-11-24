namespace Platform.Messaging;

/// <summary>
/// Обробник одного типу інтеграційної події. Споживач створює для кожного повідомлення окремий DI-scope
/// і бере обробник із нього (Scoped-залежності: Unit of Work, з'єднання з БД).
/// Обробник має бути ідемпотентним: те саме повідомлення може прийти повторно (at-least-once delivery),
/// тому EventId фіксується як оброблений в одній транзакції зі зміною даних.
/// </summary>
public interface IIntegrationEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event, CancellationToken ct);
}

/// <summary>
/// Постійна помилка обробки: повтор нічого не змінить (невалідні дані події, несумісна версія схеми).
/// Повідомлення одразу відхиляється (nack без requeue) і потрапляє в DLQ — без повторів.
/// </summary>
public sealed class PermanentMessageException(string message, Exception? inner = null) : Exception(message, inner);
