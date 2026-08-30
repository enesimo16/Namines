'use client';

import { useState, useEffect, useRef, useCallback } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useAuthStore } from '../../store/useAuthStore';
import {
  Download, X, RefreshCw, Database, Rocket, Play,
  Terminal, XCircle, CheckCircle2, AlertTriangle, Info,
} from 'lucide-react';
import DbPushModal from './DbPushModal';
import { API_BASE_URL, toAbsoluteApiUrl } from '../../lib/apiConfig';
import { Panel, PanelBar, ActionButton, OptionCard } from './PanelKit';

interface DockerSandboxPanelProps {
  schema: DatabaseSchema;
  dbType: string;
  sql?: string;
}

type PanelStatus = 'idle' | 'generating' | 'running' | 'error';

/** Emoji önekli backend log satırını lucide ikonuna eşler — emoji ikon olarak
 *  kullanılmaz (FRONTEND.md §5). */
function classifySandboxLog(raw: string): { text: string; Icon: typeof Info; tone: string } {
  const text = raw.replace(/^(🚀|📋|✅|❌|⏳|⚠️)\s*/, '');
  if (raw.startsWith('❌') || raw.startsWith('HATA') || raw.startsWith('ERROR'))
    return { text, Icon: XCircle, tone: 'text-[var(--color-danger)]' };
  if (raw.startsWith('✅')) return { text, Icon: CheckCircle2, tone: 'text-[var(--color-success)]' };
  if (raw.startsWith('⚠️')) return { text, Icon: AlertTriangle, tone: 'text-content-secondary' };
  return { text, Icon: Info, tone: 'text-content-secondary' };
}

