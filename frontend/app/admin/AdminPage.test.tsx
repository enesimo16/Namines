// @vitest-environment jsdom
import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, cleanup, waitFor, fireEvent } from '@testing-library/react';
import AdminPage from './AdminConsole';
import AutonomousEngineInspector from './AutonomousEngineInspector';
import { useAuthStore } from '../../store/useAuthStore';
import { adminService } from '../../services/adminService';
import { setLocale } from '../../lib/i18n';

// Mock Next router
vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: vi.fn(),
    replace: vi.fn(),
    prefetch: vi.fn(),
  }),
  usePathname: () => '/admin',
  useParams: () => ({}),
}));

// Mock Link
vi.mock('next/link', () => ({
  default: ({ children, href, ...props }: { children?: React.ReactNode; href: string; [key: string]: unknown }) => (
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
    getTokenStats: vi.fn().mockResolvedValue({
      timeline: [{ date: '2026-10-18', tokens: 1000, requests: 25 }],
      modelDistribution: { 'NAI Standard': 20, 'Groq': 5 },
      serverDistribution: { 'PostgreSQL': 25 },
      advancedPreferences: { totalPrompts: 25, advancedCount: 8, ratioPercent: 32 },
      topUsers: [{ id: 'user-1', email: 'admin@namines.com', username: 'testadmin', dailyUsage: 25, dailyLimit: 999999, plan: 'dev' }],
    }),
    getSessions: vi.fn().mockResolvedValue({
      sessions: [
        {
          sessionId: 'sess-1',
          userEmail: 'user@example.com',
          startedAt: '2026-10-18T10:00:00Z',
          lastActiveAt: '2026-10-18T10:15:00Z',
          durationMinutes: 15,
          pagesVisited: ['/new', '/canvas'],
          startMode: 'prompt',
          totalClicks: 12,
          recentActions: [],
        },
      ],
      overview: {
        totalRecordedEvents: 45,
        moduleClicks: { canvas: 30, desk: 15 },
        startModes: { prompt: 20, starter: 5 },
      },
    }),
    getProjects: vi.fn().mockResolvedValue({
      totalProjects: 120,
      totalTables: 480,
      totalStorageMb: 35,
      items: [
        {
          id: 'proj-1',
          name: 'DemoDB',
          ownerId: 'user-1',
          ownerEmail: 'admin@namines.com',
          ownerUsername: 'testadmin',
          dbType: 'postgresql',
          tableCount: 12,
          estimatedMb: 2,
          vaultBackupsCount: 1,
          hasGroundDatabase: false,
          createdAt: '2026-10-01T00:00:00Z',
          updatedAt: '2026-10-18T00:00:00Z',
        },
      ],
      page: 1,
      pageSize: 50,
      totalPages: 1,
    }),
    getBotDiagnostics: vi.fn().mockResolvedValue({
      auditTimestamp: '2026-10-18T10:00:00Z',
      overallStatus: 'Healthy',
      healthScore: 100,
      probes: [
        {
          serviceName: 'PostgreSQL Control DB',
          status: 'pass',
          latencyMs: 12,
          message: 'Veritabanı bağlantısı sağlıklı',
          checkedAt: '2026-10-18T10:00:00Z',
        },
      ],
      summaryText: 'Tüm sistem bileşenleri yanıt veriyor.',
    }),
    runBotAudit: vi.fn().mockResolvedValue({
      auditTimestamp: '2026-10-18T10:00:00Z',
      overallStatus: 'Healthy',
      healthScore: 100,
      probes: [],
      summaryText: 'Denetim başarılı',
    }),
    trackEvent: vi.fn().mockResolvedValue(undefined),
    getLogs: vi.fn().mockResolvedValue({
      logs: [
        {
          id: 'log-1',
          timestamp: '2026-10-18T10:00:00Z',
          level: 'Error',
          source: 'AI Gateway',
          message: 'Upstream timeout',
          resolved: false,
        },
      ],
      stats: { totalErrors: 1, totalWarnings: 0, unresolvedCount: 1, totalRecorded: 1 },
    }),
    resolveLog: vi.fn().mockResolvedValue({ message: 'Resolved' }),
    clearLogs: vi.fn().mockResolvedValue({ message: 'Cleared' }),
    simulateError: vi.fn().mockResolvedValue({
      message: 'Simulated',
      log: { id: 'sim-1', timestamp: '2026-10-18T10:00:00Z', level: 'Error', source: 'Test', message: 'Test error', resolved: false },
    }),
    getBotHistory: vi.fn().mockResolvedValue({
      history: [],
      incidents: [],
    }),
    getAiRoutes: vi.fn().mockResolvedValue({
      routes: [],
      providers: ['groq', 'deepseek', 'ollama'],
      autonomousEngine: null,
    }),
    getFeatureFlags: vi.fn().mockResolvedValue([]),
  },
}));

