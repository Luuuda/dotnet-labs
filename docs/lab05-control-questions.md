# Лаб. 05 — відповіді на контрольні питання

### 1. Яку роль виконують AppHost і ServiceDefaults?

**AppHost** (`Platform.AppHost/AppHost.cs`) — оркестратор для локальної розробки. У ньому лише опис ресурсів і зв'язків: контейнери SQL Server і MongoDB з томами, бази, три сервіси, Aggregator і Gateway. AppHost запускає все одним `dotnet run`, передає кожному сервісу рядки підключення й адреси інших сервісів через змінні середовища, задає порядок старту і піднімає Aspire Dashboard. Бізнес-логіки в ньому немає.

**ServiceDefaults** (`Platform.ServiceDefaults/Extensions.cs`) — спільна бібліотека, яку підключає кожен сервіс одним рядком `builder.AddServiceDefaults()`. Вона однаково налаштовує те, що потрібно всім: Serilog, OpenTelemetry (traces, metrics, logs) з експортом в Dashboard, health checks `/health` і `/alive`, service discovery, resilience для HttpClient і передачу CorrelationId. Без неї ці налаштування довелося б копіювати в кожен `Program.cs`.

### 2. Як працює service discovery і чому ім'я ресурсу можна використовувати як адресу?

Коли в AppHost пишемо `aggregator.WithReference(booking)`, Aspire передає Aggregator змінну середовища на кшталт `services__booking-api__http__0=http://localhost:5180`. Бібліотека `Microsoft.Extensions.ServiceDiscovery` (її вмикає ServiceDefaults) читає ці змінні з конфігурації. HttpClient з адресою `https+http://booking-api` або кластер YARP з адресою `http://booking-api` перед запитом питає resolver, і той підставляє реальну адресу й порт. Тому в коді немає жодного `localhost:port`: ім'я ресурсу — логічна адреса, а фізичну дає середовище (локально — Aspire, у Kubernetes — DNS сервісу). Схема `https+http` означає «спершу https, якщо його нема — http».

### 3. Яка різниця між WithReference() і WaitFor()?

`WithReference()` — про **конфігурацію**: ресурс отримує рядок підключення до бази (`ConnectionStrings__CatalogDb`) або адресу іншого сервісу для service discovery. `WaitFor()` — про **порядок запуску**: ресурс не стартує, доки залежність не перейде в стан Running/Healthy. Одне без іншого працює: з самим `WithReference` сервіс знатиме адресу бази, але може стартувати раніше за неї й упасти на міграціях; з самим `WaitFor` дочекається бази, але не знатиме, як до неї підключитися. У платформі порядок такий: бази → сервіси → Aggregator → Gateway.

### 4. Чим CorrelationId відрізняється від TraceId?

**TraceId** створює OpenTelemetry: 32 hex-символи, передається між сервісами заголовком `traceparent` (W3C) і зв'язує spans у дерево trace. Він технічний, нове значення з'являється на кожен вхідний запит без `traceparent`.

**CorrelationId** — бізнесовий ідентифікатор, який задає клієнт (`X-Correlation-Id: order-checkout-42`) або генерує Gateway. Він потрібен, щоб знайти всі логи однієї дії користувача, навіть коли дія складається з кількох HTTP-запитів або виходить за межі трасування (черги, зовнішні системи, звернення в підтримку). У платформі CorrelationId іде в `LogContext` (поле кожного запису логу), у тег span `correlation.id` і у відповідь; TraceId повертається в заголовку `X-Trace-Id`, щоб одразу відкрити trace в Dashboard.

### 5. Чому логи Serilog лише в консолі не з'являються у Structured logs?

Вкладка Structured logs показує тільки те, що прийшло по OTLP. Якщо Serilog пише лише в консольний sink, записи не потрапляють у `ILoggerProvider` OpenTelemetry і Dashboard бачить їх тільки як сирий текст у Console logs ресурсу. Тому в `ConfigureSerilog` стоїть `AddSerilog(..., writeToProviders: true)`: Serilog передає кожну подію й іншим провайдерам `Microsoft.Extensions.Logging`, зокрема провайдеру OpenTelemetry, який з `UseOtlpExporter()` відправляє їх у Dashboard разом з властивостями (`CorrelationId`, `ServiceName`, `BookingId` …), TraceId і SpanId.

### 6. Що таке span і trace? Як в одному trace опиняються spans різних сервісів?

**Span** — одна операція з початком і тривалістю: вхідний HTTP-запит, вихідний HTTP-виклик, SQL-команда, команда MongoDB або власна бізнес-операція (`AggregateBookingCard`, `PayBooking`). **Trace** — дерево spans однієї операції користувача з одним TraceId.

Spans різних сервісів потрапляють в один trace через **поширення контексту**: інструментація HttpClient додає до вихідного запиту заголовок `traceparent` з TraceId і id поточного span, а інструментація ASP.NET Core у сервісі-одержувачі читає його і створює свій span дочірнім. Так виходить ланцюжок gateway → aggregator → booking-api → SQL, який видно на скріні [trace-aggregate-booking.png](lab05/trace-aggregate-booking.png).

### 7. Чому Aggregator використовує Task.WhenAll і як має поводитися при відмові сервісу?

Виклики до різних сервісів незалежні, тож їх запускають одночасно й чекають усі разом: загальний час ≈ найдовший виклик, а не сума. `Task.WhenAll` не блокує потоки (на відміну від `.Result` чи `Task.WaitAll`), а `CancellationToken` скасовує всі виклики, якщо клієнт закрив з'єднання.

При відмові Aggregator не повертає 500. Обов'язкові дані (саме бронювання для картки бронювання) — якщо їх немає, відповідь 503 або 404, бо без них картка не має сенсу. Необов'язкові (рейтинг, відгуки) — кожен виклик обгорнуто в `SafeAsync`: помилку логують як Warning з тривалістю, додають подію до span, а клієнт отримує 200 з наявними даними і списком `warnings`. Таймаути й повтори задає `AddStandardResilienceHandler`, тож зависання одного сервісу не тримає весь запит. Приклад — [aggregate-booking-partial.json](lab05/aggregate-booking-partial.json).

### 8. Навіщо Gateway, якщо клієнт міг би звертатися до сервісів напряму?

* Одна адреса (`http://localhost:5000`) замість п'яти: клієнт не знає, скільки сервісів і де вони; сервіси можна ділити, переносити й масштабувати, не змінюючи клієнта.
* Наскрізні речі в одному місці: CorrelationId, логування маршрутизації, ліміти розміру тіла, таймаути, згодом автентифікація, CORS і rate limiting (Лаб. 06–07).
* Сервіси не потрібно виставляти назовні: зовнішній ендпоінт має тільки Gateway (`WithExternalHttpEndpoints`).
* Переписування шляхів і розв'язання конфліктів: `/api/screenings` належить каталогу, а копію сеансів у booking відкрито як `/api/booking/screenings` через transform.
* Балансування між репліками і service discovery на рівні YARP.
