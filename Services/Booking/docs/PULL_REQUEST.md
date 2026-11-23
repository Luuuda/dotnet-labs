# Pull Request: Лаб. 02 — мікросервіс бронювання (ADO.NET + Dapper, Unit of Work, Web API, Aspire)

**Гілка:** `lab-02` → `main`

## Що зроблено
- **Domain:** моделі за таблицями Проєкту №1 з Лаб. 01; доменні винятки `NotFoundException`, `BusinessConflictException`, `ValidationException`.
- **DAL:**
  - `IGenericRepository<T>` і `BaseDapperRepository<T>` — спільний CRUD;
  - `CustomerRepository` на чистому ADO.NET (не успадковує generic);
  - 4 репозиторії на Dapper: multi-mapping, процедури з Лаб. 01;
  - `UnitOfWork`: одне з'єднання на запит, спільна транзакція;
  - `SqlErrorTranslator`: перетворює `SqlException` на доменні винятки.
- **BLL:**
  - DTO з DataAnnotations;
  - профілі AutoMapper (централізовано);
  - сервіси клієнтів, сеансів і бронювань;
  - транзакційні операції: створення бронювання (4 таблиці), підтвердження, оплата з ідемпотентністю.
- **API:**
  - тонкі контролери з атрибутною маршрутизацією, асинхронні дії з `CancellationToken`;
  - `DomainExceptionHandler` + ProblemDetails (`traceId`, `correlationId`);
  - Serilog з кореляційним Id;
  - Swagger.
- **Aspire:** AppHost з SQL Server у контейнері. API сам застосовує скрипти Лаб. 01 до порожньої бази.
- **Тести:** 28 тестів (unit з Moq, AutoMapper, інтеграційні DAL/UoW, API через WebApplicationFactory) — усі зелені.

## Як перевірити
1. `dotnet run --project Lab02-BookingService/src/BookingService.AppHost` — у Dashboard усі ресурси мають бути Running.
2. Swagger: http://localhost:5180/swagger.
3. Неіснуючий Id має дати 404, повторне підтвердження — 409, оплата непідтвердженого бронювання — 409 без збереженої оплати.
4. `verify.cmd` (або `dotnet test` зі змінною `CINEMA_TEST_DB`) — 28/28.
