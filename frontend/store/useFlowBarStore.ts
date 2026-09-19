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
 * Sol kenarda, proje kartının ALTINDA kalan ilk boş şerit. Kart yaklaşık
 * 290px'e kadar iniyor; daha yukarısı ilk açılışta onun üstüne biniyordu.
 */
const DEFAULT_POSITION = { x: 24, y: 320 };

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
      // `panelOpen`/`pickingTable` oturumluk: yarım kalmış bir "tablo seç"
      // modunun sayfa yenilendiğinde geri gelmesi kullanıcıyı şaşırtırdı.
      partialize: (state) => ({
        position: state.position,
        hidden: state.hidden,
        paused: state.paused,
      }),
    },
  ),
);
