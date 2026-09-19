'use client';

import { useEffect } from 'react';
import { naminesFlow } from '../lib/naminesFlowEventBus';
import { matchRules, toastMessageFor } from '../lib/naminesFlowRuntime';
import { useAutomationStore } from '../store/useAutomationStore';
import { useFlowBarStore } from '../store/useFlowBarStore';
import { useFlowFiringStore } from '../store/useFlowFiringStore';
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

      const matched = matchRules(event, useAutomationStore.getState().rules);
      if (matched.length === 0) return;

      const { markFired } = useFlowFiringStore.getState();
      const { showToast } = useToastStore.getState();

      for (const rule of matched) {
        markFired(rule.id);
        // Sunucu "Toast"u hiç işlemez (bkz. AutomationExecutor) — bu aksiyonun
        // tek gerçekleştiği yer burası. Zincirde birden fazla Toast olsa bile
        // tek bildirim çıkıyor: aynı olay için aynı metni iki kez göstermenin
        // kullanıcıya bir faydası yok.
        if (rule.actions.some(a => a.actionType === 'Toast')) {
          showToast(toastMessageFor(event), 'info');
        }
      }
    });
  }, []);
}
