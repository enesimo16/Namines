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
  const { schema, nodes, edges, onNodesChange, onEdgesChange, setIsGenerating, isEditMode } = useSchemaStore();
  const { score, issues, assessment, isAnalyzing, isPanelOpen, setIsPanelOpen } = useDbaStore();

  const { projects, activeProjectId } = useProjectHistoryStore();
  const { isDiffMode, compareBranchName } = useBranchStore();

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
    if (!schema) router.push('/');
    setIsGenerating(false);
  }, [schema, router, setIsGenerating]);

  // Unified Edit Mode Toast Alert
  useEffect(() => {
    if (isEditMode) {
      showToast('Manual Editing Mode Active (Right-click: Actions)', 'info');
    }
  }, [isEditMode, showToast]);

  if (!schema) return null;

  return (
    <div className="w-full bg-zinc-950 overflow-hidden relative font-sans" style={{ height: 'calc(100vh - 56px)' }}>

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
            nodesDraggable={!isDiffMode}
            nodesConnectable={!isEditMode && !isDiffMode}
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
                <div className="flex items-center gap-1.5 mt-1 text-[10px] bg-indigo-500/10 px-2 py-0.5 rounded border border-indigo-500/20 text-indigo-300 w-fit">
                  <span className="h-1.5 w-1.5 rounded-full bg-indigo-400 animate-pulse"></span>
                  Active Branch: <strong className="font-semibold text-indigo-100">{currentBranchName}</strong>
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
