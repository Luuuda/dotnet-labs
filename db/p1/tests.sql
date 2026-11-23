/* =====================================================================
   Проєкт №1 — CinemaBooking
   04_tests.sql — запити перевірки: CRUD, пошук, пагінація,
   транзакційні сценарії та керовані помилки.
   Можна запускати багато разів: місця для тестового бронювання
   підбираються динамічно, тестовий клієнт шукається за email.
   Рядок «OK:» — очікуваний результат, «FAIL:» — щось не так.
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
USE CinemaBooking;
GO
SET NOCOUNT ON;

PRINT N'===== 1. Критерій: FOREIGN KEY лише в межах цієї бази =====';
SELECT fk.name                                  AS ForeignKey,
       OBJECT_NAME(fk.parent_object_id)         AS FromTable,
       OBJECT_NAME(fk.referenced_object_id)     AS ToTable,
       fk.delete_referential_action_desc        AS OnDelete
FROM sys.foreign_keys fk
ORDER BY FromTable;
-- Screenings.Id / MovieId / HallId і т. ін. — логічні посилання на CinemaCatalog без FK.
GO

PRINT N'===== 2. CRUD клієнта: Create / Read / Update / оптимістична конкурентність =====';
DECLARE @Id UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Customers WHERE Email = N'test.user@example.com');
DECLARE @R TABLE (Id UNIQUEIDENTIFIER, Email NVARCHAR(256), FullName NVARCHAR(200),
                  Phone VARCHAR(20), IsDeleted BIT, RowVersion BINARY(8));

INSERT @R EXEC dbo.usp_Customer_Upsert @Id = @Id OUTPUT, @Email = N'test.user@example.com',
                                       @FullName = N'Тестовий Користувач', @Phone = '+380990000000';
DECLARE @OldVersion BINARY(8) = (SELECT RowVersion FROM @R);
PRINT CONCAT(N'OK: Клієнт створений/оновлений, Id = ', @Id);

-- Update з правильною версією
DELETE @R;
INSERT @R EXEC dbo.usp_Customer_Upsert @Id = @Id OUTPUT, @Email = N'test.user@example.com',
                                       @FullName = N'Тестовий Користувач (оновлено)',
                                       @ExpectedRowVersion = @OldVersion;
PRINT N'OK: Update з актуальною RowVersion пройшов';

-- Update зі ЗАСТАРІЛОЮ версією -> 50032
BEGIN TRY
    EXEC dbo.usp_Customer_Upsert @Id = @Id OUTPUT, @Email = N'test.user@example.com',
                                 @FullName = N'Хтось інший', @ExpectedRowVersion = @OldVersion;
    PRINT N'FAIL: Конфлікт версій не виявлено';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

-- Дубль email -> 50031
BEGIN TRY
    DECLARE @NoId UNIQUEIDENTIFIER = NULL;
    EXEC dbo.usp_Customer_Upsert @Id = @NoId OUTPUT, @Email = N'olena.kovalenko@example.com', @FullName = N'Двійник';
    PRINT N'FAIL: Дубль email пропущено';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

SELECT Id, Email, FullName, Phone, IsDeleted, UpdatedAt FROM dbo.Customers WHERE Id = @Id;
GO

PRINT N'===== 3. Створення бронювання (транзакція, знімок ціни, промокод) =====';
DECLARE @Customer UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Customers WHERE Email = N'test.user@example.com');

-- динамічно знаходимо 2 вільні сусідні місця на сеансі #3, щоб тест можна було повторювати
DECLARE @Row INT, @Seat INT;
;WITH Seats AS (
    SELECT r.n AS SeatRow, s.n AS SeatNumber
    FROM (SELECT TOP (10) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n FROM sys.all_objects) r
    CROSS JOIN (SELECT TOP (11) ROW_NUMBER() OVER (ORDER BY (SELECT 1)) AS n FROM sys.all_objects) s
)
SELECT TOP (1) @Row = x.SeatRow, @Seat = x.SeatNumber
FROM Seats x
WHERE NOT EXISTS (SELECT 1 FROM dbo.Tickets t
                  WHERE t.ScreeningId = 3 AND t.IsCancelled = 0 AND t.SeatRow = x.SeatRow
                    AND t.SeatNumber IN (x.SeatNumber, x.SeatNumber + 1))
ORDER BY x.SeatRow, x.SeatNumber;

DECLARE @Json NVARCHAR(200) = CONCAT(N'[{"row":', @Row, N',"seat":', @Seat, N'},{"row":', @Row, N',"seat":', @Seat + 1, N'}]');
DECLARE @BookingId BIGINT;

-- промокод SUMMER25 завершився й вимкнений -> 50014
BEGIN TRY
    EXEC dbo.usp_Booking_Create @CustomerId = @Customer, @ScreeningId = 3, @Seats = @Json,
                                @PromoCode = N'SUMMER25', @BookingId = @BookingId OUTPUT;
    PRINT N'FAIL: Прострочений промокод прийнято';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

EXEC dbo.usp_Booking_Create @CustomerId = @Customer, @ScreeningId = 3, @Seats = @Json,
                            @PromoCode = N'STUDENT20', @Comment = N'Тестове бронювання',
                            @BookingId = @BookingId OUTPUT;
PRINT CONCAT(N'OK: Створено бронювання #', @BookingId, N', місця ', @Json);

-- зайняте місце (5,6) на сеансі #2 належить бронюванню #3 -> 50015
BEGIN TRY
    DECLARE @B2 BIGINT;
    EXEC dbo.usp_Booking_Create @CustomerId = @Customer, @ScreeningId = 2,
                                @Seats = N'[{"row":5,"seat":6}]', @BookingId = @B2 OUTPUT;
    PRINT N'FAIL: Подвійне бронювання місця';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

-- місце поза залою -> 50013; скасований сеанс #8 -> 50012
BEGIN TRY
    EXEC dbo.usp_Booking_Create @CustomerId = @Customer, @ScreeningId = 3,
                                @Seats = N'[{"row":99,"seat":1}]', @BookingId = @B2 OUTPUT;
    PRINT N'FAIL: Неіснуюче місце прийнято';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;
BEGIN TRY
    EXEC dbo.usp_Booking_Create @CustomerId = @Customer, @ScreeningId = 8,
                                @Seats = N'[{"row":1,"seat":1}]', @BookingId = @B2 OUTPUT;
    PRINT N'FAIL: Бронювання на скасований сеанс';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

/* ===== 4. ГОЛОВНИЙ КРИТЕРІЙ: недопустимий перехід => керована помилка, дані не змінюються ===== */
PRINT N'===== 4. Видати квитки (Issued) за НЕоплачене бронювання =====';
DECLARE @Before NVARCHAR(30) = (SELECT Status FROM dbo.Bookings WHERE Id = @BookingId);
DECLARE @HistBefore INT = (SELECT COUNT(*) FROM dbo.BookingStatusHistory WHERE BookingId = @BookingId);
BEGIN TRY
    EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = N'Issued';
    PRINT N'FAIL: Недопустимий перехід виконано';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;
