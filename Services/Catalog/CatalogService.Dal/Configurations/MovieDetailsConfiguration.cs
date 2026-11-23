using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogService.Dal.Configurations;

public sealed class MovieDetailsConfiguration : IEntityTypeConfiguration<MovieDetails>
{
    public void Configure(EntityTypeBuilder<MovieDetails> builder)
    {
        builder.ToTable("MovieDetails");
        builder.HasKey(d => d.MovieId);                                     // PK = FK (1:1)
        builder.Property(d => d.Description).IsRequired().HasMaxLength(4000);
        builder.Property(d => d.Director).HasMaxLength(200);
        builder.Property(d => d.Country).HasMaxLength(100);
        builder.Property(d => d.Language).HasMaxLength(50);
        builder.Property(d => d.PosterUrl).HasMaxLength(500);
        builder.Property(d => d.TrailerUrl).HasMaxLength(500);
    }
}
