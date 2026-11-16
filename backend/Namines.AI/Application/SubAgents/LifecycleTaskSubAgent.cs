using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;
using Namines.Core.Nsl;
using Namines.Core.Prompts;

namespace Namines.AI.Application;

/// <summary>
/// Şema üretimi/revizyonu dışındaki tüm yaşam döngüsü görevlerini (NL->SQL, Migration,
/// SmartSeed, MockData, ProjectSummary, Streamlit, Vision ERD, DbContext parse, Impact Explainer)
/// NSL-sıkıştırılmış bağlam ve ILlmCompletionEngine üzerinden yürüten uzman alt-ajan.
/// </summary>
public sealed class LifecycleTaskSubAgent
{
    private readonly ILlmCompletionEngine _llm;

    public LifecycleTaskSubAgent(ILlmCompletionEngine llm)
    {
        _llm = llm;
    }

    public async Task<DatabaseSchema> ParseDbContextToSchemaAsync(
        string dbContextCode,
        DatabaseType dbType,
        CancellationToken ct = default)
    {
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: DbContextParsePromptBuilder.BuildSystemPrompt(),
                UserPrompt: DbContextParsePromptBuilder.BuildUserPrompt(dbContextCode, dbType.ToString()),
                Temperature: 0.1,
                JsonResponse: true,
                FeatureKey: "dbcontext_parse"),
            ct).ConfigureAwait(false);

        return DeserializeSchema(resp.Content);
    }

    public async Task<MigrationResult> GenerateMigrationCodeAsync(
        DatabaseSchema oldSchema,
        DatabaseSchema newSchema,
        DatabaseType dbType,
        CancellationToken ct = default)
    {
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: MigrationPromptBuilder.BuildSystemPrompt(),
                UserPrompt: MigrationPromptBuilder.BuildUserPrompt(oldSchema, newSchema, dbType),
                Temperature: 0.1,
                JsonResponse: true,
                FeatureKey: "migration"),
            ct).ConfigureAwait(false);

        var json = ExtractJsonObject(resp.Content);
        var sanitized = JsonSanitizerPreprocessor.Sanitize(json);
        return JsonSerializer.Deserialize<MigrationResult>(sanitized, SchemaJsonOptions.Default)
               ?? new MigrationResult { Summary = "Migration generated" };
    }

    public async Task<string> GenerateSmartSeedSqlAsync(
        DatabaseSchema schema,
        DatabaseType dbType,
        string domain,
        int rowCount,
        CancellationToken ct = default)
    {
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: SmartSeedPromptBuilder.BuildSystemPrompt(),
                UserPrompt: SmartSeedPromptBuilder.BuildUserPrompt(schema, dbType, domain, rowCount),
                Temperature: 0.2,
                FeatureKey: "smart_seed"),
            ct).ConfigureAwait(false);

        return NlQueryPromptBuilder.StripFences(resp.Content);
    }

    public async Task<string> GenerateMockDataAsync(
        DatabaseSchema schema,
        CancellationToken ct = default)
    {
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: MockDataPromptBuilder.BuildSystemPrompt(),
                UserPrompt: MockDataPromptBuilder.BuildUserPrompt(schema),
                Temperature: 0.3,
                JsonResponse: true,
                FeatureKey: "mock_data"),
            ct).ConfigureAwait(false);

        return resp.Content;
    }

    public async Task<string> GenerateProjectSummaryAsync(
        DatabaseSchema schema,
        string projectName,
        CancellationToken ct = default)
    {
        var nsl = NslWriter.Write(schema, DatabaseType.PostgreSQL);
        if (nsl.Length > 18_000) nsl = nsl[..18_000] + "\n// ...(truncated)";

        var system =
            "You are a concise Software & Database Architect.\n" +
            "Write a clear executive markdown summary explaining the domain model, core entities, key relationships, and security/performance highlights.";

        var user = $"Project Name: {projectName}\nTables: {schema.Tables.Count}, Relations: {schema.Relations.Count}\n\nNSL Schema:\n{nsl}";

        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: system,
                UserPrompt: user,
                Temperature: 0.3,
                FeatureKey: "summary"),
            ct).ConfigureAwait(false);

        return resp.Content;
    }

    public async Task<string> GenerateStreamlitAppAsync(
        DatabaseSchema schema,
        DatabaseType dbType,
        CancellationToken ct = default)
    {
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: StreamlitPromptBuilder.BuildSystemPrompt(),
                UserPrompt: StreamlitPromptBuilder.BuildUserPrompt(schema, dbType),
                Temperature: 0.2,
                FeatureKey: "streamlit"),
            ct).ConfigureAwait(false);

        return NlQueryPromptBuilder.StripFences(resp.Content);
    }

    public async Task<DatabaseSchema> AnalyzeImageAsync(
        byte[] imageBytes,
        string mimeType,
        CancellationToken ct = default)
    {
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: VisionPromptBuilder.BuildSystemPrompt(),
                UserPrompt: VisionPromptBuilder.BuildUserPrompt(),
                Temperature: 0.1,
                ImageBytes: imageBytes,
                ImageMimeType: mimeType,
                JsonResponse: true,
                FeatureKey: "vision_erd"),
            ct).ConfigureAwait(false);

        return DeserializeSchema(resp.Content);
    }

    public async Task<string> ExplainImpactAsync(
        ImpactReport impact,
        CancellationToken ct = default)
    {
        var (system, user) = ImpactExplainerPromptBuilder.Build(impact);
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: system,
                UserPrompt: user,
                Temperature: 0.2,
                FeatureKey: "impact_explain"),
            ct).ConfigureAwait(false);

        return resp.Content;
    }

    public async Task<string> GenerateSqlFromQuestionAsync(
        DatabaseSchema schema,
        DatabaseType dbType,
        string question,
        CancellationToken ct = default)
    {
        var (system, user) = NlQueryPromptBuilder.Build(schema, dbType, question);
        var resp = await _llm.CompleteAsync(
            new LlmRequest(
                SystemPrompt: system,
                UserPrompt: user,
                Temperature: 0.05,
                FeatureKey: "nl_to_sql"),
            ct).ConfigureAwait(false);

        return NlQueryPromptBuilder.StripFences(resp.Content);
    }

    private static DatabaseSchema DeserializeSchema(string raw)
    {
        var json = ExtractJsonObject(raw);
        var sanitized = JsonSanitizerPreprocessor.Sanitize(json);
        return JsonSerializer.Deserialize<DatabaseSchema>(sanitized, SchemaJsonOptions.Default)
               ?? new DatabaseSchema();
    }

    private static string ExtractJsonObject(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }
}
