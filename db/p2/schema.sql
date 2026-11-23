/* =====================================================================
   Проєкт №2 — CinemaCatalog (Каталог фільмів і розклад сеансів)
   01_schema.sql — 6 сутностей, усі типи зв'язків.
   У Лаб. 03 ця схема буде відтворена EF Core (Code First + Fluent API)
   і далі змінюватиметься ЛИШЕ міграціями; цей скрипт — проєкт схеми,
   за яким пишуться сутності та конфігурації.

   Зв'язки:
     1:1  Movies  <-> MovieDetails          (MovieId = PK і FK)
     1:N  Movies   -> Screenings,  Halls -> Screenings
     M:N  Movies  <-> Genres через MovieGenres (власні поля AddedAt, IsPrimary)

   Типи PK:
     INT IDENTITY    — Movies, Genres, Halls: невеликі довідники, один джерело вставки.
     BIGINT IDENTITY — Screenings: росте щодня (тисячі сеансів на рік у мережі),
                       Id передається в CinemaBooking як посилання.
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
USE CinemaCatalog;
GO
SET NOCOUNT ON;
GO

IF OBJECT_ID(N'dbo.Genres', N'U') IS NULL
CREATE TABLE dbo.Genres (
    Id        INT IDENTITY(1,1) NOT NULL,
    Name      NVARCHAR(60)  NOT NULL,
    Slug      VARCHAR(60)   NOT NULL,                -- для URL: /genres/sci-fi
    CONSTRAINT PK_Genres PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Genres_Name UNIQUE (Name),
    CONSTRAINT UQ_Genres_Slug UNIQUE (Slug),           -- запит: «знайти жанр за slug з URL»
    CONSTRAINT CK_Genres_Slug CHECK (Slug NOT LIKE '%[^a-z0-9-]%')
);
GO

IF OBJECT_ID(N'dbo.Movies', N'U') IS NULL
CREATE TABLE dbo.Movies (
    Id              INT IDENTITY(1,1) NOT NULL,
    Title           NVARCHAR(200) NOT NULL,
    OriginalTitle   NVARCHAR(200) NULL,
    ReleaseYear     SMALLINT      NOT NULL,
    DurationMinutes SMALLINT      NOT NULL,
    AgeRating       VARCHAR(3)    NOT NULL CONSTRAINT DF_Movies_AgeRating DEFAULT '0+',
    -- копія агрегату з сервісу Відгуків (MongoDB movieRatings), оновлюється подією MovieRatingChanged
    AverageRating   DECIMAL(3,2)  NULL,
    RatingsCount    INT           NOT NULL CONSTRAINT DF_Movies_RatingsCount DEFAULT 0,
    CreatedAt       DATETIME2(3)  NOT NULL CONSTRAINT DF_Movies_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy       NVARCHAR(100) NOT NULL CONSTRAINT DF_Movies_CreatedBy DEFAULT N'system',
    UpdatedAt       DATETIME2(3)  NULL,
    UpdatedBy       NVARCHAR(100) NULL,
    IsDeleted       BIT           NOT NULL CONSTRAINT DF_Movies_IsDeleted DEFAULT 0,
    RowVersion      ROWVERSION    NOT NULL,           -- картку фільму редагують кілька контент-менеджерів
    CONSTRAINT PK_Movies PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Movies_Title_Year UNIQUE (Title, ReleaseYear),
    CONSTRAINT CK_Movies_Year     CHECK (ReleaseYear BETWEEN 1895 AND 2100),
    CONSTRAINT CK_Movies_Duration CHECK (DurationMinutes BETWEEN 1 AND 600),
    CONSTRAINT CK_Movies_Age      CHECK (AgeRating IN ('0+', '6+', '12+', '16+', '18+')),
    CONSTRAINT CK_Movies_Rating   CHECK (AverageRating IS NULL OR AverageRating BETWEEN 1 AND 5),
    CONSTRAINT CK_Movies_RatingsCount CHECK (RatingsCount >= 0)
);
GO

/* 1:1 — рідко потрібні «важкі» поля винесені окремо, щоб список фільмів читався швидко */
IF OBJECT_ID(N'dbo.MovieDetails', N'U') IS NULL
CREATE TABLE dbo.MovieDetails (
    MovieId     INT            NOT NULL,
    Description NVARCHAR(4000) NOT NULL,
    Director    NVARCHAR(200)  NULL,
    Country     NVARCHAR(100)  NULL,
    Language    NVARCHAR(50)   NULL,
    PosterUrl   NVARCHAR(500)  NULL,
    TrailerUrl  NVARCHAR(500)  NULL,
    CONSTRAINT PK_MovieDetails PRIMARY KEY CLUSTERED (MovieId),
    CONSTRAINT FK_MovieDetails_Movies FOREIGN KEY (MovieId) REFERENCES dbo.Movies(Id) ON DELETE CASCADE
);
GO

/* M:N з власними полями */
IF OBJECT_ID(N'dbo.MovieGenres', N'U') IS NULL
CREATE TABLE dbo.MovieGenres (
    MovieId   INT          NOT NULL,
    GenreId   INT          NOT NULL,
    AddedAt   DATETIME2(3) NOT NULL CONSTRAINT DF_MovieGenres_AddedAt DEFAULT SYSUTCDATETIME(),
    IsPrimary BIT          NOT NULL CONSTRAINT DF_MovieGenres_IsPrimary DEFAULT 0,
    CONSTRAINT PK_MovieGenres PRIMARY KEY CLUSTERED (MovieId, GenreId),
    CONSTRAINT FK_MovieGenres_Movies FOREIGN KEY (MovieId) REFERENCES dbo.Movies(Id) ON DELETE CASCADE,
    -- без каскаду: жанр, що використовується, видалити не можна
    CONSTRAINT FK_MovieGenres_Genres FOREIGN KEY (GenreId) REFERENCES dbo.Genres(Id)
);
GO

