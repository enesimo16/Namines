'use client';

import { useEffect, useState } from 'react';
import { type DeskSession } from '../lib/api';
import {
  deploymentsApi, type ChangeRequestSummary, type ChangeRequestDetail, type AuditEntry,
} from '../lib/deployments';
import PageHead from './PageHead';

/**
 * D5 — Deployments (namines_desk/05-DEPLOYMENTS.md). Vercel Deployments'ın
 * liste düzeni; "deployment" = şema sürümü (ChangeRequest + SchemaVersion).
 *
 * SALT-OKUNUR: Desk bu ekranda onay VERMEZ (§3) — onay geri alınamaz bir karar
 * ve ana uygulamada kendi ekranı var. İki yerde onaylatmak, hangi ekranın
 * yetkili olduğunu bulanıklaştırır. Aynı sebeple burada hiçbir yerde DDL
 * çalıştırılmaz (§4.3 — Vault'tan sonraya bırakıldı).
 */
export default function Deployments({ session }: { session: DeskSession }) {
  const [list, setList] = useState<ChangeRequestSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [detail, setDetail] = useState<ChangeRequestDetail | null>(null);
  const [audit, setAudit] = useState<AuditEntry[] | null>(null);
  const [detailError, setDetailError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    deploymentsApi.list(session, session.projectId)
      .then(l => { if (!cancelled) setList(l); })
      .catch(err => { if (!cancelled) setError(err instanceof Error ? err.message : 'Sürümler okunamadı.'); });
    return () => { cancelled = true; };
  }, [session]);

  useEffect(() => {
    if (!selectedId) { setDetail(null); setAudit(null); return; }
    let cancelled = false;
    setDetailError(null);
    Promise.all([deploymentsApi.detail(session, selectedId), deploymentsApi.audit(session, selectedId)])
      .then(([d, a]) => { if (!cancelled) { setDetail(d); setAudit(a); } })
      .catch(err => { if (!cancelled) setDetailError(err instanceof Error ? err.message : 'Detay okunamadı.'); });
    return () => { cancelled = true; };
  }, [session, selectedId]);

  // Erken donusler de basligi TASIR: baslıksız bir ekran, kullanicinin
  // "yanlis yere mi geldim?" diye dusunmesine yol aciyordu.
  const head = (
    <PageHead
      title="Sürümler"
      desc={<>
        Ana uygulamada açılan şema değişikliği istekleri ve etki raporları.
        <b> Desk buradan DDL çalıştırmaz</b> — şemayı canlı veritabanına uygulamak
        geri alınamaz bir işlem ve yedek altyapısı (Vault) gelene kadar bilinçli
        olarak kapsam dışı.
      </>}
    />
  );

  if (error) return <div className="page">{head}<div className="notice notice-error">{error}</div></div>;
  if (!list) return <div className="page">{head}<div className="empty empty-plain">Sürümler yükleniyor…</div></div>;

  if (list.length === 0) {
    return (
      <div className="page">
        {head}
        <div className="empty">
          <div style={{ fontWeight: 600, color: 'var(--content-primary)', marginBottom: 6 }}>
            Henüz şema sürümü yok
          </div>
          Ana uygulamada bir değişiklik yapıp &quot;Request Review&quot; dediğinizde ilk sürüm burada belirir.
        </div>
      </div>
    );
  }

  return (
    <div>
      <div style={{ padding: '22px 24px 0', maxWidth: 1320 }}>{head}</div>
      <div className="split-col" style={{ display: 'flex', minHeight: 0 }}>
      <div style={{ flex: 1, minWidth: 0, overflow: 'auto', padding: '0 24px 24px' }}>
        <div className="grid-wrap">
          <table>
            <thead>
              <tr>
                <th>Mesaj</th><th>Durum</th><th>Risk</th><th>Branch</th><th>Sürüm</th><th>Zaman</th>
              </tr>
            </thead>
            <tbody>
              {list.map(cr => (
                <tr key={cr.id} style={{ cursor: 'pointer' }}
                    className={cr.id === selectedId ? undefined : undefined}
                    onClick={() => setSelectedId(cr.id)}>
                  <td className={cr.id === selectedId ? 'pk-cell' : undefined}>{cr.title || '(mesaj yok)'}</td>
                  <td><StatusBadge status={cr.status} /></td>
                  <td><RiskBadge risk={cr.riskLevel} /></td>
                  <td>{cr.branchName}</td>
                  <td>{cr.tableCount} tablo</td>
                  <td>{new Date(cr.createdAt).toLocaleString('tr-TR')}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      {selectedId && (
        <aside style={{
          width: 340, flexShrink: 0, borderLeft: '1px solid var(--line)',
          background: 'var(--surface-800)', padding: 14, overflowY: 'auto',
        }}>
          {detailError && <div className="notice notice-error">{detailError}</div>}
          {!detail ? (
            <div className="empty">Yükleniyor…</div>
          ) : (
            <>
              <div style={{ fontWeight: 700, marginBottom: 4 }}>{detail.title || '(mesaj yok)'}</div>
              <div style={{ fontSize: 11.5, color: 'var(--content-muted)', marginBottom: 12 }}>
                <StatusBadge status={detail.status} /> · <RiskBadge risk={detail.riskLevel} /> · {detail.branchName} · v{detail.headVersion.version}
              </div>

              <div style={{ fontSize: 11.5, color: 'var(--content-muted)', marginBottom: 4 }}>
                Onay: {detail.approvedCount}/{detail.requiredApprovals}
                {detail.rejectedCount > 0 && ` · ${detail.rejectedCount} red`}
              </div>

              {detail.impact && <ImpactSummary impact={detail.impact} />}

              {detail.approvals.length > 0 && (
                <div style={{ marginTop: 12 }}>
                  <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', marginBottom: 6 }}>
                    Onaylar
                  </div>
                  {detail.approvals.map(a => (
                    <div key={a.id} style={{ fontSize: 12, marginBottom: 4 }}>
                      {a.username ?? a.userId} — {a.decision === 'Approved' ? '✓ onayladı' : '✗ reddetti'}
                      <span style={{ color: 'var(--content-subtle)', fontSize: 10.5 }}> · {new Date(a.createdAt).toLocaleString('tr-TR')}</span>
                    </div>
                  ))}
                </div>
              )}

              {audit && audit.length > 0 && (
                <div style={{ marginTop: 12 }}>
                  <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', marginBottom: 6 }}>
                    Zaman çizelgesi
                  </div>
                  {audit.map(e => (
                    <div key={e.id} style={{ fontSize: 12, marginBottom: 4 }}>
                      <span style={{ fontWeight: 600 }}>{e.action}</span>
                      {e.actorUsername && ` — ${e.actorUsername}`}
                      <div style={{ color: 'var(--content-subtle)', fontSize: 10.5 }}>{new Date(e.createdAt).toLocaleString('tr-TR')}</div>
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
        </aside>
      )}
      </div>
    </div>
  );
}

function StatusBadge({ status }: { status: string }) {
  const label = status === 'PendingReview' ? 'Pending' : status === 'Approved' ? 'Approved' : 'Rejected';
  const color = status === 'Approved' ? 'var(--success)' : status === 'Rejected' ? 'var(--danger)' : 'var(--content-muted)';
  return <span style={{ color, fontWeight: 600 }}>● {label}</span>;
}

function RiskBadge({ risk }: { risk: string }) {
  const color = risk === 'Breaking' || risk === 'Destructive' ? 'var(--danger)' : risk === 'Risky' ? 'var(--accent-text)' : 'var(--content-muted)';
  return <span style={{ color }}>{risk}</span>;
}

/** ImpactReport'un Desk için özeti — ana uygulamadaki `/review/{id}` ekranıyla aynı veri, farklı yerleşim. */
function ImpactSummary({ impact }: { impact: NonNullable<ChangeRequestDetail['impact']> }) {
  return (
    <div style={{ marginTop: 8 }}>
      <div style={{ fontSize: 11, fontWeight: 700, textTransform: 'uppercase', color: 'var(--content-subtle)', marginBottom: 6 }}>
        Değişiklik özeti
      </div>
      {impact.affectedTables.length === 0 && impact.breakingChanges.length === 0 && impact.dataLossRisks.length === 0 ? (
        <div style={{ fontSize: 12, color: 'var(--content-muted)' }}>Etkilenen tablo yok.</div>
      ) : (
        <>
          {impact.affectedTables.map((t, i) => (
            <div key={i} style={{ fontSize: 12 }}>
              <span className="col-badge">{t.kind}</span> {t.tableName}
              {t.changedColumns.length > 0 && ` (${t.changedColumns.join(', ')})`}
            </div>
          ))}
          {impact.breakingChanges.map((b, i) => (
            <div key={i} className="notice notice-error" style={{ marginTop: 6, padding: '6px 8px', fontSize: 11.5 }}>
              ⚠ {b.description}
            </div>
          ))}
          {impact.dataLossRisks.map((d, i) => (
            <div key={i} className="notice notice-error" style={{ marginTop: 6, padding: '6px 8px', fontSize: 11.5 }}>
              🗑 {d.tableName}{d.columnName && `.${d.columnName}`} — {d.reason}
            </div>
          ))}
        </>
      )}
      {!impact.rollback.isReversible && (
        <div style={{ fontSize: 11.5, color: 'var(--danger)', marginTop: 6 }}>
          Geri alınamaz{impact.rollback.reason && `: ${impact.rollback.reason}`}
        </div>
      )}
    </div>
  );
}
