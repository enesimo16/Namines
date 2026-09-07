'use client';

import { useState } from 'react';
import { Database, Boxes, PlugZap, Table2, ExternalLink, Plus, Search } from 'lucide-react';
import { type DeskProject, setProjectConnection } from '../lib/auth';
import ImportForm from './ImportForm';

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
  projects, error, onSelect, onImported, token,
}: {
  projects: DeskProject[] | null;
  error: string | null;
  onSelect: (projectId: string) => void;
  onImported: () => void;
  token: string;
}) {
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
          <h1 className="page-title">Projeler</h1>
          <p className="page-desc">
            Namines hesabınızdaki projeler. Canlı veritabanı bağlı olan bir projeyi açtığınızda
            şemayı, satırları, sürüm geçmişini ve denetim kayıtlarını buradan yönetirsiniz.
          </p>
        </div>
        <div className="page-actions">
          <a className="btn" href={NAMINES_FRONTEND} target="_blank" rel="noreferrer">
            <Plus size={14} /> Ana uygulamada yeni proje
          </a>
        </div>
      </div>

      {error && <div className="notice notice-error">{error}</div>}

      {projects && projects.length > 0 && (
        <div className="stats">
          <div className="stat">
            <div className="stat-label"><Boxes size={12} /> Proje</div>
            <div className="stat-value">{total}</div>
            <div className="stat-sub">{shared > 0 ? `${shared} tanesi paylaşılan` : 'Hepsi sizin'}</div>
          </div>
          <div className="stat">
            <div className="stat-label"><PlugZap size={12} /> Bağlı veritabanı</div>
            <div className="stat-value">{connected}</div>
            <div className="stat-sub">{total - connected} proje henüz bağlı değil</div>
          </div>
          <div className="stat">
            <div className="stat-label"><Table2 size={12} /> Tasarlanan tablo</div>
            <div className="stat-value">{tableSum}</div>
            <div className="stat-sub">Tüm projelerin şemalarından</div>
          </div>
          <div className="stat">
            <div className="stat-label"><Database size={12} /> Motorlar</div>
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
              type="search" value={search} placeholder="Proje ara…"
              aria-label="Proje ara"
              onChange={e => setSearch(e.target.value)}
            />
          </div>
          <select
            value={engineFilter}
            aria-label="Motora göre filtrele"
            onChange={e => setEngineFilter(e.target.value)}
            style={{ height: 32, padding: '0 8px', border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)', background: 'var(--surface-800)', color: 'var(--content-primary)' }}
          >
            <option value="">Tüm motorlar</option>
            {engines.map(e => <option key={e} value={e}>{e}</option>)}
          </select>
          <select
            value={connFilter}
            aria-label="Bağlantı durumuna göre filtrele"
            onChange={e => setConnFilter(e.target.value as typeof connFilter)}
            style={{ height: 32, padding: '0 8px', border: '1px solid var(--line-strong)', borderRadius: 'var(--radius-control)', background: 'var(--surface-800)', color: 'var(--content-primary)' }}
          >
            <option value="all">Tüm durumlar</option>
            <option value="connected">Bağlı</option>
            <option value="disconnected">Bağlı değil</option>
          </select>
        </div>
      )}

      {!visibleProjects ? (
        <div className="empty empty-plain">Projeler yükleniyor…</div>
      ) : projects && projects.length === 0 ? (
        <div className="empty">
          <div style={{ fontWeight: 600, color: 'var(--content-primary)', marginBottom: 6 }}>
            Henüz proje yok
          </div>
          <div style={{ marginBottom: 14 }}>
            Desk, Namines&apos;te tasarladığınız projelerin verisini yönetir — önce ana uygulamada
            bir şema oluşturun, sonra buradan canlı veritabanına bağlayın.
          </div>
          <a className="btn btn-primary" href={NAMINES_FRONTEND} target="_blank" rel="noreferrer">
            <ExternalLink size={14} /> Ana uygulamayı aç
          </a>
        </div>
      ) : visibleProjects.length === 0 ? (
        <div className="empty">Bu filtrelerle eşleşen proje yok.</div>
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
                  title={p.hasConnection ? 'Canlı veritabanı bağlı' : 'Bağlı değil'}
                />
              </div>

              <div className="project-card-meta">
                <span>{p.connectionDbType ?? p.dbType ?? '—'}</span>
                {p.tableCount !== null && <span>· {p.tableCount} tablo</span>}
                {!p.isMine && p.ownerName && <span>· {p.ownerName}</span>}
              </div>

              <div className="spread" style={{ marginTop: 2 }}>
                <span className={`badge ${p.hasConnection ? 'badge-ok' : 'badge-off'}`}>
                  {p.hasConnection ? 'Bağlı' : 'Bağlantı yok'}
                </span>
                <span className="project-card-import">
                  {p.hasConnection ? 'Aç →' : 'Bağlantı ekle →'}
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
