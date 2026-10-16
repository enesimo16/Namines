'use client';

import React, { useState, useEffect } from 'react';
import { Download, FileText, Loader2, RefreshCw, Copy, Check, Eye, Code2 } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { PanelEmpty } from './PanelKit';
import CodeStudioViewer from './CodeStudioViewer';

interface ReadmePreviewProps {
  schema: DatabaseSchema;
}

type ViewMode = 'preview' | 'raw';
type Lang = 'tr' | 'en';

export default function ReadmePreview({ schema }: ReadmePreviewProps) {
  const showToast = useToastStore(state => state.showToast);
  const [readmeText, setReadmeText] = useState<string>('');
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [isDownloading, setIsDownloading] = useState<boolean>(false);
  const [viewMode, setViewMode] = useState<ViewMode>('preview');
  const [lang, setLang] = useState<Lang>('en');
  const [copied, setCopied] = useState<boolean>(false);

  const fetchReadme = async () => {
    if (!schema) return;
    setIsLoading(true);
    try {
      const text = await schemaService.generateReadme(schema, lang);
      setReadmeText(text);
    } catch (error) {
      console.error("Failed to generate README", error);
      setReadmeText('# Error generating README\n\nPlease try again later.');
      showToast("An error occurred while generating README.", "error");
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    fetchReadme();
  }, [schema, lang]);

  const handleDownloadReadme = async () => {
    if (!schema) return;
    setIsDownloading(true);
    try {
      const content = readmeText || (await schemaService.generateReadme(schema, lang));
      const blob = new Blob([content], { type: 'text/markdown;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `README.md`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast("README.md successfully downloaded!", "success");
    } catch (error) {
      console.error("Failed to download README", error);
      showToast("An error occurred while downloading README.", "error");
    } finally {
      setIsDownloading(false);
    }
  };

  const handleCopyRaw = async () => {
    if (!readmeText) return;
    try {
      await navigator.clipboard.writeText(readmeText);
      setCopied(true);
      showToast("Raw markdown copied to clipboard!", "success");
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error("Failed to copy raw text", err);
      showToast("An error occurred while copying to clipboard.", "error");
    }
  };

  // Custom premium markdown parsing & styling engine
  const renderMarkdown = (text: string) => {
    if (!text) return null;

    const lines = text.split('\n');
    let inCodeBlock = false;
    let codeBlockLines: string[] = [];
    
    const renderedElements: React.ReactNode[] = [];

    let inTable = false;
    let tableHeaders: string[] = [];
    let tableRows: string[][] = [];

    const flushTable = (keyIndex: number) => {
      if (tableRows.length > 0) {
        renderedElements.push(
          <div key={`table-wrapper-${keyIndex}`} className="overflow-x-auto my-4 border border-line-strong rounded-[var(--radius-control)] bg-surface-800">
            <table className="w-full text-left border-collapse text-xs">
              <thead>
                <tr className="border-b border-line text-content-muted font-bold uppercase tracking-wider bg-surface-700/50">
                  {tableHeaders.map((h, i) => (
                    <th key={`th-${keyIndex}-${i}`} className="py-2.5 px-4 font-mono">{h.replace(/\*\*/g, '').trim()}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {tableRows.map((row, rowIndex) => (
                  <tr key={`tr-${keyIndex}-${rowIndex}`} className="border-b border-line/60 hover:bg-surface-700/30 transition-colors">
                    {row.map((cell, colIndex) => (
                      <td key={`td-${keyIndex}-${rowIndex}-${colIndex}`} className="py-2.5 px-4 text-content-secondary leading-normal">
                        {cell.trim().startsWith('`') && cell.trim().endsWith('`') ? (
                          <code className="text-namines-teal-vibrant bg-surface-700 px-1 py-0.5 rounded-[var(--radius-control)] font-mono text-[10px] border border-line">
                            {cell.replace(/`/g, '')}
                          </code>
                        ) : cell.includes('🔑') || cell.includes('🔗') || cell.includes('✅') || cell.includes('❌') ? (
                          <span className="font-semibold text-content-primary">{cell.trim()}</span>
                        ) : (
                          cell.trim()
                        )}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        );
      }
      tableHeaders = [];
      tableRows = [];
      inTable = false;
    };

    lines.forEach((line, idx) => {
      if (line.trim().startsWith('|')) {
        inTable = true;
        const cells = line.split('|').map(c => c.trim()).filter((_, i, arr) => i > 0 && i < arr.length - 1);
        
        if (cells.every(c => c.startsWith(':') || c.startsWith('-') || c.endsWith(':'))) {
          return;
        }

        if (tableHeaders.length === 0) {
          tableHeaders = cells;
        } else {
          tableRows.push(cells);
        }
        return;
      } else {
        if (inTable) {
          flushTable(idx);
        }
      }

      if (line.trim().startsWith('```')) {
        if (inCodeBlock) {
          inCodeBlock = false;
          renderedElements.push(
            <pre
              key={`code-${idx}`}
              className="bg-surface-800 border border-line rounded-[var(--radius-control)] p-3 my-3 overflow-x-auto text-[11px] font-mono text-content-secondary leading-relaxed"
            >
              <code>{codeBlockLines.join('\n')}</code>
            </pre>
          );
          codeBlockLines = [];
        } else {
          inCodeBlock = true;
          codeBlockLines = [];
        }
        return;
      }

      if (inCodeBlock) {
        codeBlockLines.push(line);
        return;
      }

      const trimmed = line.trim();
      if (!trimmed) {
        renderedElements.push(<div key={`empty-${idx}`} className="h-2" />);
        return;
      }

      if (trimmed.startsWith('# ')) {
        renderedElements.push(
          <h1
            key={`h1-${idx}`}
            className="text-lg font-bold text-content-primary mt-6 mb-3 tracking-tight border-b border-line pb-2 flex items-center gap-2"
          >
            <span className="w-1.5 h-5 bg-namines-teal-vibrant rounded-full inline-block shrink-0" />
            {trimmed.slice(2)}
          </h1>
        );
      } else if (trimmed.startsWith('## ')) {
        renderedElements.push(
          <h2
            key={`h2-${idx}`}
            className="text-base font-bold text-content-primary mt-5 mb-2.5 tracking-tight flex items-center gap-2"
          >
            <span className="w-1.5 h-4 bg-content-muted rounded-full inline-block shrink-0" />
            {trimmed.slice(3)}
          </h2>
        );
      } else if (trimmed.startsWith('### ')) {
        renderedElements.push(
          <h3
            key={`h3-${idx}`}
            className="text-xs font-bold text-content-secondary mt-4 mb-2 flex items-center gap-1.5"
          >
            <span className="w-1 h-3 bg-content-muted/60 rounded-full inline-block shrink-0" />
            {trimmed.slice(4)}
          </h3>
        );
      } else if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
        renderedElements.push(
          <ul
            key={`ul-${idx}`}
            className="list-disc list-inside ml-4 text-xs text-content-secondary my-1 space-y-1.5"
          >
            <li className="marker:text-content-muted pl-1">
              <span className="text-content-secondary">{trimmed.slice(2)}</span>
            </li>
          </ul>
        );
      } else if (trimmed.startsWith('> ')) {
        renderedElements.push(
          <blockquote
            key={`bq-${idx}`}
            className="border-l-2 border-line-strong bg-surface-800/60 px-4 py-2.5 rounded-r-[var(--radius-control)] my-3 text-xs text-content-muted font-medium italic leading-relaxed"
          >
            {trimmed.slice(2)}
          </blockquote>
        );
      } else if (trimmed === '---') {
        renderedElements.push(<hr key={`hr-${idx}`} className="border-line my-5" />);
      } else {
        renderedElements.push(
          <p
            key={`p-${idx}`}
            className="text-xs text-content-muted leading-relaxed my-2 font-normal"
          >
            {line}
          </p>
        );
      }
    });

    if (inTable) {
      flushTable(9999);
    }

    return <div className="space-y-1">{renderedElements}</div>;
  };

  if (viewMode === 'raw') {
    return (
      <CodeStudioViewer
        code={readmeText}
        language="markdown"
        filename="README.md"
        dialectBadge="Markdown"
        headerControls={
          <div className="flex items-center gap-1 bg-surface-700/80 p-0.5 rounded-[var(--radius-control)] border border-line">
            <button
              onClick={() => setViewMode('preview')}
              className="tap-44 flex items-center gap-1 px-2 py-0.5 rounded-[var(--radius-control)] text-xs text-content-muted hover:text-content-primary"
            >
              <Eye className="w-3 h-3" />
              <span>Preview</span>
            </button>
            <button
              onClick={() => setViewMode('raw')}
              className="tap-44 flex items-center gap-1 px-2 py-0.5 rounded-[var(--radius-control)] text-xs bg-surface-800 text-content-primary font-bold shadow-xs"
            >
              <Code2 className="w-3 h-3" />
              <span>Raw</span>
            </button>
          </div>
        }
        onDownload={handleDownloadReadme}
        downloadLabel="Download .md"
        isDownloading={isDownloading}
        statusText="AI Generated Documentation · Markdown Format"
      />
    );
  }

  return (
    <div className="w-full h-full bg-surface-900 rounded-[var(--radius-card)] border border-line-strong flex flex-col overflow-hidden select-none">
      
      {/* ── Top Header Bar ──────────────────────────────────────────────────── */}
      <div className="shrink-0 min-h-11 px-3.5 py-1.5 bg-surface-800 border-b border-line-strong flex flex-wrap items-center justify-between gap-2.5">
        
        {/* Left: Title & Mode Toggle */}
        <div className="flex items-center gap-2">
          <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-700/80 border border-line shadow-xs">
            <FileText className="w-3.5 h-3.5 text-namines-teal-vibrant shrink-0" />
            <span className="text-xs font-mono font-semibold text-content-primary">
              README.md
            </span>
          </div>

          <div className="flex items-center gap-1 bg-surface-700/80 p-0.5 rounded-[var(--radius-control)] border border-line">
            <button
              onClick={() => setViewMode('preview')}
              className={`tap-44 flex items-center gap-1 px-2.5 py-1 rounded-[var(--radius-control)] text-xs font-medium cursor-pointer ${
                viewMode === 'preview' ? 'bg-surface-800 text-content-primary shadow-xs' : 'text-content-muted hover:text-content-primary'
              }`}
            >
              <Eye className="w-3 h-3" />
              <span>Preview</span>
            </button>
            <button
              onClick={() => setViewMode('raw')}
              className="tap-44 flex items-center gap-1 px-2.5 py-1 rounded-[var(--radius-control)] text-xs font-medium cursor-pointer text-content-muted hover:text-content-primary"
            >
              <Code2 className="w-3 h-3" />
              <span>Raw</span>
            </button>
          </div>

          <div className="flex items-center gap-0.5 bg-surface-700/80 p-0.5 rounded-[var(--radius-control)] border border-line">
            <button
              onClick={() => setLang('en')}
              className={`tap-44 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold cursor-pointer ${
                lang === 'en' ? 'bg-surface-800 text-content-primary shadow-xs' : 'text-content-muted hover:text-content-secondary'
              }`}
            >
              EN
            </button>
            <button
              onClick={() => setLang('tr')}
              className={`tap-44 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-bold cursor-pointer ${
                lang === 'tr' ? 'bg-surface-800 text-content-primary shadow-xs' : 'text-content-muted hover:text-content-secondary'
              }`}
            >
              TR
            </button>
          </div>
        </div>

        {/* Right: Actions */}
        <div className="flex items-center gap-1.5 shrink-0">
          <button
            onClick={fetchReadme}
            disabled={isLoading}
            className="tap-44 flex items-center gap-1.5 h-8 px-2.5 rounded-[var(--radius-control)] bg-surface-700/80 hover:bg-surface-700 border border-line text-xs font-medium text-content-secondary hover:text-content-primary transition-colors cursor-pointer"
            title="Regenerate README"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isLoading ? 'animate-spin' : ''}`} />
            <span className="text-[11px]">Regenerate</span>
          </button>

          <button
            onClick={handleCopyRaw}
            className="tap-44 flex items-center gap-1.5 h-8 px-2.5 rounded-[var(--radius-control)] bg-surface-700/80 hover:bg-surface-700 border border-line text-xs font-medium text-content-primary transition-colors cursor-pointer"
            title="Copy markdown text"
          >
            {copied ? <Check className="w-3.5 h-3.5 text-namines-teal-vibrant" /> : <Copy className="w-3.5 h-3.5 text-content-muted" />}
            <span className="text-[11px]">{copied ? 'Copied' : 'Copy'}</span>
          </button>

          <button
            onClick={handleDownloadReadme}
            disabled={isDownloading || isLoading}
            className="tap-44 flex items-center gap-1.5 h-8 px-3 rounded-[var(--radius-control)] bg-content-primary hover:opacity-90 text-surface-900 text-xs font-semibold transition-colors cursor-pointer disabled:opacity-50"
            title="Download README.md"
          >
            <Download className="w-3.5 h-3.5" />
            <span className="text-[11px]">Download .md</span>
          </button>
        </div>
      </div>

      {/* ── Rendered Preview Surface ────────────────────────────────────────── */}
      <div className="flex-1 min-h-0 overflow-auto p-4 lg:p-6 bg-surface-900 select-text relative">
        {isLoading ? (
          <div className="absolute inset-0 z-20 bg-surface-900/80 backdrop-blur-sm flex items-center justify-center gap-2">
            <Loader2 className="w-4 h-4 text-namines-teal-vibrant animate-spin" />
            <span className="text-xs text-content-muted">Generating AI Documentation…</span>
          </div>
        ) : readmeText ? (
          <div className="max-w-3xl mx-auto space-y-4">
            {renderMarkdown(readmeText)}
          </div>
        ) : (
          <PanelEmpty
            icon={FileText}
            title="No README yet"
            hint="Generate documentation from your schema with the refresh action."
          />
        )}
      </div>

      {/* ── Bottom Status Strip ─────────────────────────────────────────────── */}
      <div className="shrink-0 h-7 px-3 bg-surface-800/90 border-t border-line-strong flex items-center justify-between text-[10px] font-mono text-content-muted">
        <span>Rich Markdown View</span>
        <span>AI Generated Documentation</span>
      </div>
    </div>
  );
}
