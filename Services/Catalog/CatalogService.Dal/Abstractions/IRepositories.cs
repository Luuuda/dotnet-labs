using Ardalis.Specification;
using CatalogService.Domain.Entities;

namespace CatalogService.Dal.Abstractions;

/// <summary>
/// Async Generic Repository — той самий контракт, що в Лаб. 02, але реалізований EF Core через DbSet&lt;T&gt;.
/// Методи зі зверненням до БД — асинхронні; Update / Delete лише змінюють стан сутності в контексті.
/// Додатково приймає СПЕЦИФІКАЦІЮ (Specification pattern) для складних вибірок.
/// </summary>
public interface IGenericRepository<T> where T : class
{
    Task<T?> GetByIdAsync(object id, CancellationToken ct = default);
    Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(T entity, CancellationToken ct = default);
    void Update(T entity);
    void Delete(T entity);
    Task<int> SaveChangesAsync(CancellationToken ct = default);

    Task<IReadOnlyList<T>> ListAsync(ISpecification<T> specification, CancellationToken ct = default);
    /// <summary>Кількість за критеріями специфікації (без Skip/Take) — для totalCount.</summary>
    Task<int> CountAsync(ISpecification<T> specification, CancellationToken ct = default);
    Task<bool> AnyAsync(ISpecification<T> specification, CancellationToken ct = default);
    Task<T?> FirstOrDefaultAsync(ISpecification<T> specification, CancellationToken ct = default);
}

/// <summary>Фільми — EAGER LOADING (картка фільму з усім пов'язаним одним SQL-запитом).</summary>
public interface IMovieRepository : IGenericRepository<Movie>
{
    Task<Movie?> GetWithDetailsAsync(int id, DateTime screeningsFrom, CancellationToken ct = default);
    Task<bool> ExistsAsync(string title, int releaseYear, int? exceptId, CancellationToken ct = default);
    /// <summary>Очікувана клієнтом версія рядка — EF перевірить її в UPDATE ... WHERE RowVersion = @p.</summary>
    void SetExpectedVersion(Movie movie, byte[] rowVersion);
}

/// <summary>Жанри — EXPLICIT LOADING (жанр окремо, його фільми — окремим запитом лише за потреби).</summary>
public interface IGenreRepository : IGenericRepository<Genre>
{
    Task<Genre?> GetWithMoviesAsync(int id, CancellationToken ct = default);
    Task<Genre?> GetBySlugAsync(string slug, CancellationToken ct = default);
}

/// <summary>Рядок вибірки M:N «фільм у жанрі» з власними полями проміжної сутності.</summary>
public sealed record GenreMovieRow(int MovieId, string Title, short ReleaseYear, string AgeRating,
                                   DateTime AddedAt, bool IsPrimary);

/// <summary>Статистика жанру, порахована на рівні БД (GROUP BY по проміжній сутності).</summary>
public sealed record GenreStatsRow(int GenreId, string Name, string Slug, int MoviesCount, int PrimaryCount);

/// <summary>MovieGenres — LINQ to Entities: вибірки «багато до багатьох» через проміжну сутність.</summary>
public interface IMovieGenreRepository : IGenericRepository<MovieGenre>
{
    Task<IReadOnlyList<GenreMovieRow>> GetRecentlyAddedAsync(string genreSlug, DateTime since, CancellationToken ct = default);
    Task<IReadOnlyList<GenreStatsRow>> GetGenreStatsAsync(CancellationToken ct = default);
}

public interface IScreeningRepository : IGenericRepository<Screening>
{
    /// <summary>Чи перетинається новий сеанс з іншим у тій самій залі.</summary>
    Task<bool> HasOverlapAsync(int hallId, DateTime startsAt, DateTime endsAt, long? exceptId, CancellationToken ct = default);
}

/// <summary>
/// Unit of Work: усі репозиторії працюють з ОДНИМ DbContext; зміни потрапляють у БД одним
/// <see cref="SaveChangesAsync"/> (одна транзакція) наприкінці бізнес-операції.
/// </summary>
public interface IUnitOfWork
{
    IMovieRepository Movies { get; }
    IGenreRepository Genres { get; }
    IMovieGenreRepository MovieGenres { get; }
    IScreeningRepository Screenings { get; }
    IGenericRepository<Hall> Halls { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
