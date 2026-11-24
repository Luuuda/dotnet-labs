using CatalogService.Dal.Abstractions;
using CatalogService.Dal.Repositories;
using CatalogService.Domain.Entities;
using CatalogService.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CatalogService.Dal;

/// <summary>
/// Unit of Work: усі репозиторії — над ОДНИМ DbContext; SaveChangesAsync викликається один раз наприкінці
/// бізнес-операції, і EF Core записує всі зміни в ОДНІЙ транзакції. (В eShop цю роль виконує сам
/// OrderingContext, що реалізує IUnitOfWork.) Помилки EF/SQL тут перетворюються на доменні винятки.
/// </summary>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly CatalogDbContext _context;

    public UnitOfWork(CatalogDbContext context)
    {
        _context = context;
        Movies = new MovieRepository(context);
        Genres = new GenreRepository(context);
        MovieGenres = new MovieGenreRepository(context);
        Screenings = new ScreeningRepository(context);
        Halls = new GenericRepository<Hall>(context);
    }

    public IMovieRepository Movies { get; }
    public IGenreRepository Genres { get; }
    public IMovieGenreRepository MovieGenres { get; }
    public IScreeningRepository Screenings { get; }
    public IGenericRepository<Hall> Halls { get; }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConflictException("Запис уже змінив інший користувач — оновіть дані й повторіть спробу", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            throw new ConflictException("Запис з таким унікальним ключем уже існує", ex);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 547 })
        {
            throw new ConflictException("Операцію відхилено: порушено обмеження цілісності (FK / CHECK)", ex);
        }
    }
}
