IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
CREATE TABLE [Genres] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(60) NOT NULL,
    [Slug] varchar(60) NOT NULL,
    CONSTRAINT [PK_Genres] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Genres_Slug] CHECK ([Slug] NOT LIKE '%[^a-z0-9-]%')
);

CREATE TABLE [Halls] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(100) NOT NULL,
    [HallType] nvarchar(20) NOT NULL DEFAULT N'Standard',
    [RowsCount] int NOT NULL,
    [SeatsPerRow] int NOT NULL,
    [IsActive] bit NOT NULL,
    CONSTRAINT [PK_Halls] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Halls_Size] CHECK ([RowsCount] BETWEEN 1 AND 50 AND [SeatsPerRow] BETWEEN 1 AND 60),
    CONSTRAINT [CK_Halls_Type] CHECK ([HallType] IN (N'Standard', N'IMAX', N'VIP', N'4DX'))
);

CREATE TABLE [Movies] (
    [Id] int NOT NULL IDENTITY,
    [Title] nvarchar(200) NOT NULL,
    [OriginalTitle] nvarchar(200) NULL,
    [ReleaseYear] smallint NOT NULL,
    [DurationMinutes] smallint NOT NULL,
    [AgeRating] varchar(3) NOT NULL DEFAULT '0+',
    [AverageRating] decimal(3,2) NULL,
    [RatingsCount] int NOT NULL DEFAULT 0,
    [IsDeleted] bit NOT NULL DEFAULT CAST(0 AS bit),
    [RowVersion] rowversion NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
    [CreatedBy] nvarchar(100) NOT NULL DEFAULT N'system',
    [UpdatedAt] datetime2(3) NULL,
    [UpdatedBy] nvarchar(100) NULL,
    CONSTRAINT [PK_Movies] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Movies_Age] CHECK ([AgeRating] IN ('0+', '6+', '12+', '16+', '18+')),
    CONSTRAINT [CK_Movies_Duration] CHECK ([DurationMinutes] BETWEEN 1 AND 600),
    CONSTRAINT [CK_Movies_Rating] CHECK ([AverageRating] IS NULL OR [AverageRating] BETWEEN 1 AND 5),
    CONSTRAINT [CK_Movies_RatingsCount] CHECK ([RatingsCount] >= 0),
    CONSTRAINT [CK_Movies_Year] CHECK ([ReleaseYear] BETWEEN 1895 AND 2100)
);