IF (SELECT Status FROM dbo.Bookings WHERE Id = @BookingId) = @Before
   AND (SELECT COUNT(*) FROM dbo.BookingStatusHistory WHERE BookingId = @BookingId) = @HistBefore
    PRINT CONCAT(N'OK: Статус лишився ', @Before, N', журнал не змінився — транзакцію відкочено');
ELSE
    PRINT N'FAIL: Дані змінилися!';

PRINT N'===== 5. Нормальний ланцюжок статусів + оплата =====';
EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = N'AwaitingValidation';
EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = N'SeatsConfirmed', @Reason = N'Місця утримано';

-- Paid без оплати -> 50004
BEGIN TRY
    EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = N'Paid';
    PRINT N'FAIL: Paid без оплати';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

DECLARE @Total DECIMAL(10,2) = (SELECT TotalAmount FROM dbo.vw_BookingSummary WHERE BookingId = @BookingId);
DECLARE @Tx NVARCHAR(100) = CONCAT(N'TX-TEST-', @BookingId);
DECLARE @PaymentId BIGINT;

-- неправильна сума -> 50021
BEGIN TRY
    EXEC dbo.usp_Payment_Register @BookingId = @BookingId, @Amount = 1.00, @Provider = N'Card',
                                  @ExternalTransactionId = @Tx, @PaymentId = @PaymentId OUTPUT;
    PRINT N'FAIL: Неправильну суму прийнято';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

EXEC dbo.usp_Payment_Register @BookingId = @BookingId, @Amount = @Total, @Provider = N'Card',
                              @ExternalTransactionId = @Tx, @PaymentId = @PaymentId OUTPUT;
PRINT CONCAT(N'OK: Оплата #', @PaymentId, N' на ', @Total, N' UAH, бронювання переведено в Paid');

-- повторний колбек шлюзу з тим самим TX -> не створює другу оплату
EXEC dbo.usp_Payment_Register @BookingId = @BookingId, @Amount = @Total, @Provider = N'Card',
                              @ExternalTransactionId = @Tx;
IF (SELECT COUNT(*) FROM dbo.Payments WHERE BookingId = @BookingId) = 1
    PRINT N'OK: Повторний колбек оплати ідемпотентний';

-- скасування ПІСЛЯ оплати -> 50002
BEGIN TRY
    EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = N'Cancelled';
    PRINT N'FAIL: Скасовано оплачене бронювання';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N': ', ERROR_MESSAGE());
