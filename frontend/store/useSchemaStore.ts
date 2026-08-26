import { create } from 'zustand';
import { persist, createJSONStorage, StateStorage } from 'zustand/middleware';
import { Node, Edge, Connection, OnNodesChange, OnEdgesChange, applyNodeChanges, applyEdgeChanges } from '@xyflow/react';
import localforage from 'localforage';
import { DatabaseSchema, SchemaTable, SchemaColumn, SchemaRelation } from '../types/schema';
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

const HISTORY_LIMIT = 50;

type HistorySnapshot = { schema: DatabaseSchema; nodes: Node[] };

interface SchemaState {
  // ── Ağır veri (persist edilmez) ──
  schema: DatabaseSchema | null;
  nodes: Node[];
  edges: Edge[];

  // ── Undo/Redo geçmişi ──
  _past: HistorySnapshot[];
  _future: HistorySnapshot[];

  // ── Hafif UI state (persist edilir) ──
  isGenerating: boolean;
  /**
   * Kullanicinin sectigi Namines AI modeli: 'nai-flash' | 'nai' | 'nai-pro'.
   *
   * Saglayici adi (Groq/Gemini/Ollama) ARTIK TUTULMUYOR. Kullanicinin hangi
   * saglayicinin hangi modelini kullandigini bilmesi gerekmiyor ve bilmesi
   * zararliydi: yapilandirmadaki bir Groq modeli bir gun kaldirildi, secili
   * kalan kullanicilarin sema uretimi tamamen durdu. Esleme artik sunucuda.
   */
  naiModel: string;
  projectName: string;
  dbType: DbType;
  /**
   * Canvas'ta daha önce gönderilen prompt'lar — en yeni önce, en fazla 15.
   * second-phase/08-PROMPT-DENEYIMI.md §8.3.
   *
   * Kullanıcıya AİT ve yalnızca istemcide (bkz. §8.2 not) — sunucuda kalıcı
   * bir "sohbet oturumu" değil, sadece kolaylık.
   */
  promptHistory: string[];

  // ── Faz 3: Düzenleme modu ──
  isEditMode: boolean;
  selectedTableForEdit: string | null;

  // ── Actions ──
  setIsGenerating: (isGenerating: boolean) => void;
  setNaiModel: (model: string) => void;
  setProjectName: (name: string) => void;
  setDbType: (dbType: DbType) => void;
  addPromptToHistory: (prompt: string) => void;
  loadFromSchema: (schema: DatabaseSchema, nodePositions?: Record<string, { x: number; y: number }>, preserveProjectName?: boolean) => void;
  applyRevision: (partialSchema: DatabaseSchema) => void;
  resetProject: () => void;
  onNodesChange: OnNodesChange;
  onEdgesChange: OnEdgesChange;

  // ── Undo / Redo ──
  undo: () => void;
  redo: () => void;
  canUndo: () => boolean;
  canRedo: () => boolean;

  // ── Faz 3: Manuel düzenleme aksiyonları ──
  toggleEditMode: () => void;
  setSelectedTableForEdit: (tableId: string | null) => void;
  addTable: (x: number, y: number) => void;
  deleteTable: (tableId: string) => void;
  duplicateTable: (tableId: string) => void;
  updateTable: (updatedTable: SchemaTable) => void;
  /** Canvas'ta çizilen bağlantıdan FK ilişkisi kurar. Sonuç, çağırana geri bildirim için döner. */
  connectColumns: (connection: Connection) => { ok: boolean; reason: string };
  deleteRelation: (relationId: string) => void;
  importFromVision: (schema: DatabaseSchema) => void;
  /** Merges tables + relations from another schema into the current one (re-IDs everything). */
  mergeFromSchema: (incoming: DatabaseSchema) => void;
}

