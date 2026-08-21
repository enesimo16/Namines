'use client';

import { useCallback, useEffect, useMemo, useState, useRef } from 'react';
import { useRouter } from 'next/navigation';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  Panel,
  ReactFlowProvider,
  BackgroundVariant,
  useReactFlow,
  type Connection
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';


import { useSchemaStore } from '../../store/useSchemaStore';
import { useAIDba } from '../../hooks/useAIDba';
import { useDbaStore } from '../../store/useDbaStore';
import { useProjectAutoSave } from '../../hooks/useProjectAutoSave';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useBranchStore } from '../../store/useBranchStore';
import { calculateSchemaDiff } from '../../utils/schemaDiff';
import { useToastStore } from '../../store/useToastStore';
import { useMultiplayerStore } from '../../store/useMultiplayerStore';
import { useMultiplayer } from '../../hooks/useMultiplayer';
import {
  Activity, AlertTriangle, Eye, FileImage, LayoutTemplate, Loader2,
  Pencil, Plus, Redo2, Search, Sparkles, Undo2, Upload,
} from 'lucide-react';

import CommandPalette, { PaletteAction } from '../../components/canvas/CommandPalette';
import SchemaTemplateGallery from '../../components/canvas/SchemaTemplateGallery';
import CanvasSearch from '../../components/canvas/CanvasSearch';
import KeyboardShortcutsModal from '../../components/canvas/KeyboardShortcutsModal';
import TableNode from '../../components/canvas/nodes/TableNode';
import RelationEdge from '../../components/canvas/edges/RelationEdge';
import RegionalPromptPanel from '../../components/canvas/panels/RegionalPromptPanel';
import ToolbarPanel from '../../components/canvas/panels/ToolbarPanel';
import CanvasExportToolbar from '../../components/canvas/panels/CanvasExportToolbar';
import CanvasContextMenu from '../../components/canvas/CanvasContextMenu';
import TableEditorDrawer from '../../components/canvas/TableEditorDrawer';
import SqlExplorerPanel from '../../components/canvas/panels/SqlExplorerPanel';
import BranchControlPanel from '../../components/canvas/panels/BranchControlPanel';
import ConflictResolverModal from '../../components/canvas/panels/ConflictResolverModal';
import DbaIssuePanel from '../../components/canvas/DbaIssuePanel';
import AIGatewayModal from '../../components/canvas/panels/AIGatewayModal';
import SchemaTextualSummary from '../../components/canvas/SchemaTextualSummary';
import MultiplayerCursors from '../../components/canvas/MultiplayerCursors';
import EmptyCanvasState from '../../components/canvas/EmptyCanvasState';
import TourOverlay from '../../components/tour/TourOverlay';

