using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using ModelContextProtocol.Server;
using MySqlConnector;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Flow.Abstractions;
using Namines.Infrastructure.Catalogs;
using Namines.Infrastructure.Generators.DdlGenerator;
using Namines.Infrastructure.Generators.Eject;
using Npgsql;
using Oracle.ManagedDataAccess.Client;

namespace Namines.Mcp;

/// <summary>
/// Namines Universal Database Lifecycle MCP Tools.
///
/// Deterministic schema analysis, live database inspection, code generation across 19 targets,
/// ephemeral Docker migration proving, Hub architectural blueprints, and Namines Flow automation.
/// </summary>
[McpServerToolType]
public sealed class NaminesTools
{
    private readonly IDbIntrospectionService _introspection;
    private readonly IBranchTestRunner _testRunner;
    private readonly IDdlGeneratorFactory _ddlFactory;
    private readonly IPrismaGenerator _prisma;
    private readonly IEjectGeneratorRegistry _ejectRegistry;
    private readonly IGatewayService _gateway;
    private readonly IDatabaseExecutor _executor;
    private readonly IFlowRuleMatcher _flowMatcher;
    private readonly NaminesCloudClient _cloud;
    private readonly McpConnectionGuard _connectionGuard;

    public NaminesTools(
        IDbIntrospectionService introspection,
        IBranchTestRunner testRunner,
        IDdlGeneratorFactory ddlFactory,
        IPrismaGenerator prisma,
        NaminesCloudClient cloud,
        McpConnectionGuard connectionGuard)
        : this(
            introspection,
            testRunner,
            ddlFactory,
            prisma,
            new EjectGeneratorRegistry(ddlFactory),
            null!,
            null!,
            null!,
            cloud,
            connectionGuard)
    {
    }

    public NaminesTools(
        IDbIntrospectionService introspection,
        IBranchTestRunner testRunner,
        IDdlGeneratorFactory ddlFactory,
        IPrismaGenerator prisma,
        IEjectGeneratorRegistry ejectRegistry,
        IGatewayService gateway,
        IDatabaseExecutor executor,
        IFlowRuleMatcher flowMatcher,
        NaminesCloudClient cloud,
        McpConnectionGuard connectionGuard)
    {
        _introspection = introspection;
        _testRunner = testRunner;
        _ddlFactory = ddlFactory;
        _prisma = prisma;
        _ejectRegistry = ejectRegistry;
        _gateway = gateway;
        _executor = executor;
        _flowMatcher = flowMatcher;
        _cloud = cloud;
        _connectionGuard = connectionGuard;
    }

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
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

        const string shapeHint =
            "Expected an object like {\"name\":\"app\",\"tables\":[{\"name\":\"users\"," +
            "\"columns\":[{\"name\":\"id\",\"type\":\"INT\",\"isPK\":true}]}],\"relations\":[]}. " +
            "Use the output of namines_pull_schema verbatim.";

        if (schema.Tables.Count == 0 && MentionsNonEmptyTables(schemaJson))
        {
            throw new ArgumentException(
                $"{paramName} contains tables but none could be read — the JSON shape does not match " +
                $"the Namines schema format. {shapeHint}");
        }

        var unnamed = schema.Tables.Count(t => string.IsNullOrWhiteSpace(t.Name));
        if (unnamed > 0)
        {
            throw new ArgumentException(
                $"{paramName} has {unnamed} table(s) without a name — the JSON shape does not match " +
                $"the Namines schema format. {shapeHint}");
        }

