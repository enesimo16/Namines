'use client';

import { useState, useEffect, useRef } from 'react';
import { DatabaseSchema } from '../../types/schema';
import { DbType } from '../../store/useSchemaStore';
import {
  CheckCircle2, XCircle, Terminal, LayoutDashboard, Sparkles,
  RotateCcw, Download, AlertTriangle, Info, Package as PackageIcon,
} from 'lucide-react';
import { scaffolderService, coderAIService } from '../../services/api';
import { useAIGateway } from '../../hooks/useAIGateway';
import { toAbsoluteApiUrl } from '../../lib/apiConfig';
import { Panel, PanelBar, ActionButton, OptionCard, PanelEmpty } from './PanelKit';

interface DownloadHubPanelProps {
  schema: DatabaseSchema;
  dbType: DbType;
}

/** Backend log satırındaki emoji öneki -> lucide ikonu + ton.
 *  Emoji ikon olarak kullanılmaz (FRONTEND.md §5 / skill kuralı); gelen metin
 *  temizlenip semantik bir ikona eşlenir. */
function classifyLog(raw: string): { text: string; Icon: typeof Info; tone: string } {
  const text = raw.replace(/^(🚀|📋|✅|❌|⏳|⚠️|📦)\s*/, '');
  if (raw.startsWith('❌') || raw.startsWith('HATA')) return { text, Icon: XCircle, tone: 'text-[var(--color-danger)]' };
  if (raw.startsWith('✅')) return { text, Icon: CheckCircle2, tone: 'text-[var(--color-success)]' };
  if (raw.startsWith('⚠️')) return { text, Icon: AlertTriangle, tone: 'text-content-secondary' };
  if (raw.startsWith('📦')) return { text, Icon: PackageIcon, tone: 'text-content-secondary' };
  return { text, Icon: Info, tone: 'text-content-secondary' };
}

export default function DownloadHubPanel({ schema, dbType }: DownloadHubPanelProps) {
  const [status, setStatus] = useState<'idle' | 'generating' | 'success' | 'error'>('idle');
  const [logs, setLogs] = useState<string[]>([]);
  const [downloadUrl, setDownloadUrl] = useState<string | null>(null);
  const [isExportingPython, setIsExportingPython] = useState(false);

  const { checkAccess } = useAIGateway();
  const logsEndRef = useRef<HTMLDivElement | null>(null);

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
      console.error('Python export error:', err);
    } finally {
      setIsExportingPython(false);
    }
  };

  const handleGenerate = async () => {
    if (!checkAccess('AI Coder Sandbox Generation')) return;
    setStatus('generating');
    setLogs(['🚀 Admin panel package generation request sent...', '⏳ AI engine is preparing...']);
    setDownloadUrl(null);

    try {
      const jobId = await coderAIService.generate(schema, dbType, true);
      setLogs(prev => [...prev, `📋 Project ID assigned: ${jobId.substring(0, 8)}...`]);

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

  // ── Üretim / sonuç: log akışı panelin TAMAMINI kaplar (sabit 420px kutu yerine) ──
  if (status !== 'idle') {
    return (
      <Panel scroll={false}>
        <div className="h-full flex flex-col">
          <PanelBar
            left={
              <>
                <Terminal className="w-3.5 h-3.5 text-content-muted shrink-0" />
                <span className="text-[11px] font-mono text-content-secondary">CoderAI build log</span>
                {status === 'success' && (
                  <span className="flex items-center gap-1 text-[10px] font-semibold text-[var(--color-success)]">
                    <CheckCircle2 className="w-3 h-3" /> Ready
                  </span>
                )}
                {status === 'error' && (
                  <span className="flex items-center gap-1 text-[10px] font-semibold text-[var(--color-danger)]">
                    <XCircle className="w-3 h-3" /> Failed
                  </span>
                )}
              </>
            }
          >
            {status === 'success' && downloadUrl && (
              <a
                href={downloadUrl}
                className="inline-flex items-center gap-1.5 h-8 px-3 rounded-md text-[11px] font-semibold bg-content-primary text-surface-900 hover:opacity-90 transition-opacity"
              >
                <Download className="w-3.5 h-3.5" /> .zip
              </a>
            )}
            {status === 'error' && (
              <ActionButton icon={RotateCcw} onClick={handleGenerate}>Retry</ActionButton>
            )}
            {status !== 'generating' && (
              <ActionButton onClick={resetPanel}>Back</ActionButton>
            )}
          </PanelBar>

          <div className="flex-1 min-h-0 overflow-y-auto p-2.5 bg-surface-900 font-mono text-[11px] space-y-1">
            {logs.map((log, i) => {
              const { text, Icon, tone } = classifyLog(log);
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
                <span>Compiling…</span>
              </div>
            )}
            <div ref={logsEndRef} />
          </div>
        </div>
      </Panel>
    );
  }

  // ── Idle: iki seçenek, yan yana, hero başlık YOK (kabuk zaten adlandırıyor) ──
  return (
    <Panel>
      <div className="p-2.5 grid grid-cols-1 lg:grid-cols-2 gap-2.5">
        <OptionCard
          icon={LayoutDashboard}
          title="Streamlit Admin Panel"
          badge="Freemium"
          description="Reads your database and ships a ready-to-run admin project with Docker."
          bullets={[
            'Fully automated CRUD interface',
            'Plotly Express dashboard',
            'Connection string prefilled (.env)',
            'One-command start (docker-compose.yml)',
          ]}
          action={
            <div className="flex flex-col gap-1.5">
              <ActionButton icon={Download} onClick={handleExportPython} busy={isExportingPython} tone="primary" full>
                Download .zip
              </ActionButton>
              <ActionButton icon={Sparkles} onClick={handleGenerate} full>
                Build with AI Compiler
              </ActionButton>
            </div>
          }
        />

        <OptionCard
          icon={LayoutDashboard}
          title="Next.js Enterprise Panel"
          badge="Premium"
          description="Production-ready Next.js panel with Prisma ORM and enterprise layouts."
          bullets={[
            'Next.js 15 App Router + Tailwind 4',
            'Prisma ORM with generated types',
            'Dark & light dashboard UI',
            'Chart.js / Tremor components',
          ]}
          disabled
          action={
            <ActionButton disabled full>
              Coming soon
            </ActionButton>
          }
        />
      </div>
    </Panel>
  );
}
