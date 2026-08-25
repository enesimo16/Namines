import { create } from 'zustand';
import api from '../services/api';

/**
 * Gelişmiş AI tercihleri (backend: AiAdvancedSettings).
 *
 * Bu ayarlar eskiden YALNIZCA localStorage'a yazılıyor ve hiçbir yerde
 * okunmuyordu — on bir ayarın tamamı süstü. Artık sunucuda saklanıyor ve
 * şema üretiminde gerçekten uygulanıyor.
 */
export interface AiAdvancedSettings {
  seedDomain: string;
  docLevel: string;
  scaffoldVersion: string;
  dbaSeverity: string;
  temperature: string;
  promptStyle: string;
  namingConvention: string;
  fkAction: string;
  maxTokens: string;
  autoIndex: string;
  sqlPrettyPrint: string;
}

/**
 * Özellik başına model tercihi.
 *
 * Sayılar eski AIMode değerleri; sunucu bunları üç NAI modeline indiriyor:
 * 1 → NAI v1 Flash, 2 → NAI v1, 4 → NAI v1 Pro.
 */
export interface AIPolicy {
  smartSeed: number;
  documentation: number;
  scaffolding: number;
  schemaGeneration: number;
  schemaRevision: number;
  dbaAnalysis: number;
  migration: number;
  voice: number;
  advanced?: AiAdvancedSettings;
}

interface AIPolicyState {
  policy: AIPolicy;
  isLoading: boolean;
  fetchPolicy: () => Promise<void>;
  updatePolicy: (newPolicy: AIPolicy) => Promise<void>;
  setLocalPolicy: (newPolicy: AIPolicy) => void;
}

export const defaultAdvanced: AiAdvancedSettings = {
  seedDomain: 'general',
  docLevel: 'standard',
  scaffoldVersion: '.net8',
  dbaSeverity: 'warning',
  temperature: '0.2',
  promptStyle: 'clean',
  namingConvention: 'snake_case',
  // Varsayılan RESTRICT, CASCADE değil: varsayılan asla veri kaybına doğru
  // düşmemeli. CASCADE varsayılan olsaydı, ayara hiç dokunmamış bir kullanıcı
  // bir satır silerken ilişkili tüm kayıtları da sessizce silerdi.
  fkAction: 'restrict',
  maxTokens: '4096',
  autoIndex: 'true',
  sqlPrettyPrint: 'true',
};

// Varsayılan FLASH. Önceden hepsi 6 (en pahalı karşılık) idi: hiçbir ayara
// dokunmamış bir kullanıcı en ucuz işi bile en pahalı modelde çalıştırıyor ve
// günlük bütçesini iki kat hızlı tüketiyordu. Şema üretimi tek istisna —
// kullanıcının ürünle ilk teması ve en kritik çıktı orası.
const defaultPolicy: AIPolicy = {
  smartSeed: 1,
  documentation: 1,
  scaffolding: 1,
  schemaGeneration: 2,
  schemaRevision: 1,
  dbaAnalysis: 1,
  migration: 1,
  voice: 1,
  advanced: defaultAdvanced,
};

export const useAIPolicyStore = create<AIPolicyState>((set) => ({
  policy: defaultPolicy,
  isLoading: false,
  fetchPolicy: async () => {
    set({ isLoading: true });
    try {
      const response = await api.get<AIPolicy>('/user/policy');
      set({
        policy: {
          ...response.data,
          // Sunucu eski bir kayıt döndürürse advanced boş gelebilir; forma boş
          // değer vermek, kullanıcının hangi ayarın geçerli olduğunu görmesini
          // engellerdi.
          advanced: response.data.advanced ?? defaultAdvanced,
        },
      });
    } catch (e) {
      console.error('Failed to fetch AI Policy from server, using local defaults', e);
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
      console.error('Failed to update AI Policy on server', e);
      throw e;
    } finally {
      set({ isLoading: false });
    }
  },
  setLocalPolicy: (newPolicy: AIPolicy) => set({ policy: newPolicy }),
}));
