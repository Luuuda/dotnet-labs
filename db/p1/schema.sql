/* =====================================================================
   Проєкт №1 — CinemaBooking (Бронювання квитків)
   01_schema.sql — таблиці, обмеження, індекси, представлення
   Технологія доступу в Лаб. 02: ADO.NET + Dapper.

   Зв'язки всередині бази:
     1:1  Bookings  <-> BookingDetails   (FK = PK залежної таблиці)
     1:N  Customers  -> Bookings         (FK на боці «багатьох»)
     1:N  Bookings   -> BookingStatusHistory, Payments
     M:N  Bookings  <-> Screenings через Tickets (власні поля: місце, ціна)
   Screenings — ЛОКАЛЬНА КОПІЯ сеансів із CinemaCatalog (той самий Id),
   FOREIGN KEY на іншу базу немає.
   Скрипт ідемпотентний (IF OBJECT_ID / IF NOT EXISTS).
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
USE CinemaBooking;
GO
SET NOCOUNT ON;
GO

/* ---------- Customers: глядачі. PK = GUID ----------
   GUID, бо Id клієнта є «публічним» ідентифікатором, на який посилаються
   інші сервіси (відгуки в MongoDB зберігають author.userId). Його можна
   згенерувати на будь-якому вузлі без звернення до БД. NEWSEQUENTIALID()
   зменшує фрагментацію кластерного індексу. */
IF OBJECT_ID(N'dbo.Customers', N'U') IS NULL
CREATE TABLE dbo.Customers (
    Id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Customers_Id DEFAULT NEWSEQUENTIALID(),
    Email       NVARCHAR(256)    NOT NULL,
    FullName    NVARCHAR(200)    NOT NULL,
    Phone       VARCHAR(20)      NULL,
    CreatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Customers_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100)    NOT NULL CONSTRAINT DF_Customers_CreatedBy DEFAULT N'system',
    UpdatedAt   DATETIME2(3)     NULL,
    UpdatedBy   NVARCHAR(100)    NULL,
    IsDeleted   BIT              NOT NULL CONSTRAINT DF_Customers_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION       NOT NULL,                 -- оптимістична конкурентність (профіль редагують з кількох пристроїв)
    CONSTRAINT PK_Customers PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_Customers_Email CHECK (Email LIKE N'%_@_%._%'),
    CONSTRAINT CK_Customers_Phone CHECK (Phone IS NULL OR Phone LIKE '+[0-9]%')
);
GO

/* ---------- Promotions: промокоди. PK = INT IDENTITY (малий довідник) ---------- */
IF OBJECT_ID(N'dbo.Promotions', N'U') IS NULL
CREATE TABLE dbo.Promotions (
    Id              INT IDENTITY(1,1) NOT NULL,
    Code            NVARCHAR(30)  NOT NULL,
    Title           NVARCHAR(200) NOT NULL,
    DiscountPercent DECIMAL(5,2)  NOT NULL,
    ValidFrom       DATETIME2(0)  NOT NULL,
    ValidTo         DATETIME2(0)  NOT NULL,
    IsActive        BIT           NOT NULL CONSTRAINT DF_Promotions_IsActive DEFAULT 1,
    CreatedAt       DATETIME2(3)  NOT NULL CONSTRAINT DF_Promotions_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       NVARCHAR(100) NOT NULL CONSTRAINT DF_Promotions_CreatedBy DEFAULT N'system',
    UpdatedAt       DATETIME2(3)  NULL,
    UpdatedBy       NVARCHAR(100) NULL,
    CONSTRAINT PK_Promotions PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Promotions_Code UNIQUE (Code),                      -- пошук промокоду при бронюванні
    CONSTRAINT CK_Promotions_Discount CHECK (DiscountPercent > 0 AND DiscountPercent <= 90),
    CONSTRAINT CK_Promotions_Period   CHECK (ValidTo > ValidFrom)
);
GO

/* ---------- Screenings: локальна копія довідника сеансів Каталогу ----------
   Власник даних — CinemaCatalog.Screenings. Тут той самий Id (без IDENTITY)
   і БЕЗ FOREIGN KEY на іншу базу. Оновлюється подіями з Каталогу
   (ScreeningScheduled / ScreeningChanged / MovieRenamed — Лаб. 08). */
