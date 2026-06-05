import { create } from 'zustand';
import { persist, createJSONStorage, StateStorage } from 'zustand/middleware';
import { Node, Edge, OnNodesChange, OnEdgesChange, applyNodeChanges, applyEdgeChanges } from '@xyflow/react';
import localforage from 'localforage';
import { DatabaseSchema, SchemaTable, SchemaColumn } from '../types/schema';
import { schemaToFlow } from '../lib/schemaToFlow';

export type DbType = 'MSSQL' | 'PostgreSQL' | 'MySQL' | 'SQLite' | 'Oracle' | 'MariaDB' | 'Db2' | 'Firebird' | 'Spanner' | 'Redshift';

// ── UUID üretici ──────────────────────────────────────────────────────────────
const genId = (): string =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

// ── localforage (IndexedDB) State Storage Adapter ─────────────────────────────
const localforageStorage: StateStorage = {
  getItem: async (name: string): Promise<string | null> => {
    try {
      return await localforage.getItem<string>(name);
    } catch {
      return null;
    }
  },
  setItem: async (name: string, value: string): Promise<void> => {
    try {
      await localforage.setItem(name, value);
    } catch (e) {
      console.error("Zustand IndexedDB save failed: ", e);
    }
  },
  removeItem: async (name: string): Promise<void> => {
    try {
      await localforage.removeItem(name);
    } catch (e) {
      console.error("Zustand IndexedDB delete failed: ", e);
    }
  },
};

// ── Yardımcı: Schema'dan tek bir node üret ───────────────────────────────────
function tableToNode(table: SchemaTable, position: { x: number; y: number }): Node {
  return {
    id: table.id,
    type: 'tableNode',
    position,
    data: { table },
  };
}

interface SchemaState {
  // ── Ağır veri (persist edilmez) ──
  schema: DatabaseSchema | null;
  nodes: Node[];
  edges: Edge[];

  // ── Hafif UI state (persist edilir) ──
  isGenerating: boolean;
  aiProvider: 'Groq' | 'Ollama';
  modelName: string;
  projectName: string;
  dbType: DbType;

  // ── Faz 3: Düzenleme modu ──
  isEditMode: boolean;
  selectedTableForEdit: string | null;

  // ── Actions ──
  setIsGenerating: (isGenerating: boolean) => void;
  setProviderAndModel: (provider: 'Groq' | 'Ollama', model: string) => void;
  setProjectName: (name: string) => void;
  setDbType: (dbType: DbType) => void;
  loadFromSchema: (schema: DatabaseSchema, nodePositions?: Record<string, { x: number; y: number }>) => void;
  applyRevision: (partialSchema: DatabaseSchema) => void;
  resetProject: () => void;
  onNodesChange: OnNodesChange;
  onEdgesChange: OnEdgesChange;

  // ── Faz 3: Manuel düzenleme aksiyonları ──
  toggleEditMode: () => void;
  setSelectedTableForEdit: (tableId: string | null) => void;
  addTable: (x: number, y: number) => void;
  deleteTable: (tableId: string) => void;
  updateTable: (updatedTable: SchemaTable) => void;
  importFromVision: (schema: DatabaseSchema) => void;
}

