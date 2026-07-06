import { create } from 'zustand';
import api from '../services/api';

export interface AIPolicy {
  smartSeed: number;        // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
  documentation: number;    // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
  scaffolding: number;      // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
  schemaGeneration: number; // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
  schemaRevision: number;   // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
  dbaAnalysis: number;      // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
  migration: number;        // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
  voice: number;            // 0: Deterministic, 1: Quota/Medium, 2: High, 3: BYOK
}

interface AIPolicyState {
  policy: AIPolicy;
  isLoading: boolean;
  fetchPolicy: () => Promise<void>;
  updatePolicy: (newPolicy: AIPolicy) => Promise<void>;
  setLocalPolicy: (newPolicy: AIPolicy) => void;
}

const defaultPolicy: AIPolicy = {
  smartSeed: 6,
  documentation: 6,
  scaffolding: 6,
  schemaGeneration: 6,
  schemaRevision: 6,
  dbaAnalysis: 6,
  migration: 6,
  voice: 6
};

export const useAIPolicyStore = create<AIPolicyState>((set) => ({
  policy: defaultPolicy,
  isLoading: false,
  fetchPolicy: async () => {
    set({ isLoading: true });
    try {
      const response = await api.get<AIPolicy>('/user/policy');
      set({ policy: response.data });
    } catch (e) {
      console.error("Failed to fetch AI Policy from server, using local defaults", e);
    } finally {
      set({ isLoading: false });
    }
  },
  updatePolicy: async (newPolicy: AIPolicy) => {
    set({ isLoading: true });
    try {
      await api.put('/user/policy', newPolicy);
      set({ policy: newPolicy });
    } catch (e) {
      console.error("Failed to update AI Policy on server", e);
      throw e;
    } finally {
      set({ isLoading: false });
    }
  },
  setLocalPolicy: (newPolicy: AIPolicy) => set({ policy: newPolicy })
}));
