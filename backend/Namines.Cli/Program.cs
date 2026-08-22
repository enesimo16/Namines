using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Namines.Core.Interfaces;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.PrismaGenerator;
using Namines.Infrastructure.Security;
using Namines.Infrastructure.Services;
using Namines.Cli;
using Namines.Mcp;

// ─────────────────────────────────────────────────────────────────────────────
// namines — CLI (new-phase/11-MIGRATIONS-BRANCHING.md §9)
//
// MCP araçlarıyla AYNI gövdeyi çalıştırır (bkz. csproj yorumu). Buradaki tek iş
// argüman ayrıştırma, dosya G/Ç ve çıkış kodu.
//
// ÇIKIŞ KODLARI — CI'da kapı olarak kullanılabilsin diye ayrıştırıldı:
//   0  başarılı / risk kabul edilebilir
//   1  kullanım veya çalışma zamanı hatası
//   2  `diff`: Destructive veya Breaking risk bulundu
//   3  `prove`: motor DDL'i REDDETTİ
// 2 ve 3'ü 1'den ayırmak önemli: "aracın kendisi patladı" ile "araç çalıştı ve
// değişiklik tehlikeli" CI'da aynı şey değildir.
// ─────────────────────────────────────────────────────────────────────────────

const string Usage = """
namines — schema pull, impact analysis, DDL generation, migration proof

USAGE
  namines pull   --conn <connection-string> --engine <engine> [--out <file>]
  namines diff   --base <file|-> --target <file|-> --engine <engine> [--out <file>]
  namines ddl    --schema <file|-> --engine <engine> [--out <file>]
  namines prove  --schema <file|-> --engine <engine>
  namines prisma --schema <file|-> --engine <engine> [--out <file>]
  namines validate --schema <file|-> --engine <engine>

ENGINES
  PostgreSQL, MSSQL, MySQL, MariaDB, Oracle, SQLite
  (pull: no SQLite; prove: PostgreSQL, MSSQL, MySQL, SQLite)

NOTES
  Use '-' as a file argument to read from stdin.
  'diff' with no --base compares against an empty schema.
  'prove' starts a real throwaway database container and needs Docker
  (SQLite runs without it).

  'prisma' prints warnings to stderr: anything Prisma cannot express is ABSENT
  from the output (CHECK constraints, partial indexes). Oracle is not supported.

  'validate' checks the schema against the NSL rules (04 §6) and prints findings.
  It accepts both Namines JSON and .nsl text.

EXIT CODES
  0 ok   1 error   2 destructive/breaking risk   3 engine rejected the DDL
  4 validation found errors
""";

try
{
    return await Run(args);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static async Task<int> Run(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        Console.WriteLine(Usage);
        return args.Length == 0 ? 1 : 0;
    }

    var command = args[0];
    var opts = ParseOptions(args.Skip(1));
    var tools = BuildTools();

    switch (command)
    {
        case "pull":
        {
            var json = await tools.PullSchemaAsync(
                Required(opts, "conn"), Required(opts, "engine"));
            Emit(json, opts);
            return 0;
        }

        case "diff":
        {
            // --base verilmezse boş şema: "boş bir veritabanına karşı" karşılaştırma
            // meşru bir kullanım, hata değil.
            var baseJson = opts.TryGetValue("base", out var b) ? ReadInput(b) : "{}";
            var json = tools.AnalyzeImpact(
                baseJson, ReadInput(Required(opts, "target")), Required(opts, "engine"));
            Emit(json, opts);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var risk = doc.RootElement.GetProperty("overallRisk").GetString();
            return risk is "Destructive" or "Breaking" ? 2 : 0;
        }

        case "ddl":
        {
            Emit(tools.GenerateDdl(
                ReadInput(Required(opts, "schema")), Required(opts, "engine")), opts);
            return 0;
        }

        case "prisma":
        {
            var result = tools.GeneratePrismaFiles(
                ReadInput(Required(opts, "schema")), Required(opts, "engine"));

            // Uyarılar stderr'e: stdout'a karışsalar `namines prisma > schema.prisma`
            // dosyayı bozardı. Sessizce yutmak ise kısıt kaybını gizlerdi.
            foreach (var warning in result.Warnings)
                Console.Error.WriteLine($"warning: {warning}");

            Emit(result.Files["schema.prisma"], opts);
            return 0;
        }

        case "validate":
        {
            var text = ReadInput(Required(opts, "schema"));
            var engine = Required(opts, "engine");

            // Hem JSON hem .nsl kabul ediliyor: kullanıcı şemasını hangi biçimde
            // tutuyorsa onu vermeli, biçim dönüştürmek için ayrı bir adım
            // gerekmemeli. Ayırt etmek için ilk anlamlı karakter yeterli.
            var schema = text.TrimStart().StartsWith("{")
                ? System.Text.Json.JsonSerializer.Deserialize<Namines.Core.Models.DatabaseSchema>(
                      text, new System.Text.Json.JsonSerializerOptions
                      {
                          PropertyNameCaseInsensitive = true,
                          Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                      }) ?? new Namines.Core.Models.DatabaseSchema()
                : Namines.Core.Nsl.NslParser.Parse(text);

            var findings = Namines.Core.Nsl.NslValidator.Validate(
                schema, Enum.Parse<Namines.Core.Enums.DatabaseType>(engine, ignoreCase: true));

            foreach (var finding in findings)
            {
                var where = finding.Table is null ? string.Empty
                    : finding.Column is null ? $"{finding.Table}: "
                    : $"{finding.Table}.{finding.Column}: ";
                Console.WriteLine($"{finding.Code} [{finding.Severity}] {where}{finding.Message}");
            }

            var errors = findings.Count(f => f.Severity == "error");
            Console.Error.WriteLine(
                $"{errors} error(s), {findings.Count(f => f.Severity == "warning")} warning(s), " +
                $"{findings.Count(f => f.Severity == "info")} info.");

            // Hataya ayrı bir çıkış kodu: CI'da "doğrulama başarısız" ile
            // "aracın kendisi patladı" aynı şey değil.
            return errors > 0 ? 4 : 0;
        }

        case "prove":
        {
            var json = await tools.ProveMigrationAsync(
                ReadInput(Required(opts, "schema")), Required(opts, "engine"));
            Emit(json, opts);

            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.GetProperty("supported").GetBoolean())
            {
                // "Doğrulanamadı" ile "doğrulandı ve geçti" karıştırılmamalı, ama bu
                // bir başarısızlık da değil — 0 dönerken uyarıyı stderr'e yaz.
                Console.Error.WriteLine("warning: this engine has no runner; nothing was proven.");
                return 0;
            }
            return root.GetProperty("success").GetBoolean() ? 0 : 3;
        }

        default:
            Console.Error.WriteLine($"error: unknown command '{command}'.\n");
            Console.Error.WriteLine(Usage);
            return 1;
    }
}

