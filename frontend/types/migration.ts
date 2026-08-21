import { SchemaRelation } from './schema';

export interface TableDiffDetail {
  tableName: string;
  addedColumns: string[];
  removedColumns: string[];
  modifiedColumns: string[];
}

// Backend: Namines.Core/Enums/RiskLevel.cs — sıralama önemli, en şiddetlisi "Breaking".
export type RiskLevel = 'Safe' | 'Risky' | 'Destructive' | 'Breaking';

// Backend: Namines.Core/Enums/LockSeverity.cs
export type LockSeverity = 'None' | 'Brief' | 'Blocking';

// Backend: Namines.Core/Models/ImpactReport.cs
export type ChangeKind = 'Added' | 'Removed' | 'Modified' | 'RenamedFrom';

export type BreakingChangeKind =
  | 'ColumnRemoved'
  | 'ColumnRenamed'
  | 'TableRemoved'
  | 'TableRenamed'
  | 'TypeNarrowed'
  | 'NotNullWithoutDefault'
  | 'MultipleCascadePaths'
  | 'CascadeCycle'
  | 'InvalidSetNullTarget'
  | 'InvalidSetDefaultTarget';

export interface AffectedTable {
  tableName: string;
  kind: ChangeKind;
  changedColumns: string[];
  previousName: string | null;
}

export interface AffectedRelation {
  relationId: string | null;
  kind: ChangeKind;
  fromTable: string;
  toTable: string;
}

export interface AffectedIndex {
  tableName: string;
  indexName: string | null;
  kind: ChangeKind;
}

export interface BreakingChange {
  description: string;
  kind: BreakingChangeKind;
  tableName: string | null;
  columnName: string | null;
  suggestedMitigation: string | null;
}

export interface DataLossRisk {
  tableName: string;
  columnName: string | null;
  reason: string;
}

export interface MigrationLockRisk {
  operation: string;
  severity: LockSeverity;
  tableName: string | null;
  saferAlternative: string | null;
}

export interface MissingIndexSuggestion {
  tableName: string;
  columnName: string;
  reason: string;
}

export interface RollbackAssessment {
  isReversible: boolean;
  reason: string | null;
}

/** Backend: Namines.Core/Models/ImpactReport.cs — SchemaImpactAnalyzer'ın deterministik çıktısı. */
export interface ImpactReport {
  affectedTables: AffectedTable[];
  affectedRelations: AffectedRelation[];
  affectedIndexes: AffectedIndex[];
  breakingChanges: BreakingChange[];
  dataLossRisks: DataLossRisk[];
  lockRisks: MigrationLockRisk[];
  indexSuggestions: MissingIndexSuggestion[];
  rollback: RollbackAssessment;
  overallRisk: RiskLevel;
}

export interface SchemaDiffResult {
  addedTables: string[];
  removedTables: string[];
  modifiedTables: TableDiffDetail[];
  addedRelations: SchemaRelation[];
  removedRelations: SchemaRelation[];
  /** Impact.breakingChanges.length > 0 türetilir — bkz. SchemaImpactAnalyzer. */
  hasBreakingChanges: boolean;
  overallRisk: RiskLevel;
  impact: ImpactReport | null;
}

export interface MigrationResult {
  upCode: string;
  downCode: string;
  summary: string;
  warnings: string[];
}
