using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogService.Dal.Configurations;

/// <summary>M:N Movie ↔ Genre через явну проміжну сутність зі складеним ключем і власними полями.</summary>
public sealed class MovieGenreConfiguration : IEntityTypeConfiguration<MovieGenre>
{
    public void Configure(EntityTypeBuilder<MovieGenre> builder)
    {
        builder.ToTable("MovieGenres");
        builder.HasKey(mg => new { mg.MovieId, mg.GenreId });              // складений ключ
        builder.Property(mg => mg.AddedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(mg => mg.IsPrimary).HasDefaultValue(false);

        builder.HasOne(mg => mg.Movie).WithMany(m => m.MovieGenres)
               .HasForeignKey(mg => mg.MovieId)
               .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(mg => mg.Genre).WithMany(g => g.MovieGenres)
               .HasForeignKey(mg => mg.GenreId)
               .OnDelete(DeleteBehavior.Restrict);                          // жанр, що використовується, не видалити

        // Запит M:N: «нещодавно додані фільми жанру X» (WHERE GenreId = ... AND AddedAt >= ... ORDER BY AddedAt DESC)
        builder.HasIndex(mg => new { mg.GenreId, mg.AddedAt }).IncludeProperties(mg => mg.IsPrimary);
    }
}