CREATE TABLE [MovieDetails] (
    [MovieId] int NOT NULL,
    [Description] nvarchar(4000) NOT NULL,
    [Director] nvarchar(200) NULL,
    [Country] nvarchar(100) NULL,
    [Language] nvarchar(50) NULL,
    [PosterUrl] nvarchar(500) NULL,
    [TrailerUrl] nvarchar(500) NULL,
    CONSTRAINT [PK_MovieDetails] PRIMARY KEY ([MovieId]),
    CONSTRAINT [FK_MovieDetails_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [MovieGenres] (
    [MovieId] int NOT NULL,
    [GenreId] int NOT NULL,
    [AddedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
    [IsPrimary] bit NOT NULL DEFAULT CAST(0 AS bit),
    CONSTRAINT [PK_MovieGenres] PRIMARY KEY ([MovieId], [GenreId]),
    CONSTRAINT [FK_MovieGenres_Genres_GenreId] FOREIGN KEY ([GenreId]) REFERENCES [Genres] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_MovieGenres_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE CASCADE
);

CREATE TABLE [Screenings] (
    [Id] bigint NOT NULL IDENTITY,
    [MovieId] int NOT NULL,
    [HallId] int NOT NULL,
    [StartsAt] datetime2(0) NOT NULL,
    [EndsAt] datetime2(0) NOT NULL,
    [Format] varchar(10) NOT NULL DEFAULT '2D',
    [Language] nvarchar(30) NOT NULL DEFAULT N'українська',
    [BasePrice] decimal(10,2) NOT NULL,
    [IsCancelled] bit NOT NULL DEFAULT CAST(0 AS bit),
    [RowVersion] rowversion NOT NULL,
    [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
    [CreatedBy] nvarchar(100) NOT NULL DEFAULT N'system',
    [UpdatedAt] datetime2(3) NULL,
    [UpdatedBy] nvarchar(100) NULL,
    CONSTRAINT [PK_Screenings] PRIMARY KEY ([Id]),
    CONSTRAINT [CK_Screenings_Format] CHECK ([Format] IN ('2D', '3D', 'IMAX', '4DX')),
    CONSTRAINT [CK_Screenings_Price] CHECK ([BasePrice] > 0),
    CONSTRAINT [CK_Screenings_Time] CHECK ([EndsAt] > [StartsAt]),
    CONSTRAINT [FK_Screenings_Halls_HallId] FOREIGN KEY ([HallId]) REFERENCES [Halls] ([Id]) ON DELETE NO ACTION,
    CONSTRAINT [FK_Screenings_Movies_MovieId] FOREIGN KEY ([MovieId]) REFERENCES [Movies] ([Id]) ON DELETE NO ACTION
);

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Genres]'))
    SET IDENTITY_INSERT [Genres] ON;
INSERT INTO [Genres] ([Id], [Name], [Slug])
VALUES (1, N'Фантастика', 'sci-fi'),
(2, N'Драма', 'drama'),
(3, N'Історичний', 'historical'),
(4, N'Анімація', 'animation'),
(5, N'Пригоди', 'adventure'),
(6, N'Біографічний', 'biography'),
(7, N'Сімейний', 'family');
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Genres]'))
    SET IDENTITY_INSERT [Genres] OFF;

IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'HallType', N'IsActive', N'Name', N'RowsCount', N'SeatsPerRow') AND [object_id] = OBJECT_ID(N'[Halls]'))
    SET IDENTITY_INSERT [Halls] ON;
INSERT INTO [Halls] ([Id], [HallType], [IsActive], [Name], [RowsCount], [SeatsPerRow])
VALUES (1, N'Standard', CAST(1 AS bit), N'Зала 1', 10, 12),
(2, N'IMAX', CAST(1 AS bit), N'IMAX', 14, 20),
(3, N'VIP', CAST(1 AS bit), N'VIP', 5, 8);
IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'HallType', N'IsActive', N'Name', N'RowsCount', N'SeatsPerRow') AND [object_id] = OBJECT_ID(N'[Halls]'))
    SET IDENTITY_INSERT [Halls] OFF;

CREATE UNIQUE INDEX [IX_Genres_Name] ON [Genres] ([Name]);

CREATE UNIQUE INDEX [IX_Genres_Slug] ON [Genres] ([Slug]);

CREATE UNIQUE INDEX [IX_Halls_Name] ON [Halls] ([Name]);

CREATE INDEX [IX_MovieGenres_GenreId_AddedAt] ON [MovieGenres] ([GenreId], [AddedAt]) INCLUDE ([IsPrimary]);

CREATE INDEX [IX_Movies_ReleaseYear_Active] ON [Movies] ([ReleaseYear]) INCLUDE ([Title], [AgeRating], [DurationMinutes]) WHERE [IsDeleted] = 0;

CREATE UNIQUE INDEX [UQ_Movies_Title_Year] ON [Movies] ([Title], [ReleaseYear]) WHERE [IsDeleted] = 0;

CREATE UNIQUE INDEX [IX_Screenings_HallId_StartsAt] ON [Screenings] ([HallId], [StartsAt]);

CREATE INDEX [IX_Screenings_MovieId_StartsAt] ON [Screenings] ([MovieId], [StartsAt]);

CREATE INDEX [IX_Screenings_StartsAt_Active] ON [Screenings] ([StartsAt]) INCLUDE ([MovieId], [HallId], [Format], [BasePrice]) WHERE [IsCancelled] = 0;

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20261122222441_InitialCreate', N'9.0.23');

COMMIT;
GO

