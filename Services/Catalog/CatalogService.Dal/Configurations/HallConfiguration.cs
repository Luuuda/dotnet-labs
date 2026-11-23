using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogService.Dal.Configurations;

public sealed class HallConfiguration : IEntityTypeConfiguration<Hall>
{
    public void Configure(EntityTypeBuilder<Hall> builder)
    {
        builder.ToTable("Halls", t =>
        {
            t.HasCheckConstraint("CK_Halls_Type", "[HallType] IN (N'Standard', N'IMAX', N'VIP', N'4DX')");
            t.HasCheckConstraint("CK_Halls_Size", "[RowsCount] BETWEEN 1 AND 50 AND [SeatsPerRow] BETWEEN 1 AND 60");
        });
        builder.HasKey(h => h.Id);
        builder.Property(h => h.Name).IsRequired().HasMaxLength(100);
        builder.Property(h => h.HallType).IsRequired().HasMaxLength(20).HasDefaultValue("Standard");
        builder.Ignore(h => h.Capacity);
        builder.HasIndex(h => h.Name).IsUnique();

        // Статичний сідінг довідника залів
        builder.HasData(
            new { Id = 1, Name = "Зала 1", HallType = "Standard", RowsCount = 10, SeatsPerRow = 12, IsActive = true },
            new { Id = 2, Name = "IMAX", HallType = "IMAX", RowsCount = 14, SeatsPerRow = 20, IsActive = true },
            new { Id = 3, Name = "VIP", HallType = "VIP", RowsCount = 5, SeatsPerRow = 8, IsActive = true });
    }
}
