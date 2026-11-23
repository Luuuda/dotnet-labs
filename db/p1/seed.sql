/* =====================================================================
   Проєкт №1 — CinemaBooking
   03_seed.sql — тестові дані. Ідемпотентний: кожна вставка під
   IF NOT EXISTS / MERGE, повторний запуск НЕ створює дублікатів.
   Id сеансів збігаються з CinemaCatalog (локальна копія довідника).
   Час — UTC.
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
USE CinemaBooking;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------- Customers (фіксовані GUID — на них посилаються відгуки в MongoDB) ---------- */
MERGE dbo.Customers AS t
USING (VALUES
    ('6F1C2A10-0000-4000-8000-000000000001', N'olena.kovalenko@example.com', N'Олена Коваленко', '+380671112233'),
    ('6F1C2A10-0000-4000-8000-000000000002', N'andrii.shevchuk@example.com', N'Андрій Шевчук',   '+380502223344'),
    ('6F1C2A10-0000-4000-8000-000000000003', N'maria.bondar@example.com',    N'Марія Бондар',    '+380633334455'),
    ('6F1C2A10-0000-4000-8000-000000000004', N'taras.melnyk@example.com',    N'Тарас Мельник',   NULL)
) AS s (Id, Email, FullName, Phone)
ON t.Id = s.Id
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Email, FullName, Phone, CreatedBy) VALUES (s.Id, s.Email, s.FullName, s.Phone, N'seed');

/* ---------- Promotions ---------- */
IF NOT EXISTS (SELECT 1 FROM dbo.Promotions WHERE Code = N'STUDENT20')
    INSERT dbo.Promotions (Code, Title, DiscountPercent, ValidFrom, ValidTo, CreatedBy)
    VALUES (N'STUDENT20', N'Знижка для студентів', 20, '2026-01-01', '2028-01-01', N'seed');
IF NOT EXISTS (SELECT 1 FROM dbo.Promotions WHERE Code = N'WINTER10')
    INSERT dbo.Promotions (Code, Title, DiscountPercent, ValidFrom, ValidTo, CreatedBy)
    VALUES (N'WINTER10', N'Зимова акція', 10, '2026-12-01', '2027-03-01', N'seed');
IF NOT EXISTS (SELECT 1 FROM dbo.Promotions WHERE Code = N'SUMMER25')
    INSERT dbo.Promotions (Code, Title, DiscountPercent, ValidFrom, ValidTo, IsActive, CreatedBy)
    VALUES (N'SUMMER25', N'Літня акція (завершена)', 25, '2026-06-01', '2026-09-01', 0, N'seed');

/* ---------- Screenings: локальна копія сеансів Каталогу (ті самі Id) ---------- */
MERGE dbo.Screenings AS t
USING (VALUES
    (1, 3, N'Дюна: Частина друга',  2, N'IMAX',   14, 20, '2026-12-05T16:00:00', 250.00, 1),
    (2, 3, N'Дюна: Частина друга',  1, N'Зала 1', 10, 12, '2026-12-05T19:30:00', 160.00, 1),
    (3, 1, N'Довбуш',               1, N'Зала 1', 10, 12, '2026-12-05T14:00:00', 140.00, 1),
    (4, 2, N'Оппенгеймер',          3, N'VIP',     5,  8, '2026-12-06T17:00:00', 380.00, 1),
    (5, 4, N'Мавка. Лісова пісня',  1, N'Зала 1', 10, 12, '2026-12-06T10:00:00', 120.00, 1),
    (6, 5, N'Головоломка 2',        2, N'IMAX',   14, 20, '2026-12-06T12:00:00', 220.00, 1),
    (7, 3, N'Дюна: Частина друга',  2, N'IMAX',   14, 20, '2026-12-07T18:00:00', 250.00, 1),
    (8, 1, N'Довбуш',               3, N'VIP',     5,  8, '2026-12-07T16:30:00', 300.00, 0)  -- скасований у Каталозі
) AS s (Id, MovieId, MovieTitle, HallId, HallName, RowsCount, SeatsPerRow, StartsAt, BasePrice, IsActive)
ON t.Id = s.Id
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, MovieId, MovieTitle, HallId, HallName, RowsCount, SeatsPerRow, StartsAt, BasePrice, IsActive)
    VALUES (s.Id, s.MovieId, s.MovieTitle, s.HallId, s.HallName, s.RowsCount, s.SeatsPerRow, s.StartsAt, s.BasePrice, s.IsActive);

/* ---------- Bookings: 5 бронювань у різних статусах ----------
   Фіксовані Id (IDENTITY_INSERT) — щоб перевірка IF NOT EXISTS була надійною. */
DECLARE @Student INT = (SELECT Id FROM dbo.Promotions WHERE Code = N'STUDENT20');

