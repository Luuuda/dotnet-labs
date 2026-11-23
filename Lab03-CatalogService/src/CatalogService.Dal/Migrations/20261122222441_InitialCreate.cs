using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CatalogService.Dal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Genres",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Slug = table.Column<string>(type: "varchar(60)", unicode: false, maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Genres", x => x.Id);
                    table.CheckConstraint("CK_Genres_Slug", "[Slug] NOT LIKE '%[^a-z0-9-]%'");
                });

            migrationBuilder.CreateTable(
                name: "Halls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    HallType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Standard"),
                    RowsCount = table.Column<int>(type: "int", nullable: false),
                    SeatsPerRow = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Halls", x => x.Id);
                    table.CheckConstraint("CK_Halls_Size", "[RowsCount] BETWEEN 1 AND 50 AND [SeatsPerRow] BETWEEN 1 AND 60");
                    table.CheckConstraint("CK_Halls_Type", "[HallType] IN (N'Standard', N'IMAX', N'VIP', N'4DX')");
                });

            migrationBuilder.CreateTable(
                name: "Movies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OriginalTitle = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ReleaseYear = table.Column<short>(type: "smallint", nullable: false),
                    DurationMinutes = table.Column<short>(type: "smallint", nullable: false),
                    AgeRating = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: false, defaultValue: "0+"),
                    AverageRating = table.Column<decimal>(type: "decimal(3,2)", nullable: true),
                    RatingsCount = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movies", x => x.Id);
                    table.CheckConstraint("CK_Movies_Age", "[AgeRating] IN ('0+', '6+', '12+', '16+', '18+')");
                    table.CheckConstraint("CK_Movies_Duration", "[DurationMinutes] BETWEEN 1 AND 600");
                    table.CheckConstraint("CK_Movies_Rating", "[AverageRating] IS NULL OR [AverageRating] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_Movies_RatingsCount", "[RatingsCount] >= 0");
                    table.CheckConstraint("CK_Movies_Year", "[ReleaseYear] BETWEEN 1895 AND 2100");
                });

            migrationBuilder.CreateTable(
                name: "MovieDetails",
                columns: table => new
                {
                    MovieId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Director = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Country = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Language = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    PosterUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    TrailerUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieDetails", x => x.MovieId);
                    table.ForeignKey(
                        name: "FK_MovieDetails_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MovieGenres",
                columns: table => new
                {
                    MovieId = table.Column<int>(type: "int", nullable: false),
                    GenreId = table.Column<int>(type: "int", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    IsPrimary = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MovieGenres", x => new { x.MovieId, x.GenreId });
                    table.ForeignKey(
                        name: "FK_MovieGenres_Genres_GenreId",
                        column: x => x.GenreId,
                        principalTable: "Genres",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MovieGenres_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Screenings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MovieId = table.Column<int>(type: "int", nullable: false),
                    HallId = table.Column<int>(type: "int", nullable: false),
                    StartsAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "datetime2(0)", nullable: false),
                    Format = table.Column<string>(type: "varchar(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "2D"),
                    Language = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, defaultValue: "українська"),
                    BasePrice = table.Column<decimal>(type: "decimal(10,2)", nullable: false),
                    IsCancelled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "system"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Screenings", x => x.Id);
                    table.CheckConstraint("CK_Screenings_Format", "[Format] IN ('2D', '3D', 'IMAX', '4DX')");
                    table.CheckConstraint("CK_Screenings_Price", "[BasePrice] > 0");
                    table.CheckConstraint("CK_Screenings_Time", "[EndsAt] > [StartsAt]");
                    table.ForeignKey(
                        name: "FK_Screenings_Halls_HallId",
                        column: x => x.HallId,
                        principalTable: "Halls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Screenings_Movies_MovieId",
                        column: x => x.MovieId,
                        principalTable: "Movies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Genres",
                columns: new[] { "Id", "Name", "Slug" },
                values: new object[,]
                {
                    { 1, "Фантастика", "sci-fi" },
                    { 2, "Драма", "drama" },
                    { 3, "Історичний", "historical" },
                    { 4, "Анімація", "animation" },
                    { 5, "Пригоди", "adventure" },
                    { 6, "Біографічний", "biography" },
                    { 7, "Сімейний", "family" }
                });

            migrationBuilder.InsertData(
                table: "Halls",
                columns: new[] { "Id", "HallType", "IsActive", "Name", "RowsCount", "SeatsPerRow" },
                values: new object[,]
                {
                    { 1, "Standard", true, "Зала 1", 10, 12 },
                    { 2, "IMAX", true, "IMAX", 14, 20 },
                    { 3, "VIP", true, "VIP", 5, 8 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Name",
                table: "Genres",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Genres_Slug",
                table: "Genres",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Halls_Name",
                table: "Halls",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MovieGenres_GenreId_AddedAt",
                table: "MovieGenres",
                columns: new[] { "GenreId", "AddedAt" })
                .Annotation("SqlServer:Include", new[] { "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_Movies_ReleaseYear_Active",
                table: "Movies",
                column: "ReleaseYear",
                filter: "[IsDeleted] = 0")
                .Annotation("SqlServer:Include", new[] { "Title", "AgeRating", "DurationMinutes" });

            migrationBuilder.CreateIndex(
                name: "UQ_Movies_Title_Year",
                table: "Movies",
                columns: new[] { "Title", "ReleaseYear" },
                unique: true,
                filter: "[IsDeleted] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Screenings_HallId_StartsAt",
                table: "Screenings",
                columns: new[] { "HallId", "StartsAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Screenings_MovieId_StartsAt",
                table: "Screenings",
                columns: new[] { "MovieId", "StartsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Screenings_StartsAt_Active",
                table: "Screenings",
                column: "StartsAt",
                filter: "[IsCancelled] = 0")
                .Annotation("SqlServer:Include", new[] { "MovieId", "HallId", "Format", "BasePrice" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MovieDetails");

            migrationBuilder.DropTable(
                name: "MovieGenres");

            migrationBuilder.DropTable(
                name: "Screenings");

            migrationBuilder.DropTable(
                name: "Genres");

            migrationBuilder.DropTable(
                name: "Halls");

            migrationBuilder.DropTable(
                name: "Movies");
        }
    }
}
