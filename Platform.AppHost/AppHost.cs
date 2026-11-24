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

// ---------- Лаб. 06: Redis — L2 дворівневого кешу сервісів і кеш агрегованих відповідей ----------
// Рядок підключення передається як ConnectionStrings:redis; том зберігає кеш між перезапусками.
var redis = builder.AddRedis("redis")
                   .WithDataVolume("cinema-platform-redis-data")
                   .WithLifetime(ContainerLifetime.Persistent);

// ---------- Мікросервіси (логічні імена = адреси для service discovery) ----------
// Лаб. 06: кожен сервіс слухає два порти Kestrel (appsettings.json сервісу): REST (HTTP/1.1) для Gateway
// і "grpc" — лише HTTP/2 без TLS для Aggregator. Ендпоінти без проксі Aspire, щоб порт у Dashboard
// збігався з портом Kestrel; Aggregator знаходить gRPC-ендпоінт за адресою http://_grpc.<сервіс>.
var booking = builder.AddProject<Projects.BookingService_Api>("booking-api")
    .WithEndpoint("http", e => e.IsProxied = false)
    .WithHttpEndpoint(port: 5181, name: "grpc", isProxied: false)
    .WithReference(bookingDb).WaitFor(bookingDb)
    .WithReference(redis).WaitFor(redis)
    .WithEnvironment("Database__ApplyScriptsOnStartup", "true");      // порожня база → скрипти db/p1

var catalog = builder.AddProject<Projects.CatalogService_Api>("catalog-api")
    .WithEndpoint("http", e => e.IsProxied = false)
    .WithHttpEndpoint(port: 5281, name: "grpc", isProxied: false)
    .WithReference(catalogDb).WaitFor(catalogDb)
    .WithReference(redis).WaitFor(redis)                              // дворівневий кеш каталогу (L2)
    .WithEnvironment("Database__SeedOnStartup", "true");              // міграції EF Core і сідінг на старті

var reviews = builder.AddProject<Projects.Reviews_WebApi>("reviews-api")
    .WithEndpoint("http", e => e.IsProxied = false)
    .WithHttpEndpoint(port: 5381, name: "grpc", isProxied: false)
    .WithReference(reviewsDb).WaitFor(reviewsDb)
    .WithReference(redis).WaitFor(redis);                             // індекси й сідінг на старті

// ---------- Aggregator: паралельно опитує три сервіси ----------
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
    .WithReference(booking).WithReference(catalog).WithReference(reviews)
    .WithReference(redis).WaitFor(redis)                              // Лаб. 06: кеш агрегованих відповідей
    .WaitFor(booking).WaitFor(catalog).WaitFor(reviews);

// ---------- Gateway: єдина точка входу на фіксованому порту 5000 ----------
builder.AddProject<Projects.Platform_ApiGateway>("gateway")
    .WithHttpEndpoint(port: 5000, name: "public")
    .WithExternalHttpEndpoints()
    .WithReference(booking).WithReference(catalog).WithReference(reviews)
    .WithReference(aggregator)
    .WaitFor(aggregator);

builder.Build().Run();
