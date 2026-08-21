import axios from 'axios';
import { DatabaseSchema } from '../types/schema';
import { SchemaDiffResult, MigrationResult } from '../types/migration';
import { ChangeRequestSummary, ChangeRequestDetail, ApprovalDecision, ChangeRequestStatus, AffectedCodeScanResult, ChangeRequestAuditEntry } from '../types/changeRequest';
import { GatewayListResult, GatewayRow } from '../types/gateway';
import { ProjectMember, OrgRole } from '../types/member';
import { useAuthStore } from '../store/useAuthStore';
import { useQuotaStore } from '../store/useQuotaStore';
import { useSchemaStore } from '../store/useSchemaStore';
import { useByokStore } from '../store/useByokStore';
import { useToastStore } from '../store/useToastStore';
import { API_BASE_URL } from '../lib/apiConfig';

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  // httpOnly auth cookie'sinin isteklerle gönderilmesi için (cookie tabanlı JWT).
  withCredentials: true,
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

  /**
   * Prisma şeması. Önizleme arka uçtan çekilir, istemcide YENİDEN ÜRETİLMEZ:
   * üretici 21 testle (4'ü gerçek `prisma validate`) doğrulanmış tek kopyadır ve
   * `warnings` ancak oradan gelir. İstemcide ikinci bir üretici, gösterilen şema
   * ile indirilen şemanın sessizce ayrışmasına yol açardı.
   */
  compilePrisma: async (
    schema: DatabaseSchema,
    dbType: string,
  ): Promise<{ schema: string; env: string; warnings: string[] }> => {
    const response = await api.post('/compile/prisma', { schema, dbType });
    return response.data;
  },

  compilePrismaZip: async (schema: DatabaseSchema, dbType: string): Promise<Blob> => {
    const response = await api.post('/compile/prisma/zip', { schema, dbType }, { responseType: 'blob' });
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

  calculateDiff: async (oldSchema: DatabaseSchema, newSchema: DatabaseSchema, dbType?: string): Promise<SchemaDiffResult> => {
    const response = await api.post<SchemaDiffResult>('/migration/diff', { oldSchema, newSchema, dbType });
    return response.data;
  },

  generateMigration: async (oldSchema: DatabaseSchema, newSchema: DatabaseSchema, dbType: string): Promise<MigrationResult> => {
    const response = await api.post<MigrationResult>('/migration/generate', { oldSchema, newSchema, dbType });
    return response.data;
  }
};

export const changeRequestService = {
  createQuick: async (projectId: string, schema: DatabaseSchema, title?: string, message?: string): Promise<{ id: string }> => {
    const response = await api.post<{ id: string }>('/changerequest/quick', {
      projectId,
      schemaJson: JSON.stringify(schema),
      title,
      message,
    });
    return response.data;
  },

  listForProject: async (projectId: string): Promise<ChangeRequestSummary[]> => {
    const response = await api.get<ChangeRequestSummary[]>(`/changerequest/project/${projectId}`);
    return response.data;
  },

  getDetail: async (id: string): Promise<ChangeRequestDetail> => {
    const response = await api.get<ChangeRequestDetail>(`/changerequest/${id}`);
    return response.data;
  },

  decide: async (id: string, decision: ApprovalDecision, comment?: string): Promise<{ id: string; status: ChangeRequestStatus }> => {
    const response = await api.post<{ id: string; status: ChangeRequestStatus }>(`/changerequest/${id}/decide`, { decision, comment });
    return response.data;
  },

  runTests: async (id: string): Promise<{ supported: boolean; success: boolean; engineMessage: string | null; failedStatement: string | null; durationMs: number }> => {
    // Container başlatma dahil senkron çalışır — 5-20sn sürebilir (bkz. backend yorumu).
    const response = await api.post(`/changerequest/${id}/run-tests`, {}, { timeout: 90000 });
    return response.data;
  },

  scanAffectedCode: async (id: string, files: { fileName: string; content: string }[]): Promise<AffectedCodeScanResult> => {
    const response = await api.post<AffectedCodeScanResult>(`/changerequest/${id}/scan-affected-code`, { files });
    return response.data;
  },

  // G16 — Safe risk'li değişikliklerin insan onayı beklemeden otomatik onaylanması.
  setAutoApproveSafe: async (projectId: string, enabled: boolean): Promise<{ id: string; autoApproveSafeChanges: boolean }> => {
    const response = await api.put(`/changerequest/project/${projectId}/auto-approve-safe`, { enabled });
    return response.data;
  },

  getAuditLog: async (id: string): Promise<ChangeRequestAuditEntry[]> => {
    const response = await api.get(`/changerequest/${id}/audit`);
    return response.data;
  }
};

