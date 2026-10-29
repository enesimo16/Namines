using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using Namines.Core.Security;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Namines.Mcp;

/// <summary>
/// MCP araçlarının bağlandığı veritabanı hedefini GÖRÜNÜR kılar ve isteğe bağlı
/// bir allowlist uygular.
///
/// <b>Bulunan açık (docs/last-review M-16):</b> MCP süreci SSRF politikasını
/// yapısal olarak kapatıyor (<c>Security:AllowPrivateDbHosts</c> varsayılan
/// <c>true</c>, <c>McpHostEnvironment</c> her zaman "Development"). Bu, ürünün
/// hedeflenen kullanımı için <b>doğru</b> — süreç kullanıcının kendi makinesinde
/// ve localhost tam olarak amaçlanan hedef (33 §2). Kapatılmaması gereken şey
/// buydu.
///
/// Kalan gerçek risk şu: bağlantı dizesi <b>MODELİN çıktısından</b> geliyor.
/// Prompt injection'a maruz kalmış bir model, kullanıcının kimlik bilgilerini
/// içeren bir dizeye <c>Host=saldirgan.example.com</c> yazıp onları dışarı
/// taşıyabilir — ve guard tamamen kapalı olduğu için hiçbir şey buna engel değil,
/// hiçbir iz de kalmıyor.
///
/// <b>Varsayılan davranış DEĞİŞMİYOR</b> (yoksa barındırılan bir Postgres'e
/// bakan kullanıcıların akışı kırılırdı); değişen şu:
/// <list type="number">
///   <item>Her bağlantı hedefi (host + veritabanı, <b>asla kimlik bilgisi değil</b>)
///   stderr'a yazılıyor, yani modelin nereyi aradığı kullanıcıya görünür.</item>
///   <item><c>Security:McpAllowedDbHosts</c> tanımlanmışsa hedef o listede olmak
///   zorunda — kimlik bilgilerini korumak isteyen kullanıcı için sert bir kapı.</item>
/// </list>
/// </summary>
public sealed class McpConnectionGuard
{
    private readonly ILogger<McpConnectionGuard> _logger;
    private readonly string[] _allowedHosts;

    /// <summary>
    /// GENEL (internet) veritabanı hostlarına izin verilsin mi. Varsayılan <c>false</c>.
    ///
    /// <b>Neden varsayılan kapalı (docs/last-review M-16):</b> MCP süreci kullanıcının
    /// kendi makinesinde çalışıyor ve bağlantı dizesini MODELİN çıktısından alıyor.
    /// Prompt injection'a maruz kalmış bir model, kullanıcının kimlik bilgilerini
    /// içeren bir dizeye <c>Host=saldirgan.example.com</c> yazıp onları dışarı
    /// taşıyabilir — bir veritabanı sürücüsüyle yapılan, kimsenin beklemediği bir
    /// dışa sızış kanalı. Yerel bir geliştirme aracının varsayılan olarak internete
    /// çıkması için bir sebep yok.
    ///
    /// <b>Meşru kullanım tek satırla geri geliyor:</b> barındırılan bir Postgres'e
    /// (Neon, RDS, Supabase) bakan kullanıcı <c>Security:McpAllowPublicDbHosts=true</c>
    /// veriyor. Bu bir KARAR noktası olduğu için sessizce varsayılmamış olması önemli —
    /// ama kullanıcıyı çıkmaz sokağa da sokmuyor: hata mesajı ne yapacağını söylüyor.
    ///
    /// <c>Security:McpAllowedDbHosts</c> (allowlist) tanımlıysa bu bayrağa GEREK YOK:
    /// allowlist zaten daha dar bir kapı ve hedefi tek tek sınırlıyor.
    /// </summary>
    private readonly bool _allowPublicHosts;

