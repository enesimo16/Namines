'use client';

import { Suspense, useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  ReactFlow,
  Background,
  BackgroundVariant,
  Controls,
  type Node,
  type Edge,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { AlertTriangle, CheckCircle2, Info, Loader2, ShieldCheck, Wand2 } from 'lucide-react';
import TableNode from '../../components/canvas/nodes/TableNode';
import { schemaToFlow } from '../../lib/schemaToFlow';
import { TEMPLATES } from '../../lib/templates';
import { schemaService } from '../../services/api';
import { useSchemaStore } from '../../store/useSchemaStore';
import { token as designToken } from '../../lib/designTokens';
import { DatabaseSchema } from '../../types/schema';

const nodeTypes = { tableNode: TableNode };

/**
 * Girişsiz canlı demo.
 *
 * **Neden AI kullanmıyor:** demo, hesabı olmayan bir ziyaretçiye açık. AI
 * çağrısı gerçek para harcıyor ve kimliksiz bir uçtan sınırsız
 * tetiklenebilirdi — ücretsiz havuzu bir ziyaretçi akışı tek başına
 * bitirebilirdi (bkz. second-phase/16-KOTA-VE-MALIYET.md).
 *
 * **Ama demo SAHTE de değil.** Şema hazır (şablon), fakat ekranda görünen her
 * bulgu ve her satır SQL, ürünün gerçek uçlarından geliyor: `POST /api/lint` ve
 * `POST /api/compile/sql`. İkisi de kimliksiz, ikisi de deterministik — yani
 * ziyaretçinin gördüğü şey, ödeyen bir müşterinin gördüğünün aynısı. Ürünün
 * asıl iddiası ("AI üretir, kural motoru kanıtlar") tam olarak burada
 * gösteriliyor: kanıtlayan taraf demoda da gerçekten çalışıyor.
 */

const ENGINES = ['PostgreSQL', 'MySQL', 'MSSQL', 'SQLite', 'Oracle', 'MariaDB'] as const;
type Engine = (typeof ENGINES)[number];

interface LintMessage {
  severity: number | string;
  message: string;
  tableId?: string | null;
  columnId?: string | null;
}

/** Sunucu enum'u JSON'da sayı ya da ad olarak gelebiliyor; ikisini de karşıla. */
function severityOf(raw: number | string): 'error' | 'warning' | 'info' {
  const value = typeof raw === 'string' ? raw.toLowerCase() : raw;
  if (value === 2 || value === 'error') return 'error';
  if (value === 1 || value === 'warning') return 'warning';
  return 'info';
}

function DemoContent() {
  const router = useRouter();
  // Şablon adresten geliyor: iniş sayfasındaki galeriden tıklanan kart, demoyu
  // O şablonla açmalı. Bilinmeyen bir anahtar sessizce ilk şablona düşüyor —
  // bozuk bir bağlantı ziyaretçiye boş sayfa göstermemeli.
  const searchParams = useSearchParams();
  const requested = searchParams?.get('template') ?? '';
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const setDbType = useSchemaStore(s => s.setDbType);

  const [templateKey, setTemplateKey] = useState(
    TEMPLATES.some(t => t.key === requested) ? requested : (TEMPLATES[0]?.key ?? ''),
  );
  const [engine, setEngine] = useState<Engine>('PostgreSQL');

  const [lint, setLint] = useState<LintMessage[] | null>(null);
  const [sql, setSql] = useState<string | null>(null);
  const [proving, setProving] = useState(false);
  const [failed, setFailed] = useState(false);
  // Izgara rengi CSS değişkeninden okunuyor; sunucuda CSS yok, orada
  // 'transparent' dönüyor ve istemcideki gerçek renkle uyuşmuyordu (hydration
  // uyarısı). Tuval, renk okunabilir hâle geldikten SONRA çiziliyor.
  const [gridColor, setGridColor] = useState<string | null>(null);
  useEffect(() => setGridColor(designToken('--color-line-solid-strong')), []);

  const template = useMemo(
    () => TEMPLATES.find(t => t.key === templateKey) ?? TEMPLATES[0],
    [templateKey],
  );

  /**
   * Düğümler DURUM değil TÜRETİLMİŞ değer.
   *
   * Önce `useNodesState` + bir efektle kuruluyordu; bu, şablon değiştiğinde
   * tuvalin bir render boyunca ESKİ düğümlerle yeniden kurulması demekti ve
   * `fitView` o eski şemaya göre çalışıp yenisinin bir kısmını görünüm
   * penceresinin dışında bırakıyordu. Demo salt okunur — düğümleri değiştiren
   * hiçbir etkileşim yok, dolayısıyla durum tutmanın da bir gerekçesi yok.
   */
  const { nodes, edges } = useMemo(() => {
    if (!template) return { nodes: [] as Node[], edges: [] as Edge[] };
    const { nodes: flowNodes, edges: flowEdges } = schemaToFlow(template.schema);
    return {
      nodes: flowNodes.map(n => ({ ...n, draggable: false, selectable: false, connectable: false })),
      edges: flowEdges.map(e => ({ ...e, deletable: false })),
    };
  }, [template]);

  // Kanıt katmanı: gerçek linter + gerçek DDL üreticisi.
  const prove = useCallback(async (schema: DatabaseSchema, target: Engine) => {
    setProving(true);
    setFailed(false);
    try {
      const [lintResult, ddl] = await Promise.all([
        schemaService.lintSchema(schema),
        schemaService.compileSql(schema, target),
      ]);
      setLint(lintResult?.messages ?? []);
      setSql(ddl ?? '');
    } catch {
      // Sunucu ulaşılamazsa uydurma bir çıktı GÖSTERİLMİYOR. Demonun tek değeri
      // gerçek olması; sahte bir "0 hata" göstermek, ürünün en çok güvenilmesi
      // gereken iddiasını ilk temasta yalanlamak olurdu.
      setLint(null);
      setSql(null);
      setFailed(true);
    } finally {
      setProving(false);
    }
  }, []);

  useEffect(() => {
    if (template) void prove(template.schema, engine);
  }, [template, engine, prove]);

  const openInEditor = () => {
    if (!template) return;
    loadFromSchema(template.schema);
    setDbType(engine as never);
    router.push('/canvas');
  };

  const counts = useMemo(() => {
    const messages = lint ?? [];
    return {
      errors: messages.filter(m => severityOf(m.severity) === 'error').length,
      warnings: messages.filter(m => severityOf(m.severity) === 'warning').length,
      infos: messages.filter(m => severityOf(m.severity) === 'info').length,
    };
  }, [lint]);

  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 px-4 py-8">
      <div className="mx-auto max-w-6xl space-y-6">

        {/* Baslik */}
        <header className="space-y-2">
          <div className="inline-flex items-center gap-2 rounded-lg bg-white/[0.06] px-3 py-1 text-[11px] font-semibold text-content-secondary">
            <ShieldCheck className="h-3.5 w-3.5" />
            No account needed &middot; nothing is sent to an AI
          </div>
          <h1 className="text-2xl font-bold text-content-primary">See what Namines actually checks</h1>
          <p className="max-w-2xl text-sm leading-relaxed text-content-muted">
            Pick a schema and a database engine. The findings and the SQL below come from the same
            rule engine and the same DDL generator the paid product runs — not a recording.
          </p>
        </header>

        {/* Seciciler */}
        <div className="flex flex-wrap items-center gap-2">
          {TEMPLATES.map(t => (
            <button
              key={t.key}
              type="button"
              onClick={() => setTemplateKey(t.key)}
              className={`flex items-center gap-2 rounded-xl border px-3 py-2 text-xs font-semibold transition-all cursor-pointer ${
                t.key === templateKey
                  ? 'border-content-primary/40 bg-white/[0.08] text-content-primary'
                  : 'border-surface-500 bg-surface-800 text-content-muted hover:text-content-primary'
              }`}
            >
              <span className="text-base leading-none">{t.emoji}</span>
              {t.label}
            </button>
          ))}
          <span className="mx-1 h-6 w-px bg-surface-500" />
          {ENGINES.map(e => (
            <button
              key={e}
              type="button"
              onClick={() => setEngine(e)}
              className={`rounded-lg px-2.5 py-1.5 text-[11px] font-bold transition-all cursor-pointer ${
                e === engine
                  ? 'bg-content-primary text-surface-900'
                  : 'bg-surface-800 text-content-muted hover:text-content-primary'
              }`}
            >
              {e}
            </button>
          ))}
        </div>

        <div className="grid gap-4 lg:grid-cols-5">

          {/* Canvas */}
          <div className="lg:col-span-3 h-[420px] overflow-hidden rounded-2xl border border-surface-500 bg-surface-800">
            {/* key: şablon değişince tuval yeniden kuruluyor. `fitView` yalnızca
                ilk kurulumda çalışıyor; anahtar olmadan yeni şemanın bir kısmı
                eski görünüm penceresinin dışında kalıyordu. */}
            {gridColor && (
            <ReactFlow
              key={template?.key}
              nodes={nodes}
              edges={edges}
              nodeTypes={nodeTypes}
              nodesDraggable={false}
              nodesConnectable={false}
              elementsSelectable={false}
              deleteKeyCode={null}
              fitView
              fitViewOptions={{ padding: 0.15 }}
            >
              <Background variant={BackgroundVariant.Dots} gap={24} size={1} color={gridColor} />
              <Controls showInteractive={false} />
            </ReactFlow>
            )}
          </div>

          {/* Kanit paneli */}
          <div className="lg:col-span-2 flex h-[420px] flex-col overflow-hidden rounded-2xl border border-surface-500 bg-surface-800">
            <div className="flex items-center justify-between border-b border-surface-600 px-4 py-3">
              <span className="text-sm font-semibold text-content-primary">Rule engine findings</span>
              {proving && <Loader2 className="h-4 w-4 animate-spin text-content-muted" />}
            </div>

            {failed && (
              <p className="px-4 py-6 text-xs leading-relaxed text-content-muted">
                The checker could not be reached, so nothing is shown here. We would rather show you
                nothing than a result we did not actually compute.
              </p>
            )}

            {!failed && lint && (
              <>
                <div className="grid grid-cols-3 gap-px border-b border-surface-600 bg-surface-600 text-center">
                  {[
                    { label: 'Errors', value: counts.errors },
                    { label: 'Warnings', value: counts.warnings },
                    { label: 'Notes', value: counts.infos },
                  ].map(stat => (
                    <div key={stat.label} className="bg-surface-800 py-2.5">
                      <p className="text-lg font-bold text-content-primary">{stat.value}</p>
                      <p className="text-[10px] font-semibold uppercase tracking-wide text-content-subtle">{stat.label}</p>
                    </div>
                  ))}
                </div>

                <div className="flex-1 space-y-2 overflow-y-auto p-4">
                  {lint.length === 0 && (
                    <div className="flex items-start gap-2 text-xs text-content-muted">
                      <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-success-text" />
                      <span>Every rule passed on this schema. The same checks run on yours.</span>
                    </div>
                  )}
                  {lint.map((m, i) => {
                    const severity = severityOf(m.severity);
                    const Icon = severity === 'info' ? Info : AlertTriangle;
                    const color = severity === 'error'
                      ? 'text-danger'
                      : severity === 'warning'
                      ? 'text-warning-text'
                      : 'text-content-muted';
                    return (
                      <div key={i} className="flex items-start gap-2 rounded-lg bg-surface-700 p-2.5 text-xs leading-relaxed text-content-secondary">
                        <Icon className={`mt-0.5 h-3.5 w-3.5 shrink-0 ${color}`} />
                        <span>{m.message}</span>
                      </div>
                    );
                  })}
                </div>
              </>
            )}
          </div>
        </div>

        {/* Uretilen SQL */}
        <div className="overflow-hidden rounded-2xl border border-surface-500 bg-surface-800">
          <div className="flex items-center justify-between border-b border-surface-600 px-4 py-3">
            <span className="text-sm font-semibold text-content-primary">Generated {engine} DDL</span>
            <span className="text-[10px] font-semibold uppercase tracking-wide text-content-subtle">
              deterministic &middot; no model involved
            </span>
          </div>
          <pre className="max-h-72 overflow-auto p-4 font-mono text-[11px] leading-relaxed text-content-secondary">
            {sql ?? (proving ? 'Generating…' : '—')}
          </pre>
        </div>

        {/* Donusum */}
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-surface-500 bg-surface-800 px-5 py-4">
          <p className="text-sm text-content-secondary">
            Want this from a sentence instead of a template? That part uses AI — and needs an account.
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={openInEditor}
              className="flex items-center gap-2 rounded-xl bg-surface-600 px-4 py-2.5 text-xs font-bold text-content-secondary transition-all hover:text-content-primary cursor-pointer"
            >
              Open this in the editor
            </button>
            <a
              href="/"
              className="flex items-center gap-2 rounded-xl bg-content-primary px-4 py-2.5 text-xs font-bold text-surface-900 transition-all hover:bg-content-secondary"
            >
              <Wand2 className="h-3.5 w-3.5" />
              Describe your own
            </a>
          </div>
        </div>
      </div>
    </div>
  );
}

/**
 * `useSearchParams` Suspense sınırı istiyor — sınır olmadan sayfanın tamamı
 * istemci tarafında render edilmeye zorlanır ve statik HTML üretilemez.
 */
export default function DemoPage() {
  return (
    <Suspense fallback={<div className="min-h-[calc(100vh-56px)] bg-surface-900" />}>
      <DemoContent />
    </Suspense>
  );
}