// G18 — proje ekibi (organizasyon üyeliği, 05 §6).
export const memberService = {
  list: async (projectId: string): Promise<ProjectMember[]> => {
    const response = await api.get<ProjectMember[]>(`/project/${projectId}/members`);
    return response.data;
  },

  add: async (projectId: string, email: string, role: OrgRole): Promise<ProjectMember> => {
    const response = await api.post(`/project/${projectId}/members`, { email, role });
    return response.data;
  },

  changeRole: async (projectId: string, memberUserId: string, role: OrgRole): Promise<void> => {
    await api.put(`/project/${projectId}/members/${memberUserId}`, { role });
  },

  remove: async (projectId: string, memberUserId: string): Promise<void> => {
    await api.delete(`/project/${projectId}/members/${memberUserId}`);
  },
};

// G17 — CanvasHub'ın roomId'sini sunucu-otoriteli branch_id'ye bağlamak için.
export const branchService = {
  getOrCreateDefault: async (projectId: string): Promise<{ id: string; projectId: string; name: string }> => {
    const response = await api.get(`/branch/project/${projectId}/default`);
    return response.data;
  }
};

// G14 — Minimal Gateway: şemadan otomatik salt-okunur REST (liste + detay).
export const gatewayService = {
  list: async (
    connectionString: string, dbType: string, tableName: string,
    page: number, pageSize: number,
    orderByColumn?: string | null, includeTotalCount: boolean = true,
  ): Promise<GatewayListResult> => {
    const response = await api.post<GatewayListResult>('/gateway/list', {
      connectionString, dbType, tableName, page, pageSize,
      orderByColumn: orderByColumn ?? null, includeTotalCount,
    });
    return response.data;
  },

  detail: async (connectionString: string, dbType: string, tableName: string, pkColumn: string, pkValue: string): Promise<GatewayRow> => {
    const response = await api.post<GatewayRow>('/gateway/detail', { connectionString, dbType, tableName, pkColumn, pkValue });
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

  createShareLink: async (projectId: string): Promise<{ token: string }> => {
    const response = await api.post(`/share/${projectId}`);
    return response.data;
  },

  revokeShareLink: async (projectId: string): Promise<void> => {
    await api.delete(`/share/${projectId}`);
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

      // Inject BYOK Headers — anahtar bellekteki store'dan (zaten çözülmüş) okunur;
      // localStorage'da AES-256-GCM ciphertext durur, burada tekrar çözme yapılmaz.
      try {
        const { apiKey, provider } = useByokStore.getState();
        if (apiKey) {
          config.headers['X-BYOK-Key'] = apiKey;
          config.headers['X-BYOK-Provider'] = provider || 'groq';
        }
      } catch (e) {
        console.error('BYOK header injection error', e);
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

      // Token bitti → backend minimum AI'ya düştü. Kullanıcıya sağ altta bildir (dedupe'lu).
      if (response.headers?.['x-ai-fallback'] === 'quota-exhausted') {
        useToastStore.getState().showToast(
          'Tokens exhausted — continuing with minimum AI. All free features remain active.',
          'warning'
        );
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

