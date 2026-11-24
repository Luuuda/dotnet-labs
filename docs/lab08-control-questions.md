# Лаб. 08 — відповіді на контрольні питання

Приклади — з цієї платформи: Каталог (`catalog-api`) публікує події в exchange `catalog-events`, Бронювання (`booking-api`) і Відгуки (`reviews-api`) оновлюють свої копії. У термінах завдання: «Products у Замовленнях» — це копія сеансів `dbo.Screenings` у базі CinemaBooking, «productName у Відгуках» — `movieTitle` у документах `reviews` і `discussions`.

## 1. Яка різниця між синхронними gRPC-викликами та асинхронною подієвою комунікацією? Коли доречний кожен варіант?

**gRPC (Лаб. 06)** — запит-відповідь. Викликач чекає результату, обидва сервіси мають працювати одночасно, і збій сервера видно відразу як помилку виклику. Тому Aggregator потребує Polly: таймаутів, повторів і circuit breaker (Лаб. 07).

**Подія (Лаб. 08)** — повідомлення про факт, що вже стався (`ScreeningPriceChangedEvent`). Publisher кладе її в брокер і не знає, хто її отримає та коли. Споживач може бути зупинений: подія чекає в durable-черзі. Сервіси розв'язані в часі (temporal decoupling) і за складом (новий споживач лише додає свою чергу, Каталог не змінюється). Ціна цього — eventual consistency і складніша обробка помилок: збій «тихий», клієнта, який побачив би помилку, немає.

| Коли | Що обрати | Приклад у платформі |
|---|---|---|
| потрібна відповідь зараз, щоб показати її користувачу | gRPC / HTTP | Aggregator збирає картку фільму з трьох сервісів |
| потрібна перевірка перед дією (чи вільне місце) | синхронний виклик або локальна копія | бронювання перевіряє місця в копії `Screenings` |
| повідомити інші сервіси, що дані змінилися | подія | `ScreeningPriceChanged`, `MovieRenamed` |
| кілька незалежних реакцій на один факт | подія (fan-out) | `MovieRenamed` → Бронювання і Відгуки |
| довгі процеси, які мають пережити перезапуск | подія | — |

## 2. Що таке eventual consistency? Як події узгоджують копії даних, визначені в політиці дублювання з Лаб. 01?

Eventual consistency (узгодженість у кінцевому підсумку): після зміни в сервісі-власнику копії в інших сервісах деякий час можуть бути старими, але без нових змін вони гарантовано стануть такими самими. У Лаб. 01 для кожного дубльованого поля визначено власника і політику (README, розділ 10):

* `Screenings.BasePrice`, `IsActive`, `MovieTitle` у CinemaBooking — копія, власник Каталог. `ScreeningService.ChangePriceAsync` → `SaveChangesAsync` → `ScreeningPriceChangedEvent` → `ScreeningPriceChangedHandler` оновлює `BasePrice`. На демонстрації нова ціна з'являється в Бронюванні за **~10–30 мс**.
* `Tickets.UnitPrice`, `MovieTitle` — **знімок**: подія їх не змінює, оформлене бронювання зберігає ціну на момент покупки.
* `reviews.movieTitle`, `discussions.movieTitle` — копія; `MovieRenamedEvent` → `SyncMovieTitleCommand` → `updateMany` в обох колекціях.

Щоб копія таки стала узгодженою, потрібні: durable-черги й persistent-повідомлення (подія не губиться при перезапуску), ручний ack (повідомлення не зникає до успішної обробки), ідемпотентність (повтор не шкодить) і захист від застарілих подій. Для останнього в Бронюванні є `PriceSyncedAt` / `TitleSyncedAt`, а у Відгуках — колекція `movieTitleSync`: подія, старша за вже застосовану зміну, не перезапише свіжі дані.

## 3. Чому domain events мають бути незмінними і самодостатніми?

