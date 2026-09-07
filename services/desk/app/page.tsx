'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Database } from 'lucide-react';
import AppShell from './AppShell';
import Desk from './Desk';
import Projects from './Projects';
import { login, fetchProjects, getToken, setToken, clearToken, AuthError, type DeskProject } from '../lib/auth';
import { type DeskView, PROJECT_VIEWS } from '../lib/nav';
import type { DeskTable } from '../lib/schema';

/**
 * Giris kapisi + yonlendirme — D1 (namines_desk/01-KIMLIK-VE-OTURUM.md).
 *
 * v0.1'deki ham Gateway API anahtari yerine ana Namines hesabiyla oturum:
 * e-posta + parola -> JWT -> pano. JWT `sessionStorage`'da tutuluyor,
 * `localStorage`'da DEGIL: sekme kapaninca kalmamali.
 *
 * <b>v2.1:</b> giris yapan kullanici artik ortada yuzen bir "proje sec"
 * kartiyla degil, tam bir yonetim panosuyla karsilaniyor (`AppShell`).
 * Proje secimi bir ADIM degil, panonun icindeki bir GORUNUM (`overview`) —
 * kullanici projeler arasinda gezinirken kabuktan hic cikmiyor.
 */
const PROJECT_KEY = 'namines-desk-project';
const VIEW_KEY = 'namines-desk-view';

