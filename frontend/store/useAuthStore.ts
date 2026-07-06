import { create } from 'zustand';
import { persist } from 'zustand/middleware';

import { useQuotaStore } from './useQuotaStore';

export interface UserProfile {
  username: string;
  email: string;
  type: 'individual' | 'corporate';
  companyName?: string;
}

interface AuthState {
  token: string | null;
  user: UserProfile | null;
  isAuthenticated: boolean;
  setAuth: (token: string, user: UserProfile, quota?: any) => void;
  logout: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      token: null,
      user: null,
      isAuthenticated: false,
      setAuth: (token, user, quota) => {
        set({ token, user, isAuthenticated: true });
        if (quota) {
          useQuotaStore.setState({
            dailyLimit: quota.dailyLimit,
            used: quota.used,
            remaining: quota.remaining,
            resetAt: quota.resetAt,
            isLoaded: true
          });
        } else {
          useQuotaStore.getState().fetchQuota();
        }
      },
      logout: () => {
        set({ token: null, user: null, isAuthenticated: false });
        useQuotaStore.getState().reset();
      },
    }),
    {
      name: 'namines-auth', // localStorage key
    }
  )
);
