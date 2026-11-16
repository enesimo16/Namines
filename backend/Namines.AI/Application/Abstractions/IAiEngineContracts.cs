using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Ham LLM tamamlama motoruna giden sağlayıcıdan bağımsız istek.
/// </summary>
public sealed record LlmRequest(
    string SystemPrompt,
    string UserPrompt,
    double Temperature = 0.1,
    int? MaxOutputTokens = null,
    string? ModelHint = null,
    string? ProviderHint = null,
    byte[]? ImageBytes = null,
    string? ImageMimeType = null,
    bool JsonResponse = false,
    string? FeatureKey = null);

/// <summary>
/// LLM tamamlama motorunun standart yanıtı.
/// </summary>
public sealed record LlmResponse(
    string Content,
    string ModelUsed,
    int PromptTokens,
    int CompletionTokens,
    bool IsTruncated);

/// <summary>
/// Sağlayıcıdan bağımsız LLM tamamlama motoru arayüzü.
/// Namines.AI.Application yalnızca bu arayüze bağımlıdır; somut HTTP/sağlayıcı kodu
/// Infrastructure katmanında yaşar (modül sınır kuralı gereği).
/// </summary>
public interface ILlmCompletionEngine
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default);
    bool IsConfiguredFor(string? providerHint = null);
}

/// <summary>
/// L0 (topolojik iskelet), L1 (kompakt NSL alt grafiği) ve NSL->DatabaseSchema birleştirme motoru.
/// </summary>
public interface ISchemaContextEngine
{
    SchemaTopologicalSkeleton BuildSkeleton(
        DatabaseSchema schema,
        DatabaseType engine = DatabaseType.PostgreSQL);

    ScopedNslSubgraph ExtractSubgraph(
        DatabaseSchema schema,
        IReadOnlyList<string> focusTables,
        DatabaseType engine = DatabaseType.PostgreSQL);

    IReadOnlyList<TableSignatureContract> ExtractLockedContracts(
        DatabaseSchema schema,
        IReadOnlySet<string>? explicitLocks = null);

    (DatabaseSchema Merged, IReadOnlyList<string> Notes) MergeNslIntoSchema(
        DatabaseSchema current,
        string rawNslOrJsonFragment,
        DatabaseType engine = DatabaseType.PostgreSQL,
        bool preserveExistingUuids = true);
}

/// <summary>
/// Orkestratörlerin çalışma zamanında dinamik sub-agent üretip konuşturabildiği ve
/// görevi biteni emekliye ayırabildiği sürü yönetim çalışma zamanı.
/// </summary>
public interface IAgentSwarmRuntime
{
    AgentInstance SpawnAgent(
        AgentKind kind,
        string name,
        string purpose,
        NaiModel modelTier = NaiModel.Standard);

    void SendMessage(string fromAgentId, string toAgentId, string topic, string content);
    IReadOnlyList<AgentMessage> GetMessagesFor(string agentId);
    IReadOnlyList<AgentMessage> GetAllMessages();
    void RetireAgent(string agentId);
    void TerminateAgent(string agentId);
    IReadOnlyList<AgentInstance> GetActiveAgents();
    (int Spawned, int Retired, int Active) GetSwarmStats();
}

/// <summary>
/// Çalışma alanı araçlarından dönen standart sonuç.
/// </summary>
public sealed record AiToolExecutionResult(
    bool Success,
    string ToolName,
    string CompactSummary,
    DatabaseSchema? ExtractedSchema = null,
    IReadOnlyList<MinusFindingItem>? Findings = null,
    IReadOnlyDictionary<string, string>? Metadata = null);

/// <summary>
/// Orkestrasyon sırasında çoklu kaynaklardan (GitHub, JSON, OpenAPI, NSL Compiler, Code) bilgi toplayan araç.
/// </summary>
public interface IAiWorkspaceTool
{
    string Name { get; }
    string Description { get; }
    Task<AiToolExecutionResult> ExecuteAsync(
        OrchestrationWorkspace workspace,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken ct = default);
}

/// <summary>
/// Tüm çalışma alanı araçlarını adıyla keşfedip çalıştıran kayıt defteri.
/// </summary>
public interface IAiWorkspaceToolRegistry
{
    IReadOnlyList<IAiWorkspaceTool> Tools { get; }
    IAiWorkspaceTool? GetTool(string name);
    Task<AiToolExecutionResult> InvokeAsync(
        string toolName,
        OrchestrationWorkspace workspace,
        IReadOnlyDictionary<string, string>? parameters = null,
        CancellationToken ct = default);
}

