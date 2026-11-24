namespace Reviews.Application.Common.Models;

public sealed record AuthorDto(string UserId, string DisplayName);

/// <summary>Відгук у списку (без масиву коментарів — лише лічильник).</summary>
public sealed record ReviewDto
{
    public string Id { get; init; } = "";
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public AuthorDto Author { get; init; } = null!;
    public int Rating { get; init; }
    public string? Text { get; init; }
    public IReadOnlyDictionary<string, int>? Aspects { get; init; }
    public bool VerifiedViewer { get; init; }
    public int CommentsCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int Version { get; init; }
}

/// <summary>Повний відгук із вкладеними коментарями.</summary>
public sealed record ReviewDetailsDto
{
    public string Id { get; init; } = "";
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public AuthorDto Author { get; init; } = null!;
    public int Rating { get; init; }
    public string? Text { get; init; }
    public IReadOnlyDictionary<string, int>? Aspects { get; init; }
    public bool VerifiedViewer { get; init; }
    public int CommentsCount { get; init; }
    public IReadOnlyList<CommentDto> Comments { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int Version { get; init; }
}

public sealed record CommentDto
{
    public string Id { get; init; } = "";
    public AuthorDto Author { get; init; } = null!;
    public string Text { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}

public sealed record DiscussionDto
{
    public string Id { get; init; } = "";
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public string Title { get; init; } = "";
    public AuthorDto CreatedBy { get; init; } = null!;
    public string Status { get; init; } = "";
    public IReadOnlyList<string> Tags { get; init; } = [];
    public int PostsCount { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime LastActivityAt { get; init; }
    public DateTime? ClosedAt { get; init; }
    public string? ClosedReason { get; init; }
    public int Version { get; init; }
}

/// <summary>Агрегований рейтинг фільму (aggregation pipeline: $match → $group).</summary>
public sealed record MovieRatingDto(
    int MovieId, string MovieTitle, double Average, int Count,
    IReadOnlyDictionary<string, int> Distribution, int VerifiedCount);
