var builder = DistributedApplication.CreateBuilder(args);

// SQL Server у контейнері; база створюється порожньою, а API на старті застосовує МІГРАЦІЇ EF Core
// і програмний сідінг. Ім'я ресурсу "CatalogDb" = ім'я рядка підключення в API.
var password = builder.AddParameter("sql-password", secret: true);
var catalogDb = builder.AddSqlServer("sql", password)
                       .WithDataVolume("cinema-catalog-sql-data")
                       .WithLifetime(ContainerLifetime.Persistent)
                       .AddDatabase("CatalogDb", databaseName: "CinemaCatalog");

builder.AddProject<Projects.CatalogService_Api>("catalog-api")
       .WithReference(catalogDb)
       .WaitFor(catalogDb);

builder.Build().Run();
