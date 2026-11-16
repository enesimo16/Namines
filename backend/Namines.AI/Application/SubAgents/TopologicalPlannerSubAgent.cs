using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Models;

namespace Namines.AI.Application;

/// <summary>
/// Tek tablodan 100+ tabloya kadar ölçeklenen katmanlı (Tier 0..4) topolojik üretim veya
/// mevcut Canvas evrim planını çıkaran alt-ajan.
/// </summary>
public sealed class TopologicalPlannerSubAgent : ISubAgent<OrchestrationWorkspace, TopologicalArchitecturePlan>
{
    private static readonly Regex ExplicitTableCountRegex = new(
        @"\b(?<count>\d{1,3})\s*(?:\+\s*)?(?:tablo|tablolu|tablolar|tables?|entities?|entity)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly ILlmCompletionEngine _llm;

    public TopologicalPlannerSubAgent(ILlmCompletionEngine llm)
    {
        _llm = llm;
    }

    public AgentKind Kind => AgentKind.TopologicalPlanner;

    public async Task<TopologicalArchitecturePlan> ExecuteAsync(
        AgentInstance self,
        OrchestrationWorkspace workspace,
        CancellationToken ct = default)
    {
        var input = workspace.TaskInput;
        var archetype = ArchetypeDetector.Detect(input.Prompt);
        var schemaName = !string.IsNullOrWhiteSpace(workspace.CurrentSchema.Name)
            ? workspace.CurrentSchema.Name
            : archetype.ToString().ToLowerInvariant();

        // Canvas içi mevcut şema revizyonu: tüm evreni baştan planlamak yerine yalnızca odaklı evrim dalgası kur.
        if (input.SourceKind == SchemaInputSourceKind.ExistingCanvasSchema &&
            workspace.CurrentSchema.Tables.Count > 0)
        {
            var focusTargets = input.SelectedCanvasTables is { Count: > 0 }
                ? input.SelectedCanvasTables.ToList()
                : workspace.CurrentSchema.Tables.Take(15).Select(t => t.Name).ToList();

            var evolutionWave = new TopologicalWave(
                WaveIndex: 0,
                Label: "Canvas Evolution & Surgical Expansion",
                Domains: new[] { "canvas_revision" },
                TargetTables: focusTargets,
                Rationale: "Mevcut Canvas şemasının L0+L1 bağlamıyla UUID koruyarak evrimi.");

            var evoPlan = new TopologicalArchitecturePlan(
                schemaName,
                archetype,
                new[] { evolutionWave },
                Math.Max(workspace.CurrentSchema.Tables.Count, focusTargets.Count));

            workspace.Plan = evoPlan;
            workspace.Swarm.SendMessage(self.AgentId, workspace.MasterAgentId, "plan.ready",
                $"Canvas evrim planı hazır: odak={focusTargets.Count} tablo, toplam mevcut={workspace.CurrentSchema.Tables.Count}.");
            return evoPlan;
        }

        // Greenfield veya çoklu-kaynak genişletme:
        var answers = input.ClarifyingAnswers ?? new Dictionary<string, string>();
        var basePlan = PlanBuilder.Build(archetype, answers, 1);
        // Dış kaynaklardan (Kod/JSON/GitHub) halihazırda çıkarılmış gerçek tablolar.
        var extractedNames = workspace.CurrentSchema.Tables.Select(t => t.Name).ToList();
        var seedTables = basePlan.Tables.Select(t => t.Name).ToList();

        foreach (var ext in extractedNames)
        {
            if (!seedTables.Contains(ext, StringComparer.OrdinalIgnoreCase))
                seedTables.Add(ext);
        }

        var requestedCount = DetectTargetTableCount(input.Prompt, seedTables.Count);

        List<TopologicalWave>? waves = null;
        if (_llm.IsConfiguredFor(input.ProviderHint))
        {
            waves = await TryPlanWithLlmAsync(input, archetype, schemaName, seedTables, extractedNames, requestedCount, ct)
                .ConfigureAwait(false);
        }

        if (waves is null || waves.Count == 0)
        {
            waves = BuildDeterministicWaves(archetype, seedTables, requestedCount);
        }

        var totalPlanned = waves.SelectMany(w => w.TargetTables)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var plan = new TopologicalArchitecturePlan(
            schemaName,
            archetype,
            waves,
            Math.Max(totalPlanned, workspace.CurrentSchema.Tables.Count));

        workspace.Plan = plan;
        workspace.Swarm.SendMessage(
            self.AgentId,
            workspace.MasterAgentId,
            "plan.ready",
            $"Topolojik plan hazır: arketip={archetype}, {waves.Count} dalga, hedef={plan.TotalPlannedTables} tablo.");

        return plan;
    }

    internal static int DetectTargetTableCount(string prompt, int floorCount)
    {
        var match = ExplicitTableCountRegex.Match(prompt ?? string.Empty);
        if (match.Success &&
            int.TryParse(match.Groups["count"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
            parsed > 0)
        {
            return Math.Clamp(parsed, 1, 160);
        }

        var lower = (prompt ?? string.Empty).ToLowerInvariant();
        if (lower.Contains("enterprise", StringComparison.Ordinal) ||
            lower.Contains("kurumsal", StringComparison.Ordinal) ||
            lower.Contains("erp", StringComparison.Ordinal) ||
            lower.Contains("kapsamlı", StringComparison.Ordinal))
        {
            return Math.Max(floorCount, 24);
        }

        return Math.Max(floorCount, 6);
    }

    private async Task<List<TopologicalWave>?> TryPlanWithLlmAsync(
        OrchestrationTaskInput input,
        ProjectArchetype archetype,
        string schemaName,
        IReadOnlyList<string> seedTables,
        IReadOnlyList<string> extractedNames,
        int targetCount,
        CancellationToken ct)
    {
        var system =
            "You are Namines Topological Database Planner.\n" +
            "Design ONLY the database table architecture grouped into strict dependency waves (Tier 0 Foundations/Auth/Lookups -> Tier 1 Master Entities -> Tier 2 Core Transactions -> Tier 3 Details/Bridges/Ledgers -> Tier 4 Audit/Logs/Analytics).\n" +
            "Keep 8-18 tables per wave so every table is cohesive and properly normalized.\n" +
            "Return STRICT JSON ONLY:\n" +
            "{\"waves\":[{\"label\":\"Wave 0 Foundations\",\"domains\":[\"identity\",\"tenant\"],\"tables\":[\"users\",\"roles\"],\"rationale\":\"...\"}]}";

        var user =
            $"Engine: {input.Engine}\n" +
            $"Archetype: {archetype}\n" +
            $"Target scale: ~{targetCount} tables\n" +
            $"Seed/Existing tables to include: {string.Join(", ", seedTables)}\n" +
            $"Prompt: {input.Prompt}\n" +
            (string.IsNullOrWhiteSpace(ArchetypeRoles.For(archetype)) ? string.Empty : $"Domain guidance: {ArchetypeRoles.For(archetype)}\n") +
            $"Keep JSON compact and strictly ordered by FK dependencies (parents before children). Schema: {schemaName}.";

        try
        {
            var resp = await _llm.CompleteAsync(
                new LlmRequest(
                    SystemPrompt: system,
                    UserPrompt: user,
                    Temperature: 0.1,
                    ModelHint: input.ModelHint,
                    ProviderHint: input.ProviderHint,
                    JsonResponse: true,
                    FeatureKey: "schema_plan"),
                ct).ConfigureAwait(false);

            var clean = JsonSanitizerPreprocessor.Sanitize(ExtractJson(resp.Content));
            using var doc = JsonDocument.Parse(clean);
            if (!doc.RootElement.TryGetProperty("waves", out var wavesEl) || wavesEl.ValueKind != JsonValueKind.Array)
                return null;

            var waves = new List<TopologicalWave>();
            var seenTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var idx = 0;

            foreach (var w in wavesEl.EnumerateArray())
            {
                var label = w.TryGetProperty("label", out var l) ? l.GetString() ?? $"Wave {idx}" : $"Wave {idx}";
                var rationale = w.TryGetProperty("rationale", out var r) ? r.GetString() ?? "Topological wave" : "Topological wave";

                var domains = new List<string>();
                if (w.TryGetProperty("domains", out var dEl) && dEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in dEl.EnumerateArray())
                    {
                        var val = d.GetString();
                        if (!string.IsNullOrWhiteSpace(val)) domains.Add(val.Trim());
                    }
                }
                if (domains.Count == 0) domains.Add($"tier_{idx}");

                var tables = new List<string>();
                if (w.TryGetProperty("tables", out var tEl) && tEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var t in tEl.EnumerateArray())
                    {
                        var val = t.GetString()?.Trim();
                        if (!string.IsNullOrWhiteSpace(val) && seenTables.Add(val))
                            tables.Add(val);
                    }
                }

                if (tables.Count > 0)
                {
                    waves.Add(new TopologicalWave(idx, label, domains, tables, rationale));
                    idx++;
                }
            }

            if (waves.Count == 0)
                return null;

            // Kod/JSON'dan çıkarılan tablolar her zaman korunur; genel arketip tohumları
            // yalnızca LLM hedeflenen sayının altında kaldıysa tamamlanır.
            var mustKeep = extractedNames.Where(e => !seenTables.Contains(e)).ToList();
            var extraSeeds = seenTables.Count + mustKeep.Count >= targetCount
                ? Enumerable.Empty<string>()
                : seedTables.Where(s => !seenTables.Contains(s) && !mustKeep.Contains(s, StringComparer.OrdinalIgnoreCase));
            var missing = mustKeep.Concat(extraSeeds).ToList();
            if (missing.Count > 0)
            {
                var mergedFirst = waves[0].TargetTables.Concat(missing).ToList();
                waves[0] = waves[0] with { TargetTables = mergedFirst };
            }

            return waves;
        }
        catch
        {
            return null;
        }
    }

    internal static List<TopologicalWave> BuildDeterministicWaves(
        ProjectArchetype archetype,
        IReadOnlyList<string> seedTables,
        int targetCount)
    {
        var all = new List<string>(seedTables);
        var set = new HashSet<string>(all, StringComparer.OrdinalIgnoreCase);

        // Standart çekirdek kurumsal tablolarla gerektiğinde hedef tablo sayısına tamamla.
        var expansionCatalog = new[]
        {
            "users", "roles", "user_roles", "organizations", "organization_members",
            "permissions", "api_keys", "sessions", "audit_logs", "notifications",
            "notification_preferences", "attachments", "tags", "entity_tags", "comments",
            "categories", "products", "product_variants", "inventory_items", "warehouses",
            "orders", "order_items", "invoices", "invoice_lines", "payments",
            "refunds", "shipments", "shipment_events", "addresses", "coupons",
            "reviews", "wishlists", "carts", "cart_items", "webhooks",
            "webhook_deliveries", "feature_flags", "settings", "tax_rates", "currencies",
            "exchange_rates", "subscriptions", "plans", "usage_records", "support_tickets",
            "ticket_messages", "workflows", "workflow_runs", "metrics_daily", "report_exports",
            "ledger_accounts", "ledger_entries", "payouts", "disputes", "fraud_checks",
            "locations", "vehicles", "drivers", "routes", "telemetry_events",
        };

        foreach (var candidate in expansionCatalog)
        {
            if (all.Count >= targetCount) break;
            if (set.Add(candidate)) all.Add(candidate);
        }

        var extraSeq = 1;
        while (all.Count < targetCount)
        {
            var generated = $"{archetype.ToString().ToLowerInvariant()}_domain_entity_{extraSeq++}";
            if (set.Add(generated)) all.Add(generated);
        }

        const int maxPerWave = 16;
        var waves = new List<TopologicalWave>();
        for (var i = 0; i < all.Count; i += maxPerWave)
        {
            var slice = all.Skip(i).Take(maxPerWave).ToList();
            var waveIdx = waves.Count;
            waves.Add(new TopologicalWave(
                WaveIndex: waveIdx,
                Label: waveIdx switch
                {
                    0 => "Wave 0 Foundations & Core Identity",
                    1 => "Wave 1 Master Domain Entities",
                    2 => "Wave 2 Operational Transactions",
                    3 => "Wave 3 Line Items, Bridges & Ledgers",
                    _ => $"Wave {waveIdx} Governance & Analytics",
                },
                Domains: new[] { $"tier_{waveIdx}" },
                TargetTables: slice,
                Rationale: $"Bağımlılık sırasına göre katman {waveIdx} ({slice.Count} tablo)."));
        }

        return waves;
    }

    private static string ExtractJson(string raw)
    {
        var start = raw.IndexOf('{');
        var end = raw.LastIndexOf('}');
        return start >= 0 && end > start ? raw[start..(end + 1)] : raw;
    }
}
