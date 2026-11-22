var builder = DistributedApplication.CreateBuilder(args);

// API мікросервісу бронювання
var api = builder.AddProject<Projects.BookingService_Api>("booking-api");

// Ім'я ресурсу бази = ім'я рядка підключення в API ("CinemaBookingDb"),
// тож Aspire передає його в API автоматично (ConnectionStrings__CinemaBookingDb) — код API не змінюється.
if (bool.TryParse(builder.Configuration["UseExistingDatabase"], out var useExisting) && useExisting)
{
    // Варіант 1: наявна база з Лаб. 01 (контейнер cinema-mssql); рядок — у appsettings.json AppHost
    var existingDb = builder.AddConnectionString("CinemaBookingDb");
    api.WithReference(existingDb);
}
else
{
    // Варіант 2 (за замовчуванням): SQL Server у контейнері. База створюється порожньою,
    // а API під час старту застосовує до неї скрипти Лаб. 01 (schema → procedures → seed).
    var password = builder.AddParameter("sql-password", secret: true);
    var sql = builder.AddSqlServer("sql", password)
                     .WithDataVolume("cinema-booking-sql-data")
                     .WithLifetime(ContainerLifetime.Persistent);
    var db = sql.AddDatabase("CinemaBookingDb", databaseName: "CinemaBooking");

    api.WithReference(db)
       .WaitFor(db)
       .WithEnvironment("Database__ApplyScriptsOnStartup", "true");
}

builder.Build().Run();
