import { SchemaRelation } from './schema';

export interface TableDiffDetail {
  tableName: string;
  addedColumns: string[];
  removedColumns: string[];
  modifiedColumns: string[];
}

export interface SchemaDiffResult {
  addedTables: string[];
  removedTables: string[];
  modifiedTables: TableDiffDetail[];
  addedRelations: SchemaRelation[];
  removedRelations: SchemaRelation[];
  hasBreakingChanges: boolean;
}

export interface MigrationResult {
  upCode: string;
  downCode: string;
  summary: string;
  warnings: string[];
}
