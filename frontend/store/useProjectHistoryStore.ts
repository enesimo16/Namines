import { create } from 'zustand';
import { persist, createJSONStorage } from 'zustand/middleware';
import localforage from 'localforage';
import { ProjectSnapshot, ActiveSandbox, Branch } from '../types/project';
import { DatabaseSchema } from '../types/schema';
import { DbType } from './useSchemaStore';
import { Node } from '@xyflow/react';

// ── localforage instance (IndexedDB) ──────────────────────────────────────────
const naminesStore = localforage.createInstance({
  name: 'namines-v2',
  storeName: 'projects',
  description: 'Namines proje geçmişi (IndexedDB)',
});

/**
 * Zustand persist için localforage tabanlı custom storage adapter.
 * Schema ve node pozisyonları gibi büyük JSON objelerini
 * localStorage sınırını (5MB) aşmadan IndexedDB'de saklar.
 */
const localforageStorage = createJSONStorage<ProjectHistoryState>(() => ({
  getItem: async (name) => {
    const value = await naminesStore.getItem<string>(name);
    return value ?? null;           // undefined → null (zustand uyumluluğu)
  },
  setItem: async (name, value) => {
    await naminesStore.setItem(name, value);
  },
  removeItem: async (name) => {
    await naminesStore.removeItem(name);
  },
}));

// ── UUID üretici (crypto API) ─────────────────────────────────────────────────
const generateId = (): string =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2) + Date.now().toString(36);

// ── State tipi ─────────────────────────────────────────────────────────────────
interface ProjectHistoryState {
  projects: ProjectSnapshot[];
  activeProjectId: string | null;

  /** Mevcut şemayı ve node pozisyonlarını IndexedDB'ye kaydeder/günceller. */
  saveCurrentProject: (
    schema: DatabaseSchema,
    nodes: Node[],
    projectName: string,
    dbType: DbType
  ) => void;

  /** ID'ye göre proje snapshot'ını döndürür. */
  loadProject: (id: string) => ProjectSnapshot | undefined;

  /** Proje siler. */
  deleteProject: (id: string) => void;

  /** Proje adını günceller. */
  renameProject: (id: string, name: string) => void;

  /** Aktif proje ID'sini ayarlar. */
  setActiveProjectId: (id: string | null) => void;

  /**
   * Aktif proje için sandbox/admin panel oturumunu kaydeder.
   * Sayfa yenilemesinde bu bilgiyle arayüz restore edilir.
   */
  setActiveSandbox: (sandbox: ActiveSandbox | null) => void;

  /** Aktif projenin sandbox bilgisini döndürür. */
  getActiveSandbox: () => ActiveSandbox | null | undefined;

  /** Yeni dal oluşturur. */
  createBranch: (projectId: string, branchName: string) => Branch | undefined;

  /** Dallar arası geçiş yapar ve yeni dalın bilgilerini döndürür. */
  switchBranch: (projectId: string, branchName: string) => Branch | undefined;

  /** Belirtilen dalı siler (main silinemez). */
  deleteBranch: (projectId: string, branchName: string) => void;

  /** İki dalı birleştirir. */
  mergeBranch: (
    projectId: string,
    sourceBranch: string,
    targetBranch: string,
    mergedSchema: DatabaseSchema,
    mergedPositions: Record<string, { x: number; y: number }>
  ) => Branch | undefined;

  /** Hydration durumunu sorgulamak için */
  hasHydrated: boolean;
  setHasHydrated: (val: boolean) => void;
  setMigrationBaseline: (baseline: DatabaseSchema | null) => void;
}

