import { create } from 'zustand';
import {
  adminService,
  type AdminFeatureFlagItem,
  type AdminAiToolRouteItem,
} from '../services/adminService';
import { useToastStore } from './useToastStore';
import { useFeatureStore } from './useFeatureStore';

export type FeatureCategoryFilter =
  | 'all'
  | 'Tools'
  | 'Compile'
  | 'Eject'
  | 'Platform'
  | 'AI';

export type FeatureStatusFilter = 'all' | 'enabled' | 'notice' | 'hidden';

export interface AdminConsoleStore {
  // ── Features State ──────────────────────────────────────────────────────────
  flags: AdminFeatureFlagItem[];
  loadingFlags: boolean;
  flagsError: string | null;
  featureSearch: string;
  featureCategory: FeatureCategoryFilter;
  featureStatus: FeatureStatusFilter;
  selectedFeatureKey: string | null;
  expandedDrawerKeys: Record<string, boolean>;

  // ── AI Routing State ────────────────────────────────────────────────────────
  aiRoutes: AdminAiToolRouteItem[];
  loadingAiRoutes: boolean;
  aiRoutesError: string | null;
  aiSearch: string;
  aiProviderFilter: string; // 'all' | 'groq' | 'deepseek' | 'claude' | 'openai'
  selectedAiToolKey: string | null;

  // ── System Diagnostics State ────────────────────────────────────────────────
  selectedSystemComponent: string | null;

  // ── Feature Flag Actions ────────────────────────────────────────────────────
  fetchFlags: () => Promise<void>;
  setFeatureSearch: (val: string) => void;
  setFeatureCategory: (cat: FeatureCategoryFilter) => void;
  setFeatureStatus: (status: FeatureStatusFilter) => void;
  setSelectedFeatureKey: (key: string | null) => void;
  toggleDrawerExpand: (key: string) => void;
  toggleFeatureFlag: (key: string) => Promise<boolean>;
  updateFeatureFlag: (
    key: string,
    updates: Partial<AdminFeatureFlagItem>
  ) => Promise<boolean>;
  bulkUpdateCategory: (category: string, isEnabled: boolean) => Promise<boolean>;

  // ── AI Route Actions ────────────────────────────────────────────────────────
  fetchAiRoutes: () => Promise<void>;
  setAiSearch: (val: string) => void;
  setAiProviderFilter: (prov: string) => void;
  setSelectedAiToolKey: (key: string | null) => void;
  updateAiRoute: (
    toolKey: string,
    updates: Partial<AdminAiToolRouteItem>
  ) => Promise<boolean>;
  bulkSetGlobalProvider: (provider: string) => Promise<boolean>;

  // ── System Actions ──────────────────────────────────────────────────────────
  setSelectedSystemComponent: (comp: string | null) => void;
}