* **Незмінні** (`record` з `init`-властивостями, [Platform.Messaging/IntegrationEvent.cs](../Platform.Messaging/IntegrationEvent.cs)): подія описує факт, що вже стався, тому й назви в минулому часі. Факт не можна «виправити»: якщо ціну змінили ще раз, це нова подія з новим `EventId`. Незмінність дає ще й безпеку. Ту саму подію серіалізують, логують, повторюють і відправляють у DLQ, і всюди вона однакова. `EventId` стабільний, тож за ним працює ідемпотентність.
* **Самодостатні**: `ScreeningScheduledEvent` містить усе, що потрібно Бронюванню, — фільм, назву, залу, розмір зали, час і ціну. Якби подія мала лише `ScreeningId`, споживач мусив би синхронно звертатися до Каталогу. Тоді повернулася б часова залежність, від якої подія мала звільнити: Каталог недоступний — обробка стоїть. А ще за час між подією і зверненням дані могли змінитися, і споживач прочитав би не той стан, про який подія.

## 4. Як працює publish-subscribe у RabbitMQ? Яка роль exchange, queue і binding?

* **Exchange** — точка, куди publisher надсилає повідомлення з routing key; сам повідомлень не зберігає. `catalog-events` — durable **topic** exchange, Каталог публікує в нього з ключами `screening.scheduled`, `screening.price_changed`, `screening.cancelled`, `movie.renamed` і нічого не знає про споживачів.
* **Queue** — буфер повідомлень одного сервісу-споживача: `booking.catalog-sync`, `reviews.catalog-sync` (durable). Кілька екземплярів одного сервісу читають ту саму чергу й ділять повідомлення між собою (competing consumers). Різні сервіси мають різні черги й отримують кожен свою копію (fan-out).
* **Binding** — правило «exchange → черга» з шаблоном ключа (`*` — одне слово, `#` — будь-яка кількість). `booking.catalog-sync` прив'язана шаблонами `screening.*` і `movie.*`, `reviews.catalog-sync` — лише `movie.*`, тому події сеансів у Відгуки не потрапляють.

Топологію оголошує кожен сервіс під час старту ([EventTopology.cs](../Platform.Messaging/EventTopology.cs)). Повторне оголошення з тими самими параметрами безпечне, тож порядок запуску сервісів не важливий. Скріни: [exchanges](lab08/rabbitmq-exchanges.png), [bindings catalog-events](lab08/rabbitmq-catalog-events-bindings.png), [черги](lab08/rabbitmq-queues.png).

## 5. Що таке ідемпотентність і чому вона обов'язкова для consumers?

Ідемпотентна обробка: повторна обробка того самого повідомлення дає той самий стан, що й одна. RabbitMQ гарантує доставку **щонайменше один раз** (at-least-once). Повідомлення приходить повторно, якщо споживач упав після зміни БД, але до `BasicAck`, якщо обірвалося з'єднання (непідтверджені повідомлення повертаються в чергу), а також після нашого повтору, коли копію вже опубліковано, а ack оригіналу не встиг. Без ідемпотентності повтор, наприклад, двічі збільшив би лічильник.

Реалізація:

* **Бронювання** ([CatalogSyncHandlerBase.cs](../Services/Booking/BookingService.Bll/IntegrationEvents/Handlers/CatalogSyncHandlerBase.cs)): `BEGIN TRAN → INSERT dbo.ProcessedEvents(EventId) → UPDATE dbo.Screenings → COMMIT`. `EventId` — PRIMARY KEY, тож повтор дає помилку 2627, і `TryAddAsync` повертає `false`. Перевірка й запис — одна атомарна операція, тож дві паралельні доставки не пройдуть обидві. Якщо оновлення впало, відкочується і запис `EventId`, і повтор обробить подію заново.
* **Відгуки** ([SyncMovieTitle.cs](../Services/Reviews/Reviews.Application/Movies/Commands/SyncMovieTitle.cs)): MongoDB standalone, без транзакцій. Тому спершу йде сама зміна, яка ідемпотентна за природою (`updateMany` з фільтром `movieTitle ≠ нова назва`), а вже потім запис у `processedEvents` (`_id = EventId`).

