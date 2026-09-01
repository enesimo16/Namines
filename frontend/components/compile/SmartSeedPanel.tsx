'use client';

import React, { useState, useEffect, useRef } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { DbType } from '../../store/useSchemaStore';
import { smartSeedService } from '../../services/api';
import {
  Download, Copy, Check, Loader2,
  Database, Info
} from 'lucide-react';
import { Panel, PanelBar, ActionButton, IconButton, PanelEmpty, StatStrip } from './PanelKit';
import Prism from 'prismjs';
import 'prismjs/components/prism-sql';
import 'prismjs/themes/prism-tomorrow.css';

import { useAIGateway } from '../../hooks/useAIGateway';
import { useQuotaStore } from '../../store/useQuotaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useToastStore } from '../../store/useToastStore';
import ContextualHelpTooltip from '../help/ContextualHelpTooltip';
import { helpContent } from '../../lib/helpContent';

interface SmartSeedPanelProps {
  schema: DatabaseSchema;
  dbType: DbType;
}

export default function SmartSeedPanel({ schema, dbType }: SmartSeedPanelProps) {
  const [domainHint, setDomainHint] = useState<string>('');
  const [rowCount, setRowCount] = useState<number>(50);
  const [isGenerating, setIsGenerating] = useState<boolean>(false);
  const [copied, setCopied] = useState<boolean>(false);

  const { checkAccess } = useAIGateway();
  const { remaining } = useQuotaStore();
  const { isAuthenticated } = useAuthStore();
  const showToast = useToastStore(state => state.showToast);

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
      const data = await smartSeedService.generate(schema, dbType, domainHint || undefined, rowCount, false);
      setResult(data);
    } catch (err: any) {
      if (err?.response?.status === 429) {
        showToast('Daily AI limit reached! Please upgrade your plan for unlimited test data.', 'warning');
      } else {
        console.error("Test data generation error:", err);
        showToast(`Test data generation failed: ${err.message || 'Unknown error'}`, 'error');
      }
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
      console.error("Copy error:", err);
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

  // h-9: aynı şeritteki `ActionButton` ("Generate") da h-9. Bu iki select h-8
  // kaldığı için Test Data şeridinde gözle görülür bir hiza kayması vardı.
  const selectCls =
    'bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] h-9 pl-2.5 pr-6 text-[11px] text-content-secondary focus:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)] cursor-pointer disabled:opacity-50';

  return (
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        {/* Kontroller ayrı bir kart DEĞİL — tek satır şeritte (bir kart yüksekliği kazanıldı) */}
        <PanelBar
          left={
            <>
              <select
                value={domainHint}
                onChange={(e) => setDomainHint(e.target.value)}
                disabled={isGenerating}
                aria-label="Sector / domain"
                className={selectCls}
              >
                <option value="">Auto detect sector</option>
                <option value="E-Commerce">E-Commerce</option>
                <option value="Logistics & Transportation">Logistics &amp; Transportation</option>
                <option value="Healthcare & Medical">Healthcare &amp; Medical</option>
                <option value="Education & Academia">Education &amp; Academia</option>
                <option value="Finance & Banking">Finance &amp; Banking</option>
                <option value="Human Resources">Human Resources</option>
                <option value="Real Estate">Real Estate</option>
                <option value="Manufacturing & Supply Chain">Manufacturing &amp; Supply Chain</option>
                <option value="Telecommunications">Telecommunications</option>
                <option value="Travel & Hospitality">Travel &amp; Hospitality</option>
                <option value="Insurance">Insurance</option>
                <option value="Media & Entertainment">Media &amp; Entertainment</option>
                <option value="Government & Public Sector">Government &amp; Public Sector</option>
                <option value="Retail & Inventory">Retail &amp; Inventory</option>
              </select>

              <select
                value={rowCount}
                onChange={(e) => setRowCount(Number(e.target.value))}
                disabled={isGenerating}
                aria-label="Rows per table"
                className={selectCls}
              >
                <option value="10">10 rows</option>
                <option value="25">25 rows</option>
                <option value="50">50 rows</option>
                <option value="100">100 rows</option>
                <option value="200">200 rows</option>
                <option value="500">500 rows (max)</option>
              </select>

              {rowCount === 500 && (
                <span className="hidden xl:flex items-center gap-1 text-[10px] text-content-muted">
                  <Info className="w-3 h-3 shrink-0" />
                  May exceed browser WASM memory.
                </span>
              )}
            </>
          }
        >
          {result && (
            <>
              <IconButton icon={copied ? Check : Copy} label={copied ? 'Copied' : 'Copy SQL'} onClick={handleCopy} />
              <IconButton icon={Download} label="Download seed_data.sql" onClick={handleDownload} />
            </>
          )}
          <ActionButton
            onClick={handleGenerate}
            busy={isGenerating}
            disabled={!schema.tables || schema.tables.length === 0}
            tone="primary"
          >
            {isGenerating ? 'Generating…' : 'Generate'}
          </ActionButton>
        </PanelBar>

        {result && (
          <StatStrip
            items={[
              { label: 'Sector', value: result.detectedDomain },
              { label: 'Rows', value: totalGeneratedRows },
              { label: 'Size', value: formatSize(result.estimatedSizeBytes) },
            ]}
          />
        )}

        <div className="flex-1 min-h-0 relative">
          {isGenerating && (
            <div className="absolute inset-0 z-20 bg-surface-900/70 backdrop-blur-sm flex items-center justify-center gap-2">
              <Loader2 className="w-4 h-4 text-content-muted animate-spin" />
              <span className="text-[11px] text-content-muted">Generating test data…</span>
            </div>
          )}

          {result ? (
            <div className="h-full overflow-auto bg-surface-900">
              <pre className="!bg-transparent !m-0 !p-3 !text-[11px] !leading-relaxed">
                <code ref={codeRef} className="language-sql">
                  {result.sqlScript}
                </code>
              </pre>
            </div>
          ) : (
            <PanelEmpty
              icon={Database}
              title="No test data yet"
              hint="Reads your schema's relations and domain model to produce consistent, realistic rows. Pick a sector or let it auto-detect, then hit Generate."
            >
              <ContextualHelpTooltip content={helpContent.smartSeed} />
            </PanelEmpty>
          )}
        </div>
      </div>
    </Panel>
  );
}
