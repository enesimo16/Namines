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
      {/* Breaking Change & Warnings Banner */}
      {(hasBreakingChanges || (migration?.warnings && migration.warnings.length > 0)) && (
        <div className="bg-rose-950/20 border border-rose-500/30 rounded-xl p-4 space-y-2">
          <div className="flex items-center gap-2 text-rose-400 text-sm font-bold">
            <AlertCircle className="w-5 h-5 animate-pulse" />
            <span>Kritik Veri Kaybı / Yıkıcı Değişiklik Uyarısı!</span>
          </div>
          <ul className="list-disc pl-5 space-y-1 text-xs text-rose-200/90 font-medium">
            {migration?.warnings && migration.warnings.length > 0 ? (
              migration.warnings.map((w, idx) => <li key={idx}>{w}</li>)
            ) : (
              <li>Şema karşılaştırmasında yıkıcı değişiklikler (tablo veya sütun silme, daraltma vb.) tespit edildi! Geçişi uygulamadan önce veritabanınızı yedeklediğinizden emin olun.</li>
            )}
          </ul>
        </div>
      )}

      {/* Summary Box */}
      {migration?.summary && (
        <div className="bg-[#1E293B]/40 border border-indigo-500/10 rounded-xl p-3 flex gap-2.5 items-start">
          <Award className="w-4 h-4 text-indigo-400 mt-0.5" />
          <div>
            <span className="text-indigo-300 text-xs font-bold block mb-0.5">Değişiklik Özeti</span>
            <p className="text-zinc-300 text-xs leading-relaxed">{migration.summary}</p>
          </div>
        </div>
      )}

      {/* Tab Controls & Copy Button */}
      <div className="flex items-center justify-between border-b border-zinc-800 pb-2">
        <div className="flex gap-1 bg-zinc-900/60 p-1 rounded-xl border border-zinc-800/80">
          <button
            onClick={() => setActiveTab('up')}
            className={`px-4 py-1.5 rounded-lg text-xs font-bold transition-all ${activeTab === 'up' ? 'bg-[#4f46e5] text-white shadow-md' : 'text-zinc-400 hover:text-zinc-200'}`}
          >
            Up() Metodu (Geçiş)
          </button>
          <button
            onClick={() => setActiveTab('down')}
            className={`px-4 py-1.5 rounded-lg text-xs font-bold transition-all ${activeTab === 'down' ? 'bg-[#4f46e5] text-white shadow-md' : 'text-zinc-400 hover:text-zinc-200'}`}
          >
            Down() Metodu (Geri Dönüş)
          </button>
        </div>

        <div className="flex gap-2">
          <button
            onClick={handleDownload}
            className="flex items-center gap-1.5 text-xs text-zinc-300 hover:text-emerald-400 bg-zinc-900/60 hover:bg-emerald-500/10 border border-zinc-800 hover:border-emerald-500/20 px-3.5 py-1.5 rounded-xl transition-all"
            title="C# sınıf dosyasını indir"
          >
            <Download className="w-3.5 h-3.5 text-emerald-400" />
            <span>Dosyayı İndir</span>
          </button>

          <button
            onClick={handleCopy}
            className="flex items-center gap-1.5 text-xs text-zinc-400 hover:text-white bg-zinc-900/60 hover:bg-zinc-800 border border-zinc-800 px-3.5 py-1.5 rounded-xl transition-all"
          >
            {copied ? (
              <>
                <Check className="w-3.5 h-3.5 text-emerald-400" />
                <span className="text-emerald-400 font-medium">Kopyalandı!</span>
              </>
            ) : (
              <>
                <Copy className="w-3.5 h-3.5" />
                <span>Kodu Kopyala</span>
              </>
            )}
          </button>
        </div>
      </div>

      {/* Code Display Area */}
      <div className="relative rounded-xl border border-zinc-800/80 overflow-hidden bg-zinc-950/80 backdrop-blur-md">
        <div className="flex items-center justify-between bg-zinc-900/40 border-b border-zinc-800/60 px-4 py-2">
          <div className="flex items-center gap-2">
            <FileCode className="w-4 h-4 text-indigo-400" />
            <span className="text-zinc-400 text-xs font-mono">
              {activeTab === 'up' ? 'Migration_Up.cs' : 'Migration_Down.cs'}
            </span>
          </div>
        </div>
        <pre className="p-4 text-xs font-mono max-h-[300px] overflow-auto custom-scrollbar bg-zinc-950">
          <code className="language-csharp">{codeToShow || '// Herhangi bir kod üretilmedi.'}</code>
        </pre>
      </div>
    </div>
  );
}