static NaminesTools BuildTools()
{
    // MCP sunucusuyla aynı gerekçe: süreç kullanıcının kendi makinesinde, kendi
    // DB'sine bakıyor — localhost hedeflenen kullanımdır (33 §2).
    var config = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Security:AllowPrivateDbHosts"] = "true" })
        .AddEnvironmentVariables()
        .Build();

    var hostPolicy = new DbHostAccessPolicy(
        new CliHostEnvironment(), config, NullLogger<DbHostAccessPolicy>.Instance);

    var ddlFactory = new DdlGeneratorFactory();

    return new NaminesTools(
        new DbIntrospectionService(NullLogger<DbIntrospectionService>.Instance, hostPolicy),
        new BranchTestRunnerService(ddlFactory),
        ddlFactory,
        new PrismaGeneratorService(),
        new NaminesCloudClient(new HttpClient()));
}

/// <summary>--ad değer çiftleri. Bilinmeyen bayrağı sessizce yutmak, yazım hatasını
/// "seçenek verilmemiş" gibi gösterip yanlış varsayılanla çalıştırırdı.</summary>
static Dictionary<string, string> ParseOptions(IEnumerable<string> args)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var list = args.ToList();

    for (var i = 0; i < list.Count; i++)
    {
        var token = list[i];
        if (!token.StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"unexpected argument '{token}'.");

        var name = token[2..];
        if (i + 1 >= list.Count || list[i + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"option '--{name}' needs a value.");

        result[name] = list[++i];
    }
    return result;
}

static string Required(Dictionary<string, string> opts, string name) =>
    opts.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v)
        ? v
        : throw new ArgumentException($"missing required option '--{name}'.");

static string ReadInput(string path) =>
    path == "-" ? Console.In.ReadToEnd() : File.ReadAllText(path);

static void Emit(string content, Dictionary<string, string> opts)
{
    if (opts.TryGetValue("out", out var path))
    {
        File.WriteAllText(path, content);
        Console.Error.WriteLine($"written: {path}");
    }
    else
    {
        Console.WriteLine(content);
    }
}

namespace Namines.Cli
{
    /// <summary><see cref="DbHostAccessPolicy"/> gevşetmeyi yalnızca Development'ta
    /// uygular. CLI, MCP sunucusu gibi, tanımı gereği geliştiricinin makinesinde
    /// çalışan bir araçtır — politikanın barındırılan API'deki çift kapısını
    /// zayıflatmamak için bağlamı burada açıkça beyan ediyoruz.</summary>
    internal sealed class CliHostEnvironment : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Microsoft.Extensions.Hosting.Environments.Development;
        public string ApplicationName { get; set; } = "Namines.Cli";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
            = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
