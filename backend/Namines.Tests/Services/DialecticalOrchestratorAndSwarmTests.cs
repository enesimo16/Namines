using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Namines.AI.Application;
using Namines.AI.Domain;
using Namines.Core.Analysis;
using Namines.Core.Enums;
using Namines.Core.Interfaces;
using Namines.Core.Models;
using Namines.Infrastructure.Generators.DdlGenerator;
using Xunit;

namespace Namines.Tests.Services;

public sealed class DialecticalOrchestratorAndSwarmTests
{
    private sealed class ScriptedLlmEngine : ILlmCompletionEngine
    {
        private readonly Func<LlmRequest, int, string> _responder;
        private int _calls;

        public ScriptedLlmEngine(Func<LlmRequest, int, string> responder) => _responder = responder;
        public List<LlmRequest> RecordedRequests { get; } = new();

        public bool IsConfiguredFor(string? providerHint = null) => true;

        public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        {
            RecordedRequests.Add(request);
            var callIndex = ++_calls;
            var text = _responder(request, callIndex);
            return Task.FromResult(new LlmResponse(
                Content: text,
                ModelUsed: "scripted-nai-pro",
                PromptTokens: Math.Max(10, request.UserPrompt.Length / 4),
                CompletionTokens: Math.Max(10, text.Length / 4),
                IsTruncated: false));
        }
    }

    private static (MasterOrchestrator Master, ScriptedLlmEngine Llm) BuildHarness(
        Func<LlmRequest, int, string> responder,
        IApiSpecExtractor? apiExtractor = null,
        IRepositoryScanner? repoScanner = null,
        IAutonomousSwarmTelemetry? telemetry = null)
    {
        var llm = new ScriptedLlmEngine(responder);
        var context = new SchemaContextEngine();
        var tools = new WorkspaceToolRegistry(
            context,
            new DdlGeneratorFactory(),
            apiExtractor,
            repoScanner);

        var cartographer = new CartographerSubAgent(context, tools);
        var planner = new TopologicalPlannerSubAgent(llm);
        var architect = new NslDomainArchitectSubAgent(llm);
        var canvas = new CanvasEvolutionSubAgent(llm, context);
        var relationWeaver = new RelationWeaverSubAgent();
        var temporalWeaver = new AuditAndTemporalWeaverSubAgent();
        var programmability = new ProgrammabilityArchitectSubAgent(llm, context);
        var repair = new DeterministicRepairSubAgent();
        var surgical = new SurgicalMutationSubAgent(llm, context);
        var semPortCritic = new SemanticAndPortabilityCriticSubAgent();
        var normCritic = new NormalizationCriticSubAgent();
        var mtCritic = new MultiTenancyAndRlsCriticSubAgent();
        var idxCritic = new IndexAndConstraintCriticSubAgent();
        var progCritic = new TriggerAndProcedureCriticSubAgent();
        var dba = new AdversarialDbaSubAgent(llm);
        var lifecycle = new LifecycleTaskSubAgent(llm);

        var positive = new PositiveOrchestrator(
            context, architect, canvas, repair, relationWeaver, temporalWeaver, programmability);
        var negative = new NegativeOrchestrator(
            tools, dba, semPortCritic, normCritic, mtCritic, idxCritic, progCritic);
        var master = new MasterOrchestrator(
            context, cartographer, planner, positive, negative, repair, surgical, lifecycle, telemetry);

        return (master, llm);
    }

