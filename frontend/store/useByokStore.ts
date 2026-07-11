import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { encryptSecret, decryptSecret } from '../lib/byokCrypto';

interface ByokState {
  apiKey: string | null;
  provider: 'groq' | 'openai' | 'anthropic' | 'gemini';
  setApiKey: (key: string | null) => void;
  setProvider: (provider: 'groq' | 'openai' | 'anthropic' | 'gemini') => void;
  clearApiKey: () => void;
}

export const useByokStore = create<ByokState>()(
  persist(
    (set) => ({
      apiKey: null,
      provider: 'groq',
      setApiKey: (key) => set({ apiKey: key }),
      setProvider: (provider) => set({ provider }),
      clearApiKey: () => set({ apiKey: null }),
    }),
    {
      name: 'namines-byok',
      // AES-256-GCM ile at-rest şifreleme (anahtar IndexedDB'de non-extractable).
      storage: {
        getItem: async (name) => {
          const value = localStorage.getItem(name);
          if (!value) return null;
          try {
            const parsed = JSON.parse(value);
            if (parsed.state && parsed.state.apiKey) {
              parsed.state.apiKey = await decryptSecret(parsed.state.apiKey);
            }
            return parsed;
          } catch {
            return null;
          }
        },
        setItem: async (name, value) => {
          try {
            const stateClone = JSON.parse(JSON.stringify(value));
            if (stateClone.state && stateClone.state.apiKey) {
              stateClone.state.apiKey = await encryptSecret(stateClone.state.apiKey);
            }
            localStorage.setItem(name, JSON.stringify(stateClone));
          } catch (e) {
            console.error('Failed to serialize and encrypt BYOK store', e);
          }
        },
        removeItem: (name) => localStorage.removeItem(name),
      }
    }
  )
);
