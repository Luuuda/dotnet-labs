using MongoDB.Bson;

namespace Reviews.Infrastructure.Persistence.ReadModels;

// Плоскі read-моделі для сторони читання (CQRS): ті самі колекції, але без доменної поведінки
// і Value Objects — їх зручно проєктувати й одразу перетворювати на DTO.

public sealed class AuthorDocument
{
    public string UserId { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class CommentDocument
{
    public ObjectId Id { get; set; }
    public AuthorDocument Author { get; set; } = new();
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class ReviewDocument
{
    public ObjectId Id { get; set; }
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = "";
    public AuthorDocument Author { get; set; } = new();
    public int Rating { get; set; }
    public string? Text { get; set; }
    public Dictionary<string, int>? Aspects { get; set; }
    public bool VerifiedViewer { get; set; }
    public List<CommentDocument>? Comments { get; set; }
    public int CommentsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int Version { get; set; }
}

public sealed class DiscussionDocument
{
    public ObjectId Id { get; set; }
    public int MovieId { get; set; }
    public string MovieTitle { get; set; } = "";
    public string Title { get; set; } = "";
    public AuthorDocument CreatedBy { get; set; } = new();
    public string Status { get; set; } = "open";
    public List<string>? Tags { get; set; }
    public int PostsCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastActivityAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string? ClosedReason { get; set; }
    public int Version { get; set; }
}
