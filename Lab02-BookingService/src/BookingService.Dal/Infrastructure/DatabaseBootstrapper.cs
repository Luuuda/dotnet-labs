using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace BookingService.Dal.Infrastructure;

/// <summary>
/// Готує ПОРОЖНЮ базу (контейнер SQL Server, який піднімає Aspire): якщо таблиць ще немає,
/// послідовно виконує скрипти Лаб. 01 — 01_schema → 02_procedures → 03_seed.
/// Скрипти ідемпотентні, тож повторний запуск нічого не дублює.
/// Для наявної бази з Лаб. 01 нічого не робить.
/// </summary>
public sealed partial class DatabaseBootstrapper(string connectionString, ILogger<DatabaseBootstrapper> logger)
{
    private static readonly string[] Scripts = ["01_schema.sql", "02_procedures.sql", "03_seed.sql"];

    public async Task EnsureSchemaAsync(CancellationToken ct = default)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(ct);

        await using (var check = new SqlCommand("SELECT OBJECT_ID(N'dbo.Bookings', N'U')", connection))
        {
            if (await check.ExecuteScalarAsync(ct) is not DBNull and not null)
            {
                logger.LogInformation("База {Database} вже містить схему — скрипти не застосовуються", connection.Database);
                return;
            }
        }

        logger.LogWarning("База {Database} порожня — застосовую скрипти Лаб. 01", connection.Database);
        foreach (var script in Scripts)
        {
            var text = ReadScript(script);
            foreach (var batch in GoSeparator().Split(text).Where(b => !string.IsNullOrWhiteSpace(b)))
            {
                await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
                await command.ExecuteNonQueryAsync(ct);
            }
            logger.LogInformation("Застосовано {Script}", script);
        }
    }

    private static string ReadScript(string name)
    {
        var assembly = typeof(DatabaseBootstrapper).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Scripts.{name}")
            ?? throw new InvalidOperationException($"Вбудований скрипт {name} не знайдено");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // Пакети T-SQL розділені рядком «GO» (як у sqlcmd / SSMS)
    [GeneratedRegex(@"^\s*GO\s*;?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSeparator();
}
