'use client';

import { useEffect, useState } from 'react';
import {
  Rocket, CheckCircle2, XCircle, Loader2, ExternalLink, Download, GitPullRequestArrow,
  Database, FileCode2, ArchiveRestore, Table2, AlertTriangle, RefreshCw,
} from 'lucide-react';
import { launchApi, LaunchError, type LaunchOutcome } from '../../services/launchApi';
import { groundApi, type GroundProvider } from '../../services/vaultGroundApi';
import { changeRequestService } from '../../services/api';
import { useRouter } from 'next/navigation';
import { Panel } from './PanelKit';
import { DatabaseSchema } from '../../types/schema';
import { openDeskHandoff } from '../../lib/deskHandoff';
import { Button, Callout, Badge, EmptyState } from '../workspace/WorkspaceKit';

type StepState = 'pending' | 'active' | 'done' | 'error' | 'skipped';

interface StepRow { key: string; label: string; state: StepState; detail?: string }

/** Başlamadan önce kullanıcıya gösterilen dört adımın özeti. */
const PLAN: { icon: typeof Database; title: string; text: string }[] = [
  { icon: Database, title: 'Open a database', text: 'A real PostgreSQL database is created for this project.' },
  { icon: FileCode2, title: 'Apply the schema', text: 'Your approved diagram is run against it as DDL.' },
  { icon: ArchiveRestore, title: 'Take a first backup', text: 'An encrypted Vault backup is stored right away.' },
  { icon: Table2, title: 'Open Desk', text: 'You land on a working data panel for the new tables.' },
];

/**
 * "/compile"'daki tek tık akışı: Ground'da veritabanı aç, DDL'i uygula, ilk
 * Vault yedeğini al, Desk'i doğrudan bu projenin Data ekranına açan bir
 * jetonla döndür — ya da hedefte zaten veri varsa (canlı introspection
 * karar veriyor, biz DEĞİL) mevcut Change Review akışına yönlendir.
 *
 * Kod projesi indirme AYRI bir eylem: "Ready" durumundan sonra, ham DB
 * parolası ASLA — bir Gateway API anahtarı gömülü gelir (bkz. backend
 * LaunchController.Download).
 *
 * <b>DDL hazır olmadan Launch açılmaz.</b> DDL sayfada asenkron üretiliyor; hazır
 * olmadan gönderilen boş `ddlScript` sunucuda "projectId, provider and ddlScript are
 * required" hatasına dönüşüyordu (kullanıcının gördüğü "Launch çalışmıyor").
 */
