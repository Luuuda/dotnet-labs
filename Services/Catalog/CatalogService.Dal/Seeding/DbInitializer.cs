using CatalogService.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CatalogService.Dal.Seeding;

/// <summary>
/// Програмний сідінг — після MigrateAsync(). Довідники (жанри, зали) вже є з міграції (HasData),
/// тому тут додаються залежні записи: фільми з деталями, зв'язки з жанрами, сеанси.
/// Ідемпотентний: якщо фільм із такою назвою й роком уже є — він пропускається; повторний запуск нічого не дублює.
/// Лаб. 08: назву фільму тепер змінюють через API (подія MovieRenamed), тому фільм вважається наявним і тоді, коли
/// збігаються рік і тривалість (перейменований «Довбуш» не створюється вдруге), а сеанс не додається, якщо зала в цей
/// час уже зайнята (унікальний індекс IX_Screenings_HallId_StartsAt).
/// </summary>
public static class DbInitializer
{
    private sealed record SeedMovie(string Title, string? Original, int Year, int Duration, string Age,
        string Description, string Director, string Country, string Language,
        (string Slug, bool Primary, string AddedAt)[] Genres);

    private static readonly SeedMovie[] Movies =
    [
        new("Довбуш", null, 2023, 115, "12+",
            "Історія карпатського опришка Олекси Довбуша у XVIII столітті.", "Олесь Санін", "Україна", "українська",
            [("historical", true, "2026-09-01"), ("adventure", false, "2026-09-01"), ("drama", false, "2026-09-15")]),
        new("Оппенгеймер", "Oppenheimer", 2023, 180, "16+",
            "Біографічна драма про фізика, який очолив Мангеттенський проєкт.", "Крістофер Нолан", "США", "англійська",
            [("biography", true, "2026-09-01"), ("drama", false, "2026-09-01"), ("historical", false, "2026-10-02")]),
        new("Дюна: Частина друга", "Dune: Part Two", 2024, 166, "12+",
            "Пол Атрід об'єднується з фрименами, щоб помститися за свою родину.", "Дені Вільнев", "США, Канада", "англійська",
            [("sci-fi", true, "2026-09-01"), ("adventure", false, "2026-09-01"), ("drama", false, "2026-09-01")]),
        new("Мавка. Лісова пісня", null, 2023, 99, "0+",
            "Анімаційна казка за мотивами «Лісової пісні» Лесі Українки.", "Олександра Рубан", "Україна", "українська",
            [("animation", true, "2026-09-05"), ("family", false, "2026-09-05"), ("adventure", false, "2026-09-05")]),
        new("Головоломка 2", "Inside Out 2", 2024, 96, "0+",
            "Емоції дівчинки-підлітка зустрічають нових сусідів у її голові.", "Келсі Манн", "США", "англійська",
            [("animation", true, "2026-10-10"), ("family", false, "2026-10-10")]),
        new("Захар Беркут", null, 2019, 112, "12+",
            "Екранізація повісті Івана Франка про оборону карпатського села від монголів.", "Ахтем Сеітаблаєв", "Україна", "українська",
            [("historical", true, "2026-11-01"), ("adventure", false, "2026-11-01")]),
        new("Інтерстеллар", "Interstellar", 2014, 169, "12+",
            "Команда дослідників вирушає крізь червоточину в пошуках нового дому для людства.", "Крістофер Нолан", "США", "англійська",
            [("sci-fi", true, "2026-11-10"), ("drama", false, "2026-11-10"), ("adventure", false, "2026-11-10")])
    ];

    // (назва фільму, Id зали, початок UTC, формат, мова, ціна, скасований)
    private static readonly (string Movie, int Hall, string StartsAt, string Format, string Language, decimal Price, bool Cancelled)[] Screenings =
    [
        ("Дюна: Частина друга", 2, "2026-12-05T16:00:00", "IMAX", "англійська, субтитри", 250m, false),
        ("Дюна: Частина друга", 1, "2026-12-05T19:30:00", "2D", "українська", 160m, false),
        ("Довбуш", 1, "2026-12-05T14:00:00", "2D", "українська", 140m, false),
        ("Оппенгеймер", 3, "2026-12-06T17:00:00", "2D", "англійська, субтитри", 380m, false),
        ("Мавка. Лісова пісня", 1, "2026-12-06T10:00:00", "2D", "українська", 120m, false),
        ("Головоломка 2", 2, "2026-12-06T12:00:00", "3D", "українська", 220m, false),
        ("Дюна: Частина друга", 2, "2026-12-07T18:00:00", "IMAX", "українська", 250m, false),
        ("Довбуш", 3, "2026-12-07T16:30:00", "2D", "українська", 300m, true),
        ("Інтерстеллар", 2, "2026-12-08T18:00:00", "IMAX", "англійська, субтитри", 230m, false),
        ("Захар Беркут", 1, "2026-12-08T15:00:00", "2D", "українська", 130m, false)
    ];

    public static async Task SeedAsync(CatalogDbContext db, ILogger logger, CancellationToken ct = default)
    {
        // FK-цілісність: спочатку довідники (уже в БД з HasData), потім залежні записи
        var genres = await db.Genres.ToDictionaryAsync(g => g.Slug, ct);
        var halls = await db.Halls.ToDictionaryAsync(h => h.Id, ct);

        var existing = await db.Movies.IgnoreQueryFilters()
            .Select(m => new { m.Title, m.ReleaseYear, m.DurationMinutes }).ToListAsync(ct);
        var busySlots = (await db.Screenings.IgnoreQueryFilters().Select(x => new { x.HallId, x.StartsAt }).ToListAsync(ct))
            .Select(x => (x.HallId, x.StartsAt)).ToHashSet();
        var added = 0;
        var movies = new Dictionary<string, Movie>();

        foreach (var s in Movies)
        {
            if (existing.Any(e => e.ReleaseYear == s.Year && (e.Title == s.Title || e.DurationMinutes == s.Duration))) continue;   // find-or-create
            var movie = new Movie(s.Title, s.Original, s.Year, s.Duration, s.Age);
            movie.SetDetails(s.Description, s.Director, s.Country, s.Language);
            foreach (var (slug, primary, addedAt) in s.Genres)
                movie.AddGenre(genres[slug], primary, DateTime.Parse(addedAt));
            db.Movies.Add(movie);
            movies[s.Title] = movie;
            added++;
        }

        // сеанси додаються лише для фільмів, створених щойно (інакше — уже є з попереднього запуску)
        foreach (var s in Screenings.Where(x => movies.ContainsKey(x.Movie) && !busySlots.Contains((x.Hall, DateTime.Parse(x.StartsAt)))))
        {
            var screening = new Screening(movies[s.Movie], halls[s.Hall], DateTime.Parse(s.StartsAt), s.Format, s.Language, s.Price);
            if (s.Cancelled) screening.Cancel();
            db.Screenings.Add(screening);
        }

        if (added == 0)
        {
            logger.LogInformation("Seed: дані вже є — нічого не додано");
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seed: додано {MoviesCount} фільмів і їхні сеанси", added);
    }
}
