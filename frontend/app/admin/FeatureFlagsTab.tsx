'use client';

import React, { useEffect, useMemo, useState } from 'react';
import {
  RefreshCw,
  Search,
  Check,
  AlertTriangle,
  Lock,
  EyeOff,
  SlidersHorizontal,
  X,
  Wrench,
  Boxes,
  Code,
  Sparkles,
  Database,
  Layers,
} from 'lucide-react';
import {
  useAdminConsoleStore,
  type FeatureCategoryFilter,
  type FeatureStatusFilter,
} from '../../store/useAdminConsoleStore';

interface FeatureFlagsTabProps {
  locale?: string;
}

/**
 * Bakım mesajı alanı: her tuş vuruşunda PUT yerine yerel taslak; blur / Enter'da kaydedilir.
 * Boş değer geçerlidir (mesajı varsayılana döndürür).
 */
function NoticeMessageInput({
  value,
  placeholder,
  onCommit,
}: {
  value: string;
  placeholder: string;
  onCommit: (next: string) => void;
}) {
  const [draft, setDraft] = useState(value);
  useEffect(() => setDraft(value), [value]);

  return (
    <input
      type="text"
      value={draft}
      onChange={(e) => setDraft(e.target.value)}
      onBlur={() => { if (draft !== value) onCommit(draft); }}
      onKeyDown={(e) => {
        if (e.key === 'Enter') e.currentTarget.blur();
        else if (e.key === 'Escape') setDraft(value);
      }}
      placeholder={placeholder}
      className="flex-1 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong text-xs text-content-primary placeholder:text-content-muted focus:outline-none"
    />
  );
}

