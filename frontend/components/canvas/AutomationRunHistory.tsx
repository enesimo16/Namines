'use client';

import { useCallback, useEffect, useState } from 'react';
import { CheckCircle2, CircleAlert, CircleSlash, Loader2, Play, RefreshCw } from 'lucide-react';
import { fetchRuleRuns, testAutomationRule, type AutomationRun } from '../../lib/automationApi';
import { useToastStore } from '../../store/useToastStore';

/**
 * Bir kuralın çalışma geçmişi ve "şimdi test et" düğmesi.
 *
 * <b>Çözdüğü sorun:</b> sunucu tarafı aksiyonlar senkronizasyon döngüsünde,
 * en geç ~30 saniye sonra çalışıyor ve sonucu yalnızca
 * <c>AutomationRunLog</c>'a yazılıyordu. Arayüzde bunu gösteren hiçbir şey
 * yoktu: SSRF kontrolüne takılıp "Skipped" olan bir webhook, kotası dolduğu
 * için atlanan bir DBA kontrolü ve hiç tetiklenmemiş bir kural kullanıcı
 * açısından birbirinin aynısıydı — üçü de sessizdi.
 */

const STATUS_STYLE: Record<string, { className: string; Icon: typeof CheckCircle2 }> = {
  Success: { className: 'text-success-text', Icon: CheckCircle2 },
  Failed: { className: 'text-danger-text', Icon: CircleAlert },
  Skipped: { className: 'text-warning-text', Icon: CircleSlash },
};

function formatTime(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

export default function AutomationRunHistory({ ruleId }: { ruleId: string }) {
  const [runs, setRuns] = useState<AutomationRun[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [testing, setTesting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const showToast = useToastStore(s => s.showToast);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setRuns(await fetchRuleRuns(ruleId));
    } catch {
      // Bir kural henüz sunucuya ulaşmamış olabilir (iyimser oluşturma) ya da
      // ağ kopuk olabilir. Boş liste göstermek "hiç çalışmadı" demek olurdu;
      // bu ikisi ayrı şeyler, o yüzden hata ayrıca söyleniyor.
      setError('Could not load the run history.');
    } finally {
      setLoading(false);
    }
  }, [ruleId]);

  useEffect(() => { void load(); }, [load]);

  const runTest = async () => {
    setTesting(true);
    try {
      const result = await testAutomationRule(ruleId);
      const failed = result.runs.filter(r => r.status === 'Failed').length;
      const skipped = result.runs.filter(r => r.status === 'Skipped').length;

      if (result.runs.length === 0 && result.clientOnlyActions > 0) {
        showToast('This flow only has client-side actions — nothing runs on the server.', 'info');
      } else if (failed > 0) {
        showToast(`Test finished: ${failed} step(s) failed.`, 'error');
      } else if (skipped > 0) {
        showToast(`Test finished: ${skipped} step(s) were skipped.`, 'warning');
      } else {
        showToast('Test finished: every server-side step succeeded.', 'success');
      }
      setRuns(await fetchRuleRuns(ruleId));
    } catch (err) {
      // 429 en olası hata ve kullanıcının anlaması gereken tek şey bu.
      const status = (err as { response?: { status?: number } })?.response?.status;
      showToast(
        status === 429
          ? 'Too many test runs for this flow — wait a minute and try again.'
          : 'The test run could not be started.',
        'error',
      );
    } finally {
      setTesting(false);
    }
  };

  return (
    <section className="flex flex-col gap-2">
      <div className="flex items-center justify-between">
        <h3 className="text-xs font-medium text-content-secondary">Run history</h3>
        <div className="flex items-center gap-0.5">
          <button
            type="button"
            aria-label="Refresh run history"
            className="rounded-[var(--radius-control)] p-1 text-content-muted transition-colors hover:bg-content-primary/12 hover:text-content-primary cursor-pointer disabled:opacity-40"
            disabled={loading}
            onClick={() => void load()}
          >
            <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button
            type="button"
            className="flex items-center gap-1 rounded-[var(--radius-control)] border border-warning/40 px-2 py-1 text-micro font-semibold text-warning-text transition-colors hover:bg-warning/20 cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
            disabled={testing}
            onClick={() => void runTest()}
          >
            {testing ? <Loader2 className="h-3 w-3 animate-spin" /> : <Play className="h-3 w-3" />}
            Test now
          </button>
        </div>
      </div>

      <p className="text-micro leading-snug text-content-muted">
        Server-side steps normally run within ~30s of the next sync. Testing runs them right now —
        it still checks the URL and still spends AI quota.
      </p>

      {error ? (
        <p className="text-micro text-danger-text">{error}</p>
      ) : runs === null ? (
        <p className="text-micro text-content-muted">Loading…</p>
      ) : runs.length === 0 ? (
        <p className="text-micro text-content-muted">This flow hasn’t run on the server yet.</p>
      ) : (
        <ul className="flex flex-col gap-1">
          {runs.map(run => {
            const style = STATUS_STYLE[run.status] ?? { className: 'text-content-muted', Icon: CircleSlash };
            const { Icon } = style;
            return (
              <li
                key={run.id}
                className="rounded-[var(--radius-control)] border border-content-primary/10 bg-surface-700 px-2 py-1.5"
              >
                <div className="flex items-center gap-1.5">
                  <Icon className={`h-3 w-3 shrink-0 ${style.className}`} />
                  <span className={`text-micro font-semibold ${style.className}`}>{run.status}</span>
                  <span className="text-micro text-content-secondary">{run.actionType}</span>
                  {run.isTest && (
                    <span className="rounded-full bg-surface-600 px-1.5 text-micro text-content-muted">test</span>
                  )}
                  <span className="ml-auto text-micro text-content-subtle">{formatTime(run.triggeredAt)}</span>
                </div>
                {run.errorMessage && (
                  <p className="mt-0.5 text-micro leading-snug text-danger-text">{run.errorMessage}</p>
                )}
                {run.resultSummary && (
                  <p className="mt-0.5 text-micro leading-snug text-content-muted">{run.resultSummary}</p>
                )}
              </li>
            );
          })}
        </ul>
      )}
    </section>
  );
}