/// <summary>
/// Dinamik olarak üretilen uzman sub-agent'ların ortak arayüzü.
/// </summary>
public interface ISubAgent<in TTask, TResult>
{
    AgentKind Kind { get; }
    Task<TResult> ExecuteAsync(AgentInstance self, TTask task, CancellationToken ct = default);
}

/// <summary>
/// Diyalektik oturum boyunca tüm orkestratörlerin ve sub-agent'ların paylaştığı çalışma alanı.
/// </summary>
public sealed class OrchestrationWorkspace
{
    private readonly object _sync = new();
    private readonly Dictionary<string, TableSignatureContract> _lockedContracts =
        new(StringComparer.OrdinalIgnoreCase);

    public OrchestrationWorkspace(
        OrchestrationTaskInput taskInput,
        IAgentSwarmRuntime swarm,
        DatabaseSchema initialSchema,
        IProgress<AgentStep>? progress = null)
    {
        TaskInput = taskInput ?? throw new ArgumentNullException(nameof(taskInput));
        Swarm = swarm ?? throw new ArgumentNullException(nameof(swarm));
        CurrentSchema = initialSchema ?? throw new ArgumentNullException(nameof(initialSchema));
        Progress = progress;
    }

    public OrchestrationTaskInput TaskInput { get; }
    public IAgentSwarmRuntime Swarm { get; }
    public IProgress<AgentStep>? Progress { get; }
    public string MasterAgentId { get; set; } = "master";
    public string PositiveAgentId { get; set; } = "positive";
    public string NegativeAgentId { get; set; } = "negative";

