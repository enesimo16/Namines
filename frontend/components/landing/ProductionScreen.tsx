'use client';

import { useEffect, useRef, useSyncExternalStore } from 'react';
import { Check, Loader2, AlertTriangle, X, ListTodo } from 'lucide-react';
import { AgentStepEvent, AgentResultEvent } from '../../lib/sseSchemaStream';

interface Props {
  steps: AgentStepEvent[];
  /** Akış hâlâ devam ediyor mu — false olunca "kapat" görünür, otomatik kapanmaz. */
  isRunning: boolean;
  /**
   * Hat bittiğinde dönen özet; akış sürerken null.
   *
   * <b>Neden gösterilmek ZORUNDA:</b> sunucu kalan bulguları bilerek
   * döndürüyor ("çalışıyor gibi görünen bir şema, hiç vermemekten kötüdür").
   * Bunu burada yutmak o kararı sessizce geri almak olurdu — kullanıcı bozuk
   * şemayı ancak veritabanı reddedince öğrenirdi.
   */
  summary: AgentResultEvent['agent'] | null;
  onClose: () => void;
}

const KIND_ICON: Record<AgentStepEvent['kind'], typeof Check> = {
  plan: ListTodo,
  draft: Loader2,
  inspect: Loader2,
  finding: AlertTriangle,
  repair: Loader2,
  clean: Check,
};

/**
 * Üretim ekranı — bir yükleniyor çarkı değil, hattın gerçekte ne yaptığının
 * canlı raporu.
 *
 * <b>Neden var:</b> ürünün en özgün tarafı görünmezdi. AI şema üretiyor,
 * sonra kural motoru + gerçek DDL derleyicisi denetleyip düzelttiriyor —
 * kullanıcı bunların hiçbirini görmüyordu, sadece sonucu görüyordu ve o da
 * herhangi bir "AI ile şema yap" aracından farksız duruyordu. Bu ekran,
 * "biz de AI kullanıyoruz"u "biz kanıtlıyoruz"a çeviriyor.
 * bkz. second-phase/04-LOADING-EKRANI.md
 */
const REDUCED_MOTION_QUERY = '(prefers-reduced-motion: reduce)';

/**
 * Kullanicinin "hareketi azalt" tercihi.
 *
 * **Neden `useSyncExternalStore`, efekt + `useState` DEGIL:** eski kod ilk
 * render'da `false` donup efektte duzeltiyordu. Tercihi acik olan kullanici
 * ilk karede YUMUSAK kaydirmayi goruyordu -- tam olarak istemedigi sey.
 * `useSyncExternalStore` degeri render aninda okuyor, o yuzden yanlis kare
 * hic olusmuyor. Ayrica React'in resmi "dis kaynagi oku" araci bu.
 *
 * Sunucu anlik goruntusu (`getServerSnapshot`) `false`: sunucuda medya sorgusu
 * diye bir sey yok ve hidrasyon uyusmazligini onlemenin tek yolu bu.
 */
function useReducedMotion(): boolean {
  return useSyncExternalStore(
    (onChange) => {
      const mq = window.matchMedia(REDUCED_MOTION_QUERY);
      mq.addEventListener('change', onChange);
      return () => mq.removeEventListener('change', onChange);
    },
    () => window.matchMedia(REDUCED_MOTION_QUERY).matches,
    () => false,
  );
}

