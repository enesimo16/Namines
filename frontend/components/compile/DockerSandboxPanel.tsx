'use client';

import { useState, useEffect, useRef, useCallback } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useAuthStore } from '../../store/useAuthStore';
import { csrfHeaders } from '../../lib/csrf';
import {
  Download, Trash2, RefreshCw, Database, Rocket, Play,
  Terminal, XCircle, CheckCircle2, AlertTriangle, Info, type LucideIcon,
} from 'lucide-react';
import DbPushModal from './DbPushModal';
import { API_BASE_URL, toAbsoluteApiUrl } from '../../lib/apiConfig';
import { Panel } from './PanelKit';
import { Button, Badge, Callout } from '../workspace/WorkspaceKit';
import { errorMessage } from '../../lib/errors';

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
    return { text, Icon: XCircle, tone: 'text-danger-text' };
  if (raw.startsWith('✅')) return { text, Icon: CheckCircle2, tone: 'text-success-text' };
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
    // withCredentials: auth cookie'sini gonder.
    //
    // EventSource `Authorization` basligi gonderemez -- bu yuzden uc uzun sure
    // kimlik dogrulamasiz kaldi ve GUID jobId bir "capability URL" gorevi
    // gordu. Ama EventSource COOKIE gonderebiliyor ve bu uygulamada kimlik
    // zaten httpOnly cookie'de. Sunucu tarafi artik [Authorize] tasiyor.
    const sse = new EventSource(`${API_BASE_URL}/docker/stream/${newJobId}`, {
      withCredentials: true,
    });
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

  /**
   * Onceki oturumdan kalan kum havuzunu geri yukler.
   *
   * **Efekt neden `connectSse`nin ALTINDA:** `connectSse` asagida `const` ile
   * tanimli; efekt yukarida dururken bildirimden onceki bir degiskene
   * erisiyordu (TDZ). Kapanis mount sonrasi calistigi icin patlamiyordu, ama
   * ic akis senkron hale geldigi an `ReferenceError` verirdi.
   */
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

        // credentials: auth cookie'si olmadan uc artik 401 donuyor (bkz. asagidaki
        // EventSource notu).
        fetch(`${API_BASE_URL}/docker/stream/${saved.jobId}`, {
          signal: controller.signal,
          credentials: 'include',
        })
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

  const handleGenerate = async () => {
    if (!isAuthenticated) {
      setStatus('error');
      setLogs(['🔒 You need to sign in to use the Docker sandbox.']);
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
        headers: { 'Content-Type': 'application/json', ...csrfHeaders, ...(token ? { Authorization: `Bearer ${token}` } : {}) },
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

    } catch (err) {
      setStatus('error');
      addLog(dockerHint(errorMessage(err, 'Unknown error')));
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
  // STATE: IDLE — iki seçenek (kabuk zaten "Docker Sandbox" diyor, hero başlık yok)
  // ─────────────────────────────────────────────
  if (status === 'idle') {
    return (
      <Panel>
        <div className="mx-auto w-full max-w-3xl px-5 py-6 grid grid-cols-1 md:grid-cols-2 gap-4">
          <ChoiceCard
            icon={Database}
            title="Docker Database Sandbox"
            badge="Isolated"
            description="Spins your schema up in a throwaway container and packages a backup you can restore anywhere."
            bullets={['MSSQL / PostgreSQL / MySQL', 'Automatic schema setup', '.bak / .sql backup produced']}
            action={<Button variant="primary" icon={Play} full onClick={handleGenerate}>Start sandbox</Button>}
          />
          <ChoiceCard
            icon={Rocket}
            title="Live Database Sync"
            badge="Your server"
            description="Applies the DDL directly to a database you own over a host/TCP connection."
            bullets={['AWS / Azure / self-hosted', 'Host + TCP connection', 'Creates tables in place']}
            action={<Button icon={Rocket} full onClick={() => setIsPushModalOpen(true)}>Deploy to live DB</Button>}
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
  // STATE: GENERATING or ERROR — günlük terminali
  // ─────────────────────────────────────────────
  if (status === 'generating' || status === 'error') {
    return (
      <Panel scroll={false}>
        <div className="h-full flex flex-col">
          <div className="shrink-0 flex flex-wrap items-center justify-between gap-3 px-5 py-3 border-b border-content-primary/10 bg-surface-800">
            <div className="flex items-center gap-2.5">
              <Terminal className="w-4 h-4 text-content-muted" aria-hidden="true" />
              <span className="text-sm font-semibold text-content-primary">Sandbox log</span>
              {status === 'error'
                ? <Badge tone="danger" icon={XCircle}>Failed</Badge>
                : <Badge tone="accent" icon={RefreshCw}>Running</Badge>}
            </div>
            {status === 'error' && (
              <div className="flex items-center gap-2">
                <Button variant="ghost" size="sm" onClick={handleClose}>Reset</Button>
                <Button variant="primary" size="sm" icon={RefreshCw} onClick={handleGenerate}>Try again</Button>
              </div>
            )}
          </div>

          <div className="flex-1 min-h-0 overflow-y-auto px-5 py-4 bg-surface-900 font-mono text-[12px] space-y-1.5" aria-live="polite">
            {logs.map((log, i) => {
              const { text, Icon, tone } = classifySandboxLog(log);
              return (
                <div key={i} className="flex items-start gap-2.5 leading-relaxed">
                  <Icon className={`w-3.5 h-3.5 shrink-0 mt-[3px] ${tone}`} aria-hidden="true" />
                  <span className={tone}>{text}</span>
                </div>
              );
            })}
            {status === 'generating' && (
              <div className="flex items-center gap-2 text-content-muted">
                <span className="w-1.5 h-3.5 bg-content-muted animate-pulse" />
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
  // STATE: RUNNING — tamamlanmış anlık görüntü
  // ─────────────────────────────────────────────
  return (
    <Panel>
      <div className="mx-auto w-full max-w-xl px-5 py-8 space-y-5">
        <Callout tone="success" title="Sandbox ready">
          Your schema was applied inside a throwaway container and exported as a backup.
        </Callout>

        <div className="rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-800 p-5 space-y-4">
          <div className="flex items-start gap-3.5">
            <span className="shrink-0 flex items-center justify-center w-10 h-10 rounded-[var(--radius-card)] bg-accent-subtle text-accent-text">
              <Database className="w-5 h-5" aria-hidden="true" />
            </span>
            <div className="min-w-0">
              <p className="text-sm font-semibold text-content-primary">Backup packaged</p>
              <p className="mt-0.5 text-[13px] leading-relaxed text-content-muted">
                Restore it into any {dbType} server to get exactly these tables. The sandbox container itself is disposable.
              </p>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-2">
            {downloadUrl && (
              <a
                href={downloadUrl}
                className="inline-flex items-center gap-2 h-9 px-3.5 rounded-[var(--radius-control)] text-[13px] font-semibold bg-content-primary text-surface-900 hover:bg-content-primary-hover transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--color-focus-ring)]"
              >
                <Download className="w-4 h-4" aria-hidden="true" /> Download backup
              </a>
            )}
            <Button variant="danger" icon={Trash2} onClick={handleClose} title="Remove this sandbox and its log">Clean up</Button>
          </div>
        </div>
      </div>
    </Panel>
  );
}

/** Sandbox başlangıç ekranındaki seçenek kartı (geniş, okunur ölçek). */
function ChoiceCard({
  icon: Icon, title, badge, description, bullets, action,
}: {
  icon: LucideIcon;
  title: string;
  badge: string;
  description: string;
  bullets: string[];
  action: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-4 rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-800 p-5">
      <div className="flex items-start gap-3.5">
        <span className="shrink-0 flex items-center justify-center w-10 h-10 rounded-[var(--radius-card)] bg-accent-subtle text-accent-text">
          <Icon className="w-5 h-5" aria-hidden="true" />
        </span>
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <h3 className="text-sm font-semibold text-content-primary">{title}</h3>
            <Badge>{badge}</Badge>
          </div>
          <p className="mt-1 text-[13px] leading-relaxed text-content-muted">{description}</p>
        </div>
      </div>
      <ul className="space-y-1.5 text-[12px] text-content-secondary">
        {bullets.map(b => (
          <li key={b} className="flex items-start gap-2">
            <CheckCircle2 className="w-3.5 h-3.5 mt-0.5 shrink-0 text-content-subtle" aria-hidden="true" />
            <span>{b}</span>
          </li>
        ))}
      </ul>
      <div className="mt-auto">{action}</div>
    </div>
  );
}
