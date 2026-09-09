using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Namines.Core.Models.Auth;
using Namines.Core.Security;
using Namines.Infrastructure.Data;

namespace Namines.API.Services;

/// <summary>
/// Açılışta, canlı bir PostgreSQL'e bağlı hazır bir <b>deneme projesi</b>
/// oluşturur ya da tazeler.
///
/// <b>Çözdüğü sorun:</b> Desk'in, Gateway'in ve Vault'un ÇEKİRDEK akışlarının
/// hiçbiri canlı bir veritabanı bağlanmadan denenemiyor — ve o bağlantıyı elle
/// kurmak her temiz kurulumda tekrarlanan, birkaç adımlı, unutulmaya açık bir
/// iş. Bu yüzden ortam her açıldığında kendiliğinden hazır oluyor.
///
/// <b>YALNIZCA Development.</b> Üretimde çağrılmaz (bkz. Program.cs) ve
/// çağrılsa bile <see cref="SeedAsync"/> hiçbir şey yapmaz: gerçek bir
/// sunucuda kendiliğinden veritabanı yaratmak kabul edilemez.
///
/// <b>Kendi veritabanını kullanır, control DB'ye DOKUNMAZ.</b> Aynı PostgreSQL
/// sunucusunda ayrı bir veritabanı açıyor: deneme verisiyle oynamak (silmek,
/// geri yüklemek, bozmak) Namines'in kendi kayıtlarını hiçbir koşulda
/// etkilememeli — Vault'un geri yükleme akışı denenirken bu fark hayati.
/// </summary>
public static class DevSandboxSeeder
{
    /// <summary>Deneme projesinin sabit kimliği — her açılışta aynı satır tazelensin.</summary>
    private const string ProjectId = "dev-sandbox";

    private const string ProjectName = "Dev Sandbox";

    /// <summary>Açılan veritabanının adı. Control DB'den AYRI.</summary>
    private const string DatabaseName = "namines_dev_sandbox";

    public static async Task SeedAsync(
        AuthDbContext context,
        IConfiguration configuration,
        IConnectionSecretProtector protector,
        ILogger logger,
        CancellationToken ct = default)
    {
        // Dev hesabı yoksa sahipsiz bir proje oluşurdu; DevAccountSeeder ile
        // aynı kapı, aynı gerekçe.
        var devEmail = configuration["Dev:Email"];
        if (string.IsNullOrWhiteSpace(devEmail))
        {
            logger.LogDebug("Dev:Email tanımlı değil, deneme projesi tohumlanmadı.");
            return;
        }

        var controlConnection = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(controlConnection))
        {
            logger.LogDebug("DefaultConnection yok, deneme projesi tohumlanmadı.");
            return;
        }

        var owner = await context.Users.FirstOrDefaultAsync(u => u.Email == devEmail, ct);
        if (owner is null)
        {
            logger.LogDebug("Dev hesabı ({Email}) bulunamadı, deneme projesi tohumlanmadı.", devEmail);
            return;
        }

        var sandboxConnection = BuildSandboxConnectionString(controlConnection);

