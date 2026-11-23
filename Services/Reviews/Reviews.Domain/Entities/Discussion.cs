using Reviews.Domain.Common;
using Reviews.Domain.Enums;
using Reviews.Domain.Exceptions;
using Reviews.Domain.ValueObjects;

namespace Reviews.Domain.Entities;

/// <summary>Тема обговорення фільму — документ колекції <c>discussions</c>.</summary>
public sealed class Discussion : BaseEntity
{
    public const int MinTitleLength = 3;
    public const int MaxTitleLength = 200;
    public const int MaxTags = 10;

    private List<string> _tags = [];
    private Dictionary<string, object>? _extraElements = null;  // гнучка схема: poll, lastPosts, isPinned…

    public int MovieId { get; private set; }
    public string MovieTitle { get; private set; } = "";
    public string Title { get; private set; } = "";
    public Author CreatedBy { get; private set; } = null!;
    public DiscussionStatus Status { get; private set; }
    public IReadOnlyCollection<string> Tags => _tags.AsReadOnly();
    public int PostsCount { get; private set; }
    public DateTime LastActivityAt { get; private set; }
    public DateTime? ClosedAt { get; private set; }
    public string? ClosedReason { get; private set; }

    public IReadOnlyDictionary<string, object> AdditionalFields
        => _extraElements ?? (IReadOnlyDictionary<string, object>)new Dictionary<string, object>();

    private Discussion() { }

    public Discussion(int movieId, string movieTitle, string title, Author createdBy, IEnumerable<string>? tags = null)
    {
        if (movieId <= 0) throw new DomainException("Ідентифікатор фільму має бути додатним");
        if (string.IsNullOrWhiteSpace(movieTitle)) throw new DomainException("Назва фільму обов'язкова");

        MovieId = movieId;
        MovieTitle = movieTitle.Trim();
        Title = ValidateTitle(title);
        CreatedBy = createdBy ?? throw new DomainException("Автор теми обов'язковий");
        Status = DiscussionStatus.Open;
        LastActivityAt = CreatedAt;
        foreach (var tag in tags ?? []) AddTag(tag);
    }

    public void Rename(string title)
    {
        EnsureOpen();
        Title = ValidateTitle(title);
        Touch();
    }

    public void AddTag(string tag)
    {
        var normalized = tag?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > 30)
            throw new DomainException("Тег має містити від 1 до 30 символів");
        if (_tags.Contains(normalized)) return;
        if (_tags.Count >= MaxTags) throw new DomainException($"Не більше {MaxTags} тегів");
        _tags.Add(normalized);
    }

    /// <summary>Новий пост у темі (пости зберігаються в окремій колекції discussionPosts — referencing).</summary>
    public void RegisterPost(DateTime postedAt)
    {
        EnsureOpen();
        PostsCount++;
        LastActivityAt = postedAt;
        Touch();
    }

    public void Close(string? reason)
    {
        if (Status == DiscussionStatus.Closed)
            throw new DomainException("Тему вже закрито");
        Status = DiscussionStatus.Closed;
        ClosedAt = DateTime.UtcNow;
        ClosedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        Touch();
    }

    public void Reopen()
    {
        if (Status == DiscussionStatus.Open)
            throw new DomainException("Тема й так відкрита");
        Status = DiscussionStatus.Open;
        ClosedAt = null;
        ClosedReason = null;
        LastActivityAt = DateTime.UtcNow;
        Touch();
    }

    private void EnsureOpen()
    {
        if (Status == DiscussionStatus.Closed)
            throw new DomainException("Тему закрито — зміни неможливі");
    }

    private static string ValidateTitle(string title)
    {
        var trimmed = title?.Trim() ?? "";
        if (trimmed.Length is < MinTitleLength or > MaxTitleLength)
            throw new DomainException($"Назва теми має містити від {MinTitleLength} до {MaxTitleLength} символів");
        return trimmed;
    }
}
