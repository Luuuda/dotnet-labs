# Lab03: тришарова архітектура на EF Core (каталог фільмів)

## Що зроблено
- Domain: сутності `Movie`, `MovieDetails`, `Genre`, `MovieGenre`, `Hall`, `Screening` з бізнес-методами й перевірками.
- DAL: `CatalogDbContext`, Fluent API (окремий `IEntityTypeConfiguration` на сутність), міграція `InitialCreate` + SQL-скрипти, `HasData` і ідемпотентний програмний сідінг, async Generic Repository, Unit of Work, Eager / Explicit / LINQ M:N.
- BLL: DTO, AutoMapper, FluentValidation, специфікації `MoviesFilterSpec` і `ScreeningsFilterSpec`, сервіси.
- API: контролери (200/201/204/400/404/409), пагінація з метаданими, ProblemDetails, Serilog з CorrelationId і SQL-логами, Swagger.
- Тести: 32 (unit, інтеграційні з SQL Server, API).

## Як перевірити
1. `dotnet tool restore`
2. `dotnet ef database update --project src/CatalogService.Dal --startup-project src/CatalogService.Api`
3. `dotnet run --project src/CatalogService.Api` → http://localhost:5280/swagger
4. `dotnet test` (для тестів з базою — змінна `CATALOG_TEST_DB`)

## Знімки
`docs/swagger.png`, `docs/pagination-response.png`, `docs/validation-400.png`, `docs/sql-log-eager-vs-explicit.png`.
