'use client';

import React, { useEffect, useMemo } from 'react';
import {
  RefreshCw,
  Search,
  Cpu,
  Zap,
  Sparkles,
  Bot,
  X,
  AlertTriangle,
} from 'lucide-react';
import { useAdminConsoleStore } from '../../store/useAdminConsoleStore';
import type { AdminAiToolRouteItem } from '../../services/adminService';

interface AiRoutingTabProps {
  locale?: string;
}

const COMMON_MODELS: Record<string, string[]> = {
  groq: [
    'qwen-2.5-coder-32b',
    'llama-3.3-70b-versatile',
    'mixtral-8x7b-32768',
  ],
  deepseek: [
    'deepseek-chat',
    'deepseek-reasoner',
  ],
  claude: [
    'claude-3-5-sonnet-20241022',
    'claude-3-5-haiku-20241022',
  ],
  openai: [
    'gpt-4o',
    'gpt-4o-mini',
    'o1-preview',
  ],
};

export default function AiRoutingTab({ locale = 'tr' }: AiRoutingTabProps) {
  const isEn = locale === 'en';

  const {
    aiRoutes: routes,
    loadingAiRoutes: loading,
    aiRoutesError: error,
    fetchAiRoutes,
    aiSearch: searchQuery,
    setAiSearch: setSearchQuery,
    aiProviderFilter: providerFilter,
    setAiProviderFilter: setProviderFilter,
    selectedAiToolKey,
    setSelectedAiToolKey,
    updateAiRoute,
    bulkSetGlobalProvider,
  } = useAdminConsoleStore();

  const providers = useMemo(() => ['groq', 'deepseek', 'claude', 'openai'], []);

  useEffect(() => {
    fetchAiRoutes();
  }, [fetchAiRoutes]);

  // Smooth scroll to selected tool card
  useEffect(() => {
    if (selectedAiToolKey) {
      const el = document.getElementById(`ai-tool-card-${selectedAiToolKey}`);
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }
    }
  }, [selectedAiToolKey]);

  const handleFieldChange = (
    toolKey: string,
    field: keyof AdminAiToolRouteItem,
    value: string | number
  ) => {
    const item = routes.find((r) => r.toolKey === toolKey);
    if (!item) return;

    const updates: Partial<AdminAiToolRouteItem> = { [field]: value };
    if (field === 'provider' && typeof value === 'string') {
      const suggestions = COMMON_MODELS[value.toLowerCase()];
      if (suggestions && suggestions.length > 0) {
        updates.modelName = suggestions[0];
      }
    }
    updateAiRoute(toolKey, updates);
  };

  const filteredRoutes = useMemo(() => {
    return routes.filter((r) => {
      const q = searchQuery.toLowerCase().trim();
      const matchesSearch =
        q === '' ||
        (r.displayName || '').toLowerCase().includes(q) ||
        r.toolKey.toLowerCase().includes(q) ||
        r.modelName.toLowerCase().includes(q);

      const matchesProvider =
        providerFilter === 'all' ||
        r.provider?.toLowerCase() === providerFilter.toLowerCase();

      return matchesSearch && matchesProvider;
    });
  }, [routes, searchQuery, providerFilter]);

  return (
    <div className="font-sans space-y-4">
      {/* ── 1. Compact Header ────────────────────────────────────────── */}
      <div className="rounded-[var(--radius-card)] bg-surface-800 border border-line-strong p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="space-y-0.5">
          <div className="flex items-center gap-2">
            <h2 className="text-base font-bold text-content-primary">
              {isEn ? 'AI Model Routing' : 'AI Model Yönlendirme'}
            </h2>
            <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line text-micro font-medium text-content-secondary">
              {routes.length} {isEn ? 'Tools' : 'Araç'}
            </span>
          </div>
          <p className="text-xs text-content-muted">
            {isEn
              ? 'Assign upstream LLM providers and models per database engineering tool.'
              : 'Veritabanı araçlarının hangi LLM sağlayıcısı ve modeli kullanacağını belirleyin.'}
          </p>
        </div>

        <div className="flex flex-wrap items-center gap-2 shrink-0">
          <button
            onClick={() => bulkSetGlobalProvider('groq')}
            disabled={loading}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-xs font-semibold text-content-primary border border-line-strong transition-colors cursor-pointer disabled:opacity-50"
            title="Tümünü Groq Yap"
          >
            <Zap className="w-3.5 h-3.5 text-content-secondary" />
            <span>Groq (32B)</span>
          </button>
          <button
            onClick={() => bulkSetGlobalProvider('deepseek')}
            disabled={loading}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-xs font-semibold text-content-primary border border-line-strong transition-colors cursor-pointer disabled:opacity-50"
            title="Tümünü DeepSeek Yap"
          >
            <Sparkles className="w-3.5 h-3.5 text-content-secondary" />
            <span>DeepSeek (R1)</span>
          </button>
          <button
            onClick={() => fetchAiRoutes()}
            disabled={loading}
            className="p-2 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-muted hover:text-content-primary border border-line-strong transition-colors cursor-pointer disabled:opacity-50"
            title={isEn ? 'Refresh' : 'Yenile'}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
        </div>
      </div>

      {error && (
        <div className="p-3 rounded-[var(--radius-card)] bg-surface-800 border border-danger/40 text-danger text-xs flex items-center gap-2">
          <AlertTriangle className="w-4 h-4 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      {/* ── 2. Filters Bar ─────────────────────────────────────────── */}
      <div className="rounded-[var(--radius-card)] bg-surface-800 border border-line-strong p-3 flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-2.5">
        <div className="relative flex-1">
          <Search className="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-content-muted" />
          <input
            type="text"
            value={searchQuery}
            onChange={(e) => setSearchQuery(e.target.value)}
            placeholder={isEn ? 'Filter AI tools or models...' : 'AI araç veya model ara...'}
            className="w-full pl-8 pr-7 py-1.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-xs text-content-primary placeholder:text-content-muted focus:outline-none"
          />
          {searchQuery && (
            <button
              onClick={() => setSearchQuery('')}
              className="absolute right-2 top-2 text-content-muted hover:text-content-primary tap-44 cursor-pointer"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          )}
        </div>

        <div className="flex items-center gap-1 shrink-0 overflow-x-auto scrollbar-none">
          {['all', 'groq', 'deepseek', 'claude', 'openai'].map((p) => {
            const active = providerFilter.toLowerCase() === p;
            return (
              <button
                key={p}
                onClick={() => setProviderFilter(p)}
                className={`px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-medium uppercase tracking-wider transition-colors cursor-pointer ${
                  active
                    ? 'bg-surface-600 text-content-primary font-bold border border-line-strong'
                    : 'text-content-muted hover:text-content-primary hover:bg-surface-700'
                }`}
              >
                {p === 'all' ? (isEn ? 'All' : 'Tümü') : p}
              </button>
            );
          })}
        </div>
      </div>

      {/* ── 3. AI Tool Cards Grid ─────────────────────────────────── */}
      {loading && routes.length === 0 ? (
        <div className="py-10 text-center text-xs text-content-muted rounded-[var(--radius-card)] bg-surface-800 border border-line-strong">
          <RefreshCw className="w-5 h-5 animate-spin mx-auto mb-2 text-content-muted" />
          <span>{isEn ? 'Loading AI routes...' : 'AI rotaları yükleniyor...'}</span>
        </div>
      ) : filteredRoutes.length === 0 ? (
        <div className="py-10 text-center text-xs text-content-muted rounded-[var(--radius-card)] bg-surface-800 border border-line-strong">
          {isEn ? 'No AI tools found.' : 'Kriterlere uygun AI aracı bulunamadı.'}
        </div>
      ) : (
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-3.5">
          {filteredRoutes.map((route) => {
            const isSelected = selectedAiToolKey?.toLowerCase() === route.toolKey.toLowerCase();
            const suggestions = COMMON_MODELS[route.provider?.toLowerCase()] || [];

            return (
              <div
                key={route.toolKey}
                id={`ai-tool-card-${route.toolKey}`}
                onClick={() => setSelectedAiToolKey(route.toolKey)}
                className={`rounded-[var(--radius-card)] bg-surface-800 border transition-all p-4 space-y-3 cursor-pointer ${
                  isSelected
                    ? 'border-line-strong bg-surface-750 ring-2 ring-content-secondary/40'
                    : 'border-line-strong/70 hover:border-line-strong'
                }`}
              >
                {/* Header */}
                <div className="flex items-center justify-between gap-2 pb-2.5 border-b border-line">
                  <div className="flex items-center gap-2.5 min-w-0">
                    <div className="p-1.5 rounded-[var(--radius-control)] bg-surface-700 border border-line shrink-0">
                      <Bot className="w-4 h-4 text-content-primary" />
                    </div>
                    <div className="min-w-0">
                      <span className="font-semibold text-sm text-content-primary truncate block">
                        {route.displayName || route.toolKey}
                      </span>
                      <span className="text-[10px] font-mono text-content-muted truncate block">
                        {route.toolKey}
                      </span>
                    </div>
                  </div>

                  <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-micro font-bold uppercase tracking-wider text-content-primary">
                    {route.provider}
                  </span>
                </div>

                {/* Form Controls */}
                <div className="space-y-2.5 text-xs" onClick={(e) => e.stopPropagation()}>
                  <div className="grid grid-cols-2 gap-2.5">
                    <div className="space-y-1">
                      <label className="text-micro font-medium text-content-muted">
                        {isEn ? 'Provider' : 'Sağlayıcı'}
                      </label>
                      <select
                        value={route.provider}
                        onChange={(e) => handleFieldChange(route.toolKey, 'provider', e.target.value)}
                        className="w-full px-2.5 py-1.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-xs text-content-primary focus:outline-none"
                      >
                        {providers.map((p) => (
                          <option key={p} value={p}>
                            {p.toUpperCase()}
                          </option>
                        ))}
                      </select>
                    </div>

                    <div className="space-y-1">
                      <label className="text-micro font-medium text-content-muted">
                        {isEn ? 'Fallback' : 'Yedek Sağlayıcı'}
                      </label>
                      <select
                        value={route.fallbackProvider || ''}
                        onChange={(e) => handleFieldChange(route.toolKey, 'fallbackProvider', e.target.value)}
                        className="w-full px-2.5 py-1.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-xs text-content-primary focus:outline-none"
                      >
                        <option value="">{isEn ? 'None' : 'Yok'}</option>
                        {providers
                          .filter((p) => p !== route.provider?.toLowerCase())
                          .map((p) => (
                            <option key={p} value={p}>
                              {p.toUpperCase()}
                            </option>
                          ))}
                      </select>
                    </div>
                  </div>

                  {/* Model Name */}
                  <div className="space-y-1">
                    <div className="flex items-center justify-between">
                      <label className="text-micro font-medium text-content-muted">
                        {isEn ? 'Model' : 'Model Adı'}
                      </label>
                      {suggestions.length > 0 && (
                        <div className="flex items-center gap-1">
                          {suggestions.map((m) => (
                            <button
                              key={m}
                              type="button"
                              onClick={() => handleFieldChange(route.toolKey, 'modelName', m)}
                              className={`text-micro px-1.5 py-0.5 rounded-[var(--radius-control)] border transition-colors cursor-pointer ${
                                route.modelName === m
                                  ? 'bg-surface-600 text-content-primary font-bold border-line-strong'
                                  : 'bg-surface-700 text-content-muted hover:text-content-primary border-line'
                              }`}
                            >
                              {m.split('-')[0]}
                            </button>
                          ))}
                        </div>
                      )}
                    </div>
                    <input
                      type="text"
                      value={route.modelName}
                      onChange={(e) => handleFieldChange(route.toolKey, 'modelName', e.target.value)}
                      className="w-full px-2.5 py-1.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-xs font-mono text-content-primary focus:outline-none"
                    />
                  </div>

                  {/* Temperature */}
                  <div className="space-y-1 pt-0.5">
                    <div className="flex items-center justify-between text-micro">
                      <span className="text-content-muted">
                        {isEn ? 'Temperature' : 'Sıcaklık (Rastgelelik)'}
                      </span>
                      <span className="text-content-primary font-semibold">
                        {route.temperature?.toFixed(2) ?? '0.20'}
                      </span>
                    </div>
                    <input
                      type="range"
                      min="0.0"
                      max="1.0"
                      step="0.05"
                      value={route.temperature ?? 0.2}
                      onChange={(e) =>
                        handleFieldChange(route.toolKey, 'temperature', parseFloat(e.target.value))
                      }
                      className="w-full accent-content-primary bg-surface-700 h-1.5 rounded-[var(--radius-control)] appearance-none cursor-pointer"
                    />
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
