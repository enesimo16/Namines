'use client';

import { useEffect, useRef, useState } from 'react';
import { Check, Loader2, AlertTriangle, X } from 'lucide-react';
import { AgentStepEvent } from '../../lib/sseSchemaStream';

interface Props {
  steps: AgentStepEvent[];
  /** Akış hâlâ devam ediyor mu — false olunca "kapat" görünür, otomatik kapanmaz. */
  isRunning: boolean;
  onClose: () => void;
}

const KIND_ICON: Record<AgentStepEvent['kind'], typeof Check> = {
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
export default function ProductionScreen({ steps, isRunning, onClose }: Props) {
  const listRef = useRef<HTMLDivElement>(null);
  const [reduceMotion, setReduceMotion] = useState(false);

  useEffect(() => {
    const mq = window.matchMedia('(prefers-reduced-motion: reduce)');
    setReduceMotion(mq.matches);
    const handler = (e: MediaQueryListEvent) => setReduceMotion(e.matches);
    mq.addEventListener('change', handler);
    return () => mq.removeEventListener('change', handler);
  }, []);

  useEffect(() => {
    listRef.current?.scrollTo({ top: listRef.current.scrollHeight, behavior: reduceMotion ? 'auto' : 'smooth' });
  }, [steps.length, reduceMotion]);

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-surface-900/85 backdrop-blur-sm">
      <div className="w-full max-w-md glass-panel rounded-[var(--radius-modal)] p-5 sm:p-6">
        <div className="flex items-center justify-between mb-4">
          <h2 className="text-sm font-semibold text-content-primary">
            {isRunning ? 'Şema üretiliyor' : 'Tamamlandı'}
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
            const spinning = isRunning && isLast && (step.kind === 'draft' || step.kind === 'inspect' || step.kind === 'repair');
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

        {!isRunning && (
          <button
            type="button"
            onClick={onClose}
            className="w-full mt-5 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2 rounded-[var(--radius-card)] text-sm transition-colors"
          >
            Devam et
          </button>
        )}
      </div>
    </div>
  );
}
