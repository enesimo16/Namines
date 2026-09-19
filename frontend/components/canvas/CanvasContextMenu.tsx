'use client';

import { useCallback, useState } from 'react';
import * as ContextMenu from '@radix-ui/react-context-menu';
import { useReactFlow } from '@xyflow/react';
import { Plus, Trash2, Pencil, Table2, Copy, Zap } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useAutomationStore } from '../../store/useAutomationStore';
import { useFlowNodePositionStore } from '../../store/useFlowNodePositionStore';

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
 * Görüntüleme modunda şema işlemleri yerine gösterilen açıklama. Menüyü
 * tamamen kapatmak "sağ tık kırık" izlenimi veriyordu; boş bir menü açmak da
 * aynı şey. Kullanıcı NEDENİNİ menünün içinde görmeli.
 */
const EDIT_MODE_HINT = 'Turn on Edit Mode (pencil icon) to change the schema.';
const editModeHintClass =
  'px-2 py-1.5 text-xs leading-snug text-content-muted max-w-[200px]';

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
  const rulesForTable = useAutomationStore(s => s.rulesForTable);
  const deleteRule = useAutomationStore(s => s.deleteRule);
  const setSelectedRuleId = useAutomationStore(s => s.setSelectedRuleId);
  const clearFlowNodePosition = useFlowNodePositionStore(s => s.clearPosition);
  const [menuState, setMenuState] = useState<ContextMenuState | null>(null);

  // Sağ tıklanan node bir GERÇEK tablo mu yoksa bir Namines Flow node'u mu?
  // Otomasyon node'unun kendi üzerinde "Delete Table"/"Add to Namines Flow"
  // göstermek anlamsız olurdu — o node bir tablo değil.
  const isTableNode = !!menuState?.nodeId && !!schema?.tables.some(t => t.id === menuState.nodeId);
  // Otomasyon node id'leri her zaman `automation-<ruleId>` — önceden bu ikinci
  // durum hiç ele alınmıyordu, yani bir Flow düğümüne sağ tıklayınca menü
  // ne "Canvas" ne de "Table Operations" dalına girip TAMAMEN BOŞ açılıyordu.
  const automationRuleId = menuState?.nodeId?.startsWith('automation-')
    ? menuState.nodeId.slice('automation-'.length)
    : undefined;

  const handleContextMenu = useCallback((e: React.MouseEvent) => {
    // Menü ARTIK Edit Mode'a bağlı DEĞİL. Eskiden görüntüleme modunda hiç
    // açılmıyordu; bu, Namines Flow'un tek giriş kapısını da kapatıyordu —
    // oysa bir flow kurmak şemayı değiştirmek değil. Şema işlemleri (tablo
    // ekle/sil/çoğalt) hâlâ Edit Mode istiyor ve aşağıda öyle işaretleniyor;
    // Flow işlemleri her zaman açık.

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
  }, [screenToFlowPosition]);

  const handleAddTable = () => {
    if (!menuState) return;
    addTable(menuState.flowX ?? 0, menuState.flowY ?? 0);
    setMenuState(null);
  };

  const handleDeleteTable = () => {
    if (!menuState?.nodeId) return;
    // Kural silme önce: tablo silindikten sonra artık hangi tabloya ait
    // olduğunu bilmenin yolu kalmaz. Elle sürüklenmiş Flow konumlarını da
    // aynı anda temizliyoruz (bkz. useFlowNodePositionStore).
    rulesForTable(menuState.nodeId).forEach(rule => clearFlowNodePosition(rule.id));
    deleteRulesForTable(menuState.nodeId);
    deleteTable(menuState.nodeId);
    setMenuState(null);
  };

  const handleEditAutomationRule = () => {
    if (!automationRuleId) return;
    setSelectedRuleId(automationRuleId);
    setMenuState(null);
  };

  const handleDeleteAutomationRule = () => {
    if (!automationRuleId) return;
    deleteRule(automationRuleId);
    clearFlowNodePosition(automationRuleId);
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
    const ruleId = addAutomationRule(menuState.nodeId, 'TableDeleted', 'Toast');
    // Kural oluşturulur oluşturulmaz çekmece açılıyor: aksi hâlde kullanıcı
    // canvas'ta ne yaptığını bilmediği hazır bir kutucukla baş başa kalıyor
    // ve onu düzenleyebileceğini keşfetmesi gerekiyordu.
    setSelectedRuleId(ruleId);
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

      <ContextMenu.Portal>
          <ContextMenu.Content
            className="min-w-[180px] bg-gradient-to-b from-surface-700/95 to-surface-600/95 backdrop-blur-md rounded-[var(--radius-card)] border border-content-primary/12 p-1.5 shadow-[0_8px_30px_color-mix(in srgb, var(--color-scrim) 40%, transparent),0_0_15px_color-mix(in srgb, var(--color-accent-hover) 15%, transparent)] z-[100] animate-in fade-in-0 zoom-in-95 data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=closed]:zoom-out-95"
            onCloseAutoFocus={e => e.preventDefault()}
          >
            {menuState?.type === 'canvas' && (
              <>
                <div className="px-2 py-1.5 text-xs font-semibold text-content-muted uppercase tracking-wider mb-1">Canvas</div>
                {isEditMode ? (
                  <ContextMenu.Item
                    className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-content-primary/12 hover:text-content-primary focus:bg-content-primary/12 focus:text-content-primary"
                    onSelect={handleAddTable}
                  >
                    <Plus className="w-4 h-4 text-accent-text" />
                    <span>Add New Table</span>
                  </ContextMenu.Item>
                ) : (
                  <div className={editModeHintClass}>{EDIT_MODE_HINT}</div>
                )}
              </>
            )}

            {menuState?.type === 'node' && isTableNode && (
              <>
                <div className="flex items-center gap-2 px-2 py-1.5 text-xs font-semibold text-content-muted uppercase tracking-wider mb-1">
                  <Table2 className="w-3.5 h-3.5" />
                  Table Operations
                </div>
                {/* Flow kurmak şemayı DEĞİŞTİRMEZ — bu yüzden Edit Mode
                    gerektirmeyen tek tablo işlemi bu ve en üstte duruyor. */}
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-warning/20 hover:text-warning-text focus:bg-warning/20 focus:text-warning-text"
                  onSelect={handleAddAutomation}
                >
                  <Zap className="w-4 h-4 text-warning-text" />
                  <span>Start a Flow here</span>
                </ContextMenu.Item>

                {isEditMode ? (
                  <>
                    <ContextMenu.Separator className="h-px bg-content-primary/[0.06] my-1 mx-1" />
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
                      className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-danger/20 hover:text-danger-text focus:bg-danger/20 focus:text-danger-text group"
                      onSelect={handleDeleteTable}
                    >
                      <Trash2 className="w-4 h-4 text-danger-text/80 group-hover:text-danger-text" />
                      <span>Delete Table</span>
                    </ContextMenu.Item>
                  </>
                ) : (
                  <div className={editModeHintClass}>{EDIT_MODE_HINT}</div>
                )}
              </>
            )}

            {menuState?.type === 'node' && automationRuleId && (
              <>
                <div className="flex items-center gap-2 px-2 py-1.5 text-xs font-semibold text-content-muted uppercase tracking-wider mb-1">
                  <Zap className="w-3.5 h-3.5" />
                  Namines Flow
                </div>
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-content-primary/12 hover:text-content-primary focus:bg-content-primary/12 focus:text-content-primary"
                  onSelect={handleEditAutomationRule}
                >
                  <Pencil className="w-4 h-4 text-accent-text" />
                  <span>Edit Rule</span>
                </ContextMenu.Item>
                <ContextMenu.Separator className="h-px bg-content-primary/[0.06] my-1 mx-1" />
                <ContextMenu.Item
                  className="flex items-center gap-2 px-2 py-1.5 text-sm font-medium text-content-primary rounded-[var(--radius-control)] cursor-pointer outline-none transition-colors hover:bg-danger/20 hover:text-danger-text focus:bg-danger/20 focus:text-danger-text group"
                  onSelect={handleDeleteAutomationRule}
                >
                  <Trash2 className="w-4 h-4 text-danger-text/80 group-hover:text-danger-text" />
                  <span>Delete Rule</span>
                </ContextMenu.Item>
              </>
            )}
          </ContextMenu.Content>
      </ContextMenu.Portal>
    </ContextMenu.Root>
  );
}
