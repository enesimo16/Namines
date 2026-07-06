import axios from 'axios';
import { DatabaseSchema } from '../types/schema';
import { SchemaDiffResult, MigrationResult } from '../types/migration';
import { useAuthStore } from '../store/useAuthStore';
import { useQuotaStore } from '../store/useQuotaStore';
import { useSchemaStore } from '../store/useSchemaStore';

const api = axios.create({
  baseURL: (typeof process !== 'undefined' && process.env.NEXT_PUBLIC_API_URL)
    ? `${process.env.NEXT_PUBLIC_API_URL}/api`
    : 'http://localhost:5000/api',
  headers: {
    'Content-Type': 'application/json',
  },
});

export const schemaService = {
  generateSchema: async (prompt: string, dbType: string, aiProvider: string, modelName: string, image?: File | null, referenceUrl?: string): Promise<DatabaseSchema> => {
    const formData = new FormData();
    formData.append('Prompt', prompt);
    formData.append('DbType', dbType);
    formData.append('AIProvider', aiProvider);
    formData.append('ModelName', modelName);
    if (image) formData.append('Image', image);
    if (referenceUrl) formData.append('ReferenceUrl', referenceUrl);

    const response = await api.post<DatabaseSchema>('/schema/generate', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    });
    return response.data;
  },

  reviseSchema: async (selectedTables: any[], existingRelations: any[], prompt: string, aiProvider: string, modelName: string): Promise<DatabaseSchema> => {
    const response = await api.post<DatabaseSchema>('/schema/revise', {
      revisionPrompt: prompt,
      selectedTables,
      existingRelations,
      aiProvider,
      modelName
    });
    return response.data;
  },

  lintSchema: async (schema: DatabaseSchema): Promise<any> => {
    const response = await api.post('/lint', schema);
    return response.data;
  },

  compileSql: async (schema: DatabaseSchema, dbType: string): Promise<string> => {
    const response = await api.post('/compile/sql', { schema, dbType });
    return response.data.sql;
  },

  compileEfCore: async (schema: DatabaseSchema, dbType: string): Promise<Blob> => {
    const response = await api.post('/compile/efcore', { schema, dbType }, { responseType: 'blob' });
    return response.data;
  },

  runDockerSandbox: async (schema: DatabaseSchema, dbType: string): Promise<string> => {
    const response = await api.post('/docker/run', { schema, dbType });
    return response.data.jobId;
  },

  transcribeVoice: async (file: File): Promise<string> => {
    const formData = new FormData();
    formData.append('audio', file);
    const response = await api.post('/voice/transcribe', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    });
    return response.data.text;
  },

  generateMockData: async (schema: DatabaseSchema): Promise<string> => {
    const response = await api.post('/schema/mockdata', schema);
    return response.data.sql;
  },

  generatePdf: async (schema: DatabaseSchema, projectName: string, language: string = 'tr'): Promise<Blob> => {
    const response = await api.post('/documentation/pdf', { schema, projectName, language }, { responseType: 'blob' });
    return response.data;
  },

  generateReadme: async (schema: DatabaseSchema, language: string = 'tr'): Promise<string> => {
    const response = await api.post(`/documentation/readme?language=${language}`, schema);
    return response.data.readme;
  },

  generateMermaid: async (schema: DatabaseSchema): Promise<string> => {
    const response = await api.post('/documentation/mermaid', schema);
    return response.data.mermaid;
  }
};

export const coderAIService = {
  generate: async (schema: DatabaseSchema, dbType: string, enhanceWithAI: boolean = false): Promise<string> => {
    const response = await api.post('/coderai/generate', { schema, dbType, enhanceWithAI });
    return response.data.jobId;
  },
  cleanup: async (jobId: string): Promise<void> => {
    await api.delete(`/coderai/sandbox/${jobId}`);
  },
  getStreamUrl: (jobId: string): string => {
    return `${api.defaults.baseURL}/coderai/stream/${jobId}`;
  }
};

export const aiDbaService = {
  analyze: async (schema: DatabaseSchema, dbType: string): Promise<any> => {
    const response = await api.post('/aidba/analyze', { schema, dbType });
    return response.data;
  }
};

export const smartSeedService = {
  generate: async (schema: DatabaseSchema, dbType: string, domainHint?: string, rowCount: number = 50, enhanceWithAI: boolean = false): Promise<any> => {
    const response = await api.post('/smartseed/generate', { schema, dbType, domainHint, rowCount, enhanceWithAI });
    return response.data;
  }
};

export const migrationService = {
  parseDbContext: async (dbContextCode: string, dbType: string): Promise<DatabaseSchema> => {
    const response = await api.post<DatabaseSchema>('/migration/parse', { dbContextCode, dbType });
    return response.data;
  },

  calculateDiff: async (oldSchema: DatabaseSchema, newSchema: DatabaseSchema): Promise<SchemaDiffResult> => {
    const response = await api.post<SchemaDiffResult>('/migration/diff', { oldSchema, newSchema });
    return response.data;
  },

  generateMigration: async (oldSchema: DatabaseSchema, newSchema: DatabaseSchema, dbType: string): Promise<MigrationResult> => {
    const response = await api.post<MigrationResult>('/migration/generate', { oldSchema, newSchema, dbType });
    return response.data;
  }
};

