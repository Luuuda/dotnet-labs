using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogService.Dal.Configurations;

public sealed class MovieConfiguration : IEntityTypeConfiguration<Movie>
{
    public void Configure(EntityTypeBuilder<Movie> builder)
    {
        builder.ToTable("Movies", t =>
        {
            t.HasCheckConstraint("CK_Movies_Year", "[ReleaseYear] BETWEEN 1895 AND 2100");
            t.HasCheckConstraint("CK_Movies_Duration", "[DurationMinutes] BETWEEN 1 AND 600");
            t.HasCheckConstraint("CK_Movies_Age", "[AgeRating] IN ('0+', '6+', '12+', '16+', '18+')");
            t.HasCheckConstraint("CK_Movies_Rating", "[AverageRating] IS NULL OR [AverageRating] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Movies_RatingsCount", "[RatingsCount] >= 0");
        });

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Title).IsRequired().HasMaxLength(Movie.TitleMaxLength);
        builder.Property(m => m.OriginalTitle).HasMaxLength(Movie.TitleMaxLength);
        builder.Property(m => m.AgeRating).IsRequired().HasMaxLength(3).IsUnicode(false).HasDefaultValue("0+");
        builder.Property(m => m.AverageRating).HasColumnType("decimal(3,2)");
        builder.Property(m => m.RatingsCount).HasDefaultValue(0);
        builder.Property(m => m.IsDeleted).HasDefaultValue(false);
        builder.Property(m => m.RowVersion).IsRowVersion();                 // оптимістична конкурентність
        builder.ConfigureAudit();

        // Запит: «чи є вже фільм з такою назвою й роком» — UNIQUE лише серед не видалених
        builder.HasIndex(m => new { m.Title, m.ReleaseYear })
               .IsUnique().HasFilter("[IsDeleted] = 0").HasDatabaseName("UQ_Movies_Title_Year");
        // Запит: «пошук фільму за назвою + сортування за роком» (список фільмів з фільтрами)
        builder.HasIndex(m => m.ReleaseYear)
               .IncludeProperties(m => new { m.Title, m.AgeRating, m.DurationMinutes })
               .HasFilter("[IsDeleted] = 0").HasDatabaseName("IX_Movies_ReleaseYear_Active");

        // Soft delete: видалені фільми не потрапляють у жоден запит
        builder.HasQueryFilter(m => !m.IsDeleted);

        builder.HasOne(m => m.Details)                                      // 1:1
               .WithOne()
               .HasForeignKey<MovieDetails>(d => d.MovieId)
               .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(m => m.Screenings)                                  // 1:N
               .WithOne(s => s.Movie)
               .HasForeignKey(s => s.MovieId)
               .OnDelete(DeleteBehavior.Restrict);                          // сеанси — історія продажів
    }
}
