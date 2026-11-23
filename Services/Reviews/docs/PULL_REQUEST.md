# Lab04: Clean Architecture, CQRS (MediatR) і MongoDB — мікросервіс відгуків

## Що зроблено
- Чотири шари з правильним напрямком залежностей: Reviews.Domain (без посилань на інші проєкти) ← Application ← Infrastructure, WebApi.
- Domain: сутності Review (з вкладеними Comment), Discussion на BaseEntity (ObjectId, CreatedAt, UpdatedAt, Version); Value Objects Author, Rating, ReviewAspects; DomainException, ConflictException; інтерфейси репозиторіїв.
- Application: команди й запити MediatR з окремими обробниками; читання через IReviewQueries / IDiscussionQueries; валідатори FluentValidation (ObjectId, сортування, асинхронне правило «один відгук на фільм»); Pipeline Behaviors Logging → ExceptionHandling → Validation → Performance; профілі AutoMapper.
- Infrastructure: MongoDbContext через IOptions, BsonClassMap без атрибутів у домені (camelCase, приватне поле _comments, створення VO через конструктор, власні серіалізатори), репозиторії з оптимістичною конкурентністю, read-моделі з проєкцією, aggregation pipeline, курсорна пагінація, індекси фоновим сервісом, ідемпотентний сідінг з JSON, health check.
- WebApi: контролери лише через ISender, ETag / If-Match, ProblemDetails з MongoDB-винятками (DuplicateKey → 409, недоступність → 503), Swagger, Serilog.
- Тести: 50 (domain, архітектура, валідатори, behaviors, AutoMapper, обробники з Moq, інтеграційні з MongoDB, API) — усі зелені.

## Як перевірити
1. docker start cinema-mongo (або будь-який MongoDB на localhost:27017)
2. dotnet run --project src/Reviews.WebApi → http://localhost:5380/swagger
3. POST /api/reviews з rating = 10 → 400, у лозі «відхилено валідацією ДО виклику обробника»
4. PUT /api/reviews/{id} зі старим If-Match → 409
5. dotnet test (для тестів з базою — змінна REVIEWS_TEST_MONGO)

## Знімки
docs/swagger.png, docs/compass-review.png, docs/compass-indexes.png, docs/validation-400.png, docs/pipeline-log.png