export const useSchemaStore = create<SchemaState>()(
  persist(
    (set, get) => ({
      // Ağır veri — persist edilmez
      schema: null,
      nodes: [],
      edges: [],
      _past: [],
      _future: [],

      // Hafif state — persist edilir
      isGenerating: false,
      naiModel: 'nai',
      projectName: 'Yeni Proje',
      dbType: 'MSSQL',
      promptHistory: [],

      // Faz 3
      isEditMode: false,
      selectedTableForEdit: null,

      // ── Geçmişe anlık görüntü ekle (her mutasyondan önce çağrılır) ─────────
      // set() çağrılmadan önce mevcut schema+nodes'u _past'a koy, _future'ı sıfırla.
      // Bu fonksiyon store'un dışından çağrılmaz; action'lar içinde kullanılır.

      // ── Temel actions ─────────────────────────────────────────────────────
      setIsGenerating: (isGenerating) => set({ isGenerating }),
      setNaiModel: (model) => set({ naiModel: model }),
      setProjectName: (name) => set({ projectName: name }),
      setDbType: (dbType) => set({ dbType }),

      addPromptToHistory: (prompt) => set((state) => {
        const trimmed = prompt.trim();
        if (!trimmed) return {};
        // Aynı prompt tekrar gönderilirse listenin başına taşınır, ikiletilmez.
        const deduped = state.promptHistory.filter(p => p !== trimmed);
        return { promptHistory: [trimmed, ...deduped].slice(0, 15) };
      }),

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

      // ── Undo / Redo ──────────────────────────────────────────────────────────
      canUndo: () => get()._past.length > 0,
      canRedo: () => get()._future.length > 0,

      undo: () => {
        const { schema, nodes, _past, _future } = get();
        if (_past.length === 0 || !schema) return;
        const prev = _past[_past.length - 1];
        const newPast = _past.slice(0, -1);
        const { edges } = schemaToFlow(prev.schema);
        set({
          schema: prev.schema,
          nodes: prev.nodes,
          edges,
          _past: newPast,
          _future: [{ schema, nodes }, ..._future].slice(0, HISTORY_LIMIT),
        });
      },

      redo: () => {
        const { schema, nodes, _past, _future } = get();
        if (_future.length === 0 || !schema) return;
        const next = _future[0];
        const newFuture = _future.slice(1);
        const { edges } = schemaToFlow(next.schema);
        set({
          schema: next.schema,
          nodes: next.nodes,
          edges,
          _past: [..._past, { schema, nodes }].slice(-HISTORY_LIMIT),
          _future: newFuture,
        });
      },

      loadFromSchema: (schema, nodePositions, preserveProjectName) => {
        const { nodes, edges } = schemaToFlow(schema);
        const restoredNodes = nodePositions
          ? nodes.map(n => nodePositions[n.id] ? { ...n, position: nodePositions[n.id] } : n)
          : nodes;
        
        const currentName = get().projectName || 'Yeni Proje';
        const isCustomName = currentName !== 'Yeni Proje' && currentName !== 'Shared Room Project' && currentName.trim() !== '';

        // We preserve the current name if preserveProjectName is true, OR if it's not explicitly false AND we already have a custom name set.
        const shouldPreserve = preserveProjectName === true || (preserveProjectName !== false && isCustomName);

        const newProjectName = shouldPreserve
          ? currentName
          : ((schema.name && schema.name !== 'Yeni Proje' && schema.name !== 'Shared Room Project' && schema.name.trim() !== '')
            ? schema.name
            : currentName);

        // Argümanı MUTATE ETME. Çağıranlar buraya kendilerine ait olmayan nesneler
        // geçiyor: BranchControlPanel `branch.schema`'yı (useProjectHistoryStore'un
        // kaydı) veriyor — mutasyon o store'un verisini set() dışından bozardı;
        // useMultiplayer ise ağdan gelen nesneyi veriyor.
        const normalizedSchema: DatabaseSchema = { ...schema, name: newProjectName };

        set({ schema: normalizedSchema, nodes: restoredNodes, edges, projectName: newProjectName });
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
        // history
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

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
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

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

      duplicateTable: (tableId) => {
        const state = get();
        if (!state.schema) return;
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

        const original = state.schema.tables.find(t => t.id === tableId);
        if (!original) return;

        const newTableId = genId();
        const colIdMap = new Map<string, string>();
        const newTable: SchemaTable = {
          ...original,
          id: newTableId,
          stableUuid: genId(),
          name: `${original.name}_copy`,
          columns: original.columns.map(c => {
            const newColId = genId();
            colIdMap.set(c.id, newColId);
            return { ...c, id: newColId, stableUuid: genId() };
          }),
        };

        const originalNode = state.nodes.find(n => n.id === tableId);
        const newNode = tableToNode(newTable, {
          x: (originalNode?.position.x ?? 0) + 320,
          y: (originalNode?.position.y ?? 0) + 40,
        });

        set({
          schema: { ...state.schema, tables: [...state.schema.tables, newTable] },
          nodes: [...state.nodes, newNode],
        });
      },

      /**
       * Drawer'dan gelen güncellenmiş tablo ile schema ve ilgili node'u günceller.
       * Diğer node'ların pozisyonları korunur.
       *
       * Tablodan kolon silindiyse o kolona bağlı ilişkiler de temizlenir: aksi hâlde
       * relation artık var olmayan bir kolonu işaret eder, edge çizilemez ama şemada
       * kalmaya devam eder ve derlemede olmayan bir kolona FOREIGN KEY üretilir.
       */
      updateTable: (updatedTable) => {
        const state = get();
        if (!state.schema) return;
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

        const newTables = state.schema.tables.map(t =>
          t.id === updatedTable.id ? updatedTable : t
        );

        const survivingColumnIds = new Set(updatedTable.columns.map(c => c.id));
        const touchesUpdatedTable = (tableId: string) => tableId === updatedTable.id;

        const newRelations = state.schema.relations.filter(r => {
          if (touchesUpdatedTable(r.sourceTableId) && !survivingColumnIds.has(r.sourceColumnId)) return false;
          if (touchesUpdatedTable(r.targetTableId) && !survivingColumnIds.has(r.targetColumnId)) return false;
          return true;
        });

        const newSchema: DatabaseSchema = { ...state.schema, tables: newTables, relations: newRelations };

        // Sadece ilgili node'u güncelle, pozisyonu koru
        const existingNode = state.nodes.find(n => n.id === updatedTable.id);
        const updatedNode: Node = {
          ...(existingNode ?? tableToNode(updatedTable, { x: 0, y: 0 })),
          data: { table: updatedTable },
        };

        const survivingRelationIds = new Set(newRelations.map(r => r.id));

        set({
          schema: newSchema,
          nodes: state.nodes.map(n => n.id === updatedTable.id ? updatedNode : n),
          edges: state.edges.filter(e => survivingRelationIds.has(e.id)),
        });
      },

      /**
       * Canvas'ta iki kolon handle'ı birleştirildiğinde yeni bir FK ilişkisi kurar.
       * React Flow bağlantı bırakıldığında onConnect'i çağırır; bu action bağlanmazsa
       * kullanıcı bağlantı çizgisini görür ama hiçbir şey olmaz.
       *
       * Geçersiz bağlantılar sessizce yutulmaz — çağıran tarafa sebep döner.
       */
      connectColumns: (connection) => {
        const state = get();
        if (!state.schema) return { ok: false, reason: 'Şema yüklü değil.' };
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

        const { source, target, sourceHandle, targetHandle } = connection;
        if (!source || !target || !sourceHandle || !targetHandle)
          return { ok: false, reason: 'Bağlantı için kaynak ve hedef kolonu seçin.' };

        if (source === target)
          return { ok: false, reason: 'Bir tabloyu kendisine bağlamak için ilişkiyi manuel düzenleyin.' };

        const sourceTable = state.schema.tables.find(t => t.id === source);
        const targetTable = state.schema.tables.find(t => t.id === target);
        if (!sourceTable || !targetTable)
          return { ok: false, reason: 'Tablo bulunamadı.' };

        // React Flow handle id'leri kolon id'sidir (schemaToFlow bu şekilde üretir).
        const sourceColumn = sourceTable.columns.find(c => c.id === sourceHandle);
        const targetColumn = targetTable.columns.find(c => c.id === targetHandle);
        if (!sourceColumn || !targetColumn)
          return { ok: false, reason: 'Kolon bulunamadı.' };

        // Hedef bir anahtar olmalı: FK yalnızca PK/UNIQUE bir kolonu işaret edebilir.
        if (!targetColumn.isPK)
          return { ok: false, reason: `İlişki birincil anahtara kurulmalı. '${targetTable.name}.${targetColumn.name}' bir PK değil.` };

        const alreadyExists = state.schema.relations.some(r =>
          r.sourceTableId === source && r.sourceColumnId === sourceHandle &&
          r.targetTableId === target && r.targetColumnId === targetHandle
        );
        if (alreadyExists)
          return { ok: false, reason: 'Bu ilişki zaten mevcut.' };

        const newRelation: SchemaRelation = {
          id: genId(),
          type: 'OneToMany',
          sourceTableId: source,
          sourceColumnId: sourceHandle,
          targetTableId: target,
          targetColumnId: targetHandle,
        };

        // Kaynak kolon artık bir yabancı anahtar — DDL üretimi bu bayrağa bakar.
        const newTables = state.schema.tables.map(t =>
          t.id !== source ? t : {
            ...t,
            columns: t.columns.map(c => c.id === sourceHandle ? { ...c, isFK: true } : c),
          }
        );

        const newSchema: DatabaseSchema = {
          ...state.schema,
          tables: newTables,
          relations: [...state.schema.relations, newRelation],
        };

        // Node/edge'leri şemadan yeniden türet, pozisyonları koru.
        const { nodes: regeneratedNodes, edges: newEdges } = schemaToFlow(newSchema);
        const finalNodes = regeneratedNodes.map(n => {
          const existing = state.nodes.find(en => en.id === n.id);
          return existing ? { ...n, position: existing.position } : n;
        });

        set({ schema: newSchema, nodes: finalNodes, edges: newEdges });
        return { ok: true, reason: `${sourceTable.name}.${sourceColumn.name} → ${targetTable.name}.${targetColumn.name} ilişkisi kuruldu.` };
      },

      /**
       * Bir ilişkiyi (ve edge'ini) siler; kaynak kolonun başka ilişkisi kalmadıysa
       * isFK bayrağını da kaldırır.
       */
      deleteRelation: (relationId) => {
        const state = get();
        if (!state.schema) return;
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

        const removed = state.schema.relations.find(r => r.id === relationId);
        if (!removed) return;

        const newRelations = state.schema.relations.filter(r => r.id !== relationId);

        const stillReferenced = newRelations.some(
          r => r.sourceTableId === removed.sourceTableId && r.sourceColumnId === removed.sourceColumnId
        );

        const newTables = stillReferenced
          ? state.schema.tables
          : state.schema.tables.map(t =>
              t.id !== removed.sourceTableId ? t : {
                ...t,
                columns: t.columns.map(c => c.id === removed.sourceColumnId ? { ...c, isFK: false } : c),
              }
            );

        set({
          schema: { ...state.schema, tables: newTables, relations: newRelations },
          nodes: state.nodes.map(n => {
            const table = newTables.find(t => t.id === n.id);
            return table ? { ...n, data: { table } } : n;
          }),
          edges: state.edges.filter(e => e.id !== relationId),
        });
      },

      importFromVision: (visionSchema) => {
        const state = get();
        const currentSchema = state.schema || { schemaId: genId(), name: 'Yeni Proje', tables: [], relations: [] };

        // Güvenli: AI/vision çıktısı dizi olmayabilir (obje/null) → forEach patlamasın.
        const rawTables = visionSchema.tables || (visionSchema as any).Tables;
        const rawRelations = visionSchema.relations || (visionSchema as any).Relations;
        const visionTables = Array.isArray(rawTables) ? rawTables : [];
        const visionRelations = Array.isArray(rawRelations) ? rawRelations : [];
        if (visionTables.length === 0) {
          throw new Error('İçe aktarılacak geçerli tablo bulunamadı. Görsel/şema net değilse tekrar deneyin.');
        }

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
            // Alanlar TEK TEK sayılmıyor: sayılan bir liste, modele eklenen her
            // yeni alanı (enum, identity, generated, collation, dizi) sessizce
            // düşürür ve bunu ancak kullanıcı verisinin kaybolduğunu görünce
            // fark edersin. Yayma (spread) bilmediğimiz alanları da taşır.
            columns: (t.columns || []).map(c => ({
              ...c,
              id: genId(),
              name: c.name || 'Column',
              type: c.type || 'INT',
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
        //
        // Kolon bağlama kuralları (görsel/AI çıktısı güvenilmez olduğu için):
        //  - Kolon çözülemezse ilişki ATLANIR. Eskiden `|| genId()` ile uydurma bir id
        //    üretiliyordu; bu id hiçbir kolona karşılık gelmediğinden edge çizilmiyor
        //    ama ilişki şemada kalıyor ve olmayan bir kolona FOREIGN KEY üretiliyordu.
        //  - Aynı kaynak kolon iki ilişkiye bağlanmaz: bir tabloda iki FK varsa
        //    (Orders.CustomerId, Orders.ProductId) ilk FK'yı seçmek her iki ilişkiyi de
        //    aynı kolona çökertiyordu. Bağlananlar takip edilir.
        const boundSourceColumns = new Set<string>();
        const skippedRelations: string[] = [];

        visionRelations.forEach((r: any) => {
          const mappedSourceTableId = idMap[r.sourceTableId] || r.sourceTableId;
          const mappedTargetTableId = idMap[r.targetTableId] || r.targetTableId;

          const sourceTable = updatedTables.find(t => t.id === mappedSourceTableId);
          const targetTable = updatedTables.find(t => t.id === mappedTargetTableId);
          if (!sourceTable || !targetTable) {
            skippedRelations.push(`${r.sourceTableId} → ${r.targetTableId} (tablo bulunamadı)`);
            return;
          }

          // Hedef: birincil anahtar. Yoksa ilişki kurulamaz.
          const targetColumn = targetTable.columns.find(c => c.isPK);
          if (!targetColumn) {
            skippedRelations.push(`${sourceTable.name} → ${targetTable.name} (hedefte birincil anahtar yok)`);
            return;
          }

          // Kaynak: önce isim yakınlığı (Orders.CustomerId → Customers), sonra
          // henüz bağlanmamış herhangi bir FK kolonu.
          const normalized = (s: string) => s.toLowerCase().replace(/[^a-z0-9]/g, '');
          const targetHint = normalized(targetTable.name).replace(/s$/, '');

          const available = sourceTable.columns.filter(
            c => c.isFK && !boundSourceColumns.has(c.id)
          );

          const sourceColumn =
            available.find(c => normalized(c.name).includes(targetHint)) ??
            available[0];

          if (!sourceColumn) {
            skippedRelations.push(`${sourceTable.name} → ${targetTable.name} (kaynakta uygun yabancı anahtar kolonu yok)`);
            return;
          }

          boundSourceColumns.add(sourceColumn.id);

          updatedRelations.push({
            id: genId(),
            type: r.type || 'OneToMany',
            sourceTableId: mappedSourceTableId,
            sourceColumnId: sourceColumn.id,
            targetTableId: mappedTargetTableId,
            targetColumnId: targetColumn.id,
          });
        });

        if (skippedRelations.length > 0) {
          console.warn(
            `[importFromVision] ${skippedRelations.length} ilişki kolonlara bağlanamadığı için atlandı:\n` +
            skippedRelations.join('\n')
          );
        }

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

      mergeFromSchema: (incoming) => {
        const state = get();
        if (!state.schema) return;
        set({ _past: [...state._past, { schema: state.schema, nodes: state.nodes }].slice(-HISTORY_LIMIT), _future: [] });

        // Re-ID all incoming tables and columns so they don't collide with existing ones
        const tableIdMap = new Map<string, string>();
        const colIdMap   = new Map<string, string>();

        const newTables: SchemaTable[] = incoming.tables.map(t => {
          const newTableId = genId();
          tableIdMap.set(t.id, newTableId);
          const newCols = t.columns.map(c => {
            const newColId = genId();
            colIdMap.set(c.id, newColId);
            return { ...c, id: newColId, stableUuid: genId() };
          });
          return { ...t, id: newTableId, stableUuid: genId(), columns: newCols };
        });

        const newRelations = incoming.relations
          .map(r => {
            const srcTableId = tableIdMap.get(r.sourceTableId);
            const tgtTableId = tableIdMap.get(r.targetTableId);
            const srcColId   = colIdMap.get(r.sourceColumnId);
            const tgtColId   = colIdMap.get(r.targetColumnId);
            if (!srcTableId || !tgtTableId || !srcColId || !tgtColId) return null;
            return { ...r, id: genId(), sourceTableId: srcTableId, targetTableId: tgtTableId, sourceColumnId: srcColId, targetColumnId: tgtColId };
          })
          .filter((r): r is SchemaRelation => r !== null);

        // Offset new nodes so they don't overlap existing ones
        const xOffset = state.nodes.length > 0
          ? Math.max(...state.nodes.map(n => (n.position.x) + 360)) + 60
          : 100;

        const newNodes = newTables.map((t, i) => tableToNode(t, {
          x: xOffset + (i % 3) * 360,
          y: 100 + Math.floor(i / 3) * 280,
        }));

        const mergedSchema: DatabaseSchema = {
          ...state.schema,
          tables: [...state.schema.tables, ...newTables],
          relations: [...state.schema.relations, ...newRelations],
        };
        const { edges } = schemaToFlow(mergedSchema);
        set({ schema: mergedSchema, nodes: [...state.nodes, ...newNodes], edges });
      },

    }),
    {
      name: 'namines-ui-state',
      storage: createJSONStorage(() => localforageStorage),
      partialize: (state) => ({
        // YALNIZCA UI tercihleri persist edilir.
        // schema / nodes / edges çifte yazımı önlemek için ÇIKARILDI.
        // Bu verinin tek otoriser kaynağı: useProjectHistoryStore (namines-projects key'i).
        // Sayfa yenilendiğinde schema, activeProjectId üzerinden loadProject() ile restore edilir.
        projectName: state.projectName,
        dbType: state.dbType,
        naiModel: state.naiModel,
        promptHistory: state.promptHistory,
      }),
    }
  )
);
