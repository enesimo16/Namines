using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.AI.Domain;

/// <summary>
/// Otonom AI oturumunun beslendiği kaynak türü.
/// </summary>
public enum SchemaInputSourceKind
{
    PromptOnly = 0,
    ExistingCanvasSchema = 1,
    GithubRepository = 2,
    CodeFiles = 3,
    SampleJsonShapes = 4,
    OpenApiOrGraphQl = 5,
    LiveDatabase = 6,
    WhiteboardImage = 7,
}

/// <summary>
/// Ana Baş Orkestratörün tüm çoklu-kaynak ve bağlam girdilerini birleştiren görev tanımı.
/// </summary>
public sealed record OrchestrationTaskInput(
    string Prompt,
    DatabaseType Engine,
    SchemaInputSourceKind SourceKind = SchemaInputSourceKind.PromptOnly,
    DatabaseSchema? ExistingSchema = null,
    IReadOnlyList<string>? SelectedCanvasTables = null,
    IReadOnlyDictionary<string, string>? ClarifyingAnswers = null,
    IReadOnlyDictionary<string, string>? CodeFiles = null,
    IReadOnlyList<ObservedResponse>? ObservedJsonResponses = null,
    string? ApiSpecUrl = null,
    string? GithubRepoUrl = null,
    string? GithubBranch = null,
    string? ModelHint = null,
    string? ProviderHint = null,
    int MaxAutonomyWaves = 6,
    bool DeepDbaAudit = true,
    bool EnableAutonomyRepair = true,
    byte[]? ImageBytes = null,
    string? ImageMimeType = null);