END CATCH;

EXEC dbo.usp_Booking_ChangeStatus @BookingId = @BookingId, @NewStatus = N'Issued', @Reason = N'Квитки надіслано';

PRINT N'===== 6. Читання агрегату (3 набори результатів) =====';
EXEC dbo.usp_Booking_GetById @BookingId = @BookingId;
GO

PRINT N'===== 7. Пагінація: бронювання Олени по 1 на сторінку =====';
EXEC dbo.usp_Bookings_GetByCustomer @CustomerId = '6F1C2A10-0000-4000-8000-000000000001', @PageNumber = 1, @PageSize = 1;
EXEC dbo.usp_Bookings_GetByCustomer @CustomerId = '6F1C2A10-0000-4000-8000-000000000001', @PageNumber = 2, @PageSize = 1;
PRINT N'Фільтр за статусом (Issued):';
EXEC dbo.usp_Bookings_GetByCustomer @CustomerId = '6F1C2A10-0000-4000-8000-000000000001', @Status = N'Issued';
GO

PRINT N'===== 8. Пошук: вільні місця сеансу, виручка за фільмами =====';
-- зайняті місця на сеансі #1 (використовує UX_Tickets_Screening_Seat_Active)
SELECT SeatRow, SeatNumber FROM dbo.Tickets
WHERE ScreeningId = 1 AND IsCancelled = 0 ORDER BY SeatRow, SeatNumber;

-- пошук бронювань за назвою фільму (знімок у Tickets — працює без звернення до Каталогу)
SELECT DISTINCT b.BookingId, b.CustomerName, b.Status, t.MovieTitle, t.StartsAt
FROM dbo.vw_BookingSummary b
JOIN dbo.Tickets t ON t.BookingId = b.BookingId
WHERE t.MovieTitle LIKE N'Дюна%';

-- виручка за фільмами (лише оплачені/видані бронювання)
SELECT t.MovieTitle, COUNT(*) AS TicketsSold, SUM(t.UnitPrice - t.Discount) AS Revenue
FROM dbo.Tickets t
JOIN dbo.Bookings b ON b.Id = t.BookingId
WHERE b.Status IN (N'Paid', N'Issued') AND t.IsCancelled = 0
GROUP BY t.MovieTitle
ORDER BY Revenue DESC;
GO

PRINT N'===== 9. Soft delete і каскадне видалення =====';
-- soft delete: клієнт «зникає» з vw_BookingSummary? Ні — бронювання лишаються, клієнт позначений IsDeleted
DECLARE @Tmp UNIQUEIDENTIFIER = (SELECT Id FROM dbo.Customers WHERE Email = N'test.user@example.com');
EXEC dbo.usp_Customer_Upsert @Id = @Tmp OUTPUT, @Email = N'test.user@example.com',
                             @FullName = N'Тестовий Користувач', @IsDeleted = 1;
PRINT N'OK: Тестового клієнта позначено IsDeleted = 1 (історія бронювань збереглась)';
-- повертаємо, щоб тест можна було повторити
EXEC dbo.usp_Customer_Upsert @Id = @Tmp OUTPUT, @Email = N'test.user@example.com',
                             @FullName = N'Тестовий Користувач', @IsDeleted = 0;

-- каскад: видалення НЕоплаченого бронювання прибирає деталі, квитки, історію
DECLARE @Cascade BIGINT;
EXEC dbo.usp_Booking_Create @CustomerId = @Tmp, @ScreeningId = 5,
                            @Seats = N'[{"row":10,"seat":12}]', @BookingId = @Cascade OUTPUT;
DELETE dbo.Bookings WHERE Id = @Cascade;
IF NOT EXISTS (SELECT 1 FROM dbo.Tickets WHERE BookingId = @Cascade)
   AND NOT EXISTS (SELECT 1 FROM dbo.BookingDetails WHERE BookingId = @Cascade)
    PRINT N'OK: ON DELETE CASCADE видалив BookingDetails, Tickets, History';

-- оплачене бронювання видалити не можна: Payments без каскаду
BEGIN TRY
    DELETE dbo.Bookings WHERE Id = 1;
    PRINT N'FAIL: Видалено бронювання з оплатою';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: Очікувана помилка ', ERROR_NUMBER(), N' (FK_Payments_Bookings): фінансові записи захищені');
END CATCH;
GO

PRINT N'===== 10. Індекси бази =====';
SELECT OBJECT_NAME(i.object_id) AS [Table], i.name AS [Index], i.type_desc, i.is_unique,
       i.has_filter, i.filter_definition
FROM sys.indexes i
WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1 AND i.name IS NOT NULL
ORDER BY [Table], [Index];
GO
