using CatalogService.Bll.Dtos;

namespace CatalogService.Bll.Services;

public interface IMovieService
{
    Task<PagedResult<MovieListItemDto>> GetListAsync(MovieQuery query, CancellationToken ct);
    Task<MovieDto> GetByIdAsync(int id, CancellationToken ct);
    Task<MovieDto> CreateAsync(CreateMovieDto input, CancellationToken ct);
    Task<MovieDto> UpdateAsync(int id, UpdateMovieDto input, CancellationToken ct);
    Task DeleteAsync(int id, CancellationToken ct);
    Task<MovieDto> AddGenreAsync(int id, AssignGenreDto input, CancellationToken ct);
    Task RemoveGenreAsync(int id, int genreId, CancellationToken ct);
}

public interface IGenreService
{
    Task<IReadOnlyList<GenreDto>> GetAllAsync(CancellationToken ct);
    Task<GenreWithMoviesDto> GetWithMoviesAsync(int id, CancellationToken ct);
    Task<IReadOnlyList<RecentGenreMovieDto>> GetRecentlyAddedAsync(string slug, DateTime? since, CancellationToken ct);
    Task<IReadOnlyList<GenreStatsDto>> GetStatsAsync(CancellationToken ct);
    Task<IReadOnlyList<HallDto>> GetHallsAsync(CancellationToken ct);
}

public interface IScreeningService
{
    Task<PagedResult<ScreeningDto>> GetListAsync(ScreeningQuery query, CancellationToken ct);
    Task<ScreeningDto> GetByIdAsync(long id, CancellationToken ct);
    Task<ScreeningDto> CreateAsync(CreateScreeningDto input, CancellationToken ct);
    Task<ScreeningDto> ChangePriceAsync(long id, ChangePriceDto input, CancellationToken ct);
    Task CancelAsync(long id, CancellationToken ct);
}