export const useAdminConsoleStore = create<AdminConsoleStore>((set, get) => ({
  // Initial state
  flags: [],
  loadingFlags: false,
  flagsError: null,
  featureSearch: '',
  featureCategory: 'all',
  featureStatus: 'all',
  selectedFeatureKey: null,
  expandedDrawerKeys: {},

  aiRoutes: [],
  loadingAiRoutes: false,
  aiRoutesError: null,
  aiSearch: '',
  aiProviderFilter: 'all',
  selectedAiToolKey: null,

  selectedSystemComponent: null,

  // ── Feature Flags Implementation ──────────────────────────────────────────
  fetchFlags: async () => {
    set({ loadingFlags: true, flagsError: null });
    try {
      const data = await adminService.getFeatureFlags();
      set({ flags: data || [], loadingFlags: false });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Özellik bayrakları yüklenemedi';
      set({ loadingFlags: false, flagsError: msg });
    }
  },

  setFeatureSearch: (val: string) => set({ featureSearch: val }),
  setFeatureCategory: (cat: FeatureCategoryFilter) => set({ featureCategory: cat }),
  setFeatureStatus: (status: FeatureStatusFilter) => set({ featureStatus: status }),

  setSelectedFeatureKey: (key: string | null) => {
    set({ selectedFeatureKey: key });
    if (key) {
      // Auto-expand drawer when selected
      set((state) => ({
        expandedDrawerKeys: { ...state.expandedDrawerKeys, [key]: true },
      }));
    }
  },

  toggleDrawerExpand: (key: string) => {
    set((state) => ({
      expandedDrawerKeys: {
        ...state.expandedDrawerKeys,
        [key]: !state.expandedDrawerKeys[key],
      },
    }));
  },

  toggleFeatureFlag: async (key: string) => {
    const current = get().flags;
    const item = current.find((f) => f.featureKey.toLowerCase() === key.toLowerCase());
    if (!item) return false;

    const nextState = !item.isEnabled;

    // Optimistic local update
    set({
      flags: current.map((f) =>
        f.featureKey.toLowerCase() === key.toLowerCase()
          ? { ...f, isEnabled: nextState, updatedAt: new Date().toISOString() }
          : f
      ),
    });

    // Also update public feature store so canvas/compile UI reacts immediately
    useFeatureStore.getState().setFlagLocal(key, nextState, item.mode, item.noticeMessage);

    try {
      const updated = await adminService.updateFeatureFlag(key, { isEnabled: nextState });
      set((state) => ({
        flags: state.flags.map((f) =>
          f.featureKey.toLowerCase() === key.toLowerCase() ? updated : f
        ),
      }));
      useToastStore
        .getState()
        .showToast(
          `${item.displayName || key}: ${nextState ? 'Aktif yapıldı' : 'Devre dışı bırakıldı'}`,
          'success'
        );
      return true;
    } catch {
      // Revert on error
      set({ flags: current });
      useFeatureStore
        .getState()
        .setFlagLocal(key, item.isEnabled, item.mode, item.noticeMessage);
      useToastStore
        .getState()
        .showToast(`${item.displayName || key} durumu değiştirilemedi`, 'error');
      return false;
    }
  },

  updateFeatureFlag: async (key: string, updates: Partial<AdminFeatureFlagItem>) => {
    const current = get().flags;
    const item = current.find((f) => f.featureKey.toLowerCase() === key.toLowerCase());
    if (!item) return false;

    const merged = { ...item, ...updates, updatedAt: new Date().toISOString() };

    set({
      flags: current.map((f) =>
        f.featureKey.toLowerCase() === key.toLowerCase() ? merged : f
      ),
    });

    useFeatureStore
      .getState()
      .setFlagLocal(key, merged.isEnabled, merged.mode, merged.noticeMessage);

    try {
      const updated = await adminService.updateFeatureFlag(key, {
        isEnabled: merged.isEnabled,
        mode: merged.mode,
        noticeMessage: merged.noticeMessage,
      });

      set((state) => ({
        flags: state.flags.map((f) =>
          f.featureKey.toLowerCase() === key.toLowerCase() ? updated : f
        ),
      }));

      useToastStore
        .getState()
        .showToast(`${merged.displayName || key} ayarları kaydedildi`, 'success');
      return true;
    } catch {
      set({ flags: current });
      useFeatureStore
        .getState()
        .setFlagLocal(key, item.isEnabled, item.mode, item.noticeMessage);
      useToastStore
        .getState()
        .showToast(`${item.displayName || key} güncellenirken hata oluştu`, 'error');
      return false;
    }
  },

  bulkUpdateCategory: async (category: string, isEnabled: boolean) => {
    const current = get().flags;
    const targetKeys =
      category === 'all'
        ? current.map((f) => f.featureKey)
        : current
            .filter((f) => f.category.toLowerCase() === category.toLowerCase())
            .map((f) => f.featureKey);

    if (targetKeys.length === 0) return true;

    // Optimistic update
    set({
      flags: current.map((f) =>
        targetKeys.includes(f.featureKey)
          ? { ...f, isEnabled, updatedAt: new Date().toISOString() }
          : f
      ),
    });

    for (const k of targetKeys) {
      const it = current.find((c) => c.featureKey === k);
      if (it) {
        useFeatureStore.getState().setFlagLocal(k, isEnabled, it.mode, it.noticeMessage);
      }
    }

    try {
      await Promise.all(
        targetKeys.map((k) =>
          adminService.updateFeatureFlag(k, { isEnabled })
        )
      );
      useToastStore
        .getState()
        .showToast(
          `${targetKeys.length} adet özellik ${isEnabled ? 'etkinleştirildi' : 'bakım moduna alındı'}`,
          'success'
        );
      return true;
    } catch {
      set({ flags: current });
      useToastStore
        .getState()
        .showToast('Toplu işlem sırasında bazı bayraklar güncellenemedi', 'error');
      return false;
    }
  },

  // ── AI Routes Implementation ──────────────────────────────────────────────
  fetchAiRoutes: async () => {
    set({ loadingAiRoutes: true, aiRoutesError: null });
    try {
      const data = await adminService.getAiRoutes();
      set({ aiRoutes: data.routes || [], loadingAiRoutes: false });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'AI rotaları yüklenemedi';
      set({ loadingAiRoutes: false, aiRoutesError: msg });
    }
  },

  setAiSearch: (val: string) => set({ aiSearch: val }),
  setAiProviderFilter: (prov: string) => set({ aiProviderFilter: prov }),
  setSelectedAiToolKey: (key: string | null) => set({ selectedAiToolKey: key }),

  updateAiRoute: async (
    toolKey: string,
    updates: Partial<AdminAiToolRouteItem>
  ) => {
    const current = get().aiRoutes;
    const item = current.find((r) => r.toolKey === toolKey);
    if (!item) return false;

    const merged = { ...item, ...updates, updatedAt: new Date().toISOString() };

    set({
      aiRoutes: current.map((r) => (r.toolKey === toolKey ? merged : r)),
    });

    try {
      const updated = await adminService.updateAiRoute(toolKey, {
        provider: merged.provider,
        modelName: merged.modelName,
        temperature: merged.temperature,
        fallbackProvider: merged.fallbackProvider,
      });

      set((state) => ({
        aiRoutes: state.aiRoutes.map((r) =>
          r.toolKey === toolKey ? updated : r
        ),
      }));

      useToastStore
        .getState()
        .showToast(`${merged.displayName || toolKey} AI rotası güncellendi`, 'success');
      return true;
    } catch {
      set({ aiRoutes: current });
      useToastStore
        .getState()
        .showToast(`${item.displayName || toolKey} kaydedilemedi`, 'error');
      return false;
    }
  },

  bulkSetGlobalProvider: async (provider: string) => {
    const current = get().aiRoutes;
    const defaultModel =
      provider.toLowerCase() === 'groq'
        ? 'qwen-2.5-coder-32b'
        : provider.toLowerCase() === 'deepseek'
        ? 'deepseek-chat'
        : 'claude-3-5-sonnet-20241022';

    set({
      aiRoutes: current.map((r) => ({
        ...r,
        provider,
        modelName: defaultModel,
        updatedAt: new Date().toISOString(),
      })),
    });

    try {
      await Promise.all(
        current.map((r) =>
          adminService.updateAiRoute(r.toolKey, {
            provider,
            modelName: defaultModel,
            temperature: r.temperature ?? 0.2,
          })
        )
      );
      useToastStore
        .getState()
        .showToast(
          `Tüm AI araçları ${provider.toUpperCase()} (${defaultModel}) sağlayıcısına aktarıldı`,
          'success'
        );
      return true;
    } catch {
      set({ aiRoutes: current });
      useToastStore
        .getState()
        .showToast('Toplu sağlayıcı değişimi sırasında hata oluştu', 'error');
      return false;
    }
  },

  // ── System Diagnostics Implementation ─────────────────────────────────────
  setSelectedSystemComponent: (comp: string | null) =>
    set({ selectedSystemComponent: comp }),
}));
