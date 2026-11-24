using CatalogService.Bll.Dtos;
using CatalogService.Bll.Services;
using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace CatalogService.Tests.Unit;

public sealed class MovieServiceTests
{
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IMovieRepository> _movies = new();
    private readonly Mock<IGenreRepository> _genres = new();
    private readonly MovieService _service;

    public MovieServiceTests()
    {
        _uow.SetupGet(u => u.Movies).Returns(_movies.Object);
        _uow.SetupGet(u => u.Genres).Returns(_genres.Object);
        _service = new MovieService(_uow.Object, MappingTests.CreateMapper(), TimeProvider.System, TestCache.Create(), NullLogger<MovieService>.Instance);
    }

    [Fact]
    public async Task Duplicate_title_and_year_is_conflict_and_nothing_is_saved()
    {
        _movies.Setup(m => m.ExistsAsync("Довбуш", 2023, null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(
            new CreateMovieDto { Title = "Довбуш", ReleaseYear = 2023, DurationMinutes = 115, AgeRating = "12+" },
            CancellationToken.None));

        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Missing_movie_is_EntityNotFound()
    {
        _movies.Setup(m => m.GetWithDetailsAsync(404, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync((Movie?)null);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => _service.GetByIdAsync(404, CancellationToken.None));
    }

    [Fact]
    public async Task Unknown_genre_slug_is_validation_error()
    {
        _genres.Setup(g => g.GetBySlugAsync("western", It.IsAny<CancellationToken>())).ReturnsAsync((Genre?)null);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(
            new CreateMovieDto { Title = "Новий", ReleaseYear = 2025, DurationMinutes = 100, AgeRating = "12+", Genres = ["western"] },
            CancellationToken.None));

        Assert.True(ex.Errors.ContainsKey("genres"));
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Create_saves_once_through_unit_of_work()
    {
        Movie? added = null;
        _genres.Setup(g => g.GetBySlugAsync("drama", It.IsAny<CancellationToken>())).ReturnsAsync(new Genre("Драма", "drama"));
        _movies.Setup(m => m.AddAsync(It.IsAny<Movie>(), It.IsAny<CancellationToken>()))
               .Callback<Movie, CancellationToken>((m, _) => added = m).Returns(Task.CompletedTask);
        _movies.Setup(m => m.GetWithDetailsAsync(It.IsAny<int>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
               .ReturnsAsync(() => added);

        var dto = await _service.CreateAsync(new CreateMovieDto
        {
            Title = "Новий фільм", ReleaseYear = 2025, DurationMinutes = 100, AgeRating = "12+",
            Description = "Опис", Director = "Режисер", Genres = ["drama"]
        }, CancellationToken.None);

        Assert.Equal("Новий фільм", dto.Title);
        Assert.Equal("Драма", Assert.Single(dto.Genres).Name);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
