import axios from 'axios';
import { DatabaseSchema } from '../types/schema';
import { SchemaDiffResult, MigrationResult } from '../types/migration';

const api = axios.create({
  baseURL: 'http://localhost:5000/api',
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

  generateReadme: async (schema: DatabaseSchema): Promise<string> => {
    const response = await api.post('/documentation/readme', schema);
    return response.data.readme;
  },

  generateMermaid: async (schema: DatabaseSchema): Promise<string> => {
    const response = await api.post('/documentation/mermaid', schema);
    return response.data.mermaid;
  }
};

export const coderAIService = {
  generate: async (schema: DatabaseSchema, dbType: string): Promise<string> => {
    const response = await api.post('/coderai/generate', { schema, dbType });
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
  generate: async (schema: DatabaseSchema, dbType: string, domainHint?: string, rowCount: number = 50): Promise<any> => {
    const response = await api.post('/smartseed/generate', { schema, dbType, domainHint, rowCount });
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
  }
};

// Request interceptor to dynamically inject the JWT bearer token from Zustand auth store
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
    }
    return config;
  },
  (error) => {
    return Promise.reject(error);
  }
);

export default api;

