'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import AppShell from './AppShell';
import Desk from './Desk';
import Projects from './Projects';
import DeskLanding from './DeskLanding';
import { fetchProjects, getToken, clearToken, AuthError, type DeskProject } from '../lib/auth';
import { type DeskView, PROJECT_VIEWS } from '../lib/nav';
import type { DeskTable } from '../lib/schema';
import { useT } from '../lib/i18n';

/**
 * Giriş kapısı, tek sayfalık tanıtım ve çalışma alanı yönlendirmesi.
 *
 * - Oturum yoksa: Namines logosu, tanıtımı ve Namines AuthModal ile tek sayfalık Landing.
 * - Namines üzerinden gelindiyse (SSO / handoff) veya oturum açıksa: Doğrudan AppShell çalışma alanı.
 */
const PROJECT_KEY = 'namines-desk-project';
const VIEW_KEY = 'namines-desk-view';

export default function Page() {
  const t = useT();
  const [token, setTok] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  const [projects, setProjects] = useState<DeskProject[] | null>(null);
  const [projectsError, setProjectsError] = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(null);

  // Kabuk durumu: hangi görünüm, hangi tablo, ve Desk'in okuduğu tablo listesi.
  const [view, setView] = useState<DeskView>('overview');
  const [activeTable, setActiveTable] = useState<string | null>(null);
  const [tables, setTables] = useState<DeskTable[] | null>(null);

  useEffect(() => {
    setTok(getToken());
    try {
      const savedProject = sessionStorage.getItem(PROJECT_KEY);
      const savedView = sessionStorage.getItem(VIEW_KEY) as DeskView | null;
      if (savedProject) setProjectId(savedProject);
      if (savedView && (savedView === 'overview' || PROJECT_VIEWS.includes(savedView))) setView(savedView);
    } catch { /* gizli mod */ }
    setReady(true);
  }, []);

  const restoredRef = useRef(false);
  useEffect(() => {
    if (!restoredRef.current) { restoredRef.current = true; return; }
    try {
      if (projectId) sessionStorage.setItem(PROJECT_KEY, projectId);
      else sessionStorage.removeItem(PROJECT_KEY);
      sessionStorage.setItem(VIEW_KEY, view);
    } catch { /* gizli mod */ }
  }, [projectId, view]);

  const loadProjects = useCallback(async () => {
    if (!token) return;
    try {
      const list = await fetchProjects(token);
      setProjects(list);
      setProjectsError(null);
    } catch (err) {
      if (err instanceof AuthError && err.status === 401) {
        clearToken();
        setTok(null);
        return;
      }
      setProjectsError(err instanceof Error ? err.message : t('login.loadProjectsFailed'));
    }
  }, [token, t]);

  useEffect(() => { loadProjects(); }, [loadProjects]);

  const handleTablesLoaded = useCallback((next: DeskTable[] | null) => {
    setTables(next);
    setActiveTable(prev => (prev && next?.some(t => t.name === prev))
      ? prev
      : (next?.[0]?.name ?? null));
  }, []);

  const handleSelectTable = useCallback((name: string) => {
    setActiveTable(name);
    setView('data');
  }, []);

  const session = useMemo(
    () => ({ token: token ?? '', projectId: projectId ?? '' }),
    [token, projectId],
  );

  if (!ready) return null;

  function signOut() {
    clearToken();
    setTok(null);
    setProjects(null);
    setProjectId(null);
    setTables(null);
    setActiveTable(null);
    setView('overview');
  }

  function changeProject() {
    setProjectId(null);
    setTables(null);
    setActiveTable(null);
    setView('overview');
  }

  function selectProject(id: string) {
    setProjectId(id);
    setActiveTable(null);
    setTables(null);
    setView('canvas');
  }

  function useGroundFor(id: string) {
    setProjectId(id);
    setActiveTable(null);
    setTables(null);
    setView('ground');
  }

  // Oturum yoksa: Namines Desk Tek Sayfalık Tanıtım & Auth Ekranı
  if (!token) {
    return <DeskLanding onAuthSuccess={jwt => { setTok(jwt); }} />;
  }

  const selectedProject = projects?.find(p => p.id === projectId) ?? null;
  const hasProject = !!selectedProject;
  const effectiveView: DeskView = !hasProject && PROJECT_VIEWS.includes(view) ? 'overview' : view;

  return (
    <AppShell
      token={token}
      view={effectiveView}
      onNavigate={setView}
      projectName={selectedProject?.name ?? null}
      onChangeProject={changeProject}
      onSignOut={signOut}
      tables={tables}
      activeTable={activeTable}
      onSelectTable={handleSelectTable}
      isOwner={selectedProject?.isOwner ?? false}
      hasProject={hasProject}
      contentFlush={effectiveView === 'canvas'}
      projects={projects}
      projectId={projectId}
      onSelectProject={selectProject}
    >
      {effectiveView === 'overview' || !selectedProject ? (
        <Projects
          projects={projects}
          error={projectsError}
          token={token}
          onSelect={selectProject}
          onUseGround={useGroundFor}
          onImported={loadProjects}
        />
      ) : (
        <Desk
          key={selectedProject.id}
          session={session}
          designSchemaJson={selectedProject.schemaJson}
          nodePositionsJson={selectedProject.nodePositionsJson}
          isOwner={selectedProject.isOwner}
          allowDeskSql={selectedProject.allowDeskSql}
          onDeskSqlToggled={loadProjects}
          onChangeProject={changeProject}
          view={effectiveView}
          activeTable={activeTable}
          onSelectTable={handleSelectTable}
          onTablesLoaded={handleTablesLoaded}
        />
      )}
    </AppShell>
  );
}
