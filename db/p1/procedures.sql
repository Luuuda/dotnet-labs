/* =====================================================================
   Проєкт №1 — CinemaBooking
   02_procedures.sql — 6 збережуваних процедур (CRUD + бізнес-операції)

   1. usp_Customer_Upsert          — Create/Update клієнта (+ rowversion)
   2. usp_Booking_Create           — транзакційне бронювання місць (знімок цін)
   3. usp_Booking_GetById          — Read: 3 набори результатів (Dapper QueryMultiple)
   4. usp_Bookings_GetByCustomer   — Read: пошук + пагінація
   5. usp_Booking_ChangeStatus     — ТРАНЗАКЦІЙНА зміна статусу (UPDLOCK + THROW)
   6. usp_Payment_Register         — оплата + перехід у Paid в одній транзакції

   Коди керованих помилок (THROW 50xxx) — див. README.
   CREATE OR ALTER робить скрипт ідемпотентним.
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
USE CinemaBooking;
GO

/* ---------------------------------------------------------------------
   1. usp_Customer_Upsert
   @Id = NULL  -> створює клієнта і повертає новий Id в @Id
   @Id задано -> оновлює; якщо передано @ExpectedRowVersion і запис
                уже змінив хтось інший, повертає помилку 50032.
   Soft delete: @IsDeleted = 1.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Customer_Upsert
    @Id                 UNIQUEIDENTIFIER = NULL OUTPUT,
    @Email              NVARCHAR(256),
    @FullName           NVARCHAR(200),
    @Phone              VARCHAR(20)   = NULL,
    @IsDeleted          BIT           = 0,
    @ExpectedRowVersion BINARY(8)     = NULL,
    @ChangedBy          NVARCHAR(100) = N'booking-api'
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF EXISTS (SELECT 1 FROM dbo.Customers
               WHERE Email = @Email AND IsDeleted = 0 AND (@Id IS NULL OR Id <> @Id))
        THROW 50031, N'Клієнт з таким email уже існує', 1;

    IF @Id IS NULL
    BEGIN
        DECLARE @New TABLE (Id UNIQUEIDENTIFIER);
        INSERT dbo.Customers (Email, FullName, Phone, CreatedBy)
        OUTPUT inserted.Id INTO @New
        VALUES (@Email, @FullName, @Phone, @ChangedBy);
        SELECT @Id = Id FROM @New;
    END
    ELSE
    BEGIN
        UPDATE dbo.Customers
        SET Email = @Email, FullName = @FullName, Phone = @Phone, IsDeleted = @IsDeleted,
            UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
        WHERE Id = @Id
          AND (@ExpectedRowVersion IS NULL OR RowVersion = @ExpectedRowVersion);

        IF @@ROWCOUNT = 0
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM dbo.Customers WHERE Id = @Id)
                THROW 50030, N'Клієнта не знайдено', 1;
            THROW 50032, N'Конфлікт конкурентності: клієнта вже змінив інший користувач', 1;
        END;
    END;

    SELECT Id, Email, FullName, Phone, IsDeleted, RowVersion
    FROM dbo.Customers WHERE Id = @Id;
END;
GO

/* ---------------------------------------------------------------------
   2. usp_Booking_Create
   @Seats — JSON-масив місць: '[{"row":5,"seat":7},{"row":5,"seat":8}]'
   У Tickets записується ЗНІМОК назви фільму, зали, часу та ціни.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Booking_Create
    @CustomerId   UNIQUEIDENTIFIER,
    @ScreeningId  BIGINT,
    @Seats        NVARCHAR(MAX),
    @PromoCode    NVARCHAR(30)  = NULL,
    @ContactPhone VARCHAR(20)   = NULL,
    @Channel      NVARCHAR(20)  = N'Web',
    @Comment      NVARCHAR(500) = NULL,
    @CreatedBy    NVARCHAR(100) = N'booking-api',
    @BookingId    BIGINT OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @SeatList TABLE (SeatRow INT NOT NULL, SeatNumber INT NOT NULL,
                             PRIMARY KEY (SeatRow, SeatNumber));
    DECLARE @Requested INT, @Email NVARCHAR(256), @Phone VARCHAR(20),
            @Rows INT, @PerRow INT, @PromotionId INT = NULL, @Pct DECIMAL(5,2) = 0;

    -- ---- валідація вхідних даних (до відкриття транзакції) ----
    IF @Seats IS NULL OR ISJSON(@Seats) = 0
        THROW 50010, N'Список місць має бути JSON-масивом', 1;

    SELECT @Requested = COUNT(*) FROM OPENJSON(@Seats);

    INSERT @SeatList (SeatRow, SeatNumber)
    SELECT DISTINCT j.[row], j.[seat]
    FROM OPENJSON(@Seats) WITH ([row] INT '$.row', [seat] INT '$.seat') j
    WHERE j.[row] IS NOT NULL AND j.[seat] IS NOT NULL;
    DECLARE @Valid INT = @@ROWCOUNT;

    IF @Requested = 0 OR @Valid <> @Requested
        THROW 50010, N'Порожній список місць, дублікати або некоректні елементи', 1;

    SELECT @Email = Email, @Phone = Phone
    FROM dbo.Customers WHERE Id = @CustomerId AND IsDeleted = 0;
    IF @Email IS NULL
        THROW 50011, N'Клієнта не знайдено', 1;

    SELECT @Rows = RowsCount, @PerRow = SeatsPerRow
    FROM dbo.Screenings WHERE Id = @ScreeningId AND IsActive = 1;
    IF @Rows IS NULL
        THROW 50012, N'Сеанс не знайдено або його скасовано', 1;

    IF EXISTS (SELECT 1 FROM @SeatList
               WHERE SeatRow NOT BETWEEN 1 AND @Rows OR SeatNumber NOT BETWEEN 1 AND @PerRow)
        THROW 50013, N'Місце поза межами зали', 1;

    IF @PromoCode IS NOT NULL
    BEGIN
        SELECT @PromotionId = Id, @Pct = DiscountPercent
        FROM dbo.Promotions
        WHERE Code = @PromoCode AND IsActive = 1
          AND SYSUTCDATETIME() >= ValidFrom AND SYSUTCDATETIME() < ValidTo;
        IF @PromotionId IS NULL
            THROW 50014, N'Промокод недійсний або прострочений', 1;
    END;

    -- ---- транзакція ----
    BEGIN TRY
    BEGIN TRANSACTION;

        -- UPDLOCK + HOLDLOCK: блокуємо діапазон місць до кінця транзакції,
        -- щоб паралельне бронювання не «проскочило» між перевіркою і вставкою.
        IF EXISTS (SELECT 1
                   FROM dbo.Tickets t WITH (UPDLOCK, HOLDLOCK)
                   JOIN @SeatList s ON s.SeatRow = t.SeatRow AND s.SeatNumber = t.SeatNumber
                   WHERE t.ScreeningId = @ScreeningId AND t.IsCancelled = 0)
            THROW 50015, N'Одне або кілька місць уже зайняті', 1;

        INSERT dbo.Bookings (CustomerId, PromotionId, Status, CreatedBy)
        VALUES (@CustomerId, @PromotionId, N'Submitted', @CreatedBy);
        SET @BookingId = SCOPE_IDENTITY();

        INSERT dbo.BookingDetails (BookingId, ContactEmail, ContactPhone, Channel, Comment)
        VALUES (@BookingId, @Email, COALESCE(@ContactPhone, @Phone), @Channel, @Comment);

        INSERT dbo.Tickets (BookingId, ScreeningId, SeatRow, SeatNumber,
                            MovieTitle, HallName, StartsAt, UnitPrice, Discount)
        SELECT @BookingId, sc.Id, s.SeatRow, s.SeatNumber,
               sc.MovieTitle, sc.HallName, sc.StartsAt, sc.BasePrice,
               ROUND(sc.BasePrice * @Pct / 100, 2)
        FROM @SeatList s
        CROSS JOIN dbo.Screenings sc
        WHERE sc.Id = @ScreeningId;

        INSERT dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedBy, Reason)
        VALUES (@BookingId, NULL, N'Submitted', @CreatedBy, N'Бронювання створено');

    COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- відкочуємо ВСЮ транзакцію (і зовнішню, якщо процедуру викликано вкладено)
        -- та прокидаємо помилку далі без змін (код 50xxx зберігається)
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT * FROM dbo.vw_BookingSummary WHERE BookingId = @BookingId;
END;
GO

/* ---------------------------------------------------------------------
   3. usp_Booking_GetById — агрегат цілком, 3 набори результатів:
      (1) бронювання + деталі, (2) квитки, (3) історія статусів
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Booking_GetById
    @BookingId BIGINT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT s.*, d.ContactEmail, d.ContactPhone, d.Channel, d.Comment
    FROM dbo.vw_BookingSummary s
    JOIN dbo.BookingDetails d ON d.BookingId = s.BookingId
    WHERE s.BookingId = @BookingId;

    SELECT Id, ScreeningId, SeatRow, SeatNumber, MovieTitle, HallName, StartsAt,
           UnitPrice, Discount, UnitPrice - Discount AS FinalPrice, IsCancelled
    FROM dbo.Tickets
    WHERE BookingId = @BookingId
    ORDER BY SeatRow, SeatNumber;

    SELECT FromStatus, ToStatus, ChangedAt, ChangedBy, Reason
    FROM dbo.BookingStatusHistory
    WHERE BookingId = @BookingId
    ORDER BY ChangedAt, Id;
END;
GO

/* ---------------------------------------------------------------------
   4. usp_Bookings_GetByCustomer — пошук бронювань клієнта
      (необов'язковий фільтр за статусом) з пагінацією OFFSET/FETCH.
      TotalCount — для побудови пагінатора на клієнті.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Bookings_GetByCustomer
    @CustomerId UNIQUEIDENTIFIER,
    @Status     NVARCHAR(30) = NULL,
    @PageNumber INT = 1,
    @PageSize   INT = 10
AS
BEGIN
    SET NOCOUNT ON;

    IF @PageNumber < 1 OR @PageSize NOT BETWEEN 1 AND 100
        THROW 50040, N'Некоректні параметри пагінації (PageNumber >= 1, PageSize 1..100)', 1;

    SELECT BookingId, Status, CreatedAt, PromoCode, TicketsCount, TotalAmount, PaidAmount,
           FirstStartsAt, COUNT(*) OVER () AS TotalCount
    FROM dbo.vw_BookingSummary
    WHERE CustomerId = @CustomerId
      AND (@Status IS NULL OR Status = @Status)
    ORDER BY CreatedAt DESC, BookingId DESC
    OFFSET (@PageNumber - 1) * @PageSize ROWS
    FETCH NEXT @PageSize ROWS ONLY;
END;
GO

/* ---------------------------------------------------------------------
   5. usp_Booking_ChangeStatus — ключова транзакційна процедура
      * явна транзакція + XACT_ABORT + TRY/CATCH (будь-яка помилка => ROLLBACK)
      * UPDLOCK: рядок бронювання заблоковано до COMMIT, тож два
        паралельні виклики не можуть обидва «перевести» статус
      * перевірка переходу за довідником BookingStatusTransitions
      * недопустимий перехід => THROW 50002, дані НЕ змінюються
      * Paid — лише якщо сума успішних оплат покриває вартість (50004)
      * Cancelled — звільняє місця (IsCancelled = 1 у квитках)
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Booking_ChangeStatus
    @BookingId          BIGINT,
    @NewStatus          NVARCHAR(30),
    @ChangedBy          NVARCHAR(100) = N'booking-api',
    @Reason             NVARCHAR(300) = NULL,
    @ExpectedRowVersion BINARY(8)     = NULL
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Current NVARCHAR(30), @RowVer BINARY(8), @Msg NVARCHAR(400),
            @Total DECIMAL(12,2), @Paid DECIMAL(12,2);

    BEGIN TRY
    BEGIN TRANSACTION;

        SELECT @Current = Status, @RowVer = RowVersion
        FROM dbo.Bookings WITH (UPDLOCK, ROWLOCK)
        WHERE Id = @BookingId AND IsDeleted = 0;

        IF @Current IS NULL
            THROW 50001, N'Бронювання не знайдено', 1;

        IF @ExpectedRowVersion IS NOT NULL AND @ExpectedRowVersion <> @RowVer
            THROW 50003, N'Конфлікт конкурентності: бронювання вже змінено іншим запитом', 1;

        IF NOT EXISTS (SELECT 1 FROM dbo.BookingStatusTransitions
                       WHERE FromStatus = @Current AND ToStatus = @NewStatus)
        BEGIN
            SET @Msg = CONCAT(N'Недопустимий перехід статусу: ', @Current, N' -> ', @NewStatus,
                              N' (бронювання #', @BookingId, N')');
            THROW 50002, @Msg, 1;
        END;

        IF @NewStatus = N'Paid'
        BEGIN
            SELECT @Total = ISNULL(SUM(UnitPrice - Discount), 0)
            FROM dbo.Tickets WHERE BookingId = @BookingId AND IsCancelled = 0;
            SELECT @Paid = ISNULL(SUM(Amount), 0)
            FROM dbo.Payments WHERE BookingId = @BookingId AND Status = N'Succeeded';
            IF @Paid < @Total
            BEGIN
                SET @Msg = CONCAT(N'Бронювання не оплачено: сплачено ', @Paid, N' з ', @Total, N' UAH');
                THROW 50004, @Msg, 1;
            END;
        END;

        UPDATE dbo.Bookings
        SET Status = @NewStatus, UpdatedAt = SYSUTCDATETIME(), UpdatedBy = @ChangedBy
        WHERE Id = @BookingId;

        IF @NewStatus = N'Cancelled'
            UPDATE dbo.Tickets SET IsCancelled = 1 WHERE BookingId = @BookingId;

        INSERT dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedBy, Reason)
        VALUES (@BookingId, @Current, @NewStatus, @ChangedBy, @Reason);

    COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- відкочуємо ВСЮ транзакцію (і зовнішню, якщо процедуру викликано вкладено)
        -- та прокидаємо помилку далі без змін (код 50xxx зберігається)
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT Id AS BookingId, Status, RowVersion FROM dbo.Bookings WHERE Id = @BookingId;
END;
GO

/* ---------------------------------------------------------------------
   6. usp_Payment_Register — фіксує оплату і переводить бронювання в Paid
      в ОДНІЙ транзакції (вкладений виклик usp_Booking_ChangeStatus).
      Ідемпотентна: повторний колбек з тим самим ExternalTransactionId
      не створює другу оплату.
   --------------------------------------------------------------------- */