    public McpConnectionGuard(IConfiguration configuration, ILogger<McpConnectionGuard> logger)
    {
        _logger = logger;

        // Virgülle ayrılmış liste. Boşsa kapı yok, yalnızca günlük tutulur.
        _allowedHosts = (configuration["Security:McpAllowedDbHosts"] ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(h => h.ToLowerInvariant())
            .ToArray();

        _allowPublicHosts = configuration.GetValue("Security:McpAllowPublicDbHosts", defaultValue: false);
    }

    /// <summary>
    /// Hedefi kaydeder ve allowlist tanımlıysa uygular. İzin verilmiyorsa
    /// <see cref="InvalidOperationException"/> fırlatır.
    ///
    /// <b>Bulunan açık (A-7 / C-6):</b> eski sürüm yalnızca İLK host anahtarının İLK
    /// değerini okuyordu. <c>Host=localhost;Host=saldirgan.com</c> (sürücü SON anahtarı
    /// kullanır), <c>Host=localhost,saldirgan.com</c> (Npgsql/MySqlConnector çoklu-host)
    /// ya da <c>Server=localhost;Addr=saldirgan.com</c> (eşanlamlı anahtar) guard'ı
    /// geçip sürücünün ikinci hosta bağlanmasına yol açıyordu. Şimdi sürücünün KENDİ
    /// builder'ının çözdüğü host ile bağlantı dizesindeki HER host anahtarının HER
    /// değeri aday toplanıyor; biri bile reddedilirse bağlantının tamamı reddediliyor
    /// ve birbirinden farklı birden çok host anahtarı belirsiz sayılıp reddediliyor.
    /// </summary>
    public void EnsureAllowed(string connectionString, string engine)
    {
        var target = ResolveTarget(connectionString, engine);

        // Hedef HER ZAMAN kaydediliyor, allowlist olsun olmasın: asıl kazanç,
        // modelin nereyi aradığının görünür olması. Kimlik bilgisi YAZILMIYOR —
        // ham bağlantı dizesini loglamak, açığı kapatmak yerine ikinci bir
        // sızıntı yüzeyi açmak olurdu.
        _logger.LogWarning(
            "MCP veritabani baglantisi: host={Host} engine={Engine}",
            target.IsLocalFile ? "(yerel dosya)" : target.Hosts.Count == 0 ? "(bilinmiyor)" : string.Join(",", target.Hosts),
            engine);

        // SQLite yerel bir dosya: ağ hedefi yok, dolayısıyla allowlist/özel-ağ sorusu
        // anlamsız. (UNC yolu ResolveTarget'ta host olarak döner, burada değil.)
        if (target.IsLocalFile) return;

        var hosts = target.Hosts;

        // Allowlist tanımlıysa TEK kapı o: daha dar ve hedefi tek tek sınırlıyor,
        // yani "genel host" sorusunu ayrıca sormaya gerek yok.
        if (_allowedHosts.Length > 0)
        {
            // Host ayıklanamadıysa REDDEDİLİYOR: "okuyamadım"ı "izinli" saymak,
            // kapıyı bozuk bir dizeyle atlamanın yolu olurdu.
            if (hosts.Count == 0)
                throw new InvalidOperationException(
                    "The connection string does not name a host that could be checked against " +
                    "Security:McpAllowedDbHosts.");

            foreach (var host in hosts)
            {
                if (!_allowedHosts.Contains(host.ToLowerInvariant()))
                    throw new InvalidOperationException(
                        $"Host '{host}' is not in Security:McpAllowedDbHosts. " +
                        "Add it there if this database is really yours.");
            }

            return;
        }

        if (_allowPublicHosts) return;

        // Buradan sonrası: allowlist yok ve genel hostlara izin verilmemiş.
        // Yalnızca yerel/özel ağ hedefleri geçiyor — bkz. _allowPublicHosts notu.
        if (hosts.Count == 0)
            throw new InvalidOperationException(
                "The connection string does not name a host that could be checked. " +
                "Set Security:McpAllowPublicDbHosts=true if you meant to reach a hosted database.");

        foreach (var host in hosts)
        {
            // Sunucuyla aynı makinedeki hedefler (unix soketi, SQL Server ".", "(localdb)"):
            // DNS'e çözülemezler ama ağdan çıkmazlar.
            if (IsLocalSentinel(host, target.Engine)) continue;

            // <c>IsHostPrivate</c> bilerek <c>IsHostSafe</c>'in tersi DEĞİL: çözülemeyen
            // bir ad burada özel SAYILMAZ, yani geçemez. Çözülemeyen bir adı "yerel
            // olabilir" diye geçirmek, kapıyı var olmayan bir alan adıyla atlamak olurdu.
            if (SsrfGuard.IsHostPrivate(host)) continue;

            throw new InvalidOperationException(
                $"Host '{host}' is not a local or private-network address, and reaching public " +
                "database hosts from the MCP server is turned off. The connection string comes " +
                "from model output, so a public target could carry your credentials off the " +
                "machine. If this database really is yours, set " +
                "Security:McpAllowPublicDbHosts=true (or list it in Security:McpAllowedDbHosts).");
        }
    }

    private sealed record Target(string Engine, IReadOnlyList<string> Hosts, bool IsLocalFile);

    // Bağlantı dizesinde host anlamına gelen anahtarlar (boşluk/altçizgi/tire atılmış,
    // küçük harf). Sürücüler bunların hepsini aynı alanın eşanlamlısı sayıyor.
    private static readonly HashSet<string> HostKeys = new(StringComparer.Ordinal)
    {
        "host", "server", "hostname", "datasource", "address", "addr", "networkaddress",
    };

    private static string NormalizeEngine(string engine) => (engine ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "POSTGRESQL" or "POSTGRES" => "POSTGRESQL",
        "MSSQL" or "SQLSERVER" => "MSSQL",
        "MYSQL" => "MYSQL",
        "MARIADB" => "MARIADB",
        "ORACLE" => "ORACLE",
        "SQLITE" => "SQLITE",
        var other => throw new InvalidOperationException(
            $"Unknown engine '{other}'; cannot check the connection target."),
    };

    private static Target ResolveTarget(string connectionString, string engine)
    {
        var normalized = NormalizeEngine(engine);
        if (string.IsNullOrWhiteSpace(connectionString))
            return new Target(normalized, Array.Empty<string>(), false);

        // Sürücünün builder'ı HANGİ host'a bağlanacağının tek gerçeği: bozuk dizede
        // fırlatır ve aynı builder bağlantıyı kurarken de fırlatacağı için reddetmek doğru.
        string builderValue;
        try
        {
            builderValue = normalized switch
            {
                "MSSQL" => new SqlConnectionStringBuilder(connectionString).DataSource,
                "POSTGRESQL" => new NpgsqlConnectionStringBuilder(connectionString).Host ?? string.Empty,
                "MYSQL" or "MARIADB" => new MySqlConnectionStringBuilder(connectionString).Server,
                "ORACLE" => new OracleConnectionStringBuilder(connectionString).DataSource,
                _ => new SqliteConnectionStringBuilder(connectionString).DataSource,
            };
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"The connection string could not be parsed, so its host cannot be checked: {ex.Message}");
        }

        if (normalized == "SQLITE")
        {
            // UNC yolu (\\sunucu\paylasim\db.sqlite) yerel dosya DEĞİL, bir ağ hedefi.
            var path = (builderValue ?? string.Empty).Trim();
            if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            {
                var unc = path.TrimStart('\\', '/').Split('\\', '/')[0];
                return new Target(normalized, unc.Length == 0 ? Array.Empty<string>() : new[] { unc }, false);
            }
            return new Target(normalized, Array.Empty<string>(), true);
        }

        // Dizedeki HER host anahtarı da aday: builder yinelenen anahtarda yalnızca birini
        // söylüyor, ama farklı değerli birden çok host anahtarı zaten belirsiz.
        var keyed = new List<string>();
        foreach (var (key, value) in Tokenize(connectionString))
        {
            var k = key.Trim().ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            if (HostKeys.Contains(k) && !string.IsNullOrWhiteSpace(value)) keyed.Add(value.Trim());
        }

        if (keyed.Select(v => v.ToLowerInvariant()).Distinct().Count() > 1)
            throw new InvalidOperationException(
                "The connection string names more than one host (duplicate or conflicting " +
                "Host/Server/Data Source/Address keys). Specify the database host exactly once.");

        var hosts = new List<string>();
        foreach (var value in keyed.Append(builderValue ?? string.Empty))
        {
            foreach (var host in SplitHosts(value, normalized))
            {
                if (host.Length > 0 && !hosts.Contains(host, StringComparer.OrdinalIgnoreCase))
                    hosts.Add(host);
            }
        }

        return new Target(normalized, hosts, false);
    }

