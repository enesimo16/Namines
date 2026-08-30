'use client';

import { useMultiplayerStore } from '../../store/useMultiplayerStore';
import { useViewport } from '@xyflow/react';
import { MousePointer2 } from 'lucide-react';

/**
 * Odadaki diğer kullanıcıların imleçlerini gösterir.
 *
 * Koordinatlar FLOW uzayındadır (canvas'a sabit), ekran uzayında değil. Peer'lar
 * farklı pan/zoom'da olduğu için ekran pikseli göndermek imleci yanlış yere düşürür;
 * bu yüzden gönderen tarafta flow uzayına çevrilir, burada aktif viewport ile geri
 * ekran uzayına taşınır.
 *
 * ReactFlowProvider içinde render EDİLMELİ (useViewport gerektirir).
 */
export default function MultiplayerCursors() {
  const cursors = useMultiplayerStore(s => s.cursors);
  const { x: panX, y: panY, zoom } = useViewport();

  return (
    <div className="pointer-events-none absolute inset-0 z-[40] overflow-hidden">
      {Object.entries(cursors).map(([connectionId, cursor]) => {
        // flow → ekran dönüşümü (React Flow'un uyguladığı transform ile aynı)
        const screenX = cursor.x * zoom + panX;
        const screenY = cursor.y * zoom + panY;

        return (
          <div
            key={connectionId}
            className="absolute transition-transform duration-75 ease-linear will-change-transform"
            style={{ transform: `translate(${screenX}px, ${screenY}px)` }}
            aria-hidden="true"
          >
            <MousePointer2
              className="w-4 h-4 drop-shadow-md"
              style={{ color: cursor.color, fill: cursor.color }}
            />
            <span
              className="ml-3 -mt-1 inline-block rounded-[var(--radius-control)] px-1.5 py-0.5 text-[10px] font-semibold text-content-primary whitespace-nowrap shadow-md"
              style={{ backgroundColor: cursor.color }}
            >
              {cursor.userName}
            </span>
          </div>
        );
      })}
    </div>
  );
}