export default function FeatureFlagsTab({ locale = 'tr' }: FeatureFlagsTabProps) {
  const isEn = locale === 'en';

  const {
    flags,
    loadingFlags: loading,
    flagsError: error,
    fetchFlags,
    featureSearch: searchQuery,
    setFeatureSearch: setSearchQuery,
    featureCategory: selectedCategory,
    setFeatureCategory: setSelectedCategory,
    featureStatus: statusFilter,
    setFeatureStatus: setStatusFilter,
    selectedFeatureKey,
    setSelectedFeatureKey,
    expandedDrawerKeys: expandedKeys,
    toggleDrawerExpand: toggleExpand,
    toggleFeatureFlag,
    updateFeatureFlag,
    bulkUpdateCategory,
  } = useAdminConsoleStore();

  useEffect(() => {
    fetchFlags();
  }, [fetchFlags]);

  // Smooth scroll to selected feature card
  useEffect(() => {
    if (selectedFeatureKey) {
      const el = document.getElementById(`flag-card-${selectedFeatureKey}`);
      if (el) {
        el.scrollIntoView({ behavior: 'smooth', block: 'center' });
      }
    }
  }, [selectedFeatureKey]);

  const categories = useMemo(() => {
    const list: { key: FeatureCategoryFilter; label: string; count: number }[] = [
      { key: 'all', label: isEn ? 'All' : 'Tümü', count: flags.length },
      { key: 'Tools', label: 'Tools', count: flags.filter((f) => f.category === 'Tools').length },
      { key: 'Compile', label: 'Compile', count: flags.filter((f) => f.category === 'Compile').length },
      { key: 'Eject', label: 'Eject', count: flags.filter((f) => f.category === 'Eject').length },
      { key: 'Platform', label: 'Platform', count: flags.filter((f) => f.category === 'Platform').length },
      { key: 'AI', label: 'AI', count: flags.filter((f) => f.category === 'AI').length },
    ];
    return list;
  }, [flags, isEn]);

  const stats = useMemo(() => {
    const total = flags.length;
    const enabled = flags.filter((f) => f.isEnabled).length;
    const notice = flags.filter((f) => !f.isEnabled && f.mode === 'DisabledNotice').length;
    const hidden = flags.filter((f) => !f.isEnabled && f.mode === 'Hidden').length;
    return { total, enabled, notice, hidden };
  }, [flags]);

  const filteredFlags = useMemo(() => {
    return flags.filter((f) => {
      const q = searchQuery.toLowerCase().trim();
      const matchesSearch =
        q === '' ||
        f.displayName.toLowerCase().includes(q) ||
        f.featureKey.toLowerCase().includes(q) ||
        f.description.toLowerCase().includes(q);

      const matchesCategory =
        selectedCategory === 'all' ||
        f.category.toLowerCase() === selectedCategory.toLowerCase();

      const matchesStatus =
        statusFilter === 'all' ||
        (statusFilter === 'enabled' && f.isEnabled) ||
        (statusFilter === 'notice' && !f.isEnabled && f.mode === 'DisabledNotice') ||
        (statusFilter === 'hidden' && !f.isEnabled && f.mode === 'Hidden');

      return matchesSearch && matchesCategory && matchesStatus;
    });
  }, [flags, searchQuery, selectedCategory, statusFilter]);

  const getCategoryIcon = (category: string) => {
    switch (category.toLowerCase()) {
      case 'tools':
        return <Wrench className="w-4 h-4 text-content-primary" />;
      case 'compile':
        return <Code className="w-4 h-4 text-content-primary" />;
      case 'eject':
        return <Boxes className="w-4 h-4 text-content-primary" />;
      case 'ai':
        return <Sparkles className="w-4 h-4 text-content-primary" />;
      case 'platform':
      default:
        return <Database className="w-4 h-4 text-content-secondary" />;
    }
  };

  return (
    <div className="font-sans space-y-4">
      {/* ── 1. Compact Header ────────────────────────────────────────── */}
      <div className="rounded-[var(--radius-card)] bg-surface-800 border border-line-strong p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3">
        <div className="space-y-0.5">
          <div className="flex items-center gap-2">
            <h2 className="text-base font-bold text-content-primary">
              {isEn ? 'Feature Flags' : 'Özellik Şalterleri'}
            </h2>
            <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line text-micro font-medium text-content-secondary">
              {stats.enabled}/{stats.total} {isEn ? 'Active' : 'Açık'}
            </span>
          </div>
          <p className="text-xs text-content-muted">
            {isEn
              ? 'Control runtime availability and fallback behavior for all platform capabilities.'
              : 'Tüm sistem bileşenlerinin çalışma durumu ve kapalıyken davranışını yönetin.'}
          </p>
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <button
            onClick={() => fetchFlags()}
            disabled={loading}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-xs font-medium text-content-primary border border-line-strong transition-colors cursor-pointer disabled:opacity-50"
            title={isEn ? 'Refresh' : 'Yenile'}
          >
            <RefreshCw className={`w-3.5 h-3.5 ${loading ? 'animate-spin' : ''}`} />
            <span>{isEn ? 'Refresh' : 'Yenile'}</span>
          </button>
          <button
            onClick={() => bulkUpdateCategory(selectedCategory, true)}
            disabled={loading || filteredFlags.length === 0}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-xs font-semibold text-content-primary border border-line-strong transition-colors cursor-pointer disabled:opacity-50"
          >
            <Check className="w-3.5 h-3.5 text-content-primary" />
            <span>{isEn ? 'Enable All' : 'Tümünü Aç'}</span>
          </button>
          <button
            onClick={() => bulkUpdateCategory(selectedCategory, false)}
            disabled={loading || filteredFlags.length === 0}
            className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-xs font-semibold text-content-secondary border border-line-strong transition-colors cursor-pointer disabled:opacity-50"
          >
            <Lock className="w-3.5 h-3.5 text-content-secondary" />
            <span>{isEn ? 'Set Maintenance' : 'Bakıma Al'}</span>
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
      <div className="rounded-[var(--radius-card)] bg-surface-800 border border-line-strong p-3 space-y-2.5">
        <div className="flex items-center gap-1.5 overflow-x-auto scrollbar-none pb-0.5">
          {categories.map((cat) => {
            const active = selectedCategory === cat.key;
            return (
              <button
                key={cat.key}
                onClick={() => setSelectedCategory(cat.key)}
                className={`inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-medium transition-colors cursor-pointer whitespace-nowrap ${
                  active
                    ? 'bg-surface-600 text-content-primary font-semibold border border-line-strong'
                    : 'bg-surface-700/60 text-content-muted hover:text-content-primary hover:bg-surface-700 border border-line'
                }`}
              >
                <span>{cat.label}</span>
                <span className="text-micro opacity-70">({cat.count})</span>
              </button>
            );
          })}
        </div>

        <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-2.5 pt-1 border-t border-line">
          <div className="relative flex-1">
            <Search className="absolute left-2.5 top-2.5 w-3.5 h-3.5 text-content-muted" />
            <input
              type="text"
              value={searchQuery}
              onChange={(e) => setSearchQuery(e.target.value)}
              placeholder={isEn ? 'Search features or keys...' : 'Özellik veya anahtar ara...'}
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

          <div className="flex items-center gap-1 shrink-0">
            {(['all', 'enabled', 'notice', 'hidden'] as FeatureStatusFilter[]).map((st) => {
              const active = statusFilter === st;
              const labels: Record<FeatureStatusFilter, string> = {
                all: isEn ? 'All' : 'Tümü',
                enabled: isEn ? 'Active' : 'Açık',
                notice: isEn ? 'Maintenance' : 'Bakım',
                hidden: isEn ? 'Hidden' : 'Gizli',
              };
              return (
                <button
                  key={st}
                  onClick={() => setStatusFilter(st)}
                  className={`px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-medium transition-colors cursor-pointer ${
                    active
                      ? 'bg-surface-600 text-content-primary font-bold border border-line-strong'
                      : 'text-content-muted hover:text-content-primary hover:bg-surface-700'
                  }`}
                >
                  {labels[st]}
                </button>
              );
            })}
          </div>
        </div>
      </div>

      {/* ── 3. Clean Feature Rows List ────────────────────────────── */}
      {loading && flags.length === 0 ? (
        <div className="py-10 text-center text-xs text-content-muted rounded-[var(--radius-card)] bg-surface-800 border border-line-strong">
          <RefreshCw className="w-5 h-5 animate-spin mx-auto mb-2 text-content-muted" />
          <span>{isEn ? 'Loading features...' : 'Özellikler yükleniyor...'}</span>
        </div>
      ) : filteredFlags.length === 0 ? (
        <div className="py-10 text-center text-xs text-content-muted rounded-[var(--radius-card)] bg-surface-800 border border-line-strong">
          {isEn ? 'No features found matching the criteria.' : 'Kriterlere uygun özellik bulunamadı.'}
        </div>
      ) : (
        <div className="space-y-2">
          {filteredFlags.map((flag) => {
            const isExpanded = !!expandedKeys[flag.featureKey];
            const isSelected = selectedFeatureKey?.toLowerCase() === flag.featureKey.toLowerCase();

            return (
              <div
                key={flag.featureKey}
                id={`flag-card-${flag.featureKey}`}
                className={`rounded-[var(--radius-card)] bg-surface-800 border transition-all p-3.5 ${
                  isSelected
                    ? 'border-line-strong bg-surface-750 ring-2 ring-content-secondary/40'
                    : 'border-line-strong/70 hover:border-line-strong'
                }`}
              >
                <div
                  onClick={() => setSelectedFeatureKey(flag.featureKey)}
                  className="flex items-center justify-between gap-3 cursor-pointer"
                >
                  {/* Left: Icon & Title */}
                  <div className="flex items-center gap-3 min-w-0">
                    <div className="p-2 rounded-[var(--radius-control)] bg-surface-700 border border-line shrink-0">
                      {getCategoryIcon(flag.category)}
                    </div>
                    <div className="min-w-0">
                      <div className="flex items-center gap-2">
                        <span className="font-semibold text-sm text-content-primary truncate">
                          {flag.displayName}
                        </span>
                        <span className="px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line text-[10px] font-mono text-content-muted">
                          {flag.featureKey}
                        </span>
                      </div>
                      <p className="text-xs text-content-muted truncate mt-0.5">
                        {flag.description || flag.category}
                      </p>
                    </div>
                  </div>

                  {/* Right: Badges, High-Contrast Switch, Settings */}
                  <div
                    className="flex items-center gap-3 shrink-0"
                    onClick={(e) => e.stopPropagation()}
                  >
                    {/* Status Badge (no green!) */}
                    {!flag.isEnabled && flag.mode === 'DisabledNotice' && (
                      <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line text-micro text-content-secondary flex items-center gap-1">
                        <Lock className="w-3 h-3" />
                        <span>{isEn ? 'Maintenance' : 'Bakım'}</span>
                      </span>
                    )}
                    {!flag.isEnabled && flag.mode === 'Hidden' && (
                      <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 border border-line text-micro text-content-muted flex items-center gap-1">
                        <EyeOff className="w-3 h-3" />
                        <span>{isEn ? 'Hidden' : 'Gizli'}</span>
                      </span>
                    )}

                    {/* HIGH-CONTRAST SHOPIFY POLARIS SWITCH */}
                    <div className="flex items-center gap-2">
                      <button
                        type="button"
                        role="switch"
                        aria-checked={flag.isEnabled}
                        onClick={() => toggleFeatureFlag(flag.featureKey)}
                        className={`relative inline-flex h-6 w-11 shrink-0 cursor-pointer rounded-full p-0.5 transition-colors duration-200 ease-in-out focus:outline-none ${
                          flag.isEnabled
                            ? 'bg-surface-600 border border-content-secondary'
                            : 'bg-surface-700 border border-line-strong'
                        }`}
                        title={flag.isEnabled ? 'Kapat' : 'Aç'}
                      >
                        <span
                          className={`pointer-events-none inline-block h-5 w-5 transform rounded-full shadow-md transition duration-200 ease-in-out ${
                            flag.isEnabled
                              ? 'translate-x-5 bg-content-primary'
                              : 'translate-x-0 bg-content-muted'
                          }`}
                        />
                      </button>
                      <span
                        className={`text-xs font-semibold w-10 text-left select-none ${
                          flag.isEnabled ? 'text-content-primary' : 'text-content-muted'
                        }`}
                      >
                        {flag.isEnabled ? (isEn ? 'ON' : 'Açık') : (isEn ? 'OFF' : 'Kapalı')}
                      </span>
                    </div>

                    {/* Settings Trigger */}
                    <button
                      onClick={() => toggleExpand(flag.featureKey)}
                      className={`p-1.5 rounded-[var(--radius-control)] border transition-colors cursor-pointer ${
                        isExpanded
                          ? 'bg-surface-600 border-line-strong text-content-primary'
                          : 'bg-surface-700 hover:bg-surface-600 border-line text-content-muted hover:text-content-primary'
                      }`}
                      title={isEn ? 'Settings' : 'Ayarlar'}
                    >
                      <SlidersHorizontal className="w-3.5 h-3.5" />
                    </button>
                  </div>
                </div>

                {/* ── 4. Compact Settings Drawer (No Bloatware Text) ───────── */}
                {isExpanded && (
                  <div className="mt-3 pt-3 border-t border-line space-y-2.5">
                    <div className="flex flex-wrap items-center gap-4 text-xs">
                      <span className="font-semibold text-content-secondary">
                        {isEn ? 'When disabled:' : 'Kapalıyken:'}
                      </span>
                      <label className="flex items-center gap-1.5 cursor-pointer text-content-primary">
                        <input
                          type="radio"
                          name={`mode-${flag.featureKey}`}
                          checked={flag.mode === 'DisabledNotice'}
                          onChange={() => updateFeatureFlag(flag.featureKey, { mode: 'DisabledNotice' })}
                          className="accent-content-primary"
                        />
                        <span>{isEn ? 'Show maintenance notice' : 'Bakım uyarısı göster'}</span>
                      </label>
                      <label className="flex items-center gap-1.5 cursor-pointer text-content-primary">
                        <input
                          type="radio"
                          name={`mode-${flag.featureKey}`}
                          checked={flag.mode === 'Hidden'}
                          onChange={() => updateFeatureFlag(flag.featureKey, { mode: 'Hidden' })}
                          className="accent-content-primary"
                        />
                        <span>{isEn ? 'Purge from UI' : 'Arayüzden tamamen gizle'}</span>
                      </label>
                    </div>

                    {flag.mode === 'DisabledNotice' && (
                      <div className="flex items-center gap-2">
                        <NoticeMessageInput
                          value={flag.noticeMessage || ''}
                          onCommit={(next) =>
                            updateFeatureFlag(flag.featureKey, { noticeMessage: next })
                          }
                          placeholder={
                            isEn
                              ? 'Custom notice message (optional)...'
                              : 'Özel bakım mesajı (opsiyonel)...'
                          }
                        />
                      </div>
                    )}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
