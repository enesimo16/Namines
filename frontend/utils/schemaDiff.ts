import { DatabaseSchema, SchemaTable, SchemaColumn } from '../types/schema';

export interface ColumnDiffDetails {
  nameChanged?: boolean;
  typeChanged?: boolean;
  pkChanged?: boolean;
  fkChanged?: boolean;
  nullableChanged?: boolean;
  defaultValueChanged?: boolean;
  oldColumn?: SchemaColumn;
  newColumn?: SchemaColumn;
}

export interface ColumnDiff {
  status: 'added' | 'deleted' | 'modified' | 'unchanged';
  details?: ColumnDiffDetails;
}

export interface TableDiff {
  status: 'added' | 'deleted' | 'modified' | 'unchanged';
  nameChanged?: boolean;
  oldName?: string;
  newName?: string;
  columns: Record<string, ColumnDiff>; // Key is column.id (in activeSchema or compareSchema)
}

export interface SchemaDiffResult {
  tables: Record<string, TableDiff>; // Key is table.id (in activeSchema or compareSchema)
  hasChanges: boolean;
}

/**
 * İki şemayı karşılaştırarak farkları döndürür.
 * activeSchema: Üzerinde çalışılan aktif dal şeması
 * compareSchema: Karşılaştırılan dal şeması (örn: main)
 */
