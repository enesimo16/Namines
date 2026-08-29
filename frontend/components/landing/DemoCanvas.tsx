'use client';

import { useEffect } from 'react';
import {
  ReactFlow,
  ReactFlowProvider,
  Background,
  BackgroundVariant,
  Controls,
  useNodesInitialized,
  useReactFlow,
  type Node,
  type Edge,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import TableNode from '../canvas/nodes/TableNode';
import { token as designToken } from '../../lib/designTokens';

const nodeTypes = { tableNode: TableNode };

/**
 * Demo sayfasının salt okunur tuvali.
 *
 * **Neden ayrı bir dosya:** ızgara rengi CSS değişkeninden okunuyor ve sunucuda
 * CSS yok — orada `transparent` dönüp istemcideki gerçek renkle uyuşmuyordu
 * (hydration uyarısı). Önce bir efekt + durum ile "monte olduktan sonra çiz"
 * yapılmıştı; bu, efekt içinde senkron `setState` demekti ve zincirleme render
 * üretiyordu. Bileşeni ayırıp `ssr: false` ile yüklemek aynı işi durum
 * tutmadan yapıyor: bu ağaç zaten yalnızca tarayıcıda render ediliyor.
 */

/**
 * Düğümler ÖLÇÜLDÜKTEN sonra görünümü şemaya oturtur.
 *
 * `fitView` bayrağı yalnızca ilk kurulumda ve düğüm boyutları biliniyorsa
 * çalışıyor. 5-6 tablolu şemalarda bu yetiyordu; 25 tabloda ölçüm ilk render'a
 * yetişmiyor ve görünüm dönüşümü birim kalıyordu — yani tuval ekranda tamamen
 * BOŞ görünüyordu, üstelik 25 düğüm de DOM'daydı.
 */
function FitOnLoad({ dependency }: { dependency: string }) {
  const initialised = useNodesInitialized();
  const { fitView } = useReactFlow();

  useEffect(() => {
    // minZoom: okunaksızlığa kadar uzaklaşmaktansa, şemanın tamamını göstermeyi
    // bırakıp kullanıcıyı kaydırmaya bırakmak daha dürüst — 25 tabloyu tek
    // ekrana sığdırmak zaten mümkün değil.
    if (initialised) void fitView({ padding: 0.12, minZoom: 0.45 });
  }, [initialised, dependency, fitView]);

  return null;
}

export default function DemoCanvas({
  nodes,
  edges,
  resetKey,
}: {
  nodes: Node[];
  edges: Edge[];
  /** Şablon değişince tuvali yeniden kurar; `fitView` yalnızca kurulumda çalışıyor. */
  resetKey: string;
}) {
  return (
    <ReactFlowProvider>
      <ReactFlow
        key={resetKey}
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        deleteKeyCode={null}
        // Tekerlek sayfayı kaydırsın, tuvali yakınlaştırmasın. Tuval tam
        // genişlikte ve 560px yüksekliğinde; varsayılan davranışta sayfayı
        // kaydırmaya çalışan ziyaretçi tuvalin içinde sıkışıp kalıyor ve şema
        // kendiliğinden yakınlaşıyordu. Yakınlaştırma alt soldaki denetimlerde.
        zoomOnScroll={false}
        preventScrolling={false}
        minZoom={0.1}
        fitView
        fitViewOptions={{ padding: 0.12, minZoom: 0.45 }}
      >
        <FitOnLoad dependency={resetKey} />
        <Background
          variant={BackgroundVariant.Dots}
          gap={24}
          size={1}
          color={designToken('--color-line-solid-strong')}
        />
        <Controls showInteractive={false} />
      </ReactFlow>
    </ReactFlowProvider>
  );
}
