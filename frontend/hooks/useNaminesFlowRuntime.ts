'use client';

import { useEffect } from 'react';
import { naminesFlow } from '../lib/naminesFlowEventBus';
import { buildContext, matchRules, toastMessageFor } from '../lib/naminesFlowRuntime';
import { renderFlowTemplate } from '../lib/naminesFlowTemplate';
import { useAutomationStore } from '../store/useAutomationStore';
import { useFlowBarStore } from '../store/useFlowBarStore';
import { useFlowFiringStore } from '../store/useFlowFiringStore';
import { useSchemaStore } from '../store/useSchemaStore';
import { useToastStore } from '../store/useToastStore';

/**
 * Namines Flow olay veriyolunu canvas'a bağlar.
 *
 * Bu hook yazılana kadar `naminesFlow.on(...)` üretim kodunda HİÇ
 * çağrılmıyordu — `useSchemaStore` her şema mutasyonunda olay yayınlıyor ama
 * kimse dinlemiyordu. Sonuç: sağ tık menüsünün ürettiği varsayılan
 * `TableDeleted → Toast` kuralı hiçbir zaman görünür bir şey yapmıyordu.
 */
export function useNaminesFlowRuntime(): void {
  useEffect(() => {
    // Dinleyici store'ları `getState()` ile OKUYOR, abone olmuyor: aksi hâlde
    // kural listesi her değiştiğinde abonelik sökülüp yeniden kurulurdu ve
    // tam o anda gelen bir olay kaybolabilirdi.
    return naminesFlow.on('*', (event) => {
      // Flow çubuğundaki duraklatma: kuralları tek tek kapatmadan tüm istemci
      // tarafı tepkileri susturmanın yolu.
      if (useFlowBarStore.getState().paused) return;

      const schema = useSchemaStore.getState().schema;
      // Olaylar tabloyu ID ile taşıyor; ad yalnızca şemada var. Çözücüyü
      // buradan geçirmek `tableName` koşullarının ve şablonlarının istemcide
      // de çalışmasını sağlıyor.
      const context = buildContext(event, (id) => schema?.tables.find(t => t.id === id)?.name);

      const matched = matchRules(event, useAutomationStore.getState().rules, context);
      if (matched.length === 0) return;

      const { markFired } = useFlowFiringStore.getState();
      const { showToast } = useToastStore.getState();
      const projectName = schema?.name ?? '';

      for (const rule of matched) {
        markFired(rule.id);

        // Sunucu "Toast"u hiç işlemez (bkz. AutomationExecutor) — bu aksiyonun
        // tek gerçekleştiği yer burası. Zincirde birden fazla Toast olsa bile
        // ilki kullanılıyor: aynı olay için iki bildirim göstermenin faydası yok.
        const toast = rule.actions.find(a => a.actionType === 'Toast');
        if (!toast) continue;

        const custom = renderFlowTemplate(toast.actionConfig.message, rule.triggerType, context, projectName);
        showToast(custom.trim() !== '' ? custom : toastMessageFor(event), 'info');
      }
    });
  }, []);
}
