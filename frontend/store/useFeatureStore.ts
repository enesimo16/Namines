import { create } from 'zustand';
import { API_BASE_URL } from '../lib/apiConfig';

export interface PublicFeatureFlag {
  featureKey: string;
  displayName: string;
  description: string;
  category: string;
  isEnabled: boolean;
  mode: 'DisabledNotice' | 'Hidden';
  noticeMessage?: string;
}

export interface FeatureState {
  isEnabled: boolean;
  mode: 'DisabledNotice' | 'Hidden';
  noticeMessage: string;
}

interface FeatureStore {
  flags: Record<string, PublicFeatureFlag>;
  isLoading: boolean;
  error: string | null;
  hasLoaded: boolean;
  fetchFeatures: () => Promise<void>;
  isFeatureEnabled: (key: string) => boolean;
  getFeatureState: (key: string) => FeatureState;
  setFlagLocal: (key: string, isEnabled: boolean, mode: 'DisabledNotice' | 'Hidden', noticeMessage?: string) => void;
}

const DEFAULT_NOTICE = 'Bu özellik şu anda bakım çalışması nedeniyle geçici olarak devre dışıdır.';

export const useFeatureStore = create<FeatureStore>((set, get) => ({
  flags: {},
  isLoading: false,
  error: null,
  hasLoaded: false,

  fetchFeatures: async () => {
    // Periyodik yenileme ile elle tetiklemenin üst üste binmesini engelle.
    if (get().isLoading) return;
    set({ isLoading: true, error: null });
    try {
      const res = await fetch(`${API_BASE_URL}/features`, {
        headers: { Accept: 'application/json' },
      });
      if (!res.ok) {
        throw new Error(`Failed to load features: ${res.status}`);
      }
      const data: PublicFeatureFlag[] = await res.json();
      const flagMap: Record<string, PublicFeatureFlag> = {};
      for (const item of data) {
        flagMap[item.featureKey.toLowerCase()] = item;
      }
      set({ flags: flagMap, isLoading: false, hasLoaded: true });
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Unknown error';
      set({ isLoading: false, error: message });
    }
  },

  isFeatureEnabled: (key: string): boolean => {
    const flag = get().flags[key.toLowerCase()];
    if (!flag) return true; // Fail-open for undeclared flags
    return flag.isEnabled;
  },

  getFeatureState: (key: string): FeatureState => {
    const flag = get().flags[key.toLowerCase()];
    if (!flag) {
      return {
        isEnabled: true,
        mode: 'DisabledNotice',
        noticeMessage: DEFAULT_NOTICE,
      };
    }
    return {
      isEnabled: flag.isEnabled,
      mode: flag.mode || 'DisabledNotice',
      noticeMessage: flag.noticeMessage || DEFAULT_NOTICE,
    };
  },

  setFlagLocal: (key: string, isEnabled: boolean, mode: 'DisabledNotice' | 'Hidden', noticeMessage?: string) => {
    const current = get().flags;
    const lowerKey = key.toLowerCase();
    const existing = current[lowerKey];
    set({
      flags: {
        ...current,
        [lowerKey]: {
          featureKey: lowerKey,
          displayName: existing?.displayName || key,
          description: existing?.description || '',
          category: existing?.category || 'General',
          isEnabled,
          mode,
          noticeMessage: noticeMessage || existing?.noticeMessage || DEFAULT_NOTICE,
        },
      },
    });
  },
}));
