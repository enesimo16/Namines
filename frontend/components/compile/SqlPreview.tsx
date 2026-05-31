import React, { useEffect, useRef } from 'react';
import Prism from 'prismjs';
import 'prismjs/components/prism-sql';
import 'prismjs/themes/prism-tomorrow.css'; // Dark theme

interface SqlPreviewProps {
  sql: string;
}

export default function SqlPreview({ sql }: SqlPreviewProps) {
  const codeRef = useRef<HTMLElement>(null);

  useEffect(() => {
    if (codeRef.current) {
      Prism.highlightElement(codeRef.current);
    }
  }, [sql]);

  return (
    <div className="w-full h-full bg-[#030307]/60 backdrop-blur-md rounded-xl overflow-hidden border border-zinc-800/80 shadow-2xl relative">
      <div className="absolute top-0 left-0 w-full px-4 py-2 bg-zinc-950/40 backdrop-blur-sm border-b border-zinc-800/60 flex justify-between items-center z-10">
        <span className="text-xs text-zinc-400 font-mono">schema.sql</span>
      </div>
      <div className="h-full pt-10 overflow-auto custom-scrollbar">
        <pre className="!bg-transparent !m-0 !p-4 !text-sm">
          <code ref={codeRef} className="language-sql">
            {sql || '-- DDL Script will appear here...'}
          </code>
        </pre>
      </div>
    </div>
  );
}
