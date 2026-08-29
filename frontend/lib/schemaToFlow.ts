import { Node, Edge } from '@xyflow/react';
import { DatabaseSchema, SchemaTable } from '../types/schema';

export function schemaToFlow(schema: DatabaseSchema): { nodes: Node[]; edges: Edge[] } {
  const nodes: Node[] = [];
  const edges: Edge[] = [];

  const GRID_SPACING_X = 400;

  // TableNode ölçüleri (bkz. components/canvas/nodes/TableNode.tsx): 320px genişlik,
  // ~52px başlık + 12px iç boşluk, kolon başına ~38px.
  const NODE_HEADER = 64;
  const ROW_HEIGHT = 38;
  const ROW_GAP = 60;

  // Null guard — API may return PascalCase (Tables) or camelCase (tables)
  const tables: SchemaTable[] = (schema as any).Tables ?? schema.tables ?? [];
  const relations = (schema as any).Relations ?? schema.relations ?? [];

  // Sütun sayısı tablo sayısına göre değişiyor, sabit 3 DEĞİL.
  //
  // Sabit 3 sütun, 5 tablolu bir şemada makuldü ama 25 tabloluk gerçek bir
  // şemayı 9 satır yüksekliğinde ince bir şeride çeviriyordu: yatay bir
  // panelde `fitView` sonrası hiçbir şey okunmuyordu. Kare köke yaklaşmak,
  // düzeni tuvalin en-boy oranına yakın tutuyor.
  const columns = Math.max(3, Math.ceil(Math.sqrt(tables.length)));

  // Satır yüksekliği SABİT DEĞİL, o satırdaki en uzun tabloya göre.
  // Sabit 300px, 12 kolonlu bir tablonun altındakinin üzerine binmesine yol
  // açıyordu — ve bindirmeyi düzelten kişi genelde kullanıcı oluyordu.
  const rowOffsets: number[] = [];
  let cursor = 0;
  for (let row = 0; row * columns < tables.length; row++) {
    rowOffsets.push(cursor);
    const inRow = tables.slice(row * columns, (row + 1) * columns);
    const tallest = Math.max(0, ...inRow.map(t => (t.columns?.length ?? 0)));
    cursor += NODE_HEADER + tallest * ROW_HEIGHT + ROW_GAP;
  }

  tables.forEach((table: SchemaTable, index: number) => {
    const row = Math.floor(index / columns);
    const col = index % columns;

    nodes.push({
      id: table.id,
      type: 'tableNode',
      position: { x: col * GRID_SPACING_X, y: rowOffsets[row] },
      data: {
        table: table,
      },
    });
  });

  relations.forEach((relation: any) => {
    edges.push({
      id: relation.id,
      type: 'relationEdge',
      source: relation.sourceTableId,
      sourceHandle: relation.sourceColumnId,
      target: relation.targetTableId,
      targetHandle: relation.targetColumnId,
      data: {
        relationType: relation.type,
        // FK davranışını edge.data'da taşı — flowToSchema geri okuyacak.
        // Taşınmazsa canvas'ta yapılan HER düzenlemede bu değerler sıfırlanır.
        onDelete: relation.onDelete || 'NoAction',
        onUpdate: relation.onUpdate || 'NoAction',
      },
      animated: true,
      style: { stroke: '#5b6b93', strokeWidth: 2 },
    });
  });

  return { nodes, edges };
}
