using MongoDB.Bson;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Коментар до відгуку — ВКЛАДЕНИЙ документ у масиві <c>comments</c> (embedding з Лаб. 01).
/// Має власний <c>_id</c>, тому його можна видалити окремо, але живе лише всередині Review.
/// </summary>
public sealed class Comment
{
    public const int MaxTextLength = 1000;

    public ObjectId Id { get; private set; }
    public Author Author { get; private set; } = null!;
    public string Text { get; private set; } = "";
    public DateTime CreatedAt { get; private set; }

    private Comment() { }                                  // для десеріалізації

    internal Comment(Author author, string text, DateTime createdAt)
    {
        ArgumentNullException.ThrowIfNull(author);
        if (string.IsNullOrWhiteSpace(text))
            throw new DomainException("Текст коментаря обов'язковий");
        if (text.Trim().Length > MaxTextLength)
            throw new DomainException($"Коментар довший за {MaxTextLength} символів");

        Id = ObjectId.GenerateNewId();
        Author = author;
        Text = text.Trim();
        CreatedAt = createdAt;
    }
}
