'use client';

import React, { useState, useEffect, useRef } from 'react';
import { Terminal, Play, X, RefreshCw, Database, AlertCircle, CheckCircle, Table2, Copy, Check, RotateCcw } from 'lucide-react';
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
  const [copied, setCopied] = useState(false);

  // Syncing database state
  const [isSyncing, setIsSyncing] = useState(false);
  const [syncError, setSyncError] = useState<string | null>(null);
  const [syncSuccess, setSyncSuccess] = useState(false);

  const containerRef = useRef<HTMLDivElement>(null);

  // Synchronize and seed the WebAssembly DB with DDL + Mock Data
  const syncAndSeedDb = async () => {
    if (!schema || schema.tables.length === 0) return;
    if (!checkAccess("Live Database Seeding")) return;
    setIsSyncing(true);
    setSyncError(null);
    setSyncSuccess(false);

    try {
      // 1. Reset/Initialize SQLite Wasm DB
      await sqliteService.initDb();

      // 2. Compile schema tables to SQLite DDL syntax
      const ddl = await schemaService.compileSql(schema, 'SQLite');

      // 3. Apply DDL scripts
      await sqliteService.executeScript(ddl);

      // 4. Call smart seeding API for mock data
      const seedRes = await smartSeedService.generate(schema, 'SQLite', '', 20);
      
      // 5. Apply seed data scripts
      if (seedRes && seedRes.sqlScript) {
        await sqliteService.executeScript(seedRes.sqlScript);
      }

      // Save database state binary to IndexedDB
      await sqliteService.saveToIndexedDb();

      setSyncSuccess(true);

      // Set default query in editor if empty or generic
      const firstTableName = schema.tables[0]?.name || 'Table';
      setSqlQuery(`SELECT * FROM ${firstTableName} LIMIT 10;`);

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
            // Set default query if empty
            if (!sqlQuery.trim()) {
              const firstTableName = schema.tables[0]?.name || 'Table';
              setSqlQuery(`SELECT * FROM ${firstTableName} LIMIT 10;`);
            }
          } else {
            await syncAndSeedDb();
          }
        } catch (err) {
          console.error("Failed to restore DB, fallback to seed:", err);
          await syncAndSeedDb();
        } finally {
          setIsSyncing(false);
        }
      }
    };
    initOrRestoreDb();
  }, [isOpen, schema]);

  // Periodic saving and page unload/visibility save listeners
  useEffect(() => {
    if (!isOpen) return;

    // Periodic save every 10 seconds
    const interval = setInterval(() => {
      if (sqliteService.isActive()) {
        sqliteService.saveToIndexedDb();
      }
    }, 10000);

    // Save on unload or tab switch/backgrounding
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

  const handleRunQuery = async () => {
    if (!sqlQuery.trim()) return;

    setExecuting(true);
    setQueryError(null);
    setQueryResult(null);
    const start = performance.now();

    try {
      const res = await sqliteService.executeQuery(sqlQuery);
      setQueryResult(res);
      setExecDuration(Math.round(performance.now() - start));
      
      // Save changes to IndexedDB after executing a query
      await sqliteService.saveToIndexedDb();
    } catch (err) {
      setQueryError(errorMessage(err, 'An unknown SQLite error occurred while executing the query.'));
    } finally {
      setExecuting(false);
    }
  };

  const handleCopyJson = () => {
    if (!queryResult || !queryResult.rows) return;
    navigator.clipboard.writeText(JSON.stringify(queryResult.rows, null, 2));
    setCopied(true);
    showToast('Result set copied as JSON.', 'success');
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div
      ref={containerRef}
      className="fixed bottom-[104px] left-3 right-3 md:left-[88px] md:right-[248px] z-[49] h-[min(360px,calc(100vh-160px))] bg-surface-900/98 border border-line-strong shadow-2xl flex flex-col overflow-hidden animate-in slide-in-from-bottom duration-250 font-sans rounded-[var(--radius-modal)] backdrop-blur-2xl"
    >
      {/* Panel Header — Shopify Polaris Console Bar */}
      <div className="flex items-center justify-between px-3.5 py-2 border-b border-line bg-surface-800/80 select-none shrink-0 gap-2">
        <div className="flex items-center gap-2.5 min-w-0">
          <div className="w-6 h-6 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong flex items-center justify-center text-content-primary shrink-0">
            <Terminal className="w-3.5 h-3.5" />
          </div>
          <span className="text-xs font-bold text-content-primary">SQL Console</span>

          {/* Active status indicator badge */}
          {sqliteService.isActive() && !isSyncing ? (
            <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-success-subtle border border-success/30 text-micro font-bold text-success-text shrink-0">
              <span className="w-1.5 h-1.5 rounded-full bg-success shrink-0 animate-pulse" />
              <span>Live DB Active</span>
            </span>
          ) : (
            <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-surface-700 border border-line text-micro font-medium text-content-muted shrink-0">
              <span>Live DB Inactive</span>
            </span>
          )}

          {/* Quick Table Selector Chips */}
          {schema && schema.tables.length > 0 && (
            <div className="hidden sm:flex items-center gap-1 overflow-x-auto scrollbar-none ml-2">
              {schema.tables.slice(0, 5).map(t => (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => {
                    const q = `SELECT * FROM ${t.name} LIMIT 10;`;
                    setSqlQuery(q);
                  }}
                  className="px-2 py-0.5 text-micro font-mono bg-surface-700/60 hover:bg-surface-700 text-content-secondary hover:text-content-primary rounded-[var(--radius-control)] border border-line transition-colors truncate max-w-[120px] cursor-pointer"
                  title={`Query ${t.name}`}
                >
                  {t.name}
                </button>
              ))}
            </div>
          )}
        </div>

        {/* Action controls */}
        <div className="flex items-center gap-1.5 shrink-0">
          <button
            onClick={syncAndSeedDb}
            disabled={isSyncing}
            className="flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] text-micro font-bold text-content-secondary hover:text-content-primary bg-surface-700 hover:bg-surface-600 border border-line hover:border-line-strong transition-all disabled:opacity-50 cursor-pointer shadow-xs"
            title="Reload diagram schema and mock data into local database"
          >
            <RefreshCw className={`w-3 h-3 ${isSyncing ? 'animate-spin' : ''}`} />
            <span>{isSyncing ? 'Setting up…' : 'Reset & Seed'}</span>
          </button>

          <button
            onClick={() => setIsOpen(false)}
            className="p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700/80 transition-colors cursor-pointer"
            aria-label="Close SQL Console"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Main Panel Content Area */}
      <div className="flex-1 flex overflow-hidden">

        {/* Left Side: Tabular Results Grid */}
        <div className="flex-1 flex flex-col overflow-hidden bg-surface-900/40">

          {/* Status/Errors inside Result Section */}
          {syncError && (
            <div className="m-2.5 bg-danger-subtle border border-danger/30 rounded-[var(--radius-control)] p-2.5 flex gap-2 items-start shrink-0">
              <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
              <div>
                <span className="text-danger-text text-xs font-bold block mb-0.5">Setup Error</span>
                <p className="text-content-muted text-micro leading-relaxed font-mono">{syncError}</p>
              </div>
            </div>
          )}

          {syncSuccess && (
            <div className="m-2.5 bg-success-subtle border border-success/30 rounded-[var(--radius-control)] p-2.5 flex gap-2 items-start shrink-0">
              <CheckCircle className="w-4 h-4 text-success-text mt-0.5 shrink-0" />
              <div>
                <span className="text-success-text text-xs font-bold block mb-0.5">Sync Successful</span>
                <p className="text-content-secondary text-micro leading-relaxed">Tables created and mock data seeded. Ready to query.</p>
              </div>
            </div>
          )}

          {queryError && (
            <div className="m-2.5 bg-danger-subtle border border-danger/30 rounded-[var(--radius-control)] p-2.5 flex gap-2 items-start shrink-0">
              <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
              <div className="min-w-0 flex-1">
                <span className="text-danger-text text-xs font-bold block mb-0.5">Runtime Error</span>
                <p className="text-danger-text text-micro font-mono mt-0.5 leading-relaxed bg-surface-900/60 p-2 rounded-[var(--radius-control)] overflow-x-auto">{queryError}</p>
              </div>
            </div>
          )}

          {/* Grid / Content Rendering */}
          <div className="flex-1 overflow-auto p-2.5">
            {isSyncing ? (
              <div className="h-full flex flex-col items-center justify-center gap-2.5">
                <RefreshCw className="w-5 h-5 text-content-primary animate-spin" />
                <div className="text-center">
                  <span className="text-content-primary text-xs font-bold block">Setting up Database…</span>
                  <span className="text-content-muted text-micro mt-0.5 block">Loading SQLite Wasm engine and generating mock rows.</span>
                </div>
              </div>
            ) : queryResult ? (
              queryResult.isSelect ? (
                queryResult.rows.length === 0 ? (
                  <div className="h-full flex flex-col items-center justify-center gap-2 select-none">
                    <Table2 className="w-5 h-5 text-content-muted" />
                    <div className="text-center">
                      <span className="text-content-primary text-xs font-bold block">Empty Result Set</span>
                      <span className="text-content-muted text-micro mt-0.5 block">Query executed successfully but matched 0 rows.</span>
                    </div>
                  </div>
                ) : (
                  <div className="border border-line rounded-[var(--radius-card)] overflow-hidden bg-surface-800/60 shadow-xs">
                    <div className="px-3 py-1.5 border-b border-line bg-surface-700/50 flex items-center justify-between text-micro text-content-muted font-mono">
                      <div className="flex items-center gap-3">
                        <span className="font-semibold text-content-primary">{queryResult.rows.length} row(s)</span>
                        <span>{queryResult.columns.length} columns</span>
                        {execDuration !== null && <span className="text-content-subtle">· {execDuration}ms</span>}
                      </div>
                      <button
                        type="button"
                        onClick={handleCopyJson}
                        className="flex items-center gap-1 text-micro text-content-muted hover:text-content-primary transition-colors cursor-pointer"
                        title="Copy as JSON"
                      >
                        {copied ? <Check className="w-3 h-3 text-success-text" /> : <Copy className="w-3 h-3" />}
                        <span>{copied ? 'Copied' : 'JSON'}</span>
                      </button>
                    </div>
                    <table className="w-full text-left text-xs border-collapse">
                      <thead>
                        <tr className="bg-surface-700/80 border-b border-line text-[10px] font-bold text-content-muted uppercase tracking-wider">
                          <th className="px-2 py-2 w-8 text-center text-content-subtle font-mono">#</th>
                          {queryResult.columns.map((col) => (
                            <th key={col} className="px-3 py-2 font-bold">{col}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-line text-content-secondary font-mono text-micro">
                        {queryResult.rows.map((row, idx) => (
                          <tr key={idx} className="hover:bg-surface-700/40 transition-colors">
                            <td className="px-2 py-1.5 text-center text-content-subtle text-[10px]">{idx + 1}</td>
                            {queryResult.columns.map((col) => (
                              <td key={col} className="px-3 py-1.5 truncate max-w-[220px]">
                                {row[col] === null ? (
                                  <span className="text-content-subtle italic text-[10px] px-1 py-0.5 rounded-[var(--radius-control)] bg-surface-700/60">null</span>
                                ) : typeof row[col] === 'boolean' ? (
                                  <span className={row[col] ? 'text-success-text font-bold' : 'text-danger-text font-bold'}>{row[col] ? 'true' : 'false'}</span>
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
                )
              ) : (
                <div className="h-full flex flex-col items-center justify-center gap-2 select-none">
                  <CheckCircle className="w-5 h-5 text-success-text" />
                  <div className="text-center">
                    <span className="text-content-primary text-xs font-bold block">Operation Successful</span>
                    <span className="text-success-text font-mono text-xs mt-1 block">{queryResult.message}</span>
                  </div>
                </div>
              )
            ) : !queryError && !syncError ? (
              <div className="h-full flex flex-col items-center justify-center gap-2.5 select-none">
                <Database className="w-5 h-5 text-content-muted" />
                <div className="text-center max-w-sm">
                  <span className="text-content-primary text-xs font-bold block">Ready to Explore</span>
                  <span className="text-content-muted text-micro mt-0.5 leading-relaxed block">
                    Write a SQL query in the editor or click any table chip above to query live test rows.
                  </span>
                </div>
              </div>
            ) : null}
          </div>
        </div>

        {/* Right Side: SQL Input Editor Area */}
        <div className="w-[38%] border-l border-line flex flex-col p-3 gap-2 shrink-0 bg-surface-800/30">
          <div className="flex justify-between items-center select-none">
            <span className="text-[10px] font-bold text-content-muted uppercase tracking-wider">SQL Query</span>
            <div className="flex items-center gap-1.5">
              <span className="text-[10px] font-mono text-content-subtle bg-surface-700/80 px-1.5 py-0.5 rounded-[var(--radius-control)] border border-line">Ctrl + ↵</span>
              <button
                type="button"
                onClick={() => setSqlQuery('')}
                className="text-micro text-content-muted hover:text-content-primary cursor-pointer p-0.5"
                title="Clear query"
              >
                <RotateCcw className="w-3 h-3" />
              </button>
            </div>
          </div>

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
            placeholder="SELECT * FROM Users LIMIT 10;"
            className="flex-1 font-mono text-xs text-content-primary bg-surface-800/80 border border-line focus:border-line-strong rounded-[var(--radius-control)] p-2.5 focus:outline-none resize-none leading-relaxed transition-all"
            spellCheck={false}
          />

          <button
            onClick={handleRunQuery}
            disabled={executing || isSyncing || !sqlQuery.trim()}
            className="flex items-center justify-center gap-2 w-full py-2 rounded-[var(--radius-control)] font-bold text-xs text-surface-900 bg-content-primary hover:bg-content-secondary transition-all disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer shadow-sm"
          >
            {executing ? (
              <>
                <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                <span>Executing…</span>
              </>
            ) : (
              <>
                <Play className="w-3.5 h-3.5 fill-current" />
                <span>Execute Query</span>
              </>
            )}
          </button>
        </div>

      </div>
    </div>
  );
}