CREATE OR ALTER PROCEDURE dbo.usp_Payment_Register
    @BookingId             BIGINT,
    @Amount                DECIMAL(10,2),
    @Provider              NVARCHAR(20),
    @ExternalTransactionId NVARCHAR(100) = NULL,
    @CreatedBy             NVARCHAR(100) = N'payment-gateway',
    @PaymentId             BIGINT = NULL OUTPUT
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    DECLARE @Status NVARCHAR(30), @Total DECIMAL(12,2), @Msg NVARCHAR(400);

    IF @ExternalTransactionId IS NOT NULL
    BEGIN
        SELECT @PaymentId = Id FROM dbo.Payments WHERE ExternalTransactionId = @ExternalTransactionId;
        IF @PaymentId IS NOT NULL
        BEGIN
            SELECT Id AS PaymentId, BookingId, Amount, Status, N'AlreadyProcessed' AS Result
            FROM dbo.Payments WHERE Id = @PaymentId;
            RETURN;
        END;
    END;

    BEGIN TRY
    BEGIN TRANSACTION;

        SELECT @Status = Status FROM dbo.Bookings WITH (UPDLOCK, ROWLOCK)
        WHERE Id = @BookingId AND IsDeleted = 0;

        IF @Status IS NULL
            THROW 50001, N'Бронювання не знайдено', 1;
        IF @Status <> N'SeatsConfirmed'
        BEGIN
            SET @Msg = CONCAT(N'Оплата можлива лише для бронювання зі статусом SeatsConfirmed (зараз: ', @Status, N')');
            THROW 50020, @Msg, 1;
        END;

        SELECT @Total = ISNULL(SUM(UnitPrice - Discount), 0)
        FROM dbo.Tickets WHERE BookingId = @BookingId AND IsCancelled = 0;
        IF @Amount <> @Total
        BEGIN
            SET @Msg = CONCAT(N'Сума оплати ', @Amount, N' не дорівнює вартості бронювання ', @Total);
            THROW 50021, @Msg, 1;
        END;

        INSERT dbo.Payments (BookingId, Amount, Provider, ExternalTransactionId, Status, CreatedBy)
        VALUES (@BookingId, @Amount, @Provider, @ExternalTransactionId, N'Succeeded', @CreatedBy);
        SET @PaymentId = SCOPE_IDENTITY();

        EXEC dbo.usp_Booking_ChangeStatus
             @BookingId = @BookingId, @NewStatus = N'Paid',
             @ChangedBy = @CreatedBy, @Reason = N'Оплату отримано';

    COMMIT TRANSACTION;
    END TRY
    BEGIN CATCH
        -- відкочуємо ВСЮ транзакцію (і зовнішню, якщо процедуру викликано вкладено)
        -- та прокидаємо помилку далі без змін (код 50xxx зберігається)
        IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
        THROW;
    END CATCH;

    SELECT Id AS PaymentId, BookingId, Amount, Status, N'Created' AS Result
    FROM dbo.Payments WHERE Id = @PaymentId;
END;
GO

PRINT N'CinemaBooking: процедури створено.';
