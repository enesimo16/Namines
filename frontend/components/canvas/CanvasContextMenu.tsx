'use client';

import { useCallback, useState } from 'react';
import * as ContextMenu from '@radix-ui/react-context-menu';
import { useReactFlow } from '@xyflow/react';
import { Plus, Trash2, Pencil, Table2, Copy } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';

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
  const { isEditMode, addTable, deleteTable, duplicateTable, setSelectedTableForEdit } = useSchemaStore();
  const [menuState, setMenuState] = useState<ContextMenuState | null>(null);

  const handleContextMenu = useCallback((e: React.MouseEvent) => {
    if (!isEditMode) return; // Don't show menu if not in edit mode

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
    deleteTable(menuState.nodeId);
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
            className="min-w-[180px] bg-gradient-to-b from-surface-700/95 to-surface-600/95 backdrop-blur-md rounded-xl border border-indigo-500/20 p-1.5 shadow-[0_8px_30px_rgb(0,0,0,0.4),0_0_15px_rgba(59,130,246,0.15)] z-[100] animate-in fade-in-0 zoom-in-95 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95"
            onCloseAutoFocus={e => e.preventDefault()}
          >
            {menuState?.type === 'canvas' && (
              <>
                <div className="px-2 py-1.5 text-xs font-semibold text-indigo-300/80 uppercase tracking-wider mb-1">Canvas</div>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-zinc-200 rounded-lg cursor-pointer outline-none transition-colors hover:bg-indigo-500/20 hover:text-indigo-200 focus:bg-indigo-500/20 focus:text-indigo-200"
                  onSelect={handleAddTable}
                >
                  <Plus className="w-4 h-4 text-indigo-400" />
                  <span>Add New Table</span>
                </ContextMenu.Item>
              </>
            )}

            {menuState?.type === 'node' && (
              <>
                <div className="flex items-center gap-2 px-2 py-1.5 text-xs font-semibold text-indigo-300/80 uppercase tracking-wider mb-1">
                  <Table2 className="w-3.5 h-3.5" />
                  Table Operations
                </div>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-zinc-200 rounded-lg cursor-pointer outline-none transition-colors hover:bg-indigo-500/20 hover:text-indigo-200 focus:bg-indigo-500/20 focus:text-indigo-200"
                  onSelect={handleEditTable}
                >
                  <Pencil className="w-4 h-4 text-indigo-400" />
                  <span>Edit</span>
                </ContextMenu.Item>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-zinc-200 rounded-lg cursor-pointer outline-none transition-colors hover:bg-sky-500/20 hover:text-sky-200 focus:bg-sky-500/20 focus:text-sky-200"
                  onSelect={() => { if (menuState?.nodeId) { duplicateTable(menuState.nodeId); setMenuState(null); } }}
                >
                  <Copy className="w-4 h-4 text-sky-400" />
                  <span>Duplicate Table</span>
                </ContextMenu.Item>
                <ContextMenu.Separator className="h-px bg-indigo-500/10 my-1 mx-1" />
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-zinc-200 rounded-lg cursor-pointer outline-none transition-colors hover:bg-red-500/20 hover:text-red-300 focus:bg-red-500/20 focus:text-red-300 group"
                  onSelect={handleDeleteTable}
                >
                  <Trash2 className="w-4 h-4 text-red-400/80 group-hover:text-red-400" />
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