export default function LaunchPanel({
  projectId, projectName, ddlScript, schema, ddlStatus = 'ready', onRetryDdl,
}: {
  projectId: string | null;
  projectName: string;
  ddlScript: string;
  schema: DatabaseSchema;
  ddlStatus?: 'loading' | 'ready' | 'error';
  onRetryDdl?: () => void;
}) {
  const router = useRouter();
  const [providers, setProviders] = useState<GroundProvider[] | null>(null);
  const [selectedProvider, setSelectedProvider] = useState<string | null>(null);
  const [steps, setSteps] = useState<StepRow[] | null>(null);
  const [outcome, setOutcome] = useState<LaunchOutcome | null>(null);
  const [launching, setLaunching] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [downloadError, setDownloadError] = useState<string | null>(null);
  const [reviewError, setReviewError] = useState<string | null>(null);

  useEffect(() => {
    groundApi.providers().then(setProviders).catch(() => setProviders([]));
  }, []);

  const readyProviders = (providers ?? []).filter(p => p.problem === null);
  // Tek hazır sağlayıcı varsa seçimi kullanıcıya bırakmaya gerek yok. Efekt yerine
  // TÜRETİLMİŞ değer: seçim state'i yalnızca kullanıcının açık tercihini tutuyor.
  const chosenProvider = selectedProvider ?? (readyProviders.length === 1 ? readyProviders[0].name : null);

  const canLaunch = !!chosenProvider && ddlStatus === 'ready' && ddlScript.trim().length > 0 && !launching;

  async function handleLaunch() {
    if (!projectId || !chosenProvider || !ddlScript.trim()) return;

    setOutcome(null);
    setLaunching(true);
    setSteps([
      { key: 'provision', label: 'Opening database…', state: 'active' },
      { key: 'apply', label: 'Applying schema…', state: 'pending' },
      { key: 'backup', label: 'Taking first backup…', state: 'pending' },
      { key: 'ready', label: 'Ready', state: 'pending' },
    ]);

    try {
      const result = await launchApi.launch(projectId, chosenProvider, ddlScript);
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
          { key: 'apply', label: 'Schema not applied — sent to review instead', state: 'skipped' },
        ]);
      } else if (result.status === 'DdlFailed') {
        // Provizyon başarılıydı — hata DDL adımında, "Opening database"'de değil.
        setSteps([
          { key: 'provision', label: 'Database opened', state: 'done' },
          { key: 'apply', label: 'Schema application failed', state: 'error', detail: result.error },
        ]);
      } else {
        setSteps([
          { key: 'provision', label: 'Could not open database', state: 'error', detail: result.error },
        ]);
      }
    } catch (err) {
      const message = err instanceof LaunchError ? err.message : 'Launch failed unexpectedly.';
      setSteps([{ key: 'provision', label: 'Launch failed', state: 'error', detail: message }]);
      // launchApi bir LaunchOutcome ayrıştıramadığında (ör. istek gövdesi geçersiz —
      // {status} yok, yalnızca {error}) fırlatır; "Try again" düğmesi outcome.status'a
      // bağlı olduğu için burada da bir outcome set ETMEZSEK kullanıcı kilitli kalır.
      setOutcome({ status: 'ProvisionFailed', error: message });
    } finally {
      setLaunching(false);
    }
  }

  async function handleNeedsReview() {
    if (!projectId) return;
    setReviewError(null);
    try {
      const { id } = await changeRequestService.createQuick(
        projectId, schema, 'Launch: schema update');
      router.push(`/review/${id}`);
    } catch {
      setReviewError('Could not send this to review. Please try again.');
    }
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

  function reset() {
    setSteps(null);
    setOutcome(null);
    setReviewError(null);
    setDownloadError(null);
  }

  if (!projectId) {
    return (
      <Panel>
        <EmptyState icon={Rocket} title="Save your project first"
          description="Launch needs a saved project to open a database against. Give it a moment to sync, then try again." />
      </Panel>
    );
  }

  return (
    <Panel scroll className="h-full">
      <div className="mx-auto w-full max-w-2xl px-5 py-6 space-y-6">
        <header className="flex items-start gap-3.5">
          <span className="shrink-0 flex items-center justify-center w-10 h-10 rounded-[var(--radius-card)] bg-accent-subtle text-accent-text">
            <Rocket className="w-5 h-5" aria-hidden="true" />
          </span>
          <div className="min-w-0">
            <h2 className="text-lg font-semibold text-content-primary leading-tight">Launch — {projectName}</h2>
            <p className="mt-1 text-[13px] leading-relaxed text-content-muted">
              One step from an approved diagram to a running database with a working data panel.
              If the target already holds data, Launch never touches it — it sends the change to review instead.
            </p>
          </div>
        </header>

        {!steps && (
          <>
            <ol className="grid grid-cols-1 sm:grid-cols-2 gap-3" aria-label="What Launch does">
              {PLAN.map((item, index) => (
                <li key={item.title} className="flex items-start gap-3 rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-800 p-3.5">
                  <span className="shrink-0 flex items-center justify-center w-7 h-7 rounded-full bg-surface-600 text-content-secondary text-[12px] font-semibold font-mono">
                    {index + 1}
                  </span>
                  <div className="min-w-0">
                    <p className="text-[13px] font-semibold text-content-primary flex items-center gap-1.5">
                      <item.icon className="w-3.5 h-3.5 text-content-muted" aria-hidden="true" /> {item.title}
                    </p>
                    <p className="mt-0.5 text-[12px] leading-relaxed text-content-muted">{item.text}</p>
                  </div>
                </li>
              ))}
            </ol>

            {ddlStatus === 'error' && (
              <Callout
                tone="danger"
                title="Could not prepare the PostgreSQL script"
                action={onRetryDdl && <Button size="sm" icon={RefreshCw} onClick={onRetryDdl}>Retry</Button>}
              >
                Launch needs the schema compiled to PostgreSQL first. Check that the diagram has at least one
                table and try again.
              </Callout>
            )}

            <section aria-labelledby="launch-provider-heading" className="space-y-2.5">
              <h3 id="launch-provider-heading" className="text-sm font-semibold text-content-primary">Where should the database live?</h3>

              {!providers ? (
                <div className="flex items-center gap-2 text-[13px] text-content-muted">
                  <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading providers…
                </div>
              ) : readyProviders.length === 0 ? (
                <Callout tone="neutral" title="No provider is ready">
                  An admin needs to configure at least one Ground provider (LocalPostgres, Neon, or Supabase) on the server.
                </Callout>
              ) : (
                <div className="space-y-2">
                  {readyProviders.map(p => {
                    const selected = chosenProvider === p.name;
                    return (
                      <label key={p.name}
                        className={`flex items-start gap-3 rounded-[var(--radius-card)] border p-3.5 cursor-pointer transition-colors ${
                          selected ? 'border-accent-hover bg-accent-subtle' : 'border-content-primary/10 bg-surface-800 hover:border-content-primary/25'
                        }`}>
                        <input type="radio" name="provider" value={p.name} className="mt-1 accent-[var(--color-accent-hover)]"
                          checked={selected} onChange={() => setSelectedProvider(p.name)} />
                        <span className="min-w-0 flex-1">
                          <span className="flex items-center gap-2">
                            <span className="text-[13px] font-semibold text-content-primary">{p.name}</span>
                            {p.liveVerified && <Badge tone="success" icon={CheckCircle2}>Verified</Badge>}
                          </span>
                          {p.responsibility && (
                            <span className="mt-0.5 block text-[12px] leading-relaxed text-content-muted">{p.responsibility}</span>
                          )}
                        </span>
                      </label>
                    );
                  })}
                </div>
              )}
            </section>

            <div className="flex flex-wrap items-center gap-3">
              <Button variant="primary" icon={Rocket} disabled={!canLaunch} busy={launching} onClick={handleLaunch}>
                Launch
              </Button>
              {ddlStatus === 'loading' && (
                <span className="flex items-center gap-2 text-[12px] text-content-muted">
                  <Loader2 className="w-3.5 h-3.5 animate-spin" aria-hidden="true" /> Preparing the PostgreSQL script…
                </span>
              )}
              {ddlStatus === 'ready' && !chosenProvider && readyProviders.length > 1 && (
                <span className="text-[12px] text-content-muted">Choose a provider to continue.</span>
              )}
            </div>
          </>
        )}

        {steps && (
          <ol className="rounded-[var(--radius-card)] border border-content-primary/10 bg-surface-800 divide-y divide-content-primary/[0.07]" aria-live="polite">
            {steps.map(s => (
              <li key={s.key} className="flex items-start gap-3 px-4 py-3">
                <StepIcon state={s.state} />
                <div className="min-w-0">
                  <p className={`text-[13px] ${
                    s.state === 'error' ? 'text-danger-text font-medium'
                    : s.state === 'pending' ? 'text-content-muted'
                    : 'text-content-primary'
                  }`}>{s.label}</p>
                  {s.detail && <p className="mt-0.5 text-[12px] text-content-muted break-words">{s.detail}</p>}
                </div>
              </li>
            ))}
          </ol>
        )}

        {outcome?.status === 'Ready' && (
          <div className="space-y-3">
            <Callout tone="success" title="Your database is live">
              The schema is applied and Desk is ready for the new tables.
            </Callout>
            <div className="flex flex-wrap gap-2">
              <Button variant="primary" icon={ExternalLink}
                onClick={() => openDeskHandoff({ token: outcome.deskHandoffToken, projectId: outcome.projectId, view: 'data' })}>
                Open Desk
              </Button>
              <Button icon={Download} busy={downloading} onClick={handleDownload}>
                Download project
              </Button>
            </div>
          </div>
        )}
        {downloadError && <Callout tone="danger">{downloadError}</Callout>}

        {outcome?.status === 'NeedsReview' && (
          <div className="space-y-3">
            <Callout tone="neutral" icon={AlertTriangle} title="Your database already has tables — Launch left it untouched">
              Applying a schema over existing data is exactly what Change Review is for. Send this diagram
              to review to see what would change, and apply it from there once it is approved.
            </Callout>
            <div className="flex flex-wrap gap-2">
              <Button variant="primary" icon={GitPullRequestArrow} onClick={handleNeedsReview}>Review changes</Button>
              <Button variant="ghost" onClick={reset}>Back</Button>
            </div>
          </div>
        )}
        {reviewError && <Callout tone="danger">{reviewError}</Callout>}

        {(outcome?.status === 'ProvisionFailed' || outcome?.status === 'DdlFailed') && (
          <div className="flex flex-wrap gap-2">
            <Button variant="primary" icon={RefreshCw} onClick={reset}>Try again</Button>
          </div>
        )}
      </div>
    </Panel>
  );
}

function StepIcon({ state }: { state: StepState }) {
  const base = 'w-4 h-4 shrink-0 mt-0.5';
  if (state === 'done') return <CheckCircle2 className={`${base} text-success-text`} aria-label="Done" />;
  if (state === 'error') return <XCircle className={`${base} text-danger-text`} aria-label="Failed" />;
  if (state === 'skipped') return <AlertTriangle className={`${base} text-content-secondary`} aria-label="Skipped" />;
  if (state === 'active') return <Loader2 className={`${base} animate-spin text-content-muted`} aria-label="In progress" />;
  return <span className={`${base} rounded-full border border-content-primary/20`} aria-label="Waiting" />;
}
