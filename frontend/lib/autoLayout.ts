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
// <b>BİLİNEN SINIR (dürüstlük notu).</b> Dagre burada YALNIZCA düğüm konumu
// üretiyor; kenarları React Flow kendi çiziyor ve bunu iki KOLON tutamacı
// arasında düz bir bezier olarak yapıyor. Dagre'nin kendi kenar yönlendirmesi
// (ara katmanlara koyduğu sanal düğümlerle çizgiyi kırma) kullanılmıyor.
// Sonuç: birden fazla katman atlayan bir FK (ör. `comments.author_id → users`,
// aralarında `tasks` varken) aradaki tabloların üzerinden düz geçer.
// Canlı ölçüm ("Tasks & Projects", 6 tablo / 9 ilişki): 9 kenardan 10 çift
// kesişiyor, düğüm çakışması 0. Kesişimi daha da düşürmek kenar yönlendirme
// (orthogonal routing) gerektirir — ayrı ve çok daha büyük bir iş.
//
// Node boyutları GERÇEK render'la senkron olmak ZORUNDA: dagre her düğüm için
// verilen yüksekliği rezerve eder ve düğümü o alanın MERKEZİNE koyar. Tahmin
// küçükse dagre düğümleri gerçekte olduklarından yakın sanır → satırlar üst üste
// biner, kenarlar düğümlerin içinden geçer.
//
// <b>ÖLÇÜLDÜ (canlı DOM, zoom geri alınarak, "Tasks & Projects" şeması):</b>
//   task_labels 3 kolon → 168px · users 4 → 204 · comments 5 → 241
//   projects 6 → 277 · tasks 11 → 460
// İki noktadan doğru: (204-168)/(4-3) = 36px/satır, taban = 168 - 3*36 = 60px.
// Doğrulama: 5 kolon → 60+180 = 240 (gerçek 241) · 11 → 60+396 = 456 (gerçek 460).
//
// Eski değerler (48 taban + 32/satır) 11 kolonluk `tasks` için 412px diyordu —
// gerçeğin 48px altında, yani %10 hata. "Düzenleme hâlâ hatalı" geri bildiriminin
// ölçülebilir nedeni buydu.
const NODE_WIDTH = 288;
const NODE_HEADER = 60;
const ROW_HEIGHT = 36;

function estimateNodeHeight(node: Node): number {
  const columnCount = (node.data as any)?.table?.columns?.length ?? 0;
  return NODE_HEADER + columnCount * ROW_HEIGHT;
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
    ranksep: 140,
    // Aynı katmandaki tablolar arası boşluk. Tablolar 460px'e kadar uzayabildiği
    // için dar bir değer, komşu tabloların kenar etiketlerini (`1:N` rozetleri)
    // birbirine yapıştırıyordu.
    nodesep: 80,
    // Şemalarda döngü NORMALDİR (karşılıklı FK, `parent_id` gibi kendine
    // referanslar). Dagre döngülü bir çizgede katman atayamaz; varsayılan DFS
    // tabanlı kırıcı yerine `greedy` sezgiseli, geri çevrilen kenar sayısını
    // daha iyi azaltıyor — az geri kenar = az uzun/çapraz bağlantı.
    acyclicer: 'greedy',
    // `ranker` bilinçli olarak VARSAYILANDA ('network-simplex') bırakıldı:
    // 'tight-tree' canlı ölçüldü, bu şemada tıpatıp aynı sonucu verdi
    // (10 kesişen kenar çifti, 11855px toplam kenar uzunluğu) — hiçbir şey
    // kazandırmayan bir ayarı taşımamak için eklenmedi.
    marginx: 40,
    marginy: 40,
  });

  nodes.forEach(node => {
    g.setNode(node.id, { width: NODE_WIDTH, height: estimateNodeHeight(node) });
  });

  edges.forEach(edge => {
    // Kendine referans (self-relation) dagre'de döngü hatası üretir — atla.
    if (edge.source === edge.target) return;
    // <b>YÖN TERS ÇEVRİLİYOR — kasıtlı.</b> Şemadaki kenar ÇOCUK→EBEVEYN akar
    // (`tasks.project_id` → `projects.id`). Dagre kaynakları ilk katmana koyar;
    // ham yönle beslenince `users`/`projects` gibi HERKESİN referans verdiği
    // birkaç tablo en SON katmana düşüyor ve tuvalin yarısını geçen onlarca
    // çizgi onların üzerinde toplanıyordu (kullanıcının ekran görüntüsündeki
    // tam olarak buydu: `users` en sağda, her şey ona uzanıyor).
    //
    // Ters çevirince ebeveynler solda başlıyor, çocuklar sağa doğru DAĞILIYOR:
    // bir hub'ın çok sayıda kısa kenarı olur, çok sayıda uzun kenarı değil.
    // Yalnızca YERLEŞİM hesabını etkiler — React Flow'un çizdiği gerçek kenar
    // yönü/oku değişmez.
    g.setEdge(edge.target, edge.source);
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