export default function DockerSandboxPanel({ schema, dbType, sql = '' }: DockerSandboxPanelProps) {
  const { setActiveSandbox, getActiveSandbox } = useProjectHistoryStore();
  const { token, isAuthenticated } = useAuthStore();

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
    let cancelled = false;
    let controller: AbortController | undefined;
    let timeoutId: ReturnType<typeof setTimeout> | undefined;

    const saved = getActiveSandbox();
    if (saved && saved.type === 'DB') {
      jobIdRef.current = saved.jobId;
      setDownloadUrl(saved.url || null);

      if (saved.url) {
        setStatus('running');
        setLogs([`Previous sandbox restored. Backup (.bak) is ready.`]);
      } else {
        setStatus('generating');
        setLogs([`Previous sandbox operation restored. Waiting for log stream...`]);

        // Perform a quick HTTP fetch check to the stream URL on mount before initiating EventSource
        controller = new AbortController();
        timeoutId = setTimeout(() => controller?.abort(), 2000);

        fetch(`${API_BASE_URL}/docker/stream/${saved.jobId}`, { signal: controller.signal })
          .then(res => {
            clearTimeout(timeoutId);
            if (cancelled) return; // unmount sonrası setState/connectSse'yi engelle
            if (res.status === 404) {
              setActiveSandbox(null);
              setStatus('idle');
              setDownloadUrl(null);
              setLogs([]);
              jobIdRef.current = null;
            } else {
              connectSse(saved.jobId);
            }
          })
          .catch(() => {
            clearTimeout(timeoutId);
            if (cancelled) return;
            // Fallback to connecting anyway on timeout or network error
            connectSse(saved.jobId);
          });
      }
    }

    return () => { cancelled = true; controller?.abort(); clearTimeout(timeoutId); };
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

  // Docker daemon'a bağlanılamadığında (Docker Desktop kapalı) net, açıklayıcı mesaj.
  const dockerHint = (raw: string): string => {
    const t = (raw || '').toLowerCase();
    if (t.includes('pipe') || t.includes('docker_engine') || t.includes('timed out') ||
        t.includes('timeout') || t.includes('actively refused') || t.includes('cannot find the file')) {
      return '❌ Docker Desktop does not seem to be running. Please start Docker Desktop and try again.';
    }
    return `❌ ${raw}`;
  };

  const connectSse = (newJobId: string) => {
    eventSourceRef.current?.close();
    const sse = new EventSource(`${API_BASE_URL}/docker/stream/${newJobId}`);
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
        addLog(dockerHint(msg));
        sse.close();
        return;
      }

      if (msg.startsWith('DOWNLOAD_URL|')) {
        const path = msg.split('|')[1];
        const fullUrl = toAbsoluteApiUrl(path);
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
    if (!isAuthenticated) {
      setStatus('error');
      setLogs(['🔒 Docker sandbox için giriş yapmanız gerekiyor.']);
      return;
    }
    eventSourceRef.current?.close();
    setStatus('generating');
    setLogs(['🚀 Initializing Docker Sandbox...']);
    setDownloadUrl(null);

    try {
      const response = await fetch(`${API_BASE_URL}/docker/run`, {
        method: 'POST',
        credentials: 'include',
        headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
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
      addLog(dockerHint(err?.message || 'Unknown error'));
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
  // STATE: IDLE — iki seçenek, hero başlık yok (kabuk zaten "Docker Sandbox" diyor)
  // ─────────────────────────────────────────────
  if (status === 'idle') {
    return (
      <Panel>
        <div className="p-2.5 grid grid-cols-1 lg:grid-cols-2 gap-2.5">
          <OptionCard
            icon={Database}
            title="Docker Database Sandbox"
            badge="Isolated"
            description="Spins your schema up in a throwaway container and packages a backup."
            bullets={['MSSQL / PostgreSQL / MySQL', 'Automatic schema setup', '.bak / .sql backup produced']}
            action={<ActionButton icon={Play} onClick={handleGenerate} tone="primary" full>Start sandbox</ActionButton>}
          />
          <OptionCard
            icon={Rocket}
            title="Live Database Sync"
            badge="Your server"
            description="Applies the DDL directly to a database you own over a host/TCP connection."
            bullets={['AWS / Azure / self-hosted', 'Host + TCP connection', 'Creates tables in place']}
            action={<ActionButton icon={Rocket} onClick={() => setIsPushModalOpen(true)} full>Deploy to live DB</ActionButton>}
          />
        </div>

        <DbPushModal
          open={isPushModalOpen}
          onOpenChange={setIsPushModalOpen}
          sqlScript={sql}
        />
      </Panel>
    );
  }

  // ─────────────────────────────────────────────
  // STATE: GENERATING or ERROR — Log terminal
  // ─────────────────────────────────────────────
  if (status === 'generating' || status === 'error') {
    return (
      <Panel scroll={false}>
        <div className="h-full flex flex-col">
          <PanelBar
            left={
              <>
                <Terminal className="w-3.5 h-3.5 text-content-muted shrink-0" />
                <span className="text-[11px] font-mono text-content-secondary">Sandbox log</span>
                {status === 'error' && (
                  <span className="flex items-center gap-1 text-[10px] font-semibold text-[var(--color-danger)]">
                    <XCircle className="w-3 h-3" /> Failed
                  </span>
                )}
              </>
            }
          >
            {status === 'error' && (
              <>
                <ActionButton onClick={handleClose}>Reset</ActionButton>
                <ActionButton icon={RefreshCw} onClick={handleGenerate} tone="primary">Try again</ActionButton>
              </>
            )}
          </PanelBar>

          <div className="flex-1 min-h-0 overflow-y-auto p-2.5 bg-surface-900 font-mono text-[11px] space-y-1">
            {logs.map((log, i) => {
              const { text, Icon, tone } = classifySandboxLog(log);
              return (
                <div key={i} className="flex items-start gap-2 leading-relaxed">
                  <Icon className={`w-3 h-3 shrink-0 mt-[3px] ${tone}`} />
                  <span className={tone}>{text}</span>
                </div>
              );
            })}
            {status === 'generating' && (
              <div className="flex items-center gap-2 text-content-muted">
                <span className="w-1 h-3 bg-content-muted animate-pulse" />
                <span>Docker operations in progress…</span>
              </div>
            )}
            <div ref={logsEndRef} />
          </div>
        </div>
      </Panel>
    );
  }

  // ─────────────────────────────────────────────
  // STATE: RUNNING — Completed snapshot screen
  // ─────────────────────────────────────────────
  return (
    <Panel scroll={false}>
      <div className="h-full flex flex-col">
        <PanelBar
          left={
            <>
              <span className="inline-flex rounded-full h-1.5 w-1.5 bg-[var(--color-success)]" />
              <span className="text-[11px] font-semibold text-content-secondary">Sandbox ready</span>
            </>
          }
        >
          <ActionButton icon={X} onClick={handleClose} tone="danger">Clean up</ActionButton>
        </PanelBar>

        <div className="flex-1 min-h-0 flex items-center justify-center p-4">
          <div className="flex items-center gap-3 px-3.5 py-3 rounded-[var(--radius-control)] bg-surface-600 border border-surface-500">
            <span className="shrink-0 flex items-center justify-center w-8 h-8 rounded-[var(--radius-control)] bg-accent-subtle text-accent-text">
              <Database className="w-4 h-4" />
            </span>
            <div className="min-w-0">
              <p className="text-[12px] font-semibold text-content-primary">Backup packaged</p>
              <p className="text-[11px] text-content-muted">Schema applied in the sandbox and exported.</p>
            </div>
            {downloadUrl && (
              <a
                href={downloadUrl}
                className="ml-2 shrink-0 inline-flex items-center gap-1.5 h-9 px-3 rounded-[var(--radius-control)] text-[11px] font-semibold bg-content-primary text-surface-900 hover:opacity-90 transition-opacity"
              >
                <Download className="w-3.5 h-3.5" /> Download .bak
              </a>
            )}
          </div>
        </div>
      </div>
    </Panel>
  );
}