IF OBJECT_ID(N'dbo.Screenings', N'U') IS NULL
CREATE TABLE dbo.Screenings (
    Id          BIGINT        NOT NULL,              -- = CinemaCatalog.dbo.Screenings.Id
    MovieId     INT           NOT NULL,              -- = CinemaCatalog.dbo.Movies.Id (логічне посилання)
    MovieTitle  NVARCHAR(200) NOT NULL,
    HallId      INT           NOT NULL,              -- = CinemaCatalog.dbo.Halls.Id (логічне посилання)
    HallName    NVARCHAR(100) NOT NULL,
    RowsCount   INT           NOT NULL,
    SeatsPerRow INT           NOT NULL,
    StartsAt    DATETIME2(0)  NOT NULL,
    BasePrice   DECIMAL(10,2) NOT NULL,
    IsActive    BIT           NOT NULL CONSTRAINT DF_Screenings_IsActive DEFAULT 1,
    SyncedAt    DATETIME2(3)  NOT NULL CONSTRAINT DF_Screenings_SyncedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Screenings PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT CK_Screenings_Price CHECK (BasePrice > 0),
    CONSTRAINT CK_Screenings_Size  CHECK (RowsCount > 0 AND SeatsPerRow > 0)
);
GO

/* ---------- Bookings: корінь агрегату «Бронювання». PK = BIGINT IDENTITY ----------
   BIGINT IDENTITY: записи створює лише цей сервіс, послідовна вставка,
   компактний (8 байт) ключ для FK у Tickets/Payments/History. */
IF OBJECT_ID(N'dbo.Bookings', N'U') IS NULL
CREATE TABLE dbo.Bookings (
    Id          BIGINT IDENTITY(1,1) NOT NULL,
    CustomerId  UNIQUEIDENTIFIER NOT NULL,
    PromotionId INT              NULL,
    Status      NVARCHAR(30)     NOT NULL CONSTRAINT DF_Bookings_Status DEFAULT N'Submitted',
    CreatedAt   DATETIME2(3)     NOT NULL CONSTRAINT DF_Bookings_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100)    NOT NULL CONSTRAINT DF_Bookings_CreatedBy DEFAULT N'system',
    UpdatedAt   DATETIME2(3)     NULL,
    UpdatedBy   NVARCHAR(100)    NULL,
    IsDeleted   BIT              NOT NULL CONSTRAINT DF_Bookings_IsDeleted DEFAULT 0,
    RowVersion  ROWVERSION       NOT NULL,              -- статус змінюють одночасно касир, платіжний шлюз і фонові задачі
    CONSTRAINT PK_Bookings PRIMARY KEY CLUSTERED (Id),
    -- 1:N Customers -> Bookings. Без каскаду: клієнта з історією не видаляємо (лише soft delete)
    CONSTRAINT FK_Bookings_Customers  FOREIGN KEY (CustomerId)  REFERENCES dbo.Customers(Id),
    CONSTRAINT FK_Bookings_Promotions FOREIGN KEY (PromotionId) REFERENCES dbo.Promotions(Id),
    -- статуси за зразком OrderStatus з Microsoft eShop
    CONSTRAINT CK_Bookings_Status CHECK (Status IN (N'Submitted', N'AwaitingValidation', N'SeatsConfirmed',
                                                    N'Paid', N'Issued', N'Cancelled'))
);
GO

/* ---------- BookingDetails: 1:1 з Bookings (FK є водночас PK) ---------- */
IF OBJECT_ID(N'dbo.BookingDetails', N'U') IS NULL
CREATE TABLE dbo.BookingDetails (
    BookingId    BIGINT        NOT NULL,
    ContactEmail NVARCHAR(256) NOT NULL,
    ContactPhone VARCHAR(20)   NULL,
    Channel      NVARCHAR(20)  NOT NULL CONSTRAINT DF_BookingDetails_Channel DEFAULT N'Web',
    Comment      NVARCHAR(500) NULL,
    CONSTRAINT PK_BookingDetails PRIMARY KEY CLUSTERED (BookingId),
    -- каскад доречний: деталі не існують без бронювання
    CONSTRAINT FK_BookingDetails_Bookings FOREIGN KEY (BookingId) REFERENCES dbo.Bookings(Id) ON DELETE CASCADE,
    CONSTRAINT CK_BookingDetails_Channel CHECK (Channel IN (N'Web', N'Mobile', N'BoxOffice'))
);
GO

/* ---------- Tickets: M:N Bookings <-> Screenings з власними полями ----------
   MovieTitle, HallName, StartsAt, UnitPrice — ЗНІМОК на момент бронювання
   (як OrderItem.ProductName / UnitPrice в eShop): НЕ оновлюється, навіть
   якщо Каталог змінить ціну чи назву. */
