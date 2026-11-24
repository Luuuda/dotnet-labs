namespace CatalogService.Bll.Caching;

/// <summary>
/// Схема ключів кешу каталогу. Префікс сервісу «catalog:» ізолює ключі від інших сервісів у спільному Redis;
/// за ключем одразу видно, що лежить у кеші, і його легко видалити при записі.
/// </summary>
public static class CacheKeys
{
    public const string Prefix = "catalog:";

    /// <summary>Картка фільму (деталі, жанри, найближчі сеанси).</summary>
    public static string Movie(int id) => $"{Prefix}movie:{id}";

    /// <summary>Сеанс розкладу.</summary>
    public static string Screening(long id) => $"{Prefix}screening:{id}";

    /// <summary>Довідник жанрів.</summary>
    public const string Genres = Prefix + "genres";

    /// <summary>Довідник залів.</summary>
    public const string Halls = Prefix + "halls";

    /// <summary>
    /// Ключі-версії списків. Списків з різними фільтрами може бути багато, тож при записі їх не перебирають:
    /// змінюють версію, і всі старі ключі сторінок одразу стають недосяжними (а потім витісняються за TTL).
    /// </summary>
    public const string MoviesListVersion = Prefix + "movies:list:version";
    public const string ScreeningsListVersion = Prefix + "screenings:list:version";

    public static string MoviesList(string version, string queryHash) => $"{Prefix}movies:list:v{version}:{queryHash}";
    public static string ScreeningsList(string version, string queryHash) => $"{Prefix}screenings:list:v{version}:{queryHash}";
}

public static class CacheKeyHash
{
    /// <summary>Короткий стабільний хеш параметрів запиту (фільтри, сортування, сторінка) для ключа сторінки списку.</summary>
    public static string Of(object query)
    {
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(query, query.GetType());
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(json), 0, 8).ToLowerInvariant();
    }
}
