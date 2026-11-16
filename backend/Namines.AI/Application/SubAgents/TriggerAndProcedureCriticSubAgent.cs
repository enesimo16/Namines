using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Şemadaki tetikleyiciler (<see cref="SchemaTrigger"/>) ve saklı yordamlar (<see cref="SchemaStoredProcedure"/>)
/// üzerindeki kırık tablo referanslarını, boş SQL gövdelerini, geçersiz event/timing çiftlerini ve
/// yıkıcı DDL ifadelerini 0-token ile yakalayan eleştirmen alt-ajan.
/// </summary>
public sealed class TriggerAndProcedureCriticSubAgent
    : ISubAgent<OrchestrationWorkspace, IReadOnlyList<MinusFindingItem>>
{
    public AgentKind Kind => AgentKind.TriggerAndProcedureCritic;

    public Task<IReadOnlyList<MinusFindingItem>> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var schema = workspace.CurrentSchema;
        var findings = new List<MinusFindingItem>();

        var validTableIds = schema.Tables
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var trg in schema.Triggers)
        {
            if (string.IsNullOrWhiteSpace(trg.TableId) || !validTableIds.Contains(trg.TableId))
            {
                findings.Add(new MinusFindingItem(
                    Id: $"prog-orphan-trg-{trg.Id}",
                    Code: "PROG-ORPHAN-TRIGGER",
                    Severity: MinusSeverity.Blocker,
                    TableName: trg.TableId,
                    ColumnName: null,
                    ProblemDescription: $"Trigger '{trg.Id}' var olmayan bir TableId ('{trg.TableId}') referanslıyor.",
                    ActionableDirective: "Trigger'ı geçerli bir SchemaTable.Id ile eşle.",
                    SourceSubAgent: nameof(TriggerAndProcedureCriticSubAgent),
                    IsDeterministicAutoFixable: true));
            }

            if (string.IsNullOrWhiteSpace(trg.Body))
            {
                findings.Add(new MinusFindingItem(
                    Id: $"prog-empty-trg-{trg.Id}",
                    Code: "PROG-EMPTY-BODY",
                    Severity: MinusSeverity.Blocker,
                    TableName: trg.TableId,
                    ColumnName: null,
                    ProblemDescription: $"Trigger '{trg.Id}' boş SQL gövdesine sahip.",
                    ActionableDirective: "Geçerli bir SQL trigger gövdesi yaz.",
                    SourceSubAgent: nameof(TriggerAndProcedureCriticSubAgent)));
            }

            if (!IsValidTiming(trg.Timing) || !IsValidEvent(trg.Event))
            {
                findings.Add(new MinusFindingItem(
                    Id: $"prog-event-trg-{trg.Id}",
                    Code: "PROG-INVALID-EVENT",
                    Severity: MinusSeverity.Structural,
                    TableName: trg.TableId,
                    ColumnName: null,
                    ProblemDescription: $"Trigger '{trg.Id}' geçersiz Timing/Event ('{trg.Timing}' / '{trg.Event}') taşıyor.",
                    ActionableDirective: "Timing için Before|After, Event için Insert|Update|Delete kullan.",
                    SourceSubAgent: nameof(TriggerAndProcedureCriticSubAgent),
                    IsDeterministicAutoFixable: true));
            }

            if (ContainsDestructiveSql(trg.Body))
            {
                findings.Add(new MinusFindingItem(
                    Id: $"prog-danger-trg-{trg.Id}",
                    Code: "PROG-DESTRUCTIVE-SQL",
                    Severity: MinusSeverity.Blocker,
                    TableName: trg.TableId,
                    ColumnName: null,
                    ProblemDescription: $"Trigger '{trg.Id}' içinde tehlikeli DROP DATABASE/SCHEMA veya TRUNCATE komutu tespit edildi.",
                    ActionableDirective: "Tetikleyici gövdesinden yıkıcı DDL komutunu kaldır.",
                    SourceSubAgent: nameof(TriggerAndProcedureCriticSubAgent)));
            }
        }

        foreach (var sp in schema.StoredProcedures)
        {
            if (string.IsNullOrWhiteSpace(sp.Name) || string.IsNullOrWhiteSpace(sp.Body))
            {
                findings.Add(new MinusFindingItem(
                    Id: $"prog-empty-sp-{sp.Id}",
                    Code: "PROG-EMPTY-BODY",
                    Severity: MinusSeverity.Blocker,
                    TableName: sp.Name,
                    ColumnName: null,
                    ProblemDescription: $"Stored Procedure '{sp.Name}' adı veya SQL gövdesi boş.",
                    ActionableDirective: "Prosedür adını ve geçerli SQL gövdesini tanımla.",
                    SourceSubAgent: nameof(TriggerAndProcedureCriticSubAgent)));
            }

            if (ContainsDestructiveSql(sp.Body))
            {
                findings.Add(new MinusFindingItem(
                    Id: $"prog-danger-sp-{sp.Id}",
                    Code: "PROG-DESTRUCTIVE-SQL",
                    Severity: MinusSeverity.Blocker,
                    TableName: sp.Name,
                    ColumnName: null,
                    ProblemDescription: $"Stored Procedure '{sp.Name}' içinde yıkıcı DROP DATABASE/SCHEMA veya TRUNCATE komutu tespit edildi.",
                    ActionableDirective: "Saklı yordamdan yıkıcı DDL komutlarını temizle.",
                    SourceSubAgent: nameof(TriggerAndProcedureCriticSubAgent)));
            }
        }

        if (findings.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.NegativeAgentId,
                "critic.programmability",
                $"{findings.Count} trigger/stored procedure bulgusu tespit edildi.");
        }

        return Task.FromResult<IReadOnlyList<MinusFindingItem>>(findings);
    }

    private static bool IsValidTiming(string? timing)
        => string.Equals(timing, "Before", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(timing, "After", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(timing, "InsteadOf", StringComparison.OrdinalIgnoreCase);

    private static bool IsValidEvent(string? ev)
        => string.Equals(ev, "Insert", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(ev, "Update", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(ev, "Delete", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsDestructiveSql(string? sql)
    {
        if (string.IsNullOrWhiteSpace(sql)) return false;
        return sql.Contains("DROP DATABASE", StringComparison.OrdinalIgnoreCase) ||
               sql.Contains("DROP SCHEMA", StringComparison.OrdinalIgnoreCase) ||
               sql.Contains("TRUNCATE TABLE", StringComparison.OrdinalIgnoreCase);
    }
}
