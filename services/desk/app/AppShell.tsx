'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import {
  Database, Search, ChevronsUpDown, LayoutGrid, Workflow, Table2, Terminal,
  GitBranch, ScrollText, BarChart3, Users, KeyRound, PanelLeftClose, PanelLeftOpen,
  LogOut, ExternalLink, LifeBuoy, ArrowLeftRight, Lock, Menu, Check, Plug, Archive, Server,
} from 'lucide-react';
import { type DeskView, decodeSessionUser, initialsOf } from '../lib/nav';
import { useT, type MessageKey } from '../lib/i18n';
import type { DeskTable } from '../lib/schema';
import type { DeskProject } from '../lib/auth';
import ThemeToggle from './ThemeToggle';
import LanguageToggle from './LanguageToggle';

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

const NAV_GROUPS: { labelKey: MessageKey; items: NavEntry[] }[] = [
  {
    labelKey: 'nav.groups.general',
    items: [{ view: 'overview', icon: LayoutGrid }],
  },
  {
    labelKey: 'nav.groups.database',
    items: [
      { view: 'canvas', icon: Workflow, needsProject: true },
      { view: 'data', icon: Table2, needsProject: true },
      { view: 'sql', icon: Terminal, needsProject: true, ownerOnly: true },
    ],
  },
  {
    labelKey: 'nav.groups.operations',
    items: [
      { view: 'deployments', icon: GitBranch, needsProject: true },
      { view: 'logs', icon: ScrollText, needsProject: true },
      { view: 'analytics', icon: BarChart3, needsProject: true },
      { view: 'vault', icon: Archive, needsProject: true },
      { view: 'ground', icon: Server, needsProject: true, ownerOnly: true },
    ],
  },
  {
    labelKey: 'nav.groups.settings',
    items: [
      { view: 'members', icon: Users, needsProject: true },
      { view: 'apikeys', icon: KeyRound, needsProject: true, ownerOnly: true },
    ],
  },
];

