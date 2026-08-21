import React, { useEffect, useState } from 'react';
import { MigrationResult } from '../../types/migration';
import { AlertCircle, Copy, Check, FileCode, Award, Download } from 'lucide-react';
import Prism from 'prismjs';
import 'prismjs/components/prism-csharp';

interface MigrationCodeViewProps {
  migration: MigrationResult;
  hasBreakingChanges: boolean;
}

export default function MigrationCodeView({ migration, hasBreakingChanges }: MigrationCodeViewProps) {
  const [activeTab, setActiveTab] = useState<'up' | 'down'>('up');
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    Prism.highlightAll();
  }, [activeTab, migration]);

  const handleCopy = () => {
    const codeToCopy = activeTab === 'up' ? migration.upCode : migration.downCode;
    navigator.clipboard.writeText(codeToCopy);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const handleDownload = () => {
    const code = activeTab === 'up' ? migration.upCode : migration.downCode;
    const filename = activeTab === 'up' ? 'Migration_Up.cs' : 'Migration_Down.cs';
    const blob = new Blob([code], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = filename;
    link.click();
    URL.revokeObjectURL(url);
  };

  const codeToShow = activeTab === 'up' ? migration.upCode : migration.downCode;

  return (
    <div className="space-y-4">
      {/* Breaking Change & Warnings Banner — semantik danger, desatüre */}
      {(hasBreakingChanges || (migration?.warnings && migration.warnings.length > 0)) && (
        <div className="bg-danger-subtle border border-danger/25 rounded-xl p-3.5 space-y-2">
          <div className="flex items-center gap-2 text-danger-text text-sm font-bold">
            <AlertCircle className="w-4 h-4" />
            <span>Critical Data Loss / Destructive Change Warning!</span>
          </div>
          <ul className="list-disc pl-5 space-y-1 text-xs text-content-secondary">
            {migration?.warnings && migration.warnings.length > 0 ? (
              migration.warnings.map((w, idx) => <li key={idx}>{w}</li>)
            ) : (
              <li>Destructive changes (table or column deletion, narrowing, etc.) were detected. Back up your database before applying this migration.</li>
            )}
          </ul>
        </div>
      )}

      {/* Summary Box */}
      {migration?.summary && (
        <div className="bg-surface-700 rounded-xl p-3 flex gap-2.5 items-start">
          <FileCode className="w-4 h-4 text-content-muted mt-0.5" />
          <div>
            <span className="text-content-secondary text-xs font-bold block mb-0.5">Change Summary</span>
            <p className="text-content-muted text-xs leading-relaxed">{migration.summary}</p>
          </div>
        </div>
      )}

      {/* Tab Controls & Copy Button */}
      <div className="flex items-center justify-between border-b border-content-primary/8 pb-2">
        <div className="flex gap-1 bg-surface-700 p-1 rounded-lg">
          <button
            onClick={() => setActiveTab('up')}
            className={`px-3.5 py-1.5 rounded-md text-xs font-semibold transition-all ${activeTab === 'up' ? 'bg-white/[0.1] text-content-primary' : 'text-content-subtle hover:text-content-secondary'}`}
          >
            Up() Method (Apply)
          </button>
          <button
            onClick={() => setActiveTab('down')}
            className={`px-3.5 py-1.5 rounded-md text-xs font-semibold transition-all ${activeTab === 'down' ? 'bg-white/[0.1] text-content-primary' : 'text-content-subtle hover:text-content-secondary'}`}
          >
            Down() Method (Rollback)
          </button>
        </div>

        <div className="flex gap-2">
          <button
            onClick={handleDownload}
            className="flex items-center gap-1.5 text-xs text-content-secondary hover:text-content-primary bg-surface-700 hover:bg-surface-600 px-3 py-1.5 rounded-lg transition-all"
            title="Download C# class file"
          >
            <Download className="w-3.5 h-3.5" />
            <span>Download File</span>
          </button>

          <button
            onClick={handleCopy}
            className="flex items-center gap-1.5 text-xs text-content-muted hover:text-content-primary bg-surface-700 hover:bg-surface-600 px-3 py-1.5 rounded-lg transition-all"
          >
            {copied ? (
              <>
                <Check className="w-3.5 h-3.5 text-success-text" />
                <span className="text-success-text font-medium">Copied!</span>
              </>
            ) : (
              <>
                <Copy className="w-3.5 h-3.5" />
                <span>Copy Code</span>
              </>
            )}
          </button>
        </div>
      </div>

      {/* Code Display Area */}
      <div className="relative rounded-xl overflow-hidden bg-surface-700">
        <div className="flex items-center justify-between bg-surface-600 px-3.5 py-2">
          <div className="flex items-center gap-2">
            <FileCode className="w-3.5 h-3.5 text-content-subtle" />
            <span className="text-content-muted text-xs font-mono">
              {activeTab === 'up' ? 'Migration_Up.cs' : 'Migration_Down.cs'}
            </span>
          </div>
        </div>
        <pre className="p-3.5 text-xs font-mono max-h-[300px] overflow-auto bg-surface-800">
          <code className="language-csharp">{codeToShow || '// No code generated.'}</code>
        </pre>
      </div>
    </div>
  );
}
