using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Github;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Core.Nsl;

namespace Namines.AI.Application;

/// <summary>
/// Çoklu kaynakları (kod, JSON gözlemleri, OpenAPI/GraphQL, GitHub, NSL/DDL derleyici, L1 alt grafik)
/// orkestratörlerin ve sub-agent'ların kullanımına açan merkezi araç kayıt defteri.
/// </summary>
public sealed class WorkspaceToolRegistry : IAiWorkspaceToolRegistry
{
    private readonly Dictionary<string, IAiWorkspaceTool> _tools;

    public WorkspaceToolRegistry(
        ISchemaContextEngine contextEngine,
        IDdlGeneratorFactory? ddlFactory = null,
        IApiSpecExtractor? apiSpecExtractor = null,
        IRepositoryScanner? repositoryScanner = null)
    {
        var list = new IAiWorkspaceTool[]
        {
            new CodeSchemaExtractTool(contextEngine),
            new JsonShapeInferTool(contextEngine),
            new OpenApiExtractTool(apiSpecExtractor),
            new GithubExtractTool(contextEngine, repositoryScanner),
            new DeterministicValidateCompileTool(ddlFactory),
            new SubgraphInspectTool(contextEngine),
        };

        _tools = list.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);
        Tools = list;
    }

    public IReadOnlyList<IAiWorkspaceTool> Tools { get; }

    public IAiWorkspaceTool? GetTool(string name)
        => _tools.TryGetValue(name, out var tool) ? tool : null;

    public Task<AiToolExecutionResult> InvokeAsync(
        string toolName,
        OrchestrationWorkspace workspace,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!_tools.TryGetValue(toolName, out var tool))
        {
            return Task.FromResult(new AiToolExecutionResult(
                false,
                toolName,
                $"Bilinmeyen çalışma alanı aracı: '{toolName}'."));
        }

        return tool.ExecuteAsync(workspace, parameters, ct);
    }

    internal static DatabaseSchema MergeExtractedIntoWorkspace(
        ISchemaContextEngine contextEngine,
        DatabaseSchema current,
        DatabaseSchema extracted,
        DatabaseType engine)
    {
        if (extracted.Tables.Count == 0 && extracted.Enums.Count == 0)
            return current;

        if (current.Tables.Count == 0)
        {
            return new DatabaseSchema
            {
                SchemaId = string.IsNullOrWhiteSpace(current.SchemaId) ? Guid.NewGuid().ToString() : current.SchemaId,
                Name = !string.IsNullOrWhiteSpace(current.Name) ? current.Name : extracted.Name,
                CloudProvider = current.CloudProvider ?? extracted.CloudProvider,
                IncludeBiModule = current.IncludeBiModule || extracted.IncludeBiModule,
                Tables = extracted.Tables.ToList(),
                Relations = extracted.Relations.ToList(),
                Enums = extracted.Enums.ToList(),
                Triggers = extracted.Triggers.ToList(),
                StoredProcedures = extracted.StoredProcedures.ToList(),
            };
        }

        var nsl = NslWriter.Write(extracted, engine);
        var (merged, _) = contextEngine.MergeNslIntoSchema(current, nsl, engine, preserveExistingUuids: true);
        return merged;
    }

    private sealed class CodeSchemaExtractTool : IAiWorkspaceTool
    {
        private readonly ISchemaContextEngine _contextEngine;
        public CodeSchemaExtractTool(ISchemaContextEngine contextEngine) => _contextEngine = contextEngine;

        public string Name => "code_schema_extract";
        public string Description => "Prisma, EF Core C# veya ham SQL dosyalarından 0-token ile deterministik şema çıkarır.";

        public Task<AiToolExecutionResult> ExecuteAsync(
            OrchestrationWorkspace workspace,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken ct = default)
        {
            var files = workspace.TaskInput.CodeFiles;
            if (files is null || files.Count == 0)
                return Task.FromResult(new AiToolExecutionResult(true, Name, "Kod dosyası verilmedi; atlandı."));

            try
            {
                var result = CodeSchemaExtractor.Extract(files);
                workspace.CurrentSchema = MergeExtractedIntoWorkspace(
                    _contextEngine,
                    workspace.CurrentSchema,
                    result.Schema,
                    workspace.TaskInput.Engine);

                foreach (var skip in result.Skipped)
                    workspace.SourceInsights.Add($"[code:{skip.Name}] atlandı: {skip.Reason}");

                var msg = $"Kod taraması ({result.Format}): {result.Schema.Tables.Count} tablo, {result.Schema.Relations.Count} ilişki çıkarıldı (atlanan: {result.Skipped.Count}).";
                workspace.SourceInsights.Add(msg);
                return Task.FromResult(new AiToolExecutionResult(true, Name, msg, result.Schema));
            }
            catch (Exception ex)
            {
                var warn = $"Kod taraması uyarı: {ex.Message}";
                workspace.SourceInsights.Add(warn);
                return Task.FromResult(new AiToolExecutionResult(false, Name, warn));
            }
        }
    }

    private sealed class JsonShapeInferTool : IAiWorkspaceTool
    {
        private readonly ISchemaContextEngine _contextEngine;
        public JsonShapeInferTool(ISchemaContextEngine contextEngine) => _contextEngine = contextEngine;

        public string Name => "json_shape_infer";
        public string Description => "Örnek JSON API yanıtlarından (değerleri saklamadan) varlık, tip ve FK ilişkisi çıkarır.";

        public Task<AiToolExecutionResult> ExecuteAsync(
            OrchestrationWorkspace workspace,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken ct = default)
        {
            var responses = workspace.TaskInput.ObservedJsonResponses;
            if (responses is null || responses.Count == 0)
                return Task.FromResult(new AiToolExecutionResult(true, Name, "Örnek JSON yanıtı yok; atlandı."));

            var inferred = JsonShapeInferencer.Infer(responses);
            var schema = new DatabaseSchema { Name = workspace.CurrentSchema.Name };
            var tablesByName = new Dictionary<string, SchemaTable>(StringComparer.OrdinalIgnoreCase);

            foreach (var entity in inferred.Entities)
            {
                var tableId = SchemaIdConvention.TableId(entity.Name);
                var cols = new List<SchemaColumn>();
                var hasExplicitId = entity.Fields.Any(f => string.Equals(f.Name, "id", StringComparison.OrdinalIgnoreCase));

                if (!hasExplicitId)
                {
                    cols.Add(new SchemaColumn
                    {
                        Id = $"{tableId}_id",
                        Name = "id",
                        Type = "UUID",
                        IsPK = true,
                        IsNullable = false,
                    });
                }

                foreach (var field in entity.Fields)
                {
                    var (sqlType, len, scale) = MapInferredType(field.Type, field.Name);
                    var isPk = string.Equals(field.Name, "id", StringComparison.OrdinalIgnoreCase);
                    cols.Add(new SchemaColumn
                    {
                        Id = $"{tableId}_{SchemaIdConvention.Normalize(field.Name)}",
                        Name = field.Name,
                        Type = sqlType,
                        Length = len,
                        Scale = scale,
                        IsPK = isPk,
                        IsFK = field.Name.EndsWith("_id", StringComparison.OrdinalIgnoreCase) && !isPk,
                        IsNullable = !isPk && field.IsUncertain,
                    });

                    if (field.IsUncertain)
                        workspace.SourceInsights.Add($"[json-belirsiz] {entity.Name}.{field.Name} yalnızca bazı örneklerde görüldü (nullable yapıldı).");
                }

                var table = new SchemaTable
                {
                    Id = tableId,
                    Name = entity.Name,
                    StableUuid = SchemaIdentity.ForTable(entity.Name),
                    Columns = cols,
                };
                schema.Tables.Add(table);
                tablesByName[entity.Name] = table;
            }

            foreach (var rel in inferred.Relations)
            {
                if (!tablesByName.TryGetValue(rel.FromEntity, out var src) ||
                    !tablesByName.TryGetValue(rel.ToEntity, out var dst))
                    continue;

                var srcCol = src.Columns.FirstOrDefault(c => string.Equals(c.Name, rel.FromField, StringComparison.OrdinalIgnoreCase));
                var dstCol = dst.Columns.FirstOrDefault(c => c.IsPK) ?? dst.Columns.FirstOrDefault();
                if (srcCol is null || dstCol is null)
                    continue;

                srcCol.IsFK = true;
                srcCol.Type = dstCol.Type;
                srcCol.Length = dstCol.Length;
                srcCol.Scale = dstCol.Scale;

                schema.Relations.Add(new SchemaRelation
                {
                    Id = $"{src.Name}_{srcCol.Name}_fk",
                    Type = "ManyToOne",
                    SourceTableId = src.Id,
                    SourceColumnId = srcCol.Id,
                    TargetTableId = dst.Id,
                    TargetColumnId = dstCol.Id,
                    OnDelete = ReferentialAction.NoAction,
                    OnUpdate = ReferentialAction.NoAction,
                });
            }

            workspace.CurrentSchema = MergeExtractedIntoWorkspace(
                _contextEngine,
                workspace.CurrentSchema,
                schema,
                workspace.TaskInput.Engine);

            var msg = $"JSON şekil çıkarımı: {inferred.Entities.Count} varlık, {inferred.Relations.Count} ilişki elde edildi.";
            workspace.SourceInsights.Add(msg);
            return Task.FromResult(new AiToolExecutionResult(true, Name, msg, schema));
        }

        private static (string Type, int? Len, int? Scale) MapInferredType(string inferredType, string fieldName)
        {
            var norm = (inferredType ?? string.Empty).Trim().ToLowerInvariant();
            if (norm == "uuid") return ("UUID", null, null);
            if (norm is "int" or "integer") return ("INT", null, null);
            if (norm is "bigint" or "long") return ("BIGINT", null, null);
            if (norm is "decimal" or "number" or "float" or "double") return ("DECIMAL", 19, 4);
            if (norm is "bool" or "boolean") return ("BOOLEAN", null, null);
            if (norm is "timestamp" or "datetime") return ("TIMESTAMP", null, null);
            if (norm == "date") return ("DATE", null, null);
            if (norm is "json" or "object" or "array") return ("JSONB", null, null);
            if (fieldName.Contains("email", StringComparison.OrdinalIgnoreCase)) return ("VARCHAR", 255, null);
            return ("VARCHAR", 255, null);
        }
    }

    private sealed class OpenApiExtractTool : IAiWorkspaceTool
    {
        private readonly IApiSpecExtractor? _extractor;
        public OpenApiExtractTool(IApiSpecExtractor? extractor) => _extractor = extractor;

        public string Name => "openapi_extract";
        public string Description => "OpenAPI/Swagger veya GraphQL şemasından varlık adaylarını çıkarır.";

        public async Task<AiToolExecutionResult> ExecuteAsync(
            OrchestrationWorkspace workspace,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken ct = default)
        {
            var url = workspace.TaskInput.ApiSpecUrl;
            if (string.IsNullOrWhiteSpace(url))
                return new AiToolExecutionResult(true, Name, "OpenAPI/GraphQL URL'si verilmedi; atlandı.");

            if (_extractor is null)
                return new AiToolExecutionResult(false, Name, "IApiSpecExtractor DI konteynerinde kayıtlı değil.");

            var result = await _extractor.ExtractAsync(url, ct).ConfigureAwait(false);
            if (!result.Success)
            {
                var failMsg = $"OpenAPI/GraphQL çıkarımı başarısız ({result.FailureReason}).";
                workspace.SourceInsights.Add(failMsg);
                return new AiToolExecutionResult(false, Name, failMsg);
            }

            foreach (var planned in result.Tables)
                workspace.SourceInsights.Add($"[{result.SourceKind}] varlık adayı: {planned.Name} — {planned.Reason}");

            return new AiToolExecutionResult(
                true,
                Name,
                $"{result.SourceKind} üzerinden {result.Tables.Count} varlık adayı çıkarıldı.");
        }
    }

    private sealed class GithubExtractTool : IAiWorkspaceTool
    {
        private readonly ISchemaContextEngine _contextEngine;
        private readonly IRepositoryScanner? _scanner;

        public GithubExtractTool(ISchemaContextEngine contextEngine, IRepositoryScanner? scanner)
        {
            _contextEngine = contextEngine;
            _scanner = scanner;
        }

        public string Name => "github_extract";
        public string Description => "GitHub deposunu tarayıp mevcut Prisma/EF/SQL şemasını çıkarır.";

        public async Task<AiToolExecutionResult> ExecuteAsync(
            OrchestrationWorkspace workspace,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken ct = default)
        {
            var repoUrl = workspace.TaskInput.GithubRepoUrl;
            if (string.IsNullOrWhiteSpace(repoUrl))
                return new AiToolExecutionResult(true, Name, "GitHub depo URL'si verilmedi; atlandı.");

            if (!GithubRepositoryUrl.TryParse(repoUrl, out var repo) || repo is null)
                return new AiToolExecutionResult(false, Name, $"Geçersiz GitHub URL: '{repoUrl}'.");

            if (_scanner is null)
                return new AiToolExecutionResult(false, Name, "IRepositoryScanner DI konteynerinde kayıtlı değil.");

            var scan = await _scanner.ScanAsync(repo, workspace.TaskInput.GithubBranch, null, ct).ConfigureAwait(false);
            workspace.CurrentSchema = MergeExtractedIntoWorkspace(
                _contextEngine,
                workspace.CurrentSchema,
                scan.Schema,
                workspace.TaskInput.Engine);

            foreach (var warning in scan.Warnings)
                workspace.SourceInsights.Add($"[github-uyarı] {warning}");

            var msg = $"GitHub ({repo.Owner}/{repo.Name}@{scan.Branch}, {scan.Format}): {scan.Schema.Tables.Count} tablo tarandı.";
            workspace.SourceInsights.Add(msg);
            return new AiToolExecutionResult(true, Name, msg, scan.Schema);
        }
    }

    private sealed class DeterministicValidateCompileTool : IAiWorkspaceTool
    {
        private static readonly DatabaseType[] AllEngines =
        {
            DatabaseType.PostgreSQL,
            DatabaseType.MySQL,
            DatabaseType.MSSQL,
            DatabaseType.Oracle,
            DatabaseType.SQLite,
            DatabaseType.MariaDB,
        };

        private readonly IDdlGeneratorFactory? _ddlFactory;
        public DeterministicValidateCompileTool(IDdlGeneratorFactory? ddlFactory) => _ddlFactory = ddlFactory;

        public string Name => "deterministic_validate_compile";
        public string Description => "0-token ile NslValidator + FkCascadeAnalyzer + 6 motor DDL derleme testi çalıştırır.";

        public Task<AiToolExecutionResult> ExecuteAsync(
            OrchestrationWorkspace workspace,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken ct = default)
        {
            var schema = workspace.CurrentSchema;
            var primaryEngine = workspace.TaskInput.Engine;
            var findings = new List<MinusFindingItem>();
            var idx = 0;

            foreach (var nf in NslValidator.Validate(schema, primaryEngine))
            {
                idx++;
                var severity = nf.Severity switch
                {
                    "error" => MinusSeverity.Blocker,
                    "warning" => MinusSeverity.Structural,
                    _ => MinusSeverity.DbaOptimization,
                };
                findings.Add(new MinusFindingItem(
                    Id: $"nsl-{idx}",
                    Code: nf.Code,
                    Severity: severity,
                    TableName: nf.Table,
                    ColumnName: nf.Column,
                    ProblemDescription: nf.Message,
                    ActionableDirective: nf.AutoFixable
                        ? $"Deterministik onarımcı {nf.Table}.{nf.Column} için {nf.Code} kuralını düzeltmeli."
                        : $"{nf.Table ?? "şema"} üzerinde {nf.Code} bulgusunu çöz: {nf.Message}",
                    SourceSubAgent: "DeterministicCritic",
                    IsDeterministicAutoFixable: nf.AutoFixable));
            }

            foreach (var ci in FkCascadeAnalyzer.Analyze(schema, primaryEngine))
            {
                idx++;
                findings.Add(new MinusFindingItem(
                    Id: $"cascade-{idx}",
                    Code: $"CASCADE-{ci.Kind}",
                    Severity: MinusSeverity.Blocker,
                    TableName: ci.FromTable,
                    ColumnName: null,
                    ProblemDescription: ci.Message,
                    ActionableDirective: $"{ci.FromTable} -> {ci.ToTable} ilişkisindeki cascade yayılımını NO ACTION'a çevir.",
                    SourceSubAgent: "DeterministicCritic",
                    IsDeterministicAutoFixable: true));
            }

            var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (_ddlFactory is not null)
            {
                foreach (var eng in AllEngines)
                {
                    try
                    {
                        var ddl = _ddlFactory.GetGenerator(eng).Generate(schema);
                        meta[$"ddl_{eng}"] = string.IsNullOrWhiteSpace(ddl) ? "empty" : $"ok:{ddl.Length}";
                        if (string.IsNullOrWhiteSpace(ddl) && schema.Tables.Count > 0 && eng == primaryEngine)
                        {
                            idx++;
                            findings.Add(new MinusFindingItem(
                                Id: $"ddl-{idx}",
                                Code: "DDL-EMPTY",
                                Severity: MinusSeverity.Blocker,
                                TableName: null,
                                ColumnName: null,
                                ProblemDescription: $"{eng} motoru için üretilen DDL boş çıktı.",
                                ActionableDirective: "Tabloların geçerli kolon ve tip taşıdığını doğrula.",
                                SourceSubAgent: "DeterministicCritic"));
                        }
                    }
                    catch (Exception ex)
                    {
                        meta[$"ddl_{eng}"] = $"fail:{ex.Message}";
                        if (eng == primaryEngine)
                        {
                            idx++;
                            findings.Add(new MinusFindingItem(
                                Id: $"ddl-{idx}",
                                Code: $"DDL-{eng}",
                                Severity: MinusSeverity.Blocker,
                                TableName: null,
                                ColumnName: null,
                                ProblemDescription: $"{eng} DDL derlemesi patladı: {ex.Message}",
                                ActionableDirective: $"{eng} ile uyumsuz kolon tipi, dizi veya enum referansını düzelt.",
                                SourceSubAgent: "DeterministicCritic"));
                        }
                    }
                }
            }

            var blockers = findings.Count(f => f.Severity == MinusSeverity.Blocker);
            return Task.FromResult(new AiToolExecutionResult(
                blockers == 0,
                Name,
                $"Doğrulama tamamlandı: {findings.Count} bulgu ({blockers} blocker).",
                Findings: findings,
                Metadata: meta));
        }
    }

    private sealed class SubgraphInspectTool : IAiWorkspaceTool
    {
        private readonly ISchemaContextEngine _contextEngine;
        public SubgraphInspectTool(ISchemaContextEngine contextEngine) => _contextEngine = contextEngine;

        public string Name => "subgraph_inspect";
        public string Description => "Seçili tablolar + 1-hop komşu sözleşmeleri için kompakt L1 NSL alt grafiği çıkarır.";

        public Task<AiToolExecutionResult> ExecuteAsync(
            OrchestrationWorkspace workspace,
            IReadOnlyDictionary<string, string>? parameters = null,
            CancellationToken ct = default)
        {
            var rawTables = parameters is not null && parameters.TryGetValue("tables", out var t)
                ? t.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                : workspace.CurrentSchema.Tables.Take(10).Select(x => x.Name).ToArray();

            var sub = _contextEngine.ExtractSubgraph(workspace.CurrentSchema, rawTables, workspace.TaskInput.Engine);
            return Task.FromResult(new AiToolExecutionResult(
                true,
                Name,
                sub.FocusNslText,
                Metadata: new Dictionary<string, string>
                {
                    ["estimated_tokens"] = sub.EstimatedTokens.ToString(),
                    ["neighbors"] = sub.NeighborContracts.Count.ToString(),
                }));
        }
    }
}
