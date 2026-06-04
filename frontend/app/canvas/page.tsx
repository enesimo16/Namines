'use client';

import { useEffect, useMemo, useState, useRef } from 'react';
import { useRouter } from 'next/navigation';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  Panel,
  ReactFlowProvider,
  BackgroundVariant
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
import { Loader2 } from 'lucide-react';

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

  const { schema, nodes, edges, onNodesChange, onEdgesChange, setIsGenerating, isEditMode } = useSchemaStore();
  const { score, issues, assessment, isAnalyzing, isPanelOpen, setIsPanelOpen } = useDbaStore();

  const { projects, activeProjectId } = useProjectHistoryStore();
  const { isDiffMode, compareBranchName } = useBranchStore();
  const { isOffline, setIsOffline } = useMultiplayerStore();

  const showToast = useToastStore(state => state.showToast);

  const activeProject = projects.find(p => p.id === activeProjectId);
  const branches = activeProject?.branches || [];
  const currentBranchName = activeProject?.currentBranch || 'main';



  useAIDba();
  useProjectAutoSave();

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
    <div className="w-full bg-zinc-950 overflow-hidden relative font-sans" style={{ height: 'calc(100vh - 56px)' }}>

      {/* Connection Lost Overlay for Read-Only Mode */}
      {isOffline && (
        <div className="absolute inset-0 z-[8000] bg-black/40 backdrop-blur-[2px] flex flex-col items-center justify-center pointer-events-auto">
          <div className="bg-[#09111F]/90 border border-amber-500/30 px-6 py-5 rounded-2xl shadow-[0_10px_40px_rgba(0,0,0,0.8)] flex flex-col items-center text-center gap-3 animate-in zoom-in-95 duration-200">
            <span className="bg-amber-500/10 text-amber-300 border border-amber-500/20 px-3 py-1 rounded-full text-[10px] font-black uppercase tracking-widest animate-pulse">
              ⚠️ Connection Lost
            </span>
            <div className="flex flex-col gap-1">
              <h4 className="text-zinc-200 font-extrabold text-sm">Read-Only Mode Active</h4>
              <p className="text-zinc-400 text-[11px] leading-relaxed max-w-[280px]">
                Collaborative session is disconnected. You cannot modify the schema until the connection is restored.
              </p>
            </div>
            <button
              onClick={() => setIsOffline(false)}
              className="mt-2 px-4 py-2 bg-zinc-800 hover:bg-zinc-700 text-zinc-200 text-xs font-bold rounded-xl border border-zinc-700 transition-all cursor-pointer shadow-sm hover:shadow-md"
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

      <ReactFlowProvider>
        <CanvasContextMenu>
          <ReactFlow
            nodes={processedNodes}
            edges={edges}
            onNodesChange={onNodesChange}
            onEdgesChange={onEdgesChange}
            nodeTypes={nodeTypes}
            edgeTypes={edgeTypes}
            fitView
            colorMode="dark"
            nodesDraggable={!isDiffMode && !isOffline}
            nodesConnectable={!isEditMode && !isDiffMode && !isOffline}
            proOptions={{ hideAttribution: true }}
          >
            <Background
              color={isEditMode ? '#4338ca' : '#3f3f46'}
              variant={BackgroundVariant.Dots}
              gap={20}
              size={1}
            />
            
            <MiniMap
              nodeColor={isEditMode ? '#6366f1' : '#3f3f46'}
              maskColor="rgba(0, 0, 0, 0.7)"
              className="bg-[#0F172A] border border-indigo-500/20 rounded-xl overflow-hidden shadow-lg"
            />

            {/* Static Schema Info Panel (DbContext yazan yer sabit ve büyük halinde) */}
            <Panel position="top-left" className="bg-[#0F172A]/85 backdrop-blur-md border border-indigo-500/20 p-4 rounded-2xl shadow-[0_0_20px_rgba(59,130,246,0.15)] mt-4 ml-4 w-64 select-none pointer-events-auto">
              <h2 className="text-xl font-bold bg-gradient-to-r from-zinc-100 to-indigo-200 bg-clip-text text-transparent mb-1 truncate" title={schema.name}>{schema.name || 'Untitled Schema'}</h2>
              <div className="text-xs text-indigo-300/80 flex flex-col gap-1 font-medium">
                <div className="flex gap-4">
                  <span>{schema.tables.length} Tables</span>
                  <span>{schema.relations.length} Relations</span>
                </div>
              </div>
              <BranchControlPanel />
            </Panel>
          </ReactFlow>
        </CanvasContextMenu>

        {/* Draggable Panels */}
        <ToolbarPanel />
        <RegionalPromptPanel />
        <CanvasExportToolbar />
        <ConflictResolverModal />
      </ReactFlowProvider>


      <TableEditorDrawer />
      <SqlExplorerPanel />
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
    <div className="fixed inset-0 z-[9999] bg-zinc-950 flex flex-col items-center justify-center font-sans">
      {/* Background glow effects */}
      <div className="absolute top-1/4 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[500px] h-[500px] bg-indigo-500/10 rounded-full blur-[120px] pointer-events-none" />
      <div className="absolute bottom-1/4 left-1/3 w-[300px] h-[300px] bg-cyan-500/5 rounded-full blur-[100px] pointer-events-none" />

      <div className="relative bg-[#09111F]/80 border border-indigo-500/20 p-8 rounded-3xl shadow-[0_20px_50px_rgba(0,0,0,0.8)] backdrop-blur-xl flex flex-col items-center text-center max-w-md w-full mx-4 gap-6 animate-in zoom-in-95 duration-300">
        
        {/* Pulse Logo / Circle */}
        <div className="relative flex items-center justify-center w-16 h-16 rounded-2xl bg-indigo-500/10 border border-indigo-500/30 animate-pulse">
          <Loader2 className="w-8 h-8 text-indigo-400 animate-spin" />
        </div>

        <div className="flex flex-col gap-2">
          <h3 className="text-lg font-black text-white bg-gradient-to-r from-zinc-100 to-indigo-200 bg-clip-text text-transparent">
            {status === 'connecting' ? 'Connecting to Room' : 'Room is Empty'}
          </h3>
          <p className="text-indigo-300/80 font-mono text-[11px] bg-indigo-500/5 border border-indigo-500/10 px-2.5 py-1 rounded-lg">
            Room ID: {roomId}
          </p>
          <p className="text-zinc-400 text-xs leading-relaxed max-w-sm mt-1">
            {status === 'connecting' 
              ? 'Establishing real-time connection and retrieving the shared schema from active peers...'
              : 'We connected to the room, but there are no active peers online to share the schema.'}
          </p>
        </div>

        {status === 'connecting' ? (
          <button
            onClick={onCancel}
            className="w-full py-2.5 bg-zinc-900 hover:bg-zinc-800 text-zinc-300 hover:text-white font-bold text-xs rounded-xl border border-zinc-800 hover:border-zinc-700 transition-all cursor-pointer"
          >
            Cancel and Return
          </button>
        ) : (
          <div className="flex flex-col gap-2.5 w-full">
            <button
              onClick={handleStartBlank}
              className="w-full py-2.5 bg-gradient-to-r from-indigo-600 to-violet-600 hover:from-indigo-500 hover:to-violet-500 text-white font-extrabold text-xs rounded-xl shadow-lg hover:shadow-indigo-500/20 transition-all cursor-pointer border border-indigo-400/20"
            >
              Start Clean Schema (Host Room)
            </button>
            <button
              onClick={onCancel}
              className="w-full py-2.5 bg-zinc-900 hover:bg-zinc-800 text-zinc-400 hover:text-zinc-200 font-bold text-xs rounded-xl border border-zinc-800 hover:border-zinc-700 transition-all cursor-pointer"
            >
              Return to Main Menu
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
