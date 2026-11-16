using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Eski GroqAIService / OllamaAIService ayrımını ortadan kaldırıp tüm yaşam döngüsü
/// sözleşmelerini (IAIService + IDbaSchemaAnalyzer) otonom diyalektik orkestrasyon
/// motoru üzerinden sunan tek birleşik cephe.
/// </summary>
public sealed class UnifiedAiService : IAIService
{
    private readonly IMasterOrchestrator _master;
    private readonly AdversarialDbaSubAgent _dba;
    private readonly LifecycleTaskSubAgent _lifecycle;
    private readonly string? _forcedProviderHint;

    public UnifiedAiService(
        IMasterOrchestrator master,
        AdversarialDbaSubAgent dba,
        LifecycleTaskSubAgent lifecycle,
        string? forcedProviderHint = null)
    {
        _master = master;
        _dba = dba;
        _lifecycle = lifecycle;
        _forcedProviderHint = forcedProviderHint;
    }

    public async Task<DatabaseSchema> GenerateSchemaAsync(GenerateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        byte[]? imageBytes = null;
        string? imageMime = null;
        if (request.Image is not null && request.Image.Length > 0)
        {
            await using var ms = new MemoryStream();
            await request.Image.CopyToAsync(ms).ConfigureAwait(false);
            imageBytes = ms.ToArray();
            imageMime = request.Image.ContentType;
        }

        IReadOnlyDictionary<string, string>? answers = null;
        if (!string.IsNullOrWhiteSpace(request.Answers))
        {
            try
            {
                answers = JsonSerializer.Deserialize<Dictionary<string, string>>(request.Answers, SchemaJsonOptions.Default);
            }
            catch
            {
                answers = null;
            }
        }

        var sourceKind = imageBytes is { Length: > 0 }
            ? SchemaInputSourceKind.WhiteboardImage
            : !string.IsNullOrWhiteSpace(request.ApiSpecUrl)
                ? SchemaInputSourceKind.OpenApiOrGraphQl
                : SchemaInputSourceKind.PromptOnly;

        var providerHint = !string.IsNullOrWhiteSpace(_forcedProviderHint)
            ? _forcedProviderHint
            : request.AIProvider;

        var task = new OrchestrationTaskInput(
            Prompt: request.Prompt,
            Engine: request.DbType,
            SourceKind: sourceKind,
            ClarifyingAnswers: answers,
            ApiSpecUrl: request.ApiSpecUrl,
            ModelHint: string.IsNullOrWhiteSpace(request.ModelName) ? null : request.ModelName,
            ProviderHint: string.IsNullOrWhiteSpace(providerHint) ? null : providerHint,
            MaxAutonomyWaves: request.Advanced ? 8 : 5,
            DeepDbaAudit: true,
            EnableAutonomyRepair: true,
            ImageBytes: imageBytes,
            ImageMimeType: imageMime);

        var verdict = await _master.OrchestrateAsync(task).ConfigureAwait(false);
        return verdict.FinalSchema;
    }

    public async Task<DatabaseSchema> ReviseSchemaAsync(ReviseRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var existing = new DatabaseSchema
        {
            Name = "canvas_schema",
            Tables = request.SelectedTables ?? new List<SchemaTable>(),
            Relations = request.ExistingRelations ?? new List<SchemaRelation>(),
            Enums = request.Enums ?? new List<SchemaEnum>(),
            Triggers = request.Triggers ?? new List<SchemaTrigger>(),
            StoredProcedures = request.StoredProcedures ?? new List<SchemaStoredProcedure>(),
        };

        var (parsedProvider, parsedEngine) = ParseProviderAndEngine(
            request.AIProvider,
            request.Triggers?.FirstOrDefault()?.TargetEngine ?? request.StoredProcedures?.FirstOrDefault()?.TargetEngine);

        var providerHint = !string.IsNullOrWhiteSpace(_forcedProviderHint)
            ? _forcedProviderHint
            : parsedProvider;

        var task = new OrchestrationTaskInput(
            Prompt: request.RevisionPrompt,
            Engine: parsedEngine,
            SourceKind: SchemaInputSourceKind.ExistingCanvasSchema,
            ExistingSchema: existing,
            SelectedCanvasTables: existing.Tables.ConvertAll(t => t.Name),
            ModelHint: string.IsNullOrWhiteSpace(request.ModelName) ? null : request.ModelName,
            ProviderHint: string.IsNullOrWhiteSpace(providerHint) ? null : providerHint,
            MaxAutonomyWaves: 3,
            DeepDbaAudit: false,
            EnableAutonomyRepair: true);

        var verdict = await _master.OrchestrateAsync(task).ConfigureAwait(false);
        return verdict.FinalSchema;
    }

