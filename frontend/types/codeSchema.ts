import { DatabaseSchema } from './schema';

/** Atlanan bir model/alan ve nedeni — second-phase/11-KODDAN-SEMA.md. */
export interface SkippedItem {
  name: string;
  reason: string;
}

export interface CodeDriftReport {
  hasDrift: boolean;
  overallRisk: string;
  affectedTables: { tableName: string; kind: string; changedColumns: string[] }[];
  breakingChanges: { tableName: string | null; columnName: string | null; description: string; kind: string }[];
}

/**
 * `POST /api/codeschema/extract` yanıtı.
 *
 * `parsedCount` ve `skippedCount` birlikte gösterilmeli — doc'un açık kuralı:
 * "12 modelin 9'u okundu, 3'ü anlaşılamadı". Yalnızca birini göstermek,
 * olmayan bir tam resim sunar.
 */
export interface CodeExtractionResponse {
  format: 'prisma' | 'efcore' | 'sql';
  schema: DatabaseSchema;
  parsedCount: number;
  skippedCount: number;
  parsedModels: string[];
  skipped: SkippedItem[];
  drift: CodeDriftReport | null;
}
