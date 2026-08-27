import axios from 'axios';
import { DatabaseSchema } from '../types/schema';
import { SchemaDiffResult, MigrationResult } from '../types/migration';
import { ChangeRequestSummary, ChangeRequestDetail, ApprovalDecision, ChangeRequestStatus, AffectedCodeScanResult, ChangeRequestAuditEntry } from '../types/changeRequest';
import { GatewayListResult, GatewayRow } from '../types/gateway';
import { ProjectMember, OrgRole } from '../types/member';
import { GatewayKey, GatewayKeyCreated, GatewayTablePermission } from '../types/gatewayKey';
import { ClarifyResponse, NaiModelOption, SchemaPlan } from '../types/nai';
import { CodeExtractionResponse } from '../types/codeSchema';
import { TeamStatus, CreatedInvite, TeamProject } from '../types/team';
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
  /**
   * Netlestirici sorular. AI KULLANMIYOR, kotayi hic etkilemiyor ve kimlik
   * istemiyor -- kullanici sorulari gormeden tek token harcanmamali.
   */
  clarify: async (prompt: string): Promise<ClarifyResponse> => {
    const response = await api.post<ClarifyResponse>('/schema/clarify', { prompt });
    return response.data;
  },

  /**
   * Plan modu (second-phase/05-PLAN-MODU.md). Bu da bedava, /clarify gibi —
   * tablo listesi sunucuda cevaplardan kural tabanlı çıkıyor, AI'ya gidilmiyor.
   */
  plan: async (prompt: string, answers: Record<string, string>, round: number): Promise<SchemaPlan> => {
    const response = await api.post<SchemaPlan>('/schema/plan', { prompt, answers, round });
    return response.data;
  },

  /** Plana gore kullanilabilir Namines AI modelleri. */
  naiModels: async (): Promise<NaiModelOption[]> => {
    const response = await api.get<NaiModelOption[]>('/quota/models');
    return response.data;
  },

  /**
   * Akış (streaming) yolu ve eski tek-seferlik yol AYNI form alanlarını
   * kullanıyor; tekrarı önlemek için ayrı bir dosyaya taşındı, aşağıdaki
   * generateSchema de bunu çağırıyor.
   */
  buildGenerateFormData: (prompt: string, dbType: string, naiModel: string, image?: File | null, apiSpecUrl?: string, answers?: Record<string, string>): FormData => {
    const formData = new FormData();
    formData.append('Prompt', prompt);
    formData.append('DbType', dbType);
    // Saglayici artik sunucuda cozuluyor; 'Groq' burada yalnizca eski
    // sozlesmeyi karsilamak icin duruyor, model adi ('nai', 'nai-pro') ise
    // NaiCatalog tarafindan gercek ustteki modele eslenıyor.
    formData.append('AIProvider', 'Groq');
    formData.append('ModelName', naiModel);
    if (image) formData.append('Image', image);
    if (apiSpecUrl) formData.append('ApiSpecUrl', apiSpecUrl);
    // Cevaplanmayan sorular sunucuda VARSAYILANIYLA dolduruluyor, bos
    // gonderilmeleri isteği düşürmüyor.
    if (answers && Object.keys(answers).length > 0) formData.append('Answers', JSON.stringify(answers));
    return formData;
  },

  generateSchema: async (prompt: string, dbType: string, naiModel: string, image?: File | null, apiSpecUrl?: string, answers?: Record<string, string>): Promise<DatabaseSchema> => {
    const formData = schemaService.buildGenerateFormData(prompt, dbType, naiModel, image, apiSpecUrl, answers);
    const response = await api.post<DatabaseSchema>('/schema/generate', formData, {
      headers: { 'Content-Type': 'multipart/form-data' }
    });
    return response.data;
  },

  reviseSchema: async (selectedTables: any[], existingRelations: any[], prompt: string, naiModel: string): Promise<DatabaseSchema> => {
    const response = await api.post<DatabaseSchema>('/schema/revise', {
      revisionPrompt: prompt,
      selectedTables,
      existingRelations,
      aiProvider: 'Groq',
      modelName: naiModel
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
   * second-phase/13-DAGITIM-HEDEFLERI.md — Plesk/cPanel/mobil paketi (zip: SQL/.db
   * + README). dbType yalnızca MySQL/MariaDB (phpMyAdmin) veya SQLite (mobil) olabilir.
   */
  exportSharedHosting: async (schema: DatabaseSchema, dbType: string): Promise<Blob> => {
    const response = await api.post('/compile/shared-hosting', { schema, dbType }, { responseType: 'blob' });
    return response.data;
  },

  /**
   * second-phase/11-KODDAN-SEMA.md — Prisma/EF Core dosyalarından şema çıkarır.
   * `compareWith` verilirse ayrıca drift raporu döner ("kodun şunu diyor,
   * veritabanında şu var"). Bedava — ayrıştırıcılar deterministik, AI yok.
   */
  extractFromCode: async (
    files: Record<string, string>,
    compareWith?: DatabaseSchema,
    dbType?: string,
  ): Promise<CodeExtractionResponse> => {
    const response = await api.post('/codeschema/extract', { files, compareWith, dbType });
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

  /**
   * Eject hedefleri (12-CODEGEN-EJECT.md). Liste arka uçtan geliyor — istemcide
   * ikinci bir kopya tutmak, yeni bir hedef eklendiğinde UI'ın onu göstermemesi
   * demek olurdu.
   */
  ejectTargets: async (): Promise<{ target: string; name: string }[]> => {
    const response = await api.get('/compile/eject/targets');
    return response.data;
  },

  eject: async (
    target: string,
    schema: DatabaseSchema,
    dbType: string,
  ): Promise<{ files: Record<string, string>; warnings: string[] }> => {
    const response = await api.post(`/compile/eject/${target}`, { schema, dbType });
    return response.data;
  },

  ejectZip: async (target: string, schema: DatabaseSchema, dbType: string): Promise<Blob> => {
    const response = await api.post(
      `/compile/eject/${target}/zip`, { schema, dbType }, { responseType: 'blob' });
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

/**
 * Gateway API anahtarları ve tablo izinleri (08 §4.3).
 *
 * Anahtar yönetimi OTURUMLA korunur, API anahtarıyla değil: bir anahtarın kendi
 * yetkisini genişletebilmesi ya da yeni anahtar üretebilmesi, anahtar
 * sınırlamasının tamamını anlamsız kılardı.
 */
export const gatewayKeyService = {
  list: async (projectId: string): Promise<GatewayKey[]> => {
    const response = await api.get<GatewayKey[]>(`/gateway/keys/${projectId}`);
    return response.data;
  },

  create: async (
    projectId: string,
    name: string,
    canWrite: boolean,
    expiresAt: string | null,
    restrictions?: {
      allowedOrigins?: string | null;
      allowedIps?: string | null;
      rateLimitPerMinute?: number | null;
    },
  ): Promise<GatewayKeyCreated> => {
    const response = await api.post(`/gateway/keys/${projectId}`, {
      name, canWrite, expiresAt, ...restrictions,
    });
    return response.data;
  },

  revoke: async (projectId: string, keyId: string): Promise<void> => {
    await api.delete(`/gateway/keys/${projectId}/${keyId}`);
  },

  listTables: async (projectId: string): Promise<GatewayTablePermission[]> => {
    const response = await api.get<GatewayTablePermission[]>(`/gateway/keys/${projectId}/tables`);
    return response.data;
  },

  setTable: async (
    projectId: string,
    tableName: string,
    canRead: boolean,
    canWrite: boolean,
  ): Promise<void> => {
    await api.put(`/gateway/keys/${projectId}/tables`, { tableName, canRead, canWrite });
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

  /**
   * second-phase/10-COKLU-DB.md — iki proje arasındaki mantıksal (gerçek FK
   * OLMAYAN) ilişkiler. Hepsi backend'de yetki kontrolünden geçiyor (bkz.
   * CrossDatabaseController) — iki tarafı da görebilen kullanıcı.
   */
  crossDatabase: {
    listRelations: async (projectId: string): Promise<{
      id: string; direction: 'outgoing' | 'incoming'; localColumn: string;
      otherProjectId: string; otherProjectName: string; otherColumn: string;
      note: string | null; createdAt: string;
    }[]> => {
      const response = await api.get('/crossdatabase/relations', { params: { projectId } });
      return response.data;
    },
    createRelation: async (body: {
      sourceProjectId: string; sourceTableId: string; sourceColumnId: string;
      targetProjectId: string; targetTableId: string; targetColumnId: string; note?: string;
    }): Promise<{ id: string }> => {
      const response = await api.post('/crossdatabase/relations', body);
      return response.data;
    },
    deleteRelation: async (id: string): Promise<void> => {
      await api.delete(`/crossdatabase/relations/${id}`);
    },
    impact: async (projectId: string, tableId: string, columnId?: string): Promise<{
      relationId: string; direction: 'outgoing' | 'incoming';
      otherProjectId: string; otherProjectName: string; note: string | null;
    }[]> => {
      const response = await api.get('/crossdatabase/impact', { params: { projectId, tableId, columnId } });
      return response.data;
    },
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

  createCheckoutSession: async (plan: 'pro' | 'team' = 'pro'): Promise<{ url?: string; redirect?: string }> => {
    const response = await api.post(`/subscription/checkout?plan=${plan}`);
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

/**
 * Team planı: ekip, davet bağlantıları ve ortak etkinlik (backend: TeamController).
 */
export const teamService = {
  status: async (): Promise<TeamStatus> => {
    const response = await api.get<TeamStatus>('/team');
    return response.data;
  },

  /**
   * Tek kullanımlık davet bağlantısı üretir.
   *
   * Dönen `token` YALNIZCA burada bir kez görünüyor — sunucuda özeti saklanıyor,
   * tekrar gösterilemez. Kullanıcıya hemen kopyalatmak gerekiyor.
   */
  createInvite: async (role = 'Editor', expiresInDays = 7): Promise<CreatedInvite> => {
    const response = await api.post<CreatedInvite>('/team/invites', { role, expiresInDays });
    return response.data;
  },

  revokeInvite: async (inviteId: string): Promise<void> => {
    await api.delete(`/team/invites/${inviteId}`);
  },

  previewInvite: async (token: string): Promise<{ organization: string; role: string; expiresAt: string }> => {
    const response = await api.get(`/team/invites/${encodeURIComponent(token)}/preview`);
    return response.data;
  },

  acceptInvite: async (token: string): Promise<{ joined: string; role: string }> => {
    const response = await api.post(`/team/invites/${encodeURIComponent(token)}/accept`);
    return response.data;
  },

  activity: async (): Promise<{ projects: TeamProject[] }> => {
    const response = await api.get<{ projects: TeamProject[] }>('/team/activity');
    return response.data;
  },
};

export default api;

