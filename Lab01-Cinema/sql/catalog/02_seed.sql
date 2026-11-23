/* =====================================================================
   Проєкт №2 — CinemaCatalog
   02_seed.sql — довідникові (жанри, зали) і тестові дані (фільми, сеанси).
   Ідемпотентний: MERGE ... WHEN NOT MATCHED — повторний запуск не дублює.
   Фіксовані Id потрібні, бо на них посилаються CinemaBooking і MongoDB.
   У Лаб. 03 ці ж дані переїдуть у HasData() Fluent API.
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
USE CinemaCatalog;
GO
SET NOCOUNT ON;
SET XACT_ABORT ON;
BEGIN TRANSACTION;

/* ---------- Genres (довідник) ---------- */
SET IDENTITY_INSERT dbo.Genres ON;
MERGE dbo.Genres AS t
USING (VALUES (1, N'Фантастика',   'sci-fi'),
              (2, N'Драма',        'drama'),
              (3, N'Історичний',   'historical'),
              (4, N'Анімація',     'animation'),
              (5, N'Пригоди',      'adventure'),
              (6, N'Біографічний', 'biography'),
              (7, N'Сімейний',     'family')) AS s (Id, Name, Slug)
ON t.Id = s.Id
WHEN NOT MATCHED BY TARGET THEN INSERT (Id, Name, Slug) VALUES (s.Id, s.Name, s.Slug);
SET IDENTITY_INSERT dbo.Genres OFF;

/* ---------- Halls (довідник) ---------- */
SET IDENTITY_INSERT dbo.Halls ON;
MERGE dbo.Halls AS t
USING (VALUES (1, N'Зала 1', N'Standard', 10, 12),
              (2, N'IMAX',   N'IMAX',     14, 20),
              (3, N'VIP',    N'VIP',       5,  8)) AS s (Id, Name, HallType, RowsCount, SeatsPerRow)
ON t.Id = s.Id
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Name, HallType, RowsCount, SeatsPerRow) VALUES (s.Id, s.Name, s.HallType, s.RowsCount, s.SeatsPerRow);
SET IDENTITY_INSERT dbo.Halls OFF;

/* ---------- Movies ---------- */
SET IDENTITY_INSERT dbo.Movies ON;
MERGE dbo.Movies AS t
USING (VALUES
    (1, N'Довбуш',               NULL,               2023, 115, '12+', 4.50, 2),
    (2, N'Оппенгеймер',          N'Oppenheimer',     2023, 180, '16+', 5.00, 1),
    (3, N'Дюна: Частина друга',  N'Dune: Part Two',  2024, 166, '12+', 4.33, 3),
    (4, N'Мавка. Лісова пісня',  NULL,               2023,  99, '0+',  4.00, 1),
    (5, N'Головоломка 2',        N'Inside Out 2',    2024,  96, '0+',  NULL, 0)
) AS s (Id, Title, OriginalTitle, ReleaseYear, DurationMinutes, AgeRating, AverageRating, RatingsCount)
ON t.Id = s.Id
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, Title, OriginalTitle, ReleaseYear, DurationMinutes, AgeRating, AverageRating, RatingsCount, CreatedBy)
    VALUES (s.Id, s.Title, s.OriginalTitle, s.ReleaseYear, s.DurationMinutes, s.AgeRating, s.AverageRating, s.RatingsCount, N'seed');
SET IDENTITY_INSERT dbo.Movies OFF;

/* ---------- MovieDetails (1:1) ---------- */
MERGE dbo.MovieDetails AS t
USING (VALUES
    (1, N'Історія карпатського опришка Олекси Довбуша у XVIII столітті.',      N'Олесь Санін',        N'Україна',        N'українська'),
    (2, N'Біографічна драма про фізика, який очолив Мангеттенський проєкт.',   N'Крістофер Нолан',    N'США',            N'англійська'),
    (3, N'Пол Атрід об''єднується з фрименами, щоб помститися за свою родину.',N'Дені Вільнев',       N'США, Канада',    N'англійська'),
    (4, N'Анімаційна казка за мотивами «Лісової пісні» Лесі Українки.',        N'Олександра Рубан',   N'Україна',        N'українська'),
    (5, N'Емоції дівчинки-підлітка зустрічають нових сусідів у її голові.',    N'Келсі Манн',         N'США',            N'англійська')
) AS s (MovieId, Description, Director, Country, Language)
ON t.MovieId = s.MovieId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (MovieId, Description, Director, Country, Language)
    VALUES (s.MovieId, s.Description, s.Director, s.Country, s.Language);

