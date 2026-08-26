'use client';

import { useEffect, useState } from 'react';
import { Loader2, HelpCircle, Wand2, ArrowLeft, ListChecks, Info } from 'lucide-react';
import { schemaService } from '../../services/api';
import { SchemaPlan } from '../../types/nai';

interface Props {
  prompt: string;
  /** ClarifyDialog'tan gelen ilk cevaplar — plan bunların üzerine kuruluyor. */
  initialAnswers: Record<string, string>;
  isGenerating: boolean;
  onCancel: () => void;
  /** Kullanıcı planı onayladı — üretim biriken tüm cevaplarla başlamalı. */
  onApprove: (answers: Record<string, string>) => void;
}

/**
 * Plan modu (second-phase/05-PLAN-MODU.md): şema üretmeden önce, cevaplardan
 * çıkan tablo listesini gösterip onay istiyor.
 *
 * <b>Tablo listesi AI'YA YAZDIRILMIYOR</b> — sunucuda kural tabanlı çıkıyor
 * (bkz. PlanBuilder.cs). Bu ekran o listeyi gösteriyor, uydurmuyor. Bir
 * belirsizlik varsa (ör. "çok oyunculu" dedi ama lonca mı takım mı belirsiz)
 * en fazla bir ek soru soruyor; üç turdan sonra soru sorulmuyor, elde
 * olanla devam ediliyor.
 */
export default function PlanScreen({ prompt, initialAnswers, isGenerating, onCancel, onApprove }: Props) {
  const [answers, setAnswers] = useState(initialAnswers);
  const [plan, setPlan] = useState<SchemaPlan | null>(null);
  const [isLoading, setIsLoading] = useState(true);
  const [followUpChoice, setFollowUpChoice] = useState<string | null>(null);

  const fetchPlan = async (currentAnswers: Record<string, string>, round: number) => {
    setIsLoading(true);
    setFollowUpChoice(null);
    try {
      const result = await schemaService.plan(prompt, currentAnswers, round);
      setPlan(result);
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchPlan(initialAnswers, 1);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleFollowUpSubmit = () => {
    if (!plan?.followUp) return;
    const next = { ...answers };
    if (followUpChoice) next[plan.followUp.id] = followUpChoice;
    setAnswers(next);
    fetchPlan(next, plan.round + 1);
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-surface-900/80 backdrop-blur-sm">
      <div className="w-full max-w-lg max-h-[85vh] overflow-y-auto glass-panel rounded-2xl p-5 sm:p-6">
        {isLoading || !plan ? (
          <div className="flex items-center justify-center gap-2 py-10 text-sm text-content-muted">
            <Loader2 className="w-4 h-4 animate-spin" /> Planlanıyor…
          </div>
        ) : plan.followUp ? (
          // ── Tek bir takip sorusu — belirsizliği çözmeden plan kesin değil ──
          <div>
            <div className="flex items-center gap-2 mb-1">
              <HelpCircle className="w-4 h-4 text-content-muted" />
              <h2 className="text-lg font-semibold text-content-primary">Bir şey daha</h2>
            </div>
            <p className="text-xs text-content-muted mb-4">Bu, planı belirginleştirecek.</p>

            <p className="text-sm text-content-primary font-medium mb-1">{plan.followUp.text}</p>
            <p className="flex items-start gap-1.5 text-[11px] text-content-muted mb-3 leading-snug">
              <Info className="w-3 h-3 mt-0.5 shrink-0" />
              <span>{plan.followUp.why}</span>
            </p>
            <div className="flex flex-wrap gap-1.5 mb-6">
              {plan.followUp.options.map(option => (
                <button
                  key={option}
                  type="button"
                  onClick={() => setFollowUpChoice(prev => (prev === option ? null : option))}
                  className={`px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all border ${
                    followUpChoice === option
                      ? 'bg-white/[0.10] text-content-primary border-white/30'
                      : 'text-content-muted border-white/10 hover:text-content-primary hover:bg-white/[0.04]'
                  }`}
                >
                  {option}
                </button>
              ))}
            </div>

            <div className="flex items-center justify-between">
              <button type="button" onClick={onCancel} className="text-xs text-content-muted hover:text-content-primary transition-colors">
                Vazgeç
              </button>
              <button
                type="button"
                onClick={handleFollowUpSubmit}
                className="bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2 px-4 rounded-xl text-sm transition-all"
              >
                Devam
              </button>
            </div>
          </div>
        ) : (
          // ── Plan kesin — tablo listesi + varsayımlar, onay bekleniyor ──
          <div>
            <div className="flex items-center gap-2 mb-1">
              <ListChecks className="w-4 h-4 text-content-muted" />
              <h2 className="text-lg font-semibold text-content-primary">Plan</h2>
            </div>
            <p className="text-xs text-content-muted mb-4">
              {plan.tables.length} tablo kuracağım. Onaylarsan üretim bu planla başlar.
            </p>

            <div className="flex flex-col gap-2 mb-4">
              {plan.tables.map(t => (
                <div key={t.name} className="text-xs">
                  <span className="font-mono font-semibold text-content-primary">{t.name}</span>
                  <span className="text-content-muted"> — {t.reason}</span>
                </div>
              ))}
            </div>

            {plan.assumptions.length > 0 && (
              <div className="border border-white/10 rounded-lg p-3 mb-5 bg-white/[0.03]">
                <p className="text-[10px] uppercase tracking-wider text-content-subtle font-semibold mb-1.5">
                  Cevaplamadıkların için varsayılan kullanıldı
                </p>
                <ul className="flex flex-col gap-1">
                  {plan.assumptions.map(a => (
                    <li key={a} className="text-[11px] text-content-muted">{a}</li>
                  ))}
                </ul>
              </div>
            )}

            <div className="flex items-center justify-between pt-2 border-t border-white/10">
              <button
                type="button"
                onClick={onCancel}
                disabled={isGenerating}
                className="flex items-center gap-1.5 text-xs text-content-muted hover:text-content-primary transition-colors disabled:opacity-50"
              >
                <ArrowLeft className="w-3.5 h-3.5" /> Geri
              </button>
              <button
                type="button"
                disabled={isGenerating}
                onClick={() => onApprove(answers)}
                className="flex items-center gap-2 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2 px-4 rounded-xl text-sm transition-all disabled:opacity-50"
              >
                {isGenerating ? <Loader2 className="w-4 h-4 animate-spin" /> : <Wand2 className="w-4 h-4" />}
                Onayla ve üret
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