-- #1 Олена, Дюна IMAX, 2 місця — пройшло весь цикл до Issued
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE Id = 1)
BEGIN
    SET IDENTITY_INSERT dbo.Bookings ON;
    INSERT dbo.Bookings (Id, CustomerId, PromotionId, Status, CreatedAt, CreatedBy)
    VALUES (1, '6F1C2A10-0000-4000-8000-000000000001', NULL, N'Issued', '2026-11-20T10:00:00', N'seed');
    SET IDENTITY_INSERT dbo.Bookings OFF;

    INSERT dbo.BookingDetails (BookingId, ContactEmail, ContactPhone, Channel)
    VALUES (1, N'olena.kovalenko@example.com', '+380671112233', N'Web');
    INSERT dbo.Tickets (BookingId, ScreeningId, SeatRow, SeatNumber, MovieTitle, HallName, StartsAt, UnitPrice)
    VALUES (1, 1, 7, 10, N'Дюна: Частина друга', N'IMAX', '2026-12-05T16:00:00', 250.00),
           (1, 1, 7, 11, N'Дюна: Частина друга', N'IMAX', '2026-12-05T16:00:00', 250.00);
    INSERT dbo.Payments (BookingId, Amount, Provider, ExternalTransactionId, PaidAt, CreatedBy)
    VALUES (1, 500.00, N'Card', N'TX-SEED-0001', '2026-11-20T10:05:00', N'seed');
    INSERT dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedAt, ChangedBy, Reason)
    VALUES (1, NULL,                  N'Submitted',          '2026-11-20T10:00:00', N'seed', N'Бронювання створено'),
           (1, N'Submitted',          N'AwaitingValidation', '2026-11-20T10:00:05', N'seed', NULL),
           (1, N'AwaitingValidation', N'SeatsConfirmed',     '2026-11-20T10:00:10', N'seed', NULL),
           (1, N'SeatsConfirmed',     N'Paid',               '2026-11-20T10:05:00', N'seed', N'Оплату отримано'),
           (1, N'Paid',               N'Issued',             '2026-11-20T10:05:30', N'seed', N'Квитки надіслано на email');
END;

-- #2 Андрій, Оппенгеймер VIP, промокод STUDENT20 — Paid
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE Id = 2)
BEGIN
    SET IDENTITY_INSERT dbo.Bookings ON;
    INSERT dbo.Bookings (Id, CustomerId, PromotionId, Status, CreatedAt, CreatedBy)
    VALUES (2, '6F1C2A10-0000-4000-8000-000000000002', @Student, N'Paid', '2026-11-20T12:00:00', N'seed');
    SET IDENTITY_INSERT dbo.Bookings OFF;

    INSERT dbo.BookingDetails (BookingId, ContactEmail, ContactPhone, Channel, Comment)
    VALUES (2, N'andrii.shevchuk@example.com', '+380502223344', N'Mobile', N'Студентський квиток покажу на вході');
    INSERT dbo.Tickets (BookingId, ScreeningId, SeatRow, SeatNumber, MovieTitle, HallName, StartsAt, UnitPrice, Discount)
    VALUES (2, 4, 2, 4, N'Оппенгеймер', N'VIP', '2026-12-06T17:00:00', 380.00, 76.00);
    INSERT dbo.Payments (BookingId, Amount, Provider, ExternalTransactionId, PaidAt, CreatedBy)
    VALUES (2, 304.00, N'GooglePay', N'TX-SEED-0002', '2026-11-20T12:03:00', N'seed');
    INSERT dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedAt, ChangedBy, Reason)
    VALUES (2, NULL,                  N'Submitted',          '2026-11-20T12:00:00', N'seed', N'Бронювання створено'),
           (2, N'Submitted',          N'AwaitingValidation', '2026-11-20T12:00:05', N'seed', NULL),
           (2, N'AwaitingValidation', N'SeatsConfirmed',     '2026-11-20T12:00:10', N'seed', NULL),
           (2, N'SeatsConfirmed',     N'Paid',               '2026-11-20T12:03:00', N'seed', N'Оплату отримано');
END;