/* ---------- MovieGenres (M:N з власними полями) ---------- */
MERGE dbo.MovieGenres AS t
USING (VALUES (1, 3, 1, '2026-09-01'), (1, 5, 0, '2026-09-01'), (1, 2, 0, '2026-09-15'),
              (2, 6, 1, '2026-09-01'), (2, 2, 0, '2026-09-01'), (2, 3, 0, '2026-10-02'),
              (3, 1, 1, '2026-09-01'), (3, 5, 0, '2026-09-01'), (3, 2, 0, '2026-09-01'),
              (4, 4, 1, '2026-09-05'), (4, 7, 0, '2026-09-05'), (4, 5, 0, '2026-09-05'),
              (5, 4, 1, '2026-10-10'), (5, 7, 0, '2026-10-10')) AS s (MovieId, GenreId, IsPrimary, AddedAt)
ON t.MovieId = s.MovieId AND t.GenreId = s.GenreId
WHEN NOT MATCHED BY TARGET THEN
    INSERT (MovieId, GenreId, IsPrimary, AddedAt) VALUES (s.MovieId, s.GenreId, s.IsPrimary, s.AddedAt);

/* ---------- Screenings (EndsAt = старт + тривалість + 15 хв прибирання) ---------- */
SET IDENTITY_INSERT dbo.Screenings ON;
MERGE dbo.Screenings AS t
USING (
    SELECT v.Id, v.MovieId, v.HallId, v.StartsAt,
           DATEADD(MINUTE, m.DurationMinutes + 15, v.StartsAt) AS EndsAt,
           v.Format, v.Language, v.BasePrice, v.IsCancelled
    FROM (VALUES
        (1, 3, 2, CAST('2026-12-05T16:00:00' AS DATETIME2(0)), 'IMAX', N'англійська, субтитри', 250.00, 0),
        (2, 3, 1, CAST('2026-12-05T19:30:00' AS DATETIME2(0)), '2D',   N'українська',           160.00, 0),
        (3, 1, 1, CAST('2026-12-05T14:00:00' AS DATETIME2(0)), '2D',   N'українська',           140.00, 0),
        (4, 2, 3, CAST('2026-12-06T17:00:00' AS DATETIME2(0)), '2D',   N'англійська, субтитри', 380.00, 0),
        (5, 4, 1, CAST('2026-12-06T10:00:00' AS DATETIME2(0)), '2D',   N'українська',           120.00, 0),
        (6, 5, 2, CAST('2026-12-06T12:00:00' AS DATETIME2(0)), '3D',   N'українська',           220.00, 0),
        (7, 3, 2, CAST('2026-12-07T18:00:00' AS DATETIME2(0)), 'IMAX', N'українська',           250.00, 0),
        (8, 1, 3, CAST('2026-12-07T16:30:00' AS DATETIME2(0)), '2D',   N'українська',           300.00, 1)
    ) AS v (Id, MovieId, HallId, StartsAt, Format, Language, BasePrice, IsCancelled)
    JOIN dbo.Movies m ON m.Id = v.MovieId
) AS s
ON t.Id = s.Id
WHEN NOT MATCHED BY TARGET THEN
    INSERT (Id, MovieId, HallId, StartsAt, EndsAt, Format, Language, BasePrice, IsCancelled, CreatedBy)
    VALUES (s.Id, s.MovieId, s.HallId, s.StartsAt, s.EndsAt, s.Format, s.Language, s.BasePrice, s.IsCancelled, N'seed');
SET IDENTITY_INSERT dbo.Screenings OFF;

COMMIT TRANSACTION;
GO

SELECT N'Genres' AS [Table], COUNT(*) AS [Rows] FROM dbo.Genres
UNION ALL SELECT N'Halls',        COUNT(*) FROM dbo.Halls
UNION ALL SELECT N'Movies',       COUNT(*) FROM dbo.Movies
UNION ALL SELECT N'MovieDetails', COUNT(*) FROM dbo.MovieDetails
UNION ALL SELECT N'MovieGenres',  COUNT(*) FROM dbo.MovieGenres
UNION ALL SELECT N'Screenings',   COUNT(*) FROM dbo.Screenings;
GO