На демонстрації ту саму подію опубліковано двічі, у другій копії тіло мало ціну 999. Друга обробка в логах — `Duplicate … пропущено`, ціна не змінилася ([логи](lab08/logs-duplicate.png)).

## 6. Як гарантувати, що подія буде опублікована лише після успішного збереження в БД і не загубиться? Що таке Transactional Outbox?

Перша половина в коді: у кожній бізнес-операції Каталогу спершу `await uow.SaveChangesAsync(ct)`, лише потім `await events.PublishAsync(...)` ([ScreeningService.cs](../Services/Catalog/CatalogService.Bll/Services/ScreeningService.cs), [MovieService.cs](../Services/Catalog/CatalogService.Bll/Services/MovieService.cs)). Якщо збереження кинуло виняток (409 конкурентності, порушення правила), до публікації виконання не доходить. Це перевіряє тест `Failed_save_publishes_nothing`. Publisher чекає підтвердження брокера (publisher confirms), повідомлення persistent, exchange і черги durable: прийнята брокером подія переживе його перезапуск.

Залишається вікно між COMMIT і публікацією: якщо процес упаде або брокер недоступний саме тоді, зміна збережена, а подію втрачено. Зараз така помилка логується з `EventId`, типом, routing key і `CorrelationId` ([AfterCommitEventPublisher.cs](../Services/Catalog/CatalogService.Bll/IntegrationEvents/AfterCommitEventPublisher.cs)), а клієнт отримує 200, бо зміна справді відбулася.

**Transactional Outbox** закриває це вікно. Подія пишеться в таблицю `OutboxMessages` у **тій самій транзакції БД**, що й зміна: або обидва записи є, або жодного. Окремий фоновий процес (relay) читає неопубліковані записи, публікує їх у брокер і позначає опублікованими. Якщо він упаде після публікації, але до позначки, подія піде ще раз. Це той самий at-least-once, який вже покриває ідемпотентність споживачів. Так зроблено в eShop (`IntegrationEventLogEF`) і в MassTransit (EF Core outbox). У платформі outbox не реалізовано, це наступний крок.

## 7. Що таке Dead Letter Exchange і коли повідомлення потрапляє туди?

DLX — звичайний exchange, у який RabbitMQ перекладає «мертве» повідомлення з черги, що має аргумент `x-dead-letter-exchange`. Повідомлення «вмирає», коли споживач відхилив його без повернення (`BasicNack(requeue: false)` / `BasicReject`), коли минув TTL або коли переповнена черга з лімітом довжини. Брокер додає заголовок `x-death`: з якої черги, причина (`rejected`), скільки разів.

У платформі DLX `catalog-events.dlx` — **direct**, а кожна черга передає своє ім'я як `x-dead-letter-routing-key`. Тому відхилене Бронюванням повідомлення потрапляє лише в `booking.catalog-sync.dlq`. Із fanout-DLX воно з'явилося б і в DLQ Відгуків. Споживач відхиляє повідомлення в двох випадках:

1. постійна помилка: некоректний JSON, невідомий тип, несумісна `x-schema-version`, невалідні дані (`PermanentMessageException`) — одразу, без повторів;
2. тимчасова помилка, після якої вичерпано 3 повтори.

Глибина DLQ — метрика `messaging.dlq.depth` (ObservableGauge), а коли в DLQ з'являються нові повідомлення, у лог іде Warning. Скріни: [повідомлення в DLQ з x-death](lab08/rabbitmq-dlq-message.png), [логи Nack → DLQ](lab08/logs-dlq-malformed.png).

## 8. Чому для підрахунку повторів повідомлення публікується заново, а не повертається в чергу через requeue?

`BasicNack(requeue: true)` повертає **те саме** повідомлення в голову черги, і змінити його заголовки не можна. Звідси дві проблеми:

* немає лічильника: споживач не знає, яка це спроба (`redelivered` — лише прапорець «вже доставлялося»), тож не може зупинитися після N спроб;
* немає паузи: повідомлення миттєво приходить знову, і «отруйне» повідомлення (poison message) крутиться в нескінченному циклі, навантажуючи процесор, логи й БД і блокуючи чергу.

Тому споживач ([IntegrationEventConsumer.cs](../Platform.Messaging/IntegrationEventConsumer.cs)) після паузи 1 → 2 → 4 с публікує **копію** з `x-retry-count + 1` через default exchange прямо у свою чергу. Не в `catalog-events`, бо тоді повтор отримали б і інші сервіси. Оригінал підтверджується (`BasicAck`). Коли `x-retry-count` досяг 3 — `BasicNack(requeue: false)` → DLQ. `MessageId = EventId` у копії той самий, тому ідемпотентність працює і для повторів. На демонстрації (SQL Server призупинено) — `Retry 1/3`, `2/3`, `3/3`, потім успішна обробка ([логи](lab08/logs-retry.png)).

## 9. Як контекст трасування передається через брокер повідомлень?

Так само, як через HTTP: заголовком W3C `traceparent` (+ `tracestate`, `baggage`), тільки в заголовках AMQP-повідомлення ([TracePropagation.cs](../Platform.Messaging/TracePropagation.cs)).

1. Publisher створює span `catalog-events publish` (`ActivityKind.Producer`, теги `messaging.system=rabbitmq`, `messaging.destination.name`, `messaging.rabbitmq.destination.routing_key`, `messaging.message.id`). `Propagators.DefaultTextMapPropagator.Inject` записує його контекст у `BasicProperties.Headers`.
2. Споживач читає заголовки (значення приходять як `byte[]`), `Extract` відновлює `ActivityContext` і baggage (у ньому `CorrelationId`). Span `booking.catalog-sync process` (`ActivityKind.Consumer`) стає нащадком span публікації.
3. `ActivitySource "Platform.Messaging"` підключено в ServiceDefaults (`.AddSource("Platform.Messaging")`), далі SQL / MongoDB / Redis-spans обробки потрапляють у той самий trace.

У Dashboard один trace: `gateway PUT /api/movies` → `catalog-api UpdateMovie` → `MSG rabbitmq catalog-events` → `booking-api booking.catalog-sync` → `DATA mssql CinemaBooking` і паралельно `reviews-api reviews.catalog-sync` → `MediatR SyncMovieTitleCommand` → `DATA mongodb update reviews/discussions`. Це 7 ресурсів ([скрін](lab08/trace-movie-renamed-e2e.png)). Окремо передається бізнесовий `x-correlation-id`, за яким фільтруються логи всіх трьох сервісів.

## 10. Чим тимчасовий збій (retry) відрізняється від постійного (DLQ)?

* **Тимчасовий** — причина поза повідомленням і минеться сама: БД недоступна або перевантажена, таймаут, обрив з'єднання, дедлок. Повтор через деякий час, скоріш за все, вдасться, тож потрібні **повтори з паузою і лічильником**. На демонстрації SQL Server призупинено на 16 с: перша спроба впала за таймаутом підключення, після відновлення повідомлення оброблено, жодної втрати.
* **Постійний** — причина в самому повідомленні або в несумісності: некоректний JSON, невідомий тип, версія схеми, яку споживач не розуміє, невалідні дані (ціна ≤ 0, порожня назва). Повтор дасть ту саму помилку, тому повідомлення **одразу йде в DLQ** (`Nack(requeue: false)`), не блокуючи чергу. Там його розбирає людина: виправити й переопублікувати (Management UI → Move messages / Shovel) або видалити.

Тимчасовий збій, що триває довше за всі повтори, теж потрапляє в DLQ — після 3 повторів. Це запобіжник, щоб повідомлення не крутилося вічно. Розрізнення в коді — блоки `catch` у `HandleDeliveryAsync`: `JsonException` / `PermanentMessageException` → DLQ, решта винятків → retry (поки `x-retry-count < MaxRetries`).
