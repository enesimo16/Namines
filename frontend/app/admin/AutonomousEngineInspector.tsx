'use client';

import React, { useState } from 'react';
import {
  Activity,
  Bot,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Cpu,
  Layers,
  ShieldCheck,
  Sparkles,
  Wrench,
} from 'lucide-react';
import type { AdminAutonomousEngineSnapshot } from '../../services/adminService';

interface AutonomousEngineInspectorProps {
  snapshot: AdminAutonomousEngineSnapshot | null;
  isEn?: boolean;
}

const DEFAULT_SNAPSHOT: AdminAutonomousEngineSnapshot = {
  engineVersion: 'Namines.AI v2 (Dialectical Swarm)',
  contextArchitecture: 'NSL-First L0 Topological Skeleton + L1 Scoped Subgraph + L2 Locked Contracts',
  maxOutputTokensCeiling: 65536,
  totalRuns: 0,
  convergedRuns: 0,
  averageQualityScore: 0,
  totalSubAgentsSpawned: 0,
  totalSubAgentsRetired: 0,
  totalTablesSynthesized: 0,
  totalLockedContracts: 0,
  subAgents: [
    { kind: 'Cartographer', name: 'CartographerSubAgent', role: 'Çoklu kaynak tarama + L0 topolojik iskelet (0-token)', zeroTokenCapable: true, orchestratorParent: 'MasterOrchestrator' },
    { kind: 'TopologicalPlanner', name: 'TopologicalPlannerSubAgent', role: 'Tier 0..4 katmanlı 1-160 tablo bağımlılık dalgası', zeroTokenCapable: true, orchestratorParent: 'MasterOrchestrator' },
    { kind: 'DomainArchitect', name: 'NslDomainArchitectSubAgent', role: 'Kilitli PK/FK sözleşmeleriyle NSL 1.0 üretimi', zeroTokenCapable: false, orchestratorParent: 'PositiveOrchestrator [+]' },
    { kind: 'SchemaMutator', name: 'CanvasEvolutionSubAgent', role: 'L0 + L1 alt grafikle StableUuid koruyan Canvas evrimi', zeroTokenCapable: false, orchestratorParent: 'PositiveOrchestrator [+]' },
    { kind: 'SchemaMutator', name: 'DeterministicRepairSubAgent', role: '0-token PK, DECIMAL, password_hash, FK indeks ve Cascade onarımı', zeroTokenCapable: true, orchestratorParent: 'Positive [+] / Master' },
    { kind: 'SchemaMutator', name: 'SurgicalMutationSubAgent', role: 'Kalan blocker ve kapsam eksikleri için 1-8 tablo L1 cerrahi NSL', zeroTokenCapable: false, orchestratorParent: 'MasterOrchestrator' },
    { kind: 'DbaFinOpsSecurityCritic', name: 'AdversarialDbaSubAgent', role: '0-token yerel + NSL-sıkıştırılmış derin DBA denetimi', zeroTokenCapable: true, orchestratorParent: 'NegativeOrchestrator [-]' },
    { kind: 'LifecycleSpecialist', name: 'LifecycleTaskSubAgent', role: 'Migration, SmartSeed, MockData, DbContext, NL->SQL, Vision', zeroTokenCapable: false, orchestratorParent: 'UnifiedAiService' },
  ],
  workspaceTools: [
    { name: 'code_schema_extract', description: 'Prisma (.prisma), EF Core (DbSet) ve .sql dosyalarından 0-token çıkarım' },
    { name: 'json_shape_infer', description: 'Örnek JSON yanıtlarından değer saklamadan varlık + FK çıkarımı' },
    { name: 'openapi_extract', description: 'OpenAPI/Swagger ve GraphQL introspection varlık adayları' },
    { name: 'github_extract', description: 'SSRF-güvenli GitHub depo URL taraması' },
    { name: 'deterministic_validate_compile', description: 'NslValidator + FkCascadeAnalyzer + 6 motor DDL derleme kapısı' },
    { name: 'subgraph_inspect', description: 'L1 odaklı NSL alt grafiği + 1-hop kilitli PK/FK incelemesi' },
  ],
  quotas: [
    { plan: 'Free', dailyTokens: 100000, maxAutonomyWaves: 4, maxOutputTokens: 65536 },
    { plan: 'Pro', dailyTokens: 1000000, maxAutonomyWaves: 8, maxOutputTokens: 65536 },
    { plan: 'Team', dailyTokens: 1000000, maxAutonomyWaves: 8, maxOutputTokens: 65536 },
    { plan: 'Enterprise', dailyTokens: 25000000, maxAutonomyWaves: 12, maxOutputTokens: 65536 },
    { plan: 'Dev', dailyTokens: 2147483647, maxAutonomyWaves: 16, maxOutputTokens: 65536 },
  ],
  recentRuns: [],
};

