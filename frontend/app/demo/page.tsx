'use client';

import { Suspense, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import dynamic from 'next/dynamic';
import { useRouter, useSearchParams } from 'next/navigation';
import type { Node, Edge } from '@xyflow/react';
import { AlertTriangle, CheckCircle2, Info, Loader2, ShieldCheck, Wand2 } from 'lucide-react';
import { schemaToFlow } from '../../lib/schemaToFlow';
import { TEMPLATES, TEMPLATE_SIZES, templatesOfSize, type TemplateSize } from '../../lib/templates';
import { schemaService } from '../../services/api';
import { useSchemaStore } from '../../store/useSchemaStore';

// Tuval yalnızca tarayıcıda render ediliyor: ızgara rengini CSS değişkeninden
// okuyor ve sunucuda CSS yok — sunucuda çizilirse istemcideki gerçek renkle
// uyuşmuyor (hydration uyarısı).
const DemoCanvas = dynamic(() => import('../../components/landing/DemoCanvas'), {
  ssr: false,
  loading: () => <div className="h-full w-full" />,
});

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

/**
 * Aynı kuralın farklı tablolarda tekrarını TEK satırda topla.
 *
 * 25 tablolu bir şemada "Table 'x' should ideally be PascalCase" yirmi beş kez
 * yan yana yazılıyordu. Bu, kural motorunu tek bir şey söyleyen bir araç gibi
 * gösteriyordu — oysa asıl anlatılmak istenen, hangi FARKLI kuralların
 * çalıştığı. Sayı korunuyor, tekrar korunmuyor.
 */
function groupFindings(messages: LintMessage[]) {
  const groups = new Map<string, { severity: string; message: string; count: number; subjects: string[] }>();

  for (const m of messages) {
    // Tırnak içindeki tanımlayıcılar (tablo/kolon adları) kuralın kimliğinden
    // çıkarılıyor; geriye kuralın kendisi kalıyor.
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

function DemoContent() {
  const router = useRouter();
  // Şablon adresten geliyor: iniş sayfasındaki galeriden tıklanan kart, demoyu
  // O şablonla açmalı. Bilinmeyen bir anahtar sessizce ilk şablona düşüyor —
  // bozuk bir bağlantı ziyaretçiye boş sayfa göstermemeli.
  const searchParams = useSearchParams();
  const requested = searchParams?.get('template') ?? '';
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const setDbType = useSchemaStore(s => s.setDbType);

  const initialKey = TEMPLATES.some(t => t.key === requested)
    ? requested
    : (TEMPLATES[0]?.key ?? '');
  const [templateKey, setTemplateKey] = useState(initialKey);

  // Ölçek sekmesi, açılıştaki şablonun kendi ölçeğinden başlıyor: bir bağlantı
  // `?template=erp` ile geliyorsa ziyaretçi "Enterprise" sekmesinde olmalı,
  // yoksa seçili şablonu listede göremez.
  const [sizeTab, setSizeTab] = useState<TemplateSize>(
    TEMPLATES.find(t => t.key === initialKey)?.size ?? 'standard',
  );
  const visibleTemplates = templatesOfSize(sizeTab);
  const [engine, setEngine] = useState<Engine>('PostgreSQL');

  /**
   * Kanıt katmanının sonucu, ANAHTARIYLA birlikte tek bir durumda.
   *
   * Önce dört ayrı durum (lint / sql / proving / failed) vardı ve efekt bunları
   * senkron olarak set ediyordu — her şablon değişiminde zincirleme render.
   * Sonucu istendiği isteğin anahtarıyla saklamak, "hâlâ çalışıyor mu"
   * sorusunu bir durum değil TÜRETİLMİŞ bir değer yapıyor ve geç dönen eski
   * bir cevabın yenisinin üzerine yazmasını da imkânsız kılıyor.
   */
  const [result, setResult] = useState<{
    key: string;
    lint: LintMessage[] | null;
    sql: string | null;
  } | null>(null);

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

  // İstek kimliği: hangi şablon + hangi motor. Sonuç bununla eşleşiyor.
  const requestKey = `${template?.key ?? ''}|${engine}`;

  // Kanıt katmanı: gerçek linter + gerçek DDL üreticisi.
  //
  // Durum YALNIZCA sözün içinde set ediliyor, efekt gövdesinde değil — böylece
  // efekt senkron bir render zinciri başlatmıyor.
  useEffect(() => {
    if (!template) return;
    let cancelled = false;

    void Promise.all([
      schemaService.lintSchema(template.schema),
      schemaService.compileSql(template.schema, engine),
    ])
      .then(([lintResult, ddl]) => {
        if (!cancelled) setResult({ key: requestKey, lint: lintResult?.messages ?? [], sql: ddl ?? '' });
      })
      .catch(() => {
        // Sunucu ulaşılamazsa uydurma bir çıktı GÖSTERİLMİYOR. Demonun tek
        // değeri gerçek olması; sahte bir "0 hata" göstermek, ürünün en çok
        // güvenilmesi gereken iddiasını ilk temasta yalanlamak olurdu.
        if (!cancelled) setResult({ key: requestKey, lint: null, sql: null });
      });

    return () => { cancelled = true; };
  }, [template, engine, requestKey]);

  const settled = result?.key === requestKey ? result : null;
  const proving = settled === null;
  const failed = settled !== null && settled.lint === null;
  const lint = settled?.lint ?? null;
  const sql = settled?.sql ?? null;

  const openInEditor = () => {
    if (!template) return;
    loadFromSchema(template.schema);
    setDbType(engine as never);
    router.push('/canvas');
  };

  const grouped = useMemo(() => groupFindings(lint ?? []), [lint]);

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
      <div className="mx-auto max-w-[var(--w-wide)] space-y-6">

        {/* Baslik */}
        <header className="space-y-2">
          <div className="inline-flex items-center gap-2 rounded-[var(--radius-control)] bg-white/[0.06] px-3 py-1 text-[11px] font-semibold text-content-secondary">
            <ShieldCheck className="h-3.5 w-3.5" />
            No account needed &middot; nothing is sent to an AI
          </div>
          <h1 className="text-2xl font-bold text-content-primary">See what Namines actually checks</h1>
          <p className="max-w-2xl text-sm leading-relaxed text-content-muted">
            Pick a schema and a database engine. The findings and the SQL below come from the same
            rule engine and the same DDL generator the paid product runs — not a recording.
          </p>
        </header>

        {/*
          Seçiciler üç katmanlı: ölçek → şablon → motor.

          20 şablon + 6 motor tek bir sarmalayan listede, 375px'te SEKİZ SATIR
          düğme demekti: ziyaretçi asıl içeriğe ulaşmadan ~700px kaydırıyordu.
          Ölçek sekmesi listeyi en fazla 12'ye indiriyor, yatay kaydırma da
          kalanı tek satırda tutuyor — mobilde sarmak yerine kaydırmak, düğme
          duvarını tamamen ortadan kaldırıyor.
        */}
        <div className="space-y-2.5">
          <div className="flex flex-wrap items-center gap-2">
            {TEMPLATE_SIZES.map(tier => {
              const count = templatesOfSize(tier.id).length;
              if (count === 0) return null;
              return (
                <button
                  key={tier.id}
                  type="button"
                  onClick={() => {
                    setSizeTab(tier.id);
                    // Sekme değişince o gruptaki ilk şablona geçiliyor: aksi
                    // hâlde listede görünmeyen bir şablon seçili kalırdı.
                    const first = templatesOfSize(tier.id)[0];
                    if (first) setTemplateKey(first.key);
                  }}
                  title={tier.blurb}
                  className={`rounded-[var(--radius-card)] px-3.5 min-h-11 text-xs font-bold transition-all cursor-pointer ${
                    sizeTab === tier.id
                      ? 'bg-content-primary text-surface-900'
                      : 'bg-surface-800 text-content-muted hover:text-content-primary'
                  }`}
                >
                  {tier.label}
                  <span className="ml-1.5 opacity-60">{count}</span>
                </button>
              );
            })}
          </div>

          {/* Mobilde yatay kaydırma, sm'den itibaren sarma. */}
          <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:overflow-visible sm:px-0">
            <div className="flex w-max gap-2 sm:w-auto sm:flex-wrap">
              {visibleTemplates.map(t => (
                <button
                  key={t.key}
                  type="button"
                  onClick={() => setTemplateKey(t.key)}
                  className={`shrink-0 rounded-[var(--radius-card)] border px-3 min-h-11 text-xs font-semibold transition-all cursor-pointer ${
                    t.key === templateKey
                      ? 'border-content-primary/40 bg-white/[0.08] text-content-primary'
                      : 'border-surface-500 bg-surface-800 text-content-muted hover:text-content-primary'
                  }`}
                >
                  {t.label}
                </button>
              ))}
            </div>
          </div>

          <div className="-mx-4 overflow-x-auto px-4 sm:mx-0 sm:overflow-visible sm:px-0">
            <div className="flex w-max items-center gap-2 sm:w-auto sm:flex-wrap">
              <span className="text-[10px] font-semibold uppercase tracking-wide text-content-subtle">
                Engine
              </span>
              {ENGINES.map(e => (
                <button
                  key={e}
                  type="button"
                  onClick={() => setEngine(e)}
                  className={`shrink-0 rounded-[var(--radius-control)] px-3 min-h-11 text-[11px] font-bold transition-all cursor-pointer ${
                    e === engine
                      ? 'bg-content-primary text-surface-900'
                      : 'bg-surface-800 text-content-muted hover:text-content-primary'
                  }`}
                >
                  {e}
                </button>
              ))}
            </div>
          </div>
        </div>

        {/* Tuval TAM GENİŞLİK.
            Yan yana yerleşimde 25 tablo, 680px'lik bir panele sığması için
            %20 yakınlaştırmaya iniyordu: tablolar 60px genişliğinde birer
            lekeye dönüşüyor, ekran boş görünüyordu. Gerçek boyutlu bir şema,
            gerçek boyutlu bir alan ister. */}
        <div>
          <div className="h-[340px] sm:h-[440px] lg:h-[560px] overflow-hidden rounded-[var(--radius-modal)] border border-surface-500 bg-surface-800">
            <DemoCanvas nodes={nodes} edges={edges} resetKey={template?.key ?? ''} />
          </div>
          <p className="mt-2 text-[11px] text-content-subtle">
            {template ? `${template.schema.tables.length} tables · ${template.schema.relations.length} relationships` : ''}
            {' — drag to pan, use the controls to zoom.'}
          </p>
        </div>

        <div className="grid gap-4 lg:grid-cols-2">

          {/* Kanit paneli */}
          <div className="flex max-h-[420px] flex-col overflow-hidden rounded-[var(--radius-modal)] border border-surface-500 bg-surface-800 lg:h-[420px]">
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
                  {grouped.map((g, i) => {
                    const Icon = g.severity === 'info' ? Info : AlertTriangle;
                    const color = g.severity === 'error'
                      ? 'text-danger'
                      : g.severity === 'warning'
                      ? 'text-warning-text'
                      : 'text-content-muted';
                    return (
                      <div key={i} className="flex items-start gap-2 rounded-[var(--radius-control)] bg-surface-700 p-2.5 text-xs leading-relaxed text-content-secondary">
                        <Icon className={`mt-0.5 h-3.5 w-3.5 shrink-0 ${color}`} />
                        <span className="min-w-0">
                          {g.message}
                          {g.count > 1 && (
                            <span className="ml-1.5 rounded-[var(--radius-control)] bg-white/[0.08] px-1.5 py-0.5 text-[10px] font-bold text-content-primary">
                              ×{g.count}
                            </span>
                          )}
                          {g.count > 1 && g.subjects.length > 0 && (
                            <span className="mt-1 block text-[10px] text-content-subtle">
                              {g.subjects.join(', ')}{g.count > g.subjects.length ? ', …' : ''}
                            </span>
                          )}
                        </span>
                      </div>
                    );
                  })}
                </div>
              </>
            )}
          </div>

          {/* Uretilen SQL */}
          <div className="flex h-[360px] flex-col overflow-hidden rounded-[var(--radius-modal)] border border-surface-500 bg-surface-800 lg:h-[420px]">
            {/* Başlık ve rozet sarılıyor: 375px'te ikisi tek satıra sığmıyor ve
                rozet başlığın üstüne biniyordu. */}
            <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1 border-b border-surface-600 px-4 py-3">
              <span className="text-sm font-semibold text-content-primary">Generated {engine} DDL</span>
              <span className="text-[10px] font-semibold uppercase tracking-wide text-content-subtle">
                deterministic &middot; no model involved
              </span>
            </div>
            <pre className="flex-1 overflow-auto p-4 font-mono text-[11px] leading-relaxed text-content-secondary">
              {sql ?? (proving ? 'Generating…' : '—')}
            </pre>
          </div>
        </div>

        {/* Donusum */}
        <div className="flex flex-wrap items-center justify-between gap-3 rounded-[var(--radius-modal)] border border-surface-500 bg-surface-800 px-5 py-4">
          <p className="text-sm text-content-secondary">
            Want this from a sentence instead of a template? That part uses AI — and needs an account.
          </p>
          <div className="flex gap-2">
            <button
              type="button"
              onClick={openInEditor}
              className="flex items-center gap-2 rounded-[var(--radius-card)] bg-surface-600 px-4 py-2.5 text-xs font-bold text-content-secondary transition-all hover:text-content-primary cursor-pointer"
            >
              Open this in the editor
            </button>
            <Link
              href="/"
              className="flex items-center gap-2 rounded-[var(--radius-card)] bg-content-primary px-4 py-2.5 text-xs font-bold text-surface-900 transition-all hover:bg-content-secondary"
            >
              <Wand2 className="h-3.5 w-3.5" />
              Describe your own
            </Link>
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
