namespace Reviews.WebApi.Contracts;

// Тіла HTTP-запитів, де частина даних приходить з маршруту чи заголовків (id, If-Match).
// Перетворюються на команди профілем AutoMapper (RequestMappingProfile).

/// <summary>Редагування відгуку. Version — альтернатива заголовку If-Match.</summary>
public sealed record UpdateReviewRequest(int Rating, string? Text, IReadOnlyDictionary<string, int>? Aspects, int? Version);

public sealed record AddCommentRequest(string UserId, string DisplayName, string Text);

public sealed record CloseDiscussionRequest(string? Reason, int? Version);

public sealed record RenameMovieRequest(string MovieTitle);
