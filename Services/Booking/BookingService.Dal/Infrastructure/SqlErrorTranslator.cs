using BookingService.Domain.Exceptions;
using Microsoft.Data.SqlClient;

namespace BookingService.Dal.Infrastructure;

/// <summary>
/// Перетворення помилок SQL Server на доменні винятки.
/// Номери 50xxx кидають процедури з Лаб. 01 (THROW) — їхні повідомлення написані для людей,
/// тому передаються як є. Для системних помилок (2601/2627/547) повертається власне повідомлення,
/// щоб у API не потрапляли назви таблиць, індексів чи сирий текст SQL.
/// </summary>
public static class SqlErrorTranslator
{
    // Коди з Лаб. 01 (README → «Коди керованих помилок»)
    private static readonly HashSet<int> NotFound = [50001, 50011, 50012, 50030];
    private static readonly HashSet<int> Conflict = [50002, 50003, 50004, 50015, 50020, 50021, 50031, 50032];
    private static readonly HashSet<int> Validation = [50010, 50013, 50014, 50040];

    public static Exception Translate(SqlException ex)
    {
        // Процедура може повернути кілька повідомлень (наприклад, 50002 і 266) — беремо перше.
        var number = ex.Number;
        var message = ex.Errors.Count > 0 ? ex.Errors[0].Message : ex.Message;

        if (NotFound.Contains(number)) return new NotFoundException(message, ex);
        if (Conflict.Contains(number)) return new BusinessConflictException(message, ex);
        if (Validation.Contains(number)) return new ValidationException(message);

        return number switch
        {
            2601 or 2627 when ex.Message.Contains("UX_Tickets_Screening_Seat_Active", StringComparison.OrdinalIgnoreCase)
                => new BusinessConflictException("Одне або кілька місць уже зайняті", ex),
            2601 or 2627 => new BusinessConflictException("Запис з таким ключем уже існує", ex),
            547 => new BusinessConflictException("Операцію відхилено: запис пов'язаний з іншими даними або порушує обмеження", ex),
            _ => ex // невідома помилка — нехай стане 500 (з логом), а не «вгаданим» статусом
        };
    }
}
