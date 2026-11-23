using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogService.Dal.Configurations;

public sealed class GenreConfiguration : IEntityTypeConfiguration<Genre>
{
    public void Configure(EntityTypeBuilder<Genre> builder)
    {
        builder.ToTable("Genres", t => t.HasCheckConstraint("CK_Genres_Slug", "[Slug] NOT LIKE '%[^a-z0-9-]%'"));
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).IsRequired().HasMaxLength(Genre.NameMaxLength);
        builder.Property(g => g.Slug).IsRequired().HasMaxLength(Genre.SlugMaxLength).IsUnicode(false);
        builder.HasIndex(g => g.Name).IsUnique();
        builder.HasIndex(g => g.Slug).IsUnique();                          // запит: жанр за slug з URL

        // Статичний (модельний) сідінг довідника з фіксованими Id — потрапляє в міграцію
        builder.HasData(
            new { Id = 1, Name = "Фантастика", Slug = "sci-fi" },
            new { Id = 2, Name = "Драма", Slug = "drama" },
            new { Id = 3, Name = "Історичний", Slug = "historical" },
            new { Id = 4, Name = "Анімація", Slug = "animation" },
            new { Id = 5, Name = "Пригоди", Slug = "adventure" },
            new { Id = 6, Name = "Біографічний", Slug = "biography" },
            new { Id = 7, Name = "Сімейний", Slug = "family" });
    }
}