    private static (string? Provider, DatabaseType Engine) ParseProviderAndEngine(
        string? rawProvider,
        DatabaseType? fallbackEngine)
    {
        var defaultEngine = fallbackEngine ?? DatabaseType.PostgreSQL;
        if (string.IsNullOrWhiteSpace(rawProvider))
            return (null, defaultEngine);

        var idx = rawProvider.IndexOf(':');
        if (idx < 0)
            return (rawProvider.Trim(), defaultEngine);

        var provider = rawProvider[..idx].Trim();
        var enginePart = rawProvider[(idx + 1)..].Trim();
        var parsedEngine = Enum.TryParse<DatabaseType>(enginePart, ignoreCase: true, out var db)
            ? db
            : defaultEngine;
        return (string.IsNullOrWhiteSpace(provider) ? null : provider, parsedEngine);
    }

    public async Task<List<DbaIssue>> AnalyzeSchemaDbaAsync(DatabaseSchema schema, DatabaseType dbType)
    {
        var self = new AgentInstance(
            "dba-direct",
            "dba-analyzer",
            AgentKind.DbaFinOpsSecurityCritic,
            NaiModel.Pro,
            AgentLifecycleState.Working,
            DateTimeOffset.UtcNow,
            "Doğrudan DBA analizi");

        var result = await _dba
            .ExecuteAsync(self, new DbaAuditInput(schema, dbType, UseAiDeepAudit: true, ProviderHint: _forcedProviderHint))
            .ConfigureAwait(false);

        return result.Issues.ToList();
    }

    public Task<DatabaseSchema> ParseDbContextToSchemaAsync(string dbContextCode, DatabaseType dbType)
        => _lifecycle.ParseDbContextToSchemaAsync(dbContextCode, dbType);

    public Task<MigrationResult> GenerateMigrationCodeAsync(DatabaseSchema oldSchema, DatabaseSchema newSchema, DatabaseType dbType)
        => _lifecycle.GenerateMigrationCodeAsync(oldSchema, newSchema, dbType);

    public Task<string> GenerateSmartSeedSqlAsync(DatabaseSchema schema, DatabaseType dbType, string domain, int rowCount)
        => _lifecycle.GenerateSmartSeedSqlAsync(schema, dbType, domain, rowCount);

    public Task<string> GenerateMockDataAsync(DatabaseSchema schema)
        => _lifecycle.GenerateMockDataAsync(schema);

    public Task<string> GenerateProjectSummaryAsync(DatabaseSchema schema, string projectName)
        => _lifecycle.GenerateProjectSummaryAsync(schema, projectName);

    public Task<string> GenerateStreamlitAppAsync(DatabaseSchema schema, DatabaseType dbType)
        => _lifecycle.GenerateStreamlitAppAsync(schema, dbType);

    public Task<DatabaseSchema> AnalyzeImageAsync(byte[] imageBytes, string mimeType)
        => _lifecycle.AnalyzeImageAsync(imageBytes, mimeType);

    public Task<string> ExplainImpactAsync(ImpactReport impact)
        => _lifecycle.ExplainImpactAsync(impact);

    public Task<string> GenerateSqlFromQuestionAsync(DatabaseSchema schema, DatabaseType dbType, string question)
        => _lifecycle.GenerateSqlFromQuestionAsync(schema, dbType, question);
}

/// <summary>
/// İstenen sağlayıcı ipucuna (Groq, DeepSeek, Gemini, OpenAI, Ollama) göre UnifiedAiService döndüren fabrika.
/// </summary>
public sealed class UnifiedAiFactory : IAIFactory
{
    private readonly IMasterOrchestrator _master;
    private readonly AdversarialDbaSubAgent _dba;
    private readonly LifecycleTaskSubAgent _lifecycle;

    public UnifiedAiFactory(
        IMasterOrchestrator master,
        AdversarialDbaSubAgent dba,
        LifecycleTaskSubAgent lifecycle)
    {
        _master = master;
        _dba = dba;
        _lifecycle = lifecycle;
    }

    public IAIService GetService(string? providerName) =>
        new UnifiedAiService(_master, _dba, _lifecycle, string.IsNullOrWhiteSpace(providerName) ? null : providerName);
}
