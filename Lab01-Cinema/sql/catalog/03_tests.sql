/* =====================================================================
   Проєкт №2 — CinemaCatalog
   03_tests.sql — запити перевірки: зв'язки, CRUD, пошук, пагінація, обмеження.
   Повторний запуск безпечний (зміни робляться в транзакції з ROLLBACK).
   ===================================================================== */
SET QUOTED_IDENTIFIER ON;   -- потрібно для фільтрованих індексів (sqlcmd за замовчуванням вимикає)
SET ANSI_NULLS ON;
GO
USE CinemaCatalog;
GO
SET NOCOUNT ON;

PRINT N'===== 1. FOREIGN KEY лише в межах бази =====';
SELECT fk.name AS ForeignKey, OBJECT_NAME(fk.parent_object_id) AS FromTable,
       OBJECT_NAME(fk.referenced_object_id) AS ToTable, fk.delete_referential_action_desc AS OnDelete
FROM sys.foreign_keys fk ORDER BY FromTable;
GO

PRINT N'===== 2. 1:1 — фільм з деталями =====';
SELECT m.Id, m.Title, m.ReleaseYear, m.AgeRating, d.Director, d.Country, d.Description
FROM dbo.Movies m JOIN dbo.MovieDetails d ON d.MovieId = m.Id
WHERE m.Id = 3;

PRINT N'===== 3. M:N — жанри фільмів (основний жанр першим) =====';
SELECT m.Title,
       STRING_AGG(g.Name, N', ') WITHIN GROUP (ORDER BY mg.IsPrimary DESC, g.Name) AS Genres
FROM dbo.Movies m
JOIN dbo.MovieGenres mg ON mg.MovieId = m.Id
JOIN dbo.Genres g       ON g.Id = mg.GenreId
GROUP BY m.Title;

PRINT N'Пошук фільмів жанру «adventure» (IX_MovieGenres_GenreId):';
SELECT m.Id, m.Title, mg.AddedAt
FROM dbo.Genres g
JOIN dbo.MovieGenres mg ON mg.GenreId = g.Id
JOIN dbo.Movies m       ON m.Id = mg.MovieId AND m.IsDeleted = 0
WHERE g.Slug = 'adventure';

PRINT N'===== 4. 1:N — розклад на 05.12.2026 (IX_Screenings_StartsAt_Active) =====';
SELECT ScreeningId, StartsAt, MovieTitle, HallName, Format, BasePrice
FROM dbo.vw_Schedule
WHERE StartsAt >= '2026-12-05' AND StartsAt < '2026-12-06' AND IsCancelled = 0
ORDER BY StartsAt;

PRINT N'===== 5. Пошук за назвою + пагінація (IX_Movies_Title_Active) =====';
DECLARE @PageNumber INT = 1, @PageSize INT = 2;
SELECT Id, Title, ReleaseYear, AverageRating, COUNT(*) OVER () AS TotalCount
FROM dbo.Movies
WHERE IsDeleted = 0 AND Title LIKE N'%а%'
ORDER BY Title
OFFSET (@PageNumber - 1) * @PageSize ROWS FETCH NEXT @PageSize ROWS ONLY;
GO

PRINT N'===== 6. CRUD + обмеження (у транзакції, яка відкочується) =====';
BEGIN TRANSACTION;
    INSERT dbo.Movies (Title, ReleaseYear, DurationMinutes, AgeRating, CreatedBy)
    VALUES (N'Тестовий фільм', 2026, 100, '6+', N'test');
    DECLARE @NewId INT = SCOPE_IDENTITY();
    INSERT dbo.MovieDetails (MovieId, Description) VALUES (@NewId, N'Опис');
    INSERT dbo.MovieGenres (MovieId, GenreId, IsPrimary) VALUES (@NewId, 7, 1);
    PRINT CONCAT(N'OK: Create: фільм #', @NewId);

    UPDATE dbo.Movies SET Title = N'Тестовий фільм (оновлено)', UpdatedAt = SYSUTCDATETIME(), UpdatedBy = N'test'
    WHERE Id = @NewId;
    PRINT N'OK: Update';

    DELETE dbo.Movies WHERE Id = @NewId;  -- каскад прибирає MovieDetails і MovieGenres
    IF NOT EXISTS (SELECT 1 FROM dbo.MovieGenres WHERE MovieId = @NewId)
        PRINT N'OK: Delete + ON DELETE CASCADE для MovieDetails/MovieGenres';
ROLLBACK TRANSACTION;

BEGIN TRY
    INSERT dbo.Screenings (MovieId, HallId, StartsAt, EndsAt, BasePrice)
    VALUES (1, 1, '2026-12-10T10:00:00', '2026-12-10T12:00:00', 0);
    PRINT N'FAIL: Ціну 0 прийнято';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: CHECK CK_Screenings_Price: ', ERROR_NUMBER(), N' ', ERROR_MESSAGE());
END CATCH;

BEGIN TRY
    INSERT dbo.Screenings (MovieId, HallId, StartsAt, EndsAt, BasePrice)
    VALUES (3, 2, '2026-12-05T16:00:00', '2026-12-05T19:00:00', 200);
    PRINT N'FAIL: Два сеанси в одній залі одночасно';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: UNIQUE UQ_Screenings_Hall_StartsAt: ', ERROR_NUMBER());
END CATCH;

BEGIN TRY
    DELETE dbo.Genres WHERE Id = 1;
    PRINT N'FAIL: Видалено жанр, що використовується';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: FK_MovieGenres_Genres блокує видалення жанру: ', ERROR_NUMBER());
END CATCH;

BEGIN TRY
    INSERT dbo.Movies (Title, ReleaseYear, DurationMinutes, AgeRating) VALUES (N'X', 2026, 90, '21+');
    PRINT N'FAIL: Некоректний віковий рейтинг';
END TRY
BEGIN CATCH
    PRINT CONCAT(N'OK: CHECK CK_Movies_Age: ', ERROR_NUMBER());
END CATCH;
GO

PRINT N'===== 7. Індекси бази =====';
SELECT OBJECT_NAME(i.object_id) AS [Table], i.name AS [Index], i.type_desc, i.is_unique, i.filter_definition
FROM sys.indexes i
WHERE OBJECTPROPERTY(i.object_id, 'IsUserTable') = 1 AND i.name IS NOT NULL
ORDER BY [Table], [Index];
GO
