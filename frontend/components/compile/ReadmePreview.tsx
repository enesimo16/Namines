'use client';

import React, { useState, useEffect, useRef } from 'react';
import { Download, FileText, Loader2, RefreshCw, Copy, Check } from 'lucide-react';
import { DatabaseSchema } from '../../types/schema';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import Prism from 'prismjs';
import 'prismjs/components/prism-markdown';
import 'prismjs/themes/prism-tomorrow.css';
import { Panel, PanelBar, ActionButton, IconButton, PanelEmpty, Segmented } from './PanelKit';

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
  const [lang, setLang] = useState<Lang>('tr');
  const [copied, setCopied] = useState<boolean>(false);

  const rawCodeRef = useRef<HTMLElement>(null);

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

  useEffect(() => {
    if (viewMode === 'raw' && rawCodeRef.current && readmeText) {
      Prism.highlightElement(rawCodeRef.current);
    }
  }, [viewMode, readmeText]);

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
      
      const successMsg = lang === 'tr' 
        ? "README.md başarıyla indirildi!" 
        : "README.md successfully downloaded!";
      showToast(successMsg, "success");
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
          <div key={`table-wrapper-${keyIndex}`} className="overflow-x-auto my-4 border border-content-primary/10 rounded-[var(--radius-card)] bg-surface-800">
            <table className="w-full text-left border-collapse text-xs">
              <thead>
                <tr className="border-b border-content-primary/8 text-content-subtle font-bold uppercase tracking-wider">
                  {tableHeaders.map((h, i) => (
                    <th key={`th-${keyIndex}-${i}`} className="py-2.5 px-4 font-mono">{h.replace(/\*\*/g, '').trim()}</th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {tableRows.map((row, rowIndex) => (
                  <tr key={`tr-${keyIndex}-${rowIndex}`} className="border-b border-content-primary/6 hover:bg-white/[0.02] transition-colors">
                    {row.map((cell, colIndex) => (
                      <td key={`td-${keyIndex}-${rowIndex}-${colIndex}`} className="py-2.5 px-4 text-content-secondary leading-normal">
                        {cell.trim().startsWith('`') && cell.trim().endsWith('`') ? (
                          <code className="text-accent-text bg-white/[0.05] px-1 py-0.5 rounded-[var(--radius-control)] font-mono text-[10px]">
                            {cell.replace(/`/g, '')}
                          </code>
                        ) : cell.includes('🔑') || cell.includes('🔗') || cell.includes('✅') || cell.includes('❌') ? (
                          <span className="font-semibold">{cell.trim()}</span>
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
              className="bg-surface-800 p-4 rounded-[var(--radius-card)] border border-content-primary/10 my-4 text-xs font-mono text-accent-text overflow-x-auto"
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
            className="text-xl font-extrabold text-content-primary mt-6 mb-3 tracking-tight border-b border-content-primary/10 pb-2 flex items-center gap-2"
          >
            <span className="w-1.5 h-6 bg-content-muted rounded-full inline-block shrink-0" />
            {trimmed.slice(2)}
          </h1>
        );
      } else if (trimmed.startsWith('## ')) {
        renderedElements.push(
          <h2
            key={`h2-${idx}`}
            className="text-lg font-bold text-content-primary mt-5 mb-2.5 tracking-tight flex items-center gap-2"
          >
            <span className="w-1.5 h-4.5 bg-content-muted/50 rounded-full inline-block shrink-0" />
            {trimmed.slice(3)}
          </h2>
        );
      } else if (trimmed.startsWith('### ')) {
        renderedElements.push(
          <h3
            key={`h3-${idx}`}
            className="text-sm font-bold text-content-secondary mt-4 mb-2 flex items-center gap-1.5"
          >
            <span className="w-1 h-3 bg-content-muted/30 rounded-full inline-block shrink-0" />
            {trimmed.slice(4)}
          </h3>
        );
      }
      // Lists
      else if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
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
      }
      // Blockquotes
      else if (trimmed.startsWith('> ')) {
        renderedElements.push(
          <blockquote
            key={`bq-${idx}`}
            className="border-l-4 border-surface-500 bg-white/[0.03] px-4 py-2.5 rounded-r-lg my-3 text-xs text-content-muted font-medium italic leading-relaxed"
          >
            {trimmed.slice(2)}
          </blockquote>
        );
      }
      // Horizontal rules
      else if (trimmed === '---') {
        renderedElements.push(<hr key={`hr-${idx}`} className="border-content-primary/10 my-6" />);
      }
      // Standard Paragraph
      else {
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

  return (
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        <PanelBar left={<span className="text-[11px] font-mono text-content-secondary">README.md</span>}>
          <Segmented
            ariaLabel="View mode"
            value={viewMode}
            onChange={setViewMode}
            options={[{ value: 'preview' as ViewMode, label: 'Preview' }, { value: 'raw' as ViewMode, label: 'MD' }]}
          />
          <Segmented
            ariaLabel="Document language"
            value={lang}
            onChange={setLang}
            options={[{ value: 'tr' as Lang, label: 'TR' }, { value: 'en' as Lang, label: 'EN' }]}
          />
          {viewMode === 'raw' && (
            <IconButton icon={copied ? Check : Copy} label={copied ? 'Copied' : 'Copy markdown'} onClick={handleCopyRaw} />
          )}
          <IconButton icon={RefreshCw} label="Regenerate README" onClick={fetchReadme} busy={isLoading} />
          <ActionButton icon={Download} onClick={handleDownloadReadme} busy={isDownloading} disabled={isLoading} tone="primary">
            Download
          </ActionButton>
        </PanelBar>

        <div className="flex-1 min-h-0 overflow-auto relative">
          {isLoading ? (
            <div className="absolute inset-0 z-20 bg-surface-900/70 backdrop-blur-sm flex items-center justify-center gap-2">
              <Loader2 className="w-4 h-4 text-content-muted animate-spin" />
              <span className="text-[11px] text-content-muted">Generating documentation…</span>
            </div>
          ) : viewMode === 'preview' ? (
            readmeText ? (
              <div className="prose prose-invert max-w-none text-content-secondary select-text p-3 text-[12px]">
                {renderMarkdown(readmeText)}
              </div>
            ) : (
              <PanelEmpty icon={FileText} title="No README yet" hint="Generate documentation from your schema with the refresh action." />
            )
          ) : (
            <pre className="!bg-transparent !m-0 !p-3 !text-[11px] !leading-relaxed select-text">
              <code ref={rawCodeRef} className="language-markdown">
                {readmeText || '# README content will appear here...'}
              </code>
            </pre>
          )}
        </div>
      </div>
    </Panel>
  );
}