export const useSchemaStore = create<SchemaState>()(
  persist(
    (set, get) => ({
      // Ağır veri — persist edilmez
      schema: null,
      nodes: [],
      edges: [],

      // Hafif state — persist edilir
      isGenerating: false,
      aiProvider: 'Groq',
      modelName: 'llama-3.3-70b-versatile',
      projectName: 'Yeni Proje',
      dbType: 'MSSQL',

      // Faz 3
      isEditMode: false,
      selectedTableForEdit: null,

      // ── Temel actions ─────────────────────────────────────────────────────
      setIsGenerating: (isGenerating) => set({ isGenerating }),
      setProviderAndModel: (provider, model) => set({ aiProvider: provider, modelName: model }),
      setProjectName: (name) => set({ projectName: name }),
      setDbType: (dbType) => set({ dbType }),

      resetProject: () => set({
        schema: null,
        nodes: [],
        edges: [],
        projectName: 'Yeni Proje',
        dbType: 'MSSQL',
        isGenerating: false,
        isEditMode: false,
        selectedTableForEdit: null,
      }),

      loadFromSchema: (schema, nodePositions) => {
        const { nodes, edges } = schemaToFlow(schema);
        const restoredNodes = nodePositions
          ? nodes.map(n => nodePositions[n.id] ? { ...n, position: nodePositions[n.id] } : n)
          : nodes;
        
        // Prioritize schema.name if it's a valid specific name (not 'Yeni Proje', not 'Shared Room Project')
        const newProjectName = (schema.name && schema.name !== 'Yeni Proje' && schema.name !== 'Shared Room Project' && schema.name.trim() !== '')
          ? schema.name
          : (get().projectName || 'Yeni Proje');

        if (schema) {
          schema.name = newProjectName;
        }

        set({ schema, nodes: restoredNodes, edges, projectName: newProjectName });
      },

      applyRevision: (partialSchema) => {
        const state = get();
        if (!state.schema) return;

        const updatedTables = [...state.schema.tables];
        partialSchema.tables.forEach(t => {
          const idx = updatedTables.findIndex(et => et.id === t.id);
          idx !== -1 ? (updatedTables[idx] = t) : updatedTables.push(t);
        });

        const updatedRelations = [...state.schema.relations];
        partialSchema.relations.forEach(r => {
          const idx = updatedRelations.findIndex(er => er.id === r.id);
          idx !== -1 ? (updatedRelations[idx] = r) : updatedRelations.push(r);
        });

        const newSchema = { ...state.schema, tables: updatedTables, relations: updatedRelations };
        const { nodes: newNodes, edges: newEdges } = schemaToFlow(newSchema);
        const finalNodes = newNodes.map(n => {
          const ex = state.nodes.find(en => en.id === n.id);
          return ex ? { ...n, position: ex.position } : n;
        });

        set({ schema: newSchema, nodes: finalNodes, edges: newEdges });
      },

      onNodesChange: (changes) => set({ nodes: applyNodeChanges(changes, get().nodes) }),
      onEdgesChange: (changes) => set({ edges: applyEdgeChanges(changes, get().edges) }),

      // ── Faz 3: Düzenleme modu actions ─────────────────────────────────────

      toggleEditMode: () => set(s => ({ isEditMode: !s.isEditMode, selectedTableForEdit: null })),

      setSelectedTableForEdit: (tableId) => set({ selectedTableForEdit: tableId }),

      /**
       * Belirtilen canvas koordinatına boş yeni bir tablo ekler.
       * Schema + nodes atomik olarak güncellenir.
       */
      addTable: (x, y) => {
        const state = get();
        if (!state.schema) return;

        const newTableId = genId();
        const pkColId = genId();

        const defaultPkCol: SchemaColumn = {
          id: pkColId,
          name: 'Id',
          type: 'INT',
          length: null,
          isPK: true,
          isFK: false,
          isNullable: false,
          defaultValue: null,
          stableUuid: typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : genId(),
        };

        const newTable: SchemaTable = {
          id: newTableId,
          name: `Yeni_Tablo_${state.schema.tables.length + 1}`,
          columns: [defaultPkCol],
          stableUuid: typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : genId(),
        };

        const newSchema: DatabaseSchema = {
          ...state.schema,
          tables: [...state.schema.tables, newTable],
        };

        const newNode = tableToNode(newTable, { x, y });

        set({
          schema: newSchema,
          nodes: [...state.nodes, newNode],
          // edges değişmez
        });
      },

      /**
       * Tabloyu, ilişkili tüm edge/relation'ları ve schema'dan siler.
       * Atomik tek set() çağrısı.
       */
      deleteTable: (tableId) => {
        const state = get();
        if (!state.schema) return;

        const newTables = state.schema.tables.filter(t => t.id !== tableId);
        const newRelations = state.schema.relations.filter(
          r => r.sourceTableId !== tableId && r.targetTableId !== tableId
        );
        const newSchema: DatabaseSchema = { ...state.schema, tables: newTables, relations: newRelations };

        // Silinen tableId'ye bağlı edge'leri de temizle
        const relatedRelIds = state.schema.relations
          .filter(r => r.sourceTableId === tableId || r.targetTableId === tableId)
          .map(r => r.id);

        set({
          schema: newSchema,
          nodes: state.nodes.filter(n => n.id !== tableId),
          edges: state.edges.filter(e => !relatedRelIds.includes(e.id)),
          selectedTableForEdit: state.selectedTableForEdit === tableId ? null : state.selectedTableForEdit,
        });
      },

      /**
       * Drawer'dan gelen güncellenmiş tablo ile schema ve ilgili node'u günceller.
       * Diğer node'ların pozisyonları korunur.
       */
      updateTable: (updatedTable) => {
        const state = get();
        if (!state.schema) return;

        const newTables = state.schema.tables.map(t =>
          t.id === updatedTable.id ? updatedTable : t
        );
        const newSchema: DatabaseSchema = { ...state.schema, tables: newTables };

        // Sadece ilgili node'u güncelle, pozisyonu koru
        const existingNode = state.nodes.find(n => n.id === updatedTable.id);
        const updatedNode: Node = {
          ...(existingNode ?? tableToNode(updatedTable, { x: 0, y: 0 })),
          data: { table: updatedTable },
        };

        set({
          schema: newSchema,
          nodes: state.nodes.map(n => n.id === updatedTable.id ? updatedNode : n),
        });
      },

      importFromVision: (visionSchema) => {
        const state = get();
        const currentSchema = state.schema || { schemaId: genId(), name: 'Yeni Proje', tables: [], relations: [] };

        const visionTables = visionSchema.tables || (visionSchema as any).Tables || [];
        const visionRelations = visionSchema.relations || (visionSchema as any).Relations || [];

        // Calculate Y-offset so new nodes are placed below existing nodes
        let maxY = 0;
        state.nodes.forEach(n => {
          if (n.position.y > maxY) {
            maxY = n.position.y;
          }
        });
        const startY = state.nodes.length > 0 ? maxY + 350 : 0;

        const updatedTables = [...currentSchema.tables];
        const updatedRelations = [...currentSchema.relations];

        const idMap: Record<string, string> = {};
        const GRID_SPACING_X = 400;
        const GRID_SPACING_Y = 300;
        const MAX_COLUMNS = 3;

        const newNodes: Node[] = [];

        // 1. Create clean tables and grid-positioned nodes
        visionTables.forEach((t: SchemaTable, index: number) => {
          const newId = genId();
          const oldId = t.id || t.name;
          idMap[oldId] = newId;

          const newTable: SchemaTable = {
            id: newId,
            name: t.name || `Tablo_${genId().substring(0, 4)}`,
            stableUuid: t.stableUuid || (typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : genId()),
            columns: t.columns.map(c => ({
              id: genId(),
              name: c.name || 'Column',
              type: c.type || 'INT',
              length: c.length,
              isPK: c.isPK,
              isFK: c.isFK,
              isNullable: c.isNullable,
              defaultValue: c.defaultValue,
              stableUuid: c.stableUuid || (typeof crypto !== 'undefined' && crypto.randomUUID ? crypto.randomUUID() : genId()),
            }))
          };

          updatedTables.push(newTable);

          const row = Math.floor(index / MAX_COLUMNS);
          const col = index % MAX_COLUMNS;
          
          newNodes.push({
            id: newId,
            type: 'tableNode',
            position: {
              x: col * GRID_SPACING_X,
              y: startY + (row * GRID_SPACING_Y)
            },
            data: { table: newTable }
          });
        });

        // 2. Create relationships with mapped table IDs
        visionRelations.forEach((r: any) => {
          const mappedSourceTableId = idMap[r.sourceTableId] || r.sourceTableId;
          const mappedTargetTableId = idMap[r.targetTableId] || r.targetTableId;

          // Resolve columns to bind relations
          const sourceTable = updatedTables.find(t => t.id === mappedSourceTableId);
          const targetTable = updatedTables.find(t => t.id === mappedTargetTableId);

          const sourceColumnId = sourceTable?.columns.find(c => c.isFK)?.id || genId();
          const targetColumnId = targetTable?.columns.find(c => c.isPK)?.id || genId();

          updatedRelations.push({
            id: genId(),
            type: r.type || 'OneToMany',
            sourceTableId: mappedSourceTableId,
            sourceColumnId: sourceColumnId,
            targetTableId: mappedTargetTableId,
            targetColumnId: targetColumnId
          });
        });

        const newSchema: DatabaseSchema = {
          ...currentSchema,
          tables: updatedTables,
          relations: updatedRelations
        };

        const { edges: mappedEdges } = schemaToFlow(newSchema);

        set({
          schema: newSchema,
          nodes: [...state.nodes, ...newNodes],
          edges: mappedEdges
        });
      },

    }),
    {
      name: 'namines-ui-state',
      storage: createJSONStorage(() => localforageStorage),
      partialize: (state) => ({
        projectName: state.projectName,
        dbType: state.dbType,
        aiProvider: state.aiProvider,
        modelName: state.modelName,
        schema: state.schema,
        nodes: state.nodes,
        edges: state.edges,
      }),
    }
  )
);
