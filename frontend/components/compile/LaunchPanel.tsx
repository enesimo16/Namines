'use client';

import { useEffect, useState } from 'react';
import { Rocket, CheckCircle2, XCircle, Loader2, ExternalLink, Download, GitPullRequestArrow } from 'lucide-react';
import { launchApi, LaunchError, type LaunchOutcome } from '../../services/launchApi';
import { groundApi, type GroundProvider } from '../../services/vaultGroundApi';
import { changeRequestService } from '../../services/api';
import { useRouter } from 'next/navigation';
import { Panel, PanelBar, ActionButton, PanelEmpty } from './PanelKit';
import { DatabaseSchema } from '../../types/schema';

type StepState = 'pending' | 'active' | 'done' | 'error';

interface StepRow { key: string; label: string; state: StepState; detail?: string }

const DESK_URL = process.env.NEXT_PUBLIC_DESK_URL ?? 'http://localhost:3200';

/**
 * "/compile"'daki tek tık akışı: Ground'da veritabanı aç, DDL'i uygula, ilk
 * Vault yedeğini al, Desk'i doğrudan bu projenin Data ekranına açan bir
 * jetonla döndür — ya da hedefte zaten veri varsa (canlı introspection
 * karar veriyor, biz DEĞİL) mevcut Change Review akışına yönlendir.
 *
 * Kod projesi indirme AYRI bir eylem: "Ready" durumundan sonra, ham DB
 * parolası ASLA — bir Gateway API anahtarı gömülü gelir (bkz. backend
 * LaunchController.Download).
 */
