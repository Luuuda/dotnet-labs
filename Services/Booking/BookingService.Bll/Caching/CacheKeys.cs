namespace BookingService.Bll.Caching;

/// <summary>Ключі кешу сервісу бронювання; префікс «booking:» ізолює їх у спільному Redis.</summary>
public static class CacheKeys
{
    public const string Prefix = "booking:";

    /// <summary>Локальна копія сеансу (довідникові дані з Каталогу) — читається при кожному бронюванні й агрегації.</summary>
    public static string Screening(long id) => $"{Prefix}screening:{id}";

    /// <summary>Картка бронювання. Містить контакти клієнта — лише короткий TTL (CachePolicy.PersonalShort).</summary>
    public static string Booking(long id) => $"{Prefix}booking:{id}";

    /// <summary>Версія списків локальних сеансів (змінюється при додаванні / оновленні / видаленні сеансу).</summary>
    public const string ScreeningsListVersion = Prefix + "screenings:list:version";

    public static string ScreeningsList(string version, DateTime? from, bool onlyActive) =>
        $"{Prefix}screenings:list:v{version}:{from:yyyyMMddHHmm}:{(onlyActive ? "active" : "all")}";
}