export default function CanvasPage() {
  const router = useRouter();

  // Keep real-time multiplayer connection active globally
  useMultiplayer();

  // Get roomId from URL dynamically
  const [urlRoomId, setUrlRoomId] = useState<string | null>(null);
  useEffect(() => {
    if (typeof window === 'undefined') return;

    const checkRoomId = () => {
      const params = new URLSearchParams(window.location.search);
      const rId = params.get('roomId');
      if (rId !== urlRoomId) {
        setUrlRoomId(rId);
      }
    };

    checkRoomId();
    const interval = setInterval(checkRoomId, 500);
    return () => clearInterval(interval);
  }, [urlRoomId]);

  const { schema, nodes, edges, onNodesChange, onEdgesChange, setIsGenerating, isEditMode, toggleEditMode, addTable, connectColumns, deleteTable, deleteRelation, undo, redo, canUndo, canRedo } = useSchemaStore();
  const { score, issues, assessment, isAnalyzing, isPanelOpen, setIsPanelOpen } = useDbaStore();

  const { projects, activeProjectId } = useProjectHistoryStore();
  const { isDiffMode, compareBranchName } = useBranchStore();
  const { isOffline, setIsOffline } = useMultiplayerStore();

  const showToast = useToastStore(state => state.showToast);

  const activeProject = projects.find(p => p.id === activeProjectId);
  const branches = activeProject?.branches || [];
  const currentBranchName = activeProject?.currentBranch || 'main';



  const [isPaletteOpen, setIsPaletteOpen] = useState(false);
  const [isTemplateGalleryOpen, setIsTemplateGalleryOpen] = useState(false);
  const [isSearchOpen, setIsSearchOpen] = useState(false);
  const [isShortcutsOpen, setIsShortcutsOpen] = useState(false);

  // ⌘K / Ctrl+K — toggle command palette
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
        e.preventDefault();
        setIsPaletteOpen(v => !v);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  // Ctrl+F — canvas search
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key === 'f') {
        e.preventDefault();
        setIsSearchOpen(v => !v);
      }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  // Ctrl+Z / Ctrl+Shift+Z — undo / redo
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if (!(e.metaKey || e.ctrlKey)) return;
      if (e.key === 'z' && !e.shiftKey) { e.preventDefault(); undo(); }
      if ((e.key === 'z' && e.shiftKey) || e.key === 'y') { e.preventDefault(); redo(); }
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, [undo, redo]);

  // ? — keyboard shortcuts modal
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement).tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || (e.target as HTMLElement).isContentEditable) return;
      if (e.key === '?') setIsShortcutsOpen(v => !v);
    };
    window.addEventListener('keydown', handler);
    return () => window.removeEventListener('keydown', handler);
  }, []);

  // Custom event from EmptyCanvasState "Browse Templates" button
  useEffect(() => {
    const handler = () => setIsTemplateGalleryOpen(true);
    window.addEventListener('namines:open-template-gallery', handler);
    return () => window.removeEventListener('namines:open-template-gallery', handler);
  }, []);

  const paletteActions = useMemo<PaletteAction[]>(() => [
    {
      id: 'undo',
      label: 'Undo',
      description: 'Undo the last change (Ctrl+Z)',
      icon: <Undo2 className="w-4 h-4" />,
      keywords: ['undo', 'geri al', 'ctrl z'],
      onSelect: undo,
    },
    {
      id: 'redo',
      label: 'Redo',
      description: 'Redo the undone change (Ctrl+Shift+Z)',
      icon: <Redo2 className="w-4 h-4" />,
      keywords: ['redo', 'ileri al', 'ctrl y'],
      onSelect: redo,
    },
    {
      id: 'search',
      label: 'Search Tables & Columns',
      description: 'Find a table or column on the canvas (Ctrl+F)',
      icon: <Search className="w-4 h-4" />,
      keywords: ['search', 'find', 'ara', 'bul'],
      onSelect: () => setIsSearchOpen(true),
    },
    {
      id: 'new-table',
      label: 'Add New Table',
      description: 'Insert an empty table onto the canvas',
      icon: <Plus className="w-4 h-4" />,
      keywords: ['create', 'table', 'add'],
      onSelect: () => addTable(400, 200),
    },
    {
      id: 'browse-templates',
      label: 'Browse Schema Templates',
      description: 'Load a pre-built schema for e-commerce, CMS, SaaS, and more',
      icon: <LayoutTemplate className="w-4 h-4" />,
      keywords: ['template', 'gallery', 'preset'],
      onSelect: () => setIsTemplateGalleryOpen(true),
    },
    {
      id: 'generate-ai',
      label: 'Generate with AI',
      description: 'Describe your database in plain language',
      icon: <Sparkles className="w-4 h-4" />,
      keywords: ['ai', 'generate', 'prompt'],
      onSelect: () => window.dispatchEvent(new CustomEvent('namines:open-regional-prompt')),
    },
    {
      id: 'export-prisma',
      label: 'Export Prisma Schema',
      description: 'Download as .prisma file for Prisma ORM',
      icon: <FileImage className="w-4 h-4" />,
      keywords: ['prisma', 'export', 'orm'],
      onSelect: () => window.dispatchEvent(new CustomEvent('namines:export-prisma')),
    },
    {
      id: 'import-sql',
      label: 'Import SQL DDL (.sql)',
      description: 'Upload a .sql file and parse tables onto the canvas',
      icon: <Upload className="w-4 h-4" />,
      keywords: ['import', 'sql', 'ddl', 'upload', 'file'],
      onSelect: () => window.dispatchEvent(new CustomEvent('namines:import-sql')),
    },
    {
      id: 'import-image',
      label: 'Import from Image',
      description: 'Extract schema from a whiteboard or diagram photo',
      icon: <FileImage className="w-4 h-4" />,
      keywords: ['import', 'image', 'vision', 'photo'],
      onSelect: () => window.dispatchEvent(new CustomEvent('namines:open-vision-modal')),
    },
    {
      id: 'toggle-edit',
      label: isEditMode ? 'Switch to View Mode' : 'Switch to Edit Mode',
      description: 'Toggle between view and manual editing',
      icon: isEditMode ? <Eye className="w-4 h-4" /> : <Pencil className="w-4 h-4" />,
      keywords: ['edit', 'view', 'mode'],
      onSelect: toggleEditMode,
    },
    {
      id: 'dba-panel',
      label: isPanelOpen ? 'Close DBA Panel' : 'Open DBA Analysis',
      description: 'Show or hide schema health and linter suggestions',
      icon: <Activity className="w-4 h-4" />,
      keywords: ['dba', 'lint', 'health', 'issues'],
      onSelect: () => setIsPanelOpen(!isPanelOpen),
    },
  ], [addTable, isEditMode, toggleEditMode, isPanelOpen, setIsPanelOpen, undo, redo]);

  useAIDba();
  useProjectAutoSave();

  // Kullanıcı iki kolon handle'ını birleştirince FK ilişkisi kur.
  // Bu handler bağlanmazsa React Flow bağlantı çizgisini gösterir ama bırakıldığında
  // hiçbir şey olmaz — kullanıcı canvas üzerinden hiç ilişki kuramaz.
  const handleConnect = useCallback((connection: Connection) => {
    const result = connectColumns(connection);
    showToast(result.reason, result.ok ? 'success' : 'error');
  }, [connectColumns, showToast]);

  // React Flow'un Backspace ile silme davranışı yalnızca `nodes` dizisinden çıkarır;
  // `schema.tables` dokunulmadan kalır. Sonuç: tablo görünmez olur ama şemada durur ve
  // ilk loadFromSchema/applyRevision çağrısında geri gelir. Silmeyi şemaya da uygula.
  const handleNodesDelete = useCallback((deleted: { id: string }[]) => {
    deleted.forEach(node => deleteTable(node.id));
  }, [deleteTable]);

  // Aynı sorun edge'ler için: edge silmek ilişkiyi şemadan düşürmeli.
  const handleEdgesDelete = useCallback((deleted: { id: string }[]) => {
    deleted.forEach(edge => deleteRelation(edge.id));
  }, [deleteRelation]);

  // Proje değişince eski projenin DBA sonuçları (skor/issue) kalmasın — sıfırla.
  useEffect(() => {
    useDbaStore.getState().setDbaResults({ issues: [], score: 100, assessment: 'Pending schema health check...' });
  }, [activeProjectId]);

  const nodeTypes = useMemo(() => ({ tableNode: TableNode }), []);
  const edgeTypes = useMemo(() => ({ relationEdge: RelationEdge }), []);

  // Compute final nodes list with diff states
  const processedNodes = useMemo(() => {
    if (!schema) return [];
    if (!isDiffMode || !compareBranchName) {
      return nodes;
    }

    const compareBranch = branches.find(b => b.name === compareBranchName);
    if (!compareBranch) return nodes;

    const diffResult = calculateSchemaDiff(schema, compareBranch.schema);

    // Map existing nodes
    const updatedNodes = nodes.map(node => {
      if (node.type === 'tableNode') {
        const tableDiff = diffResult.tables[node.id];
        return {
          ...node,
          data: {
            ...node.data,
            diff: tableDiff || { status: 'unchanged', columns: {} }
          }
        };
      }
      return node;
    });

    // Inject virtual deleted nodes
    const deletedNodes: any[] = [];
    Object.entries(diffResult.tables).forEach(([tableId, tableDiff]) => {
      if (tableDiff.status === 'deleted') {
        const deletedTable = compareBranch.schema.tables.find(t => t.id === tableId);
        if (deletedTable) {
          const position = compareBranch.nodePositions[tableId] || { x: 100, y: 100 };
          deletedNodes.push({
            id: tableId,
            type: 'tableNode',
            position,
            draggable: false,
            selectable: false,
            data: {
              table: deletedTable,
              diff: tableDiff
            }
          });
        }
      }
    });

    return [...updatedNodes, ...deletedNodes];
  }, [schema, nodes, isDiffMode, compareBranchName, branches]);

  useEffect(() => {
    if (!schema && typeof window !== 'undefined') {
      const params = new URLSearchParams(window.location.search);
      if (!params.get('roomId')) {
        router.push('/');
      }
    }
    setIsGenerating(false);
  }, [schema, router, setIsGenerating]);

  // Unified Edit Mode Toast Alert
  useEffect(() => {
    if (isEditMode) {
      showToast('Manual Editing Mode Active (Right-click: Actions)', 'info');
    }
  }, [isEditMode, showToast]);

  if (!schema) {
    if (urlRoomId) {
      return <MultiplayerLoadingScreen roomId={urlRoomId} onCancel={() => router.push('/')} />;
    }
    return null;
  }

  return (
    // Header yüksekliği 52px (globals.css). Burada 56px çıkarılıyordu; 4px'lik fark
    // canvas'ı kısa bırakıyordu.
    <div className="w-full bg-surface-900 overflow-hidden relative" style={{ height: 'calc(100vh - 52px)' }}>
      <SchemaTextualSummary />

      {/* Connection Lost Overlay for Read-Only Mode */}
      {isOffline && (
        <div className="absolute inset-0 z-[8000] bg-black/40 backdrop-blur-[2px] flex flex-col items-center justify-center pointer-events-auto">
          <div className="bg-surface-800 border border-danger/25 px-6 py-5 rounded-2xl flex flex-col items-center text-center gap-3">
            <span className="flex items-center gap-1.5 bg-danger-subtle text-danger-text border border-danger/20 px-3 py-1 rounded-full text-[10px] font-bold uppercase tracking-widest">
              <AlertTriangle className="w-3 h-3" />
              Connection Lost
            </span>
            <div className="flex flex-col gap-1">
              <h4 className="text-content-primary font-bold text-sm">Read-Only Mode Active</h4>
              <p className="text-content-muted text-[11px] leading-relaxed max-w-[280px]">
                Collaborative session is disconnected. You cannot modify the schema until the connection is restored.
              </p>
            </div>
            <button
              onClick={() => setIsOffline(false)}
              className="mt-2 px-4 py-2 bg-surface-700 hover:bg-surface-600 text-content-secondary text-xs font-semibold rounded-xl transition-all cursor-pointer"
            >
              Work Offline (Local Mode)
            </button>
          </div>
        </div>
      )}

      {/* Right Drawer showing Linter Issues & Optimization Suggestions */}
      <DbaIssuePanel
        isOpen={isPanelOpen}
        onClose={() => setIsPanelOpen(false)}
        issues={issues}
        score={score}
        assessment={assessment}
      />

      <EmptyCanvasState />
      <TourOverlay />

      <ReactFlowProvider>
        <CanvasContextMenu>
          <ReactFlow
            id="react-flow-canvas"
            nodes={processedNodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            onConnect={handleConnect}
            onNodesDelete={handleNodesDelete}
            onEdgesDelete={handleEdgesDelete}
            // Diff görünümü salt-okunur (sanal "silinmiş tablo" node'ları gerçek şemadan
            // silinememeli) ve çevrimdışıyken değişiklik yayınlanamaz → silme tuşunu kapat.
            deleteKeyCode={isDiffMode || isOffline ? null : ['Backspace', 'Delete']}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            fitView
            colorMode="dark"
            nodesDraggable={!isDiffMode && !isOffline}
            // Diff görünümü salt-okunur ve çevrimdışıyken değişiklik yayınlanamaz.
            // (Düzenleme modu bağlantı kurmayı ENGELLEMEZ — tam tersi beklenir.)
            nodesConnectable={!isDiffMode && !isOffline}
            proOptions={{ hideAttribution: true }}
          >
            <Background
              color={isEditMode ? '#4c5c82' : '#1e2430'}
              variant={BackgroundVariant.Dots}
              gap={20}
              size={1}
            />
            
            <MiniMap
              nodeColor={(node) => {
                const tableColor = (node.data as any)?.table?.color;
                if (tableColor) return tableColor;
                return isEditMode ? '#4c5c82' : '#1e2430';
              }}
              maskColor="rgba(0, 0, 0, 0.7)"
              className="bg-surface-700 border border-content-primary/12 rounded-2xl overflow-hidden shadow-lg"
            />

            {/* Odadaki diğer kullanıcıların imleçleri (ReactFlow içinde: viewport'a erişir) */}
            <MultiplayerCursors />

            {/* Static Schema Info Panel (DbContext yazan yer sabit ve büyük halinde) */}
            <Panel id="schema-info-panel" position="top-left" className="bg-surface-700/85 backdrop-blur-md border border-content-primary/12 p-4 rounded-2xl mt-4 ml-4 w-64 select-none pointer-events-auto">
              <h2 className="text-xl font-bold bg-gradient-to-r from-zinc-100 to-content-primary bg-clip-text text-transparent mb-1 truncate" title={schema.name}>{schema.name || 'Untitled Schema'}</h2>
              <div className="text-xs text-content-secondary/80 flex flex-col gap-1 font-medium">
                <div className="flex gap-4">
                  <span>{schema.tables.length} Tables</span>
                  <span>{schema.relations.length} Relations</span>
                </div>
              </div>
              <BranchControlPanel />
              <TableZoomList tables={schema.tables} />
            </Panel>
          </ReactFlow>
        </CanvasContextMenu>

        {/* Draggable Panels */}
        <ToolbarPanel />
        <RegionalPromptPanel />
        <CanvasExportToolbar />
        <ConflictResolverModal />

        {/* Canvas arama (ReactFlowProvider içinde olmalı — fitBounds kullanır) */}
        <CanvasSearch
          isOpen={isSearchOpen}
          onClose={() => setIsSearchOpen(false)}
        />
      </ReactFlowProvider>


      <TableEditorDrawer />
      <SqlExplorerPanel />
      <AIGatewayModal />

      <CommandPalette
        isOpen={isPaletteOpen}
        onClose={() => setIsPaletteOpen(false)}
        actions={paletteActions}
      />
      <SchemaTemplateGallery
        isOpen={isTemplateGalleryOpen}
        onClose={() => setIsTemplateGalleryOpen(false)}
      />
      <KeyboardShortcutsModal
        isOpen={isShortcutsOpen}
        onClose={() => setIsShortcutsOpen(false)}
      />
    </div>
  );
}

