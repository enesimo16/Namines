using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Namines.Vault.Providers;

/// <summary>
/// PostgreSQL yedek/geri yükleme — <c>pg_dump</c> / <c>pg_restore</c>.
///
/// Docker tarafındaki her şey <see cref="ContainerBackupProvider"/>'da; burada
/// yalnızca motora özgü komutlar var.
///
/// <b>Canlı kanıtlandı:</b> yedek → veriyi boz → geri yükle → bağımsız bir
/// <c>psql</c> oturumunda satır kontrolü.
/// </summary>
public sealed class PostgresBackupProvider : ContainerBackupProvider
{
    private readonly string _image;

    public PostgresBackupProvider(IConfiguration configuration, ILogger<PostgresBackupProvider> logger)
        : base(logger)
    {
        // Sunucu sürümü büyüdüğünde bu etiket de büyümeli: eski bir pg_dump
        // yeni bir sunucuyu reddeder (ve bu İYİ — sessizce eksik dump almaktansa).
        _image = configuration["Vault:PostgresImage"] ?? "postgres:17-alpine";
    }

    public override string Engine => "PostgreSQL";

    protected override string Image => _image;

    protected override int DefaultPort => 5432;

    protected override string ToolNames => "pg_dump/pg_restore";

    internal override IList<string> BuildEnvironment(DbConnectionParts conn) =>
        new List<string> { $"PGPASSWORD={conn.Password}" };

    internal override IList<string> BuildDumpCommand(DbConnectionParts conn) => new List<string>
    {
        // -Fc: custom format. Düz SQL değil çünkü pg_restore'un seçmeli geri
        // yükleme ve paralel çalışma yetenekleri buna bağlı (ve sıkıştırılmış).
        "pg_dump", "-Fc", "--no-owner", "--no-acl",
        "-h", conn.ContainerVisibleHost, "-p", conn.Port.ToString(),
        "-U", conn.Username, "-d", conn.Database,
    };

    internal override IList<string> BuildRestoreCommand(DbConnectionParts conn) => new List<string>
    {
        // --clean --if-exists: hedefteki nesneler önce DÜŞÜRÜLÜR. Bu, geri
        // yüklemenin "üzerine yaz" anlamına geldiği yer — çağıran onayı ve
        // ön yedeği almış olmak zorunda.
        // --exit-on-error: ilk hatada dur. Varsayılan "devam et" davranışı
        // yarım geri yüklenmiş bir veritabanını BAŞARILI gösterirdi.
        "pg_restore", "--clean", "--if-exists", "--no-owner", "--no-acl", "--exit-on-error",
        "-h", conn.ContainerVisibleHost, "-p", conn.Port.ToString(),
        "-U", conn.Username, "-d", conn.Database,
        ContainerDumpPath,
    };

    protected override IList<string> BuildVerifyServerEnvironment() => new List<string>
    {
        $"POSTGRES_PASSWORD={VerifyPassword}",
        $"POSTGRES_DB={VerifyDatabase}",
    };

    protected override IList<string> BuildVerifyReadinessCommand() => new List<string>
    {
        "pg_isready", "-h", VerifyHost, "-U", "postgres", "-d", VerifyDatabase,
    };

    protected override IList<string> BuildVerifyRestoreCommand() => new List<string>
    {
        // --exit-on-error: doğrulamanın tamamı bu bayrağa dayanıyor — hataları
        // yutan bir geri yükleme, bozuk bir dump'ı "doğrulandı" işaretlerdi.
        "pg_restore", "--no-owner", "--no-acl", "--exit-on-error",
        "-h", VerifyHost, "-U", "postgres", "-d", VerifyDatabase, ContainerDumpPath,
    };
}