        await EnsureDatabaseAsync(controlConnection, logger, ct);
        await EnsureTablesAsync(sandboxConnection, logger, ct);
        await EnsureProjectAsync(context, protector, owner, sandboxConnection, logger, ct);
    }

    /// <summary>
    /// Control DB'nin bağlantısından, aynı sunucudaki deneme veritabanına
    /// bakan bir bağlantı üretir.
    ///
    /// Ayrı bir yapılandırma anahtarı İSTENMİYOR: sunucu, kullanıcı ve parola
    /// zaten burada ve ikinci bir kaynak, ikisinin birbirinden kayması demek.
    /// </summary>
    private static string BuildSandboxConnectionString(string controlConnection) =>
        new NpgsqlConnectionStringBuilder(controlConnection) { Database = DatabaseName }.ConnectionString;

    private static async Task EnsureDatabaseAsync(
        string controlConnection, ILogger logger, CancellationToken ct)
    {
        // "postgres" bakım veritabanı: CREATE DATABASE, oluşturulacak
        // veritabanının kendisine bağlıyken çalıştırılamaz.
        var maintenance = new NpgsqlConnectionStringBuilder(controlConnection) { Database = "postgres" }
            .ConnectionString;

        await using var connection = new NpgsqlConnection(maintenance);
        await connection.OpenAsync(ct);

        await using (var exists = new NpgsqlCommand(
            "SELECT 1 FROM pg_database WHERE datname = @name", connection))
        {
            exists.Parameters.AddWithValue("name", DatabaseName);
            if (await exists.ExecuteScalarAsync(ct) is not null) return;
        }

        // CREATE DATABASE parametre kabul etmiyor; ad KOD İÇİNDE sabit
        // (const), kullanıcıdan gelmiyor — dolayısıyla birleştirme güvenli.
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{DatabaseName}\"", connection);
        await create.ExecuteNonQueryAsync(ct);

        logger.LogWarning("Deneme veritabanı oluşturuldu: {Database}", DatabaseName);
    }

    /// <summary>
    /// Tabloları ve örnek satırları kurar.
    ///
    /// <b>Var olan veriyi EZMEZ.</b> Her açılışta sıfırlamak, kullanıcının
    /// denemek için girdiği satırları habersizce silmek olurdu — üstelik tam
    /// da bir geri yükleme denemesinin ortasında.
    /// </summary>
    private static async Task EnsureTablesAsync(
        string sandboxConnection, ILogger logger, CancellationToken ct)
    {
        await using var connection = new NpgsqlConnection(sandboxConnection);
        await connection.OpenAsync(ct);

        const string schema = """
            CREATE TABLE IF NOT EXISTS musteri (
                id          SERIAL PRIMARY KEY,
                ad          TEXT        NOT NULL,
                eposta      TEXT        NOT NULL UNIQUE,
                aktif       BOOLEAN     NOT NULL DEFAULT TRUE,
                olusturuldu TIMESTAMPTZ NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS siparis (
                id         SERIAL PRIMARY KEY,
                musteri_id INTEGER        NOT NULL REFERENCES musteri(id) ON DELETE CASCADE,
                tutar      NUMERIC(10, 2) NOT NULL,
                durum      TEXT           NOT NULL DEFAULT 'yeni',
                tarih      TIMESTAMPTZ    NOT NULL DEFAULT now()
            );
            """;

        await using (var create = new NpgsqlCommand(schema, connection))
            await create.ExecuteNonQueryAsync(ct);

        await using (var count = new NpgsqlCommand("SELECT count(*) FROM musteri", connection))
        {
            if (Convert.ToInt64(await count.ExecuteScalarAsync(ct)) > 0) return;
        }

        // İki tablo ve aralarında bir yabancı anahtar: Desk'in ilişki
        // gösterimi ve Gateway'in birleştirmeleri boş bir şemada denenemezdi.
        const string seed = """
            INSERT INTO musteri (ad, eposta) VALUES
                ('Ayşe Yılmaz',  'ayse@ornek.com'),
                ('Mehmet Demir', 'mehmet@ornek.com'),
                ('Zeynep Kaya',  'zeynep@ornek.com');

            INSERT INTO siparis (musteri_id, tutar, durum) VALUES
                (1, 249.90, 'tamamlandi'),
                (1,  59.00, 'yeni'),
                (2, 1290.50, 'kargoda'),
                (3,  75.25, 'yeni');
            """;

        await using var insert = new NpgsqlCommand(seed, connection);
        await insert.ExecuteNonQueryAsync(ct);

        logger.LogWarning("Deneme veritabanına örnek veri yazıldı: {Database}", DatabaseName);
    }

    private static async Task EnsureProjectAsync(
        AuthDbContext context,
        IConnectionSecretProtector protector,
        ApplicationUser owner,
        string sandboxConnection,
        ILogger logger,
        CancellationToken ct)
    {
        var org = await context.GetOrCreatePersonalOrgAsync(owner.Id, owner.UserName ?? "Personal", ct);

        var project = await context.CloudProjects.FirstOrDefaultAsync(p => p.Id == ProjectId, ct);
        var isNew = project is null;

        if (project is null)
        {
            project = new CloudProject
            {
                Id = ProjectId,
                Name = ProjectName,
                DbType = "PostgreSQL",
                SchemaJson = "{}",
                NodePositionsJson = "{}",
                UserId = owner.Id,
                CreatedAt = DateTime.UtcNow,
            };
            context.CloudProjects.Add(project);
        }

        project.OrganizationId = org.Id;
        // Bağlantı HER AÇILIŞTA yeniden şifreleniyor: şifreleme anahtarı
        // değiştiğinde eski şifreli metin çözülemez hâle gelir ve proje
        // sessizce bozulurdu. Yeniden yazmak bu sınıfı tamamen kaldırıyor.
        project.EncryptedConnectionString = protector.Protect(sandboxConnection);
        project.ConnectionDbType = "PostgreSQL";
        project.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync(ct);
        await EnsureTablePermissionsAsync(context, logger, ct);

        if (isNew)
            logger.LogWarning(
                "Deneme projesi oluşturuldu: '{Name}' ({ProjectId}) → {Database}",
                ProjectName, ProjectId, DatabaseName);
    }

    /// <summary>
    /// Gateway tablo izinlerini açar.
    ///
    /// Gateway <b>varsayılan olarak her şeyi reddeder</b> (07 §4). İzin
    /// açılmazsa deneme projesi Desk'te sıfır tablo gösterirdi — yani hazır
    /// gelmesinin tek amacı boşa çıkardı.
    /// </summary>
    private static async Task EnsureTablePermissionsAsync(
        AuthDbContext context, ILogger logger, CancellationToken ct)
    {
        var existing = await context.GatewayTablePermissions
            .Where(p => p.ProjectId == ProjectId)
            .Select(p => p.TableName)
            .ToListAsync(ct);

        var added = 0;
        foreach (var table in new[] { "musteri", "siparis" })
        {
            if (existing.Contains(table)) continue;

            context.GatewayTablePermissions.Add(new GatewayTablePermission
            {
                ProjectId = ProjectId,
                TableName = table,
                CanRead = true,
                CanWrite = true,
                // 'eposta' maskeli: maskelemenin kendisi ve maskeli okumanın
                // denetim kaydına düşmesi, ancak maskeli bir kolon varsa denenebilir.
                MaskedColumns = table == "musteri" ? "eposta" : null,
            });
            added++;
        }

        if (added > 0)
        {
            await context.SaveChangesAsync(ct);
            logger.LogWarning("Deneme projesine {Count} tablo izni açıldı.", added);
        }
    }
}
