'use client';

import React, { useState, useEffect, useRef } from 'react';
import { Terminal, Play, X, RefreshCw, Database, AlertCircle, CheckCircle, Table2 } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useSqlExplorerStore } from '../../../store/useSqlExplorerStore';
import { sqliteService, SqlQueryResult } from '../../../services/sqliteService';
import { schemaService, smartSeedService } from '../../../services/api';

export default function SqlExplorerPanel() {
  const { schema } = useSchemaStore();
  const { isOpen, setIsOpen } = useSqlExplorerStore();

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

      setSyncSuccess(true);

      // Set default query in editor if empty or generic
      const firstTableName = schema.tables[0]?.name || 'Tablo';
      setSqlQuery(`SELECT * FROM ${firstTableName} LIMIT 10;`);

      setTimeout(() => setSyncSuccess(false), 3000);
    } catch (err: any) {
      setSyncError(err.message || 'Canlı veritabanı kurulurken hata oluştu.');
    } finally {
      setIsSyncing(false);
    }
  };

  // Auto-sync database when panel is opened for the first time
  useEffect(() => {
    if (isOpen && schema && schema.tables.length > 0 && !sqliteService.isActive()) {
      syncAndSeedDb();
    }
  }, [isOpen, schema]);

  if (!isOpen) return null;

  const handleRunQuery = async () => {
    if (!sqlQuery.trim()) return;

    setExecuting(true);
    setQueryError(null);
    setQueryResult(null);

    try {
      const res = await sqliteService.executeQuery(sqlQuery);
      setQueryResult(res);
    } catch (err: any) {
      setQueryError(err.message || 'Sorgu çalıştırılırken bilinmeyen bir SQLite hatası oluştu.');
    } finally {
      setExecuting(false);
    }
  };

  return (
    <div 
      ref={containerRef}
      className="fixed bottom-6 left-[12%] right-[12%] z-[49] h-[300px] bg-gradient-to-b from-[#09111F]/90 to-[#0D182A]/90 border border-indigo-500/30 shadow-[0_10px_50px_rgba(0,0,0,0.85)] flex flex-col overflow-hidden animate-in slide-in-from-bottom duration-300 font-sans rounded-3xl backdrop-blur-xl"
    >
      {/* Background Orbs & Stars (Uzay Teması) */}
      <div className="absolute inset-0 pointer-events-none bg-[url('/noise.png')] opacity-[0.01] mix-blend-overlay" />
      <div className="absolute inset-0 pointer-events-none bg-[radial-gradient(ellipse_at_top_right,_var(--tw-gradient-stops))] from-indigo-600/10 via-purple-900/5 to-transparent opacity-80" />
      <div className="absolute inset-0 pointer-events-none bg-[radial-gradient(circle_at_bottom_left,_var(--tw-gradient-stops))] from-teal-500/5 via-transparent to-transparent opacity-60" />
      
      {/* Glowing Orbs */}
      <div className="absolute top-10 right-20 w-44 h-44 bg-indigo-500/10 rounded-full blur-[60px] pointer-events-none" />
      <div className="absolute bottom-10 left-20 w-40 h-40 bg-purple-500/5 rounded-full blur-[70px] pointer-events-none" />

      {/* Decorative Wave SVG (Bottom) */}
      <div className="absolute bottom-0 left-0 w-full h-[60%] pointer-events-none opacity-[0.04] z-0">
        <svg viewBox="0 0 400 150" preserveAspectRatio="none" className="w-full h-full fill-indigo-300">
          <path d="M0,50 C100,150 300,0 400,100 L400,150 L0,150 Z" />
        </svg>
      </div>

      {/* Panel Header */}
      <div className="relative z-10 flex items-center justify-between px-6 py-3 border-b border-indigo-500/10 bg-[#0F172A]/40 backdrop-blur-md select-none shrink-0">
        <div className="flex items-center gap-3">
          <div className="p-1.5 bg-indigo-500/10 border border-indigo-500/20 rounded-lg text-indigo-400">
            <Terminal className="w-4 h-4" />
          </div>
          <span className="text-xs font-bold tracking-wider text-indigo-200 uppercase">SQL Konsolu</span>

          {/* Active status indicator badge */}
          {sqliteService.isActive() && !isSyncing ? (
            <span className="flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-emerald-500/5 border border-emerald-500/15 text-[10px] font-medium text-emerald-400">
              <span className="relative flex h-1.5 w-1.5 shrink-0">
                <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
                <span className="relative inline-flex rounded-full h-1.5 w-1.5 bg-emerald-500"></span>
              </span>
              <span>Canlı DB Aktif</span>
            </span>
          ) : (
            <span className="flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-zinc-900/30 border border-zinc-800 text-[10px] font-medium text-zinc-500">
              <span>Canlı DB Pasif</span>
            </span>
          )}
        </div>

        {/* Action controls */}
        <div className="flex items-center gap-2">
          {/* Database Reset/Sync button */}
          <button
            onClick={syncAndSeedDb}
            disabled={isSyncing}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-semibold text-indigo-200 hover:text-white bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/20 transition-all disabled:opacity-50 cursor-pointer shadow-sm"
            title="Diyagram şemasını ve mock verileri yerel veritabanına yeniden yükle"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isSyncing ? 'animate-spin text-indigo-400' : 'text-indigo-400/70'}`} />
            <span>{isSyncing ? 'Veritabanı Kuruluyor...' : 'Reset & Seed'}</span>
          </button>

          {/* Close Panel button */}
          <button 
            onClick={() => setIsOpen(false)}
            className="p-1.5 text-indigo-300/60 hover:text-white hover:bg-white/5 rounded-lg transition-colors border border-transparent hover:border-indigo-500/20 cursor-pointer"
          >
            <X className="w-4 h-4" />
          </button>
        </div>
      </div>

      {/* Main Panel Content Area */}
      <div className="relative z-10 flex-1 flex overflow-hidden">
        
        {/* Left Side: Tabular Results Grid */}
        <div className="flex-1 flex flex-col overflow-hidden bg-transparent">
          
          {/* Status/Errors inside Result Section */}
          {syncError && (
            <div className="m-4 bg-rose-950/10 border border-rose-500/20 rounded-xl p-4 flex gap-3 items-start animate-in fade-in shrink-0">
              <AlertCircle className="w-4 h-4 text-red-500 mt-0.5 shrink-0" />
              <div>
                <span className="text-red-400 text-xs font-bold block mb-0.5">Kurulum Hatası</span>
                <p className="text-zinc-500 text-[11px] leading-relaxed font-mono">{syncError}</p>
              </div>
            </div>
          )}

          {syncSuccess && (
            <div className="m-4 bg-emerald-950/20 border border-emerald-500/30 rounded-xl p-4 flex gap-3 items-start animate-in fade-in shrink-0">
              <CheckCircle className="w-4 h-4 text-emerald-400 mt-0.5 shrink-0" />
              <div>
                <span className="text-emerald-400 text-xs font-bold block mb-0.5">Eşitleme Başarılı</span>
                <p className="text-indigo-200/80 text-[11px] leading-relaxed">Tablolar kuruldu ve mock veriler yüklendi. Test etmeye hazır.</p>
              </div>
            </div>
          )}

          {queryError && (
            <div className="m-4 bg-rose-950/20 border border-rose-500/30 rounded-xl p-4 flex gap-3 items-start animate-in fade-in shrink-0">
              <AlertCircle className="w-4 h-4 text-rose-400 mt-0.5 shrink-0" />
              <div>
                <span className="text-rose-400 text-xs font-bold block mb-0.5">Çalışma Hatası</span>
                <p className="text-rose-200/80 text-xs font-mono mt-1 leading-relaxed bg-[#070D19]/60 p-2.5 rounded-lg border border-rose-500/20">{queryError}</p>
              </div>
            </div>
          )}

          {/* Grid / Content Rendering */}
          <div className="flex-1 overflow-auto p-4 custom-scrollbar">
            {isSyncing ? (
              /* Loading Spinner when db is initializing */
              <div className="h-full flex flex-col items-center justify-center gap-3">
                <RefreshCw className="w-6 h-6 text-indigo-400 animate-spin" />
                <div className="text-center">
                  <span className="text-indigo-250 text-xs font-bold block">Veritabanı Ayarlanıyor...</span>
                  <span className="text-indigo-400/60 text-[10px] mt-1 block">Modüller yükleniyor ve veriler dolduruluyor.</span>
                </div>
              </div>
            ) : queryResult ? (
              /* Table Rendering for SELECT queries, or message for DDL/DML */
              queryResult.isSelect ? (
                queryResult.rows.length === 0 ? (
                  /* Empty SELECT results */
                  <div className="h-full flex flex-col items-center justify-center gap-2 select-none">
                    <Table2 className="w-6 h-6 text-indigo-400/60" />
                    <div className="text-center">
                      <span className="text-indigo-300 text-xs font-bold block">Sonuç Kümesi Boş</span>
                      <span className="text-indigo-400/50 text-[10px] mt-1 block">Sorgu çalıştı ancak sonuç dönmedi.</span>
                    </div>
                  </div>
                ) : (
                  /* Render actual table */
                  <div className="border border-indigo-500/20 rounded-xl overflow-hidden shadow-2xl bg-[#070D19]/40 backdrop-blur-md">
                    <table className="w-full text-left text-xs border-collapse">
                      <thead>
                        <tr className="bg-indigo-950/40 border-b border-indigo-500/20 text-[10px] font-bold text-indigo-300 uppercase tracking-wider">
                          {queryResult.columns.map((col) => (
                            <th key={col} className="px-4 py-2.5 font-semibold">{col}</th>
                          ))}
                        </tr>
                      </thead>
                      <tbody className="divide-y divide-indigo-950/50 text-indigo-100 font-mono text-[11px]">
                        {queryResult.rows.map((row, idx) => (
                          <tr key={idx} className="hover:bg-indigo-500/5 transition-colors">
                            {queryResult.columns.map((col) => (
                              <td key={col} className="px-4 py-2 truncate max-w-[200px]">
                                {row[col] === null ? (
                                  <span className="text-indigo-400/40 italic">null</span>
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
                /* Success message for INSERT/UPDATE/DELETE queries */
                <div className="h-full flex flex-col items-center justify-center gap-2 animate-in fade-in select-none">
                  <CheckCircle className="w-6 h-6 text-emerald-400 animate-pulse" />
                  <div className="text-center">
                    <span className="text-zinc-200 text-xs font-bold block">İşlem Başarılı</span>
                    <span className="text-emerald-400 font-mono text-xs mt-1 block">{queryResult.message}</span>
                  </div>
                </div>
              )
            ) : !queryError && !syncError ? (
              /* Empty Initial State */
              <div className="h-full flex flex-col items-center justify-center gap-3 select-none">
                <div className="w-10 h-10 bg-indigo-500/10 border border-indigo-500/20 text-indigo-400 flex items-center justify-center rounded-xl">
                  <Database className="w-4 h-4 animate-pulse" />
                </div>
                <div className="text-center max-w-sm">
                  <span className="text-indigo-200 text-xs font-bold block">Sorgu Koşulmadı</span>
                  <span className="text-indigo-400/50 text-[10px] mt-1 leading-relaxed block">
                    Sağ tarafa SQL sorgusu yazıp &apos;Sorguyu Çalıştır&apos; butonu ile canlı veritabanında test edebilirsiniz.
                  </span>
                </div>
              </div>
            ) : null}
          </div>
        </div>

        {/* Right Side: SQL Input Editor Area */}
        <div className="w-[35%] border-l border-indigo-500/10 flex flex-col p-4 gap-3 shrink-0">
          <div className="flex justify-between items-center select-none">
            <span className="text-[10px] font-bold text-indigo-300/60 uppercase tracking-wider">SQL Sorgusu</span>
            <span className="text-[9px] text-indigo-400/50 font-mono">SQLite Wasm</span>
          </div>

          <textarea
            value={sqlQuery}
            onChange={(e) => setSqlQuery(e.target.value)}
            placeholder="SELECT * FROM Users LIMIT 10;"
            className="flex-1 font-mono text-xs text-indigo-100 bg-[#070D19]/60 backdrop-blur-sm border border-indigo-500/20 rounded-xl p-3 focus:border-indigo-500/50 focus:outline-none resize-none leading-relaxed custom-scrollbar shadow-inner"
            spellCheck={false}
          />

          <button
            onClick={handleRunQuery}
            disabled={executing || isSyncing || !sqlQuery.trim()}
            className="flex items-center justify-center gap-2 w-full py-2.5 rounded-xl font-bold text-xs text-white bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-450 transition-colors shadow-[0_4px_15px_rgba(99,102,241,0.25)] disabled:opacity-50 disabled:cursor-not-allowed border border-indigo-400/30 cursor-pointer"
          >
            {executing ? (
              <>
                <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                <span>Çalıştırılıyor...</span>
              </>
            ) : (
              <>
                <Play className="w-3.5 h-3.5 fill-current" />
                <span>Sorguyu Çalıştır</span>
              </>
            )}
          </button>
        </div>

      </div>
    </div>
  );
}
