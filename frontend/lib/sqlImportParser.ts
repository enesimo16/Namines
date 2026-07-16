import { DatabaseSchema, SchemaTable, SchemaColumn, SchemaRelation } from '../types/schema';

/**
 * SQL DDL → DatabaseSchema
 *
 * Desteklenen sözdizimi:
 *   CREATE TABLE name (
 *     col_name TYPE [NOT NULL] [PRIMARY KEY],
 *     ...,
 *     PRIMARY KEY (col, ...),
 *     FOREIGN KEY (col) REFERENCES other_table(other_col)
 *   );
 *
 * Sınırlamalar (kasıtlı olarak basit tutuldu):
 *   - Şema niteleyicisi yoksayılır (schema.table → table)
 *   - CONSTRAINT isim ifadeleri atlanır
 *   - UNIQUE/INDEX/CHECK kısıtları yoksayılır
 *   - CHECK ifadeleri içindeki virgüller pasörü karıştırabileceğinden
 *     bu ifadeler pre-processing'de kaldırılır
 */

function genId() {
  return typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);
}

const TYPE_ALIASES: Record<string, string> = {
  'int': 'INT', 'integer': 'INT', 'int4': 'INT', 'serial': 'INT',
  'bigint': 'BIGINT', 'int8': 'BIGINT', 'bigserial': 'BIGINT',
  'smallint': 'SMALLINT', 'int2': 'SMALLINT',
  'tinyint': 'TINYINT',
  'float': 'FLOAT', 'float4': 'FLOAT', 'float8': 'FLOAT', 'double precision': 'FLOAT', 'real': 'FLOAT',
  'numeric': 'DECIMAL', 'decimal': 'DECIMAL', 'money': 'DECIMAL',
  'boolean': 'BOOLEAN', 'bool': 'BOOLEAN', 'bit': 'BOOLEAN',
  'char': 'CHAR', 'bpchar': 'CHAR',
  'varchar': 'VARCHAR', 'varchar2': 'VARCHAR', 'nvarchar': 'VARCHAR', 'character varying': 'VARCHAR', 'nchar': 'VARCHAR',
  'text': 'TEXT', 'clob': 'TEXT', 'ntext': 'TEXT', 'mediumtext': 'TEXT', 'longtext': 'TEXT',
  'date': 'DATE',
  'time': 'TIME', 'timetz': 'TIME',
  'timestamp': 'TIMESTAMP', 'timestamptz': 'TIMESTAMP', 'datetime': 'TIMESTAMP',
  'datetime2': 'TIMESTAMP', 'smalldatetime': 'TIMESTAMP',
  'json': 'JSON', 'jsonb': 'JSON',
  'uuid': 'UUID', 'uniqueidentifier': 'UUID',
  'blob': 'BLOB', 'bytea': 'BLOB', 'varbinary': 'BLOB', 'binary': 'BLOB', 'image': 'BLOB',
};

function normalizeType(raw: string): string {
  const base = raw.toLowerCase().replace(/\s*\(.*?\)/g, '').trim();
  return TYPE_ALIASES[base] ?? raw.toUpperCase().replace(/\s*\(.*?\)/, '');
}

