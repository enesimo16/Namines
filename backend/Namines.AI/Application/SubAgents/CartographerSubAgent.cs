using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Çoklu kaynakları (GitHub, Kod, JSON, OpenAPI) sıfır LLM token ile tarayıp
/// L0 Topolojik İskelet haritasını çıkaran uzman alt-ajan.
/// </summary>
public sealed class CartographerSubAgent : ISubAgent<OrchestrationWorkspace, SchemaTopologicalSkeleton>
{
    private readonly ISchemaContextEngine _contextEngine;
    private readonly IAiWorkspaceToolRegistry _tools;

    public CartographerSubAgent(
        ISchemaContextEngine contextEngine,
        IAiWorkspaceToolRegistry tools)
    {
        _contextEngine = contextEngine;
        _tools = tools;
    }

    public AgentKind Kind => AgentKind.Cartographer;

    public async Task<SchemaTopologicalSkeleton> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        var input = workspace.TaskInput;

        if (input.CodeFiles is { Count: > 0 })
        {
            var res = await _tools.InvokeAsync("code_schema_extract", workspace, ct: ct).ConfigureAwait(false);
            workspace.RecordStep(AgentStep.Tool("Kod şeması çıkarıldı", res.CompactSummary));
        }

        if (input.ObservedJsonResponses is { Count: > 0 })
        {
            var res = await _tools.InvokeAsync("json_shape_infer", workspace, ct: ct).ConfigureAwait(false);
            workspace.RecordStep(AgentStep.Tool("JSON şekilleri çıkarıldı", res.CompactSummary));
        }

        if (!string.IsNullOrWhiteSpace(input.ApiSpecUrl))
        {
            var res = await _tools.InvokeAsync("openapi_extract", workspace, ct: ct).ConfigureAwait(false);
            workspace.RecordStep(AgentStep.Tool("API şeması incelendi", res.CompactSummary));
        }

        if (!string.IsNullOrWhiteSpace(input.GithubRepoUrl))
        {
            var res = await _tools.InvokeAsync("github_extract", workspace, ct: ct).ConfigureAwait(false);
            workspace.RecordStep(AgentStep.Tool("GitHub deposu tarandı", res.CompactSummary));
        }

        var skeleton = _contextEngine.BuildSkeleton(workspace.CurrentSchema, input.Engine);
        workspace.Skeleton = skeleton;

        if (workspace.CurrentSchema.Tables.Count > 0)
        {
            var contracts = _contextEngine.ExtractLockedContracts(workspace.CurrentSchema);
            workspace.UpsertContracts(contracts);
        }

        var report = new StringBuilder();
        report.AppendLine($"Harita çıkarıldı: {skeleton.TableCount} tablo, {skeleton.RelationCount} ilişki, baskın PK={skeleton.DominantPkType}, adlandırma={skeleton.NamingConvention}.");
        if (workspace.SourceInsights.Count > 0)
            report.AppendLine($"Kaynak bulguları: {workspace.SourceInsights.Count} adet.");

        workspace.Swarm.SendMessage(
            self.AgentId,
            workspace.MasterAgentId,
            "cartography.ready",
            report.ToString().TrimEnd());

        return skeleton;
    }
}