        NormalizeIdentities(schema, schemaJson);
        return schema;
    }

    private static void NormalizeIdentities(DatabaseSchema schema, string rawJson)
    {
        using var doc = JsonDocument.Parse(rawJson);
        if (!TryGetPropertyIgnoreCase(doc.RootElement, "tables", out var rawTables) ||
            rawTables.ValueKind != JsonValueKind.Array)
        {
            foreach (var table in schema.Tables) DeriveAll(table);
            return;
        }

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
                var col = table.Columns[j];
                var rawCol = j < rawCols.Count ? rawCols[j] : default;
                if (!HasText(rawCol, "stableUuid"))
                    col.StableUuid = SchemaIdentity.ForColumn(table.Name, col.Name);
            }
        }
    }

    private static void DeriveAll(SchemaTable table)
    {
        if (string.IsNullOrWhiteSpace(table.StableUuid))
            table.StableUuid = SchemaIdentity.ForTable(table.Name);
        foreach (var col in table.Columns)
        {
            if (string.IsNullOrWhiteSpace(col.StableUuid))
                col.StableUuid = SchemaIdentity.ForColumn(table.Name, col.Name);
        }
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

    private static DbConnection CreateConnection(string connectionString, DatabaseType dbType)
    {
        return dbType switch
        {
            DatabaseType.MSSQL      => new SqlConnection(connectionString),
            DatabaseType.PostgreSQL => new NpgsqlConnection(connectionString),
            DatabaseType.MySQL      => new MySqlConnection(connectionString),
            DatabaseType.MariaDB    => new MySqlConnection(connectionString),
            DatabaseType.Oracle     => new OracleConnection(connectionString),
            DatabaseType.SQLite     => new SqliteConnection(connectionString),
            _                       => throw new NotSupportedException($"Database type {dbType} is not supported.")
        };
    }

    private static SchemaDiffResult CalculateDiff(DatabaseSchema oldSchema, DatabaseSchema newSchema)
    {
        var result = new SchemaDiffResult();
        var oldTables = oldSchema?.Tables ?? new List<SchemaTable>();
        var newTables = newSchema?.Tables ?? new List<SchemaTable>();

        bool MatchTable(SchemaTable a, SchemaTable b) =>
            (!string.IsNullOrEmpty(a.StableUuid) && !string.IsNullOrEmpty(b.StableUuid))
                ? string.Equals(a.StableUuid, b.StableUuid, StringComparison.OrdinalIgnoreCase)
                : string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

        bool MatchCol(SchemaColumn a, SchemaColumn b) =>
            (!string.IsNullOrEmpty(a.StableUuid) && !string.IsNullOrEmpty(b.StableUuid))
                ? string.Equals(a.StableUuid, b.StableUuid, StringComparison.OrdinalIgnoreCase)
                : string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);

        foreach (var nt in newTables)
        {
            var ot = oldTables.FirstOrDefault(o => MatchTable(o, nt));
            if (ot != null && !string.Equals(ot.Name, nt.Name, StringComparison.OrdinalIgnoreCase))
            {
                result.RenamedTables.Add(new TableRenameDetail { OldName = ot.Name, NewName = nt.Name });
            }
        }

        foreach (var nt in newTables)
        {
            if (!oldTables.Any(o => MatchTable(o, nt))) result.AddedTables.Add(nt.Name);
        }

        foreach (var ot in oldTables)
        {
            if (!newTables.Any(n => MatchTable(ot, n))) result.RemovedTables.Add(ot.Name);
        }

        foreach (var ot in oldTables)
        {
            var nt = newTables.FirstOrDefault(n => MatchTable(ot, n));
            if (nt == null) continue;

            var tableDiff = new TableDiffDetail { TableName = nt.Name };
            bool modified = false;

            foreach (var nc in nt.Columns)
            {
                var oc = ot.Columns.FirstOrDefault(o => MatchCol(o, nc));
                if (oc != null && !string.Equals(oc.Name, nc.Name, StringComparison.OrdinalIgnoreCase))
                {
                    tableDiff.RenamedColumns.Add(new ColumnRenameDetail { OldName = oc.Name, NewName = nc.Name });
                    modified = true;
                }
            }

            foreach (var nc in nt.Columns)
            {
                if (!ot.Columns.Any(o => MatchCol(o, nc)))
                {
                    tableDiff.AddedColumns.Add(nc.Name);
                    modified = true;
                }
            }

            foreach (var oc in ot.Columns)
            {
                if (!nt.Columns.Any(n => MatchCol(oc, n)))
                {
                    tableDiff.RemovedColumns.Add(oc.Name);
                    modified = true;
                }
            }

            foreach (var nc in nt.Columns)
            {
                var oc = ot.Columns.FirstOrDefault(o => MatchCol(o, nc));
                if (oc != null && string.Equals(oc.Name, nc.Name, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.Equals(oc.Type, nc.Type, StringComparison.OrdinalIgnoreCase) ||
                        oc.Length != nc.Length ||
                        oc.IsNullable != nc.IsNullable ||
                        oc.IsPK != nc.IsPK)
                    {
                        tableDiff.ModifiedColumns.Add(nc.Name);
                        modified = true;
                    }
                }
            }

            if (modified) result.ModifiedTables.Add(tableDiff);
        }

        return result;
    }

    // ── 1. pull_schema ───────────────────────────────────────────────────────

    [McpServerTool(Name = "namines_pull_schema")]
    [Description(
        "Read the live structure of a database and return it as a Namines schema (JSON). " +
        "Read-only: queries INFORMATION_SCHEMA and never modifies anything. " +
        "Feed the result into namines_analyze_impact as `base_schema_json`. Runs locally, so localhost works.")]
    public async Task<string> PullSchemaAsync(
        [Description("ADO.NET connection string for the database to read.")]
        string connectionString,
        [Description("Database engine: PostgreSQL, MSSQL, MySQL, MariaDB or Oracle.")]
        string engine,
        CancellationToken cancellationToken = default)
    {
        _connectionGuard.EnsureAllowed(connectionString, engine);
        var schema = await _introspection.IntrospectAsync(connectionString, engine, cancellationToken);
        return JsonSerializer.Serialize(schema, Json);
    }

    // ── 2. analyze_impact ────────────────────────────────────────────────────

    [McpServerTool(Name = "namines_analyze_impact")]
    [Description(
        "Deterministic schema impact analysis between base and target schemas. " +
        "Calculates overallRisk (Safe, Risky, Breaking, Destructive), findings, breaking changes, data-loss warnings, " +
        "lock risks, and rollback instructions. Pass Namines schema JSON.")]
    public string AnalyzeImpact(
        [Description("Current baseline schema JSON (from namines_pull_schema or project export).")]
        string baseSchemaJson,
        [Description("Proposed target schema JSON.")]
        string targetSchemaJson,
        [Description("Database engine: PostgreSQL, MSSQL, MySQL, MariaDB, Oracle or SQLite.")]
        string engine)
    {
        var baseSchema = ParseSchema(baseSchemaJson, nameof(baseSchemaJson));
        var targetSchema = ParseSchema(targetSchemaJson, nameof(targetSchemaJson));
        var dbType = ParseEngine(engine);

        var report = SchemaImpactAnalyzer.Analyze(baseSchema, targetSchema, dbType);
        return JsonSerializer.Serialize(report, Json);
    }

    // ── 3. prove_migration ───────────────────────────────────────────────────

    [McpServerTool(Name = "namines_prove_migration")]
    [Description(
        "Prove whether a migration/DDL actually runs against a real database engine container. " +
        "Starts a throwaway Docker container, executes the DDL, and reports whether the engine accepted it. " +
        "Returns raw error messages from the engine (e.g., MSSQL Msg 1785). Docker must be running on host.")]
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

    // ── 4. generate_ddl ──────────────────────────────────────────────────────

    [McpServerTool(Name = "namines_generate_ddl")]
    [Description(
        "Generate deterministic, engine-specific SQL DDL for a Namines schema. " +
        "Covers tables, columns, primary keys, foreign keys with referential actions, indexes, and constraints. " +
        "Golden-file tested across all 6 engines.")]
    public string GenerateDdl(
        [Description("Schema to generate DDL for, as Namines JSON.")]
        string schemaJson,
        [Description("Target engine: PostgreSQL, MSSQL, MySQL, MariaDB, Oracle or SQLite.")]
        string engine)
    {
        var schema = ParseSchema(schemaJson, nameof(schemaJson));
        return _ddlFactory.GetGenerator(ParseEngine(engine)).Generate(schema);
    }

    // ── 5. generate_prisma ───────────────────────────────────────────────────

    [McpServerTool(Name = "namines_generate_prisma")]
    [Description(
        "Generate a Prisma schema (schema.prisma) from a Namines schema. " +
        "Engine-aware: model/field names use @@map/@map, native column types are mapped, referential actions explicit. " +
        "Returns schema, env example, and warnings of structures Prisma cannot express (CHECK, partial index).")]
    public string GeneratePrisma(
        [Description("Schema to convert, as Namines JSON.")]
        string schemaJson,
        [Description("Target engine: PostgreSQL, MySQL, MariaDB, MSSQL or SQLite.")]
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

    // ── 6. generate_artifact (NEW: All 19 Eject Targets) ─────────────────────

    [McpServerTool(Name = "namines_generate_artifact")]
    [Description(
        "Generate source code artifacts from a Namines schema across 19 targets. " +
        "Supported targets: 'orm.drizzle', 'types.typescript', 'types.zod', 'types.pydantic', 'orm.sqlalchemy', " +
        "'types.graphql', 'types.csharp', 'orm.gorm', 'orm.typeorm', 'schema.nsl', 'schema.canonical-ir', " +
        "'migration.flyway', 'migration.liquibase', 'migration.golang-migrate', 'console.nextjs', 'schema.json-schema', 'types.protobuf'. " +
        "Short aliases like 'drizzle', 'zod', 'typescript', 'pydantic', 'sqlalchemy', 'gorm' are accepted.")]
    public string GenerateArtifact(
        [Description("Schema to generate code for, as Namines JSON.")]
        string schemaJson,
        [Description("Target technology (e.g. 'orm.drizzle', 'types.zod', 'types.typescript', 'types.pydantic', 'orm.sqlalchemy', 'types.graphql').")]
        string target,
        [Description("Database dialect: PostgreSQL, MySQL, MSSQL, SQLite, MariaDB, Oracle.")]
        string engine = "PostgreSQL")
    {
        var schema = ParseSchema(schemaJson, nameof(schemaJson));
        var dbType = ParseEngine(engine);

        var resolvedTarget = ResolveTargetAlias(target);
        var generator = _ejectRegistry.Get(resolvedTarget);
        var result = generator.Generate(schema, dbType);

        return JsonSerializer.Serialize(new
        {
            target = generator.Target,
            displayName = generator.DisplayName,
            engine = dbType.ToString(),
            files = result.Files,
            warnings = result.Warnings,
        }, Json);
    }

    // ── 7. list_artifact_targets (NEW) ───────────────────────────────────────

    [McpServerTool(Name = "namines_list_artifact_targets")]
    [Description(
        "List all 19 supported code-generation and schema-export targets supported by Namines, " +
        "including ORMs (Drizzle, Prisma, SQLAlchemy, TypeORM, Gorm), Types (TypeScript, Zod, Pydantic, C#, GraphQL, Protobuf), " +
        "Migrations (Flyway, Liquibase, golang-migrate), and Consoles.")]
    public string ListArtifactTargets()
    {
        var targets = _ejectRegistry.All.Select(g => new
        {
            id = g.Target,
            name = g.DisplayName,
            category = g.Target.Split('.')[0] switch
            {
                "orm" => "ORM",
                "types" => "Types & Validation",
                "schema" => "Schema & IR",
                "migration" => "Migration Tools",
                "console" => "Fullstack Console",
                _ => "Other"
            }
        }).ToList();

        return JsonSerializer.Serialize(targets, Json);
    }

    private static string ResolveTargetAlias(string input)
    {
        var t = input.Trim().ToLowerInvariant();
        return t switch
        {
            "drizzle" or "orm.drizzle" => "orm.drizzle",
            "zod" or "types.zod" => "types.zod",
            "typescript" or "ts" or "types.typescript" => "types.typescript",
            "typescript-sdk" or "sdk" or "sdk.typescript" or "types.typescript-sdk" => "sdk.typescript",
            "pydantic" or "python" or "types.python" or "types.pydantic" => "types.python",
            "sqlalchemy" or "orm.sqlalchemy" => "orm.sqlalchemy",
            "graphql" or "gql" or "contract.graphql" or "types.graphql" or "types.graphql-sdl" => "contract.graphql",
            "csharp" or "c#" or "types.csharp" => "types.csharp",
            "gorm" or "go" or "orm.gorm" => "orm.gorm",
            "typeorm" or "orm.typeorm" => "orm.typeorm",
            "django" or "orm.django" => "orm.django",
            "sequelize" or "orm.sequelize" => "orm.sequelize",
            "nsl" or "schema.nsl" => "nsl",
            "ir" or "ir.json" or "canonical-ir" or "schema.canonical-ir" => "ir.json",
            "jsonschema" or "json-schema" or "contract.jsonschema" or "schema.json-schema" => "contract.jsonschema",
            "protobuf" or "proto" or "contract.protobuf" or "types.protobuf" => "contract.protobuf",
            "console" or "nextjs" or "console.nextjs" => "console.nextjs",
            "flyway" or "mig.flyway" or "migration.flyway" => "mig.flyway",
            "liquibase" or "mig.liquibase" or "migration.liquibase" => "mig.liquibase",
            _ => input
        };
    }

    // ── 8. desk_execute_query (NEW: Desk SQL runner) ─────────────────────────

    [McpServerTool(Name = "namines_desk_execute_query")]
    [Description(
        "Safely execute a SQL query or mutation against a connected database via Namines Desk. " +
        "Guarded by SSRF policy. For SELECT queries, returns column schema and paginated rows. " +
        "For INSERT/UPDATE/DELETE/DDL, returns rows affected.")]
    public async Task<string> DeskExecuteQueryAsync(
        [Description("ADO.NET connection string.")]
        string connectionString,
        [Description("Database engine: PostgreSQL, MSSQL, MySQL, MariaDB, Oracle or SQLite.")]
        string engine,
        [Description("SQL query or command to execute.")]
        string sql,
        [Description("Maximum rows to return for SELECT queries (default: 50, max: 500).")]
        int maxRows = 50,
        CancellationToken cancellationToken = default)
    {
        _connectionGuard.EnsureAllowed(connectionString, engine);
        var dbType = ParseEngine(engine);

        var sw = Stopwatch.StartNew();
        await using var connection = CreateConnection(connectionString, dbType);
        await connection.OpenAsync(cancellationToken);

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 30;

        var isSelect = sql.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) ||
                       sql.TrimStart().StartsWith("WITH", StringComparison.OrdinalIgnoreCase);

        if (isSelect)
        {
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            var columns = new List<string>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }

            var rows = new List<Dictionary<string, object?>>();
            var limit = Math.Clamp(maxRows, 1, 500);

            while (await reader.ReadAsync(cancellationToken) && rows.Count < limit)
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.GetValue(i);
                    row[columns[i]] = val == DBNull.Value ? null : val;
                }
                rows.Add(row);
            }

            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                succeeded = true,
                isQuery = true,
                durationMs = sw.ElapsedMilliseconds,
                columns,
                rowCount = rows.Count,
                rows,
            }, Json);
        }
        else
        {
            var affected = await cmd.ExecuteNonQueryAsync(cancellationToken);
            sw.Stop();
            return JsonSerializer.Serialize(new
            {
                succeeded = true,
                isQuery = false,
                durationMs = sw.ElapsedMilliseconds,
                rowsAffected = affected,
            }, Json);
        }
    }

    // ── 9. desk_browse_table (NEW: Desk Table Explorer) ──────────────────────

    [McpServerTool(Name = "namines_desk_browse_table")]
    [Description(
        "Browse records in a database table through Namines Gateway with pagination and column selection. " +
        "Read-only and parameterized. Fast high-density table inspection for database models.")]
    public async Task<string> DeskBrowseTableAsync(
        [Description("ADO.NET connection string.")]
        string connectionString,
        [Description("Database engine: PostgreSQL, MSSQL, MySQL, MariaDB, Oracle or SQLite.")]
        string engine,
        [Description("Name of the table to browse.")]
        string tableName,
        [Description("Page index (1-based, default: 1).")]
        int page = 1,
        [Description("Page size (default: 20, max: 100).")]
        int pageSize = 20,
        [Description("Optional column name to order by.")]
        string? orderByColumn = null,
        CancellationToken cancellationToken = default)
    {
        _connectionGuard.EnsureAllowed(connectionString, engine);

        var clampedSize = Math.Clamp(pageSize, 1, 100);
        var clampedPage = Math.Max(1, page);

        var result = await _gateway.ListAsync(
            connectionString,
            engine,
            tableName,
            page: clampedPage,
            pageSize: clampedSize,
            orderByColumn: orderByColumn,
            includeTotalCount: true,
            cancellationToken: cancellationToken);

        return JsonSerializer.Serialize(result, Json);
    }

    // ── 10. hub_search_blueprints (NEW: Verified Architectures) ──────────────

    [McpServerTool(Name = "namines_hub_search_blueprints")]
    [Description(
        "Search certified, 3NF-compliant production database architectures from Namines Hub. " +
        "Includes complete models for FinTech, ERP, Multi-Tenant SaaS, E-Commerce, Healthcare EHR, AI Ops, " +
        "IoT Telemetry, Logistics, Social Graph, and Education LMS.")]
    public string HubSearchBlueprints(
        [Description("Search query keyword (e.g. 'fintech', 'saas', 'erp', 'healthcare', 'ecommerce', 'iot', 'ai').")]
        string? query = null,
        [Description("Optional category filter: 'FinTech', 'ERP', 'SaaS', 'E-Commerce', 'Healthcare', 'AI', 'Enterprise', 'General'.")]
        string? category = null)
    {
        var blueprints = HubBlueprintCatalog.Search(query, category)
            .Select(b => new
            {
                slug = b.Slug,
                name = b.Name,
                category = b.Category,
                description = b.Description,
                tags = b.Tags,
                starCount = b.StarCount,
                forkCount = b.ForkCount,
                tableCount = b.Schema.Tables.Count,
                tables = b.Schema.Tables.Select(t => t.Name).ToList(),
                relationCount = b.Schema.Relations.Count,
            })
            .ToList();

        return JsonSerializer.Serialize(blueprints, Json);
    }

    // ── 11. hub_get_blueprint (NEW: Get Full Architecture Schema) ────────────

    [McpServerTool(Name = "namines_hub_get_blueprint")]
    [Description(
        "Retrieve the complete, verified DatabaseSchema and DBA notes of a Namines Hub architectural blueprint by slug. " +
        "Ready to be directly analyzed, migrated, code-generated (Prisma/Drizzle/SQL), or deployed.")]
    public string HubGetBlueprint(
        [Description("Slug of the blueprint (e.g. 'fintech-ledger-engine', 'enterprise-erp-core', 'multitenant-b2b-saas', 'ecommerce-order-pipeline').")]
        string slug)
    {
        var bp = HubBlueprintCatalog.FindBySlug(slug)
                 ?? throw new ArgumentException($"Blueprint with slug '{slug}' not found. Use namines_hub_search_blueprints to see available architectures.");

        return JsonSerializer.Serialize(new
        {
            slug = bp.Slug,
            name = bp.Name,
            category = bp.Category,
            description = bp.Description,
            tags = bp.Tags,
            starCount = bp.StarCount,
            forkCount = bp.ForkCount,
            starterIssue = new
            {
                title = bp.StarterIssueTitle,
                body = bp.StarterIssueBody,
                table = bp.StarterIssueTable,
                nslRuleCode = bp.StarterIssueNslCode,
            },
            schema = bp.Schema,
        }, Json);
    }

    // ── 12. flow_match_rules (NEW: Flow Automation Matcher) ──────────────────

    [McpServerTool(Name = "namines_flow_match_rules")]
    [Description(
        "Evaluate Namines Flow automation rules against a proposed schema change. " +
        "Calculates schema diff and identifies triggered automation rules (e.g. backup on destructive changes, " +
        "webhook notifications on column modifications, index quality warnings).")]
    public string FlowMatchRules(
        [Description("Base schema JSON.")]
        string baseSchemaJson,
        [Description("Target schema JSON.")]
        string targetSchemaJson,
        [Description("Optional JSON list of custom AutomationRule objects. If omitted, standard Namines Flow policies are evaluated.")]
        string? rulesJson = null)
    {
        var baseSchema = ParseSchema(baseSchemaJson, nameof(baseSchemaJson));
        var targetSchema = ParseSchema(targetSchemaJson, nameof(targetSchemaJson));

        var diff = CalculateDiff(baseSchema, targetSchema);

        List<AutomationRule> rules;
        if (!string.IsNullOrWhiteSpace(rulesJson))
        {
            rules = JsonSerializer.Deserialize<List<AutomationRule>>(rulesJson, Json) ?? new();
        }
        else
        {
            rules = new List<AutomationRule>
            {
                new()
                {
                    Id = "rule_table_added",
                    Name = "Notify & Scaffold on Table Added",
                    TriggerType = "TableAdded",
                    Enabled = true,
                    Actions = new List<AutomationAction>
                    {
                        new() { Id = "act_1", ActionType = "Webhook", ActionConfigJson = "{\"channel\":\"slack\"}" }
                    }
                },
                new()
                {
                    Id = "rule_column_deleted",
                    Name = "Guard against Column Dropping",
                    TriggerType = "ColumnDeleted",
                    Enabled = true,
                    Actions = new List<AutomationAction>
                    {
                        new() { Id = "act_2", ActionType = "DbaCheck", ActionConfigJson = "{\"level\":\"strict\"}" }
                    }
                },
                new()
                {
                    Id = "rule_column_changed",
                    Name = "Type Mutation Audit",
                    TriggerType = "ColumnChanged",
                    Enabled = true,
                    Actions = new List<AutomationAction>
                    {
                        new() { Id = "act_3", ActionType = "Toast", ActionConfigJson = "{}" }
                    }
                }
            };
        }

        var matches = _flowMatcher.Match(diff, baseSchema, targetSchema, rules);

        return JsonSerializer.Serialize(new
        {
            diffSummary = new
            {
                tablesAdded = diff.AddedTables,
                tablesRemoved = diff.RemovedTables,
                tablesModified = diff.ModifiedTables.Select(m => new
                {
                    table = m.TableName,
                    addedCols = m.AddedColumns,
                    removedCols = m.RemovedColumns,
                    modifiedCols = m.ModifiedColumns
                }).ToList()
            },
            matchedRulesCount = matches.Count,
            matchedRules = matches.Select(m => new
            {
                ruleId = m.Rule.Id,
                ruleName = m.Rule.Name,
                trigger = m.Rule.TriggerType,
                actions = m.Rule.Actions.Select(a => a.ActionType).ToList(),
                context = new
                {
                    table = m.Context.TableName,
                    column = m.Context.ColumnName,
                    type = m.Context.ColumnType
                }
            }).ToList()
        }, Json);
    }

    // ── 13. open_change_request ──────────────────────────────────────────────

    [McpServerTool(Name = "namines_open_change_request")]
    [Description(
        "Open a Change Request for a proposed schema change on the Namines cloud server. " +
        "Creates a branch/CR for team discussion and human review. " +
        "Requires NAMINES_API_TOKEN environment variable. Does NOT touch the live database.")]
    public async Task<string> OpenChangeRequestAsync(
        [Description("ID of the project in Namines Cloud.")]
        string projectId,
        [Description("Proposed schema as Namines JSON.")]
        string schemaJson,
        [Description("Short descriptive title for the review.")]
        string? title = null,
        [Description("Context for the reviewer: why this change, and what the impact analysis found.")]
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        ParseSchema(schemaJson, nameof(schemaJson));

        if (string.IsNullOrWhiteSpace(projectId))
            throw new ArgumentException("projectId is empty.");

        var response = await _cloud.OpenChangeRequestAsync(
            projectId, schemaJson, title, message, cancellationToken);
        return JsonSerializer.Serialize(response, Json);
    }

    /// <summary>
    /// Yapılandırılmış hâl. Prisma üreticisinin ortak çağırma noktası.
    /// </summary>
    public PrismaGenerationResult GeneratePrismaFiles(string schemaJson, string engine)
    {
        var schema = ParseSchema(schemaJson, nameof(schemaJson));
        return _prisma.Generate(schema, ParseEngine(engine));
    }
}