-- #3 Марія, Дюна Зала 1, 3 місця — щойно створене (Submitted, НЕ оплачене)
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE Id = 3)
BEGIN
    SET IDENTITY_INSERT dbo.Bookings ON;
    INSERT dbo.Bookings (Id, CustomerId, PromotionId, Status, CreatedAt, CreatedBy)
    VALUES (3, '6F1C2A10-0000-4000-8000-000000000003', NULL, N'Submitted', '2026-11-21T09:00:00', N'seed');
    SET IDENTITY_INSERT dbo.Bookings OFF;

    INSERT dbo.BookingDetails (BookingId, ContactEmail, ContactPhone, Channel)
    VALUES (3, N'maria.bondar@example.com', '+380633334455', N'Web');
    INSERT dbo.Tickets (BookingId, ScreeningId, SeatRow, SeatNumber, MovieTitle, HallName, StartsAt, UnitPrice)
    VALUES (3, 2, 5, 6, N'Дюна: Частина друга', N'Зала 1', '2026-12-05T19:30:00', 160.00),
           (3, 2, 5, 7, N'Дюна: Частина друга', N'Зала 1', '2026-12-05T19:30:00', 160.00),
           (3, 2, 5, 8, N'Дюна: Частина друга', N'Зала 1', '2026-12-05T19:30:00', 160.00);
    INSERT dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedAt, ChangedBy, Reason)
    VALUES (3, NULL, N'Submitted', '2026-11-21T09:00:00', N'seed', N'Бронювання створено');
END;

-- #4 Олена, Мавка — скасоване до оплати (місце звільнене)
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE Id = 4)
BEGIN
    SET IDENTITY_INSERT dbo.Bookings ON;
    INSERT dbo.Bookings (Id, CustomerId, PromotionId, Status, CreatedAt, CreatedBy)
    VALUES (4, '6F1C2A10-0000-4000-8000-000000000001', NULL, N'Cancelled', '2026-11-19T18:00:00', N'seed');
    SET IDENTITY_INSERT dbo.Bookings OFF;

    INSERT dbo.BookingDetails (BookingId, ContactEmail, ContactPhone, Channel)
    VALUES (4, N'olena.kovalenko@example.com', '+380671112233', N'BoxOffice');
    INSERT dbo.Tickets (BookingId, ScreeningId, SeatRow, SeatNumber, MovieTitle, HallName, StartsAt, UnitPrice, IsCancelled)
    VALUES (4, 5, 3, 3, N'Мавка. Лісова пісня', N'Зала 1', '2026-12-06T10:00:00', 120.00, 1);
    INSERT dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedAt, ChangedBy, Reason)
    VALUES (4, NULL,         N'Submitted', '2026-11-19T18:00:00', N'seed', N'Бронювання створено'),
           (4, N'Submitted', N'Cancelled', '2026-11-19T18:20:00', N'seed', N'Не оплачено вчасно');
END;

-- #5 Тарас, Головоломка 2 IMAX — місця підтверджено, чекає оплати (для тесту usp_Payment_Register)
IF NOT EXISTS (SELECT 1 FROM dbo.Bookings WHERE Id = 5)
BEGIN
    SET IDENTITY_INSERT dbo.Bookings ON;
    INSERT dbo.Bookings (Id, CustomerId, PromotionId, Status, CreatedAt, CreatedBy)
    VALUES (5, '6F1C2A10-0000-4000-8000-000000000004', NULL, N'SeatsConfirmed', '2026-11-21T11:00:00', N'seed');
    SET IDENTITY_INSERT dbo.Bookings OFF;

    INSERT dbo.BookingDetails (BookingId, ContactEmail, Channel)
    VALUES (5, N'taras.melnyk@example.com', N'Web');
    INSERT dbo.Tickets (BookingId, ScreeningId, SeatRow, SeatNumber, MovieTitle, HallName, StartsAt, UnitPrice)
    VALUES (5, 6, 8, 10, N'Головоломка 2', N'IMAX', '2026-12-06T12:00:00', 220.00),
           (5, 6, 8, 11, N'Головоломка 2', N'IMAX', '2026-12-06T12:00:00', 220.00);
    INSERT dbo.BookingStatusHistory (BookingId, FromStatus, ToStatus, ChangedAt, ChangedBy, Reason)
    VALUES (5, NULL,                  N'Submitted',          '2026-11-21T11:00:00', N'seed', N'Бронювання створено'),
           (5, N'Submitted',          N'AwaitingValidation', '2026-11-21T11:00:05', N'seed', NULL),
           (5, N'AwaitingValidation', N'SeatsConfirmed',     '2026-11-21T11:00:10', N'seed', NULL);
END;

COMMIT TRANSACTION;
GO

SELECT N'Customers' AS [Table], COUNT(*) AS [Rows] FROM dbo.Customers
UNION ALL SELECT N'Promotions', COUNT(*) FROM dbo.Promotions
UNION ALL SELECT N'Screenings', COUNT(*) FROM dbo.Screenings
UNION ALL SELECT N'Bookings',   COUNT(*) FROM dbo.Bookings
UNION ALL SELECT N'Tickets',    COUNT(*) FROM dbo.Tickets
UNION ALL SELECT N'Payments',   COUNT(*) FROM dbo.Payments
UNION ALL SELECT N'BookingStatusHistory', COUNT(*) FROM dbo.BookingStatusHistory;
GO
