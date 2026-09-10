'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import dynamic from 'next/dynamic';
import { useRouter, useSearchParams } from 'next/navigation';
import type { Node, Edge } from '@xyflow/react';
import {
  AlertTriangle,
  Check,
  CheckCircle2,
  Copy,
  ExternalLink,
  Eye,
  Filter,
  Info,
  Layers,
  Loader2,
  Search,
  ShieldCheck,
  Wand2,
  X,
} from 'lucide-react';
import { schemaToFlow } from '../../lib/schemaToFlow';
import {
  getEnrichedBlueprints,
  getScaleCounts,
  getStackCounts,
  getTagCounts,
  type EnrichedBlueprint,
} from '../../lib/blueprintCatalog';
import { type TemplateSize } from '../../lib/templates';
import { schemaService } from '../../services/api';
import { useSchemaStore } from '../../store/useSchemaStore';

const DemoCanvas = dynamic(() => import('../../components/landing/DemoCanvas'), {
  ssr: false,
  loading: () => <div className="h-full w-full" />,
});

const ENGINES = ['PostgreSQL', 'MySQL', 'MSSQL', 'SQLite', 'Oracle', 'MariaDB'] as const;
type Engine = (typeof ENGINES)[number];

interface LintMessage {
  severity: number | string;
  message: string;
  tableId?: string | null;
  columnId?: string | null;
}

function severityOf(raw: number | string): 'error' | 'warning' | 'info' {
  const value = typeof raw === 'string' ? raw.toLowerCase() : raw;
  if (value === 2 || value === 'error') return 'error';
  if (value === 1 || value === 'warning') return 'warning';
  return 'info';
}

function groupFindings(messages: LintMessage[]) {
  const groups = new Map<string, { severity: string; message: string; count: number; subjects: string[] }>();

  for (const m of messages) {
    const key = m.message.replace(/'[^']*'/g, "'…'");
    const subject = m.message.match(/'([^']*)'/)?.[1] ?? '';
    const existing = groups.get(key);
    if (existing) {
      existing.count += 1;
      if (existing.subjects.length < 6 && subject) existing.subjects.push(subject);
    } else {
      groups.set(key, {
        severity: severityOf(m.severity),
        message: m.message,
        count: 1,
        subjects: subject ? [subject] : [],
      });
    }
  }

  return [...groups.values()].sort((a, b) => {
    const rank = { error: 0, warning: 1, info: 2 } as Record<string, number>;
    return (rank[a.severity] ?? 3) - (rank[b.severity] ?? 3) || b.count - a.count;
  });
}

/** Render.com mimari merdiven çizgileri (stair-step line art) */
function RenderStairLines() {
  return (
    <svg
      className="pointer-events-none absolute bottom-0 right-0 h-28 w-28 text-content-primary opacity-20 transition-opacity group-hover:opacity-35"
      viewBox="0 0 100 100"
      fill="none"
      stroke="currentColor"
      aria-hidden="true"
    >
      <path d="M40 100 V60 H70 V30 H100" strokeWidth="1.5" />
      <path d="M10 100 V80 H40 V60" strokeWidth="1.5" />
      <path d="M70 30 V0" strokeWidth="1.5" />
      <line x1="70" y1="30" x2="70" y2="100" strokeWidth="1" strokeDasharray="2 2" />
      <line x1="40" y1="60" x2="100" y2="60" strokeWidth="1" strokeDasharray="2 2" />
      <line x1="10" y1="80" x2="100" y2="80" strokeWidth="1" strokeDasharray="2 2" />
    </svg>
  );
}

function DemoContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const requested = searchParams?.get('template') ?? '';

  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const setDbType = useSchemaStore(s => s.setDbType);

  const allBlueprints = useMemo(() => getEnrichedBlueprints(), []);

  // Filtre durumları
  const [searchQuery, setSearchQuery] = useState('');
  const [selectedStacks, setSelectedStacks] = useState<string[]>([]);
  const [selectedScales, setSelectedScales] = useState<TemplateSize[]>([]);
  const [selectedTags, setSelectedTags] = useState<string[]>([]);
  const [sortBy, setSortBy] = useState<'featured' | 'alpha' | 'tables' | 'relations'>('featured');

  // Sidebar daraltma/genişletme sınırları
  const [showAllStacks, setShowAllStacks] = useState(false);
  const [showAllTags, setShowAllTags] = useState(false);
  const [mobileFiltersOpen, setMobileFiltersOpen] = useState(false);

  // İncelenen blueprint modalı
  const initialActive = allBlueprints.find(b => b.key === requested) ?? null;
  const [activeBlueprint, setActiveBlueprint] = useState<EnrichedBlueprint | null>(initialActive);
  const [engine, setEngine] = useState<Engine>('PostgreSQL');
  const [modalTab, setModalTab] = useState<'canvas' | 'findings' | 'sql' | 'split'>('split');
  const [copiedSql, setCopiedSql] = useState(false);

  // URL'den template parametresi değişirse modalı aç
  useEffect(() => {
    if (requested) {
      const match = allBlueprints.find(b => b.key === requested);
      if (match) setActiveBlueprint(match);
    }
  }, [requested, allBlueprints]);

  // Modal içindeki linter ve DDL sonuçları
  const [result, setResult] = useState<{
    key: string;
    lint: LintMessage[] | null;
    sql: string | null;
  } | null>(null);

  const requestKey = `${activeBlueprint?.key ?? ''}|${engine}`;

  useEffect(() => {
    if (!activeBlueprint) return;
    let cancelled = false;

    void Promise.all([
      schemaService.lintSchema(activeBlueprint.schema),
      schemaService.compileSql(activeBlueprint.schema, engine),
    ])
      .then(([lintResult, ddl]) => {
        if (!cancelled) setResult({ key: requestKey, lint: lintResult?.messages ?? [], sql: ddl ?? '' });
      })
      .catch(() => {
        if (!cancelled) setResult({ key: requestKey, lint: null, sql: null });
      });

    return () => {
      cancelled = true;
    };
  }, [activeBlueprint, engine, requestKey]);

  const settled = result?.key === requestKey ? result : null;
  const proving = settled === null && activeBlueprint !== null;
  const failed = settled !== null && settled.lint === null;
  const lint = settled?.lint ?? null;
  const sql = settled?.sql ?? null;

  const { nodes, edges } = useMemo(() => {
    if (!activeBlueprint) return { nodes: [] as Node[], edges: [] as Edge[] };
    const { nodes: flowNodes, edges: flowEdges } = schemaToFlow(activeBlueprint.schema);
    return {
      nodes: flowNodes.map(n => ({ ...n, draggable: false, selectable: false, connectable: false })),
      edges: flowEdges.map(e => ({ ...e, deletable: false })),
    };
  }, [activeBlueprint]);

  const grouped = useMemo(() => groupFindings(lint ?? []), [lint]);

  const counts = useMemo(() => {
    const messages = lint ?? [];
    return {
      errors: messages.filter(m => severityOf(m.severity) === 'error').length,
      warnings: messages.filter(m => severityOf(m.severity) === 'warning').length,
      infos: messages.filter(m => severityOf(m.severity) === 'info').length,
    };
  }, [lint]);

  // Filtreleme mantığı
  const filteredBlueprints = useMemo(() => {
    let list = [...allBlueprints];

    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase().trim();
      list = list.filter(
        b =>
          b.label.toLowerCase().includes(q) ||
          b.slug.toLowerCase().includes(q) ||
          b.description.toLowerCase().includes(q) ||
          b.tags.some(t => t.toLowerCase().includes(q)) ||
          b.stack.some(s => s.toLowerCase().includes(q)) ||
          b.schema.tables.some(t => t.name.toLowerCase().includes(q)),
      );
    }

    if (selectedStacks.length > 0) {
      list = list.filter(b => selectedStacks.some(s => b.stack.includes(s)));
    }

    if (selectedScales.length > 0) {
      list = list.filter(b => selectedScales.includes(b.size));
    }

    if (selectedTags.length > 0) {
      list = list.filter(b => selectedTags.some(t => b.tags.includes(t)));
    }

    // Sıralama
    if (sortBy === 'alpha') {
      list.sort((a, b) => a.label.localeCompare(b.label));
    } else if (sortBy === 'tables') {
      list.sort((a, b) => b.tableCount - a.tableCount);
    } else if (sortBy === 'relations') {
      list.sort((a, b) => b.relationCount - a.relationCount);
    }

    return list;
  }, [allBlueprints, searchQuery, selectedStacks, selectedScales, selectedTags, sortBy]);

  // Filtreleme istatistikleri
  const stackOptions = useMemo(() => getStackCounts(allBlueprints), [allBlueprints]);
  const tagOptions = useMemo(() => getTagCounts(allBlueprints), [allBlueprints]);
  const scaleOptions = useMemo(() => getScaleCounts(allBlueprints), [allBlueprints]);

  const hasActiveFilters =
    searchQuery.trim().length > 0 ||
    selectedStacks.length > 0 ||
    selectedScales.length > 0 ||
    selectedTags.length > 0;

  const resetFilters = () => {
    setSearchQuery('');
    setSelectedStacks([]);
    setSelectedScales([]);
    setSelectedTags([]);
  };

  const handleOpenInEditor = (blueprint: EnrichedBlueprint) => {
    loadFromSchema(blueprint.schema);
    setDbType(engine as never);
    router.push('/canvas');
  };

  const handleCopySql = () => {
    if (!sql) return;
    navigator.clipboard.writeText(sql);
    setCopiedSql(true);
    setTimeout(() => setCopiedSql(false), 2000);
  };

  return (
    <div className="min-h-screen bg-surface-900 px-4 py-8 text-content-primary md:px-8">
      <div className="mx-auto max-w-[var(--w-wide)] space-y-8">
        {/* Üst Karşılama Başlığı */}
        <header className="space-y-3">
          <div className="flex flex-col justify-between gap-4 md:flex-row md:items-end">
            <div>
              <h1 className="text-3xl font-extrabold tracking-tight text-content-primary sm:text-4xl">
                Database Blueprints & Templates
              </h1>
              <p className="mt-2 max-w-3xl text-sm leading-relaxed text-content-muted">
                Explore production-ready database schemas built and proven by Namines. Every blueprint runs through the
                exact same deterministic rule engine and compiles zero-drift DDL for six major engines.
              </p>
            </div>
            <div className="flex items-center gap-2">
              <Link
                href="/new"
                className="inline-flex items-center gap-2 rounded-[var(--radius-card)] bg-accent px-4 py-2.5 text-xs font-bold text-surface-900 transition-all hover:bg-accent-hover"
              >
                <Wand2 className="h-3.5 w-3.5" />
                Describe your own with AI
              </Link>
            </div>
          </div>
        </header>

        {/* Mobil Filtre Butonu */}
        <div className="flex items-center justify-between rounded-[var(--radius-card)] border border-surface-600 bg-surface-800 p-3 lg:hidden">
          <button
            type="button"
            onClick={() => setMobileFiltersOpen(!mobileFiltersOpen)}
            className="flex items-center gap-2 text-xs font-bold text-content-primary"
          >
            <Filter className="h-4 w-4 text-accent" />
            <span>Filters {hasActiveFilters ? '(Active)' : ''}</span>
          </button>
          <span className="font-mono text-xs font-bold text-content-muted">
            {filteredBlueprints.length} BLUEPRINTS
          </span>
        </div>

        {/* Ana İki Kolonlu Yerleşim */}
        <div className="flex flex-col gap-8 lg:flex-row">
          {/* Sol Kenar Çubuğu (Filters) */}
          <aside
            className={`w-full shrink-0 space-y-6 lg:block lg:w-64 ${
              mobileFiltersOpen ? 'block' : 'hidden'
            }`}
          >
            <div className="flex items-center justify-between">
              <h2 className="text-sm font-bold tracking-wide text-content-primary">Filters</h2>
              {hasActiveFilters && (
                <button
                  type="button"
                  onClick={resetFilters}
                  className="text-xs font-semibold text-accent hover:underline cursor-pointer"
                >
                  Reset all
                </button>
              )}
            </div>

            {/* Arama Kutusu */}
            <div className="relative">
              <Search className="pointer-events-none absolute left-3 top-2.5 h-3.5 w-3.5 text-content-muted" />
              <input
                aria-label="Search blueprints"
                type="text"
                value={searchQuery}
                onChange={e => setSearchQuery(e.target.value)}
                placeholder="Search blueprints..."
                className="w-full rounded-[var(--radius-control)] border border-surface-600 bg-surface-800 py-2 pl-8.5 pr-8 text-xs text-content-primary placeholder:text-content-subtle focus:border-accent focus:outline-none"
              />
              {searchQuery && (
                <button
                  type="button"
                  onClick={() => setSearchQuery('')}
                  className="absolute right-2.5 top-2.5 text-content-muted hover:text-content-primary"
                >
                  <X className="h-3.5 w-3.5" />
                </button>
              )}
            </div>

            {/* STACK Filtresi */}
            <div className="space-y-2.5 border-t border-surface-600/70 pt-4">
              <span className="block font-mono text-[11px] font-bold uppercase tracking-wider text-content-muted">
                STACK
              </span>
              <div className="space-y-1.5">
                {(showAllStacks ? stackOptions : stackOptions.slice(0, 6)).map(item => {
                  const isChecked = selectedStacks.includes(item.name);
                  return (
                    <label
                      key={item.name}
                      className="group flex cursor-pointer items-center justify-between py-1 text-xs text-content-secondary hover:text-content-primary"
                    >
                      <div className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => {
                            setSelectedStacks(prev =>
                              isChecked ? prev.filter(x => x !== item.name) : [...prev, item.name],
                            );
                          }}
                          className="h-3.5 w-3.5 rounded-[var(--radius-control)] border-surface-500 bg-surface-800 text-accent focus:ring-0 focus:ring-offset-0 cursor-pointer"
                        />
                        <span className={isChecked ? 'font-semibold text-content-primary' : ''}>
                          {item.name}
                        </span>
                      </div>
                      <span className="font-mono text-[11px] text-content-subtle">{item.count}</span>
                    </label>
                  );
                })}
              </div>
              {stackOptions.length > 6 && (
                <button
                  type="button"
                  onClick={() => setShowAllStacks(!showAllStacks)}
                  className="mt-1 text-[11px] font-semibold text-content-muted hover:text-accent cursor-pointer"
                >
                  {showAllStacks ? 'Show less' : `Show ${stackOptions.length - 6} more`}
                </button>
              )}
            </div>

            {/* SCALE Filtresi */}
            <div className="space-y-2.5 border-t border-surface-600/70 pt-4">
              <span className="block font-mono text-[11px] font-bold uppercase tracking-wider text-content-muted">
                SCALE
              </span>
              <div className="space-y-1.5">
                {scaleOptions.map(item => {
                  const isChecked = selectedScales.includes(item.size);
                  return (
                    <label
                      key={item.size}
                      className="group flex cursor-pointer items-center justify-between py-1 text-xs text-content-secondary hover:text-content-primary"
                    >
                      <div className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => {
                            setSelectedScales(prev =>
                              isChecked ? prev.filter(x => x !== item.size) : [...prev, item.size],
                            );
                          }}
                          className="h-3.5 w-3.5 rounded-[var(--radius-control)] border-surface-500 bg-surface-800 text-accent focus:ring-0 focus:ring-offset-0 cursor-pointer"
                        />
                        <span className={isChecked ? 'font-semibold text-content-primary' : ''}>
                          {item.label}
                        </span>
                      </div>
                      <span className="font-mono text-[11px] text-content-subtle">{item.count}</span>
                    </label>
                  );
                })}
              </div>
            </div>

            {/* TAGS Filtresi */}
            <div className="space-y-2.5 border-t border-surface-600/70 pt-4">
              <span className="block font-mono text-[11px] font-bold uppercase tracking-wider text-content-muted">
                TAGS
              </span>
              <div className="space-y-1.5">
                {(showAllTags ? tagOptions : tagOptions.slice(0, 7)).map(item => {
                  const isChecked = selectedTags.includes(item.name);
                  return (
                    <label
                      key={item.name}
                      className="group flex cursor-pointer items-center justify-between py-1 text-xs text-content-secondary hover:text-content-primary"
                    >
                      <div className="flex items-center gap-2">
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => {
                            setSelectedTags(prev =>
                              isChecked ? prev.filter(x => x !== item.name) : [...prev, item.name],
                            );
                          }}
                          className="h-3.5 w-3.5 rounded-[var(--radius-control)] border-surface-500 bg-surface-800 text-accent focus:ring-0 focus:ring-offset-0 cursor-pointer"
                        />
                        <span className={isChecked ? 'font-semibold text-content-primary' : ''}>
                          {item.name}
                        </span>
                      </div>
                      <span className="font-mono text-[11px] text-content-subtle">{item.count}</span>
                    </label>
                  );
                })}
              </div>
              {tagOptions.length > 7 && (
                <button
                  type="button"
                  onClick={() => setShowAllTags(!showAllTags)}
                  className="mt-1 text-[11px] font-semibold text-content-muted hover:text-accent cursor-pointer"
                >
                  {showAllTags ? 'Show less' : `Show ${tagOptions.length - 7} more`}
                </button>
              )}
            </div>
          </aside>

          {/* Sağ Ana Alan (Grid ve Başlık) */}
          <main className="flex-1 min-w-0 space-y-6">
            {/* Üst Bar: Şablon Sayısı ve Sıralama */}
            <div className="flex flex-wrap items-center justify-between gap-4 border-b border-surface-600/70 pb-4">
              <div className="flex items-center gap-3">
                <span className="font-mono text-xs font-bold uppercase tracking-widest text-content-muted">
                  {filteredBlueprints.length} BLUEPRINTS
                </span>
                {hasActiveFilters && (
                  <span className="rounded-[var(--radius-control)] bg-accent/15 px-2 py-0.5 text-[11px] font-semibold text-accent-text">
                    Filtered
                  </span>
                )}
              </div>

              <div className="flex items-center gap-3">
                <span className="text-xs text-content-muted">Sort by:</span>
                <select
                  value={sortBy}
                  onChange={e => setSortBy(e.target.value as typeof sortBy)}
                  aria-label="Sort blueprints"
                  className="rounded-[var(--radius-control)] border border-surface-600 bg-surface-800 px-3 py-1.5 text-xs font-semibold text-content-primary focus:border-accent focus:outline-none cursor-pointer"
                >
                  <option value="featured">Featured</option>
                  <option value="alpha">Alphabetical (A-Z)</option>
                  <option value="tables">Table Count (High to Low)</option>
                  <option value="relations">Relationship Count</option>
                </select>
              </div>
            </div>

            {/* Kartlar Izgarası (Render.com Stili) */}
            {filteredBlueprints.length === 0 ? (
              <div className="flex flex-col items-center justify-center rounded-[var(--radius-modal)] border border-dashed border-surface-600 bg-surface-800/40 p-12 text-center">
                <Layers className="h-10 w-10 text-content-muted" />
                <h3 className="mt-3 text-base font-bold text-content-primary">No blueprints found</h3>
                <p className="mt-1 text-xs text-content-muted">
                  Try clearing some filters or changing your search terms.
                </p>
                <button
                  type="button"
                  onClick={resetFilters}
                  className="mt-4 rounded-[var(--radius-card)] bg-surface-700 px-4 py-2 text-xs font-bold text-content-primary hover:bg-surface-600 cursor-pointer"
                >
                  Clear all filters
                </button>
              </div>
            ) : (
              <div className="grid grid-cols-1 gap-6 md:grid-cols-2 xl:grid-cols-3">
                {filteredBlueprints.map(blueprint => {
                  const Icon = blueprint.icon;

                  // Tema sınıfları (FRONTEND.md kurallarına tam uyumlu)
                  let bannerBg = 'bg-surface-850 border-b border-surface-600/60';
                  if (blueprint.bannerTheme === 'cyan') {
                    bannerBg =
                      'bg-[color-mix(in_oklch,var(--accent)_18%,var(--surface-800))] border-b border-[color-mix(in_oklch,var(--accent)_35%,var(--surface-600))]';
                  } else if (blueprint.bannerTheme === 'mint') {
                    bannerBg =
                      'bg-[color-mix(in_oklch,var(--color-success-subtle)_80%,var(--surface-800))] border-b border-[color-mix(in_oklch,var(--color-success)_30%,var(--surface-600))]';
                  } else if (blueprint.bannerTheme === 'amber') {
                    bannerBg =
                      'bg-[color-mix(in_oklch,var(--color-warning-subtle)_85%,var(--surface-700))] border-b border-surface-600/50';
                  } else if (blueprint.bannerTheme === 'ice') {
                    bannerBg =
                      'bg-[color-mix(in_oklch,var(--accent)_12%,var(--surface-700))] border-b border-surface-600/50';
                  }

                  return (
                    <div
                      key={blueprint.key}
                      onClick={() => setActiveBlueprint(blueprint)}
                      className="group relative flex flex-col overflow-hidden rounded-[var(--radius-card)] border border-surface-600/70 bg-surface-800 transition-all hover:border-accent/60 hover:shadow-xl cursor-pointer"
                    >
                      {/* Üst Banner (Render.com Stili) */}
                      <div className={`relative flex h-40 flex-col justify-between p-4.5 ${bannerBg}`}>
                        <RenderStairLines />

                        {/* Banner Üst Satır: İkon ve Küçük Başlık */}
                        <div className="relative z-10">
                          <Icon className="h-6 w-6 text-content-primary" />
                          <h3 className="mt-2 text-xl font-bold tracking-tight text-content-primary group-hover:text-accent-text transition-colors">
                            {blueprint.slug}
                          </h3>
                        </div>

                        {/* Banner Alt Satır: Kicker */}
                        <div className="relative z-10">
                          <span className="font-mono text-[10px] font-bold tracking-widest text-content-muted uppercase">
                            NAMINES BLUEPRINT
                          </span>
                        </div>
                      </div>

                      {/* Kart Gövdesi */}
                      <div className="flex flex-1 flex-col justify-between p-5 space-y-4">
                        <div>
                          <h4 className="text-sm font-bold text-content-primary leading-snug">
                            {blueprint.label}
                          </h4>
                          <p className="mt-1.5 line-clamp-2 text-xs leading-relaxed text-content-muted">
                            {blueprint.description}
                          </p>
                        </div>

                        {/* Metadata Rozetleri */}
                        <div className="flex flex-wrap items-center gap-2 pt-1">
                          <span className="rounded-[var(--radius-control)] bg-surface-700 px-2 py-0.5 text-[11px] font-semibold text-content-secondary">
                            {blueprint.tableCount} Tables
                          </span>
                          <span className="rounded-[var(--radius-control)] bg-surface-700 px-2 py-0.5 text-[11px] font-semibold text-content-secondary">
                            {blueprint.relationCount} FKs
                          </span>
                          <span className="rounded-[var(--radius-control)] bg-white/[0.05] px-2 py-0.5 text-[11px] font-mono text-content-subtle capitalize">
                            {blueprint.size}
                          </span>
                        </div>

                        {/* Alt Butonlar */}
                        <div className="flex items-center gap-2 pt-2 border-t border-surface-600/60">
                          <button
                            type="button"
                            onClick={e => {
                              e.stopPropagation();
                              setActiveBlueprint(blueprint);
                            }}
                            className="flex-1 flex items-center justify-center gap-1.5 rounded-[var(--radius-control)] bg-surface-700 px-3 py-2 text-xs font-bold text-content-primary hover:bg-surface-600 transition-colors cursor-pointer"
                          >
                            <Eye className="h-3.5 w-3.5 text-accent" />
                            <span>Inspect</span>
                          </button>
                          <button
                            type="button"
                            onClick={e => {
                              e.stopPropagation();
                              handleOpenInEditor(blueprint);
                            }}
                            className="flex-1 flex items-center justify-center gap-1.5 rounded-[var(--radius-control)] bg-accent px-3 py-2 text-xs font-bold text-surface-900 hover:bg-accent-hover transition-colors cursor-pointer"
                          >
                            <ExternalLink className="h-3.5 w-3.5" />
                            <span>Open</span>
                          </button>
                        </div>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}
          </main>
        </div>

        {/* Alt Dönüşüm Alanı */}
        <div className="flex flex-wrap items-center justify-between gap-4 rounded-[var(--radius-modal)] border border-surface-600 bg-surface-800 p-6">
          <div>
            <h3 className="text-base font-bold text-content-primary">
              Need a completely custom schema for your product?
            </h3>
            <p className="mt-1 text-xs text-content-muted">
              Describe your requirements in plain language. Namines AI generates full entity relationships, checks
              deterministic rules, and handles migration zero-drift.
            </p>
          </div>
          <Link
            href="/new"
            className="flex items-center gap-2 rounded-[var(--radius-card)] bg-accent px-5 py-2.5 text-xs font-bold text-surface-900 transition-all hover:bg-accent-hover"
          >
            <Wand2 className="h-4 w-4" />
            Create with AI Copilot
          </Link>
        </div>
      </div>

      {/* ── Detaylı İnceleme Modalı (Live Blueprint Inspector) ── */}
      {activeBlueprint && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/75 p-3 sm:p-6 backdrop-blur-sm">
          <div className="relative flex max-h-[92vh] w-full max-w-7xl flex-col overflow-hidden rounded-[var(--radius-modal)] border border-surface-600 bg-surface-900 shadow-2xl">
            {/* Modal Üst Bar */}
            <div className="flex flex-wrap items-center justify-between gap-3 border-b border-surface-600 bg-surface-800 px-5 py-3.5">
              <div className="flex items-center gap-3">
                <activeBlueprint.icon className="h-5 w-5 text-accent" />
                <div>
                  <div className="flex items-center gap-2">
                    <span className="font-mono text-[10px] font-bold uppercase tracking-wider text-accent-text">
                      BLUEPRINT INSPECTOR
                    </span>
                    <span className="text-xs text-content-muted">&middot;</span>
                    <span className="text-xs text-content-muted capitalize">{activeBlueprint.size}</span>
                  </div>
                  <h2 className="text-base font-bold text-content-primary">{activeBlueprint.label}</h2>
                </div>
              </div>

              {/* Motor Seçici & Eylemler */}
              <div className="flex flex-wrap items-center gap-2">
                <div className="flex items-center rounded-[var(--radius-control)] border border-surface-600 bg-surface-700 p-0.5">
                  {ENGINES.map(e => (
                    <button
                      key={e}
                      type="button"
                      onClick={() => setEngine(e)}
                      className={`rounded-[var(--radius-control)] px-2.5 py-1 text-[11px] font-bold transition-all cursor-pointer ${
                        engine === e
                          ? 'bg-accent text-surface-900'
                          : 'text-content-muted hover:text-content-primary'
                      }`}
                    >
                      {e}
                    </button>
                  ))}
                </div>

                <button
                  type="button"
                  onClick={() => handleOpenInEditor(activeBlueprint)}
                  className="flex items-center gap-1.5 rounded-[var(--radius-card)] bg-accent px-4 py-1.5 text-xs font-bold text-surface-900 hover:bg-accent-hover cursor-pointer"
                >
                  <ExternalLink className="h-3.5 w-3.5" />
                  <span>Open in Canvas</span>
                </button>

                <button
                  type="button"
                  onClick={() => setActiveBlueprint(null)}
                  className="rounded-[var(--radius-control)] p-1.5 text-content-muted hover:bg-surface-700 hover:text-content-primary cursor-pointer"
                  title="Close (Esc)"
                >
                  <X className="h-5 w-5" />
                </button>
              </div>
            </div>

            {/* Modal Sekmeleri */}
            <div className="flex items-center justify-between border-b border-surface-600 bg-surface-850 px-5 py-2">
              <div className="flex items-center gap-1">
                <button
                  type="button"
                  onClick={() => setModalTab('split')}
                  className={`rounded-[var(--radius-control)] px-3 py-1.5 text-xs font-bold transition-all cursor-pointer ${
                    modalTab === 'split'
                      ? 'bg-surface-700 text-content-primary'
                      : 'text-content-muted hover:text-content-primary'
                  }`}
                >
                  Split View (Workbench)
                </button>
                <button
                  type="button"
                  onClick={() => setModalTab('canvas')}
                  className={`rounded-[var(--radius-control)] px-3 py-1.5 text-xs font-bold transition-all cursor-pointer ${
                    modalTab === 'canvas'
                      ? 'bg-surface-700 text-content-primary'
                      : 'text-content-muted hover:text-content-primary'
                  }`}
                >
                  Schema ERD ({activeBlueprint.tableCount} Tables)
                </button>
                <button
                  type="button"
                  onClick={() => setModalTab('findings')}
                  className={`flex items-center gap-1.5 rounded-[var(--radius-control)] px-3 py-1.5 text-xs font-bold transition-all cursor-pointer ${
                    modalTab === 'findings'
                      ? 'bg-surface-700 text-content-primary'
                      : 'text-content-muted hover:text-content-primary'
                  }`}
                >
                  <span>Rule Engine Proof</span>
                  {proving ? (
                    <Loader2 className="h-3 w-3 animate-spin" />
                  ) : (
                    <span className="rounded-[var(--radius-control)] bg-white/[0.08] px-1.5 py-0.2 text-[10px]">
                      {(counts.errors || 0) + (counts.warnings || 0) + (counts.infos || 0)}
                    </span>
                  )}
                </button>
                <button
                  type="button"
                  onClick={() => setModalTab('sql')}
                  className={`rounded-[var(--radius-control)] px-3 py-1.5 text-xs font-bold transition-all cursor-pointer ${
                    modalTab === 'sql'
                      ? 'bg-surface-700 text-content-primary'
                      : 'text-content-muted hover:text-content-primary'
                  }`}
                >
                  Compiled {engine} DDL
                </button>
              </div>

              <div className="hidden sm:flex items-center gap-2 text-[11px] text-content-subtle">
                <span>{activeBlueprint.schema.tables.length} tables</span>
                <span>&middot;</span>
                <span>{activeBlueprint.schema.relations.length} relations</span>
              </div>
            </div>

            {/* Modal Gövdesi */}
            <div className="flex-1 overflow-hidden p-4">
              {/* Sekme 1: Yalnızca Tuval */}
              {modalTab === 'canvas' && (
                <div className="h-[65vh] w-full overflow-hidden rounded-[var(--radius-card)] border border-surface-600 bg-surface-800">
                  <DemoCanvas nodes={nodes} edges={edges} resetKey={activeBlueprint.key} />
                </div>
              )}

              {/* Sekme 2: Kural Motoru Buluntuları */}
              {modalTab === 'findings' && (
                <div className="flex h-[65vh] flex-col overflow-hidden rounded-[var(--radius-card)] border border-surface-600 bg-surface-800">
                  <div className="grid grid-cols-3 gap-px border-b border-surface-600 bg-surface-600 text-center">
                    <div className="bg-surface-800 py-3">
                      <p className="text-xl font-bold text-danger">{counts.errors}</p>
                      <p className="text-[10px] font-semibold uppercase tracking-wide text-content-subtle">Errors</p>
                    </div>
                    <div className="bg-surface-800 py-3">
                      <p className="text-xl font-bold text-warning-text">{counts.warnings}</p>
                      <p className="text-[10px] font-semibold uppercase tracking-wide text-content-subtle">Warnings</p>
                    </div>
                    <div className="bg-surface-800 py-3">
                      <p className="text-xl font-bold text-content-primary">{counts.infos}</p>
                      <p className="text-[10px] font-semibold uppercase tracking-wide text-content-subtle">Notes</p>
                    </div>
                  </div>

                  <div className="flex-1 space-y-2 overflow-y-auto p-4">
                    {lint && lint.length === 0 && (
                      <div className="flex items-center gap-2 rounded-[var(--radius-card)] bg-surface-700/60 p-4 text-xs text-content-secondary">
                        <CheckCircle2 className="h-5 w-5 text-success-text" />
                        <span>All rules passed with 0 errors or warnings. Proven by the deterministic checker.</span>
                      </div>
                    )}
                    {grouped.map((g, i) => {
                      const Icon = g.severity === 'info' ? Info : AlertTriangle;
                      const color =
                        g.severity === 'error'
                          ? 'text-danger'
                          : g.severity === 'warning'
                          ? 'text-warning-text'
                          : 'text-content-muted';
                      return (
                        <div
                          key={i}
                          className="flex items-start gap-2.5 rounded-[var(--radius-control)] bg-surface-700 p-3 text-xs leading-relaxed text-content-secondary"
                        >
                          <Icon className={`mt-0.5 h-4 w-4 shrink-0 ${color}`} />
                          <div className="min-w-0 flex-1">
                            <span>{g.message}</span>
                            {g.count > 1 && (
                              <span className="ml-2 rounded-[var(--radius-control)] bg-white/[0.08] px-1.5 py-0.5 text-[10px] font-bold text-content-primary">
                                ×{g.count}
                              </span>
                            )}
                            {g.count > 1 && g.subjects.length > 0 && (
                              <span className="mt-1 block text-[11px] text-content-subtle">
                                {g.subjects.join(', ')}
                                {g.count > g.subjects.length ? ', …' : ''}
                              </span>
                            )}
                          </div>
                        </div>
                      );
                    })}
                  </div>
                </div>
              )}

              {/* Sekme 3: Derlenmiş DDL */}
              {modalTab === 'sql' && (
                <div className="flex h-[65vh] flex-col overflow-hidden rounded-[var(--radius-card)] border border-surface-600 bg-surface-800">
                  <div className="flex items-center justify-between border-b border-surface-600 px-4 py-2.5">
                    <span className="font-mono text-xs font-semibold text-content-secondary">
                      Compiled {engine} DDL &middot; Deterministic AST
                    </span>
                    <button
                      type="button"
                      onClick={handleCopySql}
                      className="flex items-center gap-1.5 rounded-[var(--radius-control)] bg-surface-700 px-3 py-1.5 text-xs font-bold text-content-primary hover:bg-surface-600 cursor-pointer"
                    >
                      {copiedSql ? (
                        <>
                          <Check className="h-3.5 w-3.5 text-success-text" />
                          <span>Copied!</span>
                        </>
                      ) : (
                        <>
                          <Copy className="h-3.5 w-3.5" />
                          <span>Copy SQL</span>
                        </>
                      )}
                    </button>
                  </div>
                  <pre className="flex-1 overflow-auto p-4 font-mono text-xs leading-relaxed text-content-secondary">
                    {sql ?? (proving ? 'Compiling DDL...' : 'No SQL output generated')}
                  </pre>
                </div>
              )}

              {/* Sekme 4: Split View (Çalışma Tezgahı) */}
              {modalTab === 'split' && (
                <div className="grid h-[65vh] grid-cols-1 gap-4 lg:grid-cols-12">
                  {/* Sol: İnteraktif Tuval (7 Kolon) */}
                  <div className="h-full overflow-hidden rounded-[var(--radius-card)] border border-surface-600 bg-surface-800 lg:col-span-7">
                    <DemoCanvas nodes={nodes} edges={edges} resetKey={activeBlueprint.key} />
                  </div>

                  {/* Sağ: Kanıt ve DDL (5 Kolon) */}
                  <div className="flex h-full flex-col gap-4 overflow-hidden lg:col-span-5">
                    {/* Üst Kısım: Rule Engine Findings */}
                    <div className="flex flex-1 flex-col overflow-hidden rounded-[var(--radius-card)] border border-surface-600 bg-surface-800">
                      <div className="flex items-center justify-between border-b border-surface-600 px-4 py-2.5">
                        <span className="text-xs font-bold text-content-primary">Rule Engine Findings</span>
                        {proving && <Loader2 className="h-3.5 w-3.5 animate-spin text-accent" />}
                      </div>

                      <div className="flex-1 space-y-2 overflow-y-auto p-3">
                        {lint && lint.length === 0 && (
                          <div className="flex items-center gap-2 p-2 text-xs text-content-muted">
                            <CheckCircle2 className="h-4 w-4 text-success-text" />
                            <span>100% verified schema &middot; 0 rule violations</span>
                          </div>
                        )}
                        {grouped.slice(0, 5).map((g, i) => {
                          const Icon = g.severity === 'info' ? Info : AlertTriangle;
                          const color =
                            g.severity === 'error'
                              ? 'text-danger'
                              : g.severity === 'warning'
                              ? 'text-warning-text'
                              : 'text-content-muted';
                          return (
                            <div
                              key={i}
                              className="flex items-start gap-2 rounded-[var(--radius-control)] bg-surface-700 p-2 text-[11px] text-content-secondary"
                            >
                              <Icon className={`mt-0.5 h-3.5 w-3.5 shrink-0 ${color}`} />
                              <span className="min-w-0 flex-1 truncate">{g.message}</span>
                              {g.count > 1 && (
                                <span className="font-mono text-[10px] text-content-muted">×{g.count}</span>
                              )}
                            </div>
                          );
                        })}
                      </div>
                    </div>

                    {/* Alt Kısım: Compiled SQL */}
                    <div className="flex flex-1 flex-col overflow-hidden rounded-[var(--radius-card)] border border-surface-600 bg-surface-800">
                      <div className="flex items-center justify-between border-b border-surface-600 px-4 py-2.5">
                        <span className="font-mono text-xs font-semibold text-content-secondary">
                          {engine} DDL
                        </span>
                        <button
                          type="button"
                          onClick={handleCopySql}
                          className="flex items-center gap-1 text-[11px] font-bold text-accent hover:underline cursor-pointer"
                        >
                          {copiedSql ? <Check className="h-3 w-3" /> : <Copy className="h-3 w-3" />}
                          <span>{copiedSql ? 'Copied' : 'Copy'}</span>
                        </button>
                      </div>
                      <pre className="flex-1 overflow-auto p-3 font-mono text-[11px] leading-relaxed text-content-secondary">
                        {sql ?? (proving ? 'Compiling...' : '—')}
                      </pre>
                    </div>
                  </div>
                </div>
              )}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

export default function DemoPage() {
  return (
    <Suspense fallback={<div className="min-h-screen bg-surface-900" />}>
      <DemoContent />
    </Suspense>
  );
}
