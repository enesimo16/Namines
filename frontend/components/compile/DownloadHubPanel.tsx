'use client';

import { useState, useEffect, useRef } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { DbType } from '../../store/useSchemaStore';
import {
  Download, Loader2, RefreshCw, CheckCircle2,
  Sparkles, Crown, Code2, Cpu
} from 'lucide-react';
import { scaffolderService, coderAIService } from '../../services/api';
import { useAIGateway } from '../../hooks/useAIGateway';
import { toAbsoluteApiUrl } from '../../lib/apiConfig';

interface DownloadHubPanelProps {
  schema: DatabaseSchema;
  dbType: DbType;
}

export default function DownloadHubPanel({ schema, dbType }: DownloadHubPanelProps) {
  const [status, setStatus] = useState<'idle' | 'generating' | 'success' | 'error'>('idle');
  const [logs, setLogs] = useState<string[]>([]);
  const [downloadUrl, setDownloadUrl] = useState<string | null>(null);
  const [isExportingPython, setIsExportingPython] = useState(false);

  const { checkAccess } = useAIGateway();

  const logsEndRef = useRef<HTMLDivElement | null>(null);

  // Auto-scroll logs
  useEffect(() => {
    logsEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [logs]);

  const handleExportPython = async () => {
    setIsExportingPython(true);
    try {
      const blob = await scaffolderService.exportPythonProject(schema);
      const url = window.URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${schema.name || 'streamlit_admin'}_python_crud.zip`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      window.URL.revokeObjectURL(url);
    } catch (err) {
      console.error("Python export error:", err);
    } finally {
      setIsExportingPython(false);
    }
  };

  const handleGenerate = async () => {
    if (!checkAccess("AI Coder Sandbox Generation")) return;
    setStatus('generating');
    setLogs(['🚀 Admin panel package generation request sent...', '⏳ AI engine is preparing...']);
    setDownloadUrl(null);

    try {
      const jobId = await coderAIService.generate(schema, dbType, true);
      setLogs(prev => [...prev, `📋 Project ID assigned: ${jobId.substring(0, 8)}...`]);

      // Connect to SSE for real-time progress logs
      const streamUrl = coderAIService.getStreamUrl(jobId);
      const sse = new EventSource(streamUrl);

      sse.onmessage = (e) => {
        const msg: string = e.data;

        if (msg === 'DONE') {
          sse.close();
          setStatus('success');
          return;
        }

        if (msg.startsWith('ERROR:')) {
          setStatus('error');
          setLogs(prev => [...prev, `❌ Error: ${msg}`]);
          sse.close();
          return;
        }

        if (msg.startsWith('DOWNLOAD_URL|')) {
          const path = msg.split('|')[1];
          const fullUrl = toAbsoluteApiUrl(path);
          setDownloadUrl(fullUrl);
          setLogs(prev => [...prev, '📦 Project package (.zip) successfully generated!']);
          
          // Auto trigger download
          const link = document.createElement('a');
          link.href = fullUrl;
          link.download = `${schema.name || 'streamlit_admin'}_admin_panel.zip`;
          document.body.appendChild(link);
          link.click();
          document.body.removeChild(link);
          return;
        }

        setLogs(prev => [...prev, msg]);
      };

      sse.onerror = () => {
        sse.close();
        setStatus('error');
        setLogs(prev => [...prev, '❌ Connection to server lost. Please try again.']);
      };

    } catch (err: any) {
      setStatus('error');
      if (err?.response?.status === 429) {
        setLogs(prev => [...prev, '❌ ERROR: Daily AI Limit Reached. Please upgrade to Pro for unlimited generation.']);
      } else {
        setLogs(prev => [...prev, `❌ ERROR: ${err.message}`]);
      }
    }
  };

  const resetPanel = () => {
    setStatus('idle');
    setLogs([]);
    setDownloadUrl(null);
  };

  // ─────────────────────────────────────────────
  // GENERATING state - Show Terminal logs
  // ─────────────────────────────────────────────
  if (status === 'generating') {
    return (
      <div className="w-full h-full flex flex-col items-center justify-center bg-[#0b0c10] rounded-2xl border border-zinc-800/60 overflow-hidden relative min-h-[520px]">
        {/* Local Styles for Ocean Waves inside Panel */}
        <style dangerouslySetInnerHTML={{__html: `
          .sandbox-ocean-wave {
            position: absolute;
            bottom: 0;
            left: 0;
            width: 100%;
            height: 180px;
            background: linear-gradient(to top, rgba(11, 12, 16, 1) 0%, transparent 100%);
            z-index: 0;
            pointer-events: none;
            overflow: hidden;
            border-bottom-left-radius: 1rem;
            border-bottom-right-radius: 1rem;
          }
          .sandbox-wave {
            position: absolute;
            bottom: 0;
            left: 0;
            width: 200%;
            height: 100%;
            background-size: 50% 100%;
            animation: sandbox-wave-anim 20s linear infinite;
          }
          .s-wave1 {
            background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%2306b6d4" fill-opacity="0.2" d="M0,160L48,144C96,128,192,96,288,106.7C384,117,480,171,576,165.3C672,160,768,96,864,85.3C960,75,1056,117,1152,149.3C1248,181,1344,203,1392,213.3L1440,224L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
            animation-duration: 22s;
            opacity: 0.6;
          }
          .s-wave2 {
            background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%230ea5e9" fill-opacity="0.15" d="M0,192L48,197.3C96,203,192,213,288,213.3C384,213,480,203,576,186.7C672,171,768,149,864,154.7C960,160,1056,192,1152,202.7C1248,213,1344,203,1392,197.3L1440,192L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
            animation-direction: reverse;
            animation-duration: 28s;
            opacity: 0.5;
          }
          .s-wave3 {
            background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%231e3a8a" fill-opacity="0.25" d="M0,224L48,208C96,192,192,160,288,160C384,160,480,192,576,213.3C672,235,768,245,864,229.3C960,213,1056,171,1152,149.3C1248,128,1344,128,1392,128L1440,128L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
            animation-duration: 36s;
            opacity: 0.7;
          }
          @keyframes sandbox-wave-anim {
            0% { transform: translateX(0); }
            100% { transform: translateX(-50%); }
          }
        `}} />

        {/* Ambient deep blue/indigo background glow behind terminal */}
        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[650px] h-[450px] bg-indigo-500/5 rounded-full blur-[130px] pointer-events-none" />

        {/* Wave Overlays */}
        <div aria-hidden="true" className="sandbox-ocean-wave">
          <div className="sandbox-wave s-wave1" />
          <div className="sandbox-wave s-wave2" />
          <div className="sandbox-wave s-wave3" />
        </div>

        {/* Shimmering Star Overlays */}
        <div className="absolute inset-0 pointer-events-none opacity-40 z-0">
          <div className="absolute top-[15%] left-[25%] w-1 h-1 bg-white/80 rounded-full blur-[0.5px] animate-pulse" />
          <div className="absolute top-[30%] left-[75%] w-0.5 h-0.5 bg-white rounded-full" />
          <div className="absolute top-[75%] left-[10%] w-1.5 h-1.5 bg-white/60 rounded-full blur-[1px] animate-pulse" />
          <div className="absolute top-[20%] left-[60%] w-0.5 h-0.5 bg-white/95 rounded-full" />
        </div>

        {/* High-Fidelity Black Terminal (Namines Sistem Log Terminali Replica) */}
        <div className="relative z-10 w-full max-w-[750px] mx-6 bg-black/85 backdrop-blur-xl border border-white/5 rounded-3xl shadow-[0_20px_50px_rgba(0,0,0,0.6)] flex flex-col overflow-hidden" style={{ height: '440px' }}>

          {/* macOS-style Header */}
          <div className="shrink-0 flex items-center px-5 py-4 bg-[#0e0e12]/90 border-b border-white/5 select-none">
            {/* macOS window control buttons */}
            <div className="flex gap-2">
              <span className="w-3 h-3 rounded-full bg-[#ff5f56]/85" />
              <span className="w-3 h-3 rounded-full bg-[#ffbd2e]/85" />
              <span className="w-3 h-3 rounded-full bg-[#27c93f]/85" />
            </div>
            
            {/* Terminal Title */}
            <div className="flex items-center gap-2 ml-4">
              <span className="text-[10px] text-zinc-500 font-mono tracking-widest font-black uppercase">
                &gt;_ SYSTEM LOG – CODERAI
              </span>
            </div>

            {/* Right-aligned dynamic loader */}
            <Loader2 className="w-4 h-4 text-sky-400 animate-spin ml-auto drop-shadow-[0_0_8px_rgba(56,189,248,0.5)]" />
          </div>

          {/* Log Area */}
          <div className="flex-1 min-h-0 overflow-y-auto p-6 font-mono text-[13px] space-y-2 bg-[#050508]/90">
            {logs.map((log, i) => {
              // Extract timestamp or generate a generic one
              const cleanLog = log.replace(/^(🚀|📋|✅|❌|⏳|⚠️|📦)\s*/, '');
              const icon = log.match(/^(🚀|📋|✅|❌|⏳|⚠️|📦)/)?.[0] || '';
              
              let textColorClass = 'text-zinc-300';
              if (log.startsWith('🚀')) textColorClass = 'text-amber-400 font-bold';
              else if (log.startsWith('📋') || log.startsWith('📦')) textColorClass = 'text-cyan-400';
              else if (log.startsWith('✅')) textColorClass = 'text-emerald-400 font-semibold';
              else if (log.startsWith('❌') || log.startsWith('HATA')) textColorClass = 'text-red-400';
              
              return (
                <div key={i} className="flex items-start gap-4 leading-relaxed">
                  {/* Clean timestamp in muted gray */}
                  <span className="text-zinc-600 shrink-0 select-none tabular-nums">
                    {new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                  </span>
                  
                  {/* Log line with icon and correct text coloring */}
                  <div className={`flex items-center gap-1.5 ${textColorClass}`}>
                    {icon && <span className="select-none">{icon}</span>}
                    <span>{cleanLog}</span>
                  </div>
                </div>
              );
            })}

            {/* Blinking blue cursor loader at the bottom */}
            <div className="flex items-start gap-4 leading-relaxed">
              <span className="text-zinc-600 shrink-0 select-none tabular-nums">
                {new Date().toLocaleTimeString('en-US', { hour: '2-digit', minute: '2-digit', second: '2-digit' })}
              </span>
              <div className="flex items-center gap-2 text-sky-400 font-medium">
                {/* Glowing dynamic blue vertical bar */}
                <span className="w-1.5 h-4 bg-sky-400 animate-pulse shadow-[0_0_8px_rgba(56,189,248,0.7)]" />
                <span className="animate-pulse text-xs">AI is compiling your project...</span>
              </div>
            </div>
            <div ref={logsEndRef} />
          </div>
        </div>
      </div>
    );
  }

  // ─────────────────────────────────────────────
  // SUCCESS / ERROR state - Show Results
  // ─────────────────────────────────────────────
  if (status === 'success' || status === 'error') {
    return (
      <div className="w-full h-full flex flex-col items-center justify-center bg-[#0b0c10] rounded-2xl border border-zinc-800/60 p-6 relative min-h-[520px]">
        {/* Local Styles for Ocean Waves inside Panel */}
        <style dangerouslySetInnerHTML={{__html: `
          .sandbox-ocean-wave {
            position: absolute;
            bottom: 0;
            left: 0;
            width: 100%;
            height: 180px;
            background: linear-gradient(to top, rgba(7, 7, 8, 1) 0%, transparent 100%);
            z-index: 0;
            pointer-events: none;
            overflow: hidden;
            border-bottom-left-radius: 1rem;
            border-bottom-right-radius: 1rem;
          }
          .sandbox-wave {
            position: absolute;
            bottom: 0;
            left: 0;
            width: 200%;
            height: 100%;
            background-size: 50% 100%;
            animation: sandbox-wave-anim 20s linear infinite;
          }
          .s-wave1 {
            background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%2306b6d4" fill-opacity="0.2" d="M0,160L48,144C96,128,192,96,288,106.7C384,117,480,171,576,165.3C672,160,768,96,864,85.3C960,75,1056,117,1152,149.3C1248,181,1344,203,1392,213.3L1440,224L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
            animation-duration: 22s;
            opacity: 0.6;
          }
          .s-wave2 {
            background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%230ea5e9" fill-opacity="0.15" d="M0,192L48,197.3C96,203,192,213,288,213.3C384,213,480,203,576,186.7C672,171,768,149,864,154.7C960,160,1056,192,1152,202.7C1248,213,1344,203,1392,197.3L1440,192L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
            animation-direction: reverse;
            animation-duration: 28s;
            opacity: 0.5;
          }
          .s-wave3 {
            background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%231e3a8a" fill-opacity="0.25" d="M0,224L48,208C96,192,192,160,288,160C384,160,480,192,576,213.3C672,235,768,245,864,229.3C960,213,1056,171,1152,149.3C1248,128,1344,128,1392,128L1440,128L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
            animation-duration: 36s;
            opacity: 0.7;
          }
          @keyframes sandbox-wave-anim {
            0% { transform: translateX(0); }
            100% { transform: translateX(-50%); }
          }
        `}} />

        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[400px] bg-zinc-500/5 rounded-full blur-[100px] pointer-events-none" />

        {/* Wave Overlays */}
        <div aria-hidden="true" className="sandbox-ocean-wave">
          <div className="sandbox-wave s-wave1" />
          <div className="sandbox-wave s-wave2" />
          <div className="sandbox-wave s-wave3" />
        </div>

        {/* Shimmering Star Overlays */}
        <div className="absolute inset-0 pointer-events-none opacity-40 z-0">
          <div className="absolute top-[15%] left-[25%] w-1 h-1 bg-white/80 rounded-full blur-[0.5px] animate-pulse" />
          <div className="absolute top-[30%] left-[75%] w-0.5 h-0.5 bg-white rounded-full" />
          <div className="absolute top-[75%] left-[10%] w-1.5 h-1.5 bg-white/60 rounded-full blur-[1px] animate-pulse" />
          <div className="absolute top-[20%] left-[60%] w-0.5 h-0.5 bg-white/95 rounded-full" />
        </div>

        <div className="relative z-10 w-full max-w-[480px] bg-black/40 backdrop-blur-xl border border-white/5 p-8 rounded-3xl shadow-[inset_0_0_20px_rgba(255,255,255,0.02),0_12px_40px_rgba(0,0,0,0.5)] flex flex-col items-center text-center gap-6">
          {status === 'success' ? (
            <>
              {/* Glowing Green Success Check */}
              <div className="w-14 h-14 rounded-full bg-emerald-500/10 border border-emerald-500/30 flex items-center justify-center shadow-[0_0_25px_rgba(16,185,129,0.25)] select-none">
                <CheckCircle2 className="w-7 h-7 text-emerald-400" />
              </div>
              <div className="space-y-2 select-none">
                <h3 className="text-xl font-bold text-white tracking-tight">Project Successfully Prepared!</h3>
                <p className="text-xs text-zinc-400 leading-relaxed max-w-xs">Download started automatically. If it did not start, click the button below to get the package.</p>
              </div>
              <div className="flex flex-col w-full gap-3 mt-1">
                {downloadUrl && (
                  <a
                    href={downloadUrl}
                    className="w-full py-3.5 bg-emerald-600 hover:bg-emerald-500 text-white font-bold text-xs rounded-xl transition-all shadow-[0_0_20px_rgba(16,185,129,0.3)] hover:shadow-[0_0_30px_rgba(16,185,129,0.5)] hover:scale-[1.01] active:scale-[0.99] flex items-center justify-center gap-2 border border-emerald-400/20"
                  >
                    Download Project Again (.zip)
                  </a>
                )}
                <button
                  onClick={resetPanel}
                  className="w-full py-3.5 bg-[#1c1c24]/50 hover:bg-[#1c1c24]/85 text-zinc-300 hover:text-white border border-white/5 font-bold text-xs rounded-xl transition-all duration-300 cursor-pointer active:scale-98 shadow-md"
                >
                  Back to Download Hub
                </button>
              </div>
            </>
          ) : (
            <>
              {/* Glowing Red Error Check */}
              <div className="w-14 h-14 rounded-full bg-red-500/10 border border-red-500/30 flex items-center justify-center shadow-[0_0_25px_rgba(239,68,68,0.25)] select-none">
                <CheckCircle2 className="w-7 h-7 text-red-400 rotate-45" />
              </div>
              <div className="space-y-2 select-none">
                <h3 className="text-xl font-bold text-white tracking-tight">Generation Failed</h3>
                <p className="text-xs text-zinc-400 leading-relaxed max-w-xs">An issue occurred during AI code generation or packaging.</p>
              </div>
              <div className="flex flex-col w-full gap-3 mt-1">
                <button
                  onClick={handleGenerate}
                  className="w-full py-3.5 bg-indigo-600 hover:bg-indigo-500 text-white font-bold text-xs rounded-xl transition-all shadow-[0_0_20px_rgba(79,70,229,0.3)] hover:shadow-[0_0_30px_rgba(99,102,241,0.5)] hover:scale-[1.01] active:scale-[0.99] flex items-center justify-center gap-2 border border-indigo-400/20 cursor-pointer"
                >
                  Try Again
                </button>
                <button
                  onClick={resetPanel}
                  className="w-full py-3.5 bg-[#1c1c24]/50 hover:bg-[#1c1c24]/85 text-zinc-300 hover:text-white border border-white/5 font-bold text-xs rounded-xl transition-all duration-300 cursor-pointer active:scale-98 shadow-md"
                >
                  Back to Download Hub
                </button>
              </div>
            </>
          )}
        </div>
      </div>
    );
  }

  // ─────────────────────────────────────────────
  // IDLE state - Show beautiful Download Hub Cards
  // ─────────────────────────────────────────────
  return (
    <div className="w-full h-full flex flex-col justify-center items-center bg-[#0b0c10] rounded-2xl border border-zinc-800/60 p-6 overflow-y-auto custom-scrollbar relative min-h-[520px]">
      {/* Local Styles for Ocean Waves inside Panel */}
      <style dangerouslySetInnerHTML={{__html: `
        .sandbox-ocean-wave {
          position: absolute;
          bottom: 0;
          left: 0;
          width: 100%;
          height: 180px;
          background: linear-gradient(to top, rgba(5, 5, 8, 1) 0%, transparent 100%);
          z-index: 0;
          pointer-events: none;
          overflow: hidden;
          border-bottom-left-radius: 1rem;
          border-bottom-right-radius: 1rem;
        }
        .sandbox-wave {
          position: absolute;
          bottom: 0;
          left: 0;
          width: 200%;
          height: 100%;
          background-size: 50% 100%;
          animation: sandbox-wave-anim 20s linear infinite;
        }
        .s-wave1 {
          background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%2306b6d4" fill-opacity="0.2" d="M0,160L48,144C96,128,192,96,288,106.7C384,117,480,171,576,165.3C672,160,768,96,864,85.3C960,75,1056,117,1152,149.3C1248,181,1344,203,1392,213.3L1440,224L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
          animation-duration: 22s;
          opacity: 0.6;
        }
        .s-wave2 {
          background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%230ea5e9" fill-opacity="0.15" d="M0,192L48,197.3C96,203,192,213,288,213.3C384,213,480,203,576,186.7C672,171,768,149,864,154.7C960,160,1056,192,1152,202.7C1248,213,1344,203,1392,197.3L1440,192L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
          animation-direction: reverse;
          animation-duration: 28s;
          opacity: 0.5;
        }
        .s-wave3 {
          background: url('data:image/svg+xml;utf8,<svg viewBox="0 0 1440 320" xmlns="http://www.w3.org/2000/svg"><path fill="%231e3a8a" fill-opacity="0.25" d="M0,224L48,208C96,192,192,160,288,160C384,160,480,192,576,213.3C672,235,768,245,864,229.3C960,213,1056,171,1152,149.3C1248,128,1344,128,1392,128L1440,128L1440,320L1392,320C1344,320,1248,320,1152,320C1056,320,960,320,864,320C768,320,672,320,576,320C480,320,384,320,288,320C192,320,96,320,48,320L0,320Z"></path></svg>') repeat-x;
          animation-duration: 36s;
          opacity: 0.7;
        }
        @keyframes sandbox-wave-anim {
          0% { transform: translateX(0); }
          100% { transform: translateX(-50%); }
        }
      `}} />

      {/* Ambient Radial Sky Glow */}
      <div className="absolute inset-0 pointer-events-none z-0 rounded-2xl" style={{ background: 'radial-gradient(circle at top right, rgba(26, 31, 46, 0.45) 0%, rgba(5, 5, 8, 0.95) 75%)' }} />
      
      {/* Wave Overlays */}
      <div aria-hidden="true" className="sandbox-ocean-wave">
        <div className="sandbox-wave s-wave1" />
        <div className="sandbox-wave s-wave2" />
        <div className="sandbox-wave s-wave3" />
      </div>

      {/* Shimmering Star Overlays */}
      <div className="absolute inset-0 pointer-events-none opacity-40 z-0">
        <div className="absolute top-[15%] left-[25%] w-1 h-1 bg-white/80 rounded-full blur-[0.5px] animate-pulse" />
        <div className="absolute top-[30%] left-[75%] w-0.5 h-0.5 bg-white rounded-full" />
        <div className="absolute top-[75%] left-[10%] w-1.5 h-1.5 bg-white/60 rounded-full blur-[1px] animate-pulse" />
        <div className="absolute top-[20%] left-[60%] w-0.5 h-0.5 bg-white/95 rounded-full" />
      </div>

      <div className="absolute inset-0 pointer-events-none bg-[url('data:image/svg+xml,%3Csvg%20viewBox=%220%200%20200%20200%22%20xmlns=%22http://www.w3.org/2000/svg%22%3E%3Cfilter%20id=%22noiseFilter%22%3E%3CfeTurbulence%20type=%22fractalNoise%22%20baseFrequency=%220.65%22%20numOctaves=%223%22%20stitchTiles=%22stitch%22/%3E%3C/filter%3E%3Crect%20width=%22100%25%22%20height=%22100%25%22%20filter=%22url(%23noiseFilter)%22/%3E%3C/svg%3E')] opacity-[0.01] mix-blend-overlay" />

      <div className="relative z-10 w-full max-w-4xl flex flex-col gap-8">
        {/* Title block with wave-sparkle custom logo */}
        <div className="text-center space-y-4">
          <div className="relative inline-block select-none">
            <svg className="w-16 h-16 mx-auto drop-shadow-[0_0_20px_rgba(99,102,241,0.4)]" viewBox="0 0 100 100" fill="none">
              <circle cx="50" cy="50" r="46" stroke="url(#circle-grad)" strokeWidth="3" fill="#090B11" />
              <path d="M20,62 C32,48 42,66 52,52 C62,38 72,56 84,42 L84,82 L20,82 Z" fill="url(#wave-grad)" opacity="0.8" />
              <path d="M16,68 C28,56 38,74 50,62 C62,50 72,68 84,56 L84,84 L16,84 Z" fill="url(#wave-grad-2)" opacity="0.4" />
              {/* Stars */}
              <circle cx="35" cy="30" r="1.5" fill="#FFF" />
              <circle cx="65" cy="25" r="2" fill="#FFF" />
              <circle cx="50" cy="20" r="1" fill="#FFF" />
              <circle cx="75" cy="35" r="1.2" fill="#FFF" />
              <defs>
                <linearGradient id="circle-grad" x1="0" y1="0" x2="100" y2="100">
                  <stop offset="0%" stopColor="#06b6d4" />
                  <stop offset="50%" stopColor="#818cf8" />
                  <stop offset="100%" stopColor="#a855f7" />
                </linearGradient>
                <linearGradient id="wave-grad" x1="50" y1="30" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#06b6d4" stopOpacity="0.8" />
                  <stop offset="100%" stopColor="#1e1b4b" stopOpacity="0.1" />
                </linearGradient>
                <linearGradient id="wave-grad-2" x1="50" y1="40" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#818cf8" stopOpacity="0.6" />
                  <stop offset="100%" stopColor="#0f172a" stopOpacity="0" />
                </linearGradient>
              </defs>
            </svg>
          </div>

          <div className="space-y-2 select-none">
            <h2 className="text-3xl font-extrabold text-white tracking-tight">
              Namines Download Hub
            </h2>
            <p className="text-xs text-zinc-400 max-w-xl mx-auto leading-relaxed">
              Download your projects as a zip file that runs autonomously according to your database schema and spins up instantly with Docker.
            </p>
          </div>
        </div>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 items-stretch">
          {/* CARD 1 - Streamlit Admin Panel (FREE) */}
          <div className="bg-[#08090d]/35 backdrop-blur-2xl border border-cyan-500/20 rounded-2xl p-6 flex flex-col justify-between transition-all duration-300 hover:border-cyan-400/40 shadow-[inset_0_0_20px_rgba(255,255,255,0.01),0_12px_40px_rgba(0,0,0,0.5)] group relative overflow-hidden">
            <div className="absolute top-0 right-0 w-[150px] h-[150px] bg-cyan-500/5 rounded-full blur-[40px] pointer-events-none" />
            
            <div className="space-y-5">
              <div className="flex justify-between items-center select-none">
                <span className="text-[10px] font-black tracking-wider text-cyan-400 bg-cyan-950/50 border border-cyan-800/40 px-2.5 py-1 rounded-md uppercase">
                  Freemium
                </span>
              </div>

              <div className="space-y-2">
                <h3 className="text-lg font-bold text-white tracking-tight">Streamlit Admin Panel</h3>
                <p className="text-[13px] text-zinc-400 leading-relaxed">
                  Automatically reads your database and provides ready-to-run admin projects using Docker.
                </p>
              </div>

              <div className="border-t border-zinc-800/60 pt-4 space-y-2.5">
                {[
                  'Fully Automated CRUD Interface',
                  'Plotly Express Powered Dashboard',
                  'Ready-to-use Connection String (.env)',
                  'One-Click Setup with docker-compose.yml',
                ].map((feat, idx) => (
                  <div key={idx} className="flex items-center gap-2.5 text-xs text-zinc-300 select-none">
                    <span className="text-cyan-400 shrink-0 select-none">•</span>
                    <span>{feat}</span>
                  </div>
                ))}
              </div>
            </div>

            <div className="mt-6 pt-2 flex flex-col gap-3">
              <button
                onClick={handleExportPython}
                disabled={isExportingPython}
                className="w-full py-3.5 bg-gradient-to-r from-emerald-500 to-teal-600 hover:from-emerald-400 hover:to-teal-500 text-white font-bold text-xs rounded-xl transition-all shadow-[0_4px_20px_rgba(16,185,129,0.3)] flex items-center justify-center gap-2 hover:scale-[1.01] active:scale-[0.99] group cursor-pointer border border-emerald-400/20"
              >
                {isExportingPython && (
                  <Loader2 className="w-4 h-4 animate-spin text-white" />
                )}
                <span>Download Python Freemium (.zip)</span>
              </button>

              <button
                onClick={handleGenerate}
                className="w-full py-2.5 bg-zinc-900 hover:bg-zinc-800/80 text-zinc-300 hover:text-white border border-zinc-850/80 text-xs font-bold rounded-xl transition-all flex items-center justify-center gap-2 cursor-pointer"
              >
                <span>AI Enhanced Compiler</span>
              </button>
            </div>
          </div>

          {/* CARD 2 - Next.js Enterprise Dashboard (PREMIUM) */}
          <div className="bg-[#08090d]/35 backdrop-blur-2xl border border-purple-500/15 rounded-2xl p-6 flex flex-col justify-between transition-all duration-300 hover:border-purple-400/30 shadow-[inset_0_0_20px_rgba(255,255,255,0.01),0_12px_40px_rgba(0,0,0,0.5)] relative overflow-hidden group">
            <div className="absolute top-0 right-0 w-[150px] h-[150px] bg-purple-500/5 rounded-full blur-[40px] pointer-events-none" />
            
            <div className="space-y-5 opacity-60">
              <div className="flex justify-between items-center select-none">
                <span className="text-[10px] font-black tracking-wider text-purple-400 bg-purple-950/50 border border-purple-800/40 px-2.5 py-1 rounded-md uppercase">
                  Premium
                </span>
              </div>

              <div className="space-y-2">
                <h3 className="text-lg font-bold text-white tracking-tight">Next.js Enterprise Panel</h3>
                <p className="text-[13px] text-zinc-400 leading-relaxed">
                  Next.js Enterprise Panel with Prisma ORM support, offering enterprise-grade production-ready layouts.
                </p>
              </div>

              <div className="border-t border-zinc-800/40 pt-4 space-y-2.5">
                {[
                  'Next.js 15 (App Router) + Tailwind CSS 4',
                  'Prisma ORM & Auto-generated Types',
                  'Premium Dark & Light Mode UI',
                  'Chart.js / Tremor Dashboard Components',
                ].map((feat, idx) => (
                  <div key={idx} className="flex items-center gap-2.5 text-xs text-zinc-500 select-none">
                    <span className="text-purple-500/70 shrink-0 select-none">•</span>
                    <span>{feat}</span>
                  </div>
                ))}
              </div>
            </div>

            <div className="mt-6 pt-2">
              <button
                disabled
                className="w-full py-3 bg-zinc-900 border border-zinc-800 text-zinc-600 font-semibold text-xs rounded-xl flex items-center justify-center gap-1.5 cursor-not-allowed"
              >
                Coming Soon
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
