using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.AI.Application;

public sealed record ProgrammabilitySynthesisResult(
    IReadOnlyList<SchemaTrigger> Triggers,
    IReadOnlyList<SchemaStoredProcedure> StoredProcedures,
    IReadOnlyList<string> SummaryNotes);

/// <summary>
/// Trigger ve Stored Procedure (ve veritabanı rutinleri) üreten uzman alt-ajan.
/// İstemde tetikleyici / saklı yordam / fonksiyon talebi varsa önce LLM'den seçilen motorun
/// yerel lehçesinde (PL/pgSQL, T-SQL, MySQL, Oracle PL/SQL, SQLite) JSON çıktı ister;
/// LLM ulaşılamazsa veya eksik dönerse deterministik şablon sentezleyicisi ile boş dönmez.
/// </summary>
public sealed class ProgrammabilityArchitectSubAgent
    : ISubAgent<OrchestrationWorkspace, ProgrammabilitySynthesisResult>
{
    private static readonly string[] RoutineKeywords =
    {
        "trigger", "tetikleyici", "tetikle",
        "procedure", "stored procedure", "prosedür", "prosedur",
        "sp_", "fn_", "function", "fonksiyon", "routine", "rutin",
        "after insert", "before insert", "after update", "before update",
        "after delete", "before delete", "audit trigger", "stok düş", "stock",
        "ciro", "revenue", "plpgsql", "t-sql", "pl/sql",
    };

    private static readonly Regex CodeFenceRegex = new(
        @"^```[a-zA-Z0-9_-]*\s*|\s*```$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex ProcedureNameRegex = new(
        @"\b(?<name>(?:sp|usp|proc|fn)_[a-zA-Z0-9_]+)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILlmCompletionEngine _llm;
    private readonly ISchemaContextEngine _contextEngine;

    public ProgrammabilityArchitectSubAgent(
        ILlmCompletionEngine llm,
        ISchemaContextEngine contextEngine)
    {
        _llm = llm;
        _contextEngine = contextEngine;
    }

    public AgentKind Kind => AgentKind.ProgrammabilityArchitect;

    public static bool IsProgrammabilityRequested(string? prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        foreach (var kw in RoutineKeywords)
        {
            if (prompt.Contains(kw, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Kullanıcı yalnızca trigger / stored procedure istiyor, yeni tablo/kolon istemiyorsa true döner.
    /// Böylece CanvasEvolution LLM'ine gereksiz yere şema rewrite çağrısı yapılmaz.
    /// </summary>
    public static bool IsPureProgrammabilityRequest(string? prompt, DatabaseSchema existing)
    {
        if (string.IsNullOrWhiteSpace(prompt) || existing.Tables.Count == 0)
            return false;

        if (!IsProgrammabilityRequested(prompt))
            return false;

        var schemaStructureKeywords = new[]
        {
            "add table", "new table", "create table", "tablo ekle", "yeni tablo",
            "add column", "kolon ekle", "sütun ekle", "sutun ekle",
            "drop column", "kolon sil", "drop table", "tablo sil", "rename",
            "relation", "ilişki kur", "iliski kur", "enum ekle", "add enum",
        };

        return !schemaStructureKeywords.Any(k => prompt.Contains(k, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<ProgrammabilitySynthesisResult> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var input = workspace.TaskInput;
        if (!IsProgrammabilityRequested(input.Prompt) || workspace.CurrentSchema.Tables.Count == 0)
            return new ProgrammabilitySynthesisResult(Array.Empty<SchemaTrigger>(), Array.Empty<SchemaStoredProcedure>(), Array.Empty<string>());

        var triggers = new List<SchemaTrigger>();
        var procedures = new List<SchemaStoredProcedure>();
        var notes = new List<string>();

        if (_llm.IsConfiguredFor(input.ProviderHint))
        {
            var raw = await RequestProgrammabilityFromLlmAsync(workspace, ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(raw))
                ParseLlmPayload(raw, workspace.CurrentSchema, input.Engine, triggers, procedures);
        }

        // LLM hiç üretmediyse veya bir kısmı eksik kaldıysa istemdeki niyete göre deterministik sentez yap.
        SynthesizeMissingDeterministicRoutines(input.Prompt, workspace.CurrentSchema, input.Engine, triggers, procedures);

        var (addedTrg, updTrg, addedSp, updSp) = workspace.UpsertProgrammability(triggers, procedures);

        if (addedTrg + updTrg > 0)
            notes.Add($"Trigger'lar güncellendi (+{addedTrg} yeni, {updTrg} güncel) [{input.Engine}].");
        if (addedSp + updSp > 0)
            notes.Add($"Stored Procedure'ler güncellendi (+{addedSp} yeni, {updSp} güncel) [{input.Engine}].");

        if (notes.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.PositiveAgentId,
                "programmability.authored",
                string.Join(" ", notes));
        }

        return new ProgrammabilitySynthesisResult(triggers, procedures, notes);
    }

    private async Task<string> RequestProgrammabilityFromLlmAsync(
        OrchestrationWorkspace workspace,
        CancellationToken ct)
    {
        var input = workspace.TaskInput;
        var focusTables = workspace.CurrentSchema.Tables.Take(12).Select(t => t.Name).ToList();
        var skeleton = workspace.Skeleton ?? _contextEngine.BuildSkeleton(workspace.CurrentSchema, input.Engine);
        var subgraph = _contextEngine.ExtractSubgraph(workspace.CurrentSchema, focusTables, input.Engine);

        var sys =
            "You are Namines Database Programmability Architect.\n" +
            "Generate database Triggers and Stored Procedures for the requested target SQL dialect.\n" +
            "Respond ONLY with valid JSON matching this schema:\n" +
            "{\n" +
            "  \"triggers\": [\n" +
            "    {\n" +
            "      \"id\": \"trg_table_after_insert\",\n" +
            "      \"tableName\": \"exact_table_name\",\n" +
            "      \"timing\": \"Before\" | \"After\",\n" +
            "      \"event\": \"Insert\" | \"Update\" | \"Delete\",\n" +
            "      \"body\": \"Complete executable SQL trigger definition for target engine\"\n" +
            "    }\n" +
            "  ],\n" +
            "  \"storedProcedures\": [\n" +
            "    {\n" +
            "      \"id\": \"sp_routine_name\",\n" +
            "      \"name\": \"sp_routine_name\",\n" +
            "      \"parameters\": [{ \"name\": \"p_id\", \"type\": \"UUID\" }],\n" +
            "      \"body\": \"Complete executable CREATE [OR REPLACE] PROCEDURE/FUNCTION statement\"\n" +
            "    }\n" +
            "  ]\n" +
            "}\n" +
            "Use exact table and column names from the provided schema. Output strict JSON only.";

        var user = new StringBuilder();
        user.AppendLine($"Target Engine: {input.Engine}");
        user.AppendLine($"Request: {input.Prompt}");
        user.AppendLine();
        user.AppendLine("Schema Topology (L0):");
        user.AppendLine(skeleton.CompactTextMap);
        user.AppendLine();
        user.AppendLine("Schema Details (L1 NSL):");
        user.AppendLine(subgraph.FocusNslText);

        try
        {
            var res = await _llm.CompleteAsync(
                new LlmRequest(
                    SystemPrompt: sys,
                    UserPrompt: user.ToString(),
                    Temperature: 0.1,
                    ModelHint: input.ModelHint,
                    ProviderHint: input.ProviderHint,
                    JsonResponse: true,
                    FeatureKey: "schema_revise"),
                ct).ConfigureAwait(false);
            return res.Content;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void ParseLlmPayload(
        string raw,
        DatabaseSchema schema,
        DatabaseType engine,
        List<SchemaTrigger> triggers,
        List<SchemaStoredProcedure> procedures)
    {
        var cleaned = CodeFenceRegex.Replace(raw, string.Empty).Trim();
        var start = cleaned.IndexOf('{');
        var end = cleaned.LastIndexOf('}');
        if (start < 0 || end <= start) return;

        var jsonSlice = cleaned[start..(end + 1)];
        try
        {
            using var doc = JsonDocument.Parse(JsonSanitizerPreprocessor.Sanitize(jsonSlice));
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return;

            if (root.TryGetProperty("triggers", out var trgArr) && trgArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in trgArr.EnumerateArray())
                {
                    var body = ReadString(item, "body");
                    if (string.IsNullOrWhiteSpace(body)) continue;

                    var tableRef = ReadString(item, "tableId");
                    if (string.IsNullOrWhiteSpace(tableRef))
                        tableRef = ReadString(item, "tableName");

                    var matchedTable = ResolveTable(schema, tableRef);
                    var timing = NormalizeTiming(ReadString(item, "timing"));
                    var ev = NormalizeEvent(ReadString(item, "event"));
                    var id = ReadString(item, "id");
                    if (string.IsNullOrWhiteSpace(id))
                        id = $"trg_{SchemaIdConvention.Normalize(matchedTable.Name)}_{timing.ToLowerInvariant()}_{ev.ToLowerInvariant()}";

                    triggers.Add(new SchemaTrigger
                    {
                        Id = id,
                        StableUuid = Guid.NewGuid().ToString(),
                        TableId = matchedTable.Id,
                        Timing = timing,
                        Event = ev,
                        TargetEngine = engine,
                        Body = body.Trim(),
                    });
                }
            }

            if (root.TryGetProperty("storedProcedures", out var spArr) && spArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in spArr.EnumerateArray())
                {
                    var name = ReadString(item, "name");
                    var body = ReadString(item, "body");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body)) continue;

                    var parms = new List<SchemaStoredProcedureParameter>();
                    if (item.TryGetProperty("parameters", out var pArr) && pArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var p in pArr.EnumerateArray())
                        {
                            var pName = ReadString(p, "name");
                            var pType = ReadString(p, "type");
                            if (!string.IsNullOrWhiteSpace(pName))
                            {
                                parms.Add(new SchemaStoredProcedureParameter
                                {
                                    Name = pName,
                                    Type = string.IsNullOrWhiteSpace(pType) ? "VARCHAR" : pType,
                                });
                            }
                        }
                    }

                    var id = ReadString(item, "id");
                    procedures.Add(new SchemaStoredProcedure
                    {
                        Id = string.IsNullOrWhiteSpace(id) ? SchemaIdConvention.Normalize(name) : id,
                        StableUuid = Guid.NewGuid().ToString(),
                        Name = name.Trim(),
                        TargetEngine = engine,
                        Parameters = parms,
                        Body = body.Trim(),
                    });
                }
            }
        }
        catch
        {
            // Ayrıştırma başarısız olursa alt satırdaki deterministik sentez tamamlar.
        }
    }

    private static void SynthesizeMissingDeterministicRoutines(
        string prompt,
        DatabaseSchema schema,
        DatabaseType engine,
        List<SchemaTrigger> triggers,
        List<SchemaStoredProcedure> procedures)
    {
        var wantsTrigger = prompt.Contains("trigger", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("tetikleyici", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("after insert", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("before insert", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("after update", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("before update", StringComparison.OrdinalIgnoreCase) ||
                           prompt.Contains("after delete", StringComparison.OrdinalIgnoreCase);

        var wantsProcedure = prompt.Contains("procedure", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("prosedür", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("prosedur", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("sp_", StringComparison.OrdinalIgnoreCase) ||
                             prompt.Contains("routine", StringComparison.OrdinalIgnoreCase);

        if (wantsTrigger && triggers.Count == 0 && schema.Tables.Count > 0)
        {
            var target = ResolveTableFromPrompt(schema, prompt);
            var timing = prompt.Contains("before", StringComparison.OrdinalIgnoreCase) ? "Before" : "After";
            var ev = prompt.Contains("delete", StringComparison.OrdinalIgnoreCase)
                ? "Delete"
                : prompt.Contains("update", StringComparison.OrdinalIgnoreCase)
                    ? "Update"
                    : "Insert";

            triggers.Add(new SchemaTrigger
            {
                Id = $"trg_{SchemaIdConvention.Normalize(target.Name)}_{timing.ToLowerInvariant()}_{ev.ToLowerInvariant()}",
                StableUuid = Guid.NewGuid().ToString(),
                TableId = target.Id,
                Timing = timing,
                Event = ev,
                TargetEngine = engine,
                Body = BuildEngineTriggerSql(target, timing, ev, engine),
            });
        }

        if (wantsProcedure && procedures.Count == 0 && schema.Tables.Count > 0)
        {
            var match = ProcedureNameRegex.Match(prompt);
            var target = ResolveTableFromPrompt(schema, prompt);
            var spName = match.Success
                ? SchemaIdConvention.Normalize(match.Groups["name"].Value)
                : $"sp_process_{SchemaIdConvention.Normalize(target.Name)}";

            var pkCol = target.Columns.FirstOrDefault(c => c.IsPK);
            var paramType = pkCol?.Type ?? "UUID";
            var paramName = $"p_{SchemaIdConvention.Normalize(target.Name)}_id";

            procedures.Add(new SchemaStoredProcedure
            {
                Id = spName,
                StableUuid = Guid.NewGuid().ToString(),
                Name = spName,
                TargetEngine = engine,
                Parameters = new List<SchemaStoredProcedureParameter>
                {
                    new() { Name = paramName, Type = paramType },
                },
                Body = BuildEngineProcedureSql(spName, target, paramName, paramType, engine),
            });
        }
    }

    private static SchemaTable ResolveTable(DatabaseSchema schema, string? tableRef)
    {
        if (!string.IsNullOrWhiteSpace(tableRef))
        {
            var byId = schema.Tables.FirstOrDefault(t => string.Equals(t.Id, tableRef, StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;

            var byName = schema.Tables.FirstOrDefault(t => string.Equals(t.Name, tableRef, StringComparison.OrdinalIgnoreCase));
            if (byName is not null) return byName;
        }
        return schema.Tables[0];
    }

    private static SchemaTable ResolveTableFromPrompt(DatabaseSchema schema, string prompt)
    {
        var lower = prompt.ToLowerInvariant();
        foreach (var table in schema.Tables)
        {
            var norm = table.Name.ToLowerInvariant();
            var singular = norm.EndsWith('s') && norm.Length > 2 ? norm[..^1] : norm;
            if (lower.Contains(norm, StringComparison.Ordinal) ||
                lower.Contains(singular, StringComparison.Ordinal))
                return table;
        }
        return schema.Tables[0];
    }

    private static string BuildEngineTriggerSql(SchemaTable table, string timing, string ev, DatabaseType engine)
    {
        var tName = table.Name;
        var timingSql = timing.Equals("Before", StringComparison.OrdinalIgnoreCase) ? "BEFORE" : "AFTER";
        var eventSql = ev.ToUpperInvariant();
        var trgName = $"trg_{SchemaIdConvention.Normalize(tName)}_{timing.ToLowerInvariant()}_{ev.ToLowerInvariant()}";
        var fnName = $"fn_{trgName}";
        var hasUpdatedAt = table.Columns.Any(c => string.Equals(c.Name, "updated_at", StringComparison.OrdinalIgnoreCase));
        var pgAction = hasUpdatedAt && eventSql == "UPDATE"
            ? "NEW.updated_at := CURRENT_TIMESTAMP;"
            : "-- Domain side-effect / audit assertion";

        return engine switch
        {
            DatabaseType.MSSQL =>
                $"CREATE OR ALTER TRIGGER [dbo].[{trgName}] ON [dbo].[{tName}]\n{timingSql} {eventSql}\nAS\nBEGIN\n  SET NOCOUNT ON;\n  -- Trigger logic on {tName}\n  {(hasUpdatedAt ? $"UPDATE t SET updated_at = GETUTCDATE() FROM [dbo].[{tName}] t INNER JOIN inserted i ON t.id = i.id;" : "SELECT 1;")}\nEND;",
            DatabaseType.MySQL or DatabaseType.MariaDB =>
                $"CREATE TRIGGER `{trgName}` {timingSql} {eventSql} ON `{tName}`\nFOR EACH ROW\nBEGIN\n  {(hasUpdatedAt && timingSql == "BEFORE" ? "SET NEW.updated_at = CURRENT_TIMESTAMP;" : "SET @namines_trg_fired = 1;")}\nEND;",
            DatabaseType.Oracle =>
                $"CREATE OR REPLACE TRIGGER \"{trgName}\"\n{timingSql} {eventSql} ON \"{tName}\"\nFOR EACH ROW\nBEGIN\n  {(hasUpdatedAt && timingSql == "BEFORE" ? ":NEW.updated_at := CURRENT_TIMESTAMP;" : "NULL;")}\nEND;",
            DatabaseType.SQLite =>
                $"CREATE TRIGGER IF NOT EXISTS \"{trgName}\" {timingSql} {eventSql} ON \"{tName}\"\nFOR EACH ROW\nBEGIN\n  {(hasUpdatedAt ? $"UPDATE \"{tName}\" SET updated_at = CURRENT_TIMESTAMP WHERE id = NEW.id;" : "SELECT 1;")}\nEND;",
            _ =>
                $"CREATE OR REPLACE FUNCTION {fnName}() RETURNS TRIGGER AS $$\nBEGIN\n  {pgAction}\n  RETURN NEW;\nEND;\n$$ LANGUAGE plpgsql;\n\nCREATE TRIGGER {trgName}\n{timingSql} {eventSql} ON \"{tName}\"\nFOR EACH ROW EXECUTE FUNCTION {fnName}();",
        };
    }

    private static string BuildEngineProcedureSql(
        string spName,
        SchemaTable table,
        string paramName,
        string paramType,
        DatabaseType engine)
    {
        var pkName = table.Columns.FirstOrDefault(c => c.IsPK)?.Name ?? "id";
        return engine switch
        {
            DatabaseType.MSSQL =>
                $"CREATE OR ALTER PROCEDURE [dbo].[{spName}]\n  @{paramName} {MapSqlType(paramType, engine)}\nAS\nBEGIN\n  SET NOCOUNT ON;\n  SELECT * FROM [dbo].[{table.Name}] WHERE [{pkName}] = @{paramName};\nEND;",
            DatabaseType.MySQL or DatabaseType.MariaDB =>
                $"CREATE PROCEDURE `{spName}`(IN `{paramName}` {MapSqlType(paramType, engine)})\nBEGIN\n  SELECT * FROM `{table.Name}` WHERE `{pkName}` = `{paramName}`;\nEND;",
            DatabaseType.Oracle =>
                $"CREATE OR REPLACE PROCEDURE \"{spName}\"(\"{paramName}\" IN {MapSqlType(paramType, engine)}) AS\nBEGIN\n  NULL;\nEND;",
            _ =>
                $"CREATE OR REPLACE PROCEDURE {spName}({paramName} {MapSqlType(paramType, engine)})\nLANGUAGE plpgsql\nAS $$\nBEGIN\n  PERFORM 1 FROM \"{table.Name}\" WHERE \"{pkName}\" = {paramName};\nEND;\n$$;",
        };
    }

    private static string MapSqlType(string canonical, DatabaseType engine)
    {
        var upper = (canonical ?? "UUID").Trim().ToUpperInvariant();
        if (upper == "UUID")
        {
            return engine switch
            {
                DatabaseType.MSSQL => "UNIQUEIDENTIFIER",
                DatabaseType.MySQL or DatabaseType.MariaDB => "CHAR(36)",
                DatabaseType.Oracle => "RAW(16)",
                _ => "UUID",
            };
        }
        return upper;
    }

    private static string NormalizeTiming(string? raw)
        => string.Equals(raw?.Trim(), "Before", StringComparison.OrdinalIgnoreCase) ? "Before" : "After";

    private static string NormalizeEvent(string? raw) => (raw ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "UPDATE" => "Update",
        "DELETE" => "Delete",
        _ => "Insert",
    };

    private static string ReadString(JsonElement el, string prop)
        => el.TryGetProperty(prop, out var val) && val.ValueKind == JsonValueKind.String
            ? val.GetString() ?? string.Empty
            : string.Empty;
}
