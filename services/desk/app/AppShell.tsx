'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Database, Search, ChevronsUpDown, LayoutGrid, Workflow, Table2, Terminal,
  GitBranch, ScrollText, BarChart3, Users, KeyRound, PanelLeftClose, PanelLeftOpen,
  LogOut, ExternalLink, LifeBuoy, ArrowLeftRight, Lock, Menu, Check, Plug, Archive,
} from 'lucide-react';
import { type DeskView, VIEW_LABELS, decodeSessionUser, initialsOf } from '../lib/nav';
import type { DeskTable } from '../lib/schema';
import type { DeskProject } from '../lib/auth';
import ThemeToggle from './ThemeToggle';

const NAMINES_FRONTEND = process.env.NAMINES_FRONTEND ?? 'http://localhost:3000';
const COLLAPSE_KEY = 'namines-desk-sidebar-collapsed';

interface NavEntry {
  view: DeskView;
  icon: typeof LayoutGrid;
  /** Proje seçilmeden açılamaz. */
  needsProject?: boolean;
  /** Yalnızca proje sahibine gösterilir (sunucu ayrıca doğruluyor). */
  ownerOnly?: boolean;
}

const NAV_GROUPS: { label: string; items: NavEntry[] }[] = [
  {
    label: 'Genel',
    items: [{ view: 'overview', icon: LayoutGrid }],
  },
  {
    label: 'Veritabanı',
    items: [
      { view: 'canvas', icon: Workflow, needsProject: true },
      { view: 'data', icon: Table2, needsProject: true },
      { view: 'sql', icon: Terminal, needsProject: true, ownerOnly: true },
    ],
  },
  {
    label: 'İşlemler',
    items: [
      { view: 'deployments', icon: GitBranch, needsProject: true },
      { view: 'logs', icon: ScrollText, needsProject: true },
      { view: 'analytics', icon: BarChart3, needsProject: true },
      { view: 'vault', icon: Archive, needsProject: true },
    ],
  },
  {
    label: 'Ayarlar',
    items: [
      { view: 'members', icon: Users, needsProject: true },
      { view: 'apikeys', icon: KeyRound, needsProject: true, ownerOnly: true },
    ],
  },
];

/**
 * Namines Desk'in uygulama kabuğu — kalıcı sol gezinme + üst şerit.
 *
 * <b>Neden yeniden yazıldı:</b> önceki hâlde Desk, ekranın ortasında tek bir
 * kartla ("proje seç") karşılıyordu ve tüm görünümler o karttan sonra gelen
 * yatay düğme şeridine sıkışmıştı. Bir OPERASYON paneli için bu sığ bir
 * yapıydı: kullanıcı nerede olduğunu, başka nelerin var olduğunu ve nasıl
 * geri döneceğini göremiyordu. Yeni kabuk sektörün fiili standardını
 * izliyor (Cloudflare/Vercel/Supabase panoları): solda kalıcı ve gruplu
 * gezinme + arama, üstte bağlam (ekmek kırıntısı) ve hesap, ortada içerik.
 *
 * Kabuk HİÇBİR veri çekmiyor — yalnızca sunum ve yönlendirme. Veri sahibi
 * hâlâ `page.tsx` (oturum/projeler) ve `Desk.tsx` (şema/satırlar).
 */