function TableZoomList({ tables }: { tables: { id: string; name: string; color?: string }[] }) {
  const { fitBounds, getNode } = useReactFlow();
  const [open, setOpen] = useState(false);

  if (tables.length === 0) return null;

  const zoomTo = (tableId: string) => {
    const node = getNode(tableId);
    if (!node) return;
    const { x, y } = node.position;
    const w = (node.measured?.width ?? node.width ?? 320) as number;
    const h = (node.measured?.height ?? node.height ?? 200) as number;
    fitBounds({ x, y, width: w, height: h }, { padding: 0.3, duration: 400 });
  };

  return (
    <div className="mt-3 border-t border-content-primary/[0.06] pt-3">
      <button
        onClick={() => setOpen(v => !v)}
        className="flex items-center justify-between w-full text-xs font-semibold text-content-secondary/70 hover:text-content-primary transition-colors cursor-pointer"
      >
        <span>Tables</span>
        <span className="text-[10px] opacity-60">{open ? '▲' : '▼'}</span>
      </button>
      {open && (
        <div className="mt-2 flex flex-col gap-0.5 max-h-40 overflow-y-auto pr-1">
          {tables.map(t => (
            <button
              key={t.id}
              onClick={() => zoomTo(t.id)}
              className="flex items-center gap-2 px-2 py-1 rounded-lg text-xs text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06] transition-colors text-left cursor-pointer w-full truncate"
            >
              {t.color && <span className="w-2 h-2 rounded-full shrink-0" style={{ backgroundColor: t.color }} />}
              <span className="truncate">{t.name}</span>
            </button>
          ))}
        </div>
      )}
    </div>
  );
}

