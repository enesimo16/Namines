using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;

namespace Namines.Mcp;

/// <summary>
/// new-phase/33-MCP-AND-SKILL.md §5 — YENİ İŞ MANTIĞI YOK, hepsi mevcut ve test
/// edilmiş servisleri sarar. Faz 1: pull/analyze/prove (tamamen yerel, salt-okunur).
/// Faz 2: generate_ddl (yerel, deterministik) + open_change_request (sunucuya yazar).
///
/// <b>Bilinçli eksik: `generate_migration` YOK.</b> Mevcut
/// <c>MigrationService.GenerateMigrationAsync</c> migration kodunu Groq'a yazdırıyor.
/// Onu araç olarak sunmak, BAŞKA bir dil modelinin tahminini Claude'a "Namines'in
/// deterministik çıktısı" kılığında geri verirdi — §3'teki tüm konumlandırmanın tam
/// tersi. ALTER cümlelerini Claude zaten yazabilir; bizim katma değerimiz onu
/// kanıtlamak (prove_migration). Deterministik bir migration üreticisi yazıldığında
/// (6 motor + golden-file) araç olarak eklenebilir.
///
/// Konumlandırma (33 §3): bu araçların vaadi "Claude DB'ni görsün" değil —
/// onu Claude zaten `psql` ile yapabiliyor. Vaat: <b>Claude'un yazdığı
/// migration'ı, çalıştırmadan önce kanıtlat.</b> Bir LLM riski tahmin eder;
/// SchemaImpactAnalyzer deterministik hesaplar, BranchTestRunnerService gerçek
/// motorda çalıştırıp kanıtlar.
/// </summary>
[McpServerToolType]
public sealed class NaminesTools
{
    private readonly IDbIntrospectionService _introspection;
    private readonly IBranchTestRunner _testRunner;
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly IPrismaGenerator _prisma;
    private readonly NaminesCloudClient _cloud;

    public NaminesTools(
        IDbIntrospectionService introspection,
        IBranchTestRunner testRunner,
        IDdlGeneratorFactory ddlFactory,
        IPrismaGenerator prisma,
        NaminesCloudClient cloud)
    {
        _introspection = introspection;
        _testRunner = testRunner;
        _ddlFactory = ddlFactory;
        _prisma = prisma;
        _cloud = cloud;
    }

    /// <summary>
    /// Araç girdi/çıktı JSON'u.
    ///
    /// <b>camelCase zorunlu:</b> ürünün geri kalanı (ASP.NET Core varsayılanı) camelCase
    /// üretiyor; MCP araçları PascalCase döndürseydi <c>pull_schema → analyze_impact</c>
    /// zinciri kopardı.
    ///
    /// <b>PropertyNameCaseInsensitive kritik:</b> bu bayrak olmadan camelCase bir şema
    /// JSON'u SESSİZCE boş şemaya çözülüyordu — analiz "Safe, hiçbir şey değişmemiş"
    /// diyordu, oysa tablo eklenmişti. Sessizce yanlış analiz, aracın tüm değerini
    /// yok eder; hem camelCase hem PascalCase girdi kabul edilir.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // Enum DEĞERLERİ PascalCase kalır ("Breaking", "Safe") — barındırılan API de
        // böyle döndürüyor, iki yüzey arasında fark olmasın.
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static DatabaseType ParseEngine(string engine) =>
        Enum.TryParse<DatabaseType>(engine, ignoreCase: true, out var e)
            ? e
            : throw new ArgumentException(
                $"Unknown engine '{engine}'. Use one of: {string.Join(", ", Enum.GetNames<DatabaseType>())}.");

    private static DatabaseSchema ParseSchema(string schemaJson, string paramName)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
            throw new ArgumentException($"{paramName} is empty.");

        DatabaseSchema schema;
        try
        {
            schema = JsonSerializer.Deserialize<DatabaseSchema>(schemaJson, Json)
                     ?? throw new ArgumentException($"{paramName} deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{paramName} is not a valid Namines schema JSON: {ex.Message}");
        }

        // SESSİZ BOŞALMA KORUMASI. Bağlama hatası (alan adı uyuşmazlığı, yanlış şekil)
        // istisna fırlatmaz — boş bir DatabaseSchema üretir. O hâlde analiz "hiçbir şey
        // değişmemiş, Safe" der ve KULLANICI YANLIŞ GÜVEN KAZANIR. Girdi tablo taşıdığını
        // söylüyorsa ama bağlama 0 tablo ürettiyse, sessizce devam etmek yerine patla.
        const string shapeHint =
            "Expected an object like {\"name\":\"app\",\"tables\":[{\"name\":\"users\"," +
            "\"columns\":[{\"name\":\"id\",\"type\":\"INT\",\"isPK\":true}]}],\"relations\":[]}. " +
            "Use the output of namines_pull_schema verbatim.";

