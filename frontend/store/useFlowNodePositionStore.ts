'use client';

import { create } from 'zustand';
import { persist } from 'zustand/middleware';

/**
 * Namines Flow düğümlerinin canvas'taki elle sürüklenmiş konumu.
 *
 * **Neden ayrı bir depo, `useSchemaStore`/`nodePositions` değil:** Flow
 * düğümleri bilerek React Flow'un kalıcı node state'inde YAŞAMIYOR — her
 * render'da kural listesinden (`useAutomationStore`) TÜRETİLİYOR (bkz.
 * app/canvas/page.tsx). Bu, undo/redo ve şema pozisyon kaydı gibi
 * mekanizmaların otomasyon kurallarını hiç bilmesine gerek bırakmıyordu —
 * gerçek bir mimari karardı, bozulmamalı.
 *
 * Ama bu yüzden düğümün pozisyonu da HER render'da `ankor tablo + sabit
 * ofset` olarak YENİDEN hesaplanıyordu. Kullanıcı düğümü sürükleyince
 * React Flow'un kendi anlık state'i geçici olarak hareket ettiriyordu, ama
 * bir sonraki render (herhangi bir store güncellemesi — imleç yayını,
 * şema değişikliği, ne olursa) pozisyonu sabit ofsete GERİ YAZIYORDU.
 * Kullanıcıya "sürükleyemiyorum" gibi görünüyordu; aslında sürüklüyordu,
 * sadece hiçbir yerde kalıcı olmuyordu.
 *
 * Çözüm: sürüklenmiş konumu BURADA, kural id'sine göre ayrı tutuyoruz.
 * `nodePositions`'un aksine proje/bulut senkronuna dahil değil — bu
 * sadece görsel bir düzenleme tercihi, cihazlar arası taşınması
 * gerekmiyor; localStorage yeterli.
 */
interface FlowNodePositionState {
  /** ruleId -> canvas konumu. */
  positions: Record<string, { x: number; y: number }>;
  setPosition: (ruleId: string, position: { x: number; y: number }) => void;
  clearPosition: (ruleId: string) => void;
}

export const useFlowNodePositionStore = create<FlowNodePositionState>()(
  persist(
    (set) => ({
      positions: {},
      setPosition: (ruleId, position) =>
        set(state => ({ positions: { ...state.positions, [ruleId]: position } })),
      clearPosition: (ruleId) =>
        set(state => {
          if (!(ruleId in state.positions)) return state;
          const next = { ...state.positions };
          delete next[ruleId];
          return { positions: next };
        }),
    }),
    { name: 'namines-flow-node-positions' },
  ),
);
