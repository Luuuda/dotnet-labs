using CatalogService.Domain.Exceptions;

namespace CatalogService.Domain.Entities;

/// <summary>
/// Сеанс (1:N від Movie і від Hall). Ціна задається в конструкторі й змінюється лише методом
/// <see cref="ChangePrice"/>; скасування — методом <see cref="Cancel"/> (як RemoveStock / AddStock у CatalogItem eShop).
/// </summary>
public sealed class Screening : AuditableEntity
{
    public const int CleaningMinutes = 15;
    public static readonly string[] Formats = ["2D", "3D", "IMAX", "4DX"];

    public long Id { get; private set; }
    public int MovieId { get; private set; }
    public Movie Movie { get; private set; } = null!;
    public int HallId { get; private set; }
    public Hall Hall { get; private set; } = null!;
    public DateTime StartsAt { get; private set; }
    public DateTime EndsAt { get; private set; }
    public string Format { get; private set; } = "2D";
    public string Language { get; private set; } = "українська";
    public decimal BasePrice { get; private set; }
    public bool IsCancelled { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    private Screening() { }

    public Screening(Movie movie, Hall hall, DateTime startsAt, string format, string language, decimal basePrice)
    {
        if (movie.IsDeleted) throw new DomainException($"Фільм «{movie.Title}» видалено — сеанс створити неможливо");
        if (!hall.IsActive) throw new DomainException($"Зала «{hall.Name}» не працює");
        if (!Formats.Contains(format)) throw new DomainException($"Формат має бути одним із: {string.Join(", ", Formats)}");
        if (format == "IMAX" && hall.HallType != "IMAX") throw new DomainException("Формат IMAX доступний лише в IMAX-залі");
        EnsurePositive(basePrice);

        Movie = movie;
        MovieId = movie.Id;
        Hall = hall;
        HallId = hall.Id;
        Format = format;
        Language = language;
        BasePrice = basePrice;
        SetTime(startsAt);
    }

    public void ChangePrice(decimal newPrice)
    {
        EnsureNotCancelled();
        EnsurePositive(newPrice);
        BasePrice = newPrice;
    }

    public void Reschedule(DateTime startsAt)
    {
        EnsureNotCancelled();
        SetTime(startsAt);
    }

    public void Cancel()
    {
        EnsureNotCancelled();
        IsCancelled = true;
    }

    /// <summary>Кінець = початок + тривалість фільму + прибирання зали.</summary>
    private void SetTime(DateTime startsAt)
    {
        StartsAt = startsAt;
        EndsAt = startsAt.AddMinutes(Movie.DurationMinutes + CleaningMinutes);
    }

    private void EnsureNotCancelled()
    {
        if (IsCancelled) throw new DomainException($"Сеанс {Id} уже скасовано");
    }

    private static void EnsurePositive(decimal price)
    {
        if (price <= 0) throw new DomainException("Ціна має бути більшою за 0");
    }
}
