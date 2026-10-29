import api from './api';
import { API_BASE_URL } from '../lib/apiConfig';
import type {
  HubExploreResponse,
  HubRepoDetail,
  HubIssuesResponse,
  HubIssue,
  HubIssueComment,
  ProjectVisibility,
} from '../types/hub';

export interface ExploreParams {
  q?: string;
  category?: string;
  dbType?: string;
  sort?: 'popular' | 'recent';
  page?: number;
  pageSize?: number;
}

export interface UpdateSettingsPayload {
  name?: string;
  slug?: string;
  description?: string;
  visibility?: ProjectVisibility;
  category?: string;
  tags?: string;
  linkedGithubRepo?: string;
  autoApproveSafeChanges?: boolean;
  dbType?: string;
}

export interface CreateIssuePayload {
  title: string;
  body?: string;
  relatedTableName?: string;
  nslRuleCode?: string;
  labels?: string;
}

function getLocalIssues(projectId: string): HubIssue[] {
  if (typeof window === 'undefined') return [];
  try {
    const raw = localStorage.getItem(`namines:hub:issues:${projectId}`);
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

function saveLocalIssues(projectId: string, issues: HubIssue[]) {
  if (typeof window === 'undefined') return;
  try {
    localStorage.setItem(`namines:hub:issues:${projectId}`, JSON.stringify(issues));
  } catch {
    // ignore
  }
}

/**
 * Sunucuya ulaşılamadığında yerel taklit YALNIZCA Hub'da karşılığı olmayan yerel taslak repolar için
 * geçerli (`repo.isLocalDraft`). Gerçek bir repoda hata yutulup localStorage'a sahte başarı yazmak,
 * kullanıcıya olmayan bir issue'yu "oluştu" göstermek olurdu; orada hata çağırana fırlatılır.
 */
export interface LocalFallbackOptions {
  localDraft?: boolean;
}

export const hubService = {
  explore: async (params?: ExploreParams): Promise<HubExploreResponse> => {
    const res = await api.get<HubExploreResponse>('/hub/explore', { params });
    return res.data;
  },

  getRepo: async (owner: string, slugOrId: string): Promise<HubRepoDetail> => {
    const res = await api.get<HubRepoDetail>(
      `/hub/repos/${encodeURIComponent(owner)}/${encodeURIComponent(slugOrId)}`
    );
    return res.data;
  },

  updateSettings: async (
    projectId: string,
    data: UpdateSettingsPayload
  ): Promise<Partial<HubRepoDetail>> => {
    const res = await api.patch<Partial<HubRepoDetail>>(
      `/hub/repos/${encodeURIComponent(projectId)}/settings`,
      data
    );
    return res.data;
  },

  deleteRepo: async (projectId: string): Promise<void> => {
    await api.delete(`/hub/repos/${encodeURIComponent(projectId)}`);
  },

  toggleStar: async (projectId: string): Promise<{ isStarred: boolean; starCount: number }> => {
    const res = await api.post<{ isStarred: boolean; starCount: number }>(
      `/hub/repos/${encodeURIComponent(projectId)}/star`
    );
    return res.data;
  },

  fork: async (
    projectId: string,
    name?: string
  ): Promise<{
    id: string;
    name: string;
    slug: string;
    forkedFromProjectId: string;
    visibility: ProjectVisibility;
    createdAt: string;
    owner?: { id?: string; username?: string };
  }> => {
    const res = await api.post(
      `/hub/repos/${encodeURIComponent(projectId)}/fork`,
      name ? { name } : {}
    );
    return res.data;
  },

  listIssues: async (
    projectId: string,
    params?: {
      status?: 'open' | 'closed' | 'all';
      relatedTable?: string;
      label?: string;
      page?: number;
      pageSize?: number;
    },
    opts?: LocalFallbackOptions
  ): Promise<HubIssuesResponse> => {
    try {
      const res = await api.get<HubIssuesResponse>(
        `/hub/repos/${encodeURIComponent(projectId)}/issues`,
        { params }
      );
      return res.data;
    } catch (err) {
      if (!opts?.localDraft) throw err;
      const all = getLocalIssues(projectId);
      const filtered = all.filter((iss) => {
        if (params?.status === 'open' && iss.isClosed) return false;
        if (params?.status === 'closed' && !iss.isClosed) return false;
        if (params?.relatedTable && iss.relatedTableName !== params.relatedTable) return false;
        return true;
      });
      const openCount = all.filter((i) => !i.isClosed).length;
      const closedCount = all.filter((i) => i.isClosed).length;
      return {
        total: filtered.length,
        openCount,
        closedCount,
        page: params?.page || 1,
        pageSize: params?.pageSize || 25,
        items: filtered,
      };
    }
  },

  createIssue: async (
    projectId: string,
    payload: CreateIssuePayload,
    opts?: LocalFallbackOptions
  ): Promise<HubIssue> => {
    try {
      const res = await api.post<HubIssue>(
        `/hub/repos/${encodeURIComponent(projectId)}/issues`,
        payload
      );
      return res.data;
    } catch (err) {
      if (!opts?.localDraft) throw err;
      const all = getLocalIssues(projectId);
      const newIssue: HubIssue = {
        id: 'issue-' + Date.now().toString(36),
        issueNumber: all.length + 1,
        title: payload.title,
        body: payload.body || '',
        isClosed: false,
        relatedTableName: payload.relatedTableName || null,
        nslRuleCode: payload.nslRuleCode || null,
        labels: payload.labels || null,
        createdAt: new Date().toISOString(),
        closedAt: null,
        createdBy: {
          id: 'me',
          username: 'me',
        },
        commentsCount: 0,
      };
      saveLocalIssues(projectId, [newIssue, ...all]);
      return newIssue;
    }
  },

  updateIssueState: async (
    issueId: string,
    isClosed: boolean,
    opts?: LocalFallbackOptions
  ): Promise<{ id: string; issueNumber: number; isClosed: boolean; closedAt: string | null }> => {
    try {
      const res = await api.patch(
        `/hub/issues/${encodeURIComponent(issueId)}/state`,
        { isClosed }
      );
      return res.data;
    } catch (err) {
      if (!opts?.localDraft) throw err;
      if (typeof window !== 'undefined') {
        for (let i = 0; i < localStorage.length; i++) {
          const key = localStorage.key(i);
          if (key && key.startsWith('namines:hub:issues:')) {
            try {
              const issues: HubIssue[] = JSON.parse(localStorage.getItem(key) || '[]');
              const target = issues.find((x) => x.id === issueId);
              if (target) {
                target.isClosed = isClosed;
                target.closedAt = isClosed ? new Date().toISOString() : null;
                localStorage.setItem(key, JSON.stringify(issues));
                return {
                  id: target.id,
                  issueNumber: target.issueNumber,
                  isClosed: target.isClosed,
                  closedAt: target.closedAt,
                };
              }
            } catch {
              // ignore
            }
          }
        }
      }
      throw err;
    }
  },

  listComments: async (issueId: string): Promise<HubIssueComment[]> => {
    const res = await api.get<HubIssueComment[]>(
      `/hub/issues/${encodeURIComponent(issueId)}/comments`
    );
    return res.data;
  },

  createComment: async (issueId: string, body: string): Promise<HubIssueComment> => {
    const res = await api.post<HubIssueComment>(
      `/hub/issues/${encodeURIComponent(issueId)}/comments`,
      { body }
    );
    return res.data;
  },

  getBadgeUrl: (owner: string, slug: string): string => {
    return `${API_BASE_URL}/hub/badge/${encodeURIComponent(owner)}/${encodeURIComponent(slug)}.svg`;
  },
};