IF OBJECT_ID(N'dbo.Tickets', N'U') IS NULL
CREATE TABLE dbo.Tickets (
    Id          BIGINT IDENTITY(1,1) NOT NULL,
    BookingId   BIGINT        NOT NULL,
    ScreeningId BIGINT        NOT NULL,
    SeatRow     INT           NOT NULL,
    SeatNumber  INT           NOT NULL,
    MovieTitle  NVARCHAR(200) NOT NULL,   -- знімок
    HallName    NVARCHAR(100) NOT NULL,   -- знімок
    StartsAt    DATETIME2(0)  NOT NULL,   -- знімок
    UnitPrice   DECIMAL(10,2) NOT NULL,   -- знімок
    Discount    DECIMAL(10,2) NOT NULL CONSTRAINT DF_Tickets_Discount DEFAULT 0,
    IsCancelled BIT           NOT NULL CONSTRAINT DF_Tickets_IsCancelled DEFAULT 0,
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Tickets_CreatedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_Tickets PRIMARY KEY CLUSTERED (Id),
    -- каскад доречний: квиток — частина агрегату Booking
    CONSTRAINT FK_Tickets_Bookings   FOREIGN KEY (BookingId)   REFERENCES dbo.Bookings(Id) ON DELETE CASCADE,
    CONSTRAINT FK_Tickets_Screenings FOREIGN KEY (ScreeningId) REFERENCES dbo.Screenings(Id),
    CONSTRAINT UQ_Tickets_Booking_Seat UNIQUE (BookingId, ScreeningId, SeatRow, SeatNumber),
    CONSTRAINT CK_Tickets_Seat     CHECK (SeatRow > 0 AND SeatNumber > 0),
    CONSTRAINT CK_Tickets_Price    CHECK (UnitPrice > 0),
    CONSTRAINT CK_Tickets_Discount CHECK (Discount >= 0 AND Discount < UnitPrice)
);
GO

/* ---------- BookingStatusHistory: журнал переходів (1:N) ---------- */
IF OBJECT_ID(N'dbo.BookingStatusHistory', N'U') IS NULL
CREATE TABLE dbo.BookingStatusHistory (
    Id         BIGINT IDENTITY(1,1) NOT NULL,
    BookingId  BIGINT        NOT NULL,
    FromStatus NVARCHAR(30)  NULL,
    ToStatus   NVARCHAR(30)  NOT NULL,
    ChangedAt  DATETIME2(3)  NOT NULL CONSTRAINT DF_BookingStatusHistory_ChangedAt DEFAULT SYSUTCDATETIME(),
    ChangedBy  NVARCHAR(100) NOT NULL,
    Reason     NVARCHAR(300) NULL,
    CONSTRAINT PK_BookingStatusHistory PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_BookingStatusHistory_Bookings FOREIGN KEY (BookingId) REFERENCES dbo.Bookings(Id) ON DELETE CASCADE
);
GO

/* ---------- BookingStatusTransitions: допустимі переходи (довідник) ---------- */
IF OBJECT_ID(N'dbo.BookingStatusTransitions', N'U') IS NULL
CREATE TABLE dbo.BookingStatusTransitions (
    FromStatus NVARCHAR(30) NOT NULL,
    ToStatus   NVARCHAR(30) NOT NULL,
    CONSTRAINT PK_BookingStatusTransitions PRIMARY KEY CLUSTERED (FromStatus, ToStatus)
);
GO

/* ---------- Payments: оплати (1:N). Фінансові записи — без каскаду ---------- */
IF OBJECT_ID(N'dbo.Payments', N'U') IS NULL
CREATE TABLE dbo.Payments (
    Id                    BIGINT IDENTITY(1,1) NOT NULL,
    BookingId             BIGINT        NOT NULL,
    Amount                DECIMAL(10,2) NOT NULL,
    Currency              CHAR(3)       NOT NULL CONSTRAINT DF_Payments_Currency DEFAULT 'UAH',
    Provider              NVARCHAR(20)  NOT NULL,
    ExternalTransactionId NVARCHAR(100) NULL,
    Status                NVARCHAR(20)  NOT NULL CONSTRAINT DF_Payments_Status DEFAULT N'Succeeded',
    PaidAt                DATETIME2(3)  NOT NULL CONSTRAINT DF_Payments_PaidAt DEFAULT SYSUTCDATETIME(),
    CreatedBy             NVARCHAR(100) NOT NULL CONSTRAINT DF_Payments_CreatedBy DEFAULT N'system',
    CONSTRAINT PK_Payments PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT FK_Payments_Bookings FOREIGN KEY (BookingId) REFERENCES dbo.Bookings(Id),
    CONSTRAINT CK_Payments_Amount   CHECK (Amount > 0),
    CONSTRAINT CK_Payments_Provider CHECK (Provider IN (N'Card', N'ApplePay', N'GooglePay', N'Cash')),
    CONSTRAINT CK_Payments_Status   CHECK (Status IN (N'Succeeded', N'Failed', N'Refunded'))
);
GO

