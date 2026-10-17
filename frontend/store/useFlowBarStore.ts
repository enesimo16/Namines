'use client';

import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/**
 * Canvas üzerindeki taşınabilir Namines Flow çubuğunun durumu.
 *
 * Çubuk bilerek Edit Mode'dan bağımsız: bir flow kurmak şemayı değiştirmiyor,
 * bu yüzden özelliğin giriş kapısı düzenleme moduna kilitli olmamalı.
 *
 * `useFlowNodePositionStore` ile aynı nedenle localStorage'a yazılıyor ve
 * buluta senkronlanmıyor — konum ve "duraklat" tamamen kişisel, cihaza özgü
 * bir çalışma tercihi.
 */
interface FlowBarState {
  position: { x: number; y: number };
  /** Kullanıcı çubuğu kapattı mı? Araç çubuğundan geri açılabiliyor. */
  hidden: boolean;
  /** Açıkken istemci tarafı tepkiler (toast + node animasyonu) susturulur. */
  paused: boolean;
  panelOpen: boolean;
  /** Açıkken tıklanan tablo yeni bir flow'un çapası olur. */
  pickingTable: boolean;

  setPosition: (position: { x: number; y: number }) => void;
  setHidden: (hidden: boolean) => void;
  togglePaused: () => void;
  setPanelOpen: (open: boolean) => void;
  setPickingTable: (picking: boolean) => void;
}

/**
 * Header (h: 56px) altında, proje kartının yanında duran varsayılan konum.
 * y: 72px başlığın altındadır ve başlıkla çakışmaz.
 */
const DEFAULT_POSITION = { x: 288, y: 72 };

export const useFlowBarStore = create<FlowBarState>()(
  persist(
    (set) => ({
      position: DEFAULT_POSITION,
      hidden: false,
      paused: false,
      panelOpen: false,
      pickingTable: false,

      setPosition: (position) => set({ position }),
      setHidden: (hidden) => set({ hidden, panelOpen: false, pickingTable: false }),
      togglePaused: () => set(state => ({ paused: !state.paused })),
      setPanelOpen: (panelOpen) => set({ panelOpen }),
      setPickingTable: (pickingTable) => set({ pickingTable }),
    }),
    {
      name: 'namines-flow-bar',
      version: 4,
      migrate: (persistedState: unknown) => {
        const s = persistedState as { position?: { x: number; y: number }; hidden?: boolean };
        if (!s?.position || s.position.y < 68 || s.position.y > 600 || s.position.x < 10) {
          if (s) {
            s.position = DEFAULT_POSITION;
            s.hidden = false;
          }
        }
        return s as FlowBarState;
      },
      partialize: (state) => ({
        position: state.position,
        hidden: state.hidden,
        paused: state.paused,
      }),
    },
  ),
);
