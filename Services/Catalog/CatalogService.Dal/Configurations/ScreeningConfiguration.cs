using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogService.Dal.Configurations;

public sealed class ScreeningConfiguration : IEntityTypeConfiguration<Screening>
{
    public void Configure(EntityTypeBuilder<Screening> builder)
    {
        builder.ToTable("Screenings", t =>
        {
            t.HasCheckConstraint("CK_Screenings_Price", "[BasePrice] > 0");
            t.HasCheckConstraint("CK_Screenings_Time", "[EndsAt] > [StartsAt]");
            t.HasCheckConstraint("CK_Screenings_Format", "[Format] IN ('2D', '3D', 'IMAX', '4DX')");
        });
        builder.HasKey(s => s.Id);                                          // BIGINT IDENTITY
        builder.Property(s => s.StartsAt).HasColumnType("datetime2(0)");
        builder.Property(s => s.EndsAt).HasColumnType("datetime2(0)");
        builder.Property(s => s.Format).IsRequired().HasMaxLength(10).IsUnicode(false).HasDefaultValue("2D");
        builder.Property(s => s.Language).IsRequired().HasMaxLength(30).HasDefaultValue("українська");
        builder.Property(s => s.BasePrice).HasColumnType("decimal(10,2)");
        builder.Property(s => s.IsCancelled).HasDefaultValue(false);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.ConfigureAudit();

        builder.HasOne(s => s.Hall).WithMany(h => h.Screenings)            // 1:N
               .HasForeignKey(s => s.HallId)
               .OnDelete(DeleteBehavior.Restrict);

        // Два сеанси в одній залі одночасно неможливі
        builder.HasIndex(s => new { s.HallId, s.StartsAt }).IsUnique();
        // Запит: «розклад на період» (фільтр за датою, лише не скасовані) — покриваючий, фільтрований
        builder.HasIndex(s => s.StartsAt)
               .IncludeProperties(s => new { s.MovieId, s.HallId, s.Format, s.BasePrice })
               .HasFilter("[IsCancelled] = 0").HasDatabaseName("IX_Screenings_StartsAt_Active");
        // Запит: «найближчі сеанси фільму»
        builder.HasIndex(s => new { s.MovieId, s.StartsAt });
    }
}
