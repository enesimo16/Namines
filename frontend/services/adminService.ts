import axios from 'axios';
import { API_BASE_URL } from '../lib/apiConfig';
import { csrfHeaders } from '../lib/csrf';

export interface AdminOverviewStats {
  totalUsers: number;
  totalProjects: number;
  totalFeedbacks: number;
  activeDatabases: number;
  totalQuotaUsedToday: number;
  plans: {
    free: number;
    pro: number;
    team: number;
    dev: number;
  };
}

export interface AdminUserItem {
  id: string;
  userName: string;
  email: string;
  companyName?: string;
  createdAt: string;
  isDev: boolean;
  planCode: 'free' | 'pro' | 'team' | 'dev';
  subscriptionStatus?: string;
  currentPeriodEnd?: string;
  projectsCount: number;
  dailyLimit: number;
  dailyUsageCount: number;
  lastResetDate: string;
}

export interface AdminUserListResponse {
  items: AdminUserItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

export interface AdminUserDetail extends AdminUserItem {
  projects: Array<{
    id: string;
    name: string;
    dbType: string;
  }>;
  feedbacks: Array<{
    id: number;
    category: string;
    message: string;
    createdAt: string;
  }>;
}

export interface AdminFeedbackItem {
  id: number;
  userId?: string;
  email?: string;
  category: string;
  message: string;
  createdAt: string;
}

export interface AdminFeedbackResponse {
  items: AdminFeedbackItem[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
}

const api = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
    ...csrfHeaders,
  },
  withCredentials: true,
});

api.interceptors.request.use((config) => {
  if (typeof window !== 'undefined') {
    const authRaw = localStorage.getItem('namines-auth');
    if (authRaw) {
      try {
        const parsed = JSON.parse(authRaw);
        const token = parsed?.state?.token;
        if (token) {
          config.headers.Authorization = `Bearer ${token}`;
        }
      } catch {
        // Ignore JSON parse errors
      }
    }
  }
  return config;
});

export const adminService = {
  getOverview: async (): Promise<AdminOverviewStats> => {
    const res = await api.get<AdminOverviewStats>('/admin/overview');
    return res.data;
  },

  getUsers: async (params?: { q?: string; plan?: string; page?: number; pageSize?: number }): Promise<AdminUserListResponse> => {
    const res = await api.get<AdminUserListResponse>('/admin/users', { params });
    return res.data;
  },

  getUserDetail: async (id: string): Promise<AdminUserDetail> => {
    const res = await api.get<AdminUserDetail>(`/admin/users/${id}`);
    return res.data;
  },

  updateUserPlan: async (id: string, planCode: string): Promise<{ id: string; planCode: string; isDev: boolean; dailyLimit: number; message: string }> => {
    const res = await api.patch(`/admin/users/${id}/plan`, { planCode });
    return res.data;
  },

  resetUserQuota: async (id: string, options: { resetToZero?: boolean; bonusPoints?: number } = { resetToZero: true }): Promise<{ dailyUsageCount: number; dailyLimit: number; remaining: number; message: string }> => {
    const res = await api.post(`/admin/users/${id}/reset-quota`, options);
    return res.data;
  },

  getFeedbacks: async (params?: { category?: string; page?: number; pageSize?: number }): Promise<AdminFeedbackResponse> => {
    const res = await api.get<AdminFeedbackResponse>('/admin/feedbacks', { params });
    return res.data;
  },

  deleteFeedback: async (id: number): Promise<{ message: string }> => {
    const res = await api.delete(`/admin/feedbacks/${id}`);
    return res.data;
  },
};
