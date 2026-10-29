'use client';

import React, { useCallback, useId, useMemo, useState, useRef, useEffect } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  Table2,
  Database,
  Terminal,
  Cable,
  Plus,
  Search,
  Trash2,
  Pencil,
  Copy,
  Download,
  RefreshCw,
  Play,
  KeyRound,
  Check,
  CheckCircle2,
  ArrowRight,
  ArrowUpDown,
  ArrowUp,
  ArrowDown,
  ChevronDown,
  ChevronsLeft,
  ChevronsRight,
  Layers,
  ShieldCheck,
  HardDrive,
  ExternalLink,
  FileSpreadsheet,
  FileJson,
  Sparkles,
  Info,
  X,
  Code2,
} from 'lucide-react';
import type { DatabaseSchema, SchemaColumn, SchemaTable } from '../../types/schema';
import type { CloudProjectDto } from '../../types/api';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { ModuleSymbol, ModuleWordmark } from '../layout/ModuleLogo';
import {
  PageShell,
  Card,
  Callout,
  Badge,
  Button,
  EmptyState,
  StatTile,
  Tabs,
  DataTable,
  Td,
  inputClass,
  Field,
} from '../workspace/WorkspaceKit';

export interface ProjectDeskStudioProps {
  projectId: string;
  projectName: string;
  resolvedOwner: string;
  projectDto?: CloudProjectDto | null;
  onExitProject: () => void;
}

type StudioTab = 'records' | 'schema' | 'sql' | 'gateway';

/**
 * Deterministic, realistic sample row generator matching exact column types.
 */
function generateSampleDataForTable(table: SchemaTable, count: number = 6): Record<string, unknown>[] {
  const sampleFirstNames = ['Ada', 'Alan', 'Grace', 'Linus', 'Margaret', 'Guido', 'Brendan', 'Dennis', 'Ken', 'Tim'];
  const sampleLastNames = ['Lovelace', 'Turing', 'Hopper', 'Torvalds', 'Hamilton', 'van Rossum', 'Eich', 'Ritchie', 'Thompson', 'Berners-Lee'];
  const sampleDomains = ['namines.io', 'acme.org', 'cloudscale.net', 'databox.io'];
  const sampleCities = ['Istanbul', 'San Francisco', 'London', 'Berlin', 'Tokyo', 'Zurich', 'Toronto', 'Singapore'];
  const sampleStatuses = ['Active', 'Pending', 'Verified', 'Archived'];
  const sampleRoles = ['Owner', 'Administrator', 'Maintainer', 'Viewer'];
  const sampleTitles = ['Senior Backend Architect', 'Staff Database Engineer', 'Product Designer', 'DevOps Lead', 'Fullstack Engineer'];

  return Array.from({ length: count }, (_, idx) => {
    const row: Record<string, unknown> = {};
    const fname = sampleFirstNames[idx % sampleFirstNames.length];
    const lname = sampleLastNames[idx % sampleLastNames.length];
    const domain = sampleDomains[idx % sampleDomains.length];

    table.columns.forEach((col) => {
      const colName = col.name.toLowerCase();
      const colType = col.type.toLowerCase();

      if (col.isPK) {
        if (colType.includes('int') || colType.includes('serial')) {
          row[col.name] = idx + 1;
        } else {
          row[col.name] = `pk-${idx + 1}-${Math.random().toString(36).slice(2, 8)}`;
        }
      } else if (colName.includes('email')) {
        row[col.name] = `${fname.toLowerCase()}.${lname.toLowerCase()}${idx + 1}@${domain}`;
      } else if (colName.includes('first_name') || colName === 'firstname' || colName === 'first') {
        row[col.name] = fname;
      } else if (colName.includes('last_name') || colName === 'lastname' || colName === 'last') {
        row[col.name] = lname;
      } else if (colName.includes('username') || colName === 'user_name') {
        row[col.name] = `${fname.toLowerCase()}_${lname.toLowerCase()}${idx + 1}`;
      } else if (colName.includes('name') || colName === 'title') {
        if (table.name.toLowerCase().includes('user') || table.name.toLowerCase().includes('member')) {
          row[col.name] = `${fname} ${lname}`;
        } else {
          row[col.name] = `${table.name.slice(0, 4).toUpperCase()} Item #${idx + 1}`;
        }
      } else if (colName.includes('status')) {
        row[col.name] = sampleStatuses[idx % sampleStatuses.length];
      } else if (colName.includes('role')) {
        row[col.name] = sampleRoles[idx % sampleRoles.length];
      } else if (colName.includes('city')) {
        row[col.name] = sampleCities[idx % sampleCities.length];
      } else if (colName.includes('position') || colName.includes('job')) {
        row[col.name] = sampleTitles[idx % sampleTitles.length];
      } else if (colName.includes('phone')) {
        row[col.name] = `+1 (555) 019-${1000 + idx}`;
      } else if (colType.includes('bool')) {
        row[col.name] = idx % 2 === 0;
      } else if (colType.includes('int') || colType.includes('serial') || colType.includes('count')) {
        row[col.name] = (idx + 1) * 12;
      } else if (
        colType.includes('float') ||
        colType.includes('decimal') ||
        colType.includes('numeric') ||
        colType.includes('price') ||
        colType.includes('amount') ||
        colType.includes('balance')
      ) {
        row[col.name] = Number(((idx + 1) * 34.5).toFixed(2));
      } else if (colType.includes('date') || colType.includes('time')) {
        const d = new Date(Date.now() - idx * 86400000);
        row[col.name] = d.toISOString().slice(0, 19).replace('T', ' ');
      } else if (colType.includes('json')) {
        row[col.name] = JSON.stringify({ verified: true, level: idx + 1, mode: 'auto' });
      } else {
        row[col.name] = `${col.name}_val_${idx + 1}`;
      }
    });

    return row;
  });
}

