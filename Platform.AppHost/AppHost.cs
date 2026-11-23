// Оркестрація платформи «Кінотеатр» (.NET Aspire). Тут лише опис ресурсів і зв'язків між ними — без бізнес-логіки.
// Порядок запуску задається WaitFor: бази → мікросервіси → Aggregator → Gateway.

var builder = DistributedApplication.CreateBuilder(args);

// ---------- Бази даних: Docker-контейнери з постійним сховищем ----------
// Пароль SQL Server — параметр-секрет (dotnet user-secrets або запит у Dashboard), не у коді й не в репозиторії.
var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithDataVolume("cinema-platform-sql-data")          // дані переживають перезапуск
                 .WithLifetime(ContainerLifetime.Persistent);

// Імена баз = імена рядків підключення, які сервіси вже читають (ConnectionStrings:<ім'я>)
var bookingDb = sql.AddDatabase("CinemaBookingDb", databaseName: "CinemaBooking");   // Лаб. 02 (Проєкт №1)
var catalogDb = sql.AddDatabase("CatalogDb", databaseName: "CinemaCatalog");         // Лаб. 03 (Проєкт №2)

var mongo = builder.AddMongoDB("mongo")
                   .WithDataVolume("cinema-platform-mongo-data")      // окремий том для MongoDB
                   .WithLifetime(ContainerLifetime.Persistent);
var reviewsDb = mongo.AddDatabase("ReviewsDb", databaseName: "cinema_reviews");      // Лаб. 04 (Проєкт №3)

// ---------- Мікросервіси (логічні імена = адреси для service discovery) ----------
var booking = builder.AddProject<Projects.BookingService_Api>("booking-api")
    .WithReference(bookingDb).WaitFor(bookingDb)
    .WithEnvironment("Database__ApplyScriptsOnStartup", "true");      // порожня база → скрипти db/p1

var catalog = builder.AddProject<Projects.CatalogService_Api>("catalog-api")
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithEnvironment("Database__SeedOnStartup", "true");              // міграції EF Core і сідінг на старті

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
    .WithReference(reviewsDb).WaitFor(reviewsDb);                     // індекси й сідінг на старті

// ---------- Aggregator: паралельно опитує три сервіси ----------
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithReference(booking).WithReference(catalog).WithReference(reviews)
    .WaitFor(booking).WaitFor(catalog).WaitFor(reviews);

// ---------- Gateway: єдина точка входу на фіксованому порту 5000 ----------
builder.AddProject<Projects.Platform_ApiGateway>("gateway")
    .WithHttpEndpoint(port: 5000, name: "public")
    .WithExternalHttpEndpoints()
    .WithReference(booking).WithReference(catalog).WithReference(reviews)
    .WithReference(aggregator)
    .WaitFor(aggregator);

builder.Build().Run();