export function calculateSchemaDiff(
  activeSchema: DatabaseSchema | null,
  compareSchema: DatabaseSchema | null
): SchemaDiffResult {
  const result: Record<string, TableDiff> = {};
  let hasChanges = false;

  if (!activeSchema || !compareSchema) {
    return { tables: {}, hasChanges: false };
  }

  const activeTables = activeSchema.tables || [];
  const compareTables = compareSchema.tables || [];

  // Helper to find table in active schema
  const findActiveTable = (compareTable: SchemaTable) => {
    return activeTables.find(t => 
      (t.stableUuid && compareTable.stableUuid && t.stableUuid === compareTable.stableUuid) ||
      (t.name === compareTable.name) ||
      (t.id === compareTable.id)
    );
  };

  // Helper to find table in compare schema
  const findCompareTable = (activeTable: SchemaTable) => {
    return compareTables.find(t => 
      (t.stableUuid && activeTable.stableUuid && t.stableUuid === activeTable.stableUuid) ||
      (t.name === activeTable.name) ||
      (t.id === activeTable.id)
    );
  };

  // 1. Eklenen ve Değiştirilen Tabloları Bul
  activeTables.forEach(activeTable => {
    const compareTable = findCompareTable(activeTable);

    if (!compareTable) {
      // Tablo activeSchema'da var ama compareSchema'da yok -> EKLEMDİ
      hasChanges = true;
      const columnsDiff: Record<string, ColumnDiff> = {};
      activeTable.columns.forEach(c => {
        columnsDiff[c.id] = { status: 'added' };
      });

      result[activeTable.id] = {
        status: 'added',
        columns: columnsDiff,
      };
    } else {
      // Tablo her ikisinde de var -> DEĞİŞMİŞ OLABİLİR VEYA DEĞİŞMEMİŞ OLABİLİR
      const columnDiffs: Record<string, ColumnDiff> = {};
      let isTableModified = false;
      const nameChanged = activeTable.name !== compareTable.name;

      if (nameChanged) {
        isTableModified = true;
      }

      // Kolonları karşılaştır
      const findCompareColumn = (activeCol: SchemaColumn) => {
        return compareTable.columns.find(c =>
          (c.stableUuid && activeCol.stableUuid && c.stableUuid === activeCol.stableUuid) ||
          (c.name === activeCol.name) ||
          (c.id === activeCol.id)
        );
      };

      const findActiveColumn = (compareCol: SchemaColumn) => {
        return activeTable.columns.find(c =>
          (c.stableUuid && compareCol.stableUuid && c.stableUuid === compareCol.stableUuid) ||
          (c.name === compareCol.name) ||
          (c.id === compareCol.id)
        );
      };

      // Aktif koldaki kolonları tara (Eklenen & Değişen)
      activeTable.columns.forEach(activeCol => {
        const compareCol = findCompareColumn(activeCol);

        if (!compareCol) {
          columnDiffs[activeCol.id] = { status: 'added' };
          isTableModified = true;
        } else {
          // Değişiklik kontrolü
          const nameChanged = activeCol.name !== compareCol.name;
          const typeChanged = activeCol.type !== compareCol.type || activeCol.length !== compareCol.length;
          const pkChanged = activeCol.isPK !== compareCol.isPK;
          const fkChanged = activeCol.isFK !== compareCol.isFK;
          const nullableChanged = activeCol.isNullable !== compareCol.isNullable;
          const defaultValueChanged = activeCol.defaultValue !== compareCol.defaultValue;

          if (nameChanged || typeChanged || pkChanged || fkChanged || nullableChanged || defaultValueChanged) {
            columnDiffs[activeCol.id] = {
              status: 'modified',
              details: {
                nameChanged,
                typeChanged,
                pkChanged,
                fkChanged,
                nullableChanged,
                defaultValueChanged,
                oldColumn: compareCol,
                newColumn: activeCol,
              }
            };
            isTableModified = true;
          } else {
            columnDiffs[activeCol.id] = { status: 'unchanged' };
          }
        }
      });

      // Kıyas koldaki kolonları tara (Silinen)
      compareTable.columns.forEach(compareCol => {
        const activeCol = findActiveColumn(compareCol);
        if (!activeCol) {
          columnDiffs[compareCol.id] = {
            status: 'deleted',
            details: {
              oldColumn: compareCol
            }
          };
          isTableModified = true;
        }
      });

      if (isTableModified) {
        hasChanges = true;
      }

      result[activeTable.id] = {
        status: isTableModified ? 'modified' : 'unchanged',
        nameChanged,
        oldName: nameChanged ? compareTable.name : undefined,
        newName: nameChanged ? activeTable.name : undefined,
        columns: columnDiffs,
      };
    }
  });

  // 2. Silinen Tabloları Bul
  compareTables.forEach(compareTable => {
    const activeTable = findActiveTable(compareTable);

    if (!activeTable) {
      // Tablo compareSchema'da var ama activeSchema'da yok -> SİLİNDİ
      hasChanges = true;
      const columnsDiff: Record<string, ColumnDiff> = {};
      compareTable.columns.forEach(c => {
        columnsDiff[c.id] = {
          status: 'deleted',
          details: { oldColumn: c }
        };
      });

      result[compareTable.id] = {
        status: 'deleted',
        columns: columnsDiff,
      };
    }
  });

  // 3. Find relationship changes
  const activeRelations = activeSchema.relations || [];
  const compareRelations = compareSchema.relations || [];

  const areRelationsEqual = (r1: any, r2: any) => {
    if (r1.type !== r2.type) return false;
    
    const getTableColName = (sch: any, tableId: string, colId: string) => {
      let table = sch.tables?.find((t: any) => t.id === tableId);
      if (!table) {
        table = sch.tables?.find((t: any) => t.name?.toLowerCase() === tableId.toLowerCase());
      }
      const tableName = table ? table.name : tableId;

      let col = table?.columns?.find((c: any) => c.id === colId);
      if (!col && table) {
        col = table.columns?.find((c: any) => c.name?.toLowerCase() === colId.toLowerCase());
      }
      const colName = col ? col.name : colId;

      return `${tableName || ''}.${colName || ''}`;
    };

    const sourceName1 = getTableColName(activeSchema, r1.sourceTableId, r1.sourceColumnId);
    const targetName1 = getTableColName(activeSchema, r1.targetTableId, r1.targetColumnId);
    const sourceName2 = getTableColName(compareSchema, r2.sourceTableId, r2.sourceColumnId);
    const targetName2 = getTableColName(compareSchema, r2.targetTableId, r2.targetColumnId);

    return sourceName1 === sourceName2 && targetName1 === targetName2;
  };

  const addedRels = activeRelations.filter(ar => 
    !compareRelations.some(cr => areRelationsEqual(ar, cr))
  );

  const removedRels = compareRelations.filter(cr => 
    !activeRelations.some(ar => areRelationsEqual(ar, cr))
  );

  if (addedRels.length > 0 || removedRels.length > 0) {
    hasChanges = true;
  }

  return {
    tables: result,
    hasChanges,
  };
}
