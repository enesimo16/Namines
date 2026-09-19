'use client';

import { useCallback, useRef, useState } from 'react';
import { GripVertical, Pause, Play, Plus, List, X, Zap } from 'lucide-react';
import { useFlowBarStore } from '../../../store/useFlowBarStore';
import { useAutomationStore } from '../../../store/useAutomationStore';

const BAR_WIDTH = 260;
const BAR_HEIGHT = 44;

/**
 * Canvas üzerinde serbestçe taşınabilen Namines Flow kumanda çubuğu.
 *
 * Bu çubuk olmadan özelliğin tek giriş kapısı, Edit Mode'a kilitli bir sağ tık
 * menüsündeki tek satırdı — sistem "var ama görünmüyor" durumundaydı.
 *
 * Projede hazır bir sürüklenebilir panel deseni yok, bu yüzden sürükleme
 * burada pointer capture ile elle yapılıyor: fare çubuğun dışına çıksa bile
 * olaylar gelmeye devam ediyor, bırakınca kendiliğinden kopuyor.
 */
export default function NaminesFlowBar() {
  const position = useFlowBarStore(s => s.position);
  const hidden = useFlowBarStore(s => s.hidden);
  const paused = useFlowBarStore(s => s.paused);
  const panelOpen = useFlowBarStore(s => s.panelOpen);
  const pickingTable = useFlowBarStore(s => s.pickingTable);
  const setPosition = useFlowBarStore(s => s.setPosition);
  const setHidden = useFlowBarStore(s => s.setHidden);
  const togglePaused = useFlowBarStore(s => s.togglePaused);
  const setPanelOpen = useFlowBarStore(s => s.setPanelOpen);
  const setPickingTable = useFlowBarStore(s => s.setPickingTable);

  const ruleCount = useAutomationStore(s => s.rules.length);

  const dragRef = useRef<{ pointerId: number; dx: number; dy: number } | null>(null);
  const [dragging, setDragging] = useState(false);

  const handlePointerDown = useCallback((e: React.PointerEvent) => {
    if (e.button !== 0) return;
    dragRef.current = {
      pointerId: e.pointerId,
      dx: e.clientX - position.x,
      dy: e.clientY - position.y,
    };
    e.currentTarget.setPointerCapture(e.pointerId);
    setDragging(true);
  }, [position.x, position.y]);

  const handlePointerMove = useCallback((e: React.PointerEvent) => {
    const drag = dragRef.current;
    if (!drag || drag.pointerId !== e.pointerId) return;
    // Görünür alanda tut — çubuk pencere dışına kaçarsa geri getirmenin bir
    // yolu kalmaz (konum localStorage'da kalıcı olduğu için yenilemek de
    // kurtarmaz).
    const maxX = Math.max(0, window.innerWidth - BAR_WIDTH);
    const maxY = Math.max(0, window.innerHeight - BAR_HEIGHT);
    setPosition({
      x: Math.min(Math.max(0, e.clientX - drag.dx), maxX),
      y: Math.min(Math.max(0, e.clientY - drag.dy), maxY),
    });
  }, [setPosition]);

  const endDrag = useCallback((e: React.PointerEvent) => {
    if (dragRef.current?.pointerId !== e.pointerId) return;
    dragRef.current = null;
    setDragging(false);
  }, []);

  // Gizlenmiş çubuk TAMAMEN kaybolmuyor: konum ve gizlilik localStorage'da
  // kalıcı olduğu için sayfayı yenilemek de geri getirmezdi ve özellik
  // erişilemez hâle gelirdi. Yerinde küçük bir düğme kalıyor.
  if (hidden) {
    return (
      <button
        type="button"
        style={{ left: position.x, top: position.y }}
        onClick={() => setHidden(false)}
        aria-label="Show the Namines Flow bar"
        title="Namines Flow"
        className="nodrag nopan fixed z-[80] flex items-center gap-1 rounded-full border border-warning/40 bg-surface-800/95 px-2 py-1 shadow-lg backdrop-blur-md transition-colors hover:border-warning cursor-pointer"
      >
        <Zap className="h-3.5 w-3.5 text-warning-text" />
        <span className="text-micro font-semibold text-warning-text">Flow</span>
      </button>
    );
  }

  const buttonClass =
    'flex items-center gap-1 rounded-[var(--radius-control)] px-2 py-1 text-xs font-medium text-content-secondary transition-colors hover:bg-content-primary/12 hover:text-content-primary cursor-pointer';

  return (
    <div
      style={{ left: position.x, top: position.y, width: BAR_WIDTH }}
      className={`nodrag nopan fixed z-[80] flex items-center gap-1 rounded-[var(--radius-card)] border bg-surface-800/95 px-1.5 py-1.5 backdrop-blur-md transition-shadow ${
        pickingTable ? 'border-warning' : 'border-content-primary/12'
      } ${dragging ? 'shadow-2xl' : 'shadow-lg'}`}
    >
      <button
        type="button"
        aria-label="Move the Namines Flow bar"
        onPointerDown={handlePointerDown}
        onPointerMove={handlePointerMove}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        className={`shrink-0 rounded-[var(--radius-control)] p-1 text-content-muted hover:text-content-primary ${
          dragging ? 'cursor-grabbing' : 'cursor-grab'
        }`}
      >
        <GripVertical className="h-4 w-4" />
      </button>

      <Zap className="h-3.5 w-3.5 shrink-0 text-warning-text" />
      <span className="shrink-0 text-xs font-semibold text-content-primary">Flow</span>
      <span className="shrink-0 rounded-full bg-surface-600 px-1.5 text-micro text-content-secondary">
        {ruleCount}
      </span>

      <div className="ml-auto flex items-center gap-0.5">
        <button
          type="button"
          onClick={() => setPickingTable(!pickingTable)}
          className={buttonClass}
          title={pickingTable ? 'Cancel — pick a table to attach the flow to' : 'Create a new flow'}
          aria-pressed={pickingTable}
        >
          <Plus className="h-3.5 w-3.5" />
          <span>{pickingTable ? 'Pick…' : 'New'}</span>
        </button>

        <button
          type="button"
          onClick={() => setPanelOpen(!panelOpen)}
          className={buttonClass}
          title="Show all flows"
          aria-pressed={panelOpen}
        >
          <List className="h-3.5 w-3.5" />
        </button>

        <button
          type="button"
          onClick={togglePaused}
          className={buttonClass}
          title={paused ? 'Resume flows' : 'Pause all flows'}
          aria-pressed={paused}
        >
          {paused ? <Play className="h-3.5 w-3.5 text-warning-text" /> : <Pause className="h-3.5 w-3.5" />}
        </button>

        <button
          type="button"
          onClick={() => setHidden(true)}
          className={buttonClass}
          title="Hide the bar"
          aria-label="Hide the Namines Flow bar"
        >
          <X className="h-3.5 w-3.5" />
        </button>
      </div>
    </div>
  );
}
