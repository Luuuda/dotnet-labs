using BookingService.Bll.Dtos;
using BookingService.Domain.Exceptions;
using BookingService.Domain.Models;

namespace BookingService.Bll.Validation;

/// <summary>
/// Бізнес-валідація вибору місць — правила, яким потрібен стан із БД (розмір зали, зайняті місця).
/// Модельна валідація (діапазони, обов'язковість) уже відбулася в API атрибутами DataAnnotations.
/// </summary>
public sealed class SeatSelectionValidator
{
    public IReadOnlyList<(int Row, int Seat)> Validate(
        Screening screening, IReadOnlyCollection<SeatDto> requested, IReadOnlyCollection<(int Row, int Seat)> occupied)
    {
        if (!screening.IsActive)
            throw new BusinessConflictException($"Сеанс {screening.Id} скасовано — бронювання неможливе");

        var seats = requested.Select(s => (s.Row, s.Seat)).ToList();

        if (seats.Distinct().Count() != seats.Count)
            throw new ValidationException("seats", "Місця в запиті повторюються");

        var outside = seats.Where(s => !screening.Contains(s.Row, s.Seat)).ToList();
        if (outside.Count > 0)
            throw new ValidationException("seats",
                $"Місця поза межами зали «{screening.HallName}» ({screening.RowsCount} рядів × {screening.SeatsPerRow} місць): " +
                string.Join(", ", outside.Select(Format)));

        var taken = seats.Intersect(occupied).ToList();
        if (taken.Count > 0)
            throw new BusinessConflictException("Місця вже зайняті: " + string.Join(", ", taken.Select(Format)));

        return seats;
    }

    private static string Format((int Row, int Seat) s) => $"ряд {s.Row} місце {s.Seat}";
}