    /// <summary>
    /// Bağlantı dizesini (anahtar, değer) çiftlerine ayırır. ADO.NET kuralı: tırnak
    /// yalnızca değerin İLK karakteriyse tırnaktır ("Password=pa'ss;Host=x" içindeki
    /// tek tırnak düz karakterdir) — tırnağı her yerde açan bir tarayıcı, sonraki
    /// gerçek <c>Host=</c> anahtarını bir dizenin içinde sanıp gizlerdi.
    /// </summary>
    private static List<(string Key, string Value)> Tokenize(string cs)
    {
        var pairs = new List<(string, string)>();
        var i = 0;
        while (i < cs.Length)
        {
            var eq = cs.IndexOf('=', i);
            if (eq < 0) break;

            var semi = cs.IndexOf(';', i);
            if (semi >= 0 && semi < eq) { i = semi + 1; continue; }

            var key = cs[i..eq];
            i = eq + 1;
            while (i < cs.Length && char.IsWhiteSpace(cs[i])) i++;

            string value;
            if (i < cs.Length && (cs[i] == '\'' || cs[i] == '"'))
            {
                var quote = cs[i++];
                var sb = new StringBuilder();
                while (i < cs.Length)
                {
                    if (cs[i] == quote)
                    {
                        if (i + 1 < cs.Length && cs[i + 1] == quote) { sb.Append(quote); i += 2; continue; }
                        i++;
                        break;
                    }
                    sb.Append(cs[i++]);
                }
                value = sb.ToString();
                var next = cs.IndexOf(';', i);
                i = next < 0 ? cs.Length : next + 1;
            }
            else
            {
                var next = cs.IndexOf(';', i);
                value = (next < 0 ? cs[i..] : cs[i..next]).Trim();
                i = next < 0 ? cs.Length : next + 1;
            }

            pairs.Add((key, value));
        }
        return pairs;
    }

