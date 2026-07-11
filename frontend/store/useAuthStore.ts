import { create } from 'zustand';
import { persist } from 'zustand/middleware';

import { useQuotaStore } from './useQuotaStore';
import { useMultiplayerStore } from './useMultiplayerStore';
import { useProjectHistoryStore } from './useProjectHistoryStore';
import { useSchemaStore } from './useSchemaStore';

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
          useQuotaStore.getState().fetchQuota().catch(() => {});
        }
      },
      logout: () => {
        // Sunucudaki httpOnly cookie'yi de temizle.
        const base = (typeof process !== 'undefined' && process.env.NEXT_PUBLIC_API_URL)
          ? `${process.env.NEXT_PUBLIC_API_URL}/api`
          : 'http://localhost:5000/api';
        fetch(`${base}/auth/logout`, { method: 'POST', credentials: 'include' }).catch(() => {});
        set({ token: null, user: null, isAuthenticated: false });
        useQuotaStore.getState().reset();

        // Gizlilik (özellikle paylaşılan makine): oturum kapanınca oda/live-share
        // durumunu, yerel projeleri ve tuvali temizle; URL'deki roomId'yi kaldır.
        try {
          useMultiplayerStore.setState({
            roomId: null, userName: null, isConnected: false, isOffline: false, cursors: {},
          });
          useProjectHistoryStore.setState({ projects: [], activeProjectId: null });
          useSchemaStore.getState().resetProject();
          if (typeof window !== 'undefined') {
            localStorage.removeItem('namines_username');
            const url = new URL(window.location.href);
            if (url.searchParams.has('roomId')) {
              url.searchParams.delete('roomId');
              window.history.replaceState({}, '', url.toString());
            }
          }
        } catch (e) {
          console.error('Logout cleanup error', e);
        }
      },
    }),
    {
      name: 'namines-auth', // localStorage key
      // Güvenlik: JWT'yi localStorage'a YAZMA (XSS ile çalınmasın). Auth cookie üzerinden taşınır.
      // Yalnızca UI durumu (kullanıcı profili + oturum bayrağı) kalıcı yapılır.
      partialize: (state) => ({ user: state.user, isAuthenticated: state.isAuthenticated }),
    }
  )
);
