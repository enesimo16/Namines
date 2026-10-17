import React, { useState, useRef, useEffect } from 'react';
import type { TableNodeData } from '../../../types/flow';
import { useReactFlow } from '@xyflow/react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { useAIGateway } from '../../../hooks/useAIGateway';
import { schemaService } from '../../../services/api';
import { Loader2, ArrowUp, History, Sparkles, Table } from 'lucide-react';
import { flowToSchema } from '../../../lib/flowToSchema';
import { apiErrorStatus, errorMessage } from '../../../lib/errors';

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
    } catch (error) {
      if (apiErrorStatus(error) === 429) {
        showToast("Daily AI limit reached! Please upgrade your plan for unlimited access.", "warning");
      } else {
        console.error("Revision failed", error);
        const errorMsg = errorMessage(error, "An error occurred during revision.");
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
      } catch (error) {
        if (apiErrorStatus(error) === 429) {
          showToast("Daily AI limit reached! Please upgrade your plan for unlimited access.", "warning");
        } else {
          console.error("Generation failed", error);
          const errorMsg = errorMessage(error, "An error occurred during generation.");
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


  return (
    <div
      className={`fixed bottom-3 left-3 right-3 sm:bottom-5 sm:left-1/2 sm:right-auto sm:-translate-x-1/2 z-[60] transition-all duration-300 ${
        isActive ? 'opacity-100 scale-100' : 'opacity-40 hover:opacity-100 scale-[0.98]'
      }`}
      onMouseEnter={() => setIsFocused(true)}
      onMouseLeave={() => { if (document.activeElement !== inputRef.current) setIsFocused(false); }}
    >
      {/* Yalnızca seçili tablo varsa üstte zarif bir rozet olarak görünür */}
      {selectedNodes.length > 0 && (
        <div className="flex items-center justify-center mb-2 animate-in fade-in duration-200">
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-surface-900/95 border border-line-strong backdrop-blur-xl shadow-lg text-micro font-medium text-content-secondary select-none">
            <Table className="w-3.5 h-3.5 text-content-primary shrink-0" />
            <span className="font-bold text-content-primary">
              {selectedNodes.length} table{selectedNodes.length > 1 ? 's' : ''}:
            </span>
            <div className="flex items-center gap-1 overflow-x-auto max-w-[280px]">
              {selectedNodes.slice(0, 3).map(n => (
                <span
                  key={n.id}
                  className="font-mono text-micro font-bold text-content-primary bg-surface-800 border border-line px-1.5 py-0.5 rounded-full shrink-0"
                >
                  /{((n.data as TableNodeData | undefined)?.table?.name || '').toLowerCase()}
                </span>
              ))}
              {selectedNodes.length > 3 && (
                <span className="text-micro text-content-muted shrink-0">+{selectedNodes.length - 3}</span>
              )}
            </div>
          </div>
        </div>
      )}

      {/* Prompt geçmişi popover */}
      {promptHistory.length > 0 && isHistoryOpen && (
        <div
          ref={historyRef}
          className="mb-2 w-full sm:w-[540px] sm:max-w-[90vw] max-h-52 overflow-y-auto bg-surface-900/98 backdrop-blur-2xl border border-line-strong rounded-[var(--radius-card)] p-1.5 shadow-2xl flex flex-col gap-0.5 animate-in fade-in zoom-in-95 duration-150"
        >
          <div className="px-2.5 py-1 text-micro font-bold uppercase tracking-wider text-content-muted">
            Recent Prompts
          </div>
          {promptHistory.map((p, i) => (
            <button
              key={i}
              type="button"
              onClick={() => {
                setPrompt(p);
                setIsHistoryOpen(false);
                inputRef.current?.focus();
              }}
              className="text-left text-xs text-content-secondary hover:text-content-primary hover:bg-surface-800 rounded-[var(--radius-control)] px-2.5 py-2 truncate transition-colors cursor-pointer"
              title={p}
            >
              {p}
            </button>
          ))}
        </div>
      )}

      {/* Ana Estetik Kapsül (Floating Command Pill) */}
      <form
        id="regional-prompt-panel"
        onSubmit={handleAction}
        className="flex items-center gap-2 w-full sm:w-[540px] sm:max-w-[90vw] bg-surface-900/95 backdrop-blur-2xl border border-line-strong hover:border-line-solid-strong focus-within:border-line-solid-strong rounded-full pl-3.5 pr-1.5 py-1.5 shadow-2xl transition-all duration-200"
      >
        <Sparkles className="w-4 h-4 text-content-muted shrink-0 transition-colors" />

        <input
          aria-label="Prompt for the database schema"
          ref={inputRef}
          id="regional-prompt-input"
          type="text"
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          onFocus={() => setIsFocused(true)}
          onBlur={() => setIsFocused(false)}
          placeholder={placeholder}
          disabled={isRevising}
          className="flex-1 min-w-0 bg-transparent text-xs sm:text-sm text-content-primary placeholder:text-content-muted focus:outline-none py-1"
        />

        {/* Prompt geçmişi butonu */}
        {promptHistory.length > 0 && (
          <button
            type="button"
            onClick={() => setIsHistoryOpen(v => !v)}
            aria-label="Prompt history"
            title="Prompt history"
            className={`shrink-0 w-7 h-7 rounded-full flex items-center justify-center transition-colors cursor-pointer ${
              isHistoryOpen
                ? 'text-content-primary bg-surface-700'
                : 'text-content-muted hover:text-content-primary hover:bg-surface-800'
            }`}
          >
            <History className="w-3.5 h-3.5" />
          </button>
        )}

        {/* Alternative butonu */}
        {lastGenerationPrompt && (
          <button
            type="button"
            onClick={() => {
              setIsGeneratingAlt(true);
              window.dispatchEvent(new CustomEvent('namines:generate-alternative'));
            }}
            disabled={isGeneratingAlt || isRevising}
            aria-label="Generate alternative"
            title="Alternative — replay last prompt for another variation"
            className="shrink-0 w-7 h-7 rounded-full flex items-center justify-center text-content-muted hover:text-content-primary hover:bg-surface-800 disabled:opacity-40 transition-colors cursor-pointer"
          >
            {isGeneratingAlt ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Sparkles className="w-3.5 h-3.5" />}
          </button>
        )}

        {/* Gönder butonu */}
        <button
          type="submit"
          disabled={isRevising || !prompt.trim()}
          aria-label={isInitialGeneration ? 'Generate schema' : 'Revise with AI'}
          title={isInitialGeneration ? 'Generate schema' : 'Revise with AI'}
          className="shrink-0 w-8 h-8 rounded-full flex items-center justify-center bg-content-primary hover:opacity-90 disabled:opacity-30 disabled:cursor-not-allowed text-surface-900 font-bold transition-all cursor-pointer shadow-xs"
        >
          {isRevising ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <ArrowUp className="w-3.5 h-3.5" />}
        </button>
      </form>
    </div>
  );
}
