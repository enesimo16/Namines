// @vitest-environment jsdom
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, cleanup, waitFor } from '@testing-library/react';
import AdminPage from './page';
import { useAuthStore } from '../../store/useAuthStore';
import { adminService } from '../../services/adminService';

// Mock Next router
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
    prefetch: vi.fn(),
  }),
}));

// Mock Link
vi.mock('next/link', () => ({
  default: ({ children, href, ...props }: any) => (
    <a href={href} {...props}>
      {children}
    </a>
  ),
}));

// Mock adminService
vi.mock('../../services/adminService', () => ({
  adminService: {
    getOverview: vi.fn().mockResolvedValue({
      totalUsers: 42,
      totalProjects: 120,
      totalFeedbacks: 5,
      activeDatabases: 12,
      totalQuotaUsedToday: 350,
      plans: { free: 30, pro: 8, team: 3, dev: 1 },
    }),
    getUsers: vi.fn().mockResolvedValue({
      items: [
        {
          id: 'user-1',
          userName: 'testadmin',
          email: 'admin@namines.com',
          isDev: true,
          planCode: 'dev',
          projectsCount: 10,
          dailyLimit: 999999,
          dailyUsageCount: 25,
          lastResetDate: '2026-10-18',
          createdAt: '2026-09-01T00:00:00Z',
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
      totalPages: 1,
    }),
    getUserDetail: vi.fn().mockResolvedValue({
      id: 'user-1',
      userName: 'testadmin',
      email: 'admin@namines.com',
      isDev: true,
      planCode: 'dev',
      projectsCount: 10,
      dailyLimit: 999999,
      dailyUsageCount: 25,
      lastResetDate: '2026-10-18',
      createdAt: '2026-09-01T00:00:00Z',
      projects: [{ id: 'p1', name: 'DemoDB', dbType: 'postgresql' }],
      feedbacks: [],
    }),
    getFeedbacks: vi.fn().mockResolvedValue({
      items: [
        {
          id: 101,
          email: 'user@example.com',
          category: 'idea',
          message: 'Can we have dark mode in desk?',
          createdAt: '2026-10-17T12:00:00Z',
        },
      ],
      totalCount: 1,
      page: 1,
      pageSize: 50,
      totalPages: 1,
    }),
    updateUserPlan: vi.fn().mockResolvedValue({ message: 'Success' }),
    resetUserQuota: vi.fn().mockResolvedValue({ message: 'Reset' }),
    deleteFeedback: vi.fn().mockResolvedValue({ message: 'Deleted' }),
  },
}));

describe('AdminPage', () => {
  beforeEach(() => {
    useAuthStore.setState({
      isAuthenticated: false,
      user: null,
      token: null,
    });
  });

  afterEach(() => cleanup());

  it('blocks unauthorized visitors with access restricted barrier', () => {
    render(<AdminPage />);
    expect(screen.getByText(/Erişim Kısıtlı/i)).toBeTruthy();
    expect(screen.getByText(/Yönetim Konsolu/i)).toBeTruthy();
    expect(screen.queryByText(/Operasyonel Genel Bakış/i)).toBeNull();
  });

  it('allows designated namines@namines.com admin automatically', async () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        username: 'namines',
        email: 'namines@namines.com',
        type: 'individual',
      },
      token: 'fake-jwt-token',
    });

    render(<AdminPage />);
    await waitFor(() => {
      expect(screen.getByTitle('Namines')).toBeTruthy();
      expect(screen.getByText('v1.0')).toBeTruthy();
    });
  });

  it('renders 3-panel Shopify Polaris admin layout when user is Dev/Admin', async () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        username: 'enes',
        email: 'enes@namines.com',
        type: 'Admin',
        isDev: true,
      },
      token: 'fake-jwt-token',
    });

    render(<AdminPage />);

    // Primary Left Rail
    await waitFor(() => {
      expect(screen.getByTitle('Namines')).toBeTruthy();
      expect(screen.getByText('v1.0')).toBeTruthy();
      expect(screen.getByText('Genel Bakış (Overview)')).toBeTruthy();
      expect(screen.getByText('Kullanıcılar & Kotalar')).toBeTruthy();
      expect(screen.getAllByText(/Geri Bildirimler/i).length).toBeGreaterThan(0);
    });

    // Sub-Panel and Center Workspace
    expect(screen.getByText(/Operasyon Özeti/i)).toBeTruthy();
    expect(screen.getByText(/Toplam Kayıtlı Kullanıcı/i)).toBeTruthy();
  });
});