export default function AutonomousEngineInspector({ snapshot, isEn = false }: AutonomousEngineInspectorProps) {
  const data = snapshot ?? DEFAULT_SNAPSHOT;
  const [activeView, setActiveView] = useState<'architecture' | 'quotas' | 'runs'>('architecture');
  const [expandedRunId, setExpandedRunId] = useState<string | null>(null);

  const convergencePct = data.totalRuns > 0
    ? Math.round((data.convergedRuns / data.totalRuns) * 100)
    : 100;

  return (
    <div className="rounded-[var(--radius-card)] bg-surface-800 border border-line-strong p-4 space-y-4 font-sans">
      {/* Üst Başlık & Mimari Özeti */}
      <div className="flex flex-col lg:flex-row lg:items-center justify-between gap-3 pb-3 border-b border-line">
        <div className="space-y-1">
          <div className="flex flex-wrap items-center gap-2">
            <div className="p-1.5 rounded-[var(--radius-control)] bg-accent-subtle border border-accent/30">
              <Sparkles className="w-4 h-4 text-accent-text" />
            </div>
            <h3 className="text-sm font-bold text-content-primary">
              {isEn ? 'Bounded Autonomy Dialectical AI Engine' : 'Sınırlı Özerkliğe Sahip Diyalektik AI Motoru'}
            </h3>
            <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-micro font-mono font-bold text-accent-text">
              {data.engineVersion}
            </span>
          </div>
          <p className="text-[11px] font-mono text-content-muted">
            {data.contextArchitecture}
          </p>
        </div>

        <div className="flex items-center gap-1 shrink-0 bg-surface-900 p-1 rounded-[var(--radius-control)] border border-line">
          <button
            type="button"
            onClick={() => setActiveView('architecture')}
            className={`px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-semibold transition-colors cursor-pointer ${
              activeView === 'architecture'
                ? 'bg-surface-700 text-content-primary border border-line-strong'
                : 'text-content-muted hover:text-content-primary'
            }`}
          >
            {isEn ? 'Architecture & Swarm' : 'Mimari & Ajanlar'}
          </button>
          <button
            type="button"
            onClick={() => setActiveView('quotas')}
            className={`px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-semibold transition-colors cursor-pointer ${
              activeView === 'quotas'
                ? 'bg-surface-700 text-content-primary border border-line-strong'
                : 'text-content-muted hover:text-content-primary'
            }`}
          >
            {isEn ? 'Quotas & Context' : 'Kotalar & Bağlam'}
          </button>
          <button
            type="button"
            onClick={() => setActiveView('runs')}
            className={`px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-semibold transition-colors cursor-pointer flex items-center gap-1.5 ${
              activeView === 'runs'
                ? 'bg-surface-700 text-content-primary border border-line-strong'
                : 'text-content-muted hover:text-content-primary'
            }`}
          >
            <span>{isEn ? 'Live Runs' : 'Canlı Oturumlar'}</span>
            <span className="px-1.5 py-0.2 rounded-full bg-surface-800 border border-line text-[10px] font-mono">
              {data.recentRuns.length}
            </span>
          </button>
        </div>
      </div>

      {/* 4 Canlı KPI Kartı */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-1">
          <span className="text-[10px] font-mono uppercase text-content-muted flex items-center gap-1">
            <Activity className="w-3 h-3 text-accent-text" />
            {isEn ? 'Orchestration Runs' : 'Orkestrasyon / Yakınsama'}
          </span>
          <div className="text-base font-mono font-bold text-content-primary">
            {data.totalRuns} <span className="text-xs font-normal text-content-secondary">({convergencePct}% OK)</span>
          </div>
        </div>

        <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-1">
          <span className="text-[10px] font-mono uppercase text-content-muted flex items-center gap-1">
            <ShieldCheck className="w-3 h-3 text-success" />
            {isEn ? 'Avg Quality Score' : 'Ort. Kalite Puanı'}
          </span>
          <div className="text-base font-mono font-bold text-content-primary">
            {data.totalRuns > 0 ? `${Math.round(data.averageQualityScore * 100)}%` : '—'}
          </div>
        </div>

        <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-1">
          <span className="text-[10px] font-mono uppercase text-content-muted flex items-center gap-1">
            <Bot className="w-3 h-3 text-accent-text" />
            {isEn ? 'Sub-Agent Lifecycle' : 'Üretilen / Emekli Ajan'}
          </span>
          <div className="text-base font-mono font-bold text-content-primary">
            {data.totalSubAgentsSpawned} <span className="text-xs font-normal text-content-muted">/ {data.totalSubAgentsRetired}</span>
          </div>
        </div>

        <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-1">
          <span className="text-[10px] font-mono uppercase text-content-muted flex items-center gap-1">
            <Cpu className="w-3 h-3 text-accent-text" />
            {isEn ? 'Output Ceiling' : 'Çıktı Tavanı & Kilitli'}
          </span>
          <div className="text-base font-mono font-bold text-content-primary">
            {data.maxOutputTokensCeiling.toLocaleString()} <span className="text-[10px] font-normal text-content-muted">tok</span>
          </div>
        </div>
      </div>

      {/* Görünüm 1: 3 Katmanlı Diyalektik Mimari & 8 Alt-Ajan & 6 Araç */}
      {activeView === 'architecture' && (
        <div className="space-y-4">
          {/* 3 Orkestratör Hiyerarşisi */}
          <div className="grid grid-cols-1 md:grid-cols-3 gap-3 text-xs">
            <div className="rounded-[var(--radius-control)] bg-surface-900 border border-accent/30 p-3 space-y-1">
              <span className="text-[10px] font-mono font-bold uppercase text-accent-text">
                1 · IMasterOrchestrator
              </span>
              <p className="font-semibold text-content-primary">Ana Baş Orkestratör</p>
              <p className="text-[11px] text-content-muted leading-relaxed">
                {isEn
                  ? 'Owns bounded autonomy budget, coordinates [+] and [-] loops, dispatches surgical fixes, emits MasterVerdict.'
                  : 'Sınırlı özerklik bütçesini yönetir, [+] ve [-] döngülerini koordine eder, alt-ajan doğurur ve nihai kararı verir.'}
              </p>
            </div>
            <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line-strong p-3 space-y-1">
              <span className="text-[10px] font-mono font-bold uppercase text-success">
                2 · IPositiveOrchestrator [+]
              </span>
              <p className="font-semibold text-content-primary">Yapıcı / Artıları Derleyen</p>
              <p className="text-[11px] text-content-muted leading-relaxed">
                {isEn
                  ? 'Builds topological waves in NSL 1.0, evolves Canvas preserving StableUuid, locks TableSignatureContracts.'
                  : 'NSL 1.0 ile dalgaları inşa eder, StableUuid koruyarak Canvas evrimi yapar, doğrulanan PK/FK sözleşmelerini kilitler.'}
              </p>
            </div>
            <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line-strong p-3 space-y-1">
              <span className="text-[10px] font-mono font-bold uppercase text-danger">
                3 · INegativeOrchestrator [-]
              </span>
              <p className="font-semibold text-content-primary">Eleştirel / Eksileri Derleyen</p>
              <p className="text-[11px] text-content-muted leading-relaxed">
                {isEn
                  ? 'Spawns Deterministic, Semantic Coverage and DBA/FinOps/Security critics across all 6 DDL engines.'
                  : '6 DDL motorunda derleme, plan kapsamı, cascade analizi ve DBA/FinOps/PII risklerini denetleyip eyleme dönük direktif üretir.'}
              </p>
            </div>
          </div>

          {/* 8 Uzman Alt-Ajan & 6 Çoklu Kaynak Aracı */}
          <div className="grid grid-cols-1 lg:grid-cols-2 gap-3">
            <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[11px] font-bold text-content-primary flex items-center gap-1.5">
                  <Layers className="w-3.5 h-3.5 text-accent-text" />
                  {isEn ? `Dynamic Specialist Sub-Agents (${data.subAgents.length})` : `Dinamik Uzman Alt-Ajanlar (${data.subAgents.length})`}
                </span>
                <span className="text-[10px] font-mono text-content-muted">IAgentSwarmRuntime</span>
              </div>
              <div className="divide-y divide-line/60">
                {data.subAgents.map((agent) => (
                  <div key={agent.name} className="py-1.5 flex items-start justify-between gap-2 text-[11px]">
                    <div className="min-w-0">
                      <div className="flex items-center gap-1.5">
                        <span className="font-mono font-semibold text-content-primary truncate">{agent.name}</span>
                        {agent.zeroTokenCapable && (
                          <span className="px-1.5 py-0.2 rounded-[var(--radius-control)] bg-surface-800 border border-line text-micro font-mono text-success">
                            0-TOKEN
                          </span>
                        )}
                      </div>
                      <p className="text-[10px] text-content-muted truncate">{agent.role}</p>
                    </div>
                    <span className="text-[10px] font-mono text-content-secondary shrink-0">
                      {agent.orchestratorParent}
                    </span>
                  </div>
                ))}
              </div>
            </div>

            <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[11px] font-bold text-content-primary flex items-center gap-1.5">
                  <Wrench className="w-3.5 h-3.5 text-accent-text" />
                  {isEn ? `Multi-Source Workspace Tools (${data.workspaceTools.length})` : `Çoklu Kaynak Çalışma Araçları (${data.workspaceTools.length})`}
                </span>
                <span className="text-[10px] font-mono text-content-muted">IAiWorkspaceToolRegistry</span>
              </div>
              <div className="divide-y divide-line/60">
                {data.workspaceTools.map((tool) => (
                  <div key={tool.name} className="py-1.5 space-y-0.5 text-[11px]">
                    <div className="font-mono font-semibold text-accent-text">{tool.name}</div>
                    <p className="text-[10px] text-content-muted">{tool.description}</p>
                  </div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}

      {/* Görünüm 2: Kotalar & NSL Optimizasyon */}
      {activeView === 'quotas' && (
        <div className="space-y-3 text-xs">
          <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line overflow-hidden">
            <table className="w-full text-left border-collapse">
              <thead>
                <tr className="border-b border-line text-[10px] font-mono uppercase text-content-muted bg-surface-800/70">
                  <th className="py-2 px-3">{isEn ? 'Plan Tier' : 'Plan'}</th>
                  <th className="py-2 px-3">{isEn ? 'Daily Token Pool' : 'Günlük Token Hakkı'}</th>
                  <th className="py-2 px-3">{isEn ? 'Max Autonomy Waves' : 'Maks. Otonomi Dalgası'}</th>
                  <th className="py-2 px-3">{isEn ? 'Output Truncation Cap' : 'Çıktı Token Tavanı'}</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-line/60 font-mono text-[11px]">
                {data.quotas.map((q) => (
                  <tr key={q.plan} className="hover:bg-surface-800/40">
                    <td className="py-2 px-3 font-bold text-content-primary">{q.plan}</td>
                    <td className="py-2 px-3 text-content-secondary">
                      {q.dailyTokens >= 2147483647 ? (isEn ? 'Unlimited' : 'Sınırsız') : q.dailyTokens.toLocaleString()}
                    </td>
                    <td className="py-2 px-3 text-content-secondary">{q.maxAutonomyWaves} tur / dalga</td>
                    <td className="py-2 px-3 text-success font-semibold">{q.maxOutputTokens.toLocaleString()} (yapay kesme yok)</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="text-[11px] text-content-muted">
            {isEn
              ? 'NSL 1.0 wire representation + 0-token L0 skeleton cuts schema prompt context overhead by ~80-85% vs raw JSON.'
              : 'NSL 1.0 metin temsili ve 0-token L0 topolojik iskelet sayesinde ham JSON bağlamına kıyasla ~%80-85 token tasarrufu sağlanır; Free=6K / Pro=16K yapay kırpması kaldırılmıştır.'}
          </p>
        </div>
      )}

      {/* Görünüm 3: Canlı Orkestrasyon Karar Defteri */}
      {activeView === 'runs' && (
        <div className="space-y-2">
          {data.recentRuns.length === 0 ? (
            <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-6 text-center text-xs text-content-muted">
              {isEn
                ? 'No live orchestration runs recorded yet in this server process. Trigger Schema Generate / Revise to inspect live [+]/[-] trace and swarm stats.'
                : 'Bu sunucu oturumunda henüz otonom şema orkestrasyonu çalışmadı. Şema Üret veya Canvas Revizyonu tetiklendiğinde [+] / [-] raporları, kalite puanı ve alt-ajan izleri burada canlı listelenir.'}
            </div>
          ) : (
            <div className="divide-y divide-line/60 rounded-[var(--radius-control)] bg-surface-900 border border-line overflow-hidden">
              {data.recentRuns.map((run) => {
                const expanded = expandedRunId === run.runId;
                return (
                  <div key={run.runId} className="p-3 space-y-2 text-xs">
                    <div
                      onClick={() => setExpandedRunId(expanded ? null : run.runId)}
                      className="flex flex-col sm:flex-row sm:items-center justify-between gap-2 cursor-pointer"
                    >
                      <div className="flex items-center gap-2 min-w-0">
                        {expanded ? (
                          <ChevronDown className="w-3.5 h-3.5 text-content-muted shrink-0" />
                        ) : (
                          <ChevronRight className="w-3.5 h-3.5 text-content-muted shrink-0" />
                        )}
                        <CheckCircle2
                          className={`w-3.5 h-3.5 shrink-0 ${run.converged ? 'text-success' : 'text-danger'}`}
                        />
                        <span className="font-mono text-[10px] px-1.5 py-0.2 rounded-[var(--radius-control)] bg-surface-800 border border-line text-content-secondary uppercase">
                          {run.engine}
                        </span>
                        <span className="font-medium text-content-primary truncate">
                          {run.promptPreview || run.sourceKind}
                        </span>
                      </div>

                      <div className="flex flex-wrap items-center gap-2 text-[10px] font-mono shrink-0">
                        <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-accent-subtle text-accent-text border border-accent/30 font-bold">
                          Kalite: {Math.round(run.qualityScore * 100)}%
                        </span>
                        <span className="text-content-secondary">
                          {run.tablesBuilt} tablo / {run.relationsBuilt} ilişki
                        </span>
                        <span className="text-content-muted">
                          Ajan: {run.retiredSubAgents}/{run.spawnedSubAgents}
                        </span>
                      </div>
                    </div>

                    {expanded && (
                      <div className="pt-2 pl-5 border-t border-line/60 grid grid-cols-1 md:grid-cols-2 gap-3 text-[11px]">
                        <div className="space-y-1">
                          <span className="text-[10px] font-mono font-bold uppercase text-success block">
                            [+] Yapıcı Kazanımlar ({run.lockedContractsCount} kilitli sözleşme)
                          </span>
                          <ul className="space-y-0.5 text-content-secondary list-disc pl-4">
                            {run.plusGains.map((g, i) => (
                              <li key={i}>{g}</li>
                            ))}
                          </ul>
                        </div>
                        <div className="space-y-1">
                          <span className="text-[10px] font-mono font-bold uppercase text-danger block">
                            [-] Kalan Bulgular (B:{run.minusBlockerCount} · G:{run.minusGapCount} · DBA:{run.minusDbaCount})
                          </span>
                          {run.remainingFindings.length === 0 ? (
                            <p className="text-content-muted">Blocker veya açık yapısal bulgu kalmadı.</p>
                          ) : (
                            <ul className="space-y-0.5 text-content-secondary list-disc pl-4">
                              {run.remainingFindings.map((f, i) => (
                                <li key={i}>{f}</li>
                              ))}
                            </ul>
                          )}
                        </div>
                        {run.timeline.length > 0 && (
                          <div className="md:col-span-2 space-y-1">
                            <span className="text-[10px] font-mono font-bold uppercase text-content-muted block">
                              Orkestrasyon Zaman Çizelgesi ({run.totalIterations} iterasyon)
                            </span>
                            <div className="max-h-36 overflow-y-auto rounded-[var(--radius-control)] bg-surface-800 border border-line p-2 font-mono text-[10px] text-content-secondary space-y-0.5">
                              {run.timeline.map((line, idx) => (
                                <div key={idx}>{line}</div>
                              ))}
                            </div>
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
