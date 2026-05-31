'use client';

import React, { useState, useEffect, useRef } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { DbType } from '../../store/useSchemaStore';
import { smartSeedService } from '../../services/api';
import {
  Download, Copy, Check, Loader2,
  Database, DatabaseBackup, Info
} from 'lucide-react';
import Prism from 'prismjs';
import 'prismjs/components/prism-sql';
import 'prismjs/themes/prism-tomorrow.css';

interface SmartSeedPanelProps {
  schema: DatabaseSchema;
  dbType: DbType;
}

export default function SmartSeedPanel({ schema, dbType }: SmartSeedPanelProps) {
  const [domainHint, setDomainHint] = useState<string>('');
  const [rowCount, setRowCount] = useState<number>(50);
  const [isGenerating, setIsGenerating] = useState<boolean>(false);
  const [copied, setCopied] = useState<boolean>(false);

  const [result, setResult] = useState<{
    sqlScript: string;
    detectedDomain: string;
    tableRowCounts: Record<string, number>;
    estimatedSizeBytes: number;
  } | null>(null);

  const codeRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (codeRef.current && result?.sqlScript) {
      Prism.highlightElement(codeRef.current);
    }
  }, [result]);

  const handleGenerate = async () => {
    setIsGenerating(true);
    setResult(null);
    try {
      const data = await smartSeedService.generate(schema, dbType, domainHint || undefined, rowCount);
      setResult(data);
    } catch (err) {
      console.error("Test verisi üretim hatası:", err);
    } finally {
      setIsGenerating(false);
    }
  };

  const handleCopy = async () => {
    if (!result?.sqlScript) return;
    try {
      await navigator.clipboard.writeText(result.sqlScript);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error("Kopyalama hatası:", err);
    }
  };

  const handleDownload = () => {
    if (!result?.sqlScript) return;
    const blob = new Blob([result.sqlScript], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${schema.name || 'namines'}_seed_data.sql`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  };

  const totalGeneratedRows = result 
    ? Object.values(result.tableRowCounts).reduce((a, b) => a + b, 0)
    : 0;

  return (
    <div className="w-full h-full flex flex-col gap-5 font-sans">
      
      {/* Upper control layout panel */}
      <div className="shrink-0 bg-zinc-900/60 border border-zinc-800 p-5 rounded-xl shadow-xl flex flex-col md:flex-row gap-5 items-end justify-between relative overflow-hidden">
        <div className="absolute top-0 right-0 w-[200px] h-[200px] bg-indigo-500/5 rounded-full blur-[80px] pointer-events-none" />
        
        <div className="flex-1 grid grid-cols-1 md:grid-cols-2 gap-4 w-full">
          {/* Domain selector */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-bold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5 select-none">
              <Database className="w-3.5 h-3.5 text-indigo-400" />
              Sektör / Domain
            </label>
            <select
              value={domainHint}
              onChange={(e) => setDomainHint(e.target.value)}
              disabled={isGenerating}
              className="bg-zinc-950 border border-zinc-800 rounded-xl py-[10px] px-[15px] text-sm text-zinc-300 focus:outline-none focus:border-indigo-500/50 cursor-pointer"
            >
              <option value="">Auto Detect</option>
              <option value="E-Commerce">E-Commerce</option>
              <option value="Logistics & Transportation">Logistics & Transportation</option>
              <option value="Healthcare & Medical">Healthcare & Medical</option>
              <option value="Education & Academia">Education & Academia</option>
              <option value="Finance & Banking">Finance & Banking</option>
              <option value="Human Resources">Human Resources</option>
              <option value="Real Estate">Real Estate</option>
              <option value="Manufacturing & Supply Chain">Manufacturing & Supply Chain</option>
              <option value="Telecommunications">Telecommunications</option>
              <option value="Travel & Hospitality">Travel & Hospitality</option>
              <option value="Insurance">Insurance</option>
              <option value="Media & Entertainment">Media & Entertainment</option>
              <option value="Government & Public Sector">Government & Public Sector</option>
              <option value="Retail & Inventory">Retail & Inventory</option>
            </select>
          </div>

          {/* Row count selector */}
          <div className="flex flex-col gap-1.5">
            <label className="text-xs font-bold text-zinc-400 uppercase tracking-wider flex items-center gap-1.5 select-none">
              <DatabaseBackup className="w-3.5 h-3.5 text-indigo-400" />
              Tablo Başına Satır Sayısı
            </label>
            <select
              value={rowCount}
              onChange={(e) => setRowCount(Number(e.target.value))}
              disabled={isGenerating}
              className="bg-zinc-950 border border-zinc-800 rounded-xl py-[10px] px-[15px] text-sm text-zinc-300 focus:outline-none focus:border-indigo-500/50 cursor-pointer"
            >
              <option value="10">10 Rows (Quick)</option>
              <option value="15">15 Rows (Fast)</option>
              <option value="25">25 Rows (Medium)</option>
              <option value="50">50 Rows (Standard)</option>
              <option value="100">100 Rows (Extended)</option>
            </select>
          </div>
        </div>

        {/* Generate Button */}
        <button
          onClick={handleGenerate}
          disabled={isGenerating || !schema.tables || schema.tables.length === 0}
          className="w-full md:w-auto px-8 py-3 bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white font-bold text-xs rounded-xl shadow-[0_0_15px_rgba(99,102,241,0.25)] hover:shadow-[0_0_20px_rgba(99,102,241,0.45)] transition-all duration-300 flex items-center justify-center gap-2 border border-indigo-400/20 cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
        >
          {isGenerating ? (
            <>
              <Loader2 className="w-4 h-4 animate-spin text-white" />
              Generating...
            </>
          ) : (
            <span>Generate Test Data</span>
          )}
        </button>
      </div>

      {/* Result statistics & preview */}
      <div className="flex-1 min-h-0 flex flex-col gap-4 relative">
        {isGenerating && (
          <div className="absolute inset-0 z-20 bg-zinc-950/60 backdrop-blur-sm flex items-center justify-center rounded-xl border border-zinc-850 flex-col gap-3">
            <Loader2 className="w-8 h-8 text-indigo-500 animate-spin" />
            <span className="text-sm font-semibold text-zinc-400 font-mono">AI veritabanı şemasına uygun test verilerini hazırlıyor...</span>
          </div>
        )}

        {result ? (
          <div className="flex-1 min-h-0 flex flex-col gap-4">
            
            {/* Statistics Bar Dashboard */}
            <div className="shrink-0 bg-zinc-900/40 border border-zinc-800/80 px-4 py-3 rounded-xl flex flex-wrap items-center justify-between gap-4 text-xs font-mono">
              <div className="flex flex-wrap items-center gap-4">
                <span className="text-zinc-500 flex items-center gap-1">
                  Sektör:
                  <strong className="text-indigo-400 font-bold bg-indigo-950/40 border border-indigo-900/30 px-2 py-0.5 rounded uppercase text-[10px]">
                    {result.detectedDomain}
                  </strong>
                </span>
                <span className="text-zinc-700">|</span>
                <span className="text-zinc-500">
                  Toplam Satır: <strong className="text-zinc-300">{totalGeneratedRows} satır</strong>
                </span>
                <span className="text-zinc-700">|</span>
                <span className="text-zinc-500">
                  Dosya Boyutu: <strong className="text-zinc-300">{formatSize(result.estimatedSizeBytes)}</strong>
                </span>
              </div>

              {/* Action Buttons */}
              <div className="flex gap-2">
                <button
                  onClick={handleCopy}
                  className="px-3.5 py-1.5 bg-zinc-800 hover:bg-zinc-750 text-zinc-300 hover:text-white rounded-lg border border-zinc-700 transition-colors flex items-center gap-1.5 cursor-pointer"
                >
                  {copied ? (
                    <>
                      <Check className="w-3.5 h-3.5 text-emerald-400" />
                      Kopyalandı
                    </>
                  ) : (
                    <>
                      <Copy className="w-3.5 h-3.5" />
                      Kopyala
                    </>
                  )}
                </button>
                <button
                  onClick={handleDownload}
                  className="px-3.5 py-1.5 bg-indigo-950/40 hover:bg-indigo-900/40 text-indigo-400 hover:text-indigo-300 rounded-lg border border-indigo-900/30 transition-colors flex items-center gap-1.5 cursor-pointer"
                >
                  <Download className="w-3.5 h-3.5" />
                  SQL Olarak İndir
                </button>
              </div>
            </div>

            {/* Code Block rendering */}
            <div className="flex-1 min-h-0 bg-[#1d1f21] rounded-xl overflow-hidden border border-zinc-800 shadow-2xl relative">
              <div className="absolute top-0 left-0 w-full px-4 py-2 bg-zinc-900 border-b border-zinc-800 flex justify-between items-center z-10">
                <span className="text-xs text-zinc-400 font-mono">seed_data.sql</span>
              </div>
              <div className="h-full pt-10 overflow-auto custom-scrollbar">
                <pre className="!bg-transparent !m-0 !p-4 !text-sm">
                  <code ref={codeRef} className="language-sql">
                    {result.sqlScript}
                  </code>
                </pre>
              </div>
            </div>
          </div>
        ) : (
          <div className="flex-1 min-h-0 bg-zinc-900/10 border border-zinc-800 border-dashed rounded-xl flex flex-col items-center justify-center text-center p-8 gap-3">
            <Info className="w-10 h-10 text-zinc-700" />
            <div>
              <h4 className="text-zinc-400 font-semibold text-sm">Intelligent Data Seeding</h4>
              <p className="text-zinc-600 text-xs mt-1 max-w-md leading-relaxed">
                Analyzes your database schema&apos;s relational structure and domain model to generate consistent, realistic test datasets. Select a sector above or let the engine auto-detect it, then click the button to generate.
              </p>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
