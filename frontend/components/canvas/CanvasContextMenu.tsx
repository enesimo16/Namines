'use client';

import { useCallback, useState } from 'react';
import * as ContextMenu from '@radix-ui/react-context-menu';
import { useReactFlow } from '@xyflow/react';
import { Plus, Trash2, Pencil, Table2, Copy, Zap } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAutomationStore } from '../../store/useAutomationStore';
import { useToastStore } from '../../store/useToastStore';

interface ContextMenuState {
  x: number;
  y: number;
  type: 'canvas' | 'node';
  nodeId?: string;
  flowX?: number;
  flowY?: number;
}

interface CanvasContextMenuProps {
  children: React.ReactNode;
}

/**
 * Radix ContextMenu for right-click on the canvas.
 * - Right-click on empty space → "Add New Table"
 * - Right-click on node → "Edit" + "Delete Table"
 */
export default function CanvasContextMenu({ children }: CanvasContextMenuProps) {
  const { screenToFlowPosition } = useReactFlow();
  const { isEditMode, schema, addTable, deleteTable, duplicateTable, setSelectedTableForEdit } = useSchemaStore();
  const addAutomationRule = useAutomationStore(s => s.addRule);
  const deleteRulesForTable = useAutomationStore(s => s.deleteRulesForTable);
  const showToast = useToastStore(s => s.showToast);
  const [menuState, setMenuState] = useState<ContextMenuState | null>(null);

  // Sağ tıklanan node bir GERÇEK tablo mu yoksa bir Namines Flow node'u mu?
  // Otomasyon node'unun kendi üzerinde "Delete Table"/"Add to Namines Flow"
  // göstermek anlamsız olurdu — o node bir tablo değil.
  const isTableNode = !!menuState?.nodeId && !!schema?.tables.some(t => t.id === menuState.nodeId);

  const handleContextMenu = useCallback((e: React.MouseEvent) => {
    if (!isEditMode) {
      // Sessizce hiçbir şey açmamak, "sağ tık kırık" izlenimi veriyordu —
      // menü zaten görüntüleme modunda yok, ama kullanıcı NEDENİNİ bilmeli.
      e.preventDefault();
      showToast('Right-click actions need Edit Mode — click the pencil icon to turn it on.', 'info');
      return;
    }

    // Was a node clicked?
    const nodeEl = (e.target as HTMLElement).closest('[data-id]');
    const nodeId = nodeEl?.getAttribute('data-id') ?? undefined;

    // Convert screen coordinate to flow coordinate
    const flowPos = screenToFlowPosition({ x: e.clientX, y: e.clientY });

    setMenuState({
      x: e.clientX,
      y: e.clientY,
      type: nodeId ? 'node' : 'canvas',
      nodeId,
      flowX: flowPos.x,
      flowY: flowPos.y,
    });
  }, [isEditMode, screenToFlowPosition]);

  const handleAddTable = () => {
    if (!menuState) return;
    addTable(menuState.flowX ?? 0, menuState.flowY ?? 0);
    setMenuState(null);
  };

  const handleDeleteTable = () => {
    if (!menuState?.nodeId) return;
    // Kural silme önce: tablo silindikten sonra artık hangi tabloya ait
    // olduğunu bilmenin yolu kalmaz.
    deleteRulesForTable(menuState.nodeId);
    deleteTable(menuState.nodeId);
    setMenuState(null);
  };

  /**
   * Namines Flow node'unu seçilen tabloya bağlı olarak ekler. Node'un
   * kendisi React Flow state'inde YAŞAMAZ — AutomationNode, rule
   * listesinden TÜRETİLİR (bkz. canvas/page.tsx), bu yüzden burada
   * yalnızca kural oluşturuluyor.
   */
  const handleAddAutomation = () => {
    if (!menuState?.nodeId) return;
    addAutomationRule(menuState.nodeId, 'TableDeleted', 'Toast');
    setMenuState(null);
  };

  const handleEditTable = () => {
    if (!menuState?.nodeId) return;
    setSelectedTableForEdit(menuState.nodeId);
    setMenuState(null);
  };

  return (
    <ContextMenu.Root>
      <ContextMenu.Trigger asChild onContextMenu={handleContextMenu}>
        <div className="w-full h-full">
          {children}
        </div>
      </ContextMenu.Trigger>

      {isEditMode && (
        <ContextMenu.Portal>
          <ContextMenu.Content
            className="min-w-[180px] bg-gradient-to-b from-surface-700/95 to-surface-600/95 backdrop-blur-md rounded-[var(--radius-card)] border border-content-primary/12 p-1.5 shadow-[0_8px_30px_color-mix(in srgb, var(--color-scrim) 40%, transparent),0_0_15px_color-mix(in srgb, var(--color-accent-hover) 15%, transparent)] z-[100] animate-in fade-in-0 zoom-in-95 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95"
            onCloseAutoFocus={e => e.preventDefault()}
          >
            {menuState?.type === 'canvas' && (
              <>
                <div className="px-2 py-1.5 text-xs font-semibold text-content-muted uppercase tracking-wider mb-1">Canvas</div>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-content-primary/12 hover:text-content-primary focus:bg-content-primary/12 focus:text-content-primary"
                  onSelect={handleAddTable}
                >
                  <Plus className="w-4 h-4 text-accent-text" />
                  <span>Add New Table</span>
                </ContextMenu.Item>
              </>
            )}

            {menuState?.type === 'node' && isTableNode && (
              <>
                <div className="flex items-center gap-2 px-2 py-1.5 text-xs font-semibold text-content-muted uppercase tracking-wider mb-1">
                  <Table2 className="w-3.5 h-3.5" />
                  Table Operations
                </div>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-content-primary/12 hover:text-content-primary focus:bg-content-primary/12 focus:text-content-primary"
                  onSelect={handleEditTable}
                >
                  <Pencil className="w-4 h-4 text-accent-text" />
                  <span>Edit</span>
                </ContextMenu.Item>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-accent/20 hover:text-accent-text focus:bg-accent/20 focus:text-accent-text"
                  onSelect={() => { if (menuState?.nodeId) { duplicateTable(menuState.nodeId); setMenuState(null); } }}
                >
                  <Copy className="w-4 h-4 text-accent-text" />
                  <span>Duplicate Table</span>
                </ContextMenu.Item>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-warning/20 hover:text-warning-text focus:bg-warning/20 focus:text-warning-text"
                  onSelect={handleAddAutomation}
                >
                  <Zap className="w-4 h-4 text-warning-text" />
                  <span>Add to Namines Flow</span>
                </ContextMenu.Item>
                <ContextMenu.Separator className="h-px bg-content-primary/[0.06] my-1 mx-1" />
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-danger/20 hover:text-danger-text focus:bg-danger/20 focus:text-danger-text group"
                  onSelect={handleDeleteTable}
                >
                  <Trash2 className="w-4 h-4 text-danger-text/80 group-hover:text-danger-text" />
                  <span>Delete Table</span>
                </ContextMenu.Item>
              </>
            )}
          </ContextMenu.Content>
        </ContextMenu.Portal>
      )}
    </ContextMenu.Root>
  );
}