/* =====================================================================
   ІНДЕКСИ — кожен створено під конкретний запит
   ===================================================================== */

-- Запит: «знайти активного клієнта за email» (вхід, usp_Customer_Upsert).
-- Фільтрований UNIQUE: email унікальний лише серед не видалених клієнтів.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Customers_Email_Active')
    CREATE UNIQUE INDEX UX_Customers_Email_Active ON dbo.Customers (Email) WHERE IsDeleted = 0;

-- Запит: «бронювання клієнта з певним статусом» (usp_Bookings_GetByCustomer, @Status).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bookings_CustomerId_Status')
    CREATE INDEX IX_Bookings_CustomerId_Status ON dbo.Bookings (CustomerId, Status)
        INCLUDE (CreatedAt, PromotionId) WHERE IsDeleted = 0;

-- Запит: «історія бронювань клієнта, нові зверху, з пагінацією» (OFFSET/FETCH).
-- Покриваючий: сортування CreatedAt DESC береться з індексу, без Key Lookup.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bookings_CustomerId_CreatedAt')
    CREATE INDEX IX_Bookings_CustomerId_CreatedAt ON dbo.Bookings (CustomerId, CreatedAt DESC)
        INCLUDE (Status, PromotionId) WHERE IsDeleted = 0;

-- Запит фонової задачі: «незавершені бронювання, старші за 15 хв» → скасувати й звільнити місця.
-- Фільтрований: індекс містить лише невелику частку «висячих» бронювань.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Bookings_Pending_CreatedAt')
    CREATE INDEX IX_Bookings_Pending_CreatedAt ON dbo.Bookings (CreatedAt)
        INCLUDE (CustomerId, Status)
        WHERE Status IN (N'Submitted', N'AwaitingValidation', N'SeatsConfirmed') AND IsDeleted = 0;

-- Запит: «чи вільне місце (ряд, місце) на сеансі» + гарантія «одне місце — один квиток».
-- Фільтрований UNIQUE: скасовані квитки не блокують місце.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Tickets_Screening_Seat_Active')
    CREATE UNIQUE INDEX UX_Tickets_Screening_Seat_Active ON dbo.Tickets (ScreeningId, SeatRow, SeatNumber)
        WHERE IsCancelled = 0;

-- Запит: «квитки бронювання» (usp_Booking_GetById, vw_BookingSummary) — покриває суму до сплати.
-- (UQ_Tickets_Booking_Seat теж починається з BookingId, але не містить цін.)
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Tickets_BookingId')
    CREATE INDEX IX_Tickets_BookingId ON dbo.Tickets (BookingId)
        INCLUDE (UnitPrice, Discount, IsCancelled, StartsAt, MovieTitle);

-- Запит: «журнал статусів бронювання в хронологічному порядку».
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BookingStatusHistory_BookingId_ChangedAt')
    CREATE INDEX IX_BookingStatusHistory_BookingId_ChangedAt ON dbo.BookingStatusHistory (BookingId, ChangedAt);

-- Запит: «сума успішних оплат бронювання» (перевірка перед переходом у Paid).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Payments_BookingId')
    CREATE INDEX IX_Payments_BookingId ON dbo.Payments (BookingId) INCLUDE (Amount, Status);

-- Запит: «чи вже оброблено транзакцію шлюзу» — ідемпотентність повторних колбеків.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UX_Payments_ExternalTransactionId')
    CREATE UNIQUE INDEX UX_Payments_ExternalTransactionId ON dbo.Payments (ExternalTransactionId)
        WHERE ExternalTransactionId IS NOT NULL;

-- Запит обробника події MovieRenamed з Каталогу: «оновити MovieTitle в усіх сеансах фільму».
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Screenings_MovieId')
    CREATE INDEX IX_Screenings_MovieId ON dbo.Screenings (MovieId);
