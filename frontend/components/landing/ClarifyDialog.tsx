'use client';

import { useState } from 'react';
import { Loader2, Wand2, HelpCircle, ArrowRight } from 'lucide-react';
import { ClarifyResponse } from '../../types/nai';

interface Props {
  data: ClarifyResponse;
  isGenerating: boolean;
  onCancel: () => void;
  onSubmit: (answers: Record<string, string>) => void;
}

/**
 * Üretimden ÖNCE sorulan netleştirici sorular (new-phase/36 §3).
 *
 * Bu adım tamamen bedava: sorular sunucuda anahtar kelimeden çıkarılıyor,
 * kullanıcı ilk soruyu görene kadar tek bir token harcanmıyor.
 *
 * <b>Her soru atlanabilir.</b> Cevaplanmayanlar sunucuda varsayılanıyla
 * dolduruluyor — zorunlu kılmak, hızlı bir taslak isteyen kullanıcıyı forma
 * mahkûm etmek olurdu ve o kullanıcı formu kapatıp hiçbir şey almadan gider.
 */
export default function ClarifyDialog({ data, isGenerating, onCancel, onSubmit }: Props) {
  const [answers, setAnswers] = useState<Record<string, string>>({});

  const answeredCount = Object.keys(answers).length;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-surface-900/80 backdrop-blur-sm">
      <div className="w-full max-w-lg max-h-[85vh] overflow-y-auto glass-panel rounded-2xl p-5 sm:p-6">
        <div className="mb-5">
          <h2 className="text-lg font-semibold text-content-primary mb-1">
            A few questions first
          </h2>
          <p className="text-xs text-content-muted leading-relaxed">
            {data.recognised
              ? `This looks like a ${data.archetype.toLowerCase()} project. Answering these makes the schema match what you actually need.`
              : `We could not tell what kind of project this is, so these are general questions. Answering them makes the schema match what you actually need.`}
            {' '}
            <span className="text-content-secondary">Every question is optional.</span>
          </p>
        </div>

        <div className="flex flex-col gap-4">
          {data.questions.map(q => (
            <div key={q.id}>
              <p className="text-sm text-content-primary font-medium mb-1">{q.text}</p>
              {/* Gerekçe gösteriliyor: gerekçesiz soru doldurulacak bir form
                  gibi hissettiriyor, gerekçeli soru bir sohbet gibi. */}
              <p className="flex items-start gap-1.5 text-[11px] text-content-muted mb-2 leading-snug">
                <HelpCircle className="w-3 h-3 mt-0.5 shrink-0" />
                <span>{q.why}</span>
              </p>
              <div className="flex flex-wrap gap-1.5">
                {q.options.map(option => {
                  const isSelected = answers[q.id] === option;
                  return (
                    <button
                      key={option}
                      type="button"
                      disabled={isGenerating}
                      onClick={() =>
                        setAnswers(prev => {
                          // Aynı seçeneğe tekrar basmak cevabı geri alıyor:
                          // yanlışlıkla seçilen bir cevabı temizlemenin başka
                          // yolu olmazsa kullanıcı diyaloğu kapatmak zorunda kalır.
                          const next = { ...prev };
                          if (isSelected) delete next[q.id];
                          else next[q.id] = option;
                          return next;
                        })
                      }
                      className={`px-2.5 py-1.5 rounded-lg text-xs font-medium transition-all border ${
                        isSelected
                          ? 'bg-white/[0.10] text-content-primary border-white/30'
                          : 'text-content-muted border-white/10 hover:text-content-primary hover:bg-white/[0.04]'
                      }`}
                    >
                      {option}
                    </button>
                  );
                })}
              </div>
            </div>
          ))}
        </div>

        <div className="flex items-center justify-between gap-3 mt-6 pt-4 border-t border-white/10">
          <button
            type="button"
            disabled={isGenerating}
            onClick={onCancel}
            className="text-xs text-content-muted hover:text-content-primary transition-colors disabled:opacity-50"
          >
            Back to prompt
          </button>

          <div className="flex items-center gap-3">
            <span className="text-[11px] text-content-muted hidden sm:inline">
              {answeredCount}/{data.questions.length} answered
            </span>
            <button
              type="button"
              disabled={isGenerating}
              onClick={() => onSubmit(answers)}
              className="bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2 px-4 rounded-xl transition-all flex items-center gap-2 disabled:opacity-50 disabled:cursor-not-allowed text-sm"
            >
              {isGenerating ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Generating...</span>
                </>
              ) : answeredCount === 0 ? (
                <>
                  <ArrowRight className="w-4 h-4" />
                  <span>Skip &amp; generate</span>
                </>
              ) : (
                <>
                  <Wand2 className="w-4 h-4" />
                  <span>Generate Schema</span>
                </>
              )}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
