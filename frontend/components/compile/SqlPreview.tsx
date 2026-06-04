import React, { useEffect, useRef, useState } from 'react';
import Prism from 'prismjs';
import 'prismjs/components/prism-sql';
import 'prismjs/themes/prism-tomorrow.css'; // Dark theme
import { Copy, Download, Check } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';

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

  return (
    <div className="w-full h-full bg-[#030307]/60 backdrop-blur-md rounded-xl overflow-hidden border border-zinc-800/80 shadow-2xl relative flex flex-col">
      {/* Header section with inline action buttons */}
      <div className="shrink-0 px-4 py-2 bg-zinc-950/40 backdrop-blur-sm border-b border-zinc-800/60 flex justify-between items-center z-10 select-none">
        <span className="text-xs text-zinc-400 font-mono">schema.sql</span>
        
        <div className="flex items-center gap-2">
          {/* Copy Button */}
          <button
            onClick={handleCopy}
            className="flex items-center gap-1 px-2.5 py-1 text-[11px] font-medium text-zinc-400 hover:text-white bg-zinc-900 border border-zinc-800 rounded-md hover:bg-zinc-800 transition-all cursor-pointer active:scale-95 select-none"
            title="Copy SQL code"
          >
            {copied ? (
              <Check className="w-3 h-3 text-emerald-400 animate-pulse" />
            ) : (
              <Copy className="w-3 h-3" />
            )}
            <span>{copied ? 'Copied' : 'Copy'}</span>
          </button>

          {/* Download Button */}
          <button
            onClick={handleDownloadSql}
            className="flex items-center gap-1 px-2.5 py-1 text-[11px] font-medium text-blue-400 hover:text-white bg-blue-500/10 border border-blue-500/20 hover:bg-blue-500/20 rounded-md transition-all cursor-pointer active:scale-95 select-none"
            title="Download SQL file"
          >
            <Download className="w-3 h-3" />
            <span>Download</span>
          </button>
        </div>
      </div>

      {/* Code contents scrollable area */}
      <div className="flex-1 overflow-auto custom-scrollbar pt-2">
        <pre className="!bg-transparent !m-0 !p-4 !text-sm">
          <code ref={codeRef} className="language-sql">
            {sql || '-- DDL Script will appear here...'}
          </code>
        </pre>
      </div>
    </div>
  );
}

