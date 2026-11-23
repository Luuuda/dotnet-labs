using MongoDB.Bson;
using Reviews.Domain.Common;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>
/// Відгук глядача на фільм — корінь агрегату й документ колекції <c>reviews</c>.
/// Коментарі вкладені в документ; стан змінюється лише методами, що перевіряють бізнес-правила.
/// </summary>
public sealed class Review : BaseEntity
{
    public const int MaxTextLength = 5000;
    public const int MaxTitleLength = 200;
    public const int MaxComments = 50;                 // обмежене вкладення, як maxItems у схемі Лаб. 01

    private List<Comment> _comments = [];
    private Dictionary<string, object>? _extraElements = null; // гнучка схема: поля, яких немає в моделі (spoiler, tags…), не губляться

    /// <summary>Movies.Id з сервісу Каталогу — посилання без зовнішнього ключа.</summary>
    public int MovieId { get; private set; }

    /// <summary>Назва фільму, денормалізована з Каталогу для читання без міжсервісного запиту.</summary>
    public string MovieTitle { get; private set; } = "";

    public Author Author { get; private set; } = null!;
    public Rating Rating { get; private set; } = null!;
    public string? Text { get; private set; }
    public ReviewAspects? Aspects { get; private set; }

    /// <summary>Глядач мав виданий квиток на цей фільм (подія з Бронювання).</summary>
    public bool VerifiedViewer { get; private set; }

    public IReadOnlyCollection<Comment> Comments => _comments.AsReadOnly();

    /// <summary>Лічильник для швидкого читання без завантаження масиву (як commentsCount у Лаб. 01).</summary>
    public int CommentsCount { get; private set; }

    public IReadOnlyDictionary<string, object> AdditionalFields
        => _extraElements ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();

    private Review() { }                                 // для десеріалізації

    public Review(int movieId, string movieTitle, Author author, Rating rating,
                  string? text, ReviewAspects? aspects = null, bool verifiedViewer = false)
    {
        if (movieId <= 0)
            throw new DomainException("Ідентифікатор фільму має бути додатним");
        if (string.IsNullOrWhiteSpace(movieTitle))
            throw new DomainException("Назва фільму обов'язкова");
        if (movieTitle.Trim().Length > MaxTitleLength)
            throw new DomainException($"Назва фільму довша за {MaxTitleLength} символів");

        MovieId = movieId;
        MovieTitle = movieTitle.Trim();
        Author = author ?? throw new DomainException("Автор обов'язковий");
        Rating = rating ?? throw new DomainException("Оцінка обов'язкова");
        Text = NormalizeText(text);
        Aspects = aspects;
        VerifiedViewer = verifiedViewer;
    }

    /// <summary>Зручний конструктор: оцінка числом (Rating = 10 → DomainException ще до звернення до БД).</summary>
    public Review(int movieId, string movieTitle, Author author, int rating, string? text)
        : this(movieId, movieTitle, author, new Rating(rating), text) { }

    /// <summary>Редагування відгуку автором: оцінка, текст і аспекти.</summary>
    public void Edit(Rating rating, string? text, ReviewAspects? aspects)
    {
        Rating = rating ?? throw new DomainException("Оцінка обов'язкова");
        Text = NormalizeText(text);
        Aspects = aspects;
        Touch();
    }

    /// <summary>Перейменування фільму в Каталозі (подія MovieRenamed): оновлюємо денормалізовану копію.</summary>
    public void RenameMovie(string movieTitle)
    {
        if (string.IsNullOrWhiteSpace(movieTitle))
            throw new DomainException("Назва фільму обов'язкова");
        MovieTitle = movieTitle.Trim();
        Touch();
    }

    public void MarkVerifiedViewer()
    {
        if (VerifiedViewer) return;
        VerifiedViewer = true;
        Touch();
    }

    public Comment AddComment(Author author, string text)
    {
        if (_comments.Count >= MaxComments)
            throw new DomainException($"До відгуку можна додати не більше {MaxComments} коментарів");

        var comment = new Comment(author, text, DateTime.UtcNow);
        _comments.Add(comment);
        CommentsCount = _comments.Count;
        Touch();
        return comment;
    }

    public bool HasComment(ObjectId commentId) => _comments.Any(c => c.Id == commentId);

    public void RemoveComment(ObjectId commentId)
    {
        var comment = _comments.FirstOrDefault(c => c.Id == commentId)
            ?? throw new DomainException($"Коментар {commentId} не належить цьому відгуку");
        _comments.Remove(comment);
        CommentsCount = _comments.Count;
        Touch();
    }

    private static string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var trimmed = text.Trim();
        if (trimmed.Length > MaxTextLength)
            throw new DomainException($"Текст відгуку довший за {MaxTextLength} символів");
        return trimmed;
    }
}
