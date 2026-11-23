using Microsoft.EntityFrameworkCore;
using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CatalogService.Dal.Configurations;

internal static class AuditConfigurationExtensions
{
    /// <summary>Однакова конфігурація аудитних колонок для всіх AuditableEntity.</summary>
    public static void ConfigureAudit<T>(this EntityTypeBuilder<T> builder) where T : AuditableEntity
    {
        builder.Property(e => e.CreatedAt).HasColumnType("datetime2(3)").HasDefaultValueSql("SYSUTCDATETIME()");
        builder.Property(e => e.CreatedBy).IsRequired().HasMaxLength(100).HasDefaultValue("system");
        builder.Property(e => e.UpdatedAt).HasColumnType("datetime2(3)");
        builder.Property(e => e.UpdatedBy).HasMaxLength(100);
    }
}
