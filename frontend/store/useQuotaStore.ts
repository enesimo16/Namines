import { create } from 'zustand';
import api from '../services/api';

export interface QuotaStatus {
  dailyLimit: number;
  used: number;
  remaining: number;
  resetAt: string | null;
}

interface QuotaState {
  dailyLimit: number;
  used: number;
  remaining: number;
  resetAt: string | null;
  isLoaded: boolean;
  isExhaustedModalOpen: boolean;
  
  fetchQuota: () => Promise<void>;
  decrementLocal: () => void;  // Optimistic UI update
  reset: () => void;
  setExhaustedModalOpen: (open: boolean) => void;
}

export const useQuotaStore = create<QuotaState>((set, get) => ({
  dailyLimit: 100,
  used: 0,
  remaining: 100,
  resetAt: null,
  isLoaded: false,
  isExhaustedModalOpen: false,

  fetchQuota: async () => {
    try {
      const response = await api.get<QuotaStatus>('/quota/status');
      set({
        dailyLimit: response.data.dailyLimit,
        used: response.data.used,
        remaining: response.data.remaining,
        resetAt: response.data.resetAt,
        isLoaded: true
      });
    } catch (e) {
      console.error("Failed to fetch quota from server", e);
    }
  },

  decrementLocal: () => {
    const current = get().remaining;
    if (current > 0) {
      set({
        remaining: Math.max(0, current - 5), // default decrement by 5%
        used: Math.min(get().dailyLimit, get().used + 5)
      });
    }
  },

  setExhaustedModalOpen: (open) => set({ isExhaustedModalOpen: open }),

  reset: () => {
    set({
      dailyLimit: 100,
      used: 0,
      remaining: 100,
      resetAt: null,
      isLoaded: false,
      isExhaustedModalOpen: false
    });
  }
}));
