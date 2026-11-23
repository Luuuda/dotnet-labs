using System.Diagnostics;

namespace BookingService.Bll.Services;

/// <summary>
/// Власні spans бізнес-операцій сервісу бронювання (Лаб. 05). Platform.ServiceDefaults підписується на джерела
/// «Cinema.*», тож ці spans з'являються в trace поруч зі spans HTTP і SQL.
/// </summary>
public static class BookingTelemetry
{
    public static readonly ActivitySource Source = new("Cinema.Booking");
}
