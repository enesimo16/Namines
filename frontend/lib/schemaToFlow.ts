import { Node, Edge } from '@xyflow/react';
import { DatabaseSchema, SchemaTable } from '../types/schema';

export function schemaToFlow(schema: DatabaseSchema): { nodes: Node[]; edges: Edge[] } {
  const nodes: Node[] = [];
  const edges: Edge[] = [];

  const GRID_SPACING_X = 400;
  const GRID_SPACING_Y = 300;
  const MAX_COLUMNS = 3;

  // Null guard — API may return PascalCase (Tables) or camelCase (tables)
  const tables: SchemaTable[] = (schema as any).Tables ?? schema.tables ?? [];
  const relations = (schema as any).Relations ?? schema.relations ?? [];

  tables.forEach((table: SchemaTable, index: number) => {
    const row = Math.floor(index / MAX_COLUMNS);
    const col = index % MAX_COLUMNS;

    nodes.push({
      id: table.id,
      type: 'tableNode',
      position: { x: col * GRID_SPACING_X, y: row * GRID_SPACING_Y },
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
      style: { stroke: '#6366f1', strokeWidth: 2 },
    });
  });

  return { nodes, edges };
}