function stripName(raw: string): string {
  return raw.trim().replace(/^["'`\[]|["'`\]]$/g, '');
}

export function parseSqlDdl(sql: string): DatabaseSchema {
  // ── Pre-process ────────────────────────────────────────────────────────────
  // Remove single-line comments
  let src = sql.replace(/--[^\n]*/g, '');
  // Remove block comments
  src = src.replace(/\/\*[\s\S]*?\*\//g, '');
  // Remove CHECK constraints (they may contain nested parentheses with commas)
  src = src.replace(/\bCHECK\s*\([^)]*\)/gi, '');

  const tables = new Map<string, SchemaTable>();
  const relations: SchemaRelation[] = [];
  const tableNameToId = new Map<string, string>();

  // ── Extract CREATE TABLE blocks ────────────────────────────────────────────
  const createTableRe = /CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?([^\s(]+)\s*\(([\s\S]*?)\)\s*;/gi;
  let match: RegExpExecArray | null;

  while ((match = createTableRe.exec(src)) !== null) {
    const rawTableName = stripName(match[1].split('.').pop()!);
    const body = match[2];

    const tableId = genId();
    const columns: SchemaColumn[] = [];
    let inlinePks: string[] = [];

    tableNameToId.set(rawTableName.toLowerCase(), tableId);

    // Split on commas NOT inside parentheses
    const defs = splitByComma(body);

    for (const def of defs) {
      const d = def.trim();
      if (!d) continue;

      const upper = d.toUpperCase();

      // ── Table-level PRIMARY KEY ────────────────────────────────────────────
      if (/^(?:CONSTRAINT\s+\S+\s+)?PRIMARY\s+KEY\s*\(/i.test(d)) {
        const cols = extractParenList(d);
        inlinePks = cols.map(c => c.toLowerCase());
        continue;
      }

      // ── Table-level FOREIGN KEY ────────────────────────────────────────────
      if (/^(?:CONSTRAINT\s+\S+\s+)?FOREIGN\s+KEY\s*\(/i.test(d)) {
        const srcCols = extractParenList(d);
        const refMatch = d.match(/REFERENCES\s+([^\s(]+)\s*\(([^)]+)\)/i);
        if (srcCols.length && refMatch) {
          const targetTableName = stripName(refMatch[1].split('.').pop()!);
          const targetColName   = stripName(refMatch[2].trim());
          relations.push({
            id: genId(), type: 'ManyToOne',
            sourceTableId: tableId,
            sourceColumnId: srcCols[0].toLowerCase() + '::pending::' + rawTableName,
            targetTableId: targetTableName.toLowerCase() + '::pending',
            targetColumnId: targetColName.toLowerCase() + '::pending::' + targetTableName,
          });
        }
        continue;
      }

      // ── Skip pure constraint lines ─────────────────────────────────────────
      if (/^(?:CONSTRAINT|UNIQUE|INDEX|KEY)\s/i.test(d) && !/^\w/.test(d.replace(/^(?:CONSTRAINT|UNIQUE|INDEX|KEY)\s+\S+\s*/i, ''))) {
        continue;
      }

      // ── Column definition ──────────────────────────────────────────────────
      // Format: col_name TYPE[(len)] [NULL|NOT NULL] [PRIMARY KEY] [DEFAULT ...]
      const colMatch = d.match(/^([^\s]+)\s+([^\s]+(?:\s*\([^)]*\))?)(.*)/i);
      if (!colMatch) continue;

      const colName = stripName(colMatch[1]);
      if (/^(PRIMARY|FOREIGN|UNIQUE|INDEX|KEY|CONSTRAINT|CHECK)$/i.test(colName)) continue;

      const typeRaw  = colMatch[2];
      const rest     = colMatch[3] ?? '';
      const isNotNull = /NOT\s+NULL/i.test(rest);
      const isPkInline = /PRIMARY\s+KEY/i.test(rest);

      const colId = genId();
      columns.push({
        id: colId, stableUuid: colId,
        name: colName,
        type: normalizeType(typeRaw),
        isPK: isPkInline,
        isFK: false,
        isNullable: !isNotNull && !isPkInline,
        length: null,
        defaultValue: null,
      });
    }

    // Apply table-level PKs
    for (const col of columns) {
      if (inlinePks.includes(col.name.toLowerCase())) col.isPK = true;
    }

    tables.set(tableId, { id: tableId, stableUuid: tableId, name: rawTableName, columns });
  }

  // ── Resolve pending relation references ───────────────────────────────────
  const columnLookup = new Map<string, string>(); // "tableName::colName" → colId
  for (const [, table] of tables) {
    for (const col of table.columns) {
      columnLookup.set(`${table.name.toLowerCase()}::${col.name.toLowerCase()}`, col.id);
    }
  }

  const resolvedRelations: SchemaRelation[] = [];
  for (const r of relations) {
    const [srcColName,, srcTableName] = r.sourceColumnId.split('::');
    const [tgtTableName] = r.targetTableId.split('::');
    const [tgtColName,, tgtTableNameB] = r.targetColumnId.split('::');

    const srcTableId = tableNameToId.get(srcTableName.toLowerCase());
    const tgtTableId = tableNameToId.get(tgtTableName.toLowerCase());
    const srcColId   = columnLookup.get(`${srcTableName.toLowerCase()}::${srcColName.toLowerCase()}`);
    const tgtColId   = columnLookup.get(`${(tgtTableNameB ?? tgtTableName).toLowerCase()}::${tgtColName.toLowerCase()}`);

    if (!srcTableId || !tgtTableId || !srcColId || !tgtColId) continue;

    // Mark source column as FK
    const srcTable = tables.get(srcTableId);
    if (srcTable) {
      const col = srcTable.columns.find(c => c.id === srcColId);
      if (col) col.isFK = true;
    }

    resolvedRelations.push({
      id: r.id, type: r.type,
      sourceTableId: srcTableId, sourceColumnId: srcColId,
      targetTableId: tgtTableId, targetColumnId: tgtColId,
    });
  }

  return {
    schemaId: genId(),
    name: 'Imported Schema',
    tables: Array.from(tables.values()),
    relations: resolvedRelations,
  };
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function splitByComma(s: string): string[] {
  const parts: string[] = [];
  let depth = 0, start = 0;
  for (let i = 0; i < s.length; i++) {
    if (s[i] === '(') depth++;
    else if (s[i] === ')') depth--;
    else if (s[i] === ',' && depth === 0) {
      parts.push(s.slice(start, i));
      start = i + 1;
    }
  }
  parts.push(s.slice(start));
  return parts;
}

function extractParenList(s: string): string[] {
  const m = s.match(/\(([^)]+)\)/);
  if (!m) return [];
  return m[1].split(',').map(c => stripName(c));
}