export default function LaunchPanel({
  projectId, projectName, ddlScript, schema,
}: {
  projectId: string | null;
  projectName: string;
  ddlScript: string;
  schema: DatabaseSchema;
}) {
  const router = useRouter();
  const [providers, setProviders] = useState<GroundProvider[] | null>(null);
  const [selectedProvider, setSelectedProvider] = useState<string | null>(null);
  const [steps, setSteps] = useState<StepRow[] | null>(null);
  const [outcome, setOutcome] = useState<LaunchOutcome | null>(null);
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);

  useEffect(() => {
    groundApi.providers().then(setProviders).catch(() => setProviders([]));
  }, []);

  const readyProviders = (providers ?? []).filter(p => p.problem === null);

  async function handleLaunch() {
    if (!projectId || !selectedProvider) return;

    setOutcome(null);
    setSteps([
      { key: 'provision', label: 'Opening database…', state: 'active' },
      { key: 'apply', label: 'Applying schema…', state: 'pending' },
      { key: 'backup', label: 'Taking first backup…', state: 'pending' },
      { key: 'ready', label: 'Ready', state: 'pending' },
    ]);

    try {
      const result = await launchApi.launch(projectId, selectedProvider, ddlScript);
      setOutcome(result);

      if (result.status === 'Ready') {
        setSteps([
          { key: 'provision', label: 'Database opened', state: 'done' },
          { key: 'apply', label: 'Schema applied', state: 'done' },
          {
            key: 'backup', label: result.backupWarning ? result.backupWarning : 'First backup taken',
            state: result.backupWarning ? 'error' : 'done',
          },
          { key: 'ready', label: 'Ready', state: 'done' },
        ]);
      } else if (result.status === 'NeedsReview') {
        setSteps([
          { key: 'provision', label: 'Database already has tables', state: 'done' },
          { key: 'apply', label: 'Schema NOT applied — sent to review instead', state: 'error' },
        ]);
      } else {
        setSteps(prev => (prev ?? []).map(s =>
          s.state === 'active' ? { ...s, state: 'error', detail: result.error } : s));
      }
    } catch (err) {
      const message = err instanceof LaunchError ? err.message : 'Launch failed unexpectedly.';
      setSteps(prev => (prev ?? []).map(s =>
        s.state === 'active' ? { ...s, state: 'error', detail: message } : s));
    }
  }

  async function handleNeedsReview() {
    if (!projectId) return;
    try {
      const { id } = await changeRequestService.createQuick(
        projectId, schema, 'Launch: schema update');
      router.push(`/review/${id}`);
    } catch {
      // handleLaunch'un kendi hata satırı zaten görünür; burada sessizce geç.
    }
  }

  function openDesk(deskHandoffToken: string, pid: string) {
    const form = document.createElement('form');
    form.method = 'POST';
    form.action = `${DESK_URL}/handoff`;
    form.target = '_blank';
    form.style.display = 'none';

    for (const [name, value] of [['token', deskHandoffToken], ['projectId', pid], ['view', 'data']]) {
      const input = document.createElement('input');
      input.type = 'hidden';
      input.name = name;
      input.value = value;
      form.appendChild(input);
    }

    document.body.appendChild(form);
    form.submit();
    document.body.removeChild(form);
  }

  async function handleDownload() {
    if (!projectId) return;
    setDownloading(true);
    setDownloadError(null);
    try {
      const { blob, fileName } = await launchApi.download(projectId);
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = fileName;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setDownloadError(err instanceof LaunchError ? err.message : 'Download failed.');
    } finally {
      setDownloading(false);
    }
  }

  if (!projectId) {
    return (
      <Panel>
        <PanelEmpty icon={Rocket} title="Save your project first"
          hint="Launch needs a saved project to open a database against. Give it a moment to sync, then try again." />
      </Panel>
    );
  }

  return (
    <Panel scroll className="h-full">
      <PanelBar left={<span className="text-[12px] font-semibold text-content-primary">Launch — {projectName}</span>} />
      <div className="p-3 space-y-3">
        {!steps && (
          <>
            <p className="text-[11px] text-content-muted leading-relaxed">
              Opens a real PostgreSQL database, applies this schema to it, takes a first
              backup, and hands you a working Desk panel — all in one step. If the target
              already has data, this sends the change to review instead of touching it.
            </p>
            {!providers ? (
              <div className="flex items-center gap-2 text-[11px] text-content-muted">
                <Loader2 className="w-3.5 h-3.5 animate-spin" /> Loading providers…
              </div>
            ) : readyProviders.length === 0 ? (
              <PanelEmpty icon={Rocket} title="No provider is ready"
                hint="An admin needs to configure at least one Ground provider (LocalPostgres, Neon, or Supabase) on the server." />
            ) : (
              <div className="space-y-2">
                {readyProviders.map(p => (
                  <label key={p.name}
                    className={`flex items-center gap-2 p-2.5 rounded-[var(--radius-control)] border cursor-pointer text-[11px] ${
                      selectedProvider === p.name ? 'border-accent-hover bg-accent-subtle' : 'border-surface-500 bg-surface-600'
                    }`}>
                    <input type="radio" name="provider" value={p.name}
                      checked={selectedProvider === p.name}
                      onChange={() => setSelectedProvider(p.name)} />
                    <span className="font-semibold text-content-primary">{p.name}</span>
                  </label>
                ))}
                <ActionButton icon={Rocket} tone="primary" full
                  disabled={!selectedProvider} onClick={handleLaunch}>
                  Launch
                </ActionButton>
              </div>
            )}
          </>
        )}

        {steps && (
          <div className="space-y-1.5">
            {steps.map(s => (
              <div key={s.key} className="flex items-start gap-2 text-[11px]">
                {s.state === 'done' && <CheckCircle2 className="w-3.5 h-3.5 text-accent-text shrink-0 mt-0.5" />}
                {s.state === 'error' && <XCircle className="w-3.5 h-3.5 text-[var(--color-danger)] shrink-0 mt-0.5" />}
                {s.state === 'active' && <Loader2 className="w-3.5 h-3.5 animate-spin text-content-muted shrink-0 mt-0.5" />}
                {s.state === 'pending' && <span className="w-3.5 h-3.5 rounded-full border border-surface-500 shrink-0 mt-0.5" />}
                <div>
                  <p className={s.state === 'error' ? 'text-[var(--color-danger)]' : 'text-content-secondary'}>{s.label}</p>
                  {s.detail && <p className="text-content-muted text-[10.5px] mt-0.5">{s.detail}</p>}
                </div>
              </div>
            ))}
          </div>
        )}

        {outcome?.status === 'Ready' && (
          <div className="flex flex-wrap gap-2 pt-1">
            <ActionButton icon={ExternalLink} tone="primary"
              onClick={() => openDesk(outcome.deskHandoffToken, outcome.projectId)}>
              Open Desk
            </ActionButton>
            <ActionButton icon={Download} busy={downloading} onClick={handleDownload}>
              Download project
            </ActionButton>
          </div>
        )}
        {downloadError && <p className="text-[11px] text-[var(--color-danger)]">{downloadError}</p>}

        {outcome?.status === 'NeedsReview' && (
          <ActionButton icon={GitPullRequestArrow} tone="primary" onClick={handleNeedsReview}>
            Review changes
          </ActionButton>
        )}

        {(outcome?.status === 'ProvisionFailed' || outcome?.status === 'DdlFailed') && (
          <ActionButton tone="primary" onClick={() => { setSteps(null); setOutcome(null); }}>
            Try again
          </ActionButton>
        )}
      </div>
    </Panel>
  );
}