    /// <summary>
    /// Bir host alanını motorun söz dizimine göre HER host adayına ayırır.
    /// Virgülün anlamı motora göre değişiyor: Npgsql/MySqlConnector/Oracle EZConnect'te
    /// host LİSTESİ ayıracı, SQL Server'da host'un port'undan ayıracı.
    /// </summary>
    private static IEnumerable<string> SplitHosts(string value, string engine)
    {
        value = value.Trim();
        if (value.Length == 0) yield break;

        if (engine == "MSSQL")
        {
            // "tcp:host,1433", "host\INSTANCE", "(localdb)\MSSQLLocalDB", ".\SQLEXPRESS",
            // "np:\\host\pipe\sql\query": port ve örnek adı host değildir.
            value = Regex.Replace(value, @"^(?:tcp|np|lpc|via):", string.Empty, RegexOptions.IgnoreCase).Trim();
            if (value.StartsWith(@"\\", StringComparison.Ordinal))
                yield return value[2..].Split('\\')[0].Trim();
            else
                yield return value.Split(',')[0].Split('\\')[0].Trim();
            yield break;
        }

        if (engine == "ORACLE" && value.Contains('('))
        {
            // TNS tanımlayıcısı: (ADDRESS=(PROTOCOL=TCP)(HOST=...)(PORT=...)) — HER HOST=.
            var any = false;
            foreach (Match m in Regex.Matches(value, @"\(\s*HOST\s*=\s*([^)\s]+)\s*\)", RegexOptions.IgnoreCase))
            {
                any = true;
                yield return m.Groups[1].Value.Trim();
            }
            // Hiç HOST= bulunamadıysa ham değer aday kalır ve doğal olarak reddedilir.
            if (!any) yield return value;
            yield break;
        }

        if (engine == "ORACLE") value = value.TrimStart('/');
        foreach (var segment in value.Split(','))
            yield return HostOnly(segment);
    }

    private static string HostOnly(string segment)
    {
        segment = segment.Trim();
        if (segment.Length == 0) return segment;

        // Unix soketi yolu (Host=/var/run/postgresql): ağ hedefi değil.
        if (segment[0] == '/') return segment;

        if (segment[0] == '[')
        {
            var close = segment.IndexOf(']');
            return close > 1 ? segment[1..close] : segment;
        }

        var slash = segment.IndexOf('/');
        if (slash >= 0) segment = segment[..slash];

        // "host:5432" -> host; birden çok ':' içeren çıplak IPv6'ya dokunma.
        var firstColon = segment.IndexOf(':');
        if (firstColon > 0 && firstColon == segment.LastIndexOf(':')) segment = segment[..firstColon];

        return segment.Trim();
    }

    private static bool IsLocalSentinel(string host, string engine)
    {
        if (host.StartsWith('/')) return true;
        return engine == "MSSQL" &&
               (host == "." || host.Equals("(local)", StringComparison.OrdinalIgnoreCase) ||
                host.Equals("(localdb)", StringComparison.OrdinalIgnoreCase));
    }
}
