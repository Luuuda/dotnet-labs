namespace BookingService.Tests.Integration;

/// <summary>
/// Інтеграційні тести працюють проти справжньої бази CinemaBooking (Лаб. 01).
/// Рядок підключення — у змінній оточення CINEMA_TEST_DB; якщо її немає, тести пропускаються.
/// </summary>
internal static class TestDatabase
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("CINEMA_TEST_DB");
}

/// <summary>[Fact], який пропускається, якщо тестова база не налаштована.</summary>
public sealed class DbFactAttribute : FactAttribute
{
    public DbFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
            Skip = "Змінна CINEMA_TEST_DB не задана — тест проти БД пропущено";
    }
}
