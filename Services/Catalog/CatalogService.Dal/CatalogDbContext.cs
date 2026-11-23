using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Dal;

/// <summary>
/// DbContext Каталогу. Уся конфігурація — в окремих класах IEntityTypeConfiguration&lt;T&gt;
/// (папка Configurations), підключених ЦЕНТРАЛІЗОВАНО в OnModelCreating.
/// </summary>
public sealed class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Movie> Movies => Set<Movie>();
    public DbSet<MovieDetails> MovieDetails => Set<MovieDetails>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<MovieGenre> MovieGenres => Set<MovieGenre>();
    public DbSet<Hall> Halls => Set<Hall>();
    public DbSet<Screening> Screenings => Set<Screening>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(CatalogDbContext).Assembly);
}
