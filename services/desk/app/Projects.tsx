'use client';

import { useState } from 'react';
import { Database, Boxes, PlugZap, Table2, ExternalLink, Plus, Search } from 'lucide-react';
import { type DeskProject, setProjectConnection } from '../lib/auth';
import ImportForm from './ImportForm';
import { useT } from '../lib/i18n';

const NAMINES_FRONTEND = process.env.NAMINES_FRONTEND ?? 'http://localhost:3000';

/**
 * Desk'in karşılama panosu (D2 → v2.1).
 *
 * <b>Neden yeniden yazıldı:</b> önceki hâl ekranın ortasında yüzen tek bir
 * karttı — giriş yapan kullanıcıyı karşılayan ilk ekran olarak hem sığdı hem
 * de ürünün ne yapabildiğine dair hiçbir şey söylemiyordu. Artık gerçek bir
 * pano: üstte ölçülen sayılar (hepsi elimizdeki `projects` dizisinden
 * TÜRETİLİYOR, hiçbiri uydurma değil), altında filtrelenebilir proje ızgarası.
 */
export default function Projects({
  projects, error, onSelect, onImported, onUseGround, token,
}: {
  projects: DeskProject[] | null;
  error: string | null;
  onSelect: (projectId: string) => void;
  /** Bağlantısı olmayan bir proje için Ground görünümüne geçmek. */
  onUseGround: (projectId: string) => void;
  onImported: () => void;
  token: string;
}) {
  const t = useT();
  const [importing, setImporting] = useState<DeskProject | null>(null);
  const [search, setSearch] = useState('');
  const [engineFilter, setEngineFilter] = useState('');
  const [connFilter, setConnFilter] = useState<'all' | 'connected' | 'disconnected'>('all');

  const engines = projects
    ? Array.from(new Set(projects.map(p => p.connectionDbType ?? p.dbType).filter((v): v is string => !!v))).sort()
    : [];

  const visibleProjects = projects?.filter(p => {
    if (search.trim() && !p.name.toLowerCase().includes(search.trim().toLowerCase())) return false;
    if (engineFilter && (p.connectionDbType ?? p.dbType) !== engineFilter) return false;
    if (connFilter === 'connected' && !p.hasConnection) return false;
    if (connFilter === 'disconnected' && p.hasConnection) return false;
    return true;
  }) ?? null;

  // Sayılar TÜRETİLİYOR — ayrı bir uç ya da tahmin yok. `tableCount` null
  // olabilir (şema ayrıştırılamadıysa), o yüzden toplama katılmıyor.
  const total = projects?.length ?? 0;
  const connected = projects?.filter(p => p.hasConnection).length ?? 0;
  const tableSum = projects?.reduce((sum, p) => sum + (p.tableCount ?? 0), 0) ?? 0;
  const shared = projects?.filter(p => !p.isMine).length ?? 0;

  return (
    <div className="page">
      <div className="page-head">
        <div>
          <h1 className="page-title">{t('nav.views.overview')}</h1>
          <p className="page-desc">{t('projects.desc')}</p>
        </div>
        <div className="page-actions">
          <a className="btn" href={NAMINES_FRONTEND} target="_blank" rel="noreferrer">
            <Plus size={14} /> {t('projects.newProject')}
          </a>
        </div>
      </div>

      {error && <div className="notice notice-error">{error}</div>}

      {projects && projects.length > 0 && (
        <div className="stats">
          <div className="stat">
            <div className="stat-label"><Boxes size={12} /> {t('projects.statProjects')}</div>
            <div className="stat-value">{total}</div>
            <div className="stat-sub">{shared > 0 ? t('projects.shared', { count: shared }) : t('projects.allYours')}</div>
          </div>
          <div className="stat">
            <div className="stat-label"><PlugZap size={12} /> {t('projects.statConnected')}</div>
            <div className="stat-value">{connected}</div>
            <div className="stat-sub">{t('projects.notYetConnected', { count: total - connected })}</div>
          </div>
          <div className="stat">
            <div className="stat-label"><Table2 size={12} /> {t('projects.statTables')}</div>
            <div className="stat-value">{tableSum}</div>
            <div className="stat-sub">{t('projects.acrossSchemas')}</div>
          </div>
          <div className="stat">
            <div className="stat-label"><Database size={12} /> {t('projects.statEngines')}</div>
            <div className="stat-value">{engines.length}</div>
            <div className="stat-sub">{engines.length > 0 ? engines.join(', ') : '—'}</div>
          </div>
        </div>
      )}

      {projects && projects.length > 1 && (
        <div className="row" style={{ flexWrap: 'wrap', marginBottom: 14 }}>
          <div className="search-box" style={{ flex: 1, minWidth: 200, maxWidth: 320 }}>
            <Search size={13} />
            <input
              type="search" value={search} placeholder={t('projects.searchPlaceholder')}
              aria-label={t('projects.searchLabel')}
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          <select
            value={engineFilter}
            aria-label={t('projects.filterEngine')}
            onChange={e => setEngineFilter(e.target.value)}
            style={{ height: 32, padding: '0 8px', border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)', background: 'var(--surface-800)', color: 'var(--content-primary)' }}
          >
            <option value="">{t('projects.allEngines')}</option>
            {engines.map(e => <option key={e} value={e}>{e}</option>)}
          </select>
          <select
            value={connFilter}
            aria-label={t('projects.filterConnection')}
            onChange={e => setConnFilter(e.target.value as typeof connFilter)}
            style={{ height: 32, padding: '0 8px', border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)', background: 'var(--surface-800)', color: 'var(--content-primary)' }}
          >
            <option value="all">{t('projects.allStatuses')}</option>
            <option value="connected">{t('projects.connected')}</option>
            <option value="disconnected">{t('projects.notConnected')}</option>
          </select>
        </div>
      )}

      {!visibleProjects ? (
        <div className="empty empty-plain">{t('projects.loading')}</div>
      ) : projects && projects.length === 0 ? (
        <div className="empty">
          <div style={{ fontWeight: 600, color: 'var(--content-primary)', marginBottom: 6 }}>
            {t('projects.emptyTitle')}
          </div>
          <div style={{ marginBottom: 14 }}>{t('projects.emptyDesc')}</div>
          <a className="btn btn-primary" href={NAMINES_FRONTEND} target="_blank" rel="noreferrer">
            <ExternalLink size={14} /> {t('shell.openNamines')}
          </a>
        </div>
      ) : visibleProjects.length === 0 ? (
        <div className="empty">{t('projects.noMatch')}</div>
      ) : (
        <div className="project-grid">
          {visibleProjects.map(p => (
            <button
              key={p.id}
              className="project-card"
              onClick={() => (p.hasConnection ? onSelect(p.id) : setImporting(p))}
            >
              <div className="project-card-head">
                <span className="project-card-name">{p.name}</span>
                <span
                  className={`dot ${p.hasConnection ? 'dot-connected' : 'dot-disconnected'}`}
                  title={p.hasConnection ? t('projects.liveConnected') : t('projects.notConnected')}
                />
              </div>

              <div className="project-card-meta">
                <span>{p.connectionDbType ?? p.dbType ?? '—'}</span>
                {p.tableCount !== null && <span>· {t('common.tables', { count: p.tableCount })}</span>}
                {!p.isMine && p.ownerName && <span>· {p.ownerName}</span>}
              </div>

              <div className="spread" style={{ marginTop: 2 }}>
                <span className={`badge ${p.hasConnection ? 'badge-ok' : 'badge-off'}`}>
                  {p.hasConnection ? t('projects.connected') : t('projects.notConnected')}
                </span>
                <span className="project-card-import">
                  {p.hasConnection ? t('projects.open') : t('projects.addConnection')}
                </span>
              </div>
            </button>
          ))}
        </div>
      )}

      {importing && (
        <ImportForm
          projectName={importing.name}
          onCancel={() => setImporting(null)}
          onUseGround={() => { const id = importing.id; setImporting(null); onUseGround(id); }}
          onSubmit={async (connectionString, dbType) => {
            await setProjectConnection(token, importing.id, connectionString, dbType);
            setImporting(null);
            onImported();
          }}
        />
      )}
    </div>
  );
}