        if (schema.Tables.Count == 0 && MentionsNonEmptyTables(schemaJson))
            throw new ArgumentException(
                $"{paramName} contains tables but none could be read — the JSON shape does not match " +
                $"the Namines schema format. {shapeHint}");

        // Adsız tablo = bağlama kısmen tuttu ama alanlar eşleşmedi. İstisna fırlatmadığı
        // için analiz devam eder ve tableName:"" içeren ANLAMSIZ bir rapor üretir; bunu
        // otoriteymiş gibi sunmak, aracın "deterministik kanıt" vaadini çürütür.
        var unnamed = schema.Tables.Count(t => string.IsNullOrWhiteSpace(t.Name));
        if (unnamed > 0)
            throw new ArgumentException(
                $"{paramName} has {unnamed} table(s) without a name — the JSON shape does not match " +
                $"the Namines schema format. {shapeHint}");

        NormalizeIdentities(schema, schemaJson);
        return schema;
    }

    /// <summary>
    /// StableUuid VERİLMEMİŞSE adından türet.
    ///
    /// Model varsayılanı <c>Guid.NewGuid()</c> olduğu için, uuid taşımayan bir JSON
    /// her çözümlemede FARKLI kimlikler üretir. Analizör tabloları uuid ile eşleştirip
    /// eşleşmeyeni "kaldırıldı + eklendi" saydığından, elle yazılmış iki şema —
    /// birebir aynı olsalar bile — "her tablo silinecek, veri kaybı" olarak raporlanır.
    /// Bir agent'ın hedef şemayı JSON olarak yazması tam da beklenen kullanım.
    ///
    /// Açıkça uuid veren kaynaklar (canvas) kendi değerlerini korur, böylece rename
    /// tespiti bozulmaz: rename = aynı uuid, farklı ad.
    /// </summary>
    private static void NormalizeIdentities(DatabaseSchema schema, string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        if (!TryGetPropertyIgnoreCase(doc.RootElement, "tables", out var rawTables) ||
            rawTables.ValueKind != JsonValueKind.Array)
        {
            // Ham JSON okunamıyorsa da adlardan türet: uuid'siz bir şemayı olduğu gibi
            // bırakmak sahte "silindi+eklendi" üretir.
            foreach (var table in schema.Tables) DeriveAll(table);
            return;
        }

        // System.Text.Json dizi sırasını korur, bu yüzden index eşlemesi güvenli.
        var rawList = rawTables.EnumerateArray().ToList();
        for (var i = 0; i < schema.Tables.Count; i++)
        {
            var table = schema.Tables[i];
            var raw = i < rawList.Count ? rawList[i] : default;

            if (!HasText(raw, "stableUuid"))
                table.StableUuid = SchemaIdentity.ForTable(table.Name);

            var rawCols = TryGetPropertyIgnoreCase(raw, "columns", out var c) &&
                          c.ValueKind == JsonValueKind.Array
                ? c.EnumerateArray().ToList()
                : new List<JsonElement>();

            for (var j = 0; j < table.Columns.Count; j++)
            {
                var rawCol = j < rawCols.Count ? rawCols[j] : default;
                if (!HasText(rawCol, "stableUuid"))
                    table.Columns[j].StableUuid = SchemaIdentity.ForColumn(table.Name, table.Columns[j].Name);
            }

            var rawIdx = TryGetPropertyIgnoreCase(raw, "indexes", out var x) &&
                         x.ValueKind == JsonValueKind.Array
                ? x.EnumerateArray().ToList()
                : new List<JsonElement>();

            for (var j = 0; j < table.Indexes.Count; j++)
            {
                var rawOne = j < rawIdx.Count ? rawIdx[j] : default;
                if (!HasText(rawOne, "stableUuid"))
                    table.Indexes[j].StableUuid = SchemaIdentity.ForIndex(table.Name, table.Indexes[j].Name);
            }
        }
    }

    private static void DeriveAll(SchemaTable table)
    {
        table.StableUuid = SchemaIdentity.ForTable(table.Name);
        foreach (var col in table.Columns)
            col.StableUuid = SchemaIdentity.ForColumn(table.Name, col.Name);
        foreach (var idx in table.Indexes)
            idx.StableUuid = SchemaIdentity.ForIndex(table.Name, idx.Name);
    }

    private static bool HasText(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object &&
        TryGetPropertyIgnoreCase(element, property, out var value) &&
        value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString());

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        foreach (var prop in element.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = prop.Value;
            return true;
        }
        return false;
    }

    /// <summary>Ham JSON'da dolu bir "tables" dizisi var mı? (büyük/küçük harf duyarsız)</summary>
    private static bool MentionsNonEmptyTables(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return false;
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (!prop.NameEquals("tables") && !string.Equals(prop.Name, "tables", StringComparison.OrdinalIgnoreCase))
                    continue;
                return prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 0;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // ── 1. pull_schema  (CLI: namines pull) ──────────────────────────────────

    [McpServerTool(Name = "namines_pull_schema")]
    [Description(
        "Read the live structure of a database and return it as a Namines schema (JSON). " +
        "Read-only: it queries INFORMATION_SCHEMA and never modifies anything. " +
        "Feed the result into namines_analyze_impact as `base_schema_json` to see what a " +
        "proposed change would do. Runs locally, so localhost and private-network databases work.")]
    public async Task<string> PullSchemaAsync(
        [Description("ADO.NET connection string for the database to read, e.g. 'Host=localhost;Port=5432;Database=app;Username=postgres;Password=secret'.")]
        string connectionString,
        [Description("Database engine: PostgreSQL, MSSQL, MySQL, MariaDB or Oracle.")]
        string engine,
        CancellationToken cancellationToken = default)
    {
        var schema = await _introspection.IntrospectAsync(connectionString, engine, cancellationToken);
        return JsonSerializer.Serialize(schema, Json);
    }

    // ── 2. analyze_impact  (CLI: namines diff) ───────────────────────────────

    [McpServerTool(Name = "namines_analyze_impact")]
    [Description(
        "Compare two schema versions and return a DETERMINISTIC impact report: affected tables, " +
        "breaking changes, data-loss risks, migration lock risks, missing-index suggestions, " +
        "rollback assessment and an overall risk level (Safe, Risky, Destructive, Breaking). " +
        "This is computed by a rule engine, not by a language model — treat its findings as facts, " +
        "not suggestions. Call this BEFORE proposing any schema migration.")]
    public string AnalyzeImpact(
        [Description("Current schema as Namines JSON (typically the output of namines_pull_schema). Pass '{}' if the database is empty.")]
        string baseSchemaJson,
        [Description("Proposed schema as Namines JSON — the state you want to migrate to.")]
        string targetSchemaJson,
        [Description("Target engine: PostgreSQL, MSSQL, MySQL, MariaDB, Oracle or SQLite. Engine matters — e.g. SQL Server rejects multiple cascade paths.")]
        string engine)
    {
        var baseSchema = ParseSchema(baseSchemaJson, nameof(baseSchemaJson));
        var targetSchema = ParseSchema(targetSchemaJson, nameof(targetSchemaJson));

        var report = SchemaImpactAnalyzer.Analyze(baseSchema, targetSchema, ParseEngine(engine));
        return JsonSerializer.Serialize(report, Json);
    }

    // ── 3. prove_migration  (CLI: namines apply --dry-run) ───────────────────

    [McpServerTool(Name = "namines_prove_migration")]
    [Description(
        "PROVE that a schema actually works: generates DDL for the target engine, starts a real " +
        "throwaway database container, executes the DDL against it, and reports whether the engine " +
        "accepted it — returning the engine's raw error verbatim if not. This is evidence, not a " +
        "prediction: it catches things static analysis cannot, such as SQL Server's Msg 1785 on " +
        "multiple cascade paths. Requires Docker to be running (SQLite runs without it). " +
        "Takes roughly 10-30 seconds because a container has to start.")]
    public async Task<string> ProveMigrationAsync(
        [Description("Schema to verify, as Namines JSON.")]
        string schemaJson,
        [Description("Engine to verify against: PostgreSQL, MSSQL, MySQL or SQLite. Other engines report supported=false rather than pretending.")]
        string engine,
        CancellationToken cancellationToken = default)
    {
        var schema = ParseSchema(schemaJson, nameof(schemaJson));
        var result = await _testRunner.RunAsync(schema, ParseEngine(engine), cancellationToken);
        return JsonSerializer.Serialize(result, Json);
    }

    // ── 4. generate_ddl  (CLI: namines ddl) ──────────────────────────────────

    [McpServerTool(Name = "namines_generate_ddl")]
    [Description(
        "Generate the complete CREATE-side DDL for a schema, correct for the chosen engine. " +
        "This is deterministic, engine-aware code generation covering 6 dialects — it handles " +
        "the details that get silently wrong when written by hand: identity/auto-increment " +
        "syntax, quoting rules, type mapping, and referential actions. On referential actions " +
        "the generator deliberately falls back to the most restrictive behaviour (NO ACTION) " +
        "when an engine does not support what was asked, never to CASCADE, so a default can " +
        "never drift toward data loss. Every dialect is covered by golden-file tests and " +
        "verified against real database containers.")]
    public string GenerateDdl(
        [Description("Schema to generate DDL for, as Namines JSON.")]
        string schemaJson,
        [Description("Target engine: PostgreSQL, MSSQL, MySQL, MariaDB, Oracle or SQLite.")]
        string engine)
    {
        var schema = ParseSchema(schemaJson, nameof(schemaJson));
        return _ddlFactory.GetGenerator(ParseEngine(engine)).Generate(schema);
    }

    // ── 5. open_change_request  (CLI: namines review) ────────────────────────

    [McpServerTool(Name = "namines_open_change_request")]
    [Description(
        "Submit a schema as a Change Request on the Namines server, so a human can review and " +
        "approve it in the Database Change Review UI. This is the ONLY tool that writes anything " +
        "anywhere, and it still does not touch the user's database — it opens a review, it does " +
        "not apply a migration. Use it when a change is Destructive or Breaking and therefore " +
        "needs human sign-off. Requires NAMINES_API_TOKEN to be configured; the other tools work " +
        "offline without it. Ask the user before calling this: it creates something other people " +
        "will see.")]
    public async Task<string> OpenChangeRequestAsync(
        [Description("Namines project id to open the change request against.")]
        string projectId,
        [Description("Proposed schema as Namines JSON — the state you want reviewers to approve.")]
        string schemaJson,
        [Description("Short title for the review, e.g. 'Drop legacy users.email column'.")]
        string? title = null,
        [Description("Context for the reviewer: why this change, and what the impact analysis found.")]
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        // Sunucuya göndermeden önce yerelde doğrula. Bozuk şemayı sunucuya yollayıp
        // 400'ü kullanıcıya aktarmak, aynı hatayı bir ağ turu daha geç göstermek olurdu.
        ParseSchema(schemaJson, nameof(schemaJson));

        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("projectId is empty.");

        var response = await _cloud.OpenChangeRequestAsync(
            projectId, schemaJson, title, message, cancellationToken);
        return JsonSerializer.Serialize(response, Json);
    }

    // ── 6. generate_prisma  (CLI: namines prisma) ────────────────────────────

    [McpServerTool(Name = "namines_generate_prisma")]
    [Description(
        "Generate a Prisma schema (schema.prisma) from a Namines schema. Deterministic and " +
        "engine-aware: model/field names are mapped with @@map/@map so the database is never " +
        "renamed, VARCHAR lengths survive as native types, and referential actions are written " +
        "explicitly because Prisma's defaults differ from SQL's. " +
        "IMPORTANT: read the `warnings` array and relay it to the user. Prisma cannot express " +
        "CHECK constraints or partial indexes; anything listed there is ABSENT from the output, " +
        "and running `prisma db push` from it would DROP those from the database. " +
        "Oracle is rejected outright — Prisma has no Oracle provider.")]
    public string GeneratePrisma(
        [Description("Schema to convert, as Namines JSON.")]
        string schemaJson,
        [Description("Target engine: PostgreSQL, MySQL, MariaDB, MSSQL or SQLite. Oracle is not supported by Prisma.")]
        string engine)
    {
        var result = GeneratePrismaFiles(schemaJson, engine);

        return JsonSerializer.Serialize(new
        {
            schema = result.Files["schema.prisma"],
            env = result.Files[".env.example"],
            warnings = result.Warnings,
        }, Json);
    }

    /// <summary>
    /// Yapılandırılmış hâl. MCP aracı bunun JSON'a serileştirilmiş hâlidir; CLI ise
    /// uyarıları stderr'e ayırabilmek için doğrudan bunu kullanır. Tek gövde, iki yüzey.
    /// </summary>
    public PrismaGenerationResult GeneratePrismaFiles(string schemaJson, string engine)
    {
        var schema = ParseSchema(schemaJson, nameof(schemaJson));
        return _prisma.Generate(schema, ParseEngine(engine));
    }
}
