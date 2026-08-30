'use client';

import React, { useState, useEffect, useRef } from 'react';
import { Terminal, Play, X, RefreshCw, Database, AlertCircle, CheckCircle, Table2 } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useSqlExplorerStore } from '../../../store/useSqlExplorerStore';
import { useAIGateway } from '../../../hooks/useAIGateway';
import { sqliteService, SqlQueryResult } from '../../../services/sqliteService';
import { schemaService, smartSeedService } from '../../../services/api';

export default function SqlExplorerPanel() {
  const { schema } = useSchemaStore();
  const { isOpen, setIsOpen } = useSqlExplorerStore();
  const { checkAccess } = useAIGateway();

  const [sqlQuery, setSqlQuery] = useState<string>('');
  const [queryResult, setQueryResult] = useState<SqlQueryResult | null>(null);
  const [queryError, setQueryError] = useState<string | null>(null);
  const [executing, setExecuting] = useState(false);

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
    } catch (err: any) {
      if (err?.response?.status === 429) {
        setSyncError('Daily AI Limit Reached: Please upgrade your plan for unlimited seeding.');
      } else {
        setSyncError(err.message || 'Error occurred while setting up live database.');
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

    try {
      const res = await sqliteService.executeQuery(sqlQuery);
      setQueryResult(res);
      
      // Save changes to IndexedDB after executing a query
      await sqliteService.saveToIndexedDb();
    } catch (err: any) {
      setQueryError(err.message || 'An unknown SQLite error occurred while executing the query.');
    } finally {
      setExecuting(false);
    }
  };

  return (
    <div
      ref={containerRef}
      className="fixed bottom-6 left-[12%] right-[12%] z-[49] h-[300px] bg-surface-800 border border-content-primary/12 shadow-[0_10px_50px_color-mix(in srgb, var(--color-scrim) 50%, transparent)] flex flex-col overflow-hidden animate-in slide-in-from-bottom duration-300 font-sans rounded-2xl"
    >
      {/* Panel Header */}
      <div className="flex items-center justify-between px-5 py-2.5 border-b border-content-primary/10 select-none shrink-0">
        <div className="flex items-center gap-2.5">
          <Terminal className="w-4 h-4 text-accent-text" />
          <span className="text-xs font-bold tracking-wider text-content-primary uppercase">SQL Console</span>

          {/* Active status indicator badge */}
          {sqliteService.isActive() && !isSyncing ? (
            <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-success-subtle border border-success/20 text-[10px] font-medium text-success-text">
              <span className="w-1.5 h-1.5 rounded-full bg-success shrink-0" />
              <span>Live DB Active</span>
            </span>
          ) : (
            <span className="flex items-center gap-1.5 px-2 py-0.5 rounded-full bg-surface-700 border border-content-primary/8 text-[10px] font-medium text-content-subtle">
              <span>Live DB Inactive</span>
            </span>
          )}
        </div>

        {/* Action controls */}
        <div className="flex items-center gap-2">
          <button
            onClick={syncAndSeedDb}
            disabled={isSyncing}
            className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11px] font-semibold text-content-secondary hover:text-content-primary bg-surface-700 hover:bg-surface-600 border border-content-primary/8 transition-all disabled:opacity-50 cursor-pointer"
            title="Reload diagram schema and mock data into local database"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? 'animate-spin' : ''}`} />
            <span>{isSyncing ? 'Setting up DB...' : 'Reset & Seed'}</span>
          </button>

          <button
            onClick={() => setIsOpen(false)}
            className="p-1.5 text-content-subtle hover:text-content-primary hover:bg-white/[0.06] rounded-lg transition-colors cursor-pointer"
            aria-label="Close SQL Console"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Main Panel Content Area */}
      <div className="flex-1 flex overflow-hidden">

        {/* Left Side: Tabular Results Grid */}
        <div className="flex-1 flex flex-col overflow-hidden">

          {/* Status/Errors inside Result Section */}
          {syncError && (
            <div className="m-3 bg-danger-subtle border border-danger/25 rounded-lg p-3 flex gap-2.5 items-start shrink-0">
              <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
              <div>
                <span className="text-danger-text text-xs font-bold block mb-0.5">Setup Error</span>
                <p className="text-content-muted text-[11px] leading-relaxed font-mono">{syncError}</p>
              </div>
            </div>
          )}

          {syncSuccess && (
            <div className="m-3 bg-success-subtle border border-success/25 rounded-lg p-3 flex gap-2.5 items-start shrink-0">
              <CheckCircle className="w-4 h-4 text-success-text mt-0.5 shrink-0" />
              <div>
                <span className="text-success-text text-xs font-bold block mb-0.5">Sync Successful</span>
                <p className="text-content-secondary text-[11px] leading-relaxed">Tables are created and mock data is loaded. Ready to test.</p>
              </div>
            </div>
          )}

          {queryError && (
            <div className="m-3 bg-danger-subtle border border-danger/25 rounded-lg p-3 flex gap-2.5 items-start shrink-0">
              <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
              <div>
                <span className="text-danger-text text-xs font-bold block mb-0.5">Runtime Error</span>
                <p className="text-danger-text text-xs font-mono mt-1 leading-relaxed bg-scrim/20 p-2 rounded-md">{queryError}</p>
              </div>
            </div>
          )}

          {/* Grid / Content Rendering */}
          <div className="flex-1 overflow-auto p-3">
            {isSyncing ? (
              <div className="h-full flex flex-col items-center justify-center gap-3">
                <RefreshCw className="w-5 h-5 text-accent-text animate-spin" />
                <div className="text-center">
                  <span className="text-content-secondary text-xs font-bold block">Setting up Database...</span>
                  <span className="text-content-subtle text-[10px] mt-1 block">Modules are loading and data is seeding.</span>
                </div>
              </div>
            ) : queryResult ? (
              queryResult.isSelect ? (
                queryResult.rows.length === 0 ? (
                  <div className="h-full flex flex-col items-center justify-center gap-2 select-none">
                    <Table2 className="w-5 h-5 text-content-subtle" />
                    <div className="text-center">
                      <span className="text-content-secondary text-xs font-bold block">Result Set is Empty</span>
                      <span className="text-content-subtle text-[10px] mt-1 block">Query executed successfully but returned no results.</span>
                    </div>
                  </div>
                ) : (
                  <div className="border border-content-primary/10 rounded-lg overflow-hidden bg-surface-700">
                    <table className="w-full text-left text-xs border-collapse">
                      <thead>
                        <tr className="bg-surface-600 border-b border-content-primary/10 text-[10px] font-bold text-content-muted uppercase tracking-wider">
                          {queryResult.columns.map((col) => (
                            <th key={col} className="px-3 py-2 font-semibold">{col}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-content-primary/6 text-content-secondary font-mono text-[11px]">
                        {queryResult.rows.map((row, idx) => (
                          <tr key={idx} className="hover:bg-white/[0.02] transition-colors">
                            {queryResult.columns.map((col) => (
                              <td key={col} className="px-3 py-1.5 truncate max-w-[200px]">
                                {row[col] === null ? (
                                  <span className="text-content-subtle italic">null</span>
                                ) : typeof row[col] === 'boolean' ? (
                                  row[col] ? 'true' : 'false'
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
                    <span className="text-content-secondary text-xs font-bold block">Operation Successful</span>
                    <span className="text-success-text font-mono text-xs mt-1 block">{queryResult.message}</span>
                  </div>
                </div>
              )
            ) : !queryError && !syncError ? (
              <div className="h-full flex flex-col items-center justify-center gap-3 select-none">
                <Database className="w-5 h-5 text-content-subtle" />
                <div className="text-center max-w-sm">
                  <span className="text-content-secondary text-xs font-bold block">No Query Run</span>
                  <span className="text-content-subtle text-[10px] mt-1 leading-relaxed block">
                    Write a SQL query on the right and run it against the live in-browser database.
                  </span>
                </div>
              </div>
            ) : null}
          </div>
        </div>

        {/* Right Side: SQL Input Editor Area */}
        <div className="w-[35%] border-l border-content-primary/10 flex flex-col p-3 gap-2.5 shrink-0">
          <div className="flex justify-between items-center select-none">
            <span className="text-[10px] font-bold text-content-subtle uppercase tracking-wider">SQL Query</span>
            <span className="text-[9px] text-content-subtle font-mono">SQLite Wasm</span>
          </div>

          <textarea
            value={sqlQuery}
            onChange={(e) => setSqlQuery(e.target.value)}
            placeholder="SELECT * FROM Users LIMIT 10;"
            className="flex-1 font-mono text-xs text-content-primary bg-surface-700 border border-content-primary/10 rounded-lg p-2.5 focus:border-focus-ring focus:outline-none resize-none leading-relaxed"
            spellCheck={false}
          />

          <button
            onClick={handleRunQuery}
            disabled={executing || isSyncing || !sqlQuery.trim()}
            className="flex items-center justify-center gap-2 w-full py-2.5 rounded-lg font-semibold text-xs text-surface-900 bg-content-primary hover:bg-content-secondary transition-colors disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer"
          >
            {executing ? (
              <>
                <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                <span>Executing...</span>
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
