using AutoMapper;
using CatalogService.Bll.Caching;
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
/// Лаб. 06: читання — cache-aside через дворівневий кеш (L1 пам'ять → L2 Redis → БД); кожен запис синхронно
/// інвалідує картку фільму й версію списків. REST-контролер і gRPC-сервіс викликають ті самі методи, тож кеш спільний.
/// </summary>
public sealed class MovieService(IUnitOfWork uow, IMapper mapper, TimeProvider clock, ITwoLevelCache cache, ICatalogIntegrationEventService integrationEvents,
    ILogger<MovieService> logger)
    : IMovieService
{
    public async Task<PagedResult<MovieListItemDto>> GetListAsync(MovieQuery query, CancellationToken ct)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var version = await cache.GetVersionAsync(CacheKeys.MoviesListVersion, ct);
        var key = CacheKeys.MoviesList(version, CacheKeyHash.Of(query));
        return (await cache.GetOrCreateAsync(key, CachePolicy.List, async c =>
        {
            var spec = new MoviesFilterSpec(query, page, pageSize);
            var total = await uow.Movies.CountAsync(spec, c);    // SELECT COUNT(*) ... WHERE ...
            var items = await uow.Movies.ListAsync(spec, c);     // ... ORDER BY ... OFFSET ... FETCH NEXT ...
            return new PagedResult<MovieListItemDto>(mapper.Map<List<MovieListItemDto>>(items), total, page, pageSize);
        }, ct))!;
    }

    /// <summary>Картка фільму: catalog:movie:{id}. Неіснуючий фільм не кешується (EntityNotFoundException → 404 / gRPC NotFound).</summary>
    public async Task<MovieDto> GetByIdAsync(int id, CancellationToken ct) =>
        (await cache.GetOrCreateAsync(CacheKeys.Movie(id), CachePolicy.Card,
            async c => mapper.Map<MovieDto>(await LoadAsync(id, c)), ct))!;

    public async Task<IReadOnlyList<MovieDto>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var result = new List<MovieDto>(ids.Count);
        foreach (var id in ids.Distinct())
        {
            try { result.Add(await GetByIdAsync(id, ct)); }        // кожна картка — зі свого ключа кешу
            catch (EntityNotFoundException) { /* відсутні фільми просто не повертаються */ }
        }
        return result;
    }

    public async Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("CreateMovie");
        activity?.SetTag("movie.title", input.Title);
        var title = input.Title.Trim();
        if (await uow.Movies.ExistsAsync(title, input.ReleaseYear, null, ct))
            throw new ConflictException($"Фільм «{title}» ({input.ReleaseYear}) уже є в каталозі");

        var movie = new Movie(title, input.OriginalTitle, input.ReleaseYear, input.DurationMinutes, input.AgeRating);
        if (!string.IsNullOrWhiteSpace(input.Description))
            movie.SetDetails(input.Description.Trim(), input.Director, input.Country, input.Language);
        await AttachGenresAsync(movie, input.Genres, ct);

        await uow.Movies.AddAsync(movie, ct);
        await uow.SaveChangesAsync(ct);                          // один SaveChanges — одна транзакція

        activity?.SetTag("movie.id", movie.Id);
        await cache.BumpVersionAsync(CacheKeys.MoviesListVersion, "MovieCreated", ct);   // новий фільм з'являється в списках
        logger.LogInformation("Створено фільм {MovieId} «{Title}» ({ReleaseYear}), жанрів: {GenresCount}",
            movie.Id, movie.Title, movie.ReleaseYear, movie.MovieGenres.Count);
        return await GetByIdAsync(movie.Id, ct);
    }

    public async Task<MovieDto> UpdateAsync(int id, UpdateMovieDto input, CancellationToken ct)
    {
        using var activity = CatalogTelemetry.Source.StartActivity("UpdateMovie");
        activity?.SetTag("movie.id", id);
        var movie = await LoadAsync(id, ct);
        uow.Movies.SetExpectedVersion(movie, Convert.FromBase64String(input.RowVersion));

        var title = input.Title.Trim();
        if (await uow.Movies.ExistsAsync(title, input.ReleaseYear, id, ct))
            throw new ConflictException($"Фільм «{title}» ({input.ReleaseYear}) уже є в каталозі");

        var oldTitle = movie.Title;
        movie.Rename(title, input.OriginalTitle);
        movie.ChangeInfo(input.ReleaseYear, input.DurationMinutes, input.AgeRating);
        if (!string.IsNullOrWhiteSpace(input.Description))
            movie.SetDetails(input.Description.Trim(), input.Director, input.Country, input.Language);

        await uow.SaveChangesAsync(ct);                          // застаріла версія → ConflictException (409)
        await InvalidateMovieAsync(id, "MovieUpdated", ct);
        await cache.BumpVersionAsync(CacheKeys.ScreeningsListVersion, "MovieUpdated", ct);   // назва фільму є в розкладі
        logger.LogInformation("Оновлено фільм {MovieId}", id);

        // Лаб. 08: лише після успішного SaveChangesAsync і лише якщо назва справді змінилася —
        // копії назви є в Бронюванні (dbo.Screenings.MovieTitle) і Відгуках (reviews / discussions.movieTitle)
        if (!string.Equals(oldTitle, movie.Title, StringComparison.Ordinal))
            await integrationEvents.PublishMovieRenamedAsync(id, oldTitle, movie.Title, movie.OriginalTitle, ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct)
    {
        var movie = await uow.Movies.GetByIdAsync(id, ct) ?? throw EntityNotFoundException.For<Movie>(id);
        if (await uow.Screenings.AnyAsync(new ActiveScreeningsOfMovieSpec(id, clock.GetUtcNow().UtcDateTime), ct))
            throw new ConflictException($"У фільму «{movie.Title}» є заплановані сеанси — спочатку скасуйте їх");

        movie.Delete();                                          // soft delete
        await uow.SaveChangesAsync(ct);
        await InvalidateMovieAsync(id, "MovieDeleted", ct);
        logger.LogInformation("Фільм {MovieId} видалено (soft delete)", id);
    }

    public async Task<MovieDto> AddGenreAsync(int id, AssignGenreDto input, CancellationToken ct)
    {
        var movie = await LoadAsync(id, ct);
        var genre = await uow.Genres.GetBySlugAsync(input.Slug, ct)
                    ?? throw new EntityNotFoundException($"Жанр «{input.Slug}» не знайдено");
        movie.AddGenre(genre, input.IsPrimary, clock.GetUtcNow().UtcDateTime);   // дубль → DomainException → 409
        await uow.SaveChangesAsync(ct);
        await InvalidateMovieAsync(id, "MovieGenreAdded", ct);
        return await GetByIdAsync(id, ct);
    }

    public async Task RemoveGenreAsync(int id, int genreId, CancellationToken ct)
    {
        var movie = await LoadAsync(id, ct);
        movie.RemoveGenre(genreId);
        await uow.SaveChangesAsync(ct);
        await InvalidateMovieAsync(id, "MovieGenreRemoved", ct);
    }

    /// <summary>Інвалідація одразу після SaveChanges: картка фільму + усі сторінки списків фільмів.</summary>
    private async Task InvalidateMovieAsync(int id, string reason, CancellationToken ct)
    {
        await cache.InvalidateAsync(reason, ct, CacheKeys.Movie(id));
        await cache.BumpVersionAsync(CacheKeys.MoviesListVersion, reason, ct);
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
