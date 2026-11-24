/* =====================================================================
   00_init.sql — створення двох незалежних SQL-баз і окремих логінів
   Виконується від імені sa (БД master).
   Скрипт ідемпотентний: повторний запуск нічого не дублює.
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
SET NOCOUNT ON;

IF DB_ID(N'CinemaBooking') IS NULL
    CREATE DATABASE CinemaBooking COLLATE Ukrainian_100_CI_AS;   -- Проєкт №1: Бронювання
IF DB_ID(N'CinemaCatalog') IS NULL
    CREATE DATABASE CinemaCatalog COLLATE Ukrainian_100_CI_AS;   -- Проєкт №2: Каталог і розклад
GO

/* Принцип найменших привілеїв: кожен сервіс має свій логін,
   який має доступ ЛИШЕ до своєї бази. Паролі тут — для локальної розробки. */
IF SUSER_ID(N'cinema_booking_app') IS NULL
    CREATE LOGIN cinema_booking_app
        WITH PASSWORD = N'Booking_App#2026', DEFAULT_DATABASE = CinemaBooking, CHECK_POLICY = ON;
IF SUSER_ID(N'cinema_catalog_app') IS NULL
    CREATE LOGIN cinema_catalog_app
        WITH PASSWORD = N'Catalog_App#2026', DEFAULT_DATABASE = CinemaCatalog, CHECK_POLICY = ON;
GO

USE CinemaBooking;
GO
IF DATABASE_PRINCIPAL_ID(N'cinema_booking_app') IS NULL
    CREATE USER cinema_booking_app FOR LOGIN cinema_booking_app;
ALTER ROLE db_datareader ADD MEMBER cinema_booking_app;
ALTER ROLE db_datawriter ADD MEMBER cinema_booking_app;
GRANT EXECUTE ON SCHEMA::dbo TO cinema_booking_app;      -- виклик збережуваних процедур (ADO.NET/Dapper)
GO

USE CinemaCatalog;
GO
IF DATABASE_PRINCIPAL_ID(N'cinema_catalog_app') IS NULL
    CREATE USER cinema_catalog_app FOR LOGIN cinema_catalog_app;
ALTER ROLE db_datareader ADD MEMBER cinema_catalog_app;
ALTER ROLE db_datawriter ADD MEMBER cinema_catalog_app;
ALTER ROLE db_ddladmin   ADD MEMBER cinema_catalog_app;   -- EF Core застосовує міграції (Лаб. 03)
GO

PRINT N'00_init: бази CinemaBooking, CinemaCatalog і логіни готові.';
