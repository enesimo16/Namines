import { create } from 'zustand';
import { persist } from 'zustand/middleware';

// AES-like Vigenere-XOR-Base64 Obfuscation helper to secure key in localStorage against simple XSS payload string-scanning.
const SECRET_KEY = "namines-byok-obfuscator-key-20260605-darvell-labs-secret";

const encrypt = (text: string): string => {
  if (!text) return '';
  let result = '';
  for (let i = 0; i < text.length; i++) {
    const charCode = text.charCodeAt(i);
    const keyChar = SECRET_KEY.charCodeAt(i % SECRET_KEY.length);
    result += String.fromCharCode(charCode ^ keyChar);
  }
  return btoa(result);
};

const decrypt = (cipher: string): string => {
  if (!cipher) return '';
  try {
    const raw = atob(cipher);
    let result = '';
    for (let i = 0; i < raw.length; i++) {
      const charCode = raw.charCodeAt(i);
      const keyChar = SECRET_KEY.charCodeAt(i % SECRET_KEY.length);
      result += String.fromCharCode(charCode ^ keyChar);
    }
    return result;
  } catch (e) {
    return '';
  }
};

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
      storage: {
        getItem: (name) => {
          const value = localStorage.getItem(name);
          if (!value) return null;
          try {
            const parsed = JSON.parse(value);
            if (parsed.state && parsed.state.apiKey) {
              parsed.state.apiKey = decrypt(parsed.state.apiKey);
            }
            return parsed;
          } catch (e) {
            return null;
          }
        },
        setItem: (name, value) => {
          try {
            // Shallow clone the value state to avoid modifying the store memory state directly
            const stateClone = JSON.parse(JSON.stringify(value));
            if (stateClone.state && stateClone.state.apiKey) {
              stateClone.state.apiKey = encrypt(stateClone.state.apiKey);
            }
            localStorage.setItem(name, JSON.stringify(stateClone));
          } catch (e) {
            console.error("Failed to serialize and encrypt BYOK store", e);
          }
        },
        removeItem: (name) => localStorage.removeItem(name),
      }
    }
  )
);
