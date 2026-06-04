'use client';

import React, { useState, useEffect, useRef } from 'react';
import { Download, FileText, Loader2, RefreshCw, Copy, Check } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import Prism from 'prismjs';
import 'prismjs/components/prism-markdown';
import 'prismjs/themes/prism-tomorrow.css';

interface ReadmePreviewProps {
  schema: DatabaseSchema;
}

type ViewMode = 'preview' | 'raw';

export default function ReadmePreview({ schema }: ReadmePreviewProps) {
  const showToast = useToastStore(state => state.showToast);
  const [readmeText, setReadmeText] = useState<string>('');
  const [isLoading, setIsLoading] = useState<boolean>(false);
  const [isDownloading, setIsDownloading] = useState<boolean>(false);
  const [viewMode, setViewMode] = useState<ViewMode>('preview');
  const [copied, setCopied] = useState<boolean>(false);

  const rawCodeRef = useRef<HTMLElement>(null);

  const fetchReadme = async () => {
    if (!schema) return;
    setIsLoading(true);
    try {
      const text = await schemaService.generateReadme(schema);
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
  }, [schema]);

  useEffect(() => {
    if (viewMode === 'raw' && rawCodeRef.current && readmeText) {
      Prism.highlightElement(rawCodeRef.current);
    }
  }, [viewMode, readmeText]);

  const handleDownloadReadme = async () => {
    if (!schema) return;
    setIsDownloading(true);
    try {
      const content = readmeText || (await schemaService.generateReadme(schema));
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
      console.error("Failed to download Readme", error);
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

    // Parse simple Markdown tables
    let inTable = false;
    let tableHeaders: string[] = [];
    let tableRows: string[][] = [];

    const flushTable = (keyIndex: number) => {
      if (tableRows.length > 0) {
        renderedElements.push(
          <div key={`table-wrapper-${keyIndex}`} className="overflow-x-auto my-4 border border-zinc-800/80 rounded-xl bg-zinc-950/30">
            <table className="w-full text-left border-collapse text-xs">
              <thead>
                <tr className="border-b border-zinc-800 bg-zinc-950/50 text-zinc-400 font-bold uppercase tracking-wider">
                  {tableHeaders.map((h, i) => (
                    <th key={`th-${keyIndex}-${i}`} className="py-2.5 px-4 font-mono">{h.replace(/\*\*/g, '').trim()}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {tableRows.map((row, rowIndex) => (
                  <tr key={`tr-${keyIndex}-${rowIndex}`} className="border-b border-zinc-900/40 hover:bg-zinc-900/10 transition-colors">
                    {row.map((cell, colIndex) => (
                      <td key={`td-${keyIndex}-${rowIndex}-${colIndex}`} className="py-2.5 px-4 text-zinc-300 leading-normal">
                        {cell.trim().startsWith('`') && cell.trim().endsWith('`') ? (
                          <code className="text-indigo-400 bg-indigo-500/5 border border-indigo-500/10 px-1 py-0.5 rounded font-mono text-[10px]">
                            {cell.replace(/`/g, '')}
                          </code>
                        ) : cell.includes('🔑') || cell.includes('🔗') ? (
                          <span className="font-semibold text-amber-500">{cell.trim()}</span>
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
      // Check if it's a table row
      if (line.trim().startsWith('|')) {
        inTable = true;
        const cells = line.split('|').map(c => c.trim()).filter((_, i, arr) => i > 0 && i < arr.length - 1);
        
        // Skip separator line | :--- | :--- |
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

      // Code block detection
      if (line.trim().startsWith('```')) {
        if (inCodeBlock) {
          inCodeBlock = false;
          renderedElements.push(
            <pre 
              key={`code-${idx}`} 
              className="bg-zinc-950 p-4 rounded-xl border border-zinc-800 my-4 text-xs font-mono text-indigo-300 overflow-x-auto shadow-inner"
            >
              <code>{codeBlockLines.join('\n')}</code>
            </pre>
          );
          codeBlockLines = [];
        } else {
          inCodeBlock = true;
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

      // Title/Headers
      if (trimmed.startsWith('# ')) {
        renderedElements.push(
          <h1 
            key={`h1-${idx}`} 
            className="text-xl font-extrabold text-white mt-6 mb-3 tracking-tight border-b border-zinc-800 pb-2 flex items-center gap-2"
          >
            <span className="w-1.5 h-6 bg-indigo-500 rounded-full inline-block shrink-0" />
            {trimmed.slice(2)}
          </h1>
        );
      } else if (trimmed.startsWith('## ')) {
        renderedElements.push(
          <h2 
            key={`h2-${idx}`} 
            className="text-lg font-bold text-zinc-100 mt-5 mb-2.5 tracking-tight flex items-center gap-2"
          >
            <span className="w-1.5 h-4.5 bg-indigo-500/50 rounded-full inline-block shrink-0" />
            {trimmed.slice(3)}
          </h2>
        );
      } else if (trimmed.startsWith('### ')) {
        renderedElements.push(
          <h3 
            key={`h3-${idx}`} 
            className="text-sm font-bold text-zinc-200 mt-4 mb-2 flex items-center gap-1.5"
          >
            <span className="w-1 h-3 bg-indigo-500/30 rounded-full inline-block shrink-0" />
            {trimmed.slice(4)}
          </h3>
        );
      } 
      // Lists
      else if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
        renderedElements.push(
          <ul 
            key={`ul-${idx}`} 
            className="list-disc list-inside ml-4 text-xs text-zinc-300 my-1 space-y-1.5"
          >
            <li className="marker:text-indigo-400 pl-1">
              <span className="text-zinc-300">{trimmed.slice(2)}</span>
            </li>
          </ul>
        );
      } 
      // Blockquotes
      else if (trimmed.startsWith('> ')) {
        renderedElements.push(
          <blockquote 
            key={`bq-${idx}`} 
            className="border-l-4 border-indigo-500/50 bg-indigo-500/5 px-4 py-2.5 rounded-r-lg my-3 text-xs text-zinc-400 font-medium italic leading-relaxed"
          >
            {trimmed.slice(2)}
          </blockquote>
        );
      } 
      // Horizontal rules
      else if (trimmed === '---') {
        renderedElements.push(<hr key={`hr-${idx}`} className="border-zinc-800/80 my-6" />);
      } 
      // Standard Paragraph
      else {
        renderedElements.push(
          <p 
            key={`p-${idx}`} 
            className="text-xs text-zinc-400 leading-relaxed my-2 font-normal"
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

  return (
    <div className="w-full h-full bg-[#030307]/60 backdrop-blur-md rounded-xl overflow-hidden border border-zinc-800/80 shadow-2xl flex flex-col">
      {/* Header section with inline actions */}
      <div className="shrink-0 px-5 py-3 bg-zinc-950/40 backdrop-blur-sm border-b border-zinc-800/60 flex justify-between items-center z-10 select-none">
        <div className="flex items-center gap-2">
          <FileText className="w-4 h-4 text-zinc-400" />
          <span className="text-xs font-semibold text-zinc-300 tracking-wide font-mono">README.md</span>
        </div>
        
        <div className="flex items-center gap-3">
          {/* Segmented view switch selector */}
          <div className="flex bg-zinc-900 border border-zinc-800 p-0.5 rounded-lg items-center">
            <button
              onClick={() => setViewMode('preview')}
              className={`px-2.5 py-1 text-[10px] font-bold uppercase rounded-md transition-all cursor-pointer ${
                viewMode === 'preview'
                  ? 'bg-zinc-800 text-indigo-400 shadow-md'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              Preview
            </button>
            <button
              onClick={() => setViewMode('raw')}
              className={`px-2.5 py-1 text-[10px] font-bold uppercase rounded-md transition-all cursor-pointer ${
                viewMode === 'raw'
                  ? 'bg-zinc-800 text-indigo-400 shadow-md'
                  : 'text-zinc-500 hover:text-zinc-300'
              }`}
            >
              MD Format
            </button>
          </div>

          <div className="w-px h-4 bg-zinc-800" />

          {/* Copy Button (only in raw mode) */}
          {viewMode === 'raw' && (
            <button
              onClick={handleCopyRaw}
              className="flex items-center gap-1 px-2.5 py-1.5 text-xs text-zinc-400 hover:text-white bg-zinc-900 border border-zinc-800 rounded-lg hover:bg-zinc-800 transition-colors cursor-pointer select-none"
              title="Copy markdown code"
            >
              {copied ? (
                <Check className="w-3.5 h-3.5 text-emerald-400 animate-pulse" />
              ) : (
                <Copy className="w-3.5 h-3.5" />
              )}
              <span>{copied ? 'Copied' : 'Copy'}</span>
            </button>
          )}

          {/* Refresh Button */}
          <button
            onClick={fetchReadme}
            disabled={isLoading}
            className="p-1.5 text-zinc-400 hover:text-white bg-zinc-900 border border-zinc-800 rounded-lg hover:bg-zinc-800 transition-colors disabled:opacity-50 cursor-pointer"
            title="Refresh README"
          >
            <RefreshCw className={`w-3.5 h-3.5 ${isLoading ? 'animate-spin' : ''}`} />
          </button>

          {/* Download Button */}
          <button
            onClick={handleDownloadReadme}
            disabled={isDownloading || isLoading}
            className="flex items-center gap-1.5 px-3 py-1.5 text-xs bg-zinc-800 hover:bg-zinc-750 text-zinc-200 border border-zinc-700 rounded-lg transition-all duration-300 disabled:opacity-50 select-none cursor-pointer active:scale-95 shadow-[0_0_15px_rgba(255,255,255,0.02)] font-medium"
          >
            {isDownloading ? (
              <Loader2 className="w-3.5 h-3.5 animate-spin" />
            ) : (
              <Download className="w-3.5 h-3.5" />
            )}
            <span>Download README.md</span>
          </button>
        </div>
      </div>

      {/* Main content display area */}
      <div className="flex-1 overflow-auto p-6 custom-scrollbar relative">
        {isLoading ? (
          <div className="absolute inset-0 z-20 bg-zinc-950/50 backdrop-blur-xs flex flex-col items-center justify-center gap-3">
            <Loader2 className="w-8 h-8 text-indigo-500 animate-spin" />
            <span className="text-xs text-zinc-400 animate-pulse font-medium">Generating documentation...</span>
          </div>
        ) : (
          <div className="h-full">
            {viewMode === 'preview' ? (
              <div className="prose prose-invert max-w-none text-zinc-300 select-text">
                {readmeText ? renderMarkdown(readmeText) : (
                  <div className="text-center py-12 text-zinc-500 text-xs">
                    No README content available.
                  </div>
                )}
              </div>
            ) : (
              <div className="h-full select-text">
                <pre className="!bg-transparent !m-0 !p-0 !text-sm h-full overflow-auto">
                  <code ref={rawCodeRef} className="language-markdown">
                    {readmeText || '# README content will appear here...'}
                  </code>
                </pre>
              </div>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