GO

/* =====================================================================
   ПРЕДСТАВЛЕННЯ для читання (денормалізація без порушення 3НФ у таблицях)
   ===================================================================== */
CREATE OR ALTER VIEW dbo.vw_BookingSummary
AS
SELECT  b.Id            AS BookingId,
        b.CustomerId,
        c.FullName      AS CustomerName,
        c.Email         AS CustomerEmail,
        b.Status,
        b.CreatedAt,
        p.Code          AS PromoCode,
        ISNULL(t.TicketsCount, 0)       AS TicketsCount,
        ISNULL(t.TotalAmount, 0)        AS TotalAmount,
        ISNULL(pay.PaidAmount, 0)       AS PaidAmount,
        t.FirstStartsAt,
        b.RowVersion
FROM dbo.Bookings b
JOIN dbo.Customers c       ON c.Id = b.CustomerId
LEFT JOIN dbo.Promotions p ON p.Id = b.PromotionId
OUTER APPLY (SELECT COUNT(*)                          AS TicketsCount,
                    SUM(tk.UnitPrice - tk.Discount)   AS TotalAmount,
                    MIN(tk.StartsAt)                  AS FirstStartsAt
             FROM dbo.Tickets tk
             WHERE tk.BookingId = b.Id AND tk.IsCancelled = 0) t
OUTER APPLY (SELECT SUM(py.Amount) AS PaidAmount
             FROM dbo.Payments py
             WHERE py.BookingId = b.Id AND py.Status = N'Succeeded') pay
WHERE b.IsDeleted = 0;
GO

/* Довідник допустимих переходів — частина бізнес-правил схеми, тому заповнюється тут.
   Submitted → AwaitingValidation → SeatsConfirmed → Paid → Issued;
   Cancelled — лише ДО оплати. */
MERGE dbo.BookingStatusTransitions AS t
USING (VALUES (N'Submitted',          N'AwaitingValidation'),
              (N'AwaitingValidation', N'SeatsConfirmed'),
              (N'SeatsConfirmed',     N'Paid'),
              (N'Paid',               N'Issued'),
              (N'Submitted',          N'Cancelled'),
              (N'AwaitingValidation', N'Cancelled'),
              (N'SeatsConfirmed',     N'Cancelled')) AS s (FromStatus, ToStatus)
   ON t.FromStatus = s.FromStatus AND t.ToStatus = s.ToStatus
WHEN NOT MATCHED BY TARGET THEN INSERT (FromStatus, ToStatus) VALUES (s.FromStatus, s.ToStatus)
WHEN NOT MATCHED BY SOURCE THEN DELETE;
GO

/* ---------- Лаб. 08 (оновлення схеми): час останньої застосованої зміни полів копії сеансу ----------
   Події з Каталогу можуть прийти не в тому порядку, в якому сталися (повтор після тимчасової помилки
   повертається в кінець черги). Окремий час для ціни і для назви: подія застосовується, лише якщо вона
   новіша (OccurredAt) за вже застосовану зміну ЦЬОГО поля — стара подія не перезапише свіжу ціну / назву. */
IF COL_LENGTH(N'dbo.Screenings', N'PriceSyncedAt') IS NULL
    ALTER TABLE dbo.Screenings ADD PriceSyncedAt DATETIME2(3) NULL, TitleSyncedAt DATETIME2(3) NULL;
GO

/* ---------- Лаб. 08 (оновлення схеми) ProcessedEvents: оброблені інтеграційні події ----------
   Ідемпотентність споживача: EventId події з Каталогу записується в ТІЙ САМІЙ транзакції, що й оновлення
   копії dbo.Screenings. Повторна доставка (RabbitMQ гарантує «щонайменше один раз») → порушення PRIMARY KEY
   (помилка 2627) → обробку пропущено. EventType і ProcessedAt — для розбору інцидентів. */
IF OBJECT_ID(N'dbo.ProcessedEvents', N'U') IS NULL
CREATE TABLE dbo.ProcessedEvents (
    EventId     UNIQUEIDENTIFIER NOT NULL,
    EventType   NVARCHAR(100)    NOT NULL,
    ProcessedAt DATETIME2(3)     NOT NULL CONSTRAINT DF_ProcessedEvents_ProcessedAt DEFAULT SYSUTCDATETIME(),
    CONSTRAINT PK_ProcessedEvents PRIMARY KEY CLUSTERED (EventId)
);
GO

PRINT N'CinemaBooking: схему створено.';
