'use client';

import { useState, useEffect, useRef, useCallback } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import {
  Download, X, Loader2, Terminal,
  RefreshCw, Database, Sparkles, ChevronRight, Rocket
} from 'lucide-react';
import SandboxActionCard from './SandboxActionCard';
import DbPushModal from './DbPushModal';

interface DockerSandboxPanelProps {
  schema: DatabaseSchema;
  dbType: string;
  sql?: string;
}

type PanelStatus = 'idle' | 'generating' | 'running' | 'error';

export default function DockerSandboxPanel({ schema, dbType, sql = '' }: DockerSandboxPanelProps) {
  const { setActiveSandbox, getActiveSandbox } = useProjectHistoryStore();

  const [status, setStatus] = useState<PanelStatus>('idle');
  const [logs, setLogs] = useState<string[]>([]);
  const [downloadUrl, setDownloadUrl] = useState<string | null>(null);
  const [isPushModalOpen, setIsPushModalOpen] = useState(false);

  const eventSourceRef = useRef<EventSource | null>(null);
  const jobIdRef = useRef<string | null>(null);
  const statusRef = useRef<PanelStatus>('idle');
  const logsEndRef = useRef<HTMLDivElement | null>(null);

  // Keep ref in sync with state
  useEffect(() => { statusRef.current = status; }, [status]);

  // Auto-scroll logs
  useEffect(() => {
    logsEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [logs]);

  // ── Restore previous sandbox session when page is loaded ──────────────
  useEffect(() => {
    const saved = getActiveSandbox();
    if (saved && saved.type === 'DB') {
      jobIdRef.current = saved.jobId;
      setDownloadUrl(saved.url || null);
      setStatus(saved.url ? 'running' : 'generating');
      setLogs(saved.url 
        ? [`Previous sandbox restored. Backup (.bak) is ready.`]
        : [`Previous sandbox operation restored. Waiting for log stream...`]
      );

      if (!saved.url) {
        // Reconnect to SSE stream
        connectSse(saved.jobId);
      }
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  // Cleanup on unmount
  useEffect(() => {
    return () => {
      eventSourceRef.current?.close();
    };
  }, []);

  const addLog = useCallback((msg: string) => {
    setLogs(prev => [...prev, msg]);
  }, []);

  const connectSse = (newJobId: string) => {
    eventSourceRef.current?.close();
    const sse = new EventSource(`http://localhost:5000/api/docker/stream/${newJobId}`);
    eventSourceRef.current = sse;

    sse.onmessage = (e) => {
      const msg: string = e.data;

      if (msg === 'DONE') {
        sse.close();
        setStatus('running');
        addLog('✅ Docker Sandbox created successfully and database backup (.bak) acquired!');
        return;
      }

      if (msg.startsWith('ERROR:')) {
        setStatus('error');
        addLog(`❌ ${msg}`);
        sse.close();
        return;
      }

      if (msg.startsWith('DOWNLOAD_URL|')) {
        const path = msg.split('|')[1];
        const fullUrl = `http://localhost:5000${path}`;
        setDownloadUrl(fullUrl);
        
        // ── Save sandbox status to IndexedDB ──────────────────────────
        setActiveSandbox({
          type: 'DB',
          jobId: newJobId,
          url: fullUrl,
          createdAt: new Date().toISOString(),
        });

        // Set running state (success UI screen) and close SSE stream
        setStatus('running');
        addLog('✅ Docker Sandbox created successfully and database backup (.bak) acquired!');
        sse.close();
        return;
      }

      addLog(msg);
    };

    sse.onerror = () => {
      sse.close();
      if (statusRef.current !== 'running' && statusRef.current !== 'idle') {
        setStatus('error');
        addLog('❌ Connection to server lost. Is the backend running?');
      }
    };
  };

  const handleGenerate = async () => {
    eventSourceRef.current?.close();
    setStatus('generating');
    setLogs(['🚀 Initializing Docker Sandbox...']);
    setDownloadUrl(null);

    try {
      const response = await fetch('http://localhost:5000/api/docker/run', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ schema, dbType }),
      });

      if (!response.ok) {
        const errText = await response.text();
        throw new Error(`API Error (${response.status}): ${errText}`);
      }

      const data = await response.json();
      const newJobId = data.jobId;
      jobIdRef.current = newJobId;
      addLog(`📋 Container Job ID received: ${newJobId.substring(0, 8)}...`);

      // Save initial state to IndexedDB
      setActiveSandbox({
        type: 'DB',
        jobId: newJobId,
        createdAt: new Date().toISOString(),
      });

      connectSse(newJobId);

    } catch (err: any) {
      setStatus('error');
      addLog(`❌ ERROR: ${err.message}`);
    }
  };

  const handleClose = () => {
    eventSourceRef.current?.close();
    setActiveSandbox(null);
    setStatus('idle');
    setDownloadUrl(null);
    setLogs([]);
    jobIdRef.current = null;
  };

  // ─────────────────────────────────────────────
  // STATE: IDLE — Veritabanı Entegrasyon Merkezi
  // ─────────────────────────────────────────────
  if (status === 'idle') {
    return (
      <div className="w-full h-full flex flex-col justify-center items-center bg-[#0b0c10] rounded-2xl border border-zinc-800/60 p-6 overflow-y-auto custom-scrollbar relative min-h-[520px]">
        {/* Local Styles for Ocean Waves inside Sandbox Panel */}
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

        {/* Ambient Radial Sky Glow */}
        <div className="absolute inset-0 pointer-events-none bg-radial-gradient-sandbox z-0 rounded-2xl" style={{ background: 'radial-gradient(circle at top right, rgba(26, 31, 46, 0.45) 0%, rgba(11, 12, 16, 0.95) 75%)' }} />
        
        {/* Wave and Star Overlays */}
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

        {/* Header Section */}
        <div className="max-w-4xl w-full text-center mb-10 relative z-10 select-none">
          <div className="relative inline-block select-none mb-4">
            <svg className="w-16 h-16 mx-auto drop-shadow-[0_0_20px_rgba(99,102,241,0.4)]" viewBox="0 0 100 100" fill="none">
              <circle cx="50" cy="50" r="46" stroke="url(#circle-grad-sandbox)" strokeWidth="3" fill="#090B11" />
              <path d="M20,62 C32,48 42,66 52,52 C62,38 72,56 84,42 L84,82 L20,82 Z" fill="url(#wave-grad-sandbox)" opacity="0.8" />
              <path d="M16,68 C28,56 38,74 50,62 C62,50 72,68 84,56 L84,84 L16,84 Z" fill="url(#wave-grad-2-sandbox)" opacity="0.4" />
              {/* Stars */}
              <circle cx="35" cy="30" r="1.5" fill="#FFF" />
              <circle cx="65" cy="25" r="2" fill="#FFF" />
              <circle cx="50" cy="20" r="1" fill="#FFF" />
              <circle cx="75" cy="35" r="1.2" fill="#FFF" />
              <defs>
                <linearGradient id="circle-grad-sandbox" x1="0" y1="0" x2="100" y2="100">
                  <stop offset="0%" stopColor="#06b6d4" />
                  <stop offset="50%" stopColor="#818cf8" />
                  <stop offset="100%" stopColor="#a855f7" />
                </linearGradient>
                <linearGradient id="wave-grad-sandbox" x1="50" y1="30" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#06b6d4" stopOpacity="0.8" />
                  <stop offset="100%" stopColor="#1e1b4b" stopOpacity="0.1" />
                </linearGradient>
                <linearGradient id="wave-grad-2-sandbox" x1="50" y1="40" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#818cf8" stopOpacity="0.6" />
                  <stop offset="100%" stopColor="#0f172a" stopOpacity="0" />
                </linearGradient>
              </defs>
            </svg>
          </div>
          <h3 className="text-xl font-black text-white tracking-tight drop-shadow-md">Namines Integration Center</h3>
          <p className="text-xs text-zinc-400 mt-2 max-w-lg mx-auto leading-relaxed">
            Instantly run, test, or sync your database schema inside an isolated Docker container environment.
          </p>
        </div>

        {/* Action Cards */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 w-full max-w-4xl relative z-10">
          
          {/* Card 1: Docker Sandbox */}
          <div className="flex flex-col justify-between p-6 bg-[#1a1f2e]/35 backdrop-blur-2xl border border-white/5 rounded-3xl shadow-[inset_0_0_20px_rgba(255,255,255,0.02),0_12px_40px_rgba(0,0,0,0.5)] hover:border-indigo-500/30 transition-all duration-300 group">
            <div>
              <div className="flex items-center gap-3 mb-4 select-none">
                <div>
                  <h4 className="text-xs font-black text-white uppercase tracking-wider">Docker Database Sandbox</h4>
                  <span className="text-[10px] text-cyan-400 block mt-0.5 font-semibold">Docker · MSSQL / Postgres / MySQL</span>
                </div>
              </div>
              <p className="text-xs text-zinc-400 leading-relaxed mb-6">
                Spin up your schema automatically in an isolated Docker sandbox. Get the complete `.bak` or `.sql` backup in one click.
              </p>
              <div className="flex flex-wrap gap-1.5 mb-6 select-none">
                <span className="text-[9px] font-bold tracking-wider uppercase px-2.5 py-1 rounded-lg bg-black/40 text-zinc-400 border border-white/5 shadow-sm">.BAK Backup</span>
                <span className="text-[9px] font-bold tracking-wider uppercase px-2.5 py-1 rounded-lg bg-black/40 text-zinc-400 border border-white/5 shadow-sm">Isolated Sandbox</span>
                <span className="text-[9px] font-bold tracking-wider uppercase px-2.5 py-1 rounded-lg bg-black/40 text-zinc-400 border border-white/5 shadow-sm font-semibold">Auto Setup</span>
              </div>
            </div>
            <button
              onClick={handleGenerate}
              className="w-full py-3 bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white font-bold text-xs rounded-xl shadow-[0_0_15px_rgba(79,70,229,0.3)] hover:shadow-[0_0_20px_rgba(99,102,241,0.5)] transition-all duration-300 cursor-pointer active:scale-98 border border-indigo-400/20 animate-none"
            >
              Start Sandbox
            </button>
          </div>
 
          {/* Card 2: Live Database Push */}
          <div className="flex flex-col justify-between p-6 bg-[#1a1f2e]/35 backdrop-blur-2xl border border-white/5 rounded-3xl shadow-[inset_0_0_20px_rgba(255,255,255,0.02),0_12px_40px_rgba(0,0,0,0.5)] hover:border-indigo-500/30 transition-all duration-300 group">
            <div>
              <div className="flex items-center gap-3 mb-4 select-none">
                <div>
                  <h4 className="text-xs font-black text-white uppercase tracking-wider">Live Database Sync</h4>
                  <span className="text-[10px] text-purple-400 block mt-0.5 font-semibold">External Database · Host / TCP Connection</span>
                </div>
              </div>
              <p className="text-xs text-zinc-400 leading-relaxed mb-6">
                Deploy the SQL schema script directly to your own AWS, Azure, or local MSSQL/PostgreSQL/MySQL server and set up your tables.
              </p>
              <div className="flex flex-wrap gap-1.5 mb-6 select-none">
                <span className="text-[9px] font-bold tracking-wider uppercase px-2.5 py-1 rounded-lg bg-black/40 text-zinc-400 border border-white/5 shadow-sm">AWS / Azure Support</span>
                <span className="text-[9px] font-bold tracking-wider uppercase px-2.5 py-1 rounded-lg bg-black/40 text-zinc-400 border border-white/5 shadow-sm">TCP / Host</span>
                <span className="text-[9px] font-bold tracking-wider uppercase px-2.5 py-1 rounded-lg bg-black/40 text-zinc-400 border border-white/5 shadow-sm font-semibold">Table Setup</span>
              </div>
            </div>
            <button
              onClick={() => setIsPushModalOpen(true)}
              className="w-full py-3 bg-white/5 hover:bg-white/10 text-zinc-300 hover:text-white border border-white/10 font-bold text-xs rounded-xl transition-all duration-300 cursor-pointer active:scale-98 shadow-md animate-none"
            >
              Deploy to Live Database
            </button>
          </div>
 
        </div>
 
        <DbPushModal 
          open={isPushModalOpen} 
          onOpenChange={setIsPushModalOpen} 
          sqlScript={sql} 
        />
      </div>
    );
  }

  // ─────────────────────────────────────────────
  // STATE: GENERATING or ERROR — Log terminal
  // ─────────────────────────────────────────────
  if (status === 'generating' || status === 'error') {
    return (
      <div className="w-full h-full flex flex-col items-center justify-center bg-[#0b0c10] rounded-2xl border border-zinc-800/60 overflow-hidden relative min-h-[520px]">
        {/* Ambient deep blue/indigo background glow behind terminal */}
        <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[650px] h-[450px] bg-indigo-500/5 rounded-full blur-[130px] pointer-events-none animate-none" />

        {/* High-Fidelity Black Terminal (Perfect Mockup Replica) */}
        <div className="relative z-10 w-full max-w-[750px] mx-6 bg-black/85 backdrop-blur-xl border border-white/5 rounded-3xl shadow-[0_20px_50px_rgba(0,0,0,0.6)] flex flex-col overflow-hidden animate-none" style={{ height: '440px' }}>

          {/* macOS-style Header */}
          <div className="shrink-0 flex items-center px-5 py-4 bg-[#0e0e12]/90 border-b border-white/5 select-none">
            {/* macOS Red, Yellow, Green window buttons */}
            <div className="flex gap-2">
              <span className="w-3 h-3 rounded-full bg-[#ff5f56]/85" />
              <span className="w-3 h-3 rounded-full bg-[#ffbd2e]/85" />
              <span className="w-3 h-3 rounded-full bg-[#27c93f]/85" />
            </div>
            
            {/* Terminal Title */}
            <div className="flex items-center gap-2 ml-4">
              <span className="text-[10px] text-zinc-500 font-mono tracking-widest font-black uppercase">
                &gt;_ SYSTEM LOG – SANDBOX
              </span>
            </div>

            {/* Right-aligned dynamic loader or error indicator */}
            {status === 'generating' && (
              <Loader2 className="w-4 h-4 text-sky-400 animate-spin ml-auto drop-shadow-[0_0_8px_rgba(56,189,248,0.5)]" />
            )}
            {status === 'error' && (
              <span className="ml-auto text-[10px] text-red-400 font-mono font-bold tracking-wider uppercase border border-red-500/20 px-2 py-0.5 rounded bg-red-950/20 animate-none">
                ERROR
              </span>
            )}
          </div>

          {/* Log Area */}
          <div className="flex-1 min-h-0 overflow-y-auto p-6 font-mono text-[13px] space-y-2 bg-[#050508]/90">
            {logs.map((log, i) => {
              // Extract timestamp or generate a generic one for clean visual layout
              const cleanLog = log.replace(/^(🚀|📋|✅|❌|⏳|⚠️)\s*/, '');
              const icon = log.match(/^(🚀|📋|✅|❌|⏳|⚠️)/)?.[0] || '';
              
              let textColorClass = 'text-zinc-300';
              if (log.startsWith('🚀')) textColorClass = 'text-amber-400 font-bold';
              else if (log.startsWith('📋') || log.startsWith('DOWNLOAD_URL|')) textColorClass = 'text-cyan-400';
              else if (log.startsWith('✅')) textColorClass = 'text-emerald-400 font-semibold';
              else if (log.startsWith('❌') || log.startsWith('HATA') || log.startsWith('ERROR')) textColorClass = 'text-red-400';
              
              return (
                <div key={i} className="flex items-start gap-4 leading-relaxed">
                  {/* Clean timestamp in muted gray */}
                  <span className="text-zinc-600 shrink-0 select-none tabular-nums">
                    {new Date().toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                  </span>
                  
                  {/* Log line with icon and correct text coloring */}
                  <div className={`flex items-center gap-1.5 ${textColorClass}`}>
                    {icon && <span className="select-none">{icon}</span>}
                    <span>{cleanLog}</span>
                  </div>
                </div>
              );
            })}

            {/* Glowing active blinking cursor loader */}
            {status === 'generating' && (
              <div className="flex items-start gap-4 leading-relaxed">
                <span className="text-zinc-600 shrink-0 select-none tabular-nums">
                  {new Date().toLocaleTimeString('en-US', { hour12: false, hour: '2-digit', minute: '2-digit', second: '2-digit' })}
                </span>
                <div className="flex items-center gap-2 text-sky-400 font-medium">
                  {/* Glowing dynamic blue vertical bar */}
                  <span className="w-1.5 h-4 bg-sky-400 animate-pulse shadow-[0_0_8px_rgba(56,189,248,0.7)]" />
                  <span className="animate-pulse">Docker operations in progress...</span>
                </div>
              </div>
            )}
            <div ref={logsEndRef} />
          </div>

          {/* Error Actions panel */}
          {status === 'error' && (
            <div className="shrink-0 flex gap-3 p-4 bg-[#0e0e12]/95 border-t border-white/5 backdrop-blur-md">
              <button
                onClick={handleClose}
                className="flex-1 py-3 bg-white/5 hover:bg-white/10 text-zinc-300 hover:text-white text-xs font-bold rounded-xl transition-all duration-300 border border-white/10 cursor-pointer shadow-md animate-none"
              >
                Reset
              </button>
              <button
                onClick={handleGenerate}
                className="flex-1 py-3 bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white text-xs font-bold rounded-xl transition-all duration-300 flex items-center justify-center gap-2 shadow-[0_0_15px_rgba(79,70,229,0.3)] hover:shadow-[0_0_20px_rgba(99,102,241,0.5)] cursor-pointer border border-indigo-400/20 animate-none"
              >
                <RefreshCw className="w-4 h-4 animate-spin-slow animate-none" />
                Try Again
              </button>
            </div>
          )}
        </div>
      </div>
    );
  }

  // ─────────────────────────────────────────────
  // STATE: RUNNING — Completed snapshot screen (Celestial Planet Theme)
  // ─────────────────────────────────────────────
  return (
    <div className="w-full h-full flex flex-col bg-[#05050a] rounded-xl border border-zinc-800/60 overflow-hidden relative min-h-[520px]">
      
      {/* Space Background & Stars */}
      <div className="absolute inset-0 pointer-events-none bg-[url('/noise.png')] opacity-[0.02] mix-blend-overlay" />
      <div className="absolute inset-0 pointer-events-none opacity-45">
        {/* Coded stars & cosmic particles */}
        <div className="absolute top-[10%] left-[20%] w-0.5 h-0.5 bg-white rounded-full animate-none" />
        <div className="absolute top-[40%] left-[80%] w-1 h-1 bg-white/70 rounded-full blur-[0.5px] animate-pulse animate-none" />
        <div className="absolute top-[75%] left-[15%] w-0.5 h-0.5 bg-white/90 rounded-full animate-none" />
        <div className="absolute top-[25%] left-[65%] w-1 h-1 bg-white/80 rounded-full blur-[0.5px] animate-none" />
        <div className="absolute top-[60%] left-[45%] w-0.5 h-0.5 bg-white/60 rounded-full animate-none" />
        <div className="absolute top-[85%] left-[70%] w-1.5 h-1.5 bg-white/40 rounded-full blur-[1px] animate-pulse animate-none" />
        
        {/* Shooting Stars */}
        <div className="absolute top-10 left-[40%] w-[100px] h-[1px] bg-gradient-to-r from-transparent via-indigo-400/50 to-transparent rotate-[-25deg] pointer-events-none opacity-40 animate-none" />
        <div className="absolute top-36 left-[70%] w-[120px] h-[1px] bg-gradient-to-r from-transparent via-cyan-400/40 to-transparent rotate-[-25deg] pointer-events-none opacity-30 animate-none" />
      </div>

      {/* Top status bar */}
      <div className="shrink-0 flex items-center justify-between px-5 py-3.5 bg-[#09090f]/85 backdrop-blur-md border-b border-zinc-800/80 relative z-10 select-none">
        <div className="flex items-center gap-3">
          <span className="relative flex h-2.5 w-2.5">
            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75 animate-none" />
            <span className="relative inline-flex rounded-full h-2.5 w-2.5 bg-emerald-500" />
          </span>
          <span className="text-[13px] text-zinc-300 font-bold tracking-wide">
            Docker Sandbox Ready
          </span>
        </div>
      </div>

      {/* Hero launch area */}
      <div className="flex-1 flex flex-col items-center justify-center gap-6 relative p-6">

        {/* Ambient glow layers */}
        <div className="absolute inset-0 pointer-events-none">
          <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[550px] h-[350px] bg-indigo-500/5 rounded-full blur-[130px] animate-none" />
          <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[300px] h-[200px] bg-cyan-400/5 rounded-full blur-[80px] animate-none" />
        </div>

        {/* Breathtaking Coded Cosmic Planet */}
        <div className="relative w-48 h-48 flex items-center justify-center select-none scale-110 mb-2">
          {/* Radial outer glow */}
          <div className="absolute w-52 h-52 bg-indigo-500/10 rounded-full blur-[50px] animate-pulse duration-[4000ms] pointer-events-none animate-none" />
          <div className="absolute w-44 h-44 bg-cyan-400/5 rounded-full blur-[40px] pointer-events-none animate-none" />
          
          {/* Outer ring (Back part) */}
          <div className="absolute w-[240px] h-[32px] border-[1.5px] border-indigo-400/10 rounded-full rotate-[-18deg] pointer-events-none" style={{ transform: 'rotateX(75deg)' }} />
          <div className="absolute w-[220px] h-[28px] border-[2px] border-cyan-400/15 rounded-full rotate-[-18deg] pointer-events-none" style={{ transform: 'rotateX(72deg)' }} />

          {/* Planet Sphere */}
          <div className="absolute w-32 h-32 rounded-full bg-gradient-to-tr from-[#0b0c16] via-[#1b1c3a] to-[#7c3aed] shadow-[inset_-15px_-15px_40px_rgba(0,0,0,0.9),0_0_35px_rgba(124,58,237,0.35)] overflow-hidden border border-indigo-500/20">
            {/* Specular highlight */}
            <div className="absolute inset-0 bg-[radial-gradient(circle_at_25%_25%,rgba(255,255,255,0.08),transparent_50%)]" />
            <div className="absolute -top-12 -left-12 w-28 h-28 bg-cyan-400/10 rounded-full blur-2xl animate-pulse duration-[3000ms] animate-none" />
          </div>

          {/* Inner glowing ring (Front part overlay) */}
          <div className="absolute w-[230px] h-[30px] border-t-[3px] border-l-[3px] border-cyan-400/30 rounded-full rotate-[-18deg] pointer-events-none drop-shadow-[0_0_8px_rgba(34,211,238,0.4)]" style={{ transform: 'rotateX(75deg)' }} />
          <div className="absolute w-[210px] h-[26px] border-t-[2px] border-l-[2px] border-indigo-300/40 rounded-full rotate-[-18deg] pointer-events-none drop-shadow-[0_0_10px_rgba(129,140,248,0.5)]" style={{ transform: 'rotateX(72deg)' }} />
        </div>

        {/* Heading */}
        <div className="relative z-10 text-center space-y-3 px-8 select-none max-w-lg">
          <h3 className="text-2xl font-extrabold text-white tracking-tight drop-shadow-[0_4px_12px_rgba(0,0,0,0.5)] animate-none">
            Database Backup Ready!
          </h3>
          <p className="text-xs text-zinc-400 leading-relaxed font-semibold">
            Schema created on Docker sandbox and complete `.bak` / `.sql` backup packaged successfully.
          </p>
        </div>

        {/* PRIMARY — Download button with glowing green border */}
        {downloadUrl && (
          <a
            href={downloadUrl}
            className="
              relative z-10 group
              flex items-center justify-center gap-3
              px-10 py-3.5
              bg-gradient-to-r from-emerald-600 to-teal-600
              hover:from-emerald-500 hover:to-teal-500
              text-white font-bold text-sm rounded-full
              transition-all duration-300
              border border-emerald-400/35
              shadow-[0_0_30px_rgba(16,185,129,0.3),0_4px_20px_rgba(0,0,0,0.4)]
              hover:shadow-[0_0_45px_rgba(16,185,129,0.45),0_4px_25px_rgba(0,0,0,0.5)]
              hover:scale-[1.02] active:scale-[0.98]
              animate-none
            "
          >
            <Download className="w-4 h-4 group-hover:translate-y-0.5 transition-transform" />
            <span>Download Database Backup (.bak)</span>
            <ChevronRight className="w-4 h-4 opacity-80 group-hover:translate-x-0.5 transition-transform" />
          </a>
        )}

        <div className="relative z-10 flex items-center gap-3">
          <button
            onClick={handleClose}
            className="flex items-center gap-2 px-5 py-2.5 text-sm font-medium
                       bg-red-500/10 hover:bg-red-500/20 text-red-400
                       border border-red-500/20 rounded-xl transition-colors cursor-pointer animate-none"
          >
            <X className="w-4 h-4" />
            Clean & Close Sandbox
          </button>
        </div>

      </div>
    </div>
  );
}