export const reverseEngineerService = {
  analyzeVisionImage: async (imageFile: File): Promise<DatabaseSchema> => {
    const formData = new FormData();
    formData.append('image', imageFile);

    const response = await api.post<DatabaseSchema>('/reverseengineer/analyze', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    });
    return response.data;
  }
};

export const scaffolderService = {
  exportProject: async (schema: DatabaseSchema): Promise<Blob> => {
    const response = await api.post('/scaffolder/export', schema, { responseType: 'blob' });
    return response.data;
  },
  exportPythonProject: async (schema: DatabaseSchema): Promise<Blob> => {
    const response = await api.post('/scaffolder/export/python', schema, { responseType: 'blob' });
    return response.data;
  }
};

export const authService = {
  register: async (email: string, password: string, username?: string, type?: string, companyName?: string): Promise<any> => {
    const response = await api.post('/auth/register', { email, password, username, type, companyName });
    return response.data;
  },

  login: async (email: string, password: string): Promise<any> => {
    const response = await api.post('/auth/login', { email, password });
    return response.data;
  },

  syncProjects: async (projects: any[]): Promise<any> => {
    const response = await api.post('/auth/sync', projects);
    return response.data;
  },

  getCloudProjects: async (): Promise<any[]> => {
    const response = await api.get('/auth/projects');
    return response.data;
  },

  getProfile: async (): Promise<any> => {
    const response = await api.get('/auth/profile');
    return response.data;
  },

  updateProfile: async (profile: {
    fullName?: string;
    companyName?: string;
    githubUrl?: string;
    linkedinUrl?: string;
    websiteUrl?: string;
    twitterUrl?: string;
    bio?: string;
    location?: string;
  }): Promise<any> => {
    const response = await api.put('/auth/profile', profile);
    return response.data;
  },

  getSubscriptionStatus: async (): Promise<any> => {
    const response = await api.get('/subscription/status');
    return response.data;
  },

  createCheckoutSession: async (): Promise<{ url?: string; redirect?: string }> => {
    const response = await api.post('/subscription/checkout');
    return response.data;
  },

  createBillingPortal: async (): Promise<{ url: string }> => {
    const response = await api.post('/subscription/portal');
    return response.data;
  }
};

// Request interceptor to dynamically inject the JWT bearer token from Zustand auth store and BYOK headers from useByokStore
api.interceptors.request.use(
  (config) => {
    if (typeof window !== 'undefined') {
      const naminesAuth = localStorage.getItem('namines-auth');
      if (naminesAuth) {
        try {
          const parsed = JSON.parse(naminesAuth);
          const token = parsed.state?.token;
          if (token) {
            config.headers.Authorization = `Bearer ${token}`;
          }
        } catch (e) {
          console.error('Auth token parsing error', e);
        }
      }

      // Inject BYOK Headers
      const naminesByok = localStorage.getItem('namines-byok');
      if (naminesByok) {
        try {
          const parsed = JSON.parse(naminesByok);
          const apiKey = parsed.state?.apiKey;
          const provider = parsed.state?.provider;
          if (apiKey) {
            // Decrypt the obfuscated key before attaching to headers (using same algorithm as store)
            const SECRET_KEY = "namines-byok-obfuscator-key-20260605-darvell-labs-secret";
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
            const decryptedKey = decrypt(apiKey);
            if (decryptedKey) {
              config.headers['X-BYOK-Key'] = decryptedKey;
              config.headers['X-BYOK-Provider'] = provider || 'groq';
            }
          }
        } catch (e) {
          console.error('BYOK parsing error', e);
        }
      }

      // Inject AI Provider Header (Ollama local / Gemini / Groq)
      try {
        const aiProvider = useSchemaStore.getState().aiProvider;
        if (aiProvider) {
          config.headers['X-AI-Provider'] = aiProvider;
        }
      } catch (e) {
        console.error('AI provider header injection error', e);
      }
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

// Response interceptor to globally catch 401 and 429 errors and update stores accordingly
api.interceptors.response.use(
  (response) => {
    if (typeof window !== 'undefined') {
      const url = response.config.url || "";
      const isAIEndpoint = [
        '/aidba', '/smartseed', '/schema/generate',
        '/schema/revise', '/schema/mockdata', '/coderai',
        '/migration/parse', '/migration/generate',
        '/documentation/pdf', '/reverseengineer', '/voice'
      ].some(prefix => url.includes(prefix));

      if (isAIEndpoint) {
        useQuotaStore.getState().fetchQuota().catch(() => {});
      }
    }
    return response;
  },
  (error) => {
    if (error.response) {
      if (error.response.status === 401) {
        if (typeof window !== 'undefined') {
          useAuthStore.getState().logout();
        }
      } else if (error.response.status === 429 && error.response.data?.code === 'QUOTA_EXCEEDED') {
        if (typeof window !== 'undefined') {
          useQuotaStore.getState().setExhaustedModalOpen(true);
        }
      }
    }
    return Promise.reject(error);
  }
);

export default api;

