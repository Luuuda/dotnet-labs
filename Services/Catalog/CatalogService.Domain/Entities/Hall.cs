using CatalogService.Domain.Exceptions;

namespace CatalogService.Domain.Entities;

/// <summary>Зала кінотеатру — довідник (статичний сідінг HasData).</summary>
public sealed class Hall
{
    public static readonly string[] HallTypes = ["Standard", "IMAX", "VIP", "4DX"];

    public int Id { get; private set; }
    public string Name { get; private set; } = "";
    public string HallType { get; private set; } = "Standard";
    public int RowsCount { get; private set; }
    public int SeatsPerRow { get; private set; }
    public bool IsActive { get; private set; } = true;
    public List<Screening> Screenings { get; private set; } = [];

    public int Capacity => RowsCount * SeatsPerRow;

    private Hall() { }

    public Hall(string name, string hallType, int rowsCount, int seatsPerRow)
    {
        if (!HallTypes.Contains(hallType)) throw new DomainException($"Невідомий тип зали «{hallType}»");
        if (rowsCount is < 1 or > 50 || seatsPerRow is < 1 or > 60)
            throw new DomainException("Розмір зали: 1–50 рядів і 1–60 місць у ряду");
        Name = name;
        HallType = hallType;
        RowsCount = rowsCount;
        SeatsPerRow = seatsPerRow;
    }
}
