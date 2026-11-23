namespace CatalogService.Bll.Dtos;

public sealed record GenreDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
}

public sealed record GenreMovieDto
{
    public int MovieId { get; init; }
    public string MovieTitle { get; init; } = "";
    public short ReleaseYear { get; init; }
    public bool IsPrimary { get; init; }
    public DateTime AddedAt { get; init; }
}

/// <summary>Жанр з фільмами (Explicit Loading: два окремі SQL-запити).</summary>
public sealed record GenreWithMoviesDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public IReadOnlyList<GenreMovieDto> Movies { get; init; } = [];
}

/// <summary>Результат LINQ-запиту по проміжній сутності M:N.</summary>
public sealed record RecentGenreMovieDto
{
    public int MovieId { get; init; }
    public string Title { get; init; } = "";
    public short ReleaseYear { get; init; }
    public string AgeRating { get; init; } = "";
    public DateTime AddedAt { get; init; }
    public bool IsPrimary { get; init; }
}

public sealed record GenreStatsDto
{
    public int GenreId { get; init; }
    public string Name { get; init; } = "";
    public string Slug { get; init; } = "";
    public int MoviesCount { get; init; }
    public int PrimaryCount { get; init; }
}

public sealed record HallDto
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string HallType { get; init; } = "";
    public int RowsCount { get; init; }
    public int SeatsPerRow { get; init; }
    public int Capacity { get; init; }
    public bool IsActive { get; init; }
}
