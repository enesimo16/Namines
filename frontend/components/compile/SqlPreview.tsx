import React, { useEffect, useRef, useState } from 'react';
import Prism from 'prismjs';
import 'prismjs/components/prism-sql';
import 'prismjs/themes/prism-tomorrow.css'; // Dark theme
import { Copy, Download, Check } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { Panel, PanelBar, IconButton } from './PanelKit';

interface SqlPreviewProps {
  sql: string;
}

export default function SqlPreview({ sql }: SqlPreviewProps) {
  const codeRef = useRef<HTMLElement>(null);
  const { schema } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (codeRef.current) {
      Prism.highlightElement(codeRef.current);
    }
  }, [sql]);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(sql);
      setCopied(true);
      showToast("SQL code successfully copied to clipboard!", "success");
      setTimeout(() => setCopied(false), 2000);
    } catch (err) {
      console.error("Copy error:", err);
      showToast("Failed to copy SQL to clipboard.", "error");
    }
  };

  const handleDownloadSql = () => {
    try {
      const blob = new Blob([sql], { type: 'text/plain;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${schema?.name || 'database'}_schema.sql`;
      document.body.appendChild(a);
      a.click();
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
      showToast("SQL script successfully downloaded!", "success");
    } catch (err) {
      console.error("Download error:", err);
      showToast("Failed to download SQL script.", "error");
    }
  };

  const lineCount = sql ? sql.split('\n').length : 0;

  return (
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        <PanelBar
          left={
            <>
              <span className="text-[11px] font-mono text-content-secondary truncate">
                {schema?.name || 'database'}_schema.sql
              </span>
              {lineCount > 0 && (
                <span className="text-[10px] font-mono text-content-muted shrink-0">{lineCount} ln</span>
              )}
            </>
          }
        >
          <IconButton icon={copied ? Check : Copy} label={copied ? 'Copied' : 'Copy SQL'} onClick={handleCopy} />
          <IconButton icon={Download} label="Download .sql" onClick={handleDownloadSql} tone="primary" />
        </PanelBar>

        <div className="flex-1 min-h-0 overflow-auto bg-surface-900">
          <pre className="!bg-transparent !m-0 !p-3 !text-[11px] !leading-relaxed">
            <code ref={codeRef} className="language-sql">
              {sql || '-- DDL Script will appear here...'}
            </code>
          </pre>
        </div>
      </div>
    </Panel>
  );
}
