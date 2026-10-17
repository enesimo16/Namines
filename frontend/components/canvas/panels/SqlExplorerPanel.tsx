'use client';

import React, { useState, useEffect, useRef } from 'react';
import {
  Terminal,
  Play,
  X,
  RefreshCw,
  Database,
  AlertCircle,
  CheckCircle,
  Table2,
  Copy,
  Check,
  RotateCcw,
  Sparkles,
} from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useSqlExplorerStore } from '../../../store/useSqlExplorerStore';
import { useAIGateway } from '../../../hooks/useAIGateway';
import { sqliteService, SqlQueryResult } from '../../../services/sqliteService';
import { schemaService, smartSeedService } from '../../../services/api';
import { apiErrorStatus, errorMessage } from '../../../lib/errors';
import { useToastStore } from '../../../store/useToastStore';

export default function SqlExplorerPanel() {
  const { schema } = useSchemaStore();
  const { isOpen, setIsOpen } = useSqlExplorerStore();
  const { checkAccess } = useAIGateway();
  const showToast = useToastStore(state => state.showToast);

  const [sqlQuery, setSqlQuery] = useState<string>('');
  const [queryResult, setQueryResult] = useState<SqlQueryResult | null>(null);
  const [queryError, setQueryError] = useState<string | null>(null);
  const [executing, setExecuting] = useState(false);
  const [execDuration, setExecDuration] = useState<number | null>(null);
  const [copied, setCopied] = useState<'json' | 'csv' | null>(null);

  const [isSyncing, setIsSyncing] = useState(false);
  const [syncError, setSyncError] = useState<string | null>(null);
  const [syncSuccess, setSyncSuccess] = useState(false);

  const containerRef = useRef<HTMLDivElement>(null);

  const executeSqlString = async (queryText: string) => {
    if (!queryText.trim()) return;
    setExecuting(true);
    setQueryError(null);
    setQueryResult(null);
    const start = performance.now();

    try {
      const res = await sqliteService.executeQuery(queryText);
      setQueryResult(res);
      setExecDuration(Math.round(performance.now() - start));
      await sqliteService.saveToIndexedDb();
    } catch (err) {
      setQueryError(errorMessage(err, 'An unknown SQLite error occurred while executing the query.'));
    } finally {
      setExecuting(false);
    }
  };

  // Synchronize and seed the WebAssembly DB with DDL + Mock Data
  const syncAndSeedDb = async () => {
    if (!schema || schema.tables.length === 0) return;
    if (!checkAccess('Live Database Seeding')) return;
    setIsSyncing(true);
    setSyncError(null);
    setSyncSuccess(false);

    try {
      await sqliteService.initDb();
      const ddl = await schemaService.compileSql(schema, 'SQLite');
      await sqliteService.executeScript(ddl);

      const seedRes = await smartSeedService.generate(schema, 'SQLite', '', 20);
      if (seedRes && seedRes.sqlScript) {
        await sqliteService.executeScript(seedRes.sqlScript);
      }

      await sqliteService.saveToIndexedDb();
      setSyncSuccess(true);

      const firstTableName = schema.tables[0]?.name || 'Table';
      const defaultQuery = `SELECT * FROM "${firstTableName}" LIMIT 25;`;
      setSqlQuery(defaultQuery);
      await executeSqlString(defaultQuery);

      setTimeout(() => setSyncSuccess(false), 3000);
    } catch (err) {
      if (apiErrorStatus(err) === 429) {
        setSyncError('Daily AI Limit Reached: Please upgrade your plan for unlimited seeding.');
      } else {
        setSyncError(errorMessage(err, 'Error occurred while setting up live database.'));
      }
    } finally {
      setIsSyncing(false);
    }
  };

  // Auto-sync or restore database when panel is opened
  useEffect(() => {
    const initOrRestoreDb = async () => {
      if (isOpen && schema && schema.tables.length > 0 && !sqliteService.isActive()) {
        setIsSyncing(true);
        try {
          const restored = await sqliteService.loadFromIndexedDb();
          if (restored) {
            if (!sqlQuery.trim()) {
              const firstTableName = schema.tables[0]?.name || 'Table';
              const q = `SELECT * FROM "${firstTableName}" LIMIT 25;`;
              setSqlQuery(q);
              await executeSqlString(q);
            }
          } else {
            await syncAndSeedDb();
          }
        } catch (err) {
          console.error('Failed to restore DB, fallback to seed:', err);
          await syncAndSeedDb();
        } finally {
          setIsSyncing(false);
        }
      }
    };
    initOrRestoreDb();
  }, [isOpen, schema]);

  // Escape key closes drawer
  useEffect(() => {
    if (!isOpen) return;
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setIsOpen(false);
    };
    window.addEventListener('keydown', onKeyDown);
    return () => window.removeEventListener('keydown', onKeyDown);
  }, [isOpen, setIsOpen]);

  // Periodic saving and page unload/visibility save listeners
  useEffect(() => {
    if (!isOpen) return;

    const interval = setInterval(() => {
      if (sqliteService.isActive()) {
        sqliteService.saveToIndexedDb();
      }
    }, 10000);

    const handleSave = () => {
      if (sqliteService.isActive()) {
        sqliteService.saveToIndexedDb();
      }
    };

    window.addEventListener('beforeunload', handleSave);
    window.addEventListener('pagehide', handleSave);
    document.addEventListener('visibilitychange', handleSave);

    return () => {
      clearInterval(interval);
      window.removeEventListener('beforeunload', handleSave);
      window.removeEventListener('pagehide', handleSave);
      document.removeEventListener('visibilitychange', handleSave);
    };
  }, [isOpen]);

  if (!isOpen) return null;

  const handleRunQuery = () => executeSqlString(sqlQuery);

  const handleSelectTableChip = (tableName: string) => {
    const q = `SELECT * FROM "${tableName}" LIMIT 25;`;
    setSqlQuery(q);
    if (sqliteService.isActive() && !isSyncing) {
      void executeSqlString(q);
    }
  };

  const handleCopyJson = () => {
    if (!queryResult || !queryResult.rows) return;
    navigator.clipboard.writeText(JSON.stringify(queryResult.rows, null, 2));
    setCopied('json');
    showToast('Result set copied as JSON.', 'success');
    setTimeout(() => setCopied(null), 2000);
  };

  const handleCopyCsv = () => {
    if (!queryResult || !queryResult.rows || queryResult.columns.length === 0) return;
    const escapeCsv = (val: unknown) => {
      if (val === null || val === undefined) return '';
      const str = String(val);
      return str.includes(',') || str.includes('"') || str.includes('\n')
        ? `"${str.replace(/"/g, '""')}"`
        : str;
    };
    const header = queryResult.columns.map(escapeCsv).join(',');
    const lines = queryResult.rows.map(r => queryResult.columns.map(c => escapeCsv(r[c])).join(','));
    navigator.clipboard.writeText([header, ...lines].join('\n'));
    setCopied('csv');
    showToast('Result set copied as CSV.', 'success');
    setTimeout(() => setCopied(null), 2000);
  };

  return (
    <>
      {/* Mobile / Tablet Backdrop */}
      <div
        className="fixed inset-0 top-14 bg-scrim/45 backdrop-blur-[1px] z-[64] lg:hidden"
        onClick={() => setIsOpen(false)}
        aria-hidden="true"
      />

      {/* Slide-Over Right Drawer — Shopify Polaris Console */}
      <aside
        ref={containerRef}
        aria-label="SQL Console"
        className="fixed top-14 right-0 bottom-0 z-[65] w-full sm:w-[480px] lg:w-[540px] max-w-[100vw] bg-surface-900 border-l border-line-strong shadow-2xl flex flex-col overflow-hidden animate-in slide-in-from-right duration-200 font-sans"
      >
        {/* 1. Polaris Drawer Header */}
        <div className="flex items-center justify-between px-4 py-3 border-b border-line bg-surface-800/90 select-none shrink-0 gap-2">
          <div className="flex items-center gap-2.5 min-w-0">
            <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong flex items-center justify-center text-content-primary shrink-0 shadow-xs">
              <Terminal className="w-4 h-4 text-namines-teal-vibrant" />
            </div>
            <div className="min-w-0">
              <div className="flex items-center gap-2">
                <h2 className="text-xs font-bold uppercase tracking-wider text-content-primary truncate">
                  SQL Console
                </h2>
                {sqliteService.isActive() && !isSyncing ? (
                  <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-success-subtle border border-success/30 text-[10px] font-mono font-bold text-success-text shrink-0">
                    <span className="w-1.5 h-1.5 rounded-full bg-success shrink-0 animate-pulse" />
                    <span>Live · {schema?.tables.length ?? 0} tables</span>
                  </span>
                ) : (
                  <span className="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-surface-700 border border-line text-[10px] font-mono font-medium text-content-muted shrink-0">
                    <span>{isSyncing ? 'Seeding…' : 'Standby'}</span>
                  </span>
                )}
              </div>
              <p className="text-[11px] text-content-muted truncate">
                In-memory SQLite WASM sandbox with AI mock data
              </p>
            </div>
          </div>

          <div className="flex items-center gap-1.5 shrink-0">
            <button
              onClick={syncAndSeedDb}
              disabled={isSyncing}
              className="flex items-center gap-1.5 h-8 px-2.5 rounded-[var(--radius-control)] text-xs font-semibold text-content-primary bg-surface-700 hover:bg-surface-600 border border-line-strong transition-all disabled:opacity-50 cursor-pointer shadow-xs"
              title="Rebuild tables and seed fresh mock rows"
            >
              <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? 'animate-spin' : ''}`} />
              <span className="hidden sm:inline">{isSyncing ? 'Seeding…' : 'Reset & Seed'}</span>
            </button>

            <button
              onClick={() => setIsOpen(false)}
              className="w-8 h-8 flex items-center justify-center rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
              aria-label="Close SQL Console"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        </div>

        {/* 2. Quick Table Explorer Strip */}
        {schema && schema.tables.length > 0 && (
          <div className="px-4 py-2 bg-surface-800/40 border-b border-line flex items-center gap-1.5 overflow-x-auto scrollbar-none shrink-0">
            <span className="text-[10px] font-bold uppercase tracking-wider text-content-muted mr-1 shrink-0">
              Tables
            </span>
            {schema.tables.map(t => {
              const isSelected = sqlQuery.includes(`"${t.name}"`) || sqlQuery.includes(` ${t.name} `);
              return (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => handleSelectTableChip(t.name)}
                  className={`px-2.5 py-1 text-[11px] font-mono rounded-[var(--radius-control)] border transition-all shrink-0 cursor-pointer ${
                    isSelected
                      ? 'bg-surface-700 text-content-primary border-line-strong font-semibold shadow-xs'
                      : 'bg-surface-800/70 hover:bg-surface-700/70 text-content-secondary hover:text-content-primary border-line'
                  }`}
                  title={`Query ${t.name}`}
                >
                  {t.name}
                </button>
              );
            })}
          </div>
        )}

        {/* 3. Polaris Framed Query Editor Card */}
        <div className="p-3.5 border-b border-line bg-surface-800/20 shrink-0">
          <div className="rounded-[var(--radius-card)] border border-line-strong bg-surface-900 overflow-hidden shadow-xs focus-within:border-focus-ring transition-colors">
            {/* Editor Card Top Bar */}
            <div className="px-3 py-1.5 bg-surface-800 border-b border-line flex items-center justify-between select-none">
              <div className="flex items-center gap-2">
                <span className="text-[10px] font-bold text-content-muted uppercase tracking-wider">
                  Query Editor
                </span>
                <span className="text-[10px] font-mono text-content-subtle bg-surface-700/80 px-1.5 py-0.5 rounded-[var(--radius-control)] border border-line">
                  Ctrl + ↵
                </span>
              </div>
              <div className="flex items-center gap-2">
                <button
                  type="button"
                  onClick={() => {
                    const q = `SELECT name, type FROM sqlite_master WHERE type='table' ORDER BY name;`;
                    setSqlQuery(q);
                    if (sqliteService.isActive()) void executeSqlString(q);
                  }}
                  className="text-[11px] font-medium text-content-muted hover:text-content-primary transition-colors cursor-pointer"
                >
                  Schema tables
                </button>
                <span className="text-line-strong">·</span>
                <button
                  type="button"
                  onClick={() => setSqlQuery('')}
                  className="inline-flex items-center gap-1 text-[11px] font-medium text-content-muted hover:text-content-primary transition-colors cursor-pointer"
                  title="Clear query"
                >
                  <RotateCcw className="w-3 h-3" />
                  <span>Clear</span>
                </button>
              </div>
            </div>

            {/* SQL Textarea */}
            <textarea
              aria-label="SQL Query"
              value={sqlQuery}
              onChange={(e) => setSqlQuery(e.target.value)}
              onKeyDown={(e) => {
                if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
                  e.preventDefault();
                  handleRunQuery();
                }
              }}
              placeholder='SELECT * FROM "users" LIMIT 25;'
              rows={4}
              className="w-full h-28 sm:h-32 font-mono text-xs text-content-primary bg-surface-900 p-3 focus:outline-none resize-y leading-relaxed"
              spellCheck={false}
            />

            {/* Editor Card Bottom Action Bar */}
            <div className="px-3 py-2 bg-surface-800/70 border-t border-line flex items-center justify-between gap-2">
              <span className="text-[11px] font-mono text-content-muted truncate">
                SQLite 3 · {schema?.name || 'Active Schema'}
              </span>
              <button
                onClick={handleRunQuery}
                disabled={executing || isSyncing || !sqlQuery.trim()}
                className="inline-flex items-center justify-center gap-1.5 h-8 px-3.5 rounded-[var(--radius-control)] font-bold text-xs text-surface-900 bg-content-primary hover:bg-content-secondary transition-all disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer shadow-xs shrink-0"
              >
                {executing ? (
                  <>
                    <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                    <span>Running…</span>
                  </>
                ) : (
                  <>
                    <Play className="w-3.5 h-3.5 fill-current" />
                    <span>Run Query</span>
                  </>
                )}
              </button>
            </div>
          </div>
        </div>

        {/* 4. Status Alerts & Results Grid */}
        <div className="flex-1 min-h-0 flex flex-col overflow-hidden bg-surface-900">
          {syncError && (
            <div className="mx-3.5 mt-3 bg-danger-subtle border border-danger/30 rounded-[var(--radius-card)] p-3 flex gap-2.5 items-start shrink-0">
              <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
              <div className="min-w-0 flex-1">
                <span className="text-danger-text text-xs font-bold block mb-0.5">Sandbox Setup Error</span>
                <p className="text-content-secondary text-micro leading-relaxed font-mono break-words">{syncError}</p>
              </div>
            </div>
          )}

          {syncSuccess && (
            <div className="mx-3.5 mt-3 bg-success-subtle border border-success/30 rounded-[var(--radius-card)] p-2.5 flex gap-2 items-center shrink-0">
              <CheckCircle className="w-4 h-4 text-success-text shrink-0" />
              <span className="text-success-text text-xs font-semibold">
                Tables created and seeded with AI mock records.
              </span>
            </div>
          )}

          {queryError && (
            <div className="mx-3.5 mt-3 bg-danger-subtle border border-danger/30 rounded-[var(--radius-card)] p-3 flex gap-2.5 items-start shrink-0">
              <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
              <div className="min-w-0 flex-1">
                <span className="text-danger-text text-xs font-bold block mb-1">Query Execution Error</span>
                <p className="text-danger-text text-micro font-mono leading-relaxed bg-surface-900/70 p-2 rounded-[var(--radius-control)] overflow-x-auto">
                  {queryError}
                </p>
              </div>
            </div>
          )}

          {/* Results Body */}
          <div className="flex-1 min-h-0 flex flex-col overflow-hidden p-3.5">
            {isSyncing ? (
              <div className="h-full flex flex-col items-center justify-center gap-3 rounded-[var(--radius-card)] border border-line bg-surface-800/30 p-6">
                <RefreshCw className="w-5 h-5 text-namines-teal-vibrant animate-spin" />
                <div className="text-center">
                  <span className="text-content-primary text-xs font-bold block">Provisioning In-Memory SQLite…</span>
                  <span className="text-content-muted text-micro mt-1 block">
                    Compiling DDL and seeding realistic rows for {schema?.tables.length ?? 0} tables.
                  </span>
                </div>
              </div>
            ) : queryResult ? (
              queryResult.isSelect ? (
                queryResult.rows.length === 0 ? (
                  <div className="h-full flex flex-col items-center justify-center gap-2 rounded-[var(--radius-card)] border border-line bg-surface-800/30 p-6 select-none">
                    <Table2 className="w-5 h-5 text-content-muted" />
                    <div className="text-center">
                      <span className="text-content-primary text-xs font-bold block">0 rows returned</span>
                      <span className="text-content-muted text-micro mt-0.5 block">
                        Query executed cleanly ({execDuration ?? 0}ms) with an empty result set.
                      </span>
                    </div>
                  </div>
                ) : (
                  <div className="flex-1 min-h-0 flex flex-col border border-line-strong rounded-[var(--radius-card)] overflow-hidden bg-surface-800/50 shadow-xs">
                    {/* Result Bar */}
                    <div className="px-3 py-2 border-b border-line bg-surface-800 flex items-center justify-between gap-2 text-micro text-content-muted font-mono shrink-0">
                      <div className="flex items-center gap-2.5">
                        <span className="font-bold text-content-primary">
                          {queryResult.rows.length} {queryResult.rows.length === 1 ? 'row' : 'rows'}
                        </span>
                        <span>·</span>
                        <span>{queryResult.columns.length} cols</span>
                        {execDuration !== null && (
                          <>
                            <span>·</span>
                            <span className="text-content-subtle">{execDuration}ms</span>
                          </>
                        )}
                      </div>
                      <div className="flex items-center gap-1.5">
                        <button
                          type="button"
                          onClick={handleCopyCsv}
                          className="inline-flex items-center gap-1 px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 border border-line text-[10px] font-semibold text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
                          title="Copy result set as CSV"
                        >
                          {copied === 'csv' ? <Check className="w-3 h-3 text-success-text" /> : <Copy className="w-3 h-3" />}
                          <span>{copied === 'csv' ? 'Copied' : 'CSV'}</span>
                        </button>
                        <button
                          type="button"
                          onClick={handleCopyJson}
                          className="inline-flex items-center gap-1 px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 border border-line text-[10px] font-semibold text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
                          title="Copy result set as JSON"
                        >
                          {copied === 'json' ? <Check className="w-3 h-3 text-success-text" /> : <Copy className="w-3 h-3" />}
                          <span>{copied === 'json' ? 'Copied' : 'JSON'}</span>
                        </button>
                      </div>
                    </div>

                    {/* Scrollable Table */}
                    <div className="flex-1 min-h-0 overflow-auto">
                      <table className="w-full text-left text-xs border-collapse">
                        <thead className="sticky top-0 z-10 bg-surface-800 border-b border-line-strong">
                          <tr className="text-[10px] font-bold text-content-muted uppercase tracking-wider">
                            <th className="px-2.5 py-2 w-9 text-center text-content-subtle font-mono border-r border-line/60">#</th>
                            {queryResult.columns.map((col) => (
                              <th key={col} className="px-3 py-2 font-bold whitespace-nowrap">{col}</th>
                            ))}
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-line text-content-primary font-mono text-[11px]">
                          {queryResult.rows.map((row, idx) => (
                            <tr key={idx} className="hover:bg-surface-700/50 transition-colors">
                              <td className="px-2.5 py-1.5 text-center text-content-subtle text-[10px] border-r border-line/40 select-none">
                                {idx + 1}
                              </td>
                              {queryResult.columns.map((col) => (
                                <td key={col} className="px-3 py-1.5 whitespace-nowrap max-w-[240px] truncate">
                                  {row[col] === null ? (
                                    <span className="text-content-subtle italic text-[10px] px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-700/70">
                                      NULL
                                    </span>
                                  ) : typeof row[col] === 'boolean' ? (
                                    <span className={row[col] ? 'text-success-text font-bold' : 'text-danger-text font-bold'}>
                                      {row[col] ? 'true' : 'false'}
                                    </span>
                                  ) : (
                                    String(row[col])
                                  )}
                                </td>
                              ))}
                            </tr>
                          ))}
                        </tbody>
                      </table>
                    </div>
                  </div>
                )
              ) : (
                <div className="h-full flex flex-col items-center justify-center gap-2 rounded-[var(--radius-card)] border border-line bg-surface-800/30 p-6 select-none">
                  <CheckCircle className="w-5 h-5 text-success-text" />
                  <div className="text-center">
                    <span className="text-content-primary text-xs font-bold block">Statement Executed</span>
                    <span className="text-success-text font-mono text-xs mt-1 block">{queryResult.message}</span>
                  </div>
                </div>
              )
            ) : !queryError && !syncError ? (
              <div className="h-full flex flex-col items-center justify-center gap-3 rounded-[var(--radius-card)] border border-line bg-surface-800/30 p-6 select-none">
                <div className="w-10 h-10 rounded-full bg-surface-800 border border-line-strong flex items-center justify-center text-content-muted">
                  <Sparkles className="w-4 h-4 text-namines-teal-vibrant" />
                </div>
                <div className="text-center max-w-xs">
                  <span className="text-content-primary text-xs font-bold block">Ready to Query</span>
                  <span className="text-content-muted text-micro mt-1 leading-relaxed block">
                    Click any table pill above to inspect rows immediately, or run custom SQL statements in the editor.
                  </span>
                </div>
              </div>
            ) : null}
          </div>
        </div>
      </aside>
    </>
  );
}
