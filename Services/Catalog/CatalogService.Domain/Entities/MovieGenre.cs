namespace CatalogService.Domain.Entities;

/// <summary>
/// Проміжна сутність M:N Movie ↔ Genre з ВЛАСНИМИ полями (AddedAt, IsPrimary).
/// Автоматичне M:N в EF Core таких полів зберегти не дозволило б.
/// </summary>
public sealed class MovieGenre
{
    public int MovieId { get; private set; }
    public Movie Movie { get; private set; } = null!;
    public int GenreId { get; private set; }
    public Genre Genre { get; private set; } = null!;
    public DateTime AddedAt { get; private set; }
    public bool IsPrimary { get; private set; }

    private MovieGenre() { }

    internal MovieGenre(Movie movie, Genre genre, bool isPrimary, DateTime addedAt)
    {
        Movie = movie;
        MovieId = movie.Id;
        Genre = genre;
        GenreId = genre.Id;
        IsPrimary = isPrimary;
        AddedAt = addedAt;
    }

    internal void SetPrimary(bool value) => IsPrimary = value;
}
