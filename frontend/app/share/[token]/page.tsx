'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  ReactFlow,
  ReactFlowProvider,
  useNodesInitialized,
  useReactFlow,
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
// Bu sayfada `token` zaten paylaşım anahtarının adı; tasarım token okuyucusu
// takma adla alınıyor ki route parametresini gölgelemesin.
import { token as designToken } from '../../../lib/designTokens';

const nodeTypes = { tableNode: TableNode };

/**
 * Görünümü, düğümler ÖLÇÜLDÜKTEN sonra şemaya oturtur.
 *
 * `fitView` bayrağı tek başına yalnızca ilk render'da ve boyutlar biliniyorsa
 * çalışıyor; 25+ tabloluk paylaşılan bir şemada ölçüm yetişmiyor ve ziyaretçi
 * bomboş bir tuval görüyordu. `minZoom` de okunaksızlığa kadar uzaklaşmayı
 * engelliyor — büyük şemada kaydırmak, hiçbir şey okuyamamaktan iyi.
 */
function FitOnLoad({ dependency }: { dependency: number }) {
  const initialised = useNodesInitialized();
  const { fitView } = useReactFlow();

  useEffect(() => {
    if (initialised) void fitView({ padding: 0.15, minZoom: 0.4 });
  }, [initialised, dependency, fitView]);

  return null;
}

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
  // Sayfanın bir SONUÇ sayfası olması için gereken şey: ziyaretçinin baktığı
  // şeyin ne olduğunu tek bakışta görmesi. Salt okunur bir tuvalde tabloları
  // saymak zorunda kalmak, paylaşan kişinin göstermek istediğini gizliyordu.
  const [stats, setStats] = useState<{ tables: number; relations: number; columns: number } | null>(null);

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
        setStats({
          tables: schema.tables?.length ?? 0,
          relations: schema.relations?.length ?? 0,
          columns: (schema.tables ?? []).reduce((sum, t) => sum + (t.columns?.length ?? 0), 0),
        });
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
      <div className="flex items-center justify-center h-dvh bg-surface-900 text-content-muted text-sm">
        Loading schema…
      </div>
    );
  }

  if (status === 'error') {
    return (
      <div className="flex flex-col items-center justify-center h-dvh bg-surface-900 gap-6 px-4 text-center">
        <div className="w-20 h-20 rounded-3xl bg-surface-800 border border-surface-600 flex items-center justify-center shadow-[0_0_40px_color-mix(in srgb, var(--color-scrim) 40%, transparent)]">
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
        <Link
          href="/"
          className="px-5 py-2.5 rounded-xl bg-accent hover:bg-accent-hover text-content-primary text-sm font-semibold transition-colors"
        >
          Go to Namines
        </Link>
        <p className="text-content-muted/50 text-xs font-mono">404 · Share token invalid or expired</p>
      </div>
    );
  }

  return (
    <div className="flex flex-col h-dvh bg-surface-900">
      {/* Header */}
      {/* Başlık MOBİLDE SARIYOR. Yedi öğe (marka, proje adı, motor, istatistik,
          rozet, "salt okunur", iki eylem) 375px'lik tek bir 52px'lik satıra
          sığmıyordu; sabit yükseklikte hepsi birbirinin üstüne biniyordu. */}
      <header className="flex flex-wrap items-center gap-x-3 gap-y-2 px-4 sm:px-5 py-2.5 sm:h-[52px] sm:flex-nowrap sm:py-0 border-b border-surface-600 shrink-0">
        {/* Marka artık BAĞLANTI. Paylaşılan bir şemaya gelen ziyaretçi, ürünün
            adını görüyor ama gidecek bir yeri yoktu — viral döngünün son adımı
            (gelen kişinin ürünü denemesi) tıklanamayan bir metinde kırılıyordu. */}
        <Link href="/" className="text-accent-text font-bold text-base tracking-tight hover:text-content-primary transition-colors shrink-0">
          Namines
        </Link>
        <span className="hidden sm:inline text-surface-500">|</span>
        <span className="text-content-primary font-medium text-sm truncate">{projectName}</span>
        {dbType && (
          <span className="ml-1 px-2 py-0.5 rounded-md bg-surface-700 text-content-muted text-xs font-mono shrink-0">
            {dbType}
          </span>
        )}
        {stats && (
          <span className="hidden md:inline text-content-muted text-xs shrink-0">
            {stats.tables} tables · {stats.columns} columns · {stats.relations} relations
          </span>
        )}
        {/* DBA rozeti sayfanın kendisinde de gösteriliyor: paylaşılan şemanın
            denetimden geçtiğini, README'ye rozet koymayı hiç düşünmemiş bir
            ziyaretçinin de görmesi gerekiyor. Sunucudan gelen SVG, sayfada
            gösterilenle README'de gösterilenin aynı olmasını garantiliyor. */}
        <img
          src={`${API_BASE_URL}/share/badge/${encodeURIComponent(token)}`}
          alt="DBA score"
          className="hidden sm:block h-5 shrink-0"
        />
        <span className="ml-auto hidden sm:inline px-3 py-1 rounded-lg bg-surface-700 text-content-muted text-xs border border-surface-500 shrink-0">
          Read-only view
        </span>
        <BadgeSnippet token={token} projectName={projectName} />
        {/* Dönüşüm. Bu sayfa ürünü ilk kez gören bir kişiye açılıyor olabilir ve
            buraya kadar hiçbir yerde "sen de yapabilirsin" demiyorduk. */}
        <a
          href="/demo"
          className="ml-auto sm:ml-0 shrink-0 px-3 py-2 rounded-lg bg-content-primary text-surface-900 text-xs font-bold hover:bg-content-secondary transition-colors"
        >
          Build your own
        </a>
      </header>

      {/* Canvas */}
      <div className="flex-1">
        <ReactFlowProvider>
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
          minZoom={0.1}
          fitView
          fitViewOptions={{ padding: 0.15, minZoom: 0.4 }}
          proOptions={{ hideAttribution: false }}
        >
          <FitOnLoad dependency={nodes.length} />
          <Background variant={BackgroundVariant.Dots} gap={24} size={1} color={designToken('--color-line-solid-strong')} />
          <Controls showInteractive={false} />
          <MiniMap
            nodeColor={designToken('--color-line-solid-strong')}
            maskColor="color-mix(in srgb, var(--color-scrim) 70%, transparent)"
            style={{ background: designToken('--color-surface-700'), border: `1px solid ${designToken('--color-line-solid-strong')}` }}
          />
        </ReactFlow>
        </ReactFlowProvider>
      </div>
    </div>
  );
}
