'use client';

import { create } from 'zustand';

/**
 * Bir Namines Flow kuralının "az önce tetiklendi" durumu.
 *
 * `useFlowNodePositionStore`'un aksine BU DEPO KALICI DEĞİL — localStorage'a
 * da buluta da yazılmaz. Tek işi, canvas'taki node ve edge'in kısa bir süre
 * parlaması için geçici bir işaret tutmak. Kalıcı olsaydı sayfa her açıldığında
 * hiç tetiklenmemiş kurallar parlıyor olurdu.
 */
export const FLOW_FIRING_DURATION_MS = 1200;

interface FlowFiringState {
  /** ruleId -> tetiklenme zamanı (ms). Girdinin VARLIĞI "şu an parlıyor" demek. */
  firings: Record<string, number>;
  markFired: (ruleId: string) => void;
}

/**
 * Temizleme zamanlayıcıları store'un DIŞINDA tutuluyor: state'in içinde
 * dursalardı her tetiklenme, zamanlayıcı nesnesi değiştiği için node'ları
 * gereksiz yere yeniden render ettirirdi.
 */
const timers = new Map<string, ReturnType<typeof setTimeout>>();

export const useFlowFiringStore = create<FlowFiringState>((set) => ({
  firings: {},

  markFired: (ruleId) => {
    // Arka arkaya tetiklenmelerde animasyon baştan başlasın — eski zamanlayıcı
    // iptal edilmezse ilk tetiklenmenin süresi dolduğunda ikincisi de sönerdi.
    const existing = timers.get(ruleId);
    if (existing) clearTimeout(existing);

    set(state => ({ firings: { ...state.firings, [ruleId]: Date.now() } }));

    timers.set(
      ruleId,
      setTimeout(() => {
        timers.delete(ruleId);
        set(state => {
          if (!(ruleId in state.firings)) return state;
          const next = { ...state.firings };
          delete next[ruleId];
          return { firings: next };
        });
      }, FLOW_FIRING_DURATION_MS),
    );
  },
}));