IF OBJECT_ID(N'dbo.Halls', N'U') IS NULL
CREATE TABLE dbo.Halls (
    Id          INT IDENTITY(1,1) NOT NULL,
    Name        NVARCHAR(100) NOT NULL,
    HallType    NVARCHAR(20)  NOT NULL CONSTRAINT DF_Halls_HallType DEFAULT N'Standard',
    RowsCount   INT           NOT NULL,
    SeatsPerRow INT           NOT NULL,
    IsActive    BIT           NOT NULL CONSTRAINT DF_Halls_IsActive DEFAULT 1,
    CONSTRAINT PK_Halls PRIMARY KEY CLUSTERED (Id),
    CONSTRAINT UQ_Halls_Name UNIQUE (Name),
    CONSTRAINT CK_Halls_Type CHECK (HallType IN (N'Standard', N'IMAX', N'VIP', N'4DX')),
    CONSTRAINT CK_Halls_Size CHECK (RowsCount BETWEEN 1 AND 50 AND SeatsPerRow BETWEEN 1 AND 60)
);
GO

IF OBJECT_ID(N'dbo.Screenings', N'U') IS NULL
CREATE TABLE dbo.Screenings (
    Id          BIGINT IDENTITY(1,1) NOT NULL,
    MovieId     INT           NOT NULL,
    HallId      INT           NOT NULL,
    StartsAt    DATETIME2(0)  NOT NULL,          -- UTC
    EndsAt      DATETIME2(0)  NOT NULL,
    Format      VARCHAR(10)   NOT NULL CONSTRAINT DF_Screenings_Format DEFAULT '2D',
    Language    NVARCHAR(30)  NOT NULL CONSTRAINT DF_Screenings_Language DEFAULT N'українська',
    BasePrice   DECIMAL(10,2) NOT NULL,
    IsCancelled BIT           NOT NULL CONSTRAINT DF_Screenings_IsCancelled DEFAULT 0,
    CreatedAt   DATETIME2(3)  NOT NULL CONSTRAINT DF_Screenings_CreatedAt DEFAULT SYSUTCDATETIME(),
    CreatedBy   NVARCHAR(100) NOT NULL CONSTRAINT DF_Screenings_CreatedBy DEFAULT N'system',
    UpdatedAt   DATETIME2(3)  NULL,
    UpdatedBy   NVARCHAR(100) NULL,
    RowVersion  ROWVERSION    NOT NULL,
    CONSTRAINT PK_Screenings PRIMARY KEY CLUSTERED (Id),
    -- 1:N; без каскаду: на сеанс посилаються продані квитки (в іншому сервісі)
    CONSTRAINT FK_Screenings_Movies FOREIGN KEY (MovieId) REFERENCES dbo.Movies(Id),
    CONSTRAINT FK_Screenings_Halls  FOREIGN KEY (HallId)  REFERENCES dbo.Halls(Id),
    CONSTRAINT UQ_Screenings_Hall_StartsAt UNIQUE (HallId, StartsAt),  -- два сеанси в одній залі одночасно неможливі
    CONSTRAINT CK_Screenings_Time   CHECK (EndsAt > StartsAt),
    CONSTRAINT CK_Screenings_Price  CHECK (BasePrice > 0),
    CONSTRAINT CK_Screenings_Format CHECK (Format IN ('2D', '3D', 'IMAX', '4DX'))
);
GO

/* =====================================================================
   ІНДЕКСИ — під конкретні запити
   ===================================================================== */

-- Запит: «пошук фільму за назвою» (WHERE Title LIKE N'Дюна%' AND IsDeleted = 0).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Movies_Title_Active')
    CREATE INDEX IX_Movies_Title_Active ON dbo.Movies (Title)
        INCLUDE (ReleaseYear, AgeRating, DurationMinutes, AverageRating) WHERE IsDeleted = 0;

-- Запит: «фільми жанру X» (PK MovieGenres починається з MovieId, тож для пошуку від жанру потрібен зворотний індекс).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_MovieGenres_GenreId')
    CREATE INDEX IX_MovieGenres_GenreId ON dbo.MovieGenres (GenreId, MovieId) INCLUDE (IsPrimary);

-- Запит: «розклад на день» (StartsAt BETWEEN @DayStart AND @DayEnd, лише не скасовані).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Screenings_StartsAt_Active')
    CREATE INDEX IX_Screenings_StartsAt_Active ON dbo.Screenings (StartsAt)
        INCLUDE (MovieId, HallId, Format, BasePrice) WHERE IsCancelled = 0;

-- Запит: «найближчі сеанси фільму» (сторінка фільму).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Screenings_MovieId_StartsAt')
    CREATE INDEX IX_Screenings_MovieId_StartsAt ON dbo.Screenings (MovieId, StartsAt)
        INCLUDE (HallId, Format, BasePrice, IsCancelled);
GO

/* Представлення для читання: розклад з назвами (денормалізація для UI) */
CREATE OR ALTER VIEW dbo.vw_Schedule
AS
SELECT s.Id AS ScreeningId, s.StartsAt, s.EndsAt, s.Format, s.Language, s.BasePrice, s.IsCancelled,
       m.Id AS MovieId, m.Title AS MovieTitle, m.AgeRating, m.DurationMinutes,
       h.Id AS HallId, h.Name AS HallName, h.HallType, h.RowsCount, h.SeatsPerRow
FROM dbo.Screenings s
JOIN dbo.Movies m ON m.Id = s.MovieId
JOIN dbo.Halls  h ON h.Id = s.HallId
WHERE m.IsDeleted = 0;
GO

PRINT N'CinemaCatalog: схему створено.';