/**
 * Namines Desk uygulama kabuğu — Shopify Polaris düzen mimarisi + Cloudflare estetiği.
 *
 * - Kalıcı ve bağımsız kaydırılabilir sol gezinme (50+ tabloda menüler bozulmaz).
 * - Yalınlaştırılmış üst çubuk: mükerrer butonlar kaldırılmış, profil menüsünde toplanmıştır.
 * - Namines ile ortak font ve hiyerarşi.
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
  projects: DeskProject[] | null;
  projectId: string | null;
  onSelectProject: (id: string) => void;
  contentFlush?: boolean;
  children: React.ReactNode;
}) {
  const t = useT();
  const viewLabel = (v: DeskView) => t(`nav.views.${v}`);
  const [collapsed, setCollapsed] = useState(false);
  const [query, setQuery] = useState('');
  const [tableFilter, setTableFilter] = useState('');
  const [menuOpen, setMenuOpen] = useState(false);
  const [projectMenuOpen, setProjectMenuOpen] = useState(false);
  const [drawerOpen, setDrawerOpen] = useState(false);
  const searchRef = useRef<HTMLInputElement>(null);
  const profileRef = useRef<HTMLDivElement>(null);
  const projectRef = useRef<HTMLDivElement>(null);

  const user = useMemo(() => decodeSessionUser(token), [token]);

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

  // Ctrl/Cmd+K → aramaya odaklan
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

  // Menü dışına tıklayınca kapansın
  useEffect(() => {
    if (!menuOpen && !projectMenuOpen) return;
    function onClick(e: MouseEvent) {
      const target = e.target as Node;
      if (menuOpen && profileRef.current && !profileRef.current.contains(target)) setMenuOpen(false);
      if (projectMenuOpen && projectRef.current && !projectRef.current.contains(target)) setProjectMenuOpen(false);
    }
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [menuOpen, projectMenuOpen]);

  useEffect(() => { setDrawerOpen(false); }, [view, activeTable]);

  const q = query.trim().toLowerCase();
  const tq = tableFilter.trim().toLowerCase();
  const matchingTables = (tables ?? []).filter(table => {
    const name = table.name.toLowerCase();
    if (q && !name.includes(q)) return false;
    if (tq && !name.includes(tq)) return false;
    return true;
  });

  return (
    <div className={`app${collapsed ? ' is-collapsed' : ''}${drawerOpen ? ' is-drawer-open' : ''}`}>
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
              placeholder={t('shell.search')}
              aria-label={t('shell.searchLabel')}
              onChange={e => setQuery(e.target.value)}
            />
            {!query && <span className="search-kbd">Ctrl K</span>}
          </div>
        </div>

        {/* Proje seçici */}
        <div className="side-project" ref={projectRef}>
          <button
            className="project-switch"
            onClick={() => setProjectMenuOpen(o => !o)}
            aria-haspopup="listbox"
            aria-expanded={projectMenuOpen}
            title={t('shell.switchProject')}
          >
            <ArrowLeftRight size={14} />
            <span className="ps-body">
              <span className="ps-label">{t('shell.project')}</span>
              <span className="ps-name">{projectName ?? t('shell.noneSelected')}</span>
            </span>
            <ChevronsUpDown size={13} />
          </button>

          {projectMenuOpen && (
            <div className="project-menu" role="listbox">
              <div className="project-menu-label">{t('shell.projects')}</div>
              {(projects ?? []).length === 0 && (
                <div className="nav-sub-empty">{t('shell.noProjects')}</div>
              )}
              {(projects ?? []).map(p => (
                <button
                  key={p.id}
                  className="project-menu-item"
                  role="option"
                  aria-current={p.id === projectId}
                  aria-selected={p.id === projectId}
                  title={p.hasConnection ? p.name : t('shell.noConnection', { name: p.name })}
                  onClick={() => {
                    setProjectMenuOpen(false);
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
                <span className="pm-name">{t('shell.allProjects')}</span>
              </button>
            </div>
          )}
        </div>

        <nav className="side-scroll" aria-label={t('shell.mainNavigation')}>
          {NAV_GROUPS.map(group => {
            const items = group.items.filter(item => {
              if (item.ownerOnly && !isOwner) return false;
              if (q && !viewLabel(item.view).toLowerCase().includes(q)) return false;
              return true;
            });
            if (items.length === 0) return null;

            return (
              <div className="side-group" key={group.labelKey}>
                <div className="side-group-label">{t(group.labelKey)}</div>
                {items.map(item => {
                  const disabled = !!item.needsProject && !hasProject;
                  const Icon = item.icon;
                  return (
                    <div key={item.view}>
                      <button
                        className="nav-item"
                        aria-current={view === item.view}
                        disabled={disabled}
                        title={disabled ? t('shell.selectProjectFirst') : viewLabel(item.view)}
                        onClick={() => onNavigate(item.view)}
                      >
                        <Icon size={15} />
                        <span>{viewLabel(item.view)}</span>
                        {disabled && <Lock size={11} style={{ marginLeft: 'auto' }} />}
                        {!disabled && item.view === 'data' && tables && (
                          <span className="nav-count">{tables.length}</span>
                        )}
                      </button>

                      {/* 50+ Tablolu yapılarda alt menüleri (SQL, Dağıtımlar vb.)
                          aşağı itmeyen, bağımsız kaydırılabilir ve filtrelenebilir tablo listesi */}
                      {item.view === 'data' && !disabled && (view === 'data' || q) && (
                        <div className="nav-sub-scrollable">
                          {tables && tables.length > 8 && (
                            <div className="nav-sub-filter">
                              <input
                                type="search"
                                value={tableFilter}
                                placeholder={t('shell.search')}
                                aria-label={t('shell.searchLabel')}
                                onChange={e => setTableFilter(e.target.value)}
                              />
                            </div>
                          )}
                          {matchingTables.map(table => (
                            <button
                              key={table.name}
                              className="nav-item"
                              aria-current={view === 'data' && table.name === activeTable}
                              onClick={() => onSelectTable(table.name)}
                              title={table.name}
                            >
                              <span>{table.name}</span>
                            </button>
                          ))}
                          {matchingTables.length === 0 && (
                            <div className="nav-sub-empty">
                              {q || tq ? t('shell.noMatchingTables') : t('shell.noAccessibleTables')}
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
            title={collapsed ? t('shell.expandPanel') : t('shell.collapsePanel')}
            aria-label={collapsed ? t('shell.expandPanel') : t('shell.collapsePanel')}
          >
            {collapsed ? <PanelLeftOpen size={15} /> : <PanelLeftClose size={15} />}
            <span>{t('shell.collapsePanel')}</span>
          </button>
        </div>
      </aside>

      {/* ── Üst şerit + içerik ───────────────────────────────────────── */}
      <div className="main-col">
        <header className="top">
          <button
            className="icon-btn top-menu-btn"
            onClick={() => setDrawerOpen(o => !o)}
            aria-label={t('shell.openNavigation')}
            title={t('shell.openNavigation')}
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
            <span className="crumb-current">{viewLabel(view)}</span>
          </div>

          {/* Mükerrer Namines/Destek butonları üst bardan kaldırıldı;
              sağ tarafta yalnızca Dil, Tema ve Profil menüsü yer alır. */}
          <div className="top-actions">
            <LanguageToggle />
            <ThemeToggle />

            <div className="profile" ref={profileRef}>
              <button
                className="profile-btn"
                onClick={() => setMenuOpen(o => !o)}
                aria-haspopup="menu"
                aria-expanded={menuOpen}
              >
                <span className="avatar">{initialsOf(user)}</span>
                <span className="profile-email">{user.email ?? user.name ?? t('shell.account')}</span>
                <ChevronsUpDown size={13} />
              </button>

              {menuOpen && (
                <div className="menu" role="menu">
                  <div className="menu-head">
                    <div className="mh-name">{user.name ?? t('shell.naminesAccount')}</div>
                    {user.email && <div className="mh-mail">{user.email}</div>}
                  </div>
                  <a className="menu-item" href={NAMINES_FRONTEND} target="_blank" rel="noreferrer" role="menuitem">
                    <ExternalLink size={14} /> {t('shell.openNamines')}
                  </a>
                  {hasProject && (
                    <button className="menu-item" role="menuitem" onClick={() => { setMenuOpen(false); onChangeProject(); }}>
                      <ArrowLeftRight size={14} /> {t('shell.switchProject')}
                    </button>
                  )}
                  <a className="menu-item" href="mailto:support@namines.com" role="menuitem">
                    <LifeBuoy size={14} /> {t('shell.support')}
                  </a>
                  <button className="menu-item is-danger" role="menuitem" onClick={onSignOut}>
                    <LogOut size={14} /> {t('shell.signOut')}
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
