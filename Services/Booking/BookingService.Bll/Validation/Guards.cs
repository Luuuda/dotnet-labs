using BookingService.Domain.Exceptions;

namespace BookingService.Bll.Validation;

/// <summary>Дрібні перевірки вхідних параметрів, спільні для сервісів.</summary>
public static class Guards
{
    public const int MaxPageSize = 100;

    public static void Paging(int page, int pageSize)
    {
        if (page < 1)
            throw new ValidationException("page", "Номер сторінки має бути ≥ 1");
        if (pageSize is < 1 or > MaxPageSize)
            throw new ValidationException("pageSize", $"Розмір сторінки — від 1 до {MaxPageSize}");
    }

    /// <summary>Декодування rowversion з base64 (обов'язкове поле).</summary>
    public static byte[] RowVersion(string? value, string field = "rowVersion") =>
        OptionalRowVersion(value, field) ?? throw new ValidationException(field, "Потрібна версія рядка (rowVersion)");

    public static byte[]? OptionalRowVersion(string? value, string field = "rowVersion")
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            var bytes = Convert.FromBase64String(value);
            return bytes.Length == 8 ? bytes : throw new FormatException();
        }
        catch (FormatException)
        {
            throw new ValidationException(field, "rowVersion має бути base64-рядком із 8 байтів (як у відповіді GET)");
        }
    }
}
