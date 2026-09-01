import dagre from '@dagrejs/dagre';
import { Node, Edge } from '@xyflow/react';

// ── Otomatik Yerleşim (Tidy Up) ──────────────────────────────────────────────
//
// `schemaToFlow` her zaman kolon sayısına göre bir KARE-KÖK IZGARASI kullanıyor
// (bkz. lib/schemaToFlow.ts) — tablo N, ızgaradaki N. sırada, ilişkilerden
// TAMAMEN BAĞIMSIZ. 25 tablolu bir e-ticaret şemasında `orders` ızgarada
// `users`'dan 3 satır uzakta olabiliyor ama aralarında FK var — bağlantı çizgisi
// yarım tuvali çaprazlıyor ve yolda duran her tabloyla kesişiyor. Bu, "bağlantılar
// hep iç içe geçiyor" geri bildiriminin doğrudan nedeni.
//
// Çözüm: dagre — yönlü çizge (DAG) katman yerleşimi. İlişkili tablolar aynı
// katmana veya komşu katmana düşer, dagre kenar geçişlerini (crossing) minimize
// etmeye çalışan bir sıralama algoritması (Sugiyama) kullanır. Kesişim SIFIRA
// inmeyebilir (çok-döngülü şemalarda matematiksel olarak imkansız olabilir) ama
// kare-kök ızgaraya göre ölçülebilir şekilde daha az.
//
// Node boyutları schemaToFlow'daki sabitlerle SENKRON tutulmalı — biri değişip
// diğeri değişmezse dagre, node'ları birbirinin üstüne bindirir ya da aralarında
// gereksiz boşluk bırakır (bkz. TableNode.tsx `w-72` = 288px).
const NODE_WIDTH = 288;
const NODE_HEADER = 48;
const ROW_HEIGHT = 32;
const NODE_FOOTER_PADDING = 12;

function estimateNodeHeight(node: Node): number {
  const columnCount = (node.data as any)?.table?.columns?.length ?? 0;
  return NODE_HEADER + columnCount * ROW_HEIGHT + NODE_FOOTER_PADDING;
}

export type LayoutDirection = 'LR' | 'TB';

/**
 * Verilen node/edge kümesini dagre ile yeniden diz, YENİ node dizisini döndürür
 * (girdileri mutate etmez — çağıran, undo geçmişine orijinali zaten koymuş olabilir).
 *
 * `direction`: 'LR' (soldan sağa) şema diyagramları için genelde 'TB'den daha
 * okunur — FK zincirleri genelde yatay akar (users → orders → order_items) ve
 * TableNode'lar zaten dar-uzun (288px genişlik, N satır yüksekliği); LR bu
 * en-boy oranıyla daha iyi eşleşiyor, TB'de uzun kolonlu tablolar aşırı
 * dikeyleşip yatayda boşluk israf ediyordu.
 */
export function getLayoutedNodes(
  nodes: Node[],
  edges: Edge[],
  direction: LayoutDirection = 'LR'
): Node[] {
  if (nodes.length === 0) return nodes;

  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({
    rankdir: direction,
    // Katmanlar arası (ilişki yönündeki) boşluk — tablo genişliği + nefes payı.
    ranksep: 120,
    // Aynı katmandaki tablolar arası boşluk.
    nodesep: 64,
    marginx: 40,
    marginy: 40,
  });

  nodes.forEach(node => {
    g.setNode(node.id, { width: NODE_WIDTH, height: estimateNodeHeight(node) });
  });

  edges.forEach(edge => {
    // Kendine referans (self-relation) dagre'de döngü hatası üretir — atla.
    if (edge.source === edge.target) return;
    g.setEdge(edge.source, edge.target);
  });

  dagre.layout(g);

  return nodes.map(node => {
    const pos = g.node(node.id);
    if (!pos) return node;
    // dagre merkez-nokta döndürür, React Flow sol-üst köşe bekliyor.
    return {
      ...node,
      position: { x: pos.x - NODE_WIDTH / 2, y: pos.y - estimateNodeHeight(node) / 2 },
    };
  });
}
