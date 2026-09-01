import React, { useState, useRef, useEffect } from 'react';
import { useReactFlow } from '@xyflow/react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { useAIGateway } from '../../../hooks/useAIGateway';
import { schemaService } from '../../../services/api';
import { Loader2, ArrowUp, History, Sparkles } from 'lucide-react';
import { flowToSchema } from '../../../lib/flowToSchema';

/**
 * Eskiden "Regional Revision" — sürüklenebilir, parlayan/yıldızlı büyük bir
 * kart olarak sadece tablo seçiliyken açılıyordu. Artık her zaman ekranda,
 * alt-orta sabit, minimal bir chat çubuğu: normalde soluk, tablo seçiliyken
 * veya üzerine gelindiğinde tam görünür (bkz. kullanıcı talimatı).
 */
export default function RegionalPromptPanel() {
  const { getNodes, getEdges } = useReactFlow();
  const { schema, applyRevision, naiModel, dbType, loadFromSchema, promptHistory, addPromptToHistory, recordGenerationSource } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const { checkAccess } = useAIGateway();
  const [prompt, setPrompt] = useState('');
  const [isRevising, setIsRevising] = useState(false);
  const [isFocused, setIsFocused] = useState(false);
  const [isHistoryOpen, setIsHistoryOpen] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const historyRef = useRef<HTMLDivElement>(null);

  // ── "Alternative" — üst araç çubuğundan buraya taşındı (bkz. ToolbarPanel.tsx
  //    içindeki taşıma notu). Eylem "son prompt'u ikinci kez çalıştır" demek,
  //    dolayısıyla prompt kutusunun yanı doğru yer.
  //
  //    İş mantığı (kota kontrolü, üretim, A/B karşılaştırma modalı) ToolbarPanel'de
  //    KALDI — burası yalnızca tetikleyici. Projede zaten kurulu olan custom-event
  //    deseni (`namines:open-ai-settings` vb.) kullanılıyor.
  const lastGenerationPrompt = useSchemaStore(s => s.lastGenerationPrompt);
  const [isGeneratingAlt, setIsGeneratingAlt] = useState(false);

  useEffect(() => {
    const done = () => setIsGeneratingAlt(false);
    window.addEventListener('namines:alternative-done', done);
    return () => window.removeEventListener('namines:alternative-done', done);
  }, []);

  // Geçmiş menüsü dışına tıklanınca kapan — second-phase/08-PROMPT-DENEYIMI.md §8.3.
  useEffect(() => {
    if (!isHistoryOpen) return;
    const handler = (e: MouseEvent) => {
      if (historyRef.current && !historyRef.current.contains(e.target as Node)) setIsHistoryOpen(false);
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [isHistoryOpen]);

  useEffect(() => {
    const handleOpen = () => {
      setTimeout(() => inputRef.current?.focus(), 50);
    };
    window.addEventListener('namines:open-regional-prompt', handleOpen);
    return () => window.removeEventListener('namines:open-regional-prompt', handleOpen);
  }, []);

  // Seçili tablo node'ları — referans olarak /tablo-adı çipleri şeklinde girer.
  const selectedNodes = getNodes().filter(n => n.selected && n.type === 'tableNode');
  const isInitialGeneration = schema?.tables.length === 0;
  const isActive = selectedNodes.length > 0 || isFocused;

  const handleRevise = async () => {
    if (!prompt.trim() || !schema) return;
    if (!checkAccess("Regional Revision")) return;

    try {
      setIsRevising(true);

      const currentSchema = flowToSchema(schema, getNodes(), getEdges());
      if (!currentSchema) return;

      const selectedTableIds = selectedNodes.map(n => n.id);
      const selectedTables = currentSchema.tables.filter(t => selectedTableIds.includes(t.id));

      const existingRelations = currentSchema.relations.filter(r =>
        selectedTableIds.includes(r.sourceTableId) || selectedTableIds.includes(r.targetTableId)
      );

      const partialSchema = await schemaService.reviseSchema(selectedTables, existingRelations, prompt, naiModel);

      applyRevision(partialSchema);
      addPromptToHistory(prompt);
      setPrompt('');
    } catch (error: any) {
      if (error?.response?.status === 429) {
        showToast("Daily AI limit reached! Please upgrade your plan for unlimited access.", "warning");
      } else {
        console.error("Revision failed", error);
        const errorMsg = error?.response?.data?.message || "An error occurred during revision.";
        showToast(errorMsg, "error");
      }
    } finally {
      setIsRevising(false);
    }
  };

  const handleAction = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isInitialGeneration) {
      if (!prompt.trim()) return;
      if (!checkAccess("AI Schema Generation")) return;

      try {
        setIsRevising(true);
        const generated = await schemaService.generateSchema(prompt, dbType, naiModel);
        loadFromSchema(generated);
        addPromptToHistory(prompt);
        // İlk üretim buradan da yapılabiliyor (boş canvas'taki prompt çubuğu).
        // Kaydetmezsek "Alternatif üret" (second-phase/09) bu yoldan gelen
        // kullanıcı için ölü kalır — aynı özelliğin iki girişi olup yalnızca
        // birinin kaynağı kaydetmesi tutarsızlık olurdu. Netleştirme soruları
        // bu yolda sorulmuyor, o yüzden cevap yok.
        recordGenerationSource(prompt, null);
        setPrompt('');
        showToast("Database schema successfully generated!", "success");
      } catch (error: any) {
        if (error?.response?.status === 429) {
          showToast("Daily AI limit reached! Please upgrade your plan for unlimited access.", "warning");
        } else {
          console.error("Generation failed", error);
          const errorMsg = error?.response?.data?.message || "An error occurred during generation.";
          showToast(errorMsg, "error");
        }
      } finally {
        setIsRevising(false);
      }
    } else {
      await handleRevise();
    }
  };

  const placeholder = isInitialGeneration
    ? 'Describe the database schema you want to generate…'
    : selectedNodes.length > 0
      ? 'Ask AI to modify the selected tables…'
      : 'Select a table, or click here to ask AI…';

  // Kapsam görünür olmalı — bkz. second-phase/08-PROMPT-DENEYIMI.md §8.4.
  // Seçili tablo çipleri zaten vardı ama neyin DEĞİŞECEĞİNİ açıkça söylemiyordu;
  // kullanıcı göndermeden önce bunu bilmeli, "beklenmedik değişiklik" güveni kırar.
  const scopeLabel = isInitialGeneration
    ? null
    : selectedNodes.length > 0
      ? `${selectedNodes.length} table${selectedNodes.length > 1 ? 's' : ''} selected — only ${selectedNodes.length > 1 ? 'these' : 'this one'} will change`
      : 'No table selected — the whole schema may change';

  return (
    <div
      className={`fixed bottom-5 left-1/2 -translate-x-1/2 z-[100] transition-opacity duration-300 ${
        isActive ? 'opacity-100' : 'opacity-35 hover:opacity-100'
      }`}
      onMouseEnter={() => setIsFocused(true)}
      onMouseLeave={() => { if (document.activeElement !== inputRef.current) setIsFocused(false); }}
    >
      {scopeLabel && isActive && (
        <p className="text-center text-[10px] text-content-muted mb-1.5 select-none">
          {scopeLabel}
        </p>
      )}

      {/* Prompt geçmişi — daha önce ne istendiğini gösterir, tekrar çalıştırılabilir.
          second-phase/08-PROMPT-DENEYIMI.md §8.3. */}
      {promptHistory.length > 0 && isHistoryOpen && (
        <div
          ref={historyRef}
          className="mb-2 w-[560px] max-w-[88vw] max-h-56 overflow-y-auto bg-surface-800/95 backdrop-blur-xl border border-content-primary/12 rounded-[var(--radius-modal)] p-1.5 shadow-[0_8px_32px_color-mix(in srgb, var(--color-scrim) 50%, transparent)] flex flex-col gap-0.5"
        >
          {promptHistory.map((p, i) => (
            <button
              key={i}
              type="button"
              onClick={() => {
                setPrompt(p);
                setIsHistoryOpen(false);
                inputRef.current?.focus();
              }}
              className="text-left text-xs text-content-muted hover:text-content-primary hover:bg-white/[0.05] rounded-[var(--radius-control)] px-2.5 py-1.5 truncate transition-colors"
              title={p}
            >
              {p}
            </button>
          ))}
        </div>
      )}

      <form
        onSubmit={handleAction}
        className="flex items-center gap-2 w-[560px] max-w-[88vw] bg-surface-800/95 backdrop-blur-xl border border-content-primary/12 rounded-full pl-3 pr-1.5 py-1.5 shadow-[0_8px_32px_color-mix(in srgb, var(--color-scrim) 50%, transparent)]"
      >
        {promptHistory.length > 0 && (
          <button
            type="button"
            onClick={() => setIsHistoryOpen(v => !v)}
            aria-label="Prompt history"
            title="Prompt history"
            className={`shrink-0 w-7 h-7 rounded-full flex items-center justify-center transition-colors ${
              isHistoryOpen ? 'text-content-primary bg-white/[0.08]' : 'text-content-muted hover:text-content-primary hover:bg-white/[0.05]'
            }`}
          >
            <History className="w-3.5 h-3.5" />
          </button>
        )}

        {/* Seçili tablo referans çipleri */}
        {selectedNodes.length > 0 && (
          <div className="flex items-center gap-1 shrink-0 max-w-[40%] overflow-x-auto">
            {selectedNodes.slice(0, 3).map(n => (
              <span
                key={n.id}
                className="flex items-center gap-1 text-[11px] font-mono text-content-primary bg-white/[0.08] border border-white/15 px-2 py-1 rounded-full whitespace-nowrap shrink-0"
              >
                /{((n.data as any)?.table?.name || '').toLowerCase()}
              </span>
            ))}
            {selectedNodes.length > 3 && (
              <span className="text-[11px] text-content-muted shrink-0">+{selectedNodes.length - 3}</span>
            )}
          </div>
        )}

        <input
          ref={inputRef}
          id="regional-prompt-input"
          type="text"
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          onFocus={() => setIsFocused(true)}
          onBlur={() => setIsFocused(false)}
          placeholder={placeholder}
          disabled={isRevising}
          className="flex-1 min-w-0 bg-transparent text-sm text-content-primary placeholder:text-content-subtle focus:outline-none"
        />

        {/* Alternative — YALNIZCA son üretim prompt'u varken render edilir.
            Devre dışı gri bir düğme göstermek yerine gizleniyor: eski hâlinde
            kullanıcı basmayı deniyor, hiçbir şey olmuyor ve nedenini yalnızca
            tooltip söylüyordu ("alternative kısmı çalışmıyor yani basılmıyor").
            Şablonla/import'la gelen şemalarda yeniden çalıştırılacak bir prompt
            yoktur — o yüzden düğme de yoktur. */}
        {lastGenerationPrompt && (
          <button
            type="button"
            onClick={() => {
              setIsGeneratingAlt(true);
              window.dispatchEvent(new CustomEvent('namines:generate-alternative'));
            }}
            disabled={isGeneratingAlt || isRevising}
            aria-label="Generate an alternative schema from the same prompt"
            title="Alternative — replay the last prompt for a second design (~1 AI round)"
            className="shrink-0 w-8 h-8 flex items-center justify-center rounded-full text-content-muted hover:text-content-primary hover:bg-white/[0.06] disabled:opacity-40 disabled:cursor-not-allowed transition-colors cursor-pointer"
          >
            {isGeneratingAlt ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Sparkles className="w-3.5 h-3.5" />}
          </button>
        )}

        <button
          type="submit"
          disabled={isRevising || !prompt.trim()}
          aria-label={isInitialGeneration ? 'Generate schema' : 'Revise with AI'}
          title={isInitialGeneration ? 'Generate schema' : 'Revise with AI'}
          className="shrink-0 w-8 h-8 flex items-center justify-center rounded-full bg-content-primary hover:bg-content-secondary disabled:opacity-40 disabled:cursor-not-allowed text-surface-900 transition-all cursor-pointer"
        >
          {isRevising ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <ArrowUp className="w-3.5 h-3.5" />}
        </button>
      </form>
    </div>
  );
}
