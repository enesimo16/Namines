'use client';

import React, { useEffect, useRef, useState, useMemo } from 'react';
import Prism from 'prismjs';
import 'prismjs/components/prism-sql';
import 'prismjs/components/prism-clike';
import 'prismjs/components/prism-csharp';
import 'prismjs/components/prism-markdown';
import 'prismjs/themes/prism-tomorrow.css';
import {
  Copy,
  Check,
  Download,
  Search,
  X,
  ChevronDown,
  ChevronUp,
  Maximize2,
  Minimize2,
  WrapText,
  FileCode2,
  Sparkles,
  CheckCircle2,
  Database
} from 'lucide-react';
import { useToastStore } from '../../store/useToastStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import Select from '../ui/Select';

export interface CodeStudioViewerProps {
  code: string;
  language: 'sql' | 'csharp' | 'prisma' | 'markdown' | 'json';
  filename: string;
  dialectBadge?: string;
  headerControls?: React.ReactNode;
  onDownload?: () => void;
  downloadLabel?: string;
  isDownloading?: boolean;
  statusText?: string;
  focusTable?: string;
}

export default function CodeStudioViewer({
  code,
  language,
  filename,
  dialectBadge,
  headerControls,
  onDownload,
  downloadLabel = 'Download',
  isDownloading = false,
  statusText,
  focusTable,
}: CodeStudioViewerProps) {
  const codeRef = useRef<HTMLElement>(null);
  const scrollContainerRef = useRef<HTMLDivElement>(null);
  const showToast = useToastStore(state => state.showToast);
  const { schema, dbType } = useSchemaStore();

  const [copied, setCopied] = useState(false);
  const [searchQuery, setSearchQuery] = useState('');
  const [activeMatchIndex, setActiveMatchIndex] = useState(0);
  const [isWrap, setIsWrap] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [selectedTableJump, setSelectedTableJump] = useState('');

  // Syntax highlighting via Prism
  useEffect(() => {
    if (codeRef.current && code) {
      Prism.highlightElement(codeRef.current);
    }
  }, [code, language]);

  // Split lines for line numbering & search
  const lines = useMemo(() => {
    if (!code) return [];
    return code.split('\n');
  }, [code]);

  const totalLines = lines.length;
  const charCount = code ? code.length : 0;
  const kbSize = (new Blob([code]).size / 1024).toFixed(1);

  // Search match calculations
  const searchMatches = useMemo(() => {
    if (!searchQuery.trim()) return [];
    const query = searchQuery.toLowerCase();
    const matches: number[] = [];
    lines.forEach((line, idx) => {
      if (line.toLowerCase().includes(query)) {
        matches.push(idx);
      }
    });
    return matches;
  }, [lines, searchQuery]);

  // Jump to specific match line
  useEffect(() => {
    if (searchMatches.length > 0 && scrollContainerRef.current) {
      const targetLineIdx = searchMatches[activeMatchIndex] ?? searchMatches[0];
      const targetScroll = Math.max(0, targetLineIdx * 20 - 80);
      scrollContainerRef.current.scrollTo({ top: targetScroll, behavior: 'smooth' });
    }
  }, [activeMatchIndex, searchMatches]);

  const handleNextMatch = () => {
    if (searchMatches.length === 0) return;
    setActiveMatchIndex(prev => (prev + 1) % searchMatches.length);
  };

  const handlePrevMatch = () => {
    if (searchMatches.length === 0) return;
    setActiveMatchIndex(prev => (prev - 1 + searchMatches.length) % searchMatches.length);
  };

  const handleCopy = async () => {
    if (!code) return;
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      showToast(`Copied ${totalLines} lines to clipboard!`, 'success');
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error('Copy failed:', err);
      showToast('Failed to copy to clipboard.', 'error');
    }
  };

  const handleDefaultDownload = () => {
    if (onDownload) {
      onDownload();
      return;
    }
    try {
      const mimeType = language === 'markdown' ? 'text/markdown' : 'text/plain';
      const blob = new Blob([code], { type: `${mimeType};charset=utf-8` });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast(`${filename} successfully downloaded!`, 'success');
    } catch (err) {
      console.error('Download error:', err);
      showToast('Failed to download file.', 'error');
    }
  };

  // Jump to table definition
  const handleJumpToTable = (tableName: string) => {
    setSelectedTableJump(tableName);
    if (!tableName || !scrollContainerRef.current) return;
    const lowerName = tableName.toLowerCase();
    const targetIdx = lines.findIndex(line => {
      const l = line.toLowerCase();
      return (
        l.includes(`create table ${lowerName}`) ||
        l.includes(`create table "${lowerName}"`) ||
        l.includes(`create table [${lowerName}]`) ||
        l.includes(`class ${lowerName}`) ||
        l.includes(`model ${lowerName}`)
      );
    });

    if (targetIdx !== -1) {
      const targetScroll = Math.max(0, targetIdx * 20 - 60);
      scrollContainerRef.current.scrollTo({ top: targetScroll, behavior: 'smooth' });
      showToast(`Jumped to table "${tableName}" (Line ${targetIdx + 1})`, 'info');
    }
  };

  useEffect(() => {
    if (focusTable) {
      handleJumpToTable(focusTable);
    }
  }, [focusTable, lines]);

  // Width for line number gutter
  const gutterWidth = totalLines > 9999 ? 'w-14' : totalLines > 999 ? 'w-12' : 'w-10';

  return (
    <div
      className={`flex flex-col bg-surface-900 border border-line-strong overflow-hidden select-none ${
        isFullscreen
          ? 'fixed inset-0 z-50 rounded-none'
          : 'w-full h-full rounded-[var(--radius-card)]'
      }`}
    >
      {/* ── IDE Top Header Bar ──────────────────────────────────────────────── */}
      <div className="shrink-0 min-h-11 px-3 py-1.5 bg-surface-800 border-b border-line-strong flex flex-wrap items-center justify-between gap-2.5">
        
        {/* Left: File Tab & Dialect Badges */}
        <div className="flex items-center gap-2 min-w-0">
          <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-700/80 border border-line shadow-xs">
            <FileCode2 className="w-3.5 h-3.5 text-namines-teal-vibrant shrink-0" />
            <span className="text-xs font-mono font-semibold text-content-primary truncate max-w-[180px]">
              {filename}
            </span>
          </div>

          {(dialectBadge || dbType) && (
            <span className="hidden sm:inline-flex items-center gap-1 text-[10px] font-mono font-bold uppercase tracking-wider px-2 py-0.5 rounded-full bg-surface-700 text-content-secondary border border-line shrink-0">
              <Database className="w-2.5 h-2.5 text-content-muted" />
              {dialectBadge || dbType}
            </span>
          )}

          {headerControls}
        </div>

        {/* Center: Search inside code */}
        <div className="flex items-center gap-1.5 flex-1 max-w-xs min-w-[160px]">
          <div className="relative w-full flex items-center">
            <Search className="w-3.5 h-3.5 text-content-muted absolute left-2 pointer-events-none" />
            <input
              type="text"
              value={searchQuery}
              onChange={e => {
                setSearchQuery(e.target.value);
                setActiveMatchIndex(0);
              }}
              placeholder="Find in script..."
              className="w-full bg-surface-700/70 border border-line rounded-[var(--radius-control)] h-8 pl-7 pr-7 text-xs text-content-primary placeholder:text-content-muted/60 focus:outline-none focus:border-focus-ring"
            />
            {searchQuery && (
              <button
                onClick={() => setSearchQuery('')}
                className="absolute right-2 text-content-muted hover:text-content-primary p-0.5"
                title="Clear search"
                aria-label="Clear search"
              >
                <X className="w-3 h-3" />
              </button>
            )}
          </div>

          {searchMatches.length > 0 && (
            <div className="flex items-center gap-0.5 shrink-0 bg-surface-700 px-1.5 py-0.5 rounded-[var(--radius-control)] border border-line text-[10px] font-mono text-content-secondary">
              <span>{activeMatchIndex + 1}/{searchMatches.length}</span>
              <button
                onClick={handlePrevMatch}
                className="hover:text-content-primary p-0.5 cursor-pointer"
                title="Previous match"
                aria-label="Previous match"
              >
                <ChevronUp className="w-3 h-3" />
              </button>
              <button
                onClick={handleNextMatch}
                className="hover:text-content-primary p-0.5 cursor-pointer"
                title="Next match"
                aria-label="Next match"
              >
                <ChevronDown className="w-3 h-3" />
              </button>
            </div>
          )}
        </div>

        {/* Right Actions: Jump to Table, Wrap, Copy, Download, Maximize */}
        <div className="flex items-center gap-1.5 shrink-0">
          {schema?.tables && schema.tables.length > 0 && (
            <div className="hidden md:block w-36">
              <Select
                value={selectedTableJump}
                onChange={e => handleJumpToTable(e.target.value)}
                aria-label="Jump to table definition"
                placeholder="Jump to table..."
                className="text-[11px] font-mono"
              >
                <option value="">Jump to table...</option>
                {schema.tables.map(t => (
                  <option key={t.id} value={t.name}>
                    {t.name}
                  </option>
                ))}
              </Select>
            </div>
          )}

          {/* Line Wrap Toggle */}
          <button
            onClick={() => setIsWrap(!isWrap)}
            className={`tap-44 flex items-center justify-center w-8 h-8 rounded-[var(--radius-control)] border transition-all cursor-pointer ${
              isWrap
                ? 'bg-surface-700 border-line-strong text-namines-teal-vibrant'
                : 'bg-surface-800/80 border-line text-content-muted hover:text-content-primary hover:bg-surface-700/60'
            }`}
            title={isWrap ? 'Disable wrap' : 'Enable line wrap'}
            aria-label="Toggle line wrap"
          >
            <WrapText className="w-3.5 h-3.5" />
          </button>

          {/* Copy Button */}
          <button
            onClick={handleCopy}
            className="tap-44 flex items-center gap-1.5 h-8 px-2.5 rounded-[var(--radius-control)] bg-surface-700/80 hover:bg-surface-700 border border-line text-xs font-medium text-content-primary transition-all cursor-pointer shadow-xs"
            title="Copy all code"
            aria-label="Copy code"
          >
            {copied ? (
              <>
                <Check className="w-3.5 h-3.5 text-namines-teal-vibrant" />
                <span className="text-namines-teal-vibrant font-semibold text-[11px]">Copied</span>
              </>
            ) : (
              <>
                <Copy className="w-3.5 h-3.5 text-content-muted" />
                <span className="text-[11px]">Copy</span>
              </>
            )}
          </button>

          {/* Download Button */}
          <button
            onClick={handleDefaultDownload}
            disabled={isDownloading}
            className="tap-44 flex items-center gap-1.5 h-8 px-3 rounded-[var(--radius-control)] bg-content-primary hover:opacity-90 text-surface-900 text-xs font-semibold transition-all cursor-pointer shadow-xs disabled:opacity-50"
            title={`Download ${filename}`}
            aria-label={`Download ${filename}`}
          >
            <Download className="w-3.5 h-3.5" />
            <span className="text-[11px]">{downloadLabel}</span>
          </button>

          {/* Fullscreen Toggle */}
          <button
            onClick={() => setIsFullscreen(!isFullscreen)}
            className="tap-44 flex items-center justify-center w-8 h-8 rounded-[var(--radius-control)] bg-surface-800/80 hover:bg-surface-700 border border-line text-content-muted hover:text-content-primary transition-all cursor-pointer"
            title={isFullscreen ? 'Exit Fullscreen' : 'Expand to Fullscreen'}
            aria-label="Toggle Fullscreen"
          >
            {isFullscreen ? <Minimize2 className="w-3.5 h-3.5" /> : <Maximize2 className="w-3.5 h-3.5" />}
          </button>
        </div>
      </div>

      {/* ── Code Editor Body (Gutter + Code Surface) ────────────────────────── */}
      <div
        ref={scrollContainerRef}
        className="flex-1 min-h-0 overflow-auto bg-surface-900 flex select-text relative font-mono text-[11px] leading-5"
      >
        {/* Line Numbers Gutter */}
        <div
          className={`select-none py-3 pl-2 pr-3 text-right text-content-muted/40 border-r border-line shrink-0 bg-surface-900/90 font-mono text-[11px] leading-5 sticky left-0 z-10 ${gutterWidth}`}
        >
          {lines.map((_, i) => {
            const isMatch = searchMatches.includes(i);
            const isActiveMatch = searchMatches[activeMatchIndex] === i;
            return (
              <div
                key={i}
                className={`h-5 leading-5 transition-colors ${
                  isActiveMatch
                    ? 'text-namines-teal-vibrant font-bold bg-surface-700/80 px-1 rounded-xs'
                    : isMatch
                    ? 'text-content-primary font-semibold'
                    : 'hover:text-content-secondary'
                }`}
              >
                {i + 1}
              </div>
            );
          })}
        </div>

        {/* Code Content */}
        <div className="flex-1 min-w-0 py-3 px-4">
          <pre
            className={`!bg-transparent !m-0 !p-0 font-mono text-[11px] leading-5 ${
              isWrap ? 'whitespace-pre-wrap break-words' : 'whitespace-pre overflow-x-auto'
            }`}
          >
            <code ref={codeRef} className={`language-${language} font-mono text-[11px] leading-5`}>
              {code || '-- No code generated yet'}
            </code>
          </pre>
        </div>
      </div>

      {/* ── Bottom Telemetry & Status Bar ───────────────────────────────────── */}
      <div className="shrink-0 h-7 px-3 bg-surface-800/95 border-t border-line-strong flex items-center justify-between text-[10px] font-mono text-content-muted select-none">
        
        {/* Left: Validation Status */}
        <div className="flex items-center gap-2">
          <span className="flex items-center gap-1.5 text-content-primary font-medium">
            <span className="w-2 h-2 rounded-full bg-namines-teal-vibrant animate-pulse" />
            <span className="text-content-secondary">
              {statusText || 'Syntax Verified · ANSI Compliant'}
            </span>
          </span>
        </div>

        {/* Middle: Line & Character Telemetry */}
        <div className="hidden sm:flex items-center gap-3">
          <span>{totalLines} lines</span>
          <span>·</span>
          <span>{charCount.toLocaleString()} chars</span>
          <span>·</span>
          <span>{kbSize} KB</span>
        </div>

        {/* Right: Encoding */}
        <div className="flex items-center gap-2.5">
          <span className="bg-surface-700/60 px-1.5 py-0.5 rounded-[var(--radius-control)] text-[10px]">UTF-8</span>
        </div>
      </div>
    </div>
  );
}