describe('AdminPage', () => {
  beforeEach(() => {
    setLocale('tr');
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

  it('blocks individual user even if email is namines@namines.com without admin/isDev privilege', () => {
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
    expect(screen.getByText(/Erişim Kısıtlı/i)).toBeTruthy();
  });

  it('allows user with type admin into AdminPage', async () => {
    useAuthStore.setState({
      isAuthenticated: true,
      user: {
        username: 'admin',
        email: 'admin@company.com',
        type: 'admin',
      },
      token: 'fake-jwt-token',
    });

    render(<AdminPage />);
    await waitFor(() => {
      expect(screen.getByTitle('Namines')).toBeTruthy();
    });
  });

  it('renders 3-panel Shopify Polaris admin layout with simplified tabs when user is Dev/Admin', async () => {
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

    // Primary Left Rail: Simple Tab Names (Requested in Phase 2)
    await waitFor(() => {
      expect(screen.getByTitle('Namines')).toBeTruthy();
      expect(screen.getAllByText('Genel Bakış').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Üyeler').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Tokenlar').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Oturumlar').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Projeler').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Hatalar').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Geri Bildirim').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Sistem').length).toBeGreaterThan(0);
    });

    // Sub-Panel and Center Workspace
    expect(screen.getByText(/Operasyon Özeti/i)).toBeTruthy();
    expect(screen.getByText(/Toplam Kayıtlı Üye/i)).toBeTruthy();
  });

  it('renders localized English strings when locale is set to en', async () => {
    setLocale('en');
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

    await waitFor(() => {
      expect(screen.getByTitle('Namines')).toBeTruthy();
      expect(screen.getAllByText('Overview').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Members').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Tokens').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Sessions').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Projects').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Errors').length).toBeGreaterThan(0);
      expect(screen.getAllByText('Feedback').length).toBeGreaterThan(0);
      expect(screen.getAllByText('System').length).toBeGreaterThan(0);
    });

    expect(screen.getByText(/Operations Overview/i)).toBeTruthy();
    expect(screen.getByText(/Total Registered Members/i)).toBeTruthy();
  });

  it('renders AutonomousEngineInspector with 3-tier orchestrators, sub-agents, quotas and live runs', () => {
    render(
      <AutonomousEngineInspector
        snapshot={{
          engineVersion: 'Namines.AI v2 (Dialectical Swarm)',
          contextArchitecture: 'NSL-First L0 Topological Skeleton + L1 Scoped Subgraph + L2 Locked Contracts',
          maxOutputTokensCeiling: 65536,
          totalRuns: 3,
          convergedRuns: 3,
          averageQualityScore: 0.96,
          totalSubAgentsSpawned: 24,
          totalSubAgentsRetired: 24,
          totalTablesSynthesized: 64,
          totalLockedContracts: 64,
          subAgents: [
            { kind: 'Cartographer', name: 'CartographerSubAgent', role: 'L0 Skeleton', zeroTokenCapable: true, orchestratorParent: 'MasterOrchestrator' },
          ],
          workspaceTools: [
            { name: 'json_shape_infer', description: 'JSON shape inference' },
          ],
          quotas: [
            { plan: 'Free', dailyTokens: 100000, maxAutonomyWaves: 4, maxOutputTokens: 65536 },
          ],
          recentRuns: [
            {
              runId: 'run-1',
              timestamp: '2026-11-16T14:00:00Z',
              sourceKind: 'PromptOnly',
              engine: 'PostgreSQL',
              promptPreview: '64 tablolu ERP şeması',
              converged: true,
              qualityScore: 0.98,
              totalIterations: 4,
              tablesBuilt: 64,
              relationsBuilt: 48,
              lockedContractsCount: 64,
              spawnedSubAgents: 10,
              retiredSubAgents: 10,
              plusGains: ['[WaveGeneration] 64 tablo inşa edildi'],
              minusBlockerCount: 0,
              minusGapCount: 0,
              minusDbaCount: 1,
              remainingFindings: [],
              portabilityNotes: ['ddl_PostgreSQL=ok:4200'],
              timeline: ['[master] Final: Converged'],
            },
          ],
        }}
      />
    );

    expect(screen.getByText(/Sınırlı Özerkliğe Sahip Diyalektik AI Motoru/i)).toBeTruthy();
    expect(screen.getByText(/IMasterOrchestrator/i)).toBeTruthy();
    expect(screen.getByText(/CartographerSubAgent/i)).toBeTruthy();
    expect(screen.getByText('96%')).toBeTruthy();

    fireEvent.click(screen.getByText(/Kotalar & Bağlam/i));
    expect(screen.getByText(/100\.000|100,000/)).toBeTruthy();

    fireEvent.click(screen.getByText(/Canlı Oturumlar/i));
    expect(screen.getByText(/64 tablolu ERP şeması/i)).toBeTruthy();
  });
});
