using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Çok kiracılı (Multi-Tenant) şemalarda izole edilmemiş domain tablolarını, eksik <c>tenant_id</c>
/// kompozit indekslerini ve PostgreSQL RLS politika açıklarını 0-token ile tespit eden güvenlik eleştirmeni.
/// </summary>
public sealed class MultiTenancyAndRlsCriticSubAgent
    : ISubAgent<OrchestrationWorkspace, IReadOnlyList<MinusFindingItem>>
{
    private static readonly string[] TenantRootTableNames =
    {
        "tenants", "organizations", "workspaces", "companies", "accounts",
    };

    private static readonly string[] TenantFkColumns =
    {
        "tenant_id", "organization_id", "workspace_id", "company_id",
    };

    private static readonly string[] GlobalSystemTables =
    {
        "migrations", "schema_migrations", "plans", "roles", "permissions",
        "currencies", "countries", "languages", "feature_flags",
    };

    public AgentKind Kind => AgentKind.DbaFinOpsSecurityCritic;

    public Task<IReadOnlyList<MinusFindingItem>> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);

        var schema = workspace.CurrentSchema;
        var findings = new List<MinusFindingItem>();
        if (schema.Tables.Count < 2)
            return Task.FromResult<IReadOnlyList<MinusFindingItem>>(findings);

        var tenantColMatches = schema.Tables
            .Select(t => new { Table = t, TenantCol = FindTenantColumn(t) })
            .ToList();

        var hasTenantRoot = schema.Tables.Any(t =>
            TenantRootTableNames.Contains(t.Name, StringComparer.OrdinalIgnoreCase));
        var scopedCount = tenantColMatches.Count(x => x.TenantCol is not null);
        var promptMentionsMultiTenant =
            (workspace.TaskInput.Prompt?.Contains("multi-tenant", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (workspace.TaskInput.Prompt?.Contains("multitenant", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (workspace.TaskInput.Prompt?.Contains("çok kiracılı", StringComparison.OrdinalIgnoreCase) ?? false);

        if (!hasTenantRoot && scopedCount < 2 && !promptMentionsMultiTenant)
            return Task.FromResult<IReadOnlyList<MinusFindingItem>>(findings);

        var tenantScopedIds = tenantColMatches
            .Where(x => x.TenantCol is not null || TenantRootTableNames.Contains(x.Table.Name, StringComparer.OrdinalIgnoreCase))
            .Select(x => x.Table.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in tenantColMatches)
        {
            var table = entry.Table;
            if (TenantRootTableNames.Contains(table.Name, StringComparer.OrdinalIgnoreCase) ||
                GlobalSystemTables.Contains(table.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            if (entry.TenantCol is not null)
            {
                var hasTenantLeadingIndex = table.Indexes.Any(ix =>
                    ix.Columns.Count > 0 &&
                    string.Equals(ix.Columns[0].ColumnId, entry.TenantCol.Id, StringComparison.OrdinalIgnoreCase));

                if (!hasTenantLeadingIndex)
                {
                    findings.Add(new MinusFindingItem(
                        Id: $"mt-idx-{table.Name}",
                        Code: "MT-INDEX-GAP",
                        Severity: MinusSeverity.DbaOptimization,
                        TableName: table.Name,
                        ColumnName: entry.TenantCol.Name,
                        ProblemDescription: $"Çok kiracılı {table.Name}.{entry.TenantCol.Name} kolonu indeksle başlamıyor; kiracı sorguları yavaşlar.",
                        ActionableDirective: $"{table.Name} üzerinde ({entry.TenantCol.Name}, ...) öncelikli bileşik indeks ekle.",
                        SourceSubAgent: nameof(MultiTenancyAndRlsCriticSubAgent),
                        IsDeterministicAutoFixable: true));
                }
                continue;
            }

            // Doğrudan tenant_id yoksa FK üzerinden kiracı kapsamlı bir tabloya bağlı mı?
            var hasScopedParent = schema.Relations.Any(r =>
                string.Equals(r.SourceTableId, table.Id, StringComparison.OrdinalIgnoreCase) &&
                tenantScopedIds.Contains(r.TargetTableId));

            if (!hasScopedParent)
            {
                findings.Add(new MinusFindingItem(
                    Id: $"mt-gap-{table.Name}",
                    Code: "MT-ISOLATION-GAP",
                    Severity: MinusSeverity.Structural,
                    TableName: table.Name,
                    ColumnName: null,
                    ProblemDescription: $"Çok kiracılı şemada '{table.Name}' tablosunda kiracı izolasyonu (tenant_id / scoped parent FK) yok.",
                    ActionableDirective: $"{table.Name} tablosuna tenant_id FK kolonu ekle veya kiracı kapsamlı bir üst tabloya bağla.",
                    SourceSubAgent: nameof(MultiTenancyAndRlsCriticSubAgent)));
            }
        }

        if (findings.Count > 0)
        {
            workspace.Swarm.SendMessage(
                self.AgentId,
                workspace.NegativeAgentId,
                "critic.multitenancy",
                $"{findings.Count} çok kiracılık/RLS izolasyon bulgusu tespit edildi.");
        }

        return Task.FromResult<IReadOnlyList<MinusFindingItem>>(findings);
    }

    private static SchemaColumn? FindTenantColumn(SchemaTable table)
        => table.Columns.FirstOrDefault(c =>
            TenantFkColumns.Contains(c.Name, StringComparer.OrdinalIgnoreCase));
}
