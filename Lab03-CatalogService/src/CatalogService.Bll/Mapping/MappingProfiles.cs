using AutoMapper;
using CatalogService.Bll.Dtos;
using CatalogService.Dal.Abstractions;
using CatalogService.Domain.Entities;

namespace CatalogService.Bll.Mapping;

/// <summary>Усі профілі — в одному місці: і для DI, і для тесту AssertConfigurationIsValid.</summary>
public static class MappingConfiguration
{
    public static void Configure(IMapperConfigurationExpression cfg)
    {
        cfg.AddProfile<CommonProfile>();
        cfg.AddProfile<MovieProfile>();
        cfg.AddProfile<GenreProfile>();
        cfg.AddProfile<ScreeningProfile>();
    }
}

public sealed class CommonProfile : Profile
{
    public CommonProfile() => CreateMap<byte[], string>().ConvertUsing(b => Convert.ToBase64String(b));
}

public sealed class MovieProfile : Profile
{
    public MovieProfile()
    {
        CreateMap<Movie, MovieListItemDto>()
            .ForMember(d => d.Genres, o => o.MapFrom(s => s.MovieGenres
                .OrderByDescending(mg => mg.IsPrimary).ThenBy(mg => mg.Genre.Name).Select(mg => mg.Genre.Name)))
            .ForMember(d => d.PrimaryGenre, o => o.MapFrom(s => s.MovieGenres
                .Where(mg => mg.IsPrimary).Select(mg => mg.Genre.Name).FirstOrDefault()));

        CreateMap<MovieDetails, MovieDetailsDto>();
        CreateMap<MovieGenre, MovieGenreDto>()
            .ForMember(d => d.Name, o => o.MapFrom(s => s.Genre.Name))
            .ForMember(d => d.Slug, o => o.MapFrom(s => s.Genre.Slug));
        CreateMap<Screening, ScreeningShortDto>();                            // HallName ← Hall.Name (flattening)
        CreateMap<Movie, MovieDto>()
            .ForMember(d => d.Genres, o => o.MapFrom(s => s.MovieGenres.OrderByDescending(mg => mg.IsPrimary)))
            .ForMember(d => d.UpcomingScreenings, o => o.MapFrom(s => s.Screenings));
    }
}

public sealed class GenreProfile : Profile
{
    public GenreProfile()
    {
        CreateMap<Genre, GenreDto>();
        CreateMap<MovieGenre, GenreMovieDto>()                               // MovieTitle ← Movie.Title (flattening)
            .ForMember(d => d.ReleaseYear, o => o.MapFrom(s => s.Movie.ReleaseYear));
        CreateMap<Genre, GenreWithMoviesDto>()
            .ForMember(d => d.Movies, o => o.MapFrom(s => s.MovieGenres));
        CreateMap<GenreMovieRow, RecentGenreMovieDto>();
        CreateMap<GenreStatsRow, GenreStatsDto>();
        CreateMap<Hall, HallDto>();
    }
}

public sealed class ScreeningProfile : Profile
{
    public ScreeningProfile() => CreateMap<Screening, ScreeningDto>();     // MovieTitle, HallName — flattening
}