    public DatabaseSchema CurrentSchema { get; set; }
    public SchemaTopologicalSkeleton? Skeleton { get; set; }
    public TopologicalArchitecturePlan? Plan { get; set; }
    public MinusReport? LastMinusReport { get; set; }
    public List<PlusGainItem> CumulativePlusGains { get; } = new();
    public HashSet<string> CompletedDomains { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> MergeNotes { get; } = new();
    public List<string> Timeline { get; } = new();
    public List<string> SourceInsights { get; } = new();

    public IReadOnlyList<TableSignatureContract> LockedContracts
    {
        get
        {
            lock (_sync)
                return _lockedContracts.Values.ToList();
        }
    }

    public IReadOnlySet<string> LockedTableNames
    {
        get
        {
            lock (_sync)
                return new HashSet<string>(_lockedContracts.Keys, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void UpsertContracts(IEnumerable<TableSignatureContract> contracts)
    {
        lock (_sync)
        {
            foreach (var c in contracts)
                _lockedContracts[c.TableName] = c;
        }
    }

    public void AddMergeNotes(IEnumerable<string> notes)
    {
        lock (_sync)
        {
            foreach (var n in notes)
            {
                if (!string.IsNullOrWhiteSpace(n))
                    MergeNotes.Add(n);
            }
        }
    }

    public void AddGain(PlusGainItem gain)
    {
        lock (_sync)
            CumulativePlusGains.Add(gain);
    }

    public void RecordStep(AgentStep step)
    {
        lock (_sync)
            Timeline.Add($"[{step.Kind}] {step.Message}");

        Progress?.Report(step);
    }

    /// <summary>
    /// Üretilen tetikleyici ve saklı yordamları StableUuid koruyarak çalışma alanı şemasına birleştirir.
    /// </summary>
    public (int AddedTriggers, int UpdatedTriggers, int AddedProcedures, int UpdatedProcedures) UpsertProgrammability(
        IEnumerable<SchemaTrigger> triggers,
        IEnumerable<SchemaStoredProcedure> procedures)
    {
        var addedTrg = 0;
        var updatedTrg = 0;
        var addedProc = 0;
        var updatedProc = 0;

        lock (_sync)
        {
            foreach (var incoming in triggers)
            {
                if (string.IsNullOrWhiteSpace(incoming.Body)) continue;

                var existingIdx = CurrentSchema.Triggers.FindIndex(t =>
                    (!string.IsNullOrWhiteSpace(incoming.Id) &&
                     string.Equals(t.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(t.TableId, incoming.TableId, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(t.Timing, incoming.Timing, StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(t.Event, incoming.Event, StringComparison.OrdinalIgnoreCase) &&
                     t.TargetEngine == incoming.TargetEngine));

                if (existingIdx >= 0)
                {
                    var prev = CurrentSchema.Triggers[existingIdx];
                    if (string.IsNullOrWhiteSpace(incoming.Id))
                        incoming.Id = prev.Id;
                    if (!string.IsNullOrWhiteSpace(prev.StableUuid))
                        incoming.StableUuid = prev.StableUuid;
                    CurrentSchema.Triggers[existingIdx] = incoming;
                    updatedTrg++;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(incoming.Id))
                        incoming.Id = $"trg_{SchemaIdConvention.Normalize(incoming.TableId)}_{SchemaIdConvention.Normalize(incoming.Timing)}_{SchemaIdConvention.Normalize(incoming.Event)}";
                    if (string.IsNullOrWhiteSpace(incoming.StableUuid))
                        incoming.StableUuid = Guid.NewGuid().ToString();
                    CurrentSchema.Triggers.Add(incoming);
                    addedTrg++;
                }
            }

            foreach (var incoming in procedures)
            {
                if (string.IsNullOrWhiteSpace(incoming.Name) || string.IsNullOrWhiteSpace(incoming.Body)) continue;

                var existingIdx = CurrentSchema.StoredProcedures.FindIndex(p =>
                    (!string.IsNullOrWhiteSpace(incoming.Id) &&
                     string.Equals(p.Id, incoming.Id, StringComparison.OrdinalIgnoreCase)) ||
                    (string.Equals(p.Name, incoming.Name, StringComparison.OrdinalIgnoreCase) &&
                     p.TargetEngine == incoming.TargetEngine));

                if (existingIdx >= 0)
                {
                    var prev = CurrentSchema.StoredProcedures[existingIdx];
                    if (string.IsNullOrWhiteSpace(incoming.Id))
                        incoming.Id = prev.Id;
                    if (!string.IsNullOrWhiteSpace(prev.StableUuid))
                        incoming.StableUuid = prev.StableUuid;
                    CurrentSchema.StoredProcedures[existingIdx] = incoming;
                    updatedProc++;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(incoming.Id))
                        incoming.Id = $"sp_{SchemaIdConvention.Normalize(incoming.Name)}";
                    if (string.IsNullOrWhiteSpace(incoming.StableUuid))
                        incoming.StableUuid = Guid.NewGuid().ToString();
                    CurrentSchema.StoredProcedures.Add(incoming);
                    addedProc++;
                }
            }
        }

        return (addedTrg, updatedTrg, addedProc, updatedProc);
    }
}

/// <summary>
/// Ana Orkestratörün [+] Yapıcı Orkestratöre verdiği üretim/evrim emri.
/// </summary>
public sealed record PositiveDirective(
    int WaveOrIteration,
    TopologicalWave? TargetWave = null,
    IReadOnlyList<MinusFindingItem>? RepairFindings = null,
    bool IsCanvasEvolution = false);

/// <summary>
/// Ana Orkestratörün [-] Eleştirel Orkestratöre verdiği denetim emri.
/// </summary>
public sealed record NegativeDirective(
    int WaveOrIteration,
    bool RunDbaAudit = true,
    bool RunCrossEngineCompile = true,
    bool RunSemanticCoverage = true);

/// <summary>
/// [+] Yapıcı Alt-Orkestratör: artıları derler, dalga/evrim üretir ve kazanımları kilitler.
/// </summary>
public interface IPositiveOrchestrator
{
    Task<PlusReport> ExecuteAsync(
        OrchestrationWorkspace workspace,
        PositiveDirective directive,
        CancellationToken ct = default);
}

/// <summary>
/// [-] Eleştirel Alt-Orkestratör: eksileri/riskleri derler ve eyleme dönük direktif üretir.
/// </summary>
public interface INegativeOrchestrator
{
    Task<MinusReport> AuditAsync(
        OrchestrationWorkspace workspace,
        NegativeDirective directive,
        CancellationToken ct = default);
}

/// <summary>
/// Sınırlı özerkliğe sahip, [+] ve [-] orkestratörleri yöneten ve nihai kararı veren Ana Baş Orkestratör.
/// </summary>
public interface IMasterOrchestrator
{
    Task<MasterVerdict> OrchestrateAsync(
        OrchestrationTaskInput input,
        IProgress<AgentStep>? progress = null,
        CancellationToken ct = default);
}