    [Fact]
    public void AgentSwarmRuntime_dynamically_spawns_messages_retires_and_terminates_agents()
    {
        var swarm = new AgentSwarmRuntime();

        var a1 = swarm.SpawnAgent(AgentKind.DomainArchitect, "wave-0-arch", "Temel tablolar");
        var a2 = swarm.SpawnAgent(AgentKind.DeterministicCritic, "critic-0", "DDL kontrol");

        Assert.Equal(2, swarm.GetActiveAgents().Count);

        swarm.SendMessage(a1.AgentId, "master", "wave.ready", "3 tablo hazır");
        swarm.SendMessage("master", "broadcast", "sync", "kilitlendi");

        Assert.Single(swarm.GetMessagesFor("master"), m => m.Topic == "wave.ready");
        Assert.Single(swarm.GetMessagesFor(a1.AgentId), m => m.Topic == "sync");

        swarm.RetireAgent(a1.AgentId);
        Assert.Single(swarm.GetActiveAgents());

        swarm.TerminateAgent(a2.AgentId);
        Assert.Empty(swarm.GetActiveAgents());

        var stats = swarm.GetSwarmStats();
        Assert.Equal((2, 1, 0), stats);
    }

    [Fact]
    public async Task Dialectical_master_runs_plus_and_minus_and_repairs_broken_nsl_deterministically_at_zero_extra_tokens()
    {
        var callCount = 0;
        var (master, llm) = BuildHarness((req, _) =>
        {
            callCount++;
            if (req.FeatureKey == "schema_plan")
            {
                return """
                {"waves":[{"label":"Core","domains":["commerce"],"tables":["users","orders"],"rationale":"parents first"}]}
                """;
            }

            if (req.FeatureKey == "schema_generate")
            {
                // Bilerek kusurlu: float total_price, eksik FK indexi, MSSQL cascade yolu ve düz password kolonu
                return """
                nsl 1.0

                table users {
                  id uuid pk not null
                  email varchar not null unique
                  password varchar not null
                }

                table orders {
                  id uuid pk not null
                  user_id uuid not null
                  total_price float not null
                  fk (user_id) -> users(id) on delete cascade on update no action
                }
                """;
            }

            return "[]";
        });

        var steps = new List<AgentStep>();
        var progress = new Progress<AgentStep>(s => steps.Add(s));

        var verdict = await master.OrchestrateAsync(
            new OrchestrationTaskInput(
                Prompt: "2 tablolu users ve orders ecommerce şeması",
                Engine: DatabaseType.MSSQL,
                DeepDbaAudit: false),
            progress);

        Assert.True(verdict.Converged);
        Assert.True(verdict.QualityScore >= 0.90);
        Assert.True(verdict.RemainingMinus.IsCleanOfBlockers);
        Assert.True(verdict.TotalSubAgentsSpawned >= 6);
        Assert.Equal(verdict.TotalSubAgentsSpawned, verdict.TotalSubAgentsRetired);

        var users = verdict.FinalSchema.Tables.Single(t => t.Name == "users");
        var orders = verdict.FinalSchema.Tables.Single(t => t.Name == "orders");

        // Parola sertleştirmesi + boyutsuz varchar + finansal float -> DECIMAL(19,4) + FK index kontrolü
        Assert.Contains(users.Columns, c => c.Name == "password_hash" && c.Length >= 255);
        Assert.Contains(users.Columns, c => c.Name == "email" && c.Length > 0);
        Assert.Contains(orders.Columns, c => c.Name == "total_price" && c.Type == "DECIMAL" && c.Length == 19 && c.Scale == 4);
        Assert.NotEmpty(orders.Indexes);

        // Cerrahi LLM çağrısı tetiklenmemeli (deterministik onarımcı tek başına 0-token ile çözdü).
        Assert.DoesNotContain(llm.RecordedRequests, r => r.FeatureKey == "schema_repair");
    }

