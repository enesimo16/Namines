import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';

export interface ScopedHubRepoInfo {
  id: string;
  owner: string;
  slug: string;
  name: string;
}

interface ActiveProjectSessionState {
  scopedProjectId: string | null;
  scopedProjectName: string | null;
  scopedHubRepo: ScopedHubRepoInfo | null;
  setScopedProject: (id: string | null, name?: string | null) => void;
  setScopedHubRepo: (repo: ScopedHubRepoInfo | null) => void;
  clearProjectSession: () => void;
}

/**
 * Faz 5 — Proje Oturumu Bağlamı (Project-Scoped Workspace vs. Global Console).
 *
 * Kullanıcı belirli bir projenin içine girdiğinde (`/hub/:owner/:slug`,
 * `/vault?project=<id>`, `/ground?project=<id>`, `/flow?project=<id>`,
 * `/canvas?project=<id>`, `/compile?project=<id>`), üst gezinti çubuğundaki
 * (`WorkspaceNav`) tüm sekmeler bu projeye kilitlenir.
 *
 * `sessionStorage` ile korunur; böylece Hub'dan Vault/Ground/Flow'a geçildiğinde
 * veya sekme yenilendiğinde repo sahibi ({owner}) ve repo adı ({name}) kaybolmaz,
 * ekrandaki ekmek kırıntısı (breadcrumb) her zaman `vault / owner / project-name`
 * şeklinde tam ve doğru kalır.
 */
export const useActiveProjectStore = create<ActiveProjectSessionState>()(
  persist(
    (set) => ({
      scopedProjectId: null,
      scopedProjectName: null,
      scopedHubRepo: null,
      setScopedProject: (id, name = null) =>
        set((state) => ({
          scopedProjectId: id,
          scopedProjectName: name !== null ? name : state.scopedProjectName,
        })),
      setScopedHubRepo: (repo) =>
        set({
          scopedHubRepo: repo,
          scopedProjectId: repo ? repo.id : null,
          scopedProjectName: repo ? repo.name : null,
        }),
      clearProjectSession: () =>
        set({
          scopedProjectId: null,
          scopedProjectName: null,
          scopedHubRepo: null,
        }),
    }),
    {
      name: 'namines_scoped_project_session',
      storage: createJSONStorage(() => sessionStorage),
    },
  ),
);
