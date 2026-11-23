namespace CatalogService.Domain.Entities;

/// <summary>Жанр — довідник (статичний сідінг HasData з фіксованими Id).</summary>
public sealed class Genre
{
    public const int NameMaxLength = 60;
    public const int SlugMaxLength = 60;

    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    /// <summary>Для URL: /api/genres/sci-fi/movies</summary>
    public string Slug { get; private set; } = "";
    public List<MovieGenre> MovieGenres { get; private set; } = [];

    private Genre() { }

    public Genre(string name, string slug)
    {
        Name = name;
        Slug = slug;
    }
}