    [Fact]
    public async Task Wave_generation_scales_to_60_plus_tables_with_cross_wave_locked_fk_contracts()
    {
        var tableSequence = Enumerable.Range(1, 64).Select(i => $"entity_{i:00}").ToList();

        var (master, llm) = BuildHarness((req, _) =>
        {
            if (req.FeatureKey == "schema_plan")
            {
                var waveObjs = new List<string>();
                for (var w = 0; w < 4; w++)
                {
                    var slice = tableSequence.Skip(w * 16).Take(16).Select(t => $"\"{t}\"");
                    waveObjs.Add($"{{\"label\":\"Wave {w}\",\"domains\":[\"d{w}\"],\"tables\":[{string.Join(",", slice)}],\"rationale\":\"tier {w}\"}}");
                }
                return $"{{\"waves\":[{string.Join(",", waveObjs)}]}}";
            }

            if (req.FeatureKey == "schema_generate")
            {
                // Prompt içindeki "Required tables in this wave (...): ..." listesini çıkar.
                var m = Regex.Match(req.UserPrompt, @"Required tables in this wave \(\d+\):\s*(?<list>[^\r\n]+)");
                var names = m.Success
                    ? m.Groups["list"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : Array.Empty<string>();

                var sb = new StringBuilder();
                sb.AppendLine("nsl 1.0");
                sb.AppendLine();
                foreach (var name in names)
                {
                    sb.AppendLine($"table {name} {{");
                    sb.AppendLine("  id uuid pk not null");
                    sb.AppendLine("  code varchar(64) not null unique");

                    // Dalga 1 ve sonrasında Dalga 0'daki ilk kilitli tabloya FK kur (dalga arası referans kanıtı).
                    if (!string.Equals(name, "entity_01", StringComparison.OrdinalIgnoreCase) &&
                        req.UserPrompt.Contains("entity_01(id: UUID)", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("  entity_01_id uuid not null");
                        sb.AppendLine("  fk (entity_01_id) -> entity_01(id) on delete no action on update no action");
                        sb.AppendLine("  index (entity_01_id)");
                    }

                    sb.AppendLine("}");
                    sb.AppendLine();
                }

                return sb.ToString();
            }

            return "[]";
        });

        var verdict = await master.OrchestrateAsync(new OrchestrationTaskInput(
            Prompt: "64 tablolu kurumsal multi-tenant ERP veritabanı tasarla",
            Engine: DatabaseType.PostgreSQL,
            MaxAutonomyWaves: 6,
            DeepDbaAudit: false));

        Assert.Equal(64, verdict.FinalSchema.Tables.Count);
        Assert.True(verdict.FinalSchema.Relations.Count >= 48);
        Assert.Equal(64, verdict.CumulativePlus.LockedContracts.Count);
        Assert.True(verdict.Converged);
        Assert.Contains(llm.RecordedRequests, r =>
            r.FeatureKey == "schema_generate" &&
            r.UserPrompt.Contains("LOCKED UPSTREAM CONTRACTS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Brownfield_50_table_canvas_evolution_preserves_stable_uuids_and_uses_compact_l0_l1_context()
    {
        // 50 tablolu mevcut veritabanı oluştur.
        var existing = new DatabaseSchema { Name = "big_enterprise" };
        for (var i = 1; i <= 50; i++)
        {
            var tid = $"t_mod_{i}";
            existing.Tables.Add(new SchemaTable
            {
                Id = tid,
                Name = $"mod_{i}",
                StableUuid = $"stable-uuid-{i:000}",
                Columns = new List<SchemaColumn>
                {
                    new() { Id = $"{tid}_id", Name = "id", Type = "UUID", IsPK = true, IsNullable = false },
                    new() { Id = $"{tid}_title", Name = "title", Type = "VARCHAR", Length = 120, IsNullable = false },
                },
            });
        }

        var targetTable = existing.Tables[12]; // mod_13 seçiliyor

        var (master, llm) = BuildHarness((req, _) =>
        {
            if (req.FeatureKey == "schema_revise")
            {
                // Yalnızca mod_13 güncellenip yeni mod_audit ekleniyor (geri kalan 49 tablo dokunulmamış olmalı).
                return """
                nsl 1.0

                table mod_13 {
                  @uuid("stable-uuid-013")
                  id uuid pk not null
                  title varchar(120) not null
                  priority int not null default 1
                }

                table mod_audit {
                  id uuid pk not null
                  mod_13_id uuid not null
                  note varchar(255) not null
                  fk (mod_13_id) -> mod_13(id) on delete restrict on update no action
                  index (mod_13_id)
                }
                """;
            }

            return "[]";
        });

        var verdict = await master.OrchestrateAsync(new OrchestrationTaskInput(
            Prompt: "mod_13 tablosuna priority ekle ve mod_audit tablosunu bağla",
            Engine: DatabaseType.PostgreSQL,
            SourceKind: SchemaInputSourceKind.ExistingCanvasSchema,
            ExistingSchema: existing,
            SelectedCanvasTables: new[] { targetTable.Name },
            DeepDbaAudit: false));

        Assert.Equal(51, verdict.FinalSchema.Tables.Count);

        // Düzenlenen mod_13'ün ve düzenlenmeyen 49 tablonun StableUuid'leri korunmuş olmalı!
        var updated13 = verdict.FinalSchema.Tables.Single(t => t.Name == "mod_13");
        Assert.Equal("stable-uuid-013", updated13.StableUuid);
        Assert.Contains(updated13.Columns, c => c.Name == "priority");
        Assert.Equal("stable-uuid-001", verdict.FinalSchema.Tables.Single(t => t.Name == "mod_1").StableUuid);
        Assert.Equal("stable-uuid-050", verdict.FinalSchema.Tables.Single(t => t.Name == "mod_50").StableUuid);

        // Prompt 50 tablonun tamamının JSON'unu değil kompakt L0+L1 alt grafiğini göndermiş olmalı.
        var reviseCall = Assert.Single(llm.RecordedRequests, r => r.FeatureKey == "schema_revise");
        Assert.Contains("L0 FULL SCHEMA TOPOLOGY", reviseCall.UserPrompt, StringComparison.Ordinal);
        Assert.Contains("L1 FOCUS NSL SUBGRAPH", reviseCall.UserPrompt, StringComparison.Ordinal);
        Assert.True(reviseCall.UserPrompt.Length < 10_000, "L0+L1 bağlamı kompakt kalmalı.");
    }

    [Fact]
    public async Task Multisource_code_and_json_shapes_ingest_at_zero_llm_cost_before_planning()
    {
        var prismaFiles = new Dictionary<string, string>
        {
            ["schema.prisma"] = """
                generator client { provider = "prisma-client-js" }
                model Account {
                  id Int @id @default(autoincrement())
                  email String @unique
                }
                """,
        };

        var jsonSamples = new[]
        {
            new ObservedResponse("/api/webhooks", """{"id":"550e8400-e29b-41d4-a716-446655440000","url":"https://a.test","secret":"abc"}"""),
            new ObservedResponse("/api/webhooks", """{"id":"550e8400-e29b-41d4-a716-446655440001","url":"https://b.test","secret":"xyz","retry_limit":5}"""),
        };

        var (master, _) = BuildHarness((req, _) =>
        {
            if (req.FeatureKey == "schema_plan")
            {
                return """{"waves":[{"label":"Integration","domains":["webhooks"],"tables":["Account","webhooks"],"rationale":"all extracted"}]}""";
            }

            // LLM hiç yeni tablo döndürmese bile Cartographer araçları Account ve webhooks'u zaten çıkarmış olmalı.
            return "nsl 1.0\n";
        });

        var verdict = await master.OrchestrateAsync(new OrchestrationTaskInput(
            Prompt: "Mevcut Prisma + gözlemlenen webhook JSON yanıtlarını birleştir",
            Engine: DatabaseType.PostgreSQL,
            SourceKind: SchemaInputSourceKind.CodeFiles,
            CodeFiles: prismaFiles,
            ObservedJsonResponses: jsonSamples,
            DeepDbaAudit: false));

        Assert.Contains(verdict.FinalSchema.Tables, t => t.Name == "Account");
        var webhooks = Assert.Single(verdict.FinalSchema.Tables, t => t.Name == "webhooks");
        // Sadece 2. örnekte görünen "retry_limit" alanı belirsiz -> nullable işaretlenmiş olmalı.
        var retryCol = Assert.Single(webhooks.Columns, c => c.Name == "retry_limit");
        Assert.True(retryCol.IsNullable);
        Assert.True(verdict.Converged);
    }

    [Fact]
    public async Task AutonomousSwarmTelemetry_records_verdicts_and_exposes_architecture_for_admin_panel()
    {
        var telemetry = new AutonomousSwarmTelemetry();
        var (master, _) = BuildHarness((req, _) =>
        {
            if (req.FeatureKey == "schema_plan")
                return """{"waves":[{"label":"Core","domains":["auth"],"tables":["users"],"rationale":"single root"}]}""";
            if (req.FeatureKey == "schema_generate")
                return """
                nsl 1.0
                table users {
                  id uuid pk not null
                  email varchar(255) not null unique
                }
                """;
            return "[]";
        }, telemetry: telemetry);

        await master.OrchestrateAsync(new OrchestrationTaskInput(
            Prompt: "1 tablolu users kimlik şeması",
            Engine: DatabaseType.PostgreSQL,
            DeepDbaAudit: false));

        var snapshot = telemetry.GetSnapshot();
        Assert.Equal("Namines.AI v2 (Dialectical Swarm)", snapshot.EngineVersion);
        Assert.Equal(65_536, snapshot.MaxOutputTokensCeiling);
        Assert.Equal(1, snapshot.TotalRuns);
        Assert.Equal(1, snapshot.ConvergedRuns);
        Assert.Equal(16, snapshot.SubAgents.Count);
        Assert.Equal(6, snapshot.WorkspaceTools.Count);
        Assert.Equal(5, snapshot.Quotas.Count);
        var run = Assert.Single(snapshot.RecentRuns);
        Assert.Equal(1, run.TablesBuilt);
        Assert.True(run.SpawnedSubAgents >= 4);
    }

    [Fact]
    public async Task RelationWeaver_and_AuditWeaver_wire_orphan_fk_and_soft_delete_partial_index_at_zero_tokens()
    {
        var schema = new DatabaseSchema
        {
            Name = "commerce",
            Tables = new List<SchemaTable>
            {
                new()
                {
                    Id = "t_customers",
                    Name = "customers",
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = "c_id", Name = "id", Type = "BIGINT", IsPK = true, IsNullable = false },
                    },
                },
                new()
                {
                    Id = "t_orders",
                    Name = "orders",
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = "o_id", Name = "id", Type = "BIGINT", IsPK = true, IsNullable = false },
                        new() { Id = "o_cust", Name = "customer_id", Type = "VARCHAR", IsPK = false, IsNullable = false },
                        new() { Id = "o_created", Name = "created_at", Type = "TIMESTAMP", IsNullable = false },
                        new() { Id = "o_deleted", Name = "deleted_at", Type = "TIMESTAMP", IsNullable = false },
                    },
                },
            },
        };

        var swarm = new AgentSwarmRuntime();
        var workspace = new OrchestrationWorkspace(
            new OrchestrationTaskInput("Weave relations and audit timestamps", DatabaseType.PostgreSQL),
            swarm,
            schema);

        var relAgent = swarm.SpawnAgent(AgentKind.RelationWeaver, "rel-test", "test");
        var woven = await new RelationWeaverSubAgent().ExecuteAsync(relAgent, workspace);

        var temporalAgent = swarm.SpawnAgent(AgentKind.SchemaMutator, "audit-test", "test");
        var temporal = await new AuditAndTemporalWeaverSubAgent().ExecuteAsync(temporalAgent, workspace);

        Assert.Single(woven);
        var rel = Assert.Single(schema.Relations);
        Assert.Equal("t_orders", rel.SourceTableId);
        Assert.Equal("t_customers", rel.TargetTableId);

        var orders = schema.Tables.Single(t => t.Name == "orders");
        var custCol = orders.Columns.Single(c => c.Name == "customer_id");
        Assert.True(custCol.IsFK);
        Assert.Equal("BIGINT", custCol.Type); // Hedef PK BIGINT ile hizalanmalı

        Assert.Equal("CURRENT_TIMESTAMP", orders.Columns.Single(c => c.Name == "created_at").DefaultValue);
        Assert.True(orders.Columns.Single(c => c.Name == "deleted_at").IsNullable);
        Assert.Contains(orders.Indexes, ix => ix.Where == "deleted_at IS NULL");
        Assert.NotEmpty(temporal);
    }

    [Fact]
    public async Task New_critics_detect_normalization_multitenancy_junction_and_repair_fixes_composite_indexes()
    {
        var schema = new DatabaseSchema
        {
            Name = "saas",
            Tables = new List<SchemaTable>
            {
                new()
                {
                    Id = "t_tenants",
                    Name = "tenants",
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = "ten_id", Name = "id", Type = "UUID", IsPK = true },
                    },
                },
                new()
                {
                    Id = "t_users",
                    Name = "users",
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = "u_id", Name = "id", Type = "UUID", IsPK = true },
                        new() { Id = "u_ten", Name = "tenant_id", Type = "UUID", IsFK = true },
                        new() { Id = "u_p1", Name = "phone1", Type = "VARCHAR", Length = 32 },
                        new() { Id = "u_p2", Name = "phone2", Type = "VARCHAR", Length = 32 },
                        new() { Id = "u_tags", Name = "tag_ids", Type = "TEXT" },
                    },
                },
                new()
                {
                    Id = "t_audit_orphan",
                    Name = "invoices",
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = "i_id", Name = "id", Type = "UUID", IsPK = true },
                        new() { Id = "i_st", Name = "start_date", Type = "DATE" },
                        new() { Id = "i_en", Name = "end_date", Type = "DATE" },
                    },
                },
                new()
                {
                    Id = "t_user_roles",
                    Name = "user_roles",
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = "ur_id", Name = "id", Type = "UUID", IsPK = true },
                        new() { Id = "ur_u", Name = "user_id", Type = "UUID", IsFK = true },
                        new() { Id = "ur_r", Name = "role_id", Type = "UUID", IsFK = true },
                    },
                },
            },
        };

        var swarm = new AgentSwarmRuntime();
        var workspace = new OrchestrationWorkspace(
            new OrchestrationTaskInput("Multi-tenant B2B billing", DatabaseType.PostgreSQL),
            swarm,
            schema);

        var a = swarm.SpawnAgent(AgentKind.SemanticCoverageCritic, "c1", "test");
        var normFindings = await new NormalizationCriticSubAgent().ExecuteAsync(a, workspace);
        var mtFindings = await new MultiTenancyAndRlsCriticSubAgent().ExecuteAsync(a, workspace);
        var idxFindings = await new IndexAndConstraintCriticSubAgent().ExecuteAsync(a, workspace);

        Assert.Contains(normFindings, f => f.Code == "NORM-1NF-REPEAT");
        Assert.Contains(normFindings, f => f.Code == "NORM-1NF-CSV");
        Assert.Contains(mtFindings, f => f.Code == "MT-ISOLATION-GAP" && f.TableName == "invoices");
        Assert.Contains(mtFindings, f => f.Code == "MT-INDEX-GAP" && f.TableName == "users");
        Assert.Contains(idxFindings, f => f.Code == "IDX-JUNCTION-UNIQUE" && f.TableName == "user_roles");
        Assert.Contains(idxFindings, f => f.Code == "CHK-TEMPORAL-ORDER" && f.TableName == "invoices");

        // Deterministik onarımcı çalıştığında köprü UNIQUE + CHECK + kiracı indeksi 0-token ile kapanmalı.
        await new DeterministicRepairSubAgent().ExecuteAsync(a, workspace);
        var remainingIdx = await new IndexAndConstraintCriticSubAgent().ExecuteAsync(a, workspace);
        Assert.Empty(remainingIdx);
    }

    [Fact]
    public async Task ProgrammabilityArchitect_generates_trigger_and_stored_procedure_and_critic_validates()
    {
        var existing = new DatabaseSchema
        {
            Name = "shop",
            Tables = new List<SchemaTable>
            {
                new()
                {
                    Id = "t_orders",
                    Name = "orders",
                    Columns = new List<SchemaColumn>
                    {
                        new() { Id = "o_id", Name = "id", Type = "UUID", IsPK = true },
                        new() { Id = "o_total", Name = "total_amount", Type = "DECIMAL", Length = 19, Scale = 4 },
                    },
                },
            },
        };

        var (master, _) = BuildHarness((req, _) =>
        {
            if (req.JsonResponse)
            {
                return """
                {
                  "triggers": [
                    {
                      "id": "trg_orders_after_insert",
                      "tableName": "orders",
                      "timing": "After",
                      "event": "Insert",
                      "body": "CREATE OR REPLACE FUNCTION fn_trg_orders_audit() RETURNS TRIGGER AS $$ BEGIN RETURN NEW; END; $$ LANGUAGE plpgsql;"
                    }
                  ],
                  "storedProcedures": [
                    {
                      "id": "sp_monthly_revenue",
                      "name": "sp_monthly_revenue",
                      "parameters": [{ "name": "p_year", "type": "INT" }],
                      "body": "CREATE OR REPLACE PROCEDURE sp_monthly_revenue(p_year INT) LANGUAGE plpgsql AS $$ BEGIN PERFORM SUM(total_amount) FROM orders; END; $$;"
                    }
                  ]
                }
                """;
            }

            return "nsl 1.0\n";
        });

        var verdict = await master.OrchestrateAsync(new OrchestrationTaskInput(
            Prompt: "orders tablosuna after insert trigger ve sp_monthly_revenue stored procedure oluştur",
            Engine: DatabaseType.PostgreSQL,
            SourceKind: SchemaInputSourceKind.ExistingCanvasSchema,
            ExistingSchema: existing,
            DeepDbaAudit: false));

        Assert.True(verdict.Converged);
        var trg = Assert.Single(verdict.FinalSchema.Triggers);
        Assert.Equal("t_orders", trg.TableId);
        Assert.Equal("After", trg.Timing);
        Assert.Equal("Insert", trg.Event);

        var sp = Assert.Single(verdict.FinalSchema.StoredProcedures);
        Assert.Equal("sp_monthly_revenue", sp.Name);
        Assert.Single(sp.Parameters);

        // Yetim trigger eklendiğinde TriggerAndProcedureCriticSubAgent blocker bulmalı.
        verdict.FinalSchema.Triggers.Add(new SchemaTrigger
        {
            Id = "trg_broken",
            TableId = "t_missing",
            Timing = "After",
            Event = "Insert",
            TargetEngine = DatabaseType.PostgreSQL,
            Body = string.Empty,
        });

        var swarm = new AgentSwarmRuntime();
        var criticWs = new OrchestrationWorkspace(
            new OrchestrationTaskInput("audit", DatabaseType.PostgreSQL),
            swarm,
            verdict.FinalSchema);
        var cAgent = swarm.SpawnAgent(AgentKind.TriggerAndProcedureCritic, "prog-critic", "test");
        var progFindings = await new TriggerAndProcedureCriticSubAgent().ExecuteAsync(cAgent, criticWs);

        Assert.Contains(progFindings, f => f.Code == "PROG-ORPHAN-TRIGGER");
        Assert.Contains(progFindings, f => f.Code == "PROG-EMPTY-BODY");
    }
}