// ── Store ──────────────────────────────────────────────────────────────────────
export const useProjectHistoryStore = create<ProjectHistoryState>()(
  persist(
    (set, get) => ({
      projects: [],
      activeProjectId: null,
      hasHydrated: false,

      saveCurrentProject: (schema, nodes, projectName, dbType) => {
        const { projects, activeProjectId, hasHydrated } = get();
        // HYDRATION GUARD: IndexedDB'den veriler henüz yüklenmemişse asla üzerine yazma!
        if (!hasHydrated) return;

        const now = new Date().toISOString();

        // Node pozisyonlarını kaydet
        const nodePositions: Record<string, { x: number; y: number }> = {};
        nodes.forEach(n => {
          nodePositions[n.id] = { x: n.position.x, y: n.position.y };
        });

        const existingIdx = activeProjectId
          ? projects.findIndex(p => p.id === activeProjectId)
          : -1;

        if (existingIdx !== -1) {
          const oldProj = projects[existingIdx];
          
          // Geriye dönük uyumluluk: branches yoksa main olarak oluştur
          let currentBranches = oldProj.branches ? [...oldProj.branches] : [];
          let currentBranchName = oldProj.currentBranch || 'main';

          if (currentBranches.length === 0) {
            currentBranches = [
              {
                name: 'main',
                schema: oldProj.schema || schema,
                nodePositions: oldProj.nodePositions || nodePositions,
                createdAt: oldProj.createdAt || now,
                updatedAt: oldProj.updatedAt || now,
              }
            ];
          }

          // Aktif dalı güncelle
          const activeBranchIdx = currentBranches.findIndex(b => b.name === currentBranchName);
          if (activeBranchIdx !== -1) {
            currentBranches[activeBranchIdx] = {
              ...currentBranches[activeBranchIdx],
              schema,
              nodePositions,
              updatedAt: now,
            };
          } else {
            currentBranches.push({
              name: currentBranchName,
              schema,
              nodePositions,
              createdAt: now,
              updatedAt: now,
            });
          }

          const updated = projects.map((p, i) =>
            i === existingIdx
              ? { 
                  ...p, 
                  name: projectName, 
                  dbType, 
                  schema, 
                  nodePositions, 
                  updatedAt: now,
                  branches: currentBranches,
                  currentBranch: currentBranchName
                }
              : p
          );
          set({ projects: updated });
        } else {
          // Yeni proje oluştur
          const newProjectId = generateId();
          const defaultBranch: Branch = {
            name: 'main',
            schema,
            nodePositions,
            createdAt: now,
            updatedAt: now,
          };

          const newProject: ProjectSnapshot = {
            id: newProjectId,
            name: projectName,
            createdAt: now,
            updatedAt: now,
            dbType,
            schema,
            nodePositions,
            activeSandbox: null,
            branches: [defaultBranch],
            currentBranch: 'main',
          };
          set({
            projects: [newProject, ...projects],
            activeProjectId: newProjectId,
          });
        }
      },

      loadProject: (id) => {
        return get().projects.find(p => p.id === id);
      },

      deleteProject: (id) => {
        const { projects, activeProjectId } = get();
        set({
          projects: projects.filter(p => p.id !== id),
          activeProjectId: activeProjectId === id ? null : activeProjectId,
        });
      },

      renameProject: (id, name) => {
        const updated = get().projects.map(p =>
          p.id === id ? { ...p, name, updatedAt: new Date().toISOString() } : p
        );
        set({ projects: updated });
      },

      setActiveProjectId: (id) => set({ activeProjectId: id }),

      setMigrationBaseline: (baseline) => {
        const { projects, activeProjectId, hasHydrated } = get();
        if (!hasHydrated || !activeProjectId) return;

        const updated = projects.map(p => {
          if (p.id !== activeProjectId) return p;

          const currentBranchName = p.currentBranch || 'main';
          const branches = p.branches ? [...p.branches] : [];

          const activeBranchIdx = branches.findIndex(b => b.name === currentBranchName);
          if (activeBranchIdx !== -1) {
            branches[activeBranchIdx] = {
              ...branches[activeBranchIdx],
              migrationBaseline: baseline,
              updatedAt: new Date().toISOString(),
            };
          }

          return {
            ...p,
            branches,
            migrationBaseline: baseline,
            updatedAt: new Date().toISOString(),
          };
        });

        set({ projects: updated });
      },

      setActiveSandbox: (sandbox) => {
        const { projects, activeProjectId } = get();
        if (!activeProjectId) return;

        const updated = projects.map(p =>
          p.id === activeProjectId
            ? { ...p, activeSandbox: sandbox }
            : p
        );
        set({ projects: updated });
      },

      getActiveSandbox: () => {
        const { projects, activeProjectId } = get();
        if (!activeProjectId) return null;
        return projects.find(p => p.id === activeProjectId)?.activeSandbox;
      },

      createBranch: (projectId, branchName) => {
        const { projects } = get();
        const now = new Date().toISOString();
        const projIdx = projects.findIndex(p => p.id === projectId);
        if (projIdx === -1) return undefined;

        const proj = projects[projIdx];
        let currentBranches = proj.branches ? [...proj.branches] : [];
        let currentBranchName = proj.currentBranch || 'main';

        // Geriye dönük uyumluluk: branches dizisi yoksa main oluştur
        if (currentBranches.length === 0) {
          currentBranches = [
            {
              name: 'main',
              schema: proj.schema,
              nodePositions: proj.nodePositions,
              createdAt: proj.createdAt,
              updatedAt: proj.updatedAt,
            }
          ];
        }

        // Zaten aynı isimde dal varsa hata verme, o dalı döndür
        const existingBranch = currentBranches.find(b => b.name === branchName);
        if (existingBranch) return existingBranch;

        // Aktif dalın yapısını kopyala
        const activeBranch = currentBranches.find(b => b.name === currentBranchName) || currentBranches[0];

        const newBranch: Branch = {
          name: branchName,
          schema: JSON.parse(JSON.stringify(activeBranch.schema)),
          nodePositions: { ...activeBranch.nodePositions },
          createdAt: now,
          updatedAt: now,
        };

        currentBranches.push(newBranch);

        const updatedProjects = projects.map((p, i) =>
          i === projIdx
            ? {
                ...p,
                branches: currentBranches,
                currentBranch: branchName, // Yeni dala otomatik geç
                schema: newBranch.schema,  // root'a da yaz (backward compatibility)
                nodePositions: newBranch.nodePositions,
                updatedAt: now,
              }
            : p
        );

        set({ projects: updatedProjects });
        return newBranch;
      },

      switchBranch: (projectId, branchName) => {
        const { projects } = get();
        const projIdx = projects.findIndex(p => p.id === projectId);
        if (projIdx === -1) return undefined;

        const proj = projects[projIdx];
        let currentBranches = proj.branches ? [...proj.branches] : [];

        if (currentBranches.length === 0) {
          currentBranches = [
            {
              name: 'main',
              schema: proj.schema,
              nodePositions: proj.nodePositions,
              createdAt: proj.createdAt,
              updatedAt: proj.updatedAt,
            }
          ];
        }

        const targetBranch = currentBranches.find(b => b.name === branchName);
        if (!targetBranch) return undefined;

        const updatedProjects = projects.map((p, i) =>
          i === projIdx
            ? {
                ...p,
                currentBranch: branchName,
                schema: targetBranch.schema,
                nodePositions: targetBranch.nodePositions,
                updatedAt: new Date().toISOString(),
              }
            : p
        );

        set({ projects: updatedProjects });
        return targetBranch;
      },

      deleteBranch: (projectId, branchName) => {
        if (branchName === 'main') return; // main silinemez

        const { projects } = get();
        const projIdx = projects.findIndex(p => p.id === projectId);
        if (projIdx === -1) return;

        const proj = projects[projIdx];
        if (!proj.branches) return;

        const newBranches = proj.branches.filter(b => b.name !== branchName);
        let nextBranchName = proj.currentBranch || 'main';

        // Silinen dal aktif dalsa, main'e geç
        if (nextBranchName === branchName) {
          nextBranchName = 'main';
        }

        const targetBranch = newBranches.find(b => b.name === nextBranchName) || newBranches[0];

        const updatedProjects = projects.map((p, i) =>
          i === projIdx
            ? {
                ...p,
                branches: newBranches,
                currentBranch: nextBranchName,
                schema: targetBranch ? targetBranch.schema : p.schema,
                nodePositions: targetBranch ? targetBranch.nodePositions : p.nodePositions,
                updatedAt: new Date().toISOString(),
              }
            : p
        );

        set({ projects: updatedProjects });
      },

      mergeBranch: (projectId, sourceBranch, targetBranch, mergedSchema, mergedPositions) => {
        const { projects } = get();
        const now = new Date().toISOString();
        const projIdx = projects.findIndex(p => p.id === projectId);
        if (projIdx === -1) return undefined;

        const proj = projects[projIdx];
        if (!proj.branches) return undefined;

        const newBranches = proj.branches.map(b => {
          if (b.name === targetBranch) {
            return {
              ...b,
              schema: mergedSchema,
              nodePositions: mergedPositions,
              updatedAt: now,
            };
          }
          return b;
        });

        // Eğer aktif dal targetBranch ise kök dizini de güncelle
        const isTargetActive = proj.currentBranch === targetBranch;

        const updatedProjects = projects.map((p, i) =>
          i === projIdx
            ? {
                ...p,
                branches: newBranches,
                schema: isTargetActive ? mergedSchema : p.schema,
                nodePositions: isTargetActive ? mergedPositions : p.nodePositions,
                updatedAt: now,
              }
            : p
        );

        set({ projects: updatedProjects });
        return newBranches.find(b => b.name === targetBranch);
      },

      setHasHydrated: (val) => set({ hasHydrated: val }),
    }),
    {
      name: 'namines-projects', // IndexedDB key
      storage: localforageStorage,
      onRehydrateStorage: () => (state) => {
        state?.setHasHydrated(true);
      },
    }
  )
);

