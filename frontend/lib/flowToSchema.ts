import { Node, Edge } from '@xyflow/react';
import { DatabaseSchema, SchemaTable, SchemaRelation, ReferentialAction } from '../types/schema';

export function flowToSchema(
  originalSchema: DatabaseSchema | null,
  nodes: Node[],
  edges: Edge[]
): DatabaseSchema | null {
  if (!originalSchema) return null;

  // We reconstruct the tables from nodes
  const updatedTables = nodes
    .filter((n) => n.type === 'tableNode')
    .map((n) => n.data.table as SchemaTable);

  // We reconstruct the relations from edges
  // Note: Only keeping edges that map back to schema relations correctly
  const updatedRelations: SchemaRelation[] = edges
    .filter((e) => e.type === 'relationEdge')
    .map((e) => ({
      id: e.id,
      type: (e.data?.relationType as string) || 'OneToMany',
      sourceTableId: e.source,
      sourceColumnId: e.sourceHandle || '',
      targetTableId: e.target,
      targetColumnId: e.targetHandle || '',
      // FK davranışını koru. Bu satırlar olmadan kullanıcının seçtiği ON DELETE
      // değeri canvas'a her dokunulduğunda sessizce kaybolur.
      onDelete: (e.data?.onDelete as ReferentialAction) || 'NoAction',
      onUpdate: (e.data?.onUpdate as ReferentialAction) || 'NoAction',
    }));

  return {
    ...originalSchema,
    tables: updatedTables,
    relations: updatedRelations,
  };
}