export default function Page() {
  const [token, setTok] = useState<string | null>(null);
  const [ready, setReady] = useState(false);

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [loginError, setLoginError] = useState<string | null>(null);
  const [loggingIn, setLoggingIn] = useState(false);

  const [projects, setProjects] = useState<DeskProject[] | null>(null);
  const [projectsError, setProjectsError] = useState<string | null>(null);
  const [projectId, setProjectId] = useState<string | null>(null);

  // Kabuk durumu: hangi gorunum, hangi tablo, ve Desk'in okudugu tablo listesi.
  const [view, setView] = useState<DeskView>('overview');
  const [activeTable, setActiveTable] = useState<string | null>(null);
  const [tables, setTables] = useState<DeskTable[] | null>(null);

  useEffect(() => {
    setTok(getToken());
    // Secili proje ve gorunum, JWT ile AYNI omurde (sessionStorage) saklaniyor:
    // yenilemeden sonra kullanicinin bulundugu yerden devam etmesi icin.
    // localStorage OLMAZ — sekme kapaninca oturum zaten bitiyor, proje
    // baglaminin ondan uzun yasamasi tutarsiz olurdu.
    try {
      const savedProject = sessionStorage.getItem(PROJECT_KEY);
      const savedView = sessionStorage.getItem(VIEW_KEY) as DeskView | null;
      if (savedProject) setProjectId(savedProject);
      if (savedView && (savedView === 'overview' || PROJECT_VIEWS.includes(savedView))) setView(savedView);
    } catch { /* gizli mod */ }
    setReady(true);
  }, []);

  // Secim degistikce yaz — ama GERI YUKLEME BITMEDEN YAZMA.
  //
  // Bu koruma olmadan gercek bir hata olusuyordu: yazma efekti ilk render'da
  // da calisiyor ve o anda state hala baslangic degerinde ('overview', null)
  // oluyor. Yani geri yukleme efekti kaydi okuduktan hemen sonra, yazma
  // efekti ayni kaydi baslangic degeriyle EZIYORDU. Belirti: yenilemeden
  // sonra proje geliyor ama gorunum hep 'Projeler'e dusuyordu.
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
        // Oturum sunucu tarafinda gecersiz (suresi dolmus/iptal) — kullaniciyi
        // sessizce yeniden giris ekranina dondur.
        clearToken();
        setTok(null);
        return;
      }
      setProjectsError(err instanceof Error ? err.message : 'Projeler okunamadi.');
    }
  }, [token]);

  useEffect(() => { loadProjects(); }, [loadProjects]);

  // Desk'in bildirdigi tablo listesi kabuga aktariliyor. `useCallback`:
  // Desk bunu bir effect bagimliligi olarak kullaniyor, her render'da yeni bir
  // fonksiyon vermek sonsuz donguye yol acardi.
  const handleTablesLoaded = useCallback((next: DeskTable[] | null) => {
    setTables(next);
    // Varsayilan tablo burada seciliyor — GORUNUM DEGISTIRMEDEN. Bu secim
    // Desk'in icinde `onSelectTable` ile yapilsaydi, sema okunur okunmaz
    // kullanici Data'ya atlardi (proje acilisinin varsayilani Sema).
    setActiveTable(prev => (prev && next?.some(t => t.name === prev))
      ? prev
      : (next?.[0]?.name ?? null));
  }, []);
  const handleSelectTable = useCallback((name: string) => {
    setActiveTable(name);
    setView('data');
  }, []);

  // Oturum nesnesi MEMOIZE EDILIYOR — kritik, ve TUM erken return'lerden ONCE
  // (hook'lar kosullu cagrilamaz).
  //
  // Onceden `session={{ token, projectId }}` satir ici yaziliyordu, yani her
  // render'da YENI bir nesne uretiliyordu. `Desk.tsx`'in uc efekti de
  // (`reloadSchema`, surum yoklamasi, `loadRows`) `[session]`'a bagli oldugu
  // icin her render yeniden istek atiyor, gelen yanit state'i guncelleyip
  // yeni bir render tetikliyordu: kapali bir dongu.
  //
  // OLCULDU: 8 saniyede 978 istek. Gateway'in 1200/dk limiti ~10 saniyede
  // doluyor ve arayuz 429 yagmuruna tutuluyordu — "Veri ekrani duzgun
  // calismiyor" sikayetinin gercek sebebi buydu.
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
    // Yeni projede varsayilan gorunum sema: kullanicinin once neye baktigini
    // gormesi, ham satirlara dusmesinden daha iyi bir giris (D3).
    setView('canvas');
  }

  if (!token) {
    return (
      <div className="gate">
        <form
          className="gate-card"
          onSubmit={async e => {
            e.preventDefault();
            setLoginError(null);
            setLoggingIn(true);
            try {
              const t = await login(email.trim(), password);
              setToken(t);
              setTok(t);
            } catch (err) {
              setLoginError(err instanceof Error ? err.message : 'Giris basarisiz.');
            } finally {
              setLoggingIn(false);
            }
          }}
        >
          <div className="gate-brand">
            <span className="side-mark"><Database size={15} strokeWidth={2.2} /></span>
            <h1>Namines Desk <span className="brand-badge">beta</span></h1>
          </div>
          <p>
            Namines hesabinizla giris yapin. Veritabani baglantiniz sunucuda
            sifreli duruyor &mdash; parolaniz bu sayfaya hicbir zaman gelmez.
          </p>
          {loginError && <div className="notice notice-error">{loginError}</div>}
          <div className="field">
            <label htmlFor="email">E-posta</label>
            <input id="email" type="email" value={email} placeholder="ad@ornek.com"
                   onChange={e => setEmail(e.target.value)} autoFocus required />
          </div>
          <div className="field">
            <label htmlFor="password">Parola</label>
            <input id="password" type="password" value={password}
                   onChange={e => setPassword(e.target.value)} required />
          </div>
          <button type="submit" className="btn btn-primary" style={{ width: '100%' }} disabled={loggingIn}>
            {loggingIn ? 'Giris yapiliyor…' : 'Giris yap'}
          </button>
        </form>
      </div>
    );
  }

  // Canvas (D3) drift tespiti icin: secili projenin tasarim semasi + yerlesimi.
  // Bunlar zaten `/api/auth/projects`'ten geldi (D2) — ayri bir cagri gerekmiyor.
  const selectedProject = projects?.find(p => p.id === projectId) ?? null;
  const hasProject = !!selectedProject;
  // Proje kapsamli bir gorunum secili ama proje yoksa (ornegin proje
  // degistirildi), panoya dus — bos bir ekran gostermek yerine.
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
