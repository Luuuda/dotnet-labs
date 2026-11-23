using AutoMapper;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.Specifications;
using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace CatalogService.Bll.Services;

/// <summary>
/// Бізнес-логіка фільмів. Сервіс НЕ працює з EF напряму — лише через репозиторії та Unit of Work.
/// Правила самої сутності (рік, тривалість, дубль жанру) перевіряє Movie; сервіс перевіряє правила,
/// для яких потрібні інші дані з БД (унікальність назви+року, наявність жанру, майбутні сеанси).
/// </summary>
public sealed class MovieService(IUnitOfWork uow, IMapper mapper, TimeProvider clock, ILogger<MovieService> logger)
    : IMovieService
{
    public async Task<PagedResult<MovieListItemDto>> GetListAsync(MovieQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var spec = new MoviesFilterSpec(query, page, pageSize);

        var total = await uow.Movies.CountAsync(spec, ct);      // SELECT COUNT(*) ... WHERE ...
        var items = await uow.Movies.ListAsync(spec, ct);        // ... ORDER BY ... OFFSET ... FETCH NEXT ...
        return new PagedResult<MovieListItemDto>(mapper.Map<List<MovieListItemDto>>(items), total, page, pageSize);
    }

    public async Task<MovieDto> GetByIdAsync(int id, CancellationToken ct) =>
        mapper.Map<MovieDto>(await LoadAsync(id, ct));

    public async Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct)
    {
        var title = input.Title.Trim();
        if (await uow.Movies.ExistsAsync(title, input.ReleaseYear, null, ct))
            throw new ConflictException($"Фільм «{title}» ({input.ReleaseYear}) уже є в каталозі");

        var movie = new Movie(title, input.OriginalTitle, input.ReleaseYear, input.DurationMinutes, input.AgeRating);
        if (!string.IsNullOrWhiteSpace(input.Description))
            movie.SetDetails(input.Description.Trim(), input.Director, input.Country, input.Language);
        await AttachGenresAsync(movie, input.Genres, ct);

        await uow.Movies.AddAsync(movie, ct);
        await uow.SaveChangesAsync(ct);                          // один SaveChanges — одна транзакція

        logger.LogInformation("Створено фільм {MovieId} «{Title}»", movie.Id, movie.Title);
        return await GetByIdAsync(movie.Id, ct);
    }

    public async Task<MovieDto> UpdateAsync(int id, UpdateMovieDto input, CancellationToken ct)
    {
        var movie = await LoadAsync(id, ct);
        uow.Movies.SetExpectedVersion(movie, Convert.FromBase64String(input.RowVersion));

        var title = input.Title.Trim();
        if (await uow.Movies.ExistsAsync(title, input.ReleaseYear, id, ct))
            throw new ConflictException($"Фільм «{title}» ({input.ReleaseYear}) уже є в каталозі");

        movie.Rename(title, input.OriginalTitle);
        movie.ChangeInfo(input.ReleaseYear, input.DurationMinutes, input.AgeRating);
        if (!string.IsNullOrWhiteSpace(input.Description))
            movie.SetDetails(input.Description.Trim(), input.Director, input.Country, input.Language);

        await uow.SaveChangesAsync(ct);                          // застаріла версія → ConflictException (409)
        logger.LogInformation("Оновлено фільм {MovieId}", id);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var movie = await uow.Movies.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Movie>(id);
        if (await uow.Screenings.AnyAsync(new ActiveScreeningsOfMovieSpec(id, clock.GetUtcNow().UtcDateTime), ct))
            throw new ConflictException($"У фільму «{movie.Title}» є заплановані сеанси — спочатку скасуйте їх");

        movie.Delete();                                          // soft delete
        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Фільм {MovieId} видалено (soft delete)", id);
    }

    public async Task<MovieDto> AddGenreAsync(int id, AssignGenreDto input, CancellationToken ct)
    {
        var movie = await LoadAsync(id, ct);
        var genre = await uow.Genres.GetBySlugAsync(input.Slug, ct)
                    ?? throw new EntityNotFoundException($"Жанр «{input.Slug}» не знайдено");
        movie.AddGenre(genre, input.IsPrimary, clock.GetUtcNow().UtcDateTime);   // дубль → DomainException → 409
        await uow.SaveChangesAsync(ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task RemoveGenreAsync(int id, int genreId, CancellationToken ct)
    {
        var movie = await LoadAsync(id, ct);
        movie.RemoveGenre(genreId);
        await uow.SaveChangesAsync(ct);
    }

    private async Task<Movie> LoadAsync(int id, CancellationToken ct) =>
        await uow.Movies.GetWithDetailsAsync(id, clock.GetUtcNow().UtcDateTime, ct)
        ?? throw EntityNotFoundException.For<Movie>(id);

    private async Task AttachGenresAsync(Movie movie, IReadOnlyList<string> slugs, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        for (var i = 0; i < slugs.Count; i++)
        {
            var genre = await uow.Genres.GetBySlugAsync(slugs[i], ct)
                        ?? throw new ValidationException("genres", $"Жанр «{slugs[i]}» не знайдено");
            movie.AddGenre(genre, isPrimary: i == 0, now);       // перший жанр — основний
        }
    }
}
