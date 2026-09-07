'use client';

import { useMemo, useState } from 'react';
import {
  ReactFlow, Background, Controls, MiniMap, Panel,
  type Node, type Edge, type NodeProps, Handle, Position,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import dagre from '@dagrejs/dagre';
import { type DeskTable } from '../lib/schema';
import { parseDesignTables, parseNodePositions } from '../lib/designSchema';

/**
 * D3 — Canvas (namines_desk/03-CANVAS.md). Salt-okunur şema haritası.
 *
 * İki kaynak var ve AYNI ŞEYİ göstermiyor: `SchemaJson` kullanıcının ana
 * uygulamada ÇİZDİĞİ tasarım (yerleşim bilgisiyle birlikte), `GET
 * /api/gateway/schema` veritabanının GERÇEK hâli (canlı introspection).
 * Ayrışmış olabilirler (elle ALTER TABLE, başka bir araç). Karar: ikisini de
 * göster, farkı işaretle — tek kaynağa yaslanmak ya yanlış kolonları
 * gösterirdi (tasarım) ya da kullanıcının düzenini kaybederdi (canlı).
 *
 * Konum ASLA kaydedilmiş: sürükleme/zoom yalnızca görüntüleme. Ana
 * uygulamadaki düzenle çakışacak bir "Desk'in kendi düzeni" yaratmıyoruz.
 */

const GRID_COLS = 4;
const GRID_SPACING_X = 300;
const GRID_SPACING_Y = 260;

type DriftKind = 'none' | 'not_in_design' | 'not_in_db';

interface SchemaNodeData extends Record<string, unknown> {
  table: DeskTable | null; // null => yalnizca tasarimda var, canli DB'de yok (hayalet)
  name: string;
  drift: DriftKind;
  selected: boolean;
}

function SchemaTableNode({ data }: NodeProps<Node<SchemaNodeData>>) {
  const { table, name, drift, selected } = data;
  const ghost = drift === 'not_in_db';

  return (
    <div
      style={{
        width: 220,
        borderRadius: 'var(--radius-card)',
        border: `1px solid ${selected ? 'var(--accent-hover)' : ghost ? 'var(--line)' : 'var(--line-strong)'}`,
        boxShadow: selected ? '0 0 0 2px var(--accent-subtle)' : 'none',
        background: 'var(--surface-800)',
        opacity: ghost ? 0.55 : 1,
        overflow: 'hidden',
        fontSize: 11.5,
      }}
    >
      <Handle type="target" position={Position.Left} style={{ opacity: 0 }} />
      <Handle type="source" position={Position.Right} style={{ opacity: 0 }} />

      <div style={{
        padding: '7px 10px', background: 'var(--surface-700)',
        display: 'flex', alignItems: 'center', justifyContent: 'space-between',
        borderBottom: '1px solid var(--line)', fontWeight: 700,
      }}>
        <span>{name}</span>
        <span style={{ fontWeight: 400, color: 'var(--content-subtle)' }}>
          {table ? `${table.columns.length} cols` : ''}
        </span>
      </div>

      {drift !== 'none' && (
        <div style={{
          padding: '4px 10px', fontSize: 10, fontWeight: 600,
          color: drift === 'not_in_db' ? 'var(--content-subtle)' : 'var(--accent-text)',
          background: 'var(--surface-700)',
        }}>
          {drift === 'not_in_db' ? 'veritabanında yok' : 'tasarımda yok'}
        </div>
      )}

      {table ? (
        <div>
          {table.columns.map(c => (
            <div key={c.name} style={{
              padding: '4px 10px', display: 'flex', gap: 6, alignItems: 'center',
              color: 'var(--content-secondary)', borderBottom: '1px solid var(--line)',
            }}>
              <span style={{ width: 14, textAlign: 'center', fontSize: 10 }}>
                {c.isPK ? '🔑' : c.isFK ? '🔗' : ''}
              </span>
              <span style={{ flex: 1 }}>{c.name}</span>
              <span style={{ color: 'var(--content-subtle)', fontSize: 10 }}>{c.type}</span>
            </div>
          ))}
        </div>
      ) : (
        <div style={{ padding: '8px 10px', color: 'var(--content-subtle)' }}>
          Bu tablo yalnızca tasarımda var — canlı veritabanında bulunamadı.
        </div>
      )}
    </div>
  );
}

const nodeTypes = { schemaTable: SchemaTableNode };

const NODE_WIDTH = 220;

function estimateNodeHeight(data: SchemaNodeData): number {
  const header = 34;
  const driftBanner = data.drift !== 'none' ? 20 : 0;
  const body = data.table ? data.table.columns.length * 22 : 40;
  return header + driftBanner + body;
}

/**
 * Namines Desk v2 §E3.1 — "yeniden yerleştir" düğmesi. `@dagrejs/dagre` ile
 * TÜM düğümler (hayaletler dahil) yeniden konumlandırılır — yalnızca bu
 * oturumun görüntüsü için, ASLA kaydedilmez (Canvas'ın en üstteki tasarım
 * kararıyla aynı ilke: konum burada bir gerçek kaynağı değil).
 */
function computeDagreLayout(nodes: Node<SchemaNodeData>[], edges: Edge[]): Record<string, { x: number; y: number }> {
  const g = new dagre.graphlib.Graph();
  g.setGraph({ rankdir: 'LR', nodesep: 40, ranksep: 90 });
  g.setDefaultEdgeLabel(() => ({}));

  for (const node of nodes) {
    g.setNode(node.id, { width: NODE_WIDTH, height: estimateNodeHeight(node.data) });
  }
  for (const edge of edges) {
    if (g.hasNode(edge.source) && g.hasNode(edge.target)) g.setEdge(edge.source, edge.target);
  }

  dagre.layout(g);

  const result: Record<string, { x: number; y: number }> = {};
  for (const node of nodes) {
    const pos = g.node(node.id);
    // dagre merkez koordinatı verir, React Flow sol-üst köşe bekler.
    result[node.id] = { x: pos.x - NODE_WIDTH / 2, y: pos.y - estimateNodeHeight(node.data) / 2 };
  }
  return result;
}

export default function Canvas({
  tables, schemaJson, nodePositionsJson, onOpenTable,
}: {
  tables: DeskTable[];
  schemaJson: string | null;
  nodePositionsJson: string | null;
  onOpenTable: (tableName: string) => void;
}) {
  const [selected, setSelected] = useState<string | null>(null);
  // Namines Desk v2 §E3.1 — yalnızca bu oturumun görüntüsü, ASLA kaydedilmez.
  const [layoutOverride, setLayoutOverride] = useState<Record<string, { x: number; y: number }> | null>(null);

  const { nodes, edges } = useMemo(() => {
    const designTables = parseDesignTables(schemaJson);
    const positions = parseNodePositions(nodePositionsJson);
    const designByName = new Map(designTables.map(t => [t.name, t.id]));
    const liveNames = new Set(tables.map(t => t.name));

    let gridIndex = 0;
    function autoPosition(): { x: number; y: number } {
      const pos = { x: (gridIndex % GRID_COLS) * GRID_SPACING_X, y: Math.floor(gridIndex / GRID_COLS) * GRID_SPACING_Y };
      gridIndex += 1;
      return pos;
    }

    const builtNodes: Node<SchemaNodeData>[] = [];

    for (const table of tables) {
      const designId = designByName.get(table.name);
      const pos = layoutOverride?.[table.name] ?? ((designId && positions[designId]) || autoPosition());
      builtNodes.push({
        id: table.name,
        type: 'schemaTable',
        position: pos,
        draggable: true,
        data: {
          table, name: table.name,
          drift: designByName.has(table.name) ? 'none' : 'not_in_design',
          selected: selected === table.name,
        },
      });
    }

    // Tasarımda var ama canlıda yok — hayalet düğüm (drift'in diğer yönü).
    for (const dt of designTables) {
      if (liveNames.has(dt.name)) continue;
      const pos = layoutOverride?.[`ghost:${dt.name}`] ?? (positions[dt.id] || autoPosition());
      builtNodes.push({
        id: `ghost:${dt.name}`,
        type: 'schemaTable',
        position: pos,
        draggable: true,
        data: { table: null, name: dt.name, drift: 'not_in_db', selected: false },
      });
    }

    const builtEdges: Edge[] = [];
    for (const table of tables) {
      for (const col of table.columns) {
        if (!col.references) continue;
        builtEdges.push({
          id: `${table.name}.${col.name}->${col.references.table}`,
          source: table.name,
          target: col.references.table,
          label: col.name,
          style: { stroke: 'var(--line-strong)' },
        });
      }
    }

    return { nodes: builtNodes, edges: builtEdges };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tables, schemaJson, nodePositionsJson, selected, layoutOverride]);

  function autoLayout() {
    setLayoutOverride(computeDagreLayout(nodes, edges));
  }

  const selectedTable = selected ? tables.find(t => t.name === selected) ?? null : null;

  return (
    <div style={{ display: 'flex', flex: 1, minHeight: 0 }}>
      <div style={{ flex: 1, minWidth: 0 }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          colorMode="light"
          nodesConnectable={false}
          onNodeClick={(_, node) => setSelected(node.id.startsWith('ghost:') ? null : node.id)}
          onNodeDoubleClick={(_, node) => {
            if (!node.id.startsWith('ghost:')) onOpenTable(node.id);
          }}
          onPaneClick={() => setSelected(null)}
          // Konum ASLA kaydedilmiyor — sürükleme yalnızca bu oturumda görsel.
          fitView
        >
          <Background />
          <Controls showInteractive={false} />
          <MiniMap pannable zoomable style={{ background: 'var(--surface-800)' }} />
          <Panel position="top-right" style={{ display: 'flex', gap: 6 }}>
            <button className="btn btn-sm" onClick={autoLayout}>Otomatik yerleştir</button>
            {layoutOverride && (
              <button className="btn btn-sm" onClick={() => setLayoutOverride(null)}>Düzeni sıfırla</button>
            )}
          </Panel>
        </ReactFlow>
      </div>

      {selectedTable && (
        <aside style={{
          width: 260, flexShrink: 0, borderLeft: '1px solid var(--line)',
          background: 'var(--surface-800)', padding: 14, overflowY: 'auto',
        }}>
          <div style={{ fontWeight: 700, marginBottom: 4 }}>{selectedTable.name}</div>
          <div style={{ fontSize: 11.5, color: 'var(--content-muted)', marginBottom: 12 }}>
            {selectedTable.columns.length} kolon{!selectedTable.canWrite && ' · salt-okunur'}
          </div>
          {selectedTable.columns.map(c => (
            <div key={c.name} style={{ fontSize: 12, marginBottom: 6 }}>
              <div style={{ display: 'flex', gap: 5 }}>
                {c.isPK && <span className="col-badge">PK</span>}
                {c.isFK && <span className="col-badge">FK</span>}
                <span>{c.name}</span>
              </div>
              <div style={{ color: 'var(--content-subtle)', fontSize: 10.5 }}>
                {c.type}{c.length ? `(${c.length})` : ''}
                {c.references && ` → ${c.references.table}.${c.references.column}`}
              </div>
            </div>
          ))}
          <button className="btn btn-sm btn-primary" style={{ width: '100%', marginTop: 8 }}
                  onClick={() => onOpenTable(selectedTable.name)}>
            Veriye git
          </button>
        </aside>
      )}
    </div>
  );
}
