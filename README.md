# Платформа «Кінотеатр» на .NET Aspire

Андрющенко Людмила, група 443-2.
Лабораторна робота № 5: мікросервісна платформа з YARP Gateway, Aggregator і OpenTelemetry.
Лабораторна робота № 6: gRPC-виклики та дворівневе кешування (Memory + Redis) — **розділ [7](#7-лаб-06-grpc-і-дворівневе-кешування)**.

Кожна лабораторна робота лежить в окремій гілці від `main`: `lab-01` … `lab-06`. Гілка `lab-06` продовжує `lab-05`: три сервіси з попередніх лаб зібрано в одне рішення `Platform.slnx` і запущено разом через Aspire AppHost.

## 1. Предметна область

Мережевий кінотеатр. Глядачі переглядають афішу, бронюють і оплачують місця на сеанси, після перегляду пишуть відгуки. Контент-менеджери ведуть каталог фільмів, залів і розклад. Детальний опис контекстів і агрегатів — у [docs/lab01-README.md](docs/lab01-README.md).

| Сервіс (ресурс Aspire) | Лаба | Технології | База (рядок підключення) | Що вміє |
|---|---|---|---|---|
| `booking-api` — [Services/Booking](Services/Booking) | Лаб. 02 | ADO.NET + Dapper, N-шарова (Api/Bll/Dal/Domain) | SQL Server `CinemaBooking` (`CinemaBookingDb`) | клієнти, бронювання, оплата, локальна копія сеансів |
| `catalog-api` — [Services/Catalog](Services/Catalog) | Лаб. 03 | EF Core, N-шарова | SQL Server `CinemaCatalog` (`CatalogDb`) | фільми, жанри, зали, розклад сеансів |
| `reviews-api` — [Services/Reviews](Services/Reviews) | Лаб. 04 | MongoDB.Driver, Clean Architecture + MediatR | MongoDB `cinema_reviews` (`ReviewsDb`) | відгуки, коментарі, рейтинг фільму, обговорення |
| `aggregator` — [Platform.Aggregator](Platform.Aggregator) | Лаб. 05–06 | gRPC-клієнти (Лаб. 06), Task.WhenAll, Redis | — | композитні відповіді з трьох сервісів |
| `redis` | Лаб. 06 | Redis (контейнер Aspire, том) | `ConnectionStrings:redis` | L2 дворівневого кешу сервісів, кеш агрегованих відповідей |
| `gateway` — [Platform.ApiGateway](Platform.ApiGateway) | Лаб. 05 | YARP + service discovery | — | єдина точка входу `http://localhost:5000` |

## 2. Структура рішення

```
Platform.slnx
Platform.AppHost/          AppHost.cs — ресурси й зв'язки (бази, сервіси, Aggregator, Gateway)
Platform.ServiceDefaults/  Extensions.cs — Serilog, OpenTelemetry, health checks, service discovery, resilience
                           CorrelationId.cs — middleware + DelegatingHandler для X-Correlation-Id
Platform.ApiGateway/       YARP: маршрути в appsettings.json, логування маршрутизації
Platform.Aggregator/       Clients/ (typed HttpClients), Services/ (агрегація, часткові відмови), Controllers/
Services/Booking/          BookingService.Api | Bll | Dal | Domain | Tests
Services/Catalog/          CatalogService.Api | Bll | Dal | Domain | Tests
Services/Reviews/          Reviews.Domain | Application | Infrastructure | WebApi | Tests
db/p1  db/p2  db/p3        скрипти баз з Лаб. 01 (SQL Server ×2, MongoDB)
docs/                      ERD, приклади, скріни Лаб. 05, контрольні питання
```

### Що змінено в коді сервісів

Бізнес-логіка сервісів не змінювалася. Рядки підключення сервіси, як і раніше, читають через `GetConnectionString("<ім'я>")`, а резервні значення лишилися в `appsettings.json`; Aspire передає справжні змінними середовища `ConnectionStrings__<ім'я>`.

| Де | Що зроблено | Навіщо |
|---|---|---|
| `Program.cs` трьох сервісів | `builder.AddServiceDefaults()`, `app.UseCorrelationId()`, `app.MapDefaultEndpoints()`; прибрано власні `UseSerilog`, `UseSerilogRequestLogging` і middleware CorrelationId | інтеграція з платформою; власна конфігурація Serilog і CorrelationId дублювала ServiceDefaults |
| `appsettings*.json` сервісів | видалено секції `Serilog` | логування налаштовує ServiceDefaults |
| `*.Api` csproj | посилання на `Platform.ServiceDefaults` замість пакета Serilog | |
| `BookingService.Bll`, `CatalogService.Bll`, `Reviews.Application` | бізнес-логи з властивостями (`BookingId`, `Status`, `Amount`, `MovieId`, `ScreeningId`, `ReviewId`) і власні spans `ActivitySource` (`Cinema.Booking`, `Cinema.Catalog`, `Cinema.Reviews` — span на кожен запит MediatR) | вимога «логування бізнес-подій і власні spans» |
| Контролери (`*.Api/Controllers`, `Reviews.WebApi/Controllers`) | лог бізнес-події і теги (`booking.id`, `movie.id`, `review.id`) на span HTTP-запиту для операцій запису | те саме, на рівні API |
| `Reviews.Infrastructure/Persistence/MongoDbOptions` | гачок `ConfigureClientSettings`, через який `Program.cs` підписує `DiagnosticsActivityEventSubscriber` | MongoDB-spans у trace; клієнт Mongo створюється всередині Infrastructure, тож інакше підписатися неможливо |

Чутливі дані (email, телефон, номер картки) у логи й теги не пишуться.

## 3. Запуск платформи

Потрібно: .NET SDK 9 і 10, Docker Desktop (запущений).

```powershell
# один раз: пароль SQL Server — параметр-секрет AppHost (не зберігається в репозиторії)
dotnet user-secrets set "Parameters:sql-password" "<ваш-пароль>" --project Platform.AppHost

dotnet build Platform.slnx
dotnet run --project Platform.AppHost --launch-profile http
```

Якщо секрет не задано, Dashboard сам попросить ввести пароль. Те саме робить `docs/lab05/scripts/start-platform.cmd`.

| Що | Адреса |
|---|---|
| Aspire Dashboard (ресурси, traces, structured logs, metrics) | http://localhost:15500 |
| **API Gateway — єдина точка входу** | **http://localhost:5000** (список маршрутів: `GET /`) |
| Swagger сервісів (напряму, для налагодження) | booking `:5180`, catalog `:5280`, reviews `:5380`, aggregator `:5400` |
| gRPC сервісів (лише HTTP/2, для Aggregator) | booking `:5181`, catalog `:5281`, reviews `:5381` |

Порядок старту задано `WaitFor`: контейнери SQL Server, MongoDB і Redis → `booking-api`, `catalog-api`, `reviews-api` → `aggregator` → `gateway`.

### Як розгортаються бази

AppHost піднімає два контейнери з постійними томами (`cinema-platform-sql-data`, `cinema-platform-mongo-data`, `ContainerLifetime.Persistent`), тож дані переживають перезапуск:

* `CinemaBooking` — `booking-api` на старті виконує скрипти з [db/p1](db/p1) (`schema.sql` → `procedures.sql` → `seed.sql`, вбудовані ресурси Dal, ідемпотентні);
* `CinemaCatalog` — `catalog-api` застосовує міграції EF Core і програмний сідінг;
* `cinema_reviews` — `reviews-api` створює індекси й засіває колекції (`IHostedService`).

Окремо від Aspire бази можна розгорнути скриптом Лаб. 01: `db/deploy.ps1 -Tests` (або `db/run-deploy-tests.cmd`) — він піднімає `db/docker-compose.yml` і виконує `db/init.sql`, `db/p1/*`, `db/p2/*`, `db/p3/*`.

### Тести

```powershell
dotnet test Platform.slnx
```
Інтеграційні й API-тести з реальною БД пропускаються, якщо не задано змінні `CINEMA_TEST_DB`, `CATALOG_TEST_DB`, `REVIEWS_TEST_MONGO` (див. README сервісів). З базами Лаб. 01 (контейнери `cinema-mssql`, `cinema-mongo`) останній прогін: **110 з 110** (Booking 28, Catalog 32, Reviews 50).

### Docker-образи

`Platform.ApiGateway/Dockerfile` і `Services/Catalog/CatalogService.Api/Dockerfile` збираються з кореня репозиторію (потрібні `Directory.Build.props` і `Platform.ServiceDefaults`):

```powershell
docker build -f Platform.ApiGateway/Dockerfile -t cinema-gateway .
docker build -f Services/Catalog/CatalogService.Api/Dockerfile -t cinema-catalog-api .
```

## 4. Маршрути Gateway

Маршрути описано в [Platform.ApiGateway/appsettings.json](Platform.ApiGateway/appsettings.json). Адреси кластерів — логічні імена ресурсів (`http://booking-api` тощо), їх розв'язує service discovery (`AddServiceDiscoveryDestinationResolver`).

| Шлях | Сервіс | Примітка |
|---|---|---|
| `/api/aggregator/{**catch-all}` | aggregator | окремий маршрут, `Order: 1` |
| `/api/movies/{movieId:int}/rating`, `/api/movies/{movieId:int}/title` | reviews-api | `Order: 1`, щоб не потрапити в загальний `/api/movies` |
| `/api/movies/**`, `/api/screenings/**`, `/api/genres/**`, `/api/halls/**` | catalog-api | `Order: 10` |
| `/api/bookings/**`, `/api/customers/**` | booking-api | |
| `/api/booking/screenings/**` | booking-api | transform → `/api/screenings/**` (локальна копія сеансів) |
| `/api/reviews/**`, `/api/discussions/**` | reviews-api | ліміт тіла запиту 256 КБ |

Gateway пише для кожного запиту лог маршрутизації (метод, шлях, маршрут, кластер, адреса, статус, тривалість, розмір), а якщо сервіс недоступний — Warning з помилкою YARP.

### Приклади

```bash
curl http://localhost:5000/api/movies?pageSize=3
curl http://localhost:5000/api/bookings/1
curl http://localhost:5000/api/movies/3/rating
curl -H "X-Correlation-Id: demo-1" -i http://localhost:5000/api/aggregator/bookings/1/full
curl http://localhost:5000/api/aggregator/movies/3/full

curl -X POST http://localhost:5000/api/bookings -H "Content-Type: application/json" \
     -d '{"customerId":"6f1c2a10-0000-4000-8000-000000000002","screeningId":2,"seats":[{"row":5,"seat":3}],"channel":"Web"}'
```

Кожна відповідь містить `X-Correlation-Id` (переданий клієнтом або згенерований Gateway) і `X-Trace-Id` (id trace в Dashboard).

## 5. Aggregator

| Ендпоінт | Що збирає |
|---|---|
| `GET /api/aggregator/bookings/{id}/full` | бронювання (booking) → паралельно живі дані сеансів (catalog) і локальні копії сеансів (booking) → паралельно фільми (catalog), рейтинг і топ-відгуки (reviews) |
| `GET /api/aggregator/movies/{movieId}/full` | фільм, найближчі сеанси, рейтинг і відгуки — 4 виклики в `Task.WhenAll` |

* (Лаб. 05; з Лаб. 06 основний транспорт — gRPC, див. [7.4](#74-aggregator-на-grpc)) Typed HttpClients з адресами `https+http://booking-api` тощо; `CancellationToken` передається до кожного виклику; `.Result`/`Task.WaitAll` не використовуються.
* Resilience (`AddStandardResilienceHandler`): спроба 2 с, 1 повтор, загалом 6 с, circuit breaker.
* Кожен виклик логується з тривалістю (`"reviews-api" "рейтинг фільму 3" — відповідь за 97 мс`).
* **Часткові відмови:** якщо обов'язковий сервіс (booking для картки бронювання) недоступний — 503; якщо недоступний необов'язковий — відповідь 200 з наявними даними та списком `warnings`, без 500.
* Композитний DTO у camelCase, null-поля не серіалізуються.

Приклад повної відповіді: [docs/lab05/aggregate-booking.json](docs/lab05/aggregate-booking.json); часткової (reviews-api зупинено): [docs/lab05/aggregate-booking-partial.json](docs/lab05/aggregate-booking-partial.json).

## 6. Спостережуваність

* **Serilog** (у ServiceDefaults): збагачення `ServiceName`, `Environment`, `MachineName`, `ThreadId`, `FromLogContext`; рівні Debug у Development / Information в інших середовищах; консоль у `CompactJsonFormatter`; `writeToProviders: true`, тож ті самі логи йдуть через OpenTelemetry у Dashboard.
* **CorrelationId:** middleware бере `X-Correlation-Id` з запиту або генерує новий, кладе в `LogContext` і тег span; `CorrelationIdHandler` (DelegatingHandler) передає його далі у вихідні HTTP-запити.
* **OpenTelemetry:** ASP.NET Core, HttpClient, SqlClient, MongoDB (`DiagnosticsActivityEventSubscriber`), Runtime; власні джерела `Cinema.*`; експорт `UseOtlpExporter()` в Aspire Dashboard.

| Скрін | Що видно |
|---|---|
| ![Ресурси](docs/lab05/dashboard-resources.png) | усі ресурси в стані Running |
| ![Помилок немає](docs/lab05/check-errors-before-stop.png) | Structured logs з рівнем Error після чистого старту (без томів) і всього сценарію: жодного запису |
| ![Trace агрегувального запиту](docs/lab05/trace-aggregate-booking.png) | один trace: gateway → aggregator → booking/catalog/reviews → SQL і MongoDB; паралельні виклики перекриваються |
| ![Логи за CorrelationId](docs/lab05/logs-by-correlation-id.png) | Structured logs з фільтром `CorrelationId = demo-aggregate-booking`: записи всіх сервісів одного запиту |
| ![Часткова відповідь](docs/lab05/trace-partial-failure.png) | reviews-api зупинено: виклики до нього червоні, а запит усе одно 200 |
| ![Логи часткової відмови](docs/lab05/logs-partial-failure.png) | Warning «reviews-api недоступний…» і картка з 2 попередженнями |
| ![reviews-api зупинено](docs/lab05/dashboard-resources-reviews-stopped.png) | стан ресурсів під час демонстрації часткової відмови |

Сценарій, яким зроблено скріни, — [docs/lab05/scripts/demo.js](docs/lab05/scripts/demo.js), його вивід — [docs/lab05/demo-output.txt](docs/lab05/demo-output.txt).

## 7. Лаб. 06: gRPC і дворівневе кешування

### 7.1. Де що лежить

| Що | Де |
|---|---|
| Redis в AppHost: `AddRedis("redis").WithDataVolume(...)`, сервіси й Aggregator — `WithReference(redis).WaitFor(redis)` | [Platform.AppHost/AppHost.cs](Platform.AppHost/AppHost.cs) |
| Контракти gRPC (proto3, `package`, `csharp_namespace`) | [catalog.proto](Services/Catalog/CatalogService.Api/Protos/catalog.proto), [booking.proto](Services/Booking/BookingService.Api/Protos/booking.proto), [reviews.proto](Services/Reviews/Reviews.WebApi/Protos/reviews.proto) |
| gRPC-сервіси (адаптери над BLL / MediatR, RpcException з правильними кодами) | `Services/*/<Сервіс>.Api/Grpc/` (`CatalogGrpcService`, `BookingGrpcService`, `ReviewsGrpcService`) |
| `AddGrpc` + серверний interceptor логування, `MapGrpcService<T>()` | [ServiceDefaults/Grpc](Platform.ServiceDefaults/Grpc), `Program.cs` сервісів |
| `AddMemoryCache` зі `SizeLimit`, `AddRedisDistributedCache("redis")` | `Program.cs` сервісів |
| Дворівневий кеш L1 → L2 → БД, ключі, TTL, метрики, health check, прогрівання | `Caching/` у [CatalogService.Bll](Services/Catalog/CatalogService.Bll/Caching), [BookingService.Bll](Services/Booking/BookingService.Bll/Caching), [Reviews.Application](Services/Reviews/Reviews.Application/Caching) |
| Інвалідація при записі | `MovieService`, `ScreeningService` (Catalog.Bll), `BookingsService`, `ScreeningService` (Booking.Bll), команди `Reviews.Application/Reviews/Commands` |
| gRPC-клієнти Aggregator (`AddGrpcClient`, `GrpcServices="Client"`), паралельні виклики, часткові відмови, кеш агрегованих відповідей | [Platform.Aggregator](Platform.Aggregator) (`Program.cs`, `Clients/GrpcServiceSources.cs`, `Services/PartialResults.cs`, `Services/AggregateCache.cs`) |
| Meter-и кешу й gRPC у експорті OTLP | [ServiceDefaults/Extensions.cs](Platform.ServiceDefaults/Extensions.cs) (`CacheMeters`, `GrpcMeters`) |

### 7.2. Дворівневий кеш

```
GET /api/movies/3 ──► IMemoryCache (L1, процес) ──промах──► Redis (L2, спільний) ──промах──► SQL Server
                          ▲  заповнюється на зворотному шляху  ◄──────────────────────────────┘
```

* **Cache-aside**: `ITwoLevelCache.GetOrCreateAsync(key, policy, factory)`; REST-контролер і gRPC-сервіс викликають той самий BLL, тож кеш спільний для обох протоколів. У Reviews кешування зроблено `CachingBehavior` у pipeline MediatR для запитів `ICacheableQuery`.
* **Ключі** з префіксом сервісу: `catalog:movie:{id}`, `catalog:screening:{id}`, `catalog:genres`, `booking:screening:{id}`, `booking:booking:{id}`, `reviews:movie:{id}:rating`, `reviews:review:{id}`. Списки з фільтрами — через ключ-версію: `catalog:movies:list:v{версія}:{хеш параметрів}`.
* **L1** (`IMemoryCache`): `SizeLimit` (1 одиниця = 1 КБ JSON), `CompactionPercentage = 0.2`, пріоритети (довідники — High, списки — Low), sliding + absolute expiration. **L2** (Redis): DTO у JSON (System.Text.Json), absolute TTL. **L1 завжди коротший за L2**:

  | Політика | L1 | L2 | Дані |
  |---|---|---|---|
  | reference | 10 хв | 1 год | жанри, зали, копії сеансів у booking |
  | card | 45 с sliding / 2 хв | 10 хв | картка фільму, сеанс |
  | rating | 30 с sliding / 1 хв | 5 хв | рейтинг фільму (aggregation pipeline) |
  | list | 30 с | 2 хв | сторінки списків |
  | personal | 10 с | 30 с | картка бронювання (email, телефон — лише короткий TTL) |
* **Інвалідація** синхронно після `SaveChanges` / коміту: картка + версія списків; причина в лозі (`Cache invalidated ["catalog:movie:3"], reason MovieUpdated`) і в метриці `cache.invalidations`.
* **Збій Redis** не ламає запит: помилка → Warning, `cache.errors`, запит іде в БД, L2 пропускається 30 с (щоб не чекати тайм-аут на кожному запиті). Тайм-аути клієнта Redis — 1–2 с.
* **Прогрівання**: `CacheWarmupService` (BackgroundService) завантажує жанри, зали й першу сторінку афіші на старті й раз на 10 хв; `DistributedCacheWarmup` відкриває з'єднання з Redis під час старту сервісу.
* **Статистика L1** — у `/health` (`catalog-cache`, `booking-cache`, `reviews-cache`: записи, розмір, попадання, промахи, hit ratio).
* **Порівняння з HybridCache** (`Microsoft.Extensions.Caching.Hybrid`): робить те саме L1 + L2 і додатково захищає від одночасного перерахунку одного ключа (stampede) та підтримує теги для інвалідації груп. Ручна реалізація тут — щоб явно показати кроки й мати власні метрики L1/L2; ключ-версія замінює теги.

### 7.3. gRPC

* Сервіси слухають **два порти Kestrel** (`appsettings.json` → `Kestrel:Endpoints`): REST — HTTP/1.1 для Gateway, gRPC — лише `Http2` (h2c без TLS). В AppHost це іменований ендпоінт `grpc`; Aggregator звертається за адресою `http://_grpc.<сервіс>` (service discovery вибирає саме його).
* Методи для Aggregator — насамперед читання, зокрема пакетні (`GetMoviesByIds`, `GetScreeningsByIds`, `GetRatingsForMovies`). Гроші — `Money { units, nanos }`, час — `google.protobuf.Timestamp`, необов'язкові поля — `optional`.
* Валідація входу (`Id <= 0` → `InvalidArgument`), доменні винятки → `NotFound` / `FailedPrecondition`, решта → `Internal` з логом. Перевірено тестами `GrpcServiceTests` і запитом до неіснуючого фільму (Aggregator отримує `NOT_FOUND` і повертає 404).
* Серверний interceptor логує метод, peer, статус, тривалість і CorrelationId структурованими властивостями; помилки — Warning / Error зі статусом.
* Тест `Changing_field_number_on_one_side_breaks_reading`: «клієнт» із `title = 20` замість `2` перестає читати назву фільму (перевірка «змініть номер поля лише на сервері»).

### 7.4. Aggregator на gRPC

* `AddGrpcClient<…GrpcClient>` для кожного сервісу (+ клієнтський interceptor). Клієнти створює та сама фабрика HttpClient, тож діють service discovery, resilience і `CorrelationIdHandler` з ServiceDefaults — `X-Correlation-Id` іде в метадані кожного gRPC-виклику.
* Незалежні виклики — `Task.WhenAll`; `CancellationToken` і дедлайн 3 с у кожному виклику; `RpcException` `Unavailable` / `DeadlineExceeded` → попередження у `warnings`, а не 500.
* Кеш агрегованих відповідей у Redis: `aggregator:booking:{id}:full`, `aggregator:movie:{id}:full`, TTL 30–45 с (коротший за кеші сервісів), перевірка **до** gRPC-викликів, часткові відповіді не кешуються. Заголовок відповіді `X-Cache: HIT | MISS`; `Cache-Control: no-cache` — зібрати заново.
* HTTP-клієнти Лаб. 05 лишилися для порівняння: `?transport=http`.
* Новий ендпоінт **афіші** `GET /api/aggregator/movies/board?ids=1,2,3` — кілька фільмів з рейтингами двома пакетними gRPC-викликами.

```bash
curl -H "X-Correlation-Id: demo-grpc" -i http://localhost:5000/api/aggregator/bookings/1/full       # gRPC + кеш Redis
curl -H "Cache-Control: no-cache" "http://localhost:5000/api/aggregator/bookings/1/full?transport=http"   # HTTP (Лаб. 05)
curl "http://localhost:5000/api/aggregator/movies/board?ids=1,2,3,4,5,6,7"
```

### 7.5. Перевірки «Перед здачею» і скріни

Сценарій — [docs/lab06/scripts/demo.js](docs/lab06/scripts/demo.js), вивід — [docs/lab06/demo-output.txt](docs/lab06/demo-output.txt).

| Перевірка | Результат | Скрін |
|---|---|---|
| Двічі поспіль той самий фільм | 1-й запит 350 мс з SQL-span; 2-й — 16 мс, лише `cache get card` (hit-L1), **SQL-span немає** | [перший](docs/lab06/trace-movie-first-sql.png), [повторний](docs/lab06/trace-movie-repeat-no-sql.png) |
| PUT фільму → GET | GET одразу повертає новий опис; у логах `Cache invalidated … reason MovieUpdated` | [логи](docs/lab06/logs-cache-invalidation.png) |
| Зупинено Redis | `GET /api/movies/2` — 200 за 1,6 с (Warning «Redis недоступний… працюю з L1 і БД»), наступні — 8 мс; Aggregator — 200 | [логи](docs/lab06/logs-redis-down.png) |
| Агрегувальний запит через gRPC | один trace: gateway → aggregator → gRPC-spans клієнта (`HTTP POST`) і сервера (`POST /cinema.*.v1.*Grpc/*`) → кеш / SQL / MongoDB | [trace](docs/lab06/trace-aggregate-grpc.png) |
| CorrelationId у метаданих gRPC | один CorrelationId у логах gateway, aggregator і трьох сервісів, у тому числі в записах interceptor-а | [логи](docs/lab06/logs-grpc-correlation-id.png) |
| Метрики кешу | `cache.hits` / `cache.misses` з тегами `cache.level` (L1, L2) і `cache.policy` | [hits](docs/lab06/metrics-cache-hits.png), [misses](docs/lab06/metrics-cache-misses.png) |
| Зупинено catalog-api | 200 з даними booking і reviews та двома `warnings` | [JSON](docs/lab06/aggregate-partial-catalog-down.json), [trace](docs/lab06/trace-partial-catalog-down.png) |
| Кеш агрегованої відповіді | 1-й запит MISS 23 мс, 2-й HIT 7 мс без викликів сервісів | [trace](docs/lab06/trace-aggregate-cache-hit.png) |
| Ресурси | усі Running, у сервісів по два ендпоінти (REST і gRPC) | [ресурси](docs/lab06/dashboard-resources.png) |

### 7.6. Порівняння затримок HTTP (Лаб. 05) і gRPC (Лаб. 06)

Через Gateway, кеш Aggregator вимкнено (`Cache-Control: no-cache`), кеші сервісів прогріті, по 40 запитів на транспорт по черзі, мс ([JSON](docs/lab06/latency-http-vs-grpc.json)):

| Сценарій | Викликів сервісів (HTTP / gRPC) | HTTP mean / median / p95 | gRPC mean / median / p95 |
|---|---|---|---|
| Афіша з 7 фільмів (`movies/board`) | 14 / **2** | 8,4 / 7,8 / 11,3 | **7,8 / 7,0 / 8,1** |
| Картка бронювання | 6 / 6 | 11,0 / 10,6 / 18,2 | 13,0 / 12,3 / 18,1 |
| Картка фільму | 4 / 4 | 6,9 / 7,0 / 8,7 | 7,3 / 7,3 / 8,9 |
| 16 паралельних клієнтів, картка бронювання | | 28,7 / 27,2 / 45,2 (548 RPS) | 35,4 / 34,7 / 50,4 (447 RPS) |

Traces: [афіша gRPC](docs/lab06/trace-board-grpc.png) (2 виклики) проти [афіші HTTP](docs/lab06/trace-board-http.png) (14 викликів); [картка gRPC](docs/lab06/trace-aggregate-grpc.png) проти [картки HTTP](docs/lab06/trace-aggregate-http.png).

**Висновок.** gRPC швидший там, де контракт дозволяє зменшити кількість викликів: афіша — на 7 % за середнім і на 28 % за p95, бо 14 HTTP-запитів замінено двома пакетними RPC. Для карток з однаковою кількістю викликів на одній машині (loopback, мікросекундні мережеві затримки, відповіді по 1–2 КБ, дані вже в кеші) gRPC на 0,4–2 мс повільніший, а під паралельним навантаженням дає на 18 % менше RPS. Причина: накладні витрати HTTP/2 (потоки, flow control, HPACK) і gRPC-конвеєра на кожен виклик більші за виграш від protobuf на таких малих повідомленнях. Переваги gRPC ростуть із реальною мережевою затримкою між машинами (одне постійне з'єднання, без нових TCP-рукостискань), з великими відповідями (бінарний формат компактніший за JSON) і з пакетними та streaming-методами. Перший запит після старту повільніший на обох транспортах ([холодний trace gRPC](docs/lab06/trace-aggregate-grpc-cold.png)): відкриваються з'єднання HTTP/2 і Redis.

### 7.7. Тести

`dotnet test Platform.slnx` — 127 тестів, усі проходять (з базами Лаб. 01). Нові для Лаб. 06: `TwoLevelCacheTests` (L1-hit без БД, L2-hit заповнює L1, інвалідація, збій Redis, «не знайдено» не кешується, версії списків, L1 < L2), `GrpcServiceTests` (NotFound / InvalidArgument, Money і Timestamp, зміна номера поля), `CachingTests` для Reviews (CachingBehavior, інвалідація рейтингу).

Відповіді на контрольні питання — [docs/lab06-control-questions.md](docs/lab06-control-questions.md).

## 8. Політика дублювання даних

Кожне поле має одного власника; інші сервіси зберігають знімок або копію (з Лаб. 01).

| Поле | Де зберігається | Власник | Політика |
|---|---|---|---|
| `Tickets.MovieTitle`, `HallName`, `StartsAt`, `UnitPrice` | CinemaBooking | Каталог | знімок на момент бронювання, не оновлюється |
| `Screenings.*` (MovieId, MovieTitle, HallName, StartsAt, BasePrice, IsActive …) | CinemaBooking | Каталог | копія; оновлюватиметься подіями `ScreeningChanged` тощо (Лаб. 08) |
| `reviews.movieTitle`, `discussions.movieTitle` | cinema_reviews | Каталог | копія, оновлюється подією `MovieRenamed` |
| `author.displayName` | cinema_reviews | Бронювання (Customers.FullName) | копія, оновлюється подією `CustomerRenamed` |
| `reviews.verifiedViewer` | cinema_reviews | Бронювання | знімок факту перегляду |
| `Movies.AverageRating`, `RatingsCount` | CinemaCatalog | Відгуки | копія, оновлюється подією `MovieRatingChanged` |

Aggregator нічого не зберігає: він читає актуальні дані у власників і показує розбіжності (наприклад, сеанс скасовано в каталозі, а в копії booking — ні) як `warnings`.

## 9. Документи

* [docs/lab06-control-questions.md](docs/lab06-control-questions.md) — відповіді на контрольні питання Лаб. 06;
* [docs/lab05-control-questions.md](docs/lab05-control-questions.md) — відповіді на контрольні питання Лаб. 05;
* [docs/lab01-README.md](docs/lab01-README.md), [docs/lab01-control-questions.md](docs/lab01-control-questions.md) — бази даних (Лаб. 01);
* README сервісів: [Booking](Services/Booking/README.md), [Catalog](Services/Catalog/README.md), [Reviews](Services/Reviews/README.md).