function exportToCsv(tableName: string, columns: SchemaColumn[], rows: Record<string, unknown>[]) {
  const headers = columns.map((c) => `"${c.name}"`).join(',');
  const body = rows
    .map((row) =>
      columns
        .map((c) => {
          const val = row[c.name];
          if (val === null || val === undefined) return '""';
          // =, +, -, @ ile başlayan hücre Excel/Sheets'te formül olarak çalışır: başa ' koyarak etkisizleştir
          const text = String(val);
          const safe = /^[=+\-@\t\r]/.test(text) ? `'${text}` : text;
          return `"${safe.replace(/"/g, '""')}"`;
        })
        .join(','),
    )
    .join('\n');
  const csvContent = `${headers}\n${body}`;
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', `${tableName}_records_${Date.now()}.csv`);
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

function exportToJson(tableName: string, rows: Record<string, unknown>[]) {
  const jsonStr = JSON.stringify(rows, null, 2);
  const blob = new Blob([jsonStr], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.setAttribute('download', `${tableName}_records_${Date.now()}.json`);
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(url);
}

function generateTableDdl(table: SchemaTable, dbEngine: string): string {
  const isPostgres = dbEngine.toLowerCase().includes('postgres');
  const isSqlite = dbEngine.toLowerCase().includes('sqlite');

  const lines: string[] = [];
  lines.push(`-- Table: ${table.name}`);
  lines.push(`-- Target Engine: ${dbEngine}`);
  lines.push(`CREATE TABLE "${table.name}" (`);

  const colDefs = table.columns.map((col) => {
    let def = `  "${col.name}" ${col.type.toUpperCase()}`;
    if (col.length) def += `(${col.length})`;
    if (col.isPK) def += ' PRIMARY KEY';
    if (!col.isNullable && !col.isPK) def += ' NOT NULL';
    if (col.defaultValue) def += ` DEFAULT ${col.defaultValue}`;
    if (col.identity) {
      def += isPostgres ? ' GENERATED ALWAYS AS IDENTITY' : isSqlite ? ' AUTOINCREMENT' : ' AUTO_INCREMENT';
    }
    return def;
  });

  lines.push(colDefs.join(',\n'));
  lines.push(');');

  if (table.indexes && table.indexes.length > 0) {
    lines.push('');
    lines.push('-- Indexes');
    table.indexes.forEach((idx) => {
      const idxName = idx.name || `ix_${table.name}_${idx.id.slice(0, 6)}`;
      const colNames = idx.columns
        .map((c) => {
          const found = table.columns.find((col) => col.id === c.columnId);
          return found ? `"${found.name}"${c.descending ? ' DESC' : ''}` : `"${c.columnId}"`;
        })
        .join(', ');
      lines.push(`CREATE ${idx.isUnique ? 'UNIQUE ' : ''}INDEX "${idxName}" ON "${table.name}" (${colNames});`);
    });
  }

  return lines.join('\n');
}

export function ProjectDeskStudio({
  projectId,
  projectName,
  resolvedOwner,
  projectDto,
  onExitProject,
}: ProjectDeskStudioProps) {
  const router = useRouter();

  // 1. Resolve Schema
  const schema: DatabaseSchema = useMemo(() => {
    if (projectDto?.schemaJson) {
      try {
        const parsed = JSON.parse(projectDto.schemaJson);
        if (parsed && Array.isArray(parsed.tables)) return parsed;
      } catch {
        // fallback
      }
    }
    const localSnap = useProjectHistoryStore.getState().projects.find((p) => p.id === projectId);
    if (localSnap?.schema?.tables?.length) return localSnap.schema;

    // Canvas'taki canlı şema yalnızca ETKİN proje bu proje ise geçerli; aksi hâlde başka projenin tabloları görünürdü
    const liveSchema = useSchemaStore.getState().schema;
    if (
      liveSchema?.tables?.length &&
      useProjectHistoryStore.getState().activeProjectId === projectId
    ) {
      return liveSchema;
    }

    // Proje şeması hiç yoksa: açıkça örnek şema (arayüzde "sample" olarak etiketleniyor)
    return {
      schemaId: projectId,
      name: projectName,
      tables: [
        {
          id: 'tbl-users',
          name: 'users',
          columns: [
            { id: 'c1', name: 'id', type: 'uuid', length: null, isPK: true, isFK: false, isNullable: false, defaultValue: 'gen_random_uuid()' },
            { id: 'c2', name: 'email', type: 'varchar', length: 255, isPK: false, isFK: false, isNullable: false, defaultValue: null },
            { id: 'c3', name: 'username', type: 'varchar', length: 64, isPK: false, isFK: false, isNullable: false, defaultValue: null },
            { id: 'c4', name: 'role', type: 'varchar', length: 32, isPK: false, isFK: false, isNullable: false, defaultValue: "'member'" },
            { id: 'c5', name: 'status', type: 'varchar', length: 32, isPK: false, isFK: false, isNullable: false, defaultValue: "'active'" },
            { id: 'c6', name: 'created_at', type: 'timestamp', length: null, isPK: false, isFK: false, isNullable: false, defaultValue: 'now()' },
          ],
        },
        {
          id: 'tbl-orders',
          name: 'orders',
          columns: [
            { id: 'o1', name: 'id', type: 'uuid', length: null, isPK: true, isFK: false, isNullable: false, defaultValue: 'gen_random_uuid()' },
            { id: 'o2', name: 'user_id', type: 'uuid', length: null, isPK: false, isFK: true, isNullable: false, defaultValue: null },
            { id: 'o3', name: 'amount', type: 'numeric', length: null, isPK: false, isFK: false, isNullable: false, defaultValue: '0.00' },
            { id: 'o4', name: 'status', type: 'varchar', length: 32, isPK: false, isFK: false, isNullable: false, defaultValue: "'pending'" },
            { id: 'o5', name: 'created_at', type: 'timestamp', length: null, isPK: false, isFK: false, isNullable: false, defaultValue: 'now()' },
          ],
        },
      ],
      relations: [
        {
          id: 'rel-1',
          type: 'OneToMany',
          sourceTableId: 'tbl-orders',
          sourceColumnId: 'o2',
          targetTableId: 'tbl-users',
          targetColumnId: 'c1',
          onDelete: 'NoAction',
        },
      ],
    };
  }, [projectDto, projectId, projectName]);

  const tables = schema.tables || [];
  const dbEngine = projectDto?.dbType || 'PostgreSQL 16';
  const hasLiveConnection = Boolean(projectDto?.hasConnection);

  // Active Tab
  const [activeTab, setActiveTab] = useState<StudioTab>('records');

  // Active Table
  const [activeTableId, setActiveTableId] = useState<string>(tables[0]?.id || '');
  const activeTable = tables.find((t) => t.id === activeTableId) || tables[0] || null;

  // Table Data Store (Record<tableId, rows[]>)
  // Anahtar proje + şema imzası: şema sonradan geldiğinde/değiştiğinde örnek veri yeniden üretilir
  // (eski kod yalnızca ilk render'daki tablolardan üretip bir daha dokunmuyordu).
  const schemaKey = useMemo(
    () =>
      `${projectId}|${tables
        .map((t) => `${t.id}:${t.columns.map((c) => `${c.name}/${c.type}`).join(',')}`)
        .join(';')}`,
    [projectId, tables],
  );
  const [dataState, setDataState] = useState<{ key: string; data: Record<string, Record<string, unknown>[]> }>({
    key: '',
    data: {},
  });
  const buildInitialData = useCallback(() => {
    const initial: Record<string, Record<string, unknown>[]> = {};
    tables.forEach((t) => {
      initial[t.id] = generateSampleDataForTable(t, 6);
    });
    return initial;
  }, [tables]);
  const dataStore = useMemo(
    () => (dataState.key === schemaKey ? dataState.data : buildInitialData()),
    [dataState, schemaKey, buildInitialData],
  );
  const setDataStore = useCallback(
    (
      update: (
        prev: Record<string, Record<string, unknown>[]>,
      ) => Record<string, Record<string, unknown>[]>,
    ) =>
      setDataState((prev) => ({
        key: schemaKey,
        data: update(prev.key === schemaKey ? prev.data : buildInitialData()),
      })),
    [schemaKey, buildInitialData],
  );

  // Table Search & Sort & Pagination
  const [searchQuery, setSearchQuery] = useState('');
  const [sortCol, setSortCol] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<'asc' | 'desc'>('asc');
  const [selectedPks, setSelectedPks] = useState<Set<string>>(new Set());
  const [pageSize, setPageSize] = useState<number>(25);
  const [currentPage, setCurrentPage] = useState<number>(1);

  // Edit / Insert Modal State
  const [editModalOpen, setEditModalOpen] = useState(false);
  const [editingRow, setEditingRow] = useState<Record<string, unknown> | null>(null);
  const [rowDraft, setRowDraft] = useState<Record<string, string>>({});

  // SQL Console State
  const [sqlQuery, setSqlQuery] = useState<string>(`SELECT * FROM "${activeTable?.name || 'users'}" LIMIT 25;`);
  const [sqlResult, setSqlResult] = useState<{
    columns: string[];
    rows: Record<string, unknown>[];
    durationMs: number;
    error?: string;
  } | null>(null);
  const [queryHistory, setQueryHistory] = useState<string[]>([
    `SELECT * FROM "${activeTable?.name || 'users'}" LIMIT 25;`,
  ]);

  // Copy feedback state
  const [copiedKey, setCopiedKey] = useState<string | null>(null);
  const triggerCopy = useCallback((key: string, text: string) => {
    navigator.clipboard.writeText(text);
    setCopiedKey(key);
    setTimeout(() => setCopiedKey(null), 2000);
  }, []);

  // Update SQL default when active table changes
  const handleSelectTable = (tblId: string) => {
    setActiveTableId(tblId);
    setSearchQuery('');
    setSelectedPks(new Set());
    setCurrentPage(1);
    const target = tables.find((t) => t.id === tblId);
    if (target) {
      setSqlQuery(`SELECT * FROM "${target.name}" LIMIT 25;`);
    }
  };

  // Table dropdown state (Polaris Dropdown)
  const [tableDropdownOpen, setTableDropdownOpen] = useState(false);
  const [tableFilterText, setTableFilterText] = useState('');
  const tableDropdownRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (tableDropdownRef.current && !tableDropdownRef.current.contains(e.target as Node)) {
        setTableDropdownOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const filteredTables = useMemo(() => {
    if (!tableFilterText.trim()) return tables;
    const q = tableFilterText.toLowerCase().trim();
    return tables.filter((t) => t.name.toLowerCase().includes(q));
  }, [tables, tableFilterText]);

  // Horizontal Table Scroll state (2-arrow indicator)
  const tableScrollRef = useRef<HTMLDivElement>(null);
  const [canScrollLeft, setCanScrollLeft] = useState(false);
  const [canScrollRight, setCanScrollRight] = useState(false);

  const checkTableScroll = useCallback(() => {
    const el = tableScrollRef.current;
    if (!el) return;
    setCanScrollLeft(el.scrollLeft > 10);
    setCanScrollRight(el.scrollLeft < el.scrollWidth - el.clientWidth - 10);
  }, []);

  useEffect(() => {
    checkTableScroll();
    const el = tableScrollRef.current;
    if (el) {
      el.addEventListener('scroll', checkTableScroll, { passive: true });
      window.addEventListener('resize', checkTableScroll);
    }
    return () => {
      if (el) el.removeEventListener('scroll', checkTableScroll);
      window.removeEventListener('resize', checkTableScroll);
    };
  }, [checkTableScroll, activeTable]);

  const scrollTable = (direction: 'left' | 'right') => {
    if (!tableScrollRef.current) return;
    const offset = direction === 'left' ? -320 : 320;
    tableScrollRef.current.scrollBy({ left: offset, behavior: 'smooth' });
  };

  // Current active rows
  const currentTableRows = (activeTable ? dataStore[activeTable.id] : []) || [];
  const pkCol = activeTable?.columns.find((c) => c.isPK) || activeTable?.columns[0];

  // Filtered & Sorted rows
  const processedRows = useMemo(() => {
    let result = [...currentTableRows];

    if (searchQuery.trim()) {
      const q = searchQuery.toLowerCase();
      result = result.filter((row) =>
        Object.values(row).some((val) => String(val ?? '').toLowerCase().includes(q)),
      );
    }

    if (sortCol) {
      result.sort((a, b) => {
        const valA = a[sortCol];
        const valB = b[sortCol];
        if (valA === valB) return 0;
        if (valA === null || valA === undefined) return 1;
        if (valB === null || valB === undefined) return -1;
        if (typeof valA === 'number' && typeof valB === 'number') {
          return sortDir === 'asc' ? valA - valB : valB - valA;
        }
        return sortDir === 'asc'
          ? String(valA).localeCompare(String(valB))
          : String(valB).localeCompare(String(valA));
      });
    }

    return result;
  }, [currentTableRows, searchQuery, sortCol, sortDir]);

  // Paginated Rows
  const totalPages = Math.max(1, Math.ceil(processedRows.length / pageSize));
  const paginatedRows = useMemo(() => {
    const start = (currentPage - 1) * pageSize;
    return processedRows.slice(start, start + pageSize);
  }, [processedRows, currentPage, pageSize]);

  // Handle Sort Toggle
  const toggleSort = (colName: string) => {
    if (sortCol === colName) {
      if (sortDir === 'asc') setSortDir('desc');
      else {
        setSortCol(null);
        setSortDir('asc');
      }
    } else {
      setSortCol(colName);
      setSortDir('asc');
    }
  };

  // Row Selection
  const toggleSelectAll = () => {
    if (!pkCol) return;
    if (selectedPks.size === paginatedRows.length && paginatedRows.length > 0) {
      setSelectedPks(new Set());
    } else {
      const next = new Set<string>();
      paginatedRows.forEach((r) => next.add(String(r[pkCol.name])));
      setSelectedPks(next);
    }
  };

  const toggleSelectRow = (pkVal: string) => {
    const next = new Set(selectedPks);
    if (next.has(pkVal)) next.delete(pkVal);
    else next.add(pkVal);
    setSelectedPks(next);
  };

  // Row Deletion
  const handleDeleteRow = (pkVal: string) => {
    if (!activeTable || !pkCol) return;
    setDataStore((prev) => ({
      ...prev,
      [activeTable.id]: (prev[activeTable.id] || []).filter((r) => String(r[pkCol.name]) !== pkVal),
    }));
    setSelectedPks((prev) => {
      const next = new Set(prev);
      next.delete(pkVal);
      return next;
    });
  };

  // Bulk Deletion
  const handleBulkDelete = () => {
    if (!activeTable || !pkCol || selectedPks.size === 0) return;
    setDataStore((prev) => ({
      ...prev,
      [activeTable.id]: (prev[activeTable.id] || []).filter((r) => !selectedPks.has(String(r[pkCol.name]))),
    }));
    setSelectedPks(new Set());
  };

  // Seed Rows
  const handleSeedRows = () => {
    if (!activeTable) return;
    const additional = generateSampleDataForTable(activeTable, 5);
    setDataStore((prev) => ({
      ...prev,
      [activeTable.id]: [...(prev[activeTable.id] || []), ...additional],
    }));
  };

  // Open Insert Modal
  const handleOpenInsert = () => {
    if (!activeTable) return;
    const initialDraft: Record<string, string> = {};
    activeTable.columns.forEach((c) => {
      if (c.isPK) {
        initialDraft[c.name] = c.type.includes('int')
          ? String(currentTableRows.length + 1)
          : `pk-${Date.now().toString(36)}`;
      } else {
        initialDraft[c.name] = c.defaultValue ? c.defaultValue.replace(/^'|'$/g, '') : '';
      }
    });
    setEditingRow(null);
    setRowDraft(initialDraft);
    setEditModalOpen(true);
  };

  // Open Edit Modal
  const handleOpenEdit = (row: Record<string, unknown>) => {
    if (!activeTable) return;
    const draft: Record<string, string> = {};
    activeTable.columns.forEach((c) => {
      draft[c.name] = row[c.name] !== null && row[c.name] !== undefined ? String(row[c.name]) : '';
    });
    setEditingRow(row);
    setRowDraft(draft);
    setEditModalOpen(true);
  };

  // Save Record
  const handleSaveRecord = (e: React.FormEvent) => {
    e.preventDefault();
    if (!activeTable || !pkCol) return;

    const formattedRow: Record<string, unknown> = {};
    activeTable.columns.forEach((c) => {
      const raw = rowDraft[c.name] ?? '';
      const cType = c.type.toLowerCase();
      if (raw === '' && c.isNullable && !c.isPK) {
        formattedRow[c.name] = null;
      } else if (/^(tiny|small|medium|big)?(integer|int|serial)(\d+|\b|\()/i.test(cType.trim())) {
        formattedRow[c.name] = parseInt(raw, 10) || 0;
      } else if (cType.includes('float') || cType.includes('numeric') || cType.includes('decimal')) {
        formattedRow[c.name] = parseFloat(raw) || 0;
      } else if (cType.includes('bool')) {
        formattedRow[c.name] = raw === 'true' || raw === '1';
      } else {
        formattedRow[c.name] = raw;
      }
    });

    if (editingRow) {
      // Update
      const targetPk = String(editingRow[pkCol.name]);
      setDataStore((prev) => ({
        ...prev,
        [activeTable.id]: (prev[activeTable.id] || []).map((r) =>
          String(r[pkCol.name]) === targetPk ? formattedRow : r,
        ),
      }));
    } else {
      // Insert
      setDataStore((prev) => ({
        ...prev,
        [activeTable.id]: [formattedRow, ...(prev[activeTable.id] || [])],
      }));
    }

    setEditModalOpen(false);
    setEditingRow(null);
  };

  // Execute SQL Runner
  const handleRunQuery = () => {
    if (!activeTable) return;
    const start = performance.now();
    const trimmed = sqlQuery.trim().replace(/;$/, '');

    try {
      if (/^SELECT/i.test(trimmed)) {
        let rows = [...currentTableRows];
        const limitMatch = trimmed.match(/LIMIT\s+(\d+)/i);
        if (limitMatch) {
          const lim = parseInt(limitMatch[1], 10);
          rows = rows.slice(0, lim);
        }
        const cols = activeTable.columns.map((c) => c.name);
        const durationMs = Math.max(1, Math.round(performance.now() - start));
        setSqlResult({ columns: cols, rows, durationMs });
      } else {
        const durationMs = Math.max(1, Math.round(performance.now() - start));
        // Bu önizlemede gerçek bir veritabanı çalıştırıcısı YOK: SELECT dışı ifadeler hiçbir yere gönderilmez
        setSqlResult({
          columns: ['status', 'message', 'rows_affected'],
          rows: [
            {
              status: 'NOT_EXECUTED',
              message: 'Preview only: this statement was not run against any database.',
              rows_affected: 0,
            },
          ],
          durationMs,
        });
      }

      // Add to query history
      if (!queryHistory.includes(sqlQuery.trim())) {
        setQueryHistory((prev) => [sqlQuery.trim(), ...prev.slice(0, 4)]);
      }
    } catch (err) {
      setSqlResult({
        columns: ['error'],
        rows: [],
        durationMs: Math.max(1, Math.round(performance.now() - start)),
        error: err instanceof Error ? err.message : 'SQL Execution error',
      });
    }
  };

  // Total columns and relationships
  const totalColumns = tables.reduce((acc, t) => acc + t.columns.length, 0);
  const totalRelations = schema.relations?.length || 0;

  return (
    <PageShell width="max-w-6xl">
      {/* ── Top Breadcrumbs & Actions ───────────────────────────────────── */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 pb-3 border-b border-line">
        <div className="flex items-center flex-wrap gap-2 text-xs sm:text-sm">
          <ModuleSymbol module="desk" size={20} />
          <Link
            href="/desk"
            onClick={onExitProject}
            className="group inline-flex items-center text-content-muted hover:text-content-primary transition-colors cursor-pointer"
          >
            <ModuleWordmark
              module="desk"
              height={13}
              className="text-content-muted group-hover:text-content-primary transition-colors"
            />
          </Link>
          <span className="text-content-muted">/</span>
          <span className="text-content-secondary font-mono">{resolvedOwner}</span>
          <span className="text-content-muted">/</span>
          <h1 className="font-sans font-bold text-content-primary tracking-tight text-sm sm:text-base">
            {projectName}
          </h1>
          {tables.length > 0 && (
            <div className="flex items-center gap-1.5 ml-1 sm:ml-2">
              <span className="text-content-muted">/</span>
              <select
                value={activeTable?.id || ''}
                onChange={(e) => handleSelectTable(e.target.value)}
                className="h-7 px-2.5 rounded-[var(--radius-control)] border border-line-strong bg-surface-800 hover:bg-surface-750 text-content-primary text-xs font-mono font-semibold cursor-pointer focus:outline-hidden focus:border-focus-ring shadow-xs shrink-0 transition-colors"
                aria-label="Select table (Global across all tabs)"
              >
                {tables.map((t) => {
                  const count = (dataStore[t.id] || []).length;
                  return (
                    <option key={t.id} value={t.id} className="bg-surface-850 text-content-primary">
                      {t.name} ({count} rows)
                    </option>
                  );
                })}
              </select>
            </div>
          )}
        </div>

        <div className="flex items-center gap-2">
          <Button
            size="sm"
            variant="secondary"
            icon={RefreshCw}
            onClick={() => {
              if (activeTable) {
                setDataStore((prev) => ({
                  ...prev,
                  [activeTable.id]: generateSampleDataForTable(activeTable, 6),
                }));
              }
            }}
          >
            Reset Sample Data
          </Button>
          <Button
            size="sm"
            variant="primary"
            icon={Plus}
            onClick={handleOpenInsert}
            disabled={!activeTable}
          >
            Insert Record
          </Button>
        </div>
      </div>

      <Callout tone="info" title="Sample data preview">
        Rows, edits and SQL results below are generated in your browser from this project&apos;s column types. They are
        never sent to your database. Use the Gateway tab to connect real data.
      </Callout>

      {/* ── Studio Navigation Tabs ───────────────────────────────────────── */}
      <Tabs<StudioTab>
        ariaLabel="Desk Studio Sections"
        value={activeTab}
        onChange={setActiveTab}
        tabs={[
          { value: 'records', label: 'Data Browser', icon: Table2, count: currentTableRows.length },
          { value: 'schema', label: 'Schema & DDL', icon: Database, count: tables.length },
          { value: 'sql', label: 'SQL Console', icon: Terminal },
          { value: 'gateway', label: 'Gateway REST API', icon: Cable },
        ]}
      />

      {/* ───────────────────────────────────────────────────────────────── */}
      {/* TAB 1: DATA BROWSER (RECORDS)                                     */}
      {/* ───────────────────────────────────────────────────────────────── */}
      {activeTab === 'records' && (
        <div className="space-y-4">
          {tables.length === 0 ? (
            <Card>
              <EmptyState
                icon={Table2}
                title="No tables found in this project"
                description="This project has no database tables defined yet. Open Canvas to design your tables, or generate a schema."
                action={
                  <Button
                    variant="primary"
                    icon={Layers}
                    onClick={() => router.push(`/canvas?project=${encodeURIComponent(projectId)}`)}
                  >
                    Open Canvas
                  </Button>
                }
              />
            </Card>
          ) : (
            <>
              {/* Table Controls: Dropdown + Search + Actions */}
              <div className="flex flex-col sm:flex-row items-stretch sm:items-center justify-between gap-3">
                <div className="flex items-center gap-2 flex-1 max-w-xl">
                  {/* Classic Solid Table Dropdown */}
                  <select
                    value={activeTable?.id || ''}
                    onChange={(e) => handleSelectTable(e.target.value)}
                    className="h-9 px-3 rounded-[var(--radius-control)] border border-line-strong bg-surface-800 text-content-primary text-xs font-mono font-semibold cursor-pointer focus:outline-hidden focus:border-focus-ring shadow-xs shrink-0"
                    aria-label="Select table"
                  >
                    {tables.map((t) => {
                      const count = (dataStore[t.id] || []).length;
                      return (
                        <option key={t.id} value={t.id} className="bg-surface-850 text-content-primary">
                          {t.name} ({count} rows)
                        </option>
                      );
                    })}
                  </select>

                  {/* Search Input */}
                  <div className="relative flex-1">
                    <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-content-muted" />
                    <input
                      type="text"
                      placeholder={`Search in ${activeTable?.name || 'table'}…`}
                      value={searchQuery}
                      onChange={(e) => {
                        setSearchQuery(e.target.value);
                        setCurrentPage(1);
                      }}
                      className={`${inputClass} !pl-9`}
                    />
                  </div>
                  {searchQuery && (
                    <Button size="sm" variant="ghost" onClick={() => setSearchQuery('')}>
                      Clear
                    </Button>
                  )}
                </div>

                <div className="flex items-center flex-wrap gap-2">
                  {selectedPks.size > 0 && (
                    <Button size="sm" variant="danger" icon={Trash2} onClick={handleBulkDelete}>
                      Delete ({selectedPks.size})
                    </Button>
                  )}
                  <Button size="sm" variant="secondary" icon={Sparkles} onClick={handleSeedRows}>
                    Seed 5 Rows
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    icon={FileSpreadsheet}
                    onClick={() => {
                      if (activeTable) exportToCsv(activeTable.name, activeTable.columns, currentTableRows);
                    }}
                  >
                    CSV
                  </Button>
                  <Button
                    size="sm"
                    variant="secondary"
                    icon={FileJson}
                    onClick={() => {
                      if (activeTable) exportToJson(activeTable.name, currentTableRows);
                    }}
                  >
                    JSON
                  </Button>
                </div>
              </div>

              {/* Data Grid Card */}
              <Card flush>
                {paginatedRows.length === 0 ? (
                  <EmptyState
                    icon={Table2}
                    title="No records found"
                    description={
                      searchQuery
                        ? 'No rows match your search criteria. Try a different query or clear filters.'
                        : `The table "${activeTable?.name}" has no rows. Click "Insert Record" or "Seed 5 Rows" to populate.`
                    }
                    action={
                      <div className="flex items-center gap-2">
                        {searchQuery ? (
                          <Button variant="secondary" onClick={() => setSearchQuery('')}>
                            Clear Search
                          </Button>
                        ) : (
                          <>
                            <Button variant="primary" icon={Plus} onClick={handleOpenInsert}>
                              Insert Record
                            </Button>
                            <Button variant="secondary" icon={Sparkles} onClick={handleSeedRows}>
                              Seed 5 Rows
                            </Button>
                          </>
                        )}
                      </div>
                    }
                  />
                ) : (
                  <div className="relative group">
                    {/* Left Scroll Double-Arrow Button */}
                    {canScrollLeft && (
                      <button
                        type="button"
                        onClick={() => scrollTable('left')}
                        title="Scroll left"
                        aria-label="Scroll left"
                        className="absolute left-2 top-1/2 -translate-y-1/2 z-30 w-8 h-8 rounded-full bg-surface-850 border border-line-strong text-content-primary flex items-center justify-center shadow-lg hover:bg-surface-750 transition-all hover:scale-105 cursor-pointer"
                      >
                        <ChevronsLeft className="w-4 h-4 text-accent-text" />
                      </button>
                    )}

                    {/* Right Scroll Double-Arrow Button (Pulsing Glow) */}
                    {canScrollRight && (
                      <button
                        type="button"
                        onClick={() => scrollTable('right')}
                        title="Scroll right to view more columns"
                        aria-label="Scroll right"
                        className="absolute right-2 top-1/2 -translate-y-1/2 z-30 w-8 h-8 rounded-full bg-surface-850 border border-line-strong text-content-primary flex items-center justify-center shadow-xl hover:bg-surface-750 transition-all hover:scale-105 cursor-pointer animate-pulse ring-1 ring-content-primary/20"
                      >
                        <ChevronsRight className="w-4 h-4 text-content-primary" />
                      </button>
                    )}

                    {/* Subtle Edge Fade Gradients */}
                    {canScrollRight && (
                      <div className="pointer-events-none absolute inset-y-0 right-0 w-8 bg-gradient-to-l from-surface-800 to-transparent z-20" />
                    )}
                    {canScrollLeft && (
                      <div className="pointer-events-none absolute inset-y-0 left-0 w-8 bg-gradient-to-r from-surface-800 to-transparent z-20" />
                    )}

                    <div ref={tableScrollRef} className="overflow-x-auto scrollbar-none">
                      <table className="w-full text-xs">
                      <caption className="sr-only">Table records for {activeTable?.name}</caption>
                      <thead>
                        <tr className="border-b border-line bg-surface-700/60 select-none">
                          <th scope="col" className="w-10 px-3 py-2.5 text-center">
                            <input
                              type="checkbox"
                              checked={selectedPks.size === paginatedRows.length && paginatedRows.length > 0}
                              onChange={toggleSelectAll}
                              aria-label="Select all rows"
                              className="rounded-[var(--radius-control)] border-line-strong bg-surface-800 text-content-primary cursor-pointer"
                            />
                          </th>
                          {activeTable?.columns.map((col) => {
                            const isSorted = sortCol === col.name;
                            return (
                              <th
                                key={col.id}
                                scope="col"
                                onClick={() => toggleSort(col.name)}
                                className="px-4 py-2.5 text-left text-micro font-bold uppercase tracking-wider text-content-muted whitespace-nowrap cursor-pointer hover:text-content-primary transition-colors"
                              >
                                <div className="flex items-center gap-1.5">
                                  {col.isPK && <KeyRound className="w-3 h-3 text-content-primary shrink-0" />}
                                  <span>{col.name}</span>
                                  <span className="text-[10px] font-mono lowercase text-content-muted font-normal">
                                    {col.type}
                                  </span>
                                  {isSorted ? (
                                    sortDir === 'asc' ? (
                                      <ArrowUp className="w-3 h-3 text-content-primary" />
                                    ) : (
                                      <ArrowDown className="w-3 h-3 text-content-primary" />
                                    )
                                  ) : (
                                    <ArrowUpDown className="w-2.5 h-2.5 text-content-muted opacity-40" />
                                  )}
                                </div>
                              </th>
                            );
                          })}
                          <th scope="col" className="w-28 px-4 py-2.5 text-right text-micro font-bold uppercase tracking-wider text-content-muted">
                            Actions
                          </th>
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-line">
                        {paginatedRows.map((row, idx) => {
                          const rowPkVal = pkCol ? String(row[pkCol.name]) : String(idx);
                          const isSelected = selectedPks.has(rowPkVal);
                          return (
                            <tr
                              key={rowPkVal}
                              className={`group transition-colors ${
                                isSelected ? 'bg-accent-subtle/30' : 'hover:bg-surface-700/30'
                              }`}
                            >
                              <td className="w-10 px-3 py-2 text-center">
                                <input
                                  type="checkbox"
                                  checked={isSelected}
                                  onChange={() => toggleSelectRow(rowPkVal)}
                                  aria-label={`Select row ${rowPkVal}`}
                                  className="rounded-[var(--radius-control)] border-line-strong bg-surface-800 text-content-primary cursor-pointer"
                                />
                              </td>
                              {activeTable?.columns.map((col) => {
                                const val = row[col.name];
                                const isPk = col.isPK;
                                return (
                                  <td
                                    key={col.id}
                                    className={`px-4 py-2 text-xs font-mono whitespace-nowrap max-w-xs truncate ${
                                      isPk ? 'text-content-primary font-semibold' : 'text-content-secondary'
                                    }`}
                                    title={val !== null && val !== undefined ? String(val) : 'null'}
                                  >
                                    {val === null || val === undefined ? (
                                      <span className="text-content-muted italic text-[11px]">null</span>
                                    ) : typeof val === 'boolean' ? (
                                      <span
                                        className={`inline-flex px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold ${
                                          val ? 'bg-surface-700 text-content-primary border border-line-strong' : 'bg-surface-800 text-content-muted border border-line'
                                        }`}
                                      >
                                        {val ? 'TRUE' : 'FALSE'}
                                      </span>
                                    ) : (
                                      String(val)
                                    )}
                                  </td>
                                );
                              })}
                              <td className="w-28 px-4 py-2 text-right whitespace-nowrap">
                                <div className="flex items-center justify-end gap-1 opacity-80 group-hover:opacity-100 transition-opacity">
                                  <button
                                    type="button"
                                    onClick={() => handleOpenEdit(row)}
                                    title="Edit Row"
                                    className="p-1.5 rounded-[var(--radius-control)] hover:bg-surface-700 text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
                                  >
                                    <Pencil className="w-3.5 h-3.5" />
                                  </button>
                                  <button
                                    type="button"
                                    onClick={() => triggerCopy(`row-${rowPkVal}`, JSON.stringify(row, null, 2))}
                                    title="Copy Row JSON"
                                    className="p-1.5 rounded-[var(--radius-control)] hover:bg-surface-700 text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
                                  >
                                    {copiedKey === `row-${rowPkVal}` ? (
                                      <Check className="w-3.5 h-3.5 text-content-primary" />
                                    ) : (
                                      <Copy className="w-3.5 h-3.5" />
                                    )}
                                  </button>
                                  <button
                                    type="button"
                                    onClick={() => handleDeleteRow(rowPkVal)}
                                    title="Delete Row"
                                    className="p-1.5 rounded-[var(--radius-control)] hover:bg-danger-subtle text-content-secondary hover:text-danger-text transition-colors cursor-pointer"
                                  >
                                    <Trash2 className="w-3.5 h-3.5" />
                                  </button>
                                </div>
                              </td>
                            </tr>
                          );
                        })}
                      </tbody>
                    </table>
                  </div>
                  </div>
                )}

                {/* Pagination Footer */}
                {processedRows.length > 0 && (
                  <div className="flex flex-col sm:flex-row items-center justify-between gap-3 px-5 py-3 border-t border-line bg-surface-800/40 text-xs text-content-muted">
                    <div className="flex items-center gap-3">
                      <span>
                        Showing{' '}
                        <strong className="text-content-primary font-mono">
                          {(currentPage - 1) * pageSize + 1}
                        </strong>{' '}
                        to{' '}
                        <strong className="text-content-primary font-mono">
                          {Math.min(currentPage * pageSize, processedRows.length)}
                        </strong>{' '}
                        of{' '}
                        <strong className="text-content-primary font-mono">{processedRows.length}</strong>{' '}
                        rows
                      </span>

                      <div className="flex items-center gap-1.5">
                        <span className="text-micro font-bold uppercase tracking-wider">Per page:</span>
                        <select
                          value={pageSize}
                          onChange={(e) => {
                            setPageSize(Number(e.target.value));
                            setCurrentPage(1);
                          }}
                          className="h-7 px-2 rounded-[var(--radius-control)] border border-line-strong bg-surface-700 text-content-primary text-xs font-mono cursor-pointer"
                        >
                          <option value={10}>10</option>
                          <option value={25}>25</option>
                          <option value={50}>50</option>
                          <option value={100}>100</option>
                        </select>
                      </div>
                    </div>

                    <div className="flex items-center gap-1.5">
                      <Button
                        size="sm"
                        variant="secondary"
                        disabled={currentPage <= 1}
                        onClick={() => setCurrentPage((p) => Math.max(1, p - 1))}
                      >
                        Previous
                      </Button>
                      <span className="px-2 font-mono text-content-primary text-xs">
                        Page {currentPage} of {totalPages}
                      </span>
                      <Button
                        size="sm"
                        variant="secondary"
                        disabled={currentPage >= totalPages}
                        onClick={() => setCurrentPage((p) => Math.min(totalPages, p + 1))}
                      >
                        Next
                      </Button>
                    </div>
                  </div>
                )}
              </Card>
            </>
          )}
        </div>
      )}

      {/* ───────────────────────────────────────────────────────────────── */}
      {/* TAB 2: SCHEMA & DDL EXPLORER                                      */}
      {/* ───────────────────────────────────────────────────────────────── */}
      {activeTab === 'schema' && (
        <div className="space-y-6">
          {activeTable ? (
            <>
              {/* Columns Table */}
              <Card
                title={`Table Definition — ${activeTable.name}`}
                description="Column types, primary key flags, nullability constraints, and auto-generated defaults."
                actions={
                  <div className="flex items-center gap-2">
                    <span className="text-micro font-bold uppercase text-content-muted">Table:</span>
                    <select
                      value={activeTable?.id || ''}
                      onChange={(e) => handleSelectTable(e.target.value)}
                      className="h-7 px-2.5 rounded-[var(--radius-control)] border border-line-strong bg-surface-800 text-content-primary text-xs font-mono font-semibold cursor-pointer focus:outline-hidden focus:border-focus-ring shadow-xs"
                      aria-label="Select table for schema definition"
                    >
                      {tables.map((t) => (
                        <option key={t.id} value={t.id} className="bg-surface-850 text-content-primary">
                          {t.name}
                        </option>
                      ))}
                    </select>
                  </div>
                }
                flush
              >
                <DataTable
                  caption={`Columns in ${activeTable.name}`}
                  columns={[
                    { label: 'Column Name' },
                    { label: 'Data Type' },
                    { label: 'Key & Constraints' },
                    { label: 'Nullable' },
                    { label: 'Default Value' },
                  ]}
                >
                  {activeTable.columns.map((col) => (
                    <tr key={col.id} className="hover:bg-surface-700/30 transition-colors">
                      <Td mono>
                        <div className="flex items-center gap-2">
                          {col.isPK && <KeyRound className="w-3.5 h-3.5 text-content-primary shrink-0" />}
                          <span className={col.isPK ? 'font-bold text-content-primary' : 'text-content-secondary'}>
                            {col.name}
                          </span>
                        </div>
                      </Td>
                      <Td mono>
                        <span className="inline-flex px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 text-content-primary text-micro font-mono">
                          {col.type.toUpperCase()}
                          {col.length ? `(${col.length})` : ''}
                        </span>
                      </Td>
                      <Td>
                        <div className="flex items-center gap-1.5 flex-wrap">
                          {col.isPK && <Badge tone="accent">Primary Key</Badge>}
                          {col.isFK && <Badge tone="neutral">Foreign Key</Badge>}
                          {col.identity && <Badge tone="success">Identity</Badge>}
                          {!col.isPK && !col.isFK && !col.identity && (
                            <span className="text-content-muted text-xs">—</span>
                          )}
                        </div>
                      </Td>
                      <Td>
                        {col.isNullable ? (
                          <span className="text-content-secondary text-xs">Nullable</span>
                        ) : (
                          <span className="text-xs font-semibold text-content-primary">NOT NULL</span>
                        )}
                      </Td>
                      <Td mono>
                        {col.defaultValue ? (
                          <code className="text-content-primary text-micro">{col.defaultValue}</code>
                        ) : (
                          <span className="text-content-muted text-xs">—</span>
                        )}
                      </Td>
                    </tr>
                  ))}
                </DataTable>
              </Card>

              {/* Foreign Keys / Relations */}
              {schema.relations && schema.relations.length > 0 && (
                <Card
                  title="Foreign Key Relationships"
                  description="Referential integrity constraints linking tables in this database model."
                  flush
                >
                  <DataTable
                    caption="Foreign Key Relationships"
                    columns={[
                      { label: 'Relationship' },
                      { label: 'Source Table' },
                      { label: 'Target Reference' },
                      { label: 'On Delete Action' },
                    ]}
                  >
                    {schema.relations.map((rel) => {
                      const srcTbl = tables.find((t) => t.id === rel.sourceTableId);
                      const srcCol = srcTbl?.columns.find((c) => c.id === rel.sourceColumnId);
                      const tgtTbl = tables.find((t) => t.id === rel.targetTableId);
                      const tgtCol = tgtTbl?.columns.find((c) => c.id === rel.targetColumnId);

                      return (
                        <tr key={rel.id} className="hover:bg-surface-700/30 transition-colors">
                          <Td>
                            <Badge tone="neutral">{rel.type || 'OneToMany'}</Badge>
                          </Td>
                          <Td mono>
                            <span className="font-semibold text-content-primary">
                              {srcTbl?.name || rel.sourceTableId}
                            </span>
                            .{srcCol?.name || rel.sourceColumnId}
                          </Td>
                          <Td mono>
                            <span className="font-semibold text-content-primary">
                              {tgtTbl?.name || rel.targetTableId}
                            </span>
                            .{tgtCol?.name || rel.targetColumnId}
                          </Td>
                          <Td>
                            <span className="text-xs font-mono text-content-secondary">
                              {rel.onDelete || 'NoAction'}
                            </span>
                          </Td>
                        </tr>
                      );
                    })}
                  </DataTable>
                </Card>
              )}

              {/* DDL Code Box */}
              <Card
                title="Generated DDL Syntax"
                description={`Declarative SQL CREATE TABLE script targeting ${dbEngine}.`}
                actions={
                  <Button
                    size="sm"
                    variant="secondary"
                    icon={copiedKey === 'ddl' ? Check : Copy}
                    onClick={() => triggerCopy('ddl', generateTableDdl(activeTable, dbEngine))}
                  >
                    {copiedKey === 'ddl' ? 'Copied' : 'Copy DDL'}
                  </Button>
                }
              >
                <pre className="p-4 rounded-[var(--radius-control)] bg-surface-900 border border-line-strong text-content-primary font-mono text-xs overflow-x-auto leading-relaxed">
                  {generateTableDdl(activeTable, dbEngine)}
                </pre>
              </Card>
            </>
          ) : (
            <Card>
              <EmptyState icon={Database} title="No tables found" description="Create a table to view its schema." />
            </Card>
          )}
        </div>
      )}

      {/* ───────────────────────────────────────────────────────────────── */}
      {/* TAB 3: SQL CONSOLE & RUNNER                                       */}
      {/* ───────────────────────────────────────────────────────────────── */}
      {activeTab === 'sql' && (
        <div className="space-y-4">
          <Card
            title="Interactive SQL Query Runner"
            description={`Run SQL queries directly against ${activeTable?.name || 'the project'} in ${dbEngine}.`}
            actions={
              <Button size="sm" variant="primary" icon={Play} onClick={handleRunQuery}>
                Run Query (Ctrl+Enter)
              </Button>
            }
          >
            <div className="space-y-3">
              {/* Query Templates */}
              {activeTable && (
                <div className="flex items-center gap-1.5 flex-wrap">
                  <span className="text-micro font-bold uppercase tracking-wider text-content-muted mr-1">
                    Templates:
                  </span>
                  <button
                    type="button"
                    onClick={() => setSqlQuery(`SELECT * FROM "${activeTable.name}" LIMIT 25;`)}
                    className="px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-mono bg-surface-700 hover:bg-surface-600 text-content-primary border border-line transition-colors cursor-pointer"
                  >
                    SELECT * LIMIT 25
                  </button>
                  <button
                    type="button"
                    onClick={() => setSqlQuery(`SELECT COUNT(*) AS total_records FROM "${activeTable.name}";`)}
                    className="px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-mono bg-surface-700 hover:bg-surface-600 text-content-primary border border-line transition-colors cursor-pointer"
                  >
                    COUNT(*)
                  </button>
                  <button
                    type="button"
                    onClick={() =>
                      setSqlQuery(
                        `SELECT * FROM "${activeTable.name}" WHERE ${pkCol?.name || 'id'} IS NOT NULL ORDER BY 1 DESC;`,
                      )
                    }
                    className="px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-mono bg-surface-700 hover:bg-surface-600 text-content-primary border border-line transition-colors cursor-pointer"
                  >
                    ORDER BY 1 DESC
                  </button>
                </div>
              )}

              {/* Editor Textarea */}
              <div className="relative">
                <textarea
                  rows={5}
                  value={sqlQuery}
                  onChange={(e) => setSqlQuery(e.target.value)}
                  onKeyDown={(e) => {
                    if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
                      e.preventDefault();
                      handleRunQuery();
                    }
                  }}
                  placeholder="Enter your SQL query here…"
                  className="w-full rounded-[var(--radius-control)] border border-line-strong bg-surface-900 p-3.5 font-mono text-xs text-content-primary placeholder:text-content-muted focus:outline-none focus:border-focus-ring focus:ring-1 focus:ring-focus-ring shadow-xs leading-relaxed"
                />
              </div>

              {/* Execution Status / Duration */}
              {sqlResult && (
                <div className="flex items-center justify-between text-xs font-mono py-1 px-2 rounded-[var(--radius-control)] bg-surface-700/50 border border-line">
                  <div className="flex items-center gap-2">
                    {sqlResult.error ? (
                      <span className="text-danger-text font-semibold">Query Failed</span>
                    ) : (
                      <span className="text-success-text font-semibold flex items-center gap-1">
                        <CheckCircle2 className="w-3.5 h-3.5" /> Query Succeeded
                      </span>
                    )}
                    <span className="text-content-muted">·</span>
                    <span className="text-content-secondary">{sqlResult.rows.length} rows returned</span>
                  </div>
                  <span className="text-content-muted">Execution time: {sqlResult.durationMs}ms</span>
                </div>
              )}
            </div>
          </Card>

          {/* Query Results Table */}
          {sqlResult && (
            <Card title="Query Results" description="Tabular results returned by the executor engine." flush>
              {sqlResult.error ? (
                <div className="p-4 text-xs font-mono text-danger-text bg-danger-subtle/20 border-b border-line">
                  {sqlResult.error}
                </div>
              ) : sqlResult.rows.length === 0 ? (
                <div className="p-6 text-center text-xs text-content-muted">No rows returned by this query.</div>
              ) : (
                <div className="overflow-x-auto">
                  <table className="w-full text-xs font-mono">
                    <thead>
                      <tr className="border-b border-line bg-surface-700/60">
                        {sqlResult.columns.map((c) => (
                          <th
                            key={c}
                            scope="col"
                            className="px-4 py-2.5 text-left text-micro font-bold uppercase tracking-wider text-content-muted whitespace-nowrap"
                          >
                            {c}
                          </th>
                        ))}
                      </tr>
                    </thead>
                    <tbody className="divide-y divide-line">
                      {sqlResult.rows.map((r, rowIdx) => (
                        <tr key={rowIdx} className="hover:bg-surface-700/30 transition-colors">
                          {sqlResult.columns.map((colName) => {
                            const val = r[colName];
                            return (
                              <td
                                key={colName}
                                className="px-4 py-2 text-content-secondary whitespace-nowrap max-w-xs truncate"
                              >
                                {val === null || val === undefined ? (
                                  <span className="text-content-muted italic">null</span>
                                ) : typeof val === 'boolean' ? (
                                  String(val)
                                ) : (
                                  String(val)
                                )}
                              </td>
                            );
                          })}
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              )}
            </Card>
          )}
        </div>
      )}

      {/* ───────────────────────────────────────────────────────────────── */}
      {/* TAB 4: GATEWAY REST API & MCP                                      */}
      {/* ───────────────────────────────────────────────────────────────── */}
      {activeTab === 'gateway' && (
        <div className="space-y-6">
          <Callout
            tone="info"
            title="Project-Scoped Gateway REST & MCP Engine"
            action={
              <Button
                size="sm"
                variant="secondary"
                icon={ExternalLink}
                onClick={() => router.push(`/vault?project=${encodeURIComponent(projectId)}`)}
              >
                Manage API Keys in Vault
              </Button>
            }
          >
            Every table defined in this project is automatically exposed as a secure REST endpoint and Model Context
            Protocol (MCP) tool. Requests must supply an authenticated session or a project Gateway API Key.
          </Callout>

          {activeTable ? (
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              {/* Endpoint 1: List */}
              <Card
                title={`POST /api/gateway/list (${activeTable.name})`}
                description="List and paginate records with server-side filtering and sorting."
                actions={
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={Copy}
                    onClick={() =>
                      triggerCopy(
                        'curl-list',
                        `curl -X POST https://api.namines.com/api/gateway/list \\\n  -H "Content-Type: application/json" \\\n  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\\n  -d '{"projectId":"${projectId}","tableName":"${activeTable.name}","page":1,"pageSize":25}'`,
                      )
                    }
                  >
                    Copy cURL
                  </Button>
                }
              >
                <pre className="p-3 rounded-[var(--radius-control)] bg-surface-900 border border-line-strong text-content-primary font-mono text-micro overflow-x-auto leading-relaxed">
                  {`curl -X POST https://api.namines.com/api/gateway/list \\
  -H "Content-Type: application/json" \\
  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\
  -d '{
    "projectId": "${projectId}",
    "tableName": "${activeTable.name}",
    "page": 1,
    "pageSize": 25,
    "orderByColumn": "${pkCol?.name || 'id'}",
    "sortDirection": "Asc"
  }'`}
                </pre>
              </Card>

              {/* Endpoint 2: Create */}
              <Card
                title={`POST /api/gateway/create (${activeTable.name})`}
                description="Insert a new row into the active table."
                actions={
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={Copy}
                    onClick={() =>
                      triggerCopy(
                        'curl-create',
                        `curl -X POST https://api.namines.com/api/gateway/create \\\n  -H "Content-Type: application/json" \\\n  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\\n  -d '{"projectId":"${projectId}","tableName":"${activeTable.name}","values":{}}'`,
                      )
                    }
                  >
                    Copy cURL
                  </Button>
                }
              >
                <pre className="p-3 rounded-[var(--radius-control)] bg-surface-900 border border-line-strong text-content-primary font-mono text-micro overflow-x-auto leading-relaxed">
                  {`curl -X POST https://api.namines.com/api/gateway/create \\
  -H "Content-Type: application/json" \\
  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\
  -d '{
    "projectId": "${projectId}",
    "tableName": "${activeTable.name}",
    "values": {
      ${activeTable.columns
        .filter((c) => !c.isPK)
        .slice(0, 3)
        .map((c) => `"${c.name}": "sample_value"`)
        .join(',\n      ')}
    }
  }'`}
                </pre>
              </Card>

              {/* Endpoint 3: Update */}
              <Card
                title={`POST /api/gateway/update (${activeTable.name})`}
                description="Mutate an existing record identified by primary key."
                actions={
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={Copy}
                    onClick={() =>
                      triggerCopy(
                        'curl-update',
                        `curl -X POST https://api.namines.com/api/gateway/update \\\n  -H "Content-Type: application/json" \\\n  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\\n  -d '{"projectId":"${projectId}","tableName":"${activeTable.name}","pkColumn":"${pkCol?.name || 'id'}","pkValue":"1"}'`,
                      )
                    }
                  >
                    Copy cURL
                  </Button>
                }
              >
                <pre className="p-3 rounded-[var(--radius-control)] bg-surface-900 border border-line-strong text-content-primary font-mono text-micro overflow-x-auto leading-relaxed">
                  {`curl -X POST https://api.namines.com/api/gateway/update \\
  -H "Content-Type: application/json" \\
  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\
  -d '{
    "projectId": "${projectId}",
    "tableName": "${activeTable.name}",
    "pkColumn": "${pkCol?.name || 'id'}",
    "pkValue": "1",
    "values": { "status": "updated" }
  }'`}
                </pre>
              </Card>

              {/* Endpoint 4: Delete */}
              <Card
                title={`POST /api/gateway/delete (${activeTable.name})`}
                description="Remove an existing record by primary key."
                actions={
                  <Button
                    size="sm"
                    variant="ghost"
                    icon={Copy}
                    onClick={() =>
                      triggerCopy(
                        'curl-delete',
                        `curl -X POST https://api.namines.com/api/gateway/delete \\\n  -H "Content-Type: application/json" \\\n  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\\n  -d '{"projectId":"${projectId}","tableName":"${activeTable.name}","pkColumn":"${pkCol?.name || 'id'}","pkValue":"1"}'`,
                      )
                    }
                  >
                    Copy cURL
                  </Button>
                }
              >
                <pre className="p-3 rounded-[var(--radius-control)] bg-surface-900 border border-line-strong text-content-primary font-mono text-micro overflow-x-auto leading-relaxed">
                  {`curl -X POST https://api.namines.com/api/gateway/delete \\
  -H "Content-Type: application/json" \\
  -H "X-Namines-Key: <GATEWAY_API_KEY>" \\
  -d '{
    "projectId": "${projectId}",
    "tableName": "${activeTable.name}",
    "pkColumn": "${pkCol?.name || 'id'}",
    "pkValue": "1"
  }'`}
                </pre>
              </Card>
            </div>
          ) : (
            <Card>
              <EmptyState icon={Cable} title="No table selected" description="Select a table to view its endpoints." />
            </Card>
          )}
        </div>
      )}

      {/* ───────────────────────────────────────────────────────────────── */}
      {/* INSERT / EDIT RECORD MODAL                                        */}
      {/* ───────────────────────────────────────────────────────────────── */}
      {editModalOpen && activeTable && (
        <div
          role="dialog"
          aria-modal="true"
          aria-labelledby="record-modal-title"
          className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-surface-900/80 backdrop-blur-xs"
        >
          <div className="w-full max-w-lg rounded-[var(--radius-card)] border border-line-strong bg-surface-850 shadow-xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            <div className="flex items-center justify-between px-5 py-4 border-b border-line bg-surface-800">
              <div className="flex items-center gap-2">
                <Table2 className="w-4 h-4 text-content-primary" />
                <h3 id="record-modal-title" className="text-sm font-bold text-content-primary">
                  {editingRow ? `Edit Record #${pkCol ? editingRow[pkCol.name] : ''}` : `Insert Record into "${activeTable.name}"`}
                </h3>
              </div>
              <button
                type="button"
                onClick={() => setEditModalOpen(false)}
                className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <form onSubmit={handleSaveRecord} className="p-5 space-y-4 max-h-[70vh] overflow-y-auto">
              {activeTable.columns.map((col) => {
                const isPk = col.isPK;
                return (
                  <Field
                    key={col.id}
                    label={`${col.name} ${col.isNullable ? '(Optional)' : '(Required)'}`}
                    hint={`Type: ${col.type}${col.isPK ? ' · Primary Key' : ''}`}
                  >
                    <input
                      type="text"
                      disabled={isPk && Boolean(editingRow)}
                      value={rowDraft[col.name] ?? ''}
                      onChange={(e) => setRowDraft({ ...rowDraft, [col.name]: e.target.value })}
                      placeholder={`Enter ${col.name}…`}
                      className={inputClass}
                      required={!col.isNullable && !isPk}
                    />
                  </Field>
                );
              })}

              <div className="flex items-center justify-end gap-2 pt-3 border-t border-line">
                <Button variant="secondary" onClick={() => setEditModalOpen(false)}>
                  Cancel
                </Button>
                <Button type="submit" variant="primary">
                  {editingRow ? 'Save Changes' : 'Insert Record'}
                </Button>
              </div>
            </form>
          </div>
        </div>
      )}
    </PageShell>
  );
}
