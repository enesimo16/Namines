import React, { useEffect, useRef } from 'react';
import { Terminal, Download, XCircle, CheckCircle, Loader2, X } from 'lucide-react';
import { DockerLog } from '../../hooks/useDockerJob';

interface DockerProgressModalProps {
  isOpen: boolean;
  onClose: () => void;
  status: 'idle' | 'running' | 'done' | 'error';
  logs: DockerLog[];
  downloadUrl: string | null;
}

export default function DockerProgressModal({ isOpen, onClose, status, logs, downloadUrl }: DockerProgressModalProps) {
  const logContainerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (logContainerRef.current) {
      logContainerRef.current.scrollTop = logContainerRef.current.scrollHeight;
    }
  }, [logs]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-sm p-4 font-sans">
      <div className="bg-zinc-950 border border-zinc-800 rounded-2xl shadow-2xl w-full max-w-3xl overflow-hidden flex flex-col h-[600px]">
        
        {/* Header */}
        <div className="flex items-center justify-between px-6 py-4 bg-zinc-900 border-b border-zinc-800">
          <div className="flex items-center gap-3">
            <Terminal className="w-5 h-5 text-indigo-400" />
            <h2 className="text-lg font-bold text-zinc-100">Docker Sandbox</h2>
            
            {status === 'running' && (
              <span className="flex items-center gap-2 text-xs font-medium px-2.5 py-1 bg-indigo-500/10 text-indigo-400 rounded-full border border-indigo-500/20">
                <Loader2 className="w-3 h-3 animate-spin" /> Çalışıyor
              </span>
            )}
            {status === 'done' && (
              <span className="flex items-center gap-2 text-xs font-medium px-2.5 py-1 bg-emerald-500/10 text-emerald-400 rounded-full border border-emerald-500/20">
                <CheckCircle className="w-3 h-3" /> Tamamlandı
              </span>
            )}
            {status === 'error' && (
              <span className="flex items-center gap-2 text-xs font-medium px-2.5 py-1 bg-red-500/10 text-red-400 rounded-full border border-red-500/20">
                <XCircle className="w-3 h-3" /> Hata
              </span>
            )}
          </div>
          <button onClick={onClose} className="p-2 text-zinc-400 hover:text-white rounded-lg hover:bg-zinc-800 transition-colors">
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Terminal Logs */}
        <div className="flex-1 bg-[#0c0c0c] p-4 overflow-hidden relative">
          <div ref={logContainerRef} className="h-full overflow-y-auto custom-scrollbar font-mono text-sm space-y-1 pb-10">
            {logs.map((log, i) => (
              <div key={i} className="flex gap-4 group">
                <span className="text-zinc-600 shrink-0 select-none">
                  {new Date(log.timestamp).toLocaleTimeString([], { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                </span>
                <span className={`break-all ${log.message.startsWith('ERROR:') ? 'text-red-400 font-medium' : 'text-zinc-300 group-hover:text-zinc-100'}`}>
                  {log.message}
                </span>
              </div>
            ))}
            {status === 'running' && (
              <div className="flex gap-4 animate-pulse">
                <span className="text-zinc-600">--:--:--</span>
                <span className="text-zinc-500">_</span>
              </div>
            )}
          </div>
        </div>

        {/* Footer Actions */}
        <div className="px-6 py-4 bg-zinc-900 border-t border-zinc-800 flex justify-end gap-3">
          <button 
            onClick={onClose}
            className="px-5 py-2.5 text-sm font-medium text-zinc-300 hover:text-white hover:bg-zinc-800 rounded-lg transition-colors border border-transparent"
          >
            Kapat
          </button>
          
          <button 
            disabled={status !== 'done' || !downloadUrl}
            onClick={() => {
              if (downloadUrl) window.open(downloadUrl, '_blank');
            }}
            className="flex items-center gap-2 px-5 py-2.5 text-sm font-medium bg-indigo-600 hover:bg-indigo-500 text-white rounded-lg transition-all disabled:opacity-50 disabled:cursor-not-allowed shadow-lg shadow-indigo-500/20"
          >
            <Download className="w-4 h-4" />
            Veritabanı Yedeğini İndir (.bak / .tar)
          </button>
        </div>

      </div>
    </div>
  );
}
