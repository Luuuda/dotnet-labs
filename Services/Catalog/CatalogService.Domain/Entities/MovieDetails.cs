namespace CatalogService.Domain.Entities;

/// <summary>1:1 з Movie: MovieId — водночас PK і FK. «Важкі» поля винесено окремо від списку фільмів.</summary>
public sealed class MovieDetails
{
    public int MovieId { get; private set; }
    public string Description { get; private set; } = "";
    public string? Director { get; private set; }
    public string? Country { get; private set; }
    public string? Language { get; private set; }
    public string? PosterUrl { get; private set; }
    public string? TrailerUrl { get; private set; }

    private MovieDetails() { }

    internal MovieDetails(string description, string? director, string? country, string? language,
                          string? posterUrl, string? trailerUrl)
        => Update(description, director, country, language, posterUrl, trailerUrl);

    internal void Update(string description, string? director, string? country, string? language,
                         string? posterUrl, string? trailerUrl)
    {
        Description = description;
        Director = director;
        Country = country;
        Language = language;
        PosterUrl = posterUrl;
        TrailerUrl = trailerUrl;
    }
}