export default function ProductionScreen({ steps, isRunning, summary, onClose }: Props) {
  const listRef = useRef<HTMLDivElement>(null);
  const reduceMotion = useReducedMotion();

  useEffect(() => {
    listRef.current?.scrollTo({ top: listRef.current.scrollHeight, behavior: reduceMotion ? 'auto' : 'smooth' });
  }, [steps.length, reduceMotion]);

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-surface-900/85 backdrop-blur-sm">
      <div className="w-full max-w-md glass-panel rounded-[var(--radius-modal)] p-5 sm:p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-sm font-semibold text-content-primary">
            {isRunning ? 'Generating schema' : 'Done'}
          </h2>
          {/* Kullanıcı işi kapatabilmeli — uzun bir üretimde ekranda
              hapsolmamalı. Akış hâlâ sürüyorsa da kapatma engellenmiyor;
              arka planda devam eder, sonuç geldiğinde canvas zaten açılır. */}
          <button
            type="button"
            onClick={onClose}
            aria-label="Close"
            className="tap-44 p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-white/[0.06] transition-colors"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        <div ref={listRef} className="flex flex-col gap-2 max-h-64 overflow-y-auto pr-1">
          {steps.map((step, i) => {
            const Icon = KIND_ICON[step.kind];
            const isLast = i === steps.length - 1;
            const spinning =
              isRunning &&
              isLast &&
              (step.kind === 'plan' ||
                step.kind === 'draft' ||
                step.kind === 'inspect' ||
                step.kind === 'repair');
            return (
              <div key={i} className="flex items-start gap-2.5 text-xs">
                <Icon
                  className={`w-3.5 h-3.5 mt-0.5 shrink-0 ${
                    step.kind === 'finding'
                      ? 'text-warning'
                      : step.kind === 'clean'
                      ? 'text-success-text'
                      : 'text-content-muted'
                  } ${spinning && !reduceMotion ? 'animate-spin' : ''}`}
                />
                <span
                  className={
                    step.kind === 'finding'
                      ? 'text-content-secondary'
                      : step.kind === 'clean'
                      ? 'text-content-primary font-medium'
                      : 'text-content-muted'
                  }
                >
                  {step.message}
                </span>
              </div>
            );
          })}
        </div>

        {summary && (
          <div className="mt-4 pt-4 border-t border-line-strong flex flex-col gap-3">
            {summary.clean ? (
              <div className="flex items-start gap-2.5 text-xs">
                <Check className="w-3.5 h-3.5 mt-0.5 shrink-0 text-success-text" />
                <span className="text-content-primary">
                  Schema compiled with no errors
                  {summary.rounds > 1 ? ` after ${summary.rounds} rounds` : ''}.
                </span>
              </div>
            ) : (
              <div className="flex flex-col gap-2">
                <div className="flex items-start gap-2.5 text-xs">
                  <AlertTriangle className="w-3.5 h-3.5 mt-0.5 shrink-0 text-warning" />
                  <span className="text-content-primary font-medium">
                    {summary.findings.length} unresolved{' '}
                    {summary.findings.length === 1 ? 'problem' : 'problems'} — the schema is
                    loaded, but fix these before using it.
                  </span>
                </div>
                <ul className="flex flex-col gap-1 pl-6 max-h-32 overflow-y-auto">
                  {summary.findings.map((finding, i) => (
                    <li key={i} className="text-[11px] text-content-secondary leading-snug">
                      {finding}
                    </li>
                  ))}
                </ul>
              </div>
            )}

            {/* Taşınabilirlik notları bulgu DEĞİL: kullanıcı bu motoru seçti,
                şemanın diğerlerinde takılması bugünkü işini engellemiyor.
                Katlanmış duruyor ki "yarın MySQL'e taşıyabilir miyim" sorusu
                cevapsız kalmasın ama asıl uyarıyı da gölgelemesin. */}
            {summary.portability.length > 0 && (
              <details className="text-[11px]">
                <summary className="cursor-pointer text-content-muted hover:text-content-secondary">
                  Works on this engine, but {summary.portability.length} issue
                  {summary.portability.length === 1 ? '' : 's'} on other engines
                </summary>
                <ul className="flex flex-col gap-1 mt-1.5 pl-3 max-h-28 overflow-y-auto">
                  {summary.portability.map((note, i) => (
                    <li key={i} className="text-content-muted leading-snug">
                      {note}
                    </li>
                  ))}
                </ul>
              </details>
            )}

            {/* Büyük şemalar (50-60 tablo) paralel parçalar hâlinde üretilip
                birleştiriliyor. Birleştirme sırasında tekilleştirilen bir
                tablo, düşürülen çözülemez bir ilişki ya da tamamen
                başarısız olan bir alan — bunların hiçbiri bulgu DEĞİL
                (şema yine derleniyor) ama sessizce kaybolmamalı: kullanıcı
                "45 tablo" görüp aslında 9 tablonun (bir alanın) hiç
                üretilmediğini bilmeden kalmamalı. bkz. SchemaChunkMerger. */}
            {summary.mergeNotes && summary.mergeNotes.length > 0 && (
              <details className="text-[11px]">
                <summary className="cursor-pointer text-content-muted hover:text-content-secondary">
                  {summary.mergeNotes.length} note{summary.mergeNotes.length === 1 ? '' : 's'} from
                  merging this schema's parts
                </summary>
                <ul className="flex flex-col gap-1 mt-1.5 pl-3 max-h-28 overflow-y-auto">
                  {summary.mergeNotes.map((note, i) => (
                    <li key={i} className="text-content-muted leading-snug">
                      {note}
                    </li>
                  ))}
                </ul>
              </details>
            )}
          </div>
        )}

        {!isRunning && (
          <button
            type="button"
            onClick={onClose}
            className="w-full mt-5 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2 rounded-[var(--radius-card)] text-sm transition-colors"
          >
            Continue
          </button>
        )}
      </div>
    </div>
  );
}
