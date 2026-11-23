using AutoMapper;
using CatalogService.Bll.Dtos;
using CatalogService.Bll.Mapping;
using CatalogService.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace CatalogService.Tests.Unit;

public sealed class MappingTests
{
    internal static IMapper CreateMapper() =>
        new MapperConfiguration(MappingConfiguration.Configure, NullLoggerFactory.Instance).CreateMapper();

    [Fact]
    public void AutoMapper_configuration_is_valid() =>
        new MapperConfiguration(MappingConfiguration.Configure, NullLoggerFactory.Instance).AssertConfigurationIsValid();

    [Fact]
    public void Movie_maps_primary_genre_first()
    {
        var movie = new Movie("Дюна", null, 2024, 166, "12+");
        movie.AddGenre(new Genre("Пригоди", "adventure"), false, DateTime.UtcNow);
        movie.AddGenre(new Genre("Фантастика", "sci-fi"), true, DateTime.UtcNow);

        var dto = CreateMapper().Map<MovieListItemDto>(movie);

        Assert.Equal("Фантастика", dto.PrimaryGenre);
        Assert.Equal("Фантастика", dto.Genres[0]);
    }
}