function MultiplayerLoadingScreen({ roomId, onCancel }: { roomId: string; onCancel: () => void }) {
  const { loadFromSchema } = useSchemaStore();
  const [status, setStatus] = useState<'connecting' | 'timeout'>('connecting');

  useEffect(() => {
    const timer = setTimeout(() => {
      setStatus('timeout');
    }, 6000); // 6 seconds wait time for peer discovery

    return () => clearTimeout(timer);
  }, []);

  const handleStartBlank = () => {
    const emptySchema = {
      schemaId: typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : Math.random().toString(36).substring(2, 11),
      name: 'Shared Room Project',
      tables: [],
      relations: []
    };
    loadFromSchema(emptySchema);
  };

  return (
    <div className="fixed inset-0 z-[9999] bg-surface-900 flex flex-col items-center justify-center font-sans">
      <div className="relative bg-surface-800 border border-content-primary/10 p-6 rounded-2xl shadow-[0_20px_60px_rgba(0,0,0,0.6)] flex flex-col items-center text-center max-w-sm w-full mx-4 gap-5">

        <div className="flex items-center justify-center w-12 h-12 rounded-xl bg-surface-600 border border-content-primary/10">
          <Loader2 className="w-5 h-5 text-accent-text animate-spin" />
        </div>

        <div className="flex flex-col gap-1.5">
          <h3 className="text-sm font-bold text-content-primary">
            {status === 'connecting' ? 'Connecting to Room' : 'Room is Empty'}
          </h3>
          <p className="text-content-muted font-mono text-[11px] bg-surface-700 border border-content-primary/8 px-2.5 py-1 rounded-lg">
            Room ID: {roomId}
          </p>
          <p className="text-content-muted text-xs leading-relaxed max-w-sm mt-1">
            {status === 'connecting'
              ? 'Establishing real-time connection and retrieving the shared schema from active peers...'
              : 'We connected to the room, but there are no active peers online to share the schema.'}
          </p>
        </div>

        {status === 'connecting' ? (
          <button
            onClick={onCancel}
            className="w-full py-2.5 bg-surface-700 hover:bg-surface-600 text-content-muted hover:text-content-primary font-semibold text-xs rounded-lg border border-content-primary/10 transition-all cursor-pointer"
          >
            Cancel and Return
          </button>
        ) : (
          <div className="flex flex-col gap-2 w-full">
            <button
              onClick={handleStartBlank}
              className="w-full py-2.5 bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold text-xs rounded-lg transition-all cursor-pointer"
            >
              Start Clean Schema (Host Room)
            </button>
            <button
              onClick={onCancel}
              className="w-full py-2.5 bg-surface-700 hover:bg-surface-600 text-content-muted hover:text-content-secondary font-semibold text-xs rounded-lg border border-content-primary/10 transition-all cursor-pointer"
            >
              Return to Main Menu
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
