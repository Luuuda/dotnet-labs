namespace Reviews.Application.Caching;

/// <summary>Ключі кешу сервісу відгуків; префікс «reviews:» ізолює їх у спільному Redis.</summary>
public static class CacheKeys
{
    public const string Prefix = "reviews:";

    /// <summary>Рейтинг фільму (aggregation pipeline по всіх відгуках — найдорожчий запит сервісу).</summary>
    public static string MovieRating(int movieId) => $"{Prefix}movie:{movieId}:rating";

    /// <summary>Відгук з коментарями.</summary>
    public static string Review(string id) => $"{Prefix}review:{id}";

    /// <summary>Версія сторінок списку відгуків одного фільму (змінюється при будь-якому записі про цей фільм).</summary>
    public static string ListVersion(int movieId) => $"{Prefix}movie:{movieId}:list:version";

    public static string List(int movieId, string version, string queryHash) => $"{Prefix}movie:{movieId}:list:v{version}:{queryHash}";

    public static string Hash(object query)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(query, query.GetType());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(json), 0, 8).ToLowerInvariant();
    }
}
