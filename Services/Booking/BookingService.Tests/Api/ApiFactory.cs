using BookingService.Tests.Integration;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BookingService.Tests.Api;

/// <summary>Піднімає API в пам'яті проти тестової бази (рядок підключення — змінною оточення).</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public ApiFactory()
    {
        if (!string.IsNullOrWhiteSpace(TestDatabase.ConnectionString))
            Environment.SetEnvironmentVariable("ConnectionStrings__CinemaBookingDb", TestDatabase.ConnectionString);
    }
}
