'use client';

import { useEffect, useState, useCallback } from 'react';
import { useParams } from 'next/navigation';
import {
  ReactFlow,
  Background,
  Controls,
  MiniMap,
  BackgroundVariant,
  useNodesState,
  useEdgesState,
  type Node,
  type Edge,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import TableNode from '../../../components/canvas/nodes/TableNode';
import { schemaToFlow } from '../../../lib/schemaToFlow';
import { API_BASE_URL } from '../../../lib/apiConfig';
import { DatabaseSchema } from '../../../types/schema';
import BadgeSnippet from './BadgeSnippet';

const nodeTypes = { tableNode: TableNode };

interface SharedProject {
  name: string;
  dbType: string;
  schemaJson: string;
  nodePositionsJson: string;
}

export default function SharePage() {
  const params = useParams();
  const token = params?.token as string;

  const [nodes, setNodes, onNodesChange] = useNodesState<Node>([]);
  const [edges, setEdges, onEdgesChange] = useEdgesState<Edge>([]);
  const [projectName, setProjectName] = useState<string>('');
  const [dbType, setDbType] = useState<string>('');
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading');

  useEffect(() => {
    if (!token) return;

    fetch(`${API_BASE_URL}/share/view/${encodeURIComponent(token)}`)
      .then(r => {
        if (!r.ok) throw new Error('not_found');
        return r.json() as Promise<SharedProject>;
      })
      .then(data => {
        setProjectName(data.name);
        setDbType(data.dbType);

        const schema: DatabaseSchema = JSON.parse(data.schemaJson);
        const { nodes: flowNodes, edges: flowEdges } = schemaToFlow(schema);

        // Apply saved node positions if present
        let savedPositions: Record<string, { x: number; y: number }> = {};
        try {
          savedPositions = JSON.parse(data.nodePositionsJson);
        } catch { /* use schemaToFlow positions */ }

        const positionedNodes = flowNodes.map(n => ({
          ...n,
          position: savedPositions[n.id] ?? n.position,
          draggable: false,
          selectable: false,
          connectable: false,
        }));

        setNodes(positionedNodes);
        setEdges(flowEdges.map(e => ({ ...e, deletable: false })));
        setStatus('ready');
      })
      .catch(() => setStatus('error'));
  }, [token, setNodes, setEdges]);

  if (status === 'loading') {
    return (
      <div className="flex items-center justify-center h-screen bg-surface-900 text-content-muted text-sm">
        Loading schema…
      </div>
    );
  }

  if (status === 'error') {
    return (
      <div className="flex flex-col items-center justify-center h-screen bg-surface-900 gap-6 px-4 text-center">
        <div className="w-20 h-20 rounded-3xl bg-surface-800 border border-surface-600 flex items-center justify-center shadow-[0_0_40px_rgba(0,0,0,0.4)]">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.5" className="w-10 h-10 text-surface-400">
            <path strokeLinecap="round" strokeLinejoin="round" d="M13.5 10.5V6.75a4.5 4.5 0 1 1 9 0v3.75M3.75 21.75h10.5a2.25 2.25 0 0 0 2.25-2.25v-6.75a2.25 2.25 0 0 0-2.25-2.25H3.75a2.25 2.25 0 0 0-2.25 2.25v6.75a2.25 2.25 0 0 0 2.25 2.25Z" />
          </svg>
        </div>
        <div>
          <h1 className="text-2xl font-bold text-content-primary mb-2">Link Not Available</h1>
          <p className="text-content-muted text-sm max-w-sm leading-relaxed">
            This share link has been revoked or never existed. Ask the schema owner to generate a new link.
          </p>
        </div>
        <a
          href="/"
          className="px-5 py-2.5 rounded-xl bg-indigo-600 hover:bg-indigo-500 text-white text-sm font-semibold transition-colors"
        >
          Go to Namines
        </a>
        <p className="text-content-muted/50 text-xs font-mono">404 · Share token invalid or expired</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-screen bg-surface-900">
      {/* Header */}
      <header className="flex items-center gap-3 px-5 h-[52px] border-b border-surface-600 shrink-0">
        <span className="text-indigo-400 font-bold text-base tracking-tight">⚡ Namines</span>
        <span className="text-surface-500">|</span>
        <span className="text-content-primary font-medium text-sm truncate">{projectName}</span>
        {dbType && (
          <span className="ml-1 px-2 py-0.5 rounded-md bg-surface-700 text-content-muted text-xs font-mono">
            {dbType}
          </span>
        )}
        <span className="ml-auto px-3 py-1 rounded-lg bg-surface-700 text-content-muted text-xs border border-surface-500">
          Read-only view
        </span>
        <BadgeSnippet token={token} projectName={projectName} />
      </header>

      {/* Canvas */}
      <div className="flex-1">
        <ReactFlow
          nodes={nodes}
          edges={edges}
          onNodesChange={onNodesChange}
          onEdgesChange={onEdgesChange}
          nodeTypes={nodeTypes}
          nodesDraggable={false}
          nodesConnectable={false}
          elementsSelectable={false}
          deleteKeyCode={null}
          fitView
          fitViewOptions={{ padding: 0.15 }}
          proOptions={{ hideAttribution: false }}
        >
          <Background variant={BackgroundVariant.Dots} gap={24} size={1} color="#2a3750" />
          <Controls showInteractive={false} />
          <MiniMap
            nodeColor="#2a3750"
            maskColor="rgba(9,17,31,0.7)"
            style={{ background: '#0d182a', border: '1px solid #2a3750' }}
          />
        </ReactFlow>
      </div>
    </div>
  );
}