export default function AppShell({
  token, view, onNavigate, projectName, onChangeProject, onSignOut,
  tables, activeTable, onSelectTable, isOwner, hasProject, contentFlush,
  projects, projectId, onSelectProject, children,
}: {
  token: string;
  view: DeskView;
  onNavigate: (view: DeskView) => void;
  projectName: string | null;
  onChangeProject: () => void;
  onSignOut: () => void;
  tables: DeskTable[] | null;
  activeTable: string | null;
  onSelectTable: (name: string) => void;
  isOwner: boolean;
  hasProject: boolean;
  /** Sol paneldeki acilir menu icin — proje degistirmek panoya ugramadan olsun. */
  projects: DeskProject[] | null;
  projectId: string | null;
  onSelectProject: (id: string) => void;
  /** Canvas gibi kendi yüksekliğini yöneten görünümlerde kaydırmayı kapat. */
  contentFlush?: boolean;
  children: React.ReactNode;
}) {
  const [collapsed, setCollapsed] = useState(false);
  const [query, setQuery] = useState('');
  const [menuOpen, setMenuOpen] = useState(false);
  const [projectMenuOpen, setProjectMenuOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);
  const profileRef = useRef<HTMLDivElement>(null);
  const projectRef = useRef<HTMLDivElement>(null);

  const user = useMemo(() => decodeSessionUser(token), [token]);

  // Daraltma tercihi kalıcı: her sekme açılışında yeniden daraltmak zorunda
  // kalmak, tercihi anlamsız kılardı.
  useEffect(() => {
    try {
      setCollapsed(localStorage.getItem(COLLAPSE_KEY) === '1');
    } catch { /* gizli mod */ }
  }, []);

  function toggleCollapsed() {
    setCollapsed(prev => {
      const next = !prev;
      try { localStorage.setItem(COLLAPSE_KEY, next ? '1' : '0'); } catch { /* gizli mod */ }
      return next;
    });
  }

  // Ctrl/Cmd+K → aramaya odaklan (panonun beklenen kısayolu).
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault();
        setCollapsed(false);
        searchRef.current?.focus();
      }
      if (e.key === 'Escape') { setMenuOpen(false); setProjectMenuOpen(false); setDrawerOpen(false); }
    }
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, []);

  // Menü dışına tıklayınca kapansın.
  useEffect(() => {
    if (!menuOpen && !projectMenuOpen) return;
    function onClick(e: MouseEvent) {
      const t = e.target as Node;
      if (menuOpen && profileRef.current && !profileRef.current.contains(t)) setMenuOpen(false);
      if (projectMenuOpen && projectRef.current && !projectRef.current.contains(t)) setProjectMenuOpen(false);
    }
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [menuOpen, projectMenuOpen]);

  // Gorunum degisince mobil cekmece kendiliginden kapansin — kullanicinin
  // her secimden sonra ayrica kapatmasi gerekmesi, dar ekranda gezinmeyi
  // iki kat tiklamaya cikariyordu.
  useEffect(() => { setDrawerOpen(false); }, [view, activeTable]);

  const q = query.trim().toLowerCase();
  const matchingTables = (tables ?? []).filter(t => !q || t.name.toLowerCase().includes(q));

  return (
    <div className={`app${collapsed ? ' is-collapsed' : ''}${drawerOpen ? ' is-drawer-open' : ''}`}>
      {/* Dar ekranda cekmecenin arkasindaki ortu — tiklayinca kapanir. */}
      <div className="side-backdrop" onClick={() => setDrawerOpen(false)} aria-hidden="true" />

      {/* ── Sol gezinme ─────────────────────────────────────────────── */}
      <aside className="side">
        <div className="side-head">
          <span className="side-mark"><Database size={15} strokeWidth={2.2} /></span>
          <div className="side-brand">
            <span className="side-brand-name">Namines Desk</span>
            <span className="brand-badge">beta</span>
          </div>
        </div>

        <div className="side-search">
          <div className="search-box">
            <Search size={13} />
            <input
              ref={searchRef}
              type="search"
              value={query}
              placeholder="Ara…"
              aria-label="Gezinme ve tablolarda ara"
              onChange={e => setQuery(e.target.value)}
            />
            {!query && <span className="search-kbd">Ctrl K</span>}
          </div>
        </div>

        {/* Proje secici GERCEK bir acilir menu: onceki halde bu dugme
            dogrudan panoya atiyordu, ama yanindaki cift ok ikonu bir liste
            vaat ediyordu — "calismiyor" izlenimi buradan geliyordu. Artik
            projeler burada listeleniyor, panoya ugramadan gecis yapiliyor. */}
        <div className="side-project" ref={projectRef}>
          <button
            className="project-switch"
            onClick={() => setProjectMenuOpen(o => !o)}
            aria-haspopup="listbox"
            aria-expanded={projectMenuOpen}
            title="Proje değiştir"
          >
            <ArrowLeftRight size={14} />
            <span className="ps-body">
              <span className="ps-label">Proje</span>
              <span className="ps-name">{projectName ?? 'Seçilmedi'}</span>
            </span>
            <ChevronsUpDown size={13} />
          </button>

          {projectMenuOpen && (
            <div className="project-menu" role="listbox">
              <div className="project-menu-label">Projeler</div>
              {(projects ?? []).length === 0 && (
                <div className="nav-sub-empty">Proje yok.</div>
              )}
              {(projects ?? []).map(p => (
                <button
                  key={p.id}
                  className="project-menu-item"
                  role="option"
                  aria-current={p.id === projectId}
                  aria-selected={p.id === projectId}
                  title={p.hasConnection ? p.name : `${p.name} — canlı veritabanı bağlı değil`}
                  onClick={() => {
                    setProjectMenuOpen(false);
                    // Baglantisi olmayan proje acilamaz (sema okunamaz);
                    // kullaniciyi Import akisinin oldugu panoya gonderiyoruz.
                    if (p.hasConnection) onSelectProject(p.id);
                    else { onChangeProject(); }
                  }}
                >
                  <span
                    className={`dot ${p.hasConnection ? 'dot-connected' : 'dot-disconnected'}`}
                    aria-hidden="true"
                  />
                  <span className="pm-name">{p.name}</span>
                  {p.id === projectId
                    ? <Check size={13} />
                    : !p.hasConnection && <Plug size={12} className="pm-meta" />}
                </button>
              ))}
              <div className="project-menu-sep" />
              <button
                className="project-menu-item"
                onClick={() => { setProjectMenuOpen(false); onChangeProject(); }}
              >
                <LayoutGrid size={13} />
                <span className="pm-name">Tüm projeler</span>
              </button>
            </div>
          )}
        </div>

        <nav className="side-scroll" aria-label="Ana gezinme">
          {NAV_GROUPS.map(group => {
            const items = group.items.filter(item => {
              if (item.ownerOnly && !isOwner) return false;
              if (q && !VIEW_LABELS[item.view].toLowerCase().includes(q)) return false;
              return true;
            });
            if (items.length === 0) return null;

            return (
              <div className="side-group" key={group.label}>
                <div className="side-group-label">{group.label}</div>
                {items.map(item => {
                  const disabled = !!item.needsProject && !hasProject;
                  const Icon = item.icon;
                  return (
                    <div key={item.view}>
                      <button
                        className="nav-item"
                        aria-current={view === item.view}
                        disabled={disabled}
                        title={disabled ? 'Önce bir proje seçin' : VIEW_LABELS[item.view]}
                        onClick={() => onNavigate(item.view)}
                      >
                        <Icon size={15} />
                        <span>{VIEW_LABELS[item.view]}</span>
                        {disabled && <Lock size={11} style={{ marginLeft: 'auto' }} />}
                        {!disabled && item.view === 'data' && tables && (
                          <span className="nav-count">{tables.length}</span>
                        )}
                      </button>

                      {/* Tablo listesi Data'nın ALTINDA yuvalanıyor — üst
                          seviyede olsaydı gezinme, tablo sayısı arttıkça
                          (20+ tablo) kendi bölümlerini görünmez kılardı. */}
                      {item.view === 'data' && !disabled && (view === 'data' || q) && (
                        <div className="nav-sub">
                          {matchingTables.map(t => (
                            <button
                              key={t.name}
                              className="nav-item"
                              aria-current={view === 'data' && t.name === activeTable}
                              onClick={() => onSelectTable(t.name)}
                              title={t.name}
                            >
                              <span>{t.name}</span>
                            </button>
                          ))}
                          {matchingTables.length === 0 && (
                            <div className="nav-sub-empty">
                              {q ? 'Eşleşen tablo yok.' : 'Erişilebilir tablo yok.'}
                            </div>
                          )}
                        </div>
                      )}
                    </div>
                  );
                })}
              </div>
            );
          })}
        </nav>

        <div className="side-foot">
          <button
            className="collapse-btn"
            onClick={toggleCollapsed}
            title={collapsed ? 'Paneli genişlet' : 'Paneli daralt'}
            aria-label={collapsed ? 'Paneli genişlet' : 'Paneli daralt'}
          >
            {collapsed ? <PanelLeftOpen size={15} /> : <PanelLeftClose size={15} />}
            <span>Paneli daralt</span>
          </button>
        </div>
      </aside>

      {/* ── Üst şerit + içerik ───────────────────────────────────────── */}
      <div className="main-col">
        <header className="top">
          <button
            className="icon-btn top-menu-btn"
            onClick={() => setDrawerOpen(o => !o)}
            aria-label="Gezinmeyi aç"
            title="Gezinmeyi aç"
          >
            <Menu size={16} />
          </button>

          <div className="crumbs">
            <button className="crumb-link" onClick={() => onNavigate('overview')}>Namines Desk</button>
            {projectName && (
              <>
                <span className="crumb-sep">/</span>
                <button className="crumb-link" onClick={onChangeProject}>{projectName}</button>
              </>
            )}
            <span className="crumb-sep">/</span>
            <span className="crumb-current">{VIEW_LABELS[view]}</span>
          </div>

          <div className="top-actions">
            <a
              className="top-link"
              href={NAMINES_FRONTEND}
              target="_blank"
              rel="noreferrer"
              title="Namines'i aç — şema tasarım uygulaması"
            >
              <ExternalLink size={14} />
              <span>Namines</span>
            </a>
            <a className="top-link" href="mailto:support@namines.com" title="Destek ekibine yaz">
              <LifeBuoy size={14} />
              <span>Destek</span>
            </a>
            <ThemeToggle />

            <div className="profile" ref={profileRef}>
              <button
                className="profile-btn"
                onClick={() => setMenuOpen(o => !o)}
                aria-haspopup="menu"
                aria-expanded={menuOpen}
              >
                <span className="avatar">{initialsOf(user)}</span>
                <span className="profile-email">{user.email ?? user.name ?? 'Hesap'}</span>
                <ChevronsUpDown size={13} />
              </button>

              {menuOpen && (
                <div className="menu" role="menu">
                  <div className="menu-head">
                    <div className="mh-name">{user.name ?? 'Namines hesabı'}</div>
                    {user.email && <div className="mh-mail">{user.email}</div>}
                  </div>
                  <a className="menu-item" href={NAMINES_FRONTEND} target="_blank" rel="noreferrer" role="menuitem">
                    <ExternalLink size={14} /> Namines&apos;i aç
                  </a>
                  {hasProject && (
                    <button className="menu-item" role="menuitem" onClick={() => { setMenuOpen(false); onChangeProject(); }}>
                      <ArrowLeftRight size={14} /> Proje değiştir
                    </button>
                  )}
                  <a className="menu-item" href="mailto:support@namines.com" role="menuitem">
                    <LifeBuoy size={14} /> Destek
                  </a>
                  <button className="menu-item is-danger" role="menuitem" onClick={onSignOut}>
                    <LogOut size={14} /> Çıkış yap
                  </button>
                </div>
              )}
            </div>
          </div>
        </header>

        <div className={`content${contentFlush ? ' is-flush' : ''}`}>{children}</div>
      </div>
    </div>
  );
}
