'use client';

import React from 'react';
import { useFeatureStore } from '../../store/useFeatureStore';
import { Lock } from 'lucide-react';

interface FeatureGuardProps {
  featureKey: string;
  children: React.ReactNode;
  fallback?: React.ReactNode;
  showNotice?: boolean;
  className?: string;
}

/**
 * FeatureGuard: Admin Panelinden yönetilen kill-switch ve özellik bayrakları koruyucusu.
 *
 * - IsEnabled = true -> İçeriği normal render eder.
 * - IsEnabled = false && Mode = 'Hidden' -> Tamamen gizler (null döner veya fallback render eder).
 * - IsEnabled = false && Mode = 'DisabledNotice' -> Görünür tutar fakat tıklanamaz hale getirir
 *   ve üzerine/altına bilgilendirme uyarısı ekler.
 */
export default function FeatureGuard({
  featureKey,
  children,
  fallback,
  showNotice = true,
  className = '',
}: FeatureGuardProps) {
  const { isFeatureEnabled, getFeatureState } = useFeatureStore();

  const enabled = isFeatureEnabled(featureKey);
  const state = getFeatureState(featureKey);

  if (enabled) {
    return <>{children}</>;
  }

  // Mod: Hidden -> Arayüzden tamamen yok olur
  if (state.mode === 'Hidden') {
    return fallback ? <>{fallback}</> : null;
  }

  // Mod: DisabledNotice -> Görünür ama pasif, uyarı mesajıyla
  if (fallback) {
    return <>{fallback}</>;
  }

  return (
    <div className={`relative group ${className}`}>
      <div className="opacity-50 pointer-events-none cursor-not-allowed select-none filter grayscale">
        {children}
      </div>

      {showNotice && (
        <div
          role="status"
          aria-live="polite"
          className="absolute inset-0 flex items-center justify-center p-2 bg-surface-900/80 backdrop-blur-[2px] rounded-[var(--radius-card)] border border-line"
        >
          <div className="flex items-center gap-2 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 border border-line-strong shadow-lg max-w-sm text-center">
            <Lock className="w-4 h-4 text-warning-text shrink-0" aria-hidden="true" />
            <span className="text-xs text-content-primary font-medium">
              {state.noticeMessage || 'Bu özellik geçici olarak devre dışıdır.'}
            </span>
          </div>
        </div>
      )}
    </div>
  );
}
