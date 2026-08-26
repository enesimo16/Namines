using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace Namines.Infrastructure.Services;

/// <summary>
/// second-phase/13-DAGITIM-HEDEFLERI.md — "hazır .db dosyası". DDL'i gerçek bir
/// geçici SQLite dosyasına karşı çalıştırıp bayt dizisi olarak döner.
///
/// <b>Aynı desenin bir kopyası, farklı amaç:</b> <c>BranchTestRunnerService.RunSqliteAsync</c>
/// dosyayı bir TEST için kullanıp siliyor; burada dosyanın KENDİSİ ürün —
/// kullanıcı bu .db'yi mobil uygulamasına gömecek.
/// </summary>
public static class SqliteFileBuilder
{
    public static async Task<byte[]> BuildAsync(string ddl, CancellationToken ct = default)
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"namines_export_{Guid.NewGuid():N}.db");
        // Pooling=False: Microsoft.Data.Sqlite bağlantı havuzu, Dispose'dan SONRA bile
        // dosya tutamacını açık bırakabiliyor (gerçek dosyayı hemen okumamız gerektiği
        // için bu, BranchTestRunnerService'teki "silmeyi dene, olmazsa önemli değil"
        // toleransıyla aynı şekilde geçiştirilemez — burada dosyayı okumak ZORUNDAYIZ).
        var connectionString = $"Data Source={tempFile};Pooling=False";
        try
        {
            await using (var conn = new SqliteConnection(connectionString))
            {
                await conn.OpenAsync(ct);

                foreach (var statement in ddl.Split(';', StringSplitOptions.RemoveEmptyEntries)
                             .Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith("--")))
                {
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = statement;
                    await cmd.ExecuteNonQueryAsync(ct);
                }
            }

            return await File.ReadAllBytesAsync(tempFile, ct);
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* geçici dosya, temizlik başarısız olsa da kritik değil */ }
        }
    }
}
