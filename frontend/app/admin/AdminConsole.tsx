'use client';

import React, { useState, useEffect, useCallback, useMemo } from 'react';
import Link from 'next/link';
import { useRouter, useParams } from 'next/navigation';
import { useLocale } from '../../lib/i18n';
import { adminTranslations } from './adminTranslations';

import {
  LayoutDashboard,
  Users,
  Zap,
  Clock,
  HardDrive,
  MessageSquareQuote,
  ShieldCheck,
  ShieldAlert,
  Database,
  RefreshCw,
  Search,
  Sparkles,
  Trash2,
  CheckCircle2,
  ArrowLeft,
  ChevronRight,
  ChevronDown,
  UserCheck,
  Activity,
  Layers,
  Award,
  BarChart3,
  Compass,
  PlayCircle,
  Server,
  AlertTriangle,
  FolderGit2,
  AlertOctagon,
  Bug,
  Terminal,
  FileDown,
  CheckCheck,
  FileSpreadsheet,
  Calendar,
  X,
  ExternalLink,
  Workflow,
  Radio,
  Shield,
  Box,
  Copy,
} from 'lucide-react';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useToastStore } from '../../store/useToastStore';
import {
  adminService,
  downloadBlob,
  type AdminOverviewStats,
  type AdminUserItem,
  type AdminUserDetail,
  type AdminFeedbackItem,
  type AdminTokenStats,
  type AdminSessionsResponse,
  type UserSessionItem,
  type AdminProjectsResponse,
  type AdminProjectItem,
  type BotAuditReport,
  type AdminSystemLogItem,
  type SystemIncidentItem,
  type BotHistoryResponse,
  type ProjectDeepStats,
  type UserSessionsDetailResponse,
} from '../../services/adminService';
import { Select } from '../../components/ui/Select';
import Logo from '../../components/layout/Logo';
import LanguageToggle from '../../components/layout/LanguageToggle';
import ThemeToggleButton from '../../components/landing/ThemeToggleButton';

export type AdminTab = 'overview' | 'users' | 'tokens' | 'sessions' | 'projects' | 'logs' | 'feedbacks' | 'system';

export const VALID_ADMIN_TABS: AdminTab[] = [
  'overview',
  'users',
  'tokens',
  'sessions',
  'projects',
  'logs',
  'feedbacks',
  'system',
];

export default function AdminConsole({ initialTab = 'overview' }: { initialTab?: AdminTab }) {
  const router = useRouter();
  let params: any = null;
  try {
    params = useParams?.();
  } catch {
    params = null;
  }
  const locale = useLocale();
  const t = adminTranslations[locale === 'en' ? 'en' : 'tr'] || adminTranslations.tr;

  const rawRouteTab = (params?.tab as string)?.toLowerCase() as AdminTab | undefined;
  const routeTab = rawRouteTab && VALID_ADMIN_TABS.includes(rawRouteTab) ? rawRouteTab : undefined;

  const [activeTab, setActiveTab] = useState<AdminTab>(routeTab || initialTab || 'overview');

  useEffect(() => {
    if (routeTab && routeTab !== activeTab) {
      setActiveTab(routeTab);
    }
  }, [routeTab]);

  const handleSelectTab = (tab: AdminTab) => {
    setActiveTab(tab);
    router.push(`/admin/${tab}`, { scroll: false });
  };

  const { isAuthenticated, user } = useAuthStore();
  const openAuthModal = useAuthModalStore((s) => s.open);
  const showToast = useToastStore((s) => s.showToast);

  const isAdmin = useMemo(() => {
    if (!isAuthenticated || !user) return false;
    return Boolean(
      user.isDev ||
      user.type?.toLowerCase() === 'admin' ||
      user.email?.toLowerCase() === 'namines@namines.com'
    );
  }, [isAuthenticated, user]);
  const [loading, setLoading] = useState<boolean>(true);
  const [refreshing, setRefreshing] = useState<boolean>(false);

  // Overview Data
  const [overviewStats, setOverviewStats] = useState<AdminOverviewStats | null>(null);

  // Users Data
  const [usersList, setUsersList] = useState<AdminUserItem[]>([]);
  const [userSearchQuery, setUserSearchQuery] = useState('');
  const [userPlanFilter, setUserPlanFilter] = useState<string>('all');
  const [selectedUserId, setSelectedUserId] = useState<string | null>(null);
  const [selectedUserDetail, setSelectedUserDetail] = useState<AdminUserDetail | null>(null);
  const [loadingUserDetail, setLoadingUserDetail] = useState(false);
  const [editPlanCode, setEditPlanCode] = useState<string>('free');
  const [updatingPlan, setUpdatingPlan] = useState(false);
  const [resettingQuota, setResettingQuota] = useState(false);

  // Tokens Data
  const [tokensStats, setTokensStats] = useState<AdminTokenStats | null>(null);
  const [tokenDaysFilter, setTokenDaysFilter] = useState<number>(14);
  const [loadingTokens, setLoadingTokens] = useState(false);

  // Sessions Data
  const [sessionsData, setSessionsData] = useState<AdminSessionsResponse | null>(null);
  const [sessionFilter, setSessionFilter] = useState<string>('all');
  const [selectedSessionId, setSelectedSessionId] = useState<string | null>(null);
  const [loadingSessions, setLoadingSessions] = useState(false);

  // Projects Data
  const [projectsData, setProjectsData] = useState<AdminProjectsResponse | null>(null);
  const [projectSearchQuery, setProjectSearchQuery] = useState('');
  const [projectEngineFilter, setProjectEngineFilter] = useState<string>('all');
  const [selectedProjectId, setSelectedProjectId] = useState<string | null>(null);
  const [loadingProjects, setLoadingProjects] = useState(false);

  // Feedbacks Data
  const [feedbacksList, setFeedbacksList] = useState<AdminFeedbackItem[]>([]);
  const [feedbackCategoryFilter, setFeedbackCategoryFilter] = useState<string>('all');
  const [selectedFeedbackId, setSelectedFeedbackId] = useState<number | null>(null);
  const [deletingFeedback, setDeletingFeedback] = useState(false);

  // System & Autonomous Bot Data
  const [botReport, setBotReport] = useState<BotAuditReport | null>(null);
  const [runningAudit, setRunningAudit] = useState(false);
  const [botHistory, setBotHistory] = useState<BotHistoryResponse | null>(null);

  // FAZ 3: System Logs & Error Tracking Data
  const [logsList, setLogsList] = useState<AdminSystemLogItem[]>([]);
  const [logsStats, setLogsStats] = useState<{ totalErrors: number; totalWarnings: number; unresolvedCount: number; totalRecorded: number } | null>(null);
  const [logLevelFilter, setLogLevelFilter] = useState<string>('all');
  const [logSourceFilter, setLogSourceFilter] = useState<string>('all');
  const [logResolvedFilter, setLogResolvedFilter] = useState<string>('unresolved');
  const [selectedLogId, setSelectedLogId] = useState<string | null>(null);
  const [loadingLogs, setLoadingLogs] = useState(false);
  const [resolvingLog, setResolvingLog] = useState(false);
  const [simulatingError, setSimulatingError] = useState(false);

  // FAZ 4: Export Dropdown & CSV State
  const [exportDropdownOpen, setExportDropdownOpen] = useState(false);
  const [exportingCsv, setExportingCsv] = useState<string | null>(null);

  // FAZ 4: Tokens Advanced Date Range
  const [tokenPreset, setTokenPreset] = useState<'7d' | '14d' | '30d' | '90d' | 'custom'>('14d');
  const [tokenStartDate, setTokenStartDate] = useState<string>('');
  const [tokenEndDate, setTokenEndDate] = useState<string>('');
  const [tokenInterval, setTokenInterval] = useState<'daily' | 'monthly'>('daily');

  // FAZ 4: Project Deep Stats (Vault, Flow, Ground, Desk, Compile)
  const [deepStatsModalOpen, setDeepStatsModalOpen] = useState(false);
  const [deepStatsLoading, setDeepStatsLoading] = useState(false);
  const [deepStatsData, setDeepStatsData] = useState<ProjectDeepStats | null>(null);

  // FAZ 4: User Session Journey Inspector
  const [userSessionsModalOpen, setUserSessionsModalOpen] = useState(false);
  const [userSessionsLoading, setUserSessionsLoading] = useState(false);
  const [userSessionsData, setUserSessionsData] = useState<UserSessionsDetailResponse | null>(null);

  // Initial Load
  const fetchAllData = useCallback(async () => {
    if (!isAdmin) return;
    try {
      setRefreshing(true);
      const [stats, usersRes, fbRes, tokensRes, sessRes, projRes, botRes, logsRes, botHistRes] = await Promise.allSettled([
        adminService.getOverview(),
        adminService.getUsers({ pageSize: 100 }),
        adminService.getFeedbacks({ pageSize: 100 }),
        adminService.getTokenStats(tokenDaysFilter),
        adminService.getSessions(50),
        adminService.getProjects({ pageSize: 100 }),
        adminService.getBotDiagnostics(),
        adminService.getLogs({ limit: 100 }),
        adminService.getBotHistory(),
      ]);

      if (stats.status === 'fulfilled') setOverviewStats(stats.value);
      if (usersRes.status === 'fulfilled') {
        setUsersList(usersRes.value.items);
        if (usersRes.value.items.length > 0 && !selectedUserId) {
          setSelectedUserId(usersRes.value.items[0].id);
        }
      }
      if (fbRes.status === 'fulfilled') {
        setFeedbacksList(fbRes.value.items);
        if (fbRes.value.items.length > 0 && !selectedFeedbackId) {
          setSelectedFeedbackId(fbRes.value.items[0].id);
        }
      }
      if (tokensRes.status === 'fulfilled') setTokensStats(tokensRes.value);
      if (sessRes.status === 'fulfilled') {
        setSessionsData(sessRes.value);
        if (sessRes.value.sessions.length > 0 && !selectedSessionId) {
          setSelectedSessionId(sessRes.value.sessions[0].sessionId);
        }
      }
      if (projRes.status === 'fulfilled') {
        setProjectsData(projRes.value);
        if (projRes.value.items.length > 0 && !selectedProjectId) {
          setSelectedProjectId(projRes.value.items[0].id);
        }
      }
      if (botRes.status === 'fulfilled') setBotReport(botRes.value);
      if (logsRes.status === 'fulfilled') {
        setLogsList(logsRes.value.logs);
        setLogsStats(logsRes.value.stats);
        if (logsRes.value.logs.length > 0 && !selectedLogId) {
          setSelectedLogId(logsRes.value.logs[0].id);
        }
      }
      if (botHistRes.status === 'fulfilled') setBotHistory(botHistRes.value);
    } catch {
      showToast('Veriler güncellenirken hata oluştu', 'error');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [isAdmin, tokenDaysFilter, selectedUserId, selectedFeedbackId, selectedSessionId, selectedProjectId, selectedLogId, showToast]);

  useEffect(() => {
    if (isAdmin) {
      fetchAllData();
    } else {
      setLoading(false);
    }
  }, [isAdmin, fetchAllData]);

  // Load single user detail when selectedUserId changes
  useEffect(() => {
    if (!selectedUserId || !isAdmin) return;
    let isCancelled = false;

    const loadDetail = async () => {
      try {
        setLoadingUserDetail(true);
        const detail = await adminService.getUserDetail(selectedUserId);
        if (!isCancelled) {
          setSelectedUserDetail(detail);
          setEditPlanCode(detail.planCode);
        }
      } catch {
        if (!isCancelled) {
          showToast('Kullanıcı detayları alınamadı', 'error');
        }
      } finally {
        if (!isCancelled) {
          setLoadingUserDetail(false);
        }
      }
    };

    loadDetail();
    return () => {
      isCancelled = true;
    };
  }, [selectedUserId, isAdmin, showToast]);

  // Token stats change handler
  const handleDaysChange = async (days: number) => {
    setTokenDaysFilter(days);
    try {
      setLoadingTokens(true);
      const res = await adminService.getTokenStats(days);
      setTokensStats(res);
    } catch {
      showToast('Token istatistikleri alınamadı', 'error');
    } finally {
      setLoadingTokens(false);
    }
  };

  // Run on-demand Bot Audit
  const handleRunBotAudit = async () => {
    try {
      setRunningAudit(true);
      const res = await adminService.runBotAudit();
      setBotReport(res);
      const [histRes, ovRes] = await Promise.allSettled([
        adminService.getBotHistory(),
        adminService.getOverview(),
      ]);
      if (histRes.status === 'fulfilled') setBotHistory(histRes.value);
      if (ovRes.status === 'fulfilled') setOverviewStats(ovRes.value);
      showToast(
        locale === 'en' ? 'Autonomous bot audit completed successfully' : 'Otonom denetim başarıyla tamamlandı',
        'success'
      );
    } catch {
      showToast(
        locale === 'en' ? 'Error executing bot audit' : 'Denetim çalıştırılırken hata oluştu',
        'error'
      );
    } finally {
      setRunningAudit(false);
    }
  };

  // FAZ 3: Log Actions
  const handleResolveLog = async (id: string) => {
    try {
      setResolvingLog(true);
      await adminService.resolveLog(id);
      showToast('Hata kaydı çözüldü olarak işaretlendi', 'success');
      setLogsList((prev) =>
        prev.map((l) => (l.id === id ? { ...l, resolved: true } : l))
      );
      if (logsStats) {
        setLogsStats({ ...logsStats, unresolvedCount: Math.max(0, logsStats.unresolvedCount - 1) });
      }
    } catch {
      showToast('Hata güncellenemedi', 'error');
    } finally {
      setResolvingLog(false);
    }
  };

  const handleClearLogs = async () => {
    try {
      await adminService.clearLogs();
      setLogsList([]);
      if (logsStats) {
        setLogsStats({ totalErrors: 0, totalWarnings: 0, unresolvedCount: 0, totalRecorded: 0 });
      }
      setSelectedLogId(null);
      showToast('Tüm hata kayıtları temizlendi', 'info');
    } catch {
      showToast('Hata kayıtları temizlenemedi', 'error');
    }
  };

  const handleSimulateError = async () => {
    try {
      setSimulatingError(true);
      const res = await adminService.simulateError({
        level: 'Error',
        source: 'AI Gateway',
        message: 'Simulated high-priority diagnostic alert: inference timeout 5000ms exceeded on upstream model.',
      });
      showToast('Simüle hata kaydı eklendi', 'success');
      setLogsList((prev) => [res.log, ...prev]);
      setSelectedLogId(res.log.id);
      if (logsStats) {
        setLogsStats({
          ...logsStats,
          totalErrors: logsStats.totalErrors + 1,
          unresolvedCount: logsStats.unresolvedCount + 1,
          totalRecorded: logsStats.totalRecorded + 1,
        });
      }
    } catch {
      showToast('Simülasyon gerçekleştirilemedi', 'error');
    } finally {
      setSimulatingError(false);
    }
  };

  // User Actions
  const handleUpdatePlan = async () => {
    if (!selectedUserId) return;
    try {
      setUpdatingPlan(true);
      const result = await adminService.updateUserPlan(selectedUserId, editPlanCode);
      showToast(result.message || 'Kullanıcı planı başarıyla güncellendi', 'success');
      const detail = await adminService.getUserDetail(selectedUserId);
      setSelectedUserDetail(detail);
      setUsersList((prev) =>
        prev.map((u) =>
          u.id === selectedUserId ? { ...u, planCode: editPlanCode as AdminUserItem['planCode'] } : u
        )
      );
    } catch {
      showToast('Plan güncellenirken hata oluştu', 'error');
    } finally {
      setUpdatingPlan(false);
    }
  };

  const handleResetQuota = async (resetToZero: boolean, bonus: number = 0) => {
    if (!selectedUserId) return;
    try {
      setResettingQuota(true);
      const result = await adminService.resetUserQuota(selectedUserId, {
        resetToZero,
        bonusPoints: bonus,
      });
      showToast(result.message || 'Kota başarıyla sıfırlandı', 'success');
      const detail = await adminService.getUserDetail(selectedUserId);
      setSelectedUserDetail(detail);
    } catch {
      showToast('Kota işlemi gerçekleştirilemedi', 'error');
    } finally {
      setResettingQuota(false);
    }
  };

  const handleDeleteFeedback = async (id: number) => {
    try {
      setDeletingFeedback(true);
      await adminService.deleteFeedback(id);
      showToast('Geri bildirim bileti arşivlendi ve kaldırıldı', 'success');
      setFeedbacksList((prev) => prev.filter((f) => f.id !== id));
      if (selectedFeedbackId === id) {
        setSelectedFeedbackId(null);
      }
    } catch {
      showToast('Bilet kaldırılırken hata oluştu', 'error');
    } finally {
      setDeletingFeedback(false);
    }
  };

  // Export full ops report as JSON
  const handleExportJson = () => {
    const reportData = {
      generatedAt: new Date().toISOString(),
      overview: overviewStats,
      tokens: tokensStats,
      sessions: sessionsData?.overview,
      projects: projectsData ? { total: projectsData.totalProjects, tables: projectsData.totalTables, storageMb: projectsData.totalStorageMb } : null,
      systemHealth: botReport,
      incidents: botHistory?.incidents || [],
      logsSummary: logsStats,
    };
    const blob = new Blob([JSON.stringify(reportData, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `namines-ops-report-${new Date().toISOString().substring(0, 10)}.json`;
    a.click();
    URL.revokeObjectURL(url);
    showToast('Sistem operasyon raporu JSON olarak indirildi', 'success');
  };

  // FAZ 4: Export CSV Handler
  const handleExportCsv = async (type: 'users' | 'projects' | 'logs' | 'feedbacks' | 'sessions' | 'tokens') => {
    try {
      setExportingCsv(type);
      const blob = await adminService.exportCsv(type);
      const filename = `namines-${type}-${new Date().toISOString().substring(0, 10)}.csv`;
      downloadBlob(blob, filename);
      const labels: Record<string, string> = {
        users: 'Üyeler ve Kotalar',
        projects: 'Projeler ve Depolama',
        logs: 'Hata ve Sistem Günlüğü',
        feedbacks: 'Geri Bildirimler',
        sessions: 'Kullanıcı Oturumları',
        tokens: 'Günlük Token Akışı',
      };
      showToast(`${labels[type] || type.toUpperCase()} CSV olarak indirildi`, 'success');
    } catch {
      showToast('CSV dışa aktarma işlemi başarısız oldu', 'error');
    } finally {
      setExportingCsv(null);
    }
  };

  // 1-Tıkla Sistem Durumunu Panoya Kopyalama (Markdown)
  const handleCopySystemSnapshot = () => {
    const timestamp = new Date().toLocaleString('tr-TR');
    const md = `# Namines Sistem Operasyon Durumu (${timestamp})

- **Toplam Kullanıcı:** ${overviewStats?.totalUsers ?? 0}
- **Aktif Projeler:** ${overviewStats?.activeProjects ?? 0}
- **Bugün Tüketilen Token:** ${(overviewStats?.todayTokens ?? 0).toLocaleString()}
- **Sistem Sağlığı:** %${botReport?.healthScore ?? 100} (${botReport?.overallStatus || 'Healthy'})
- **Açık Hata / Uyarı:** ${overviewStats?.openErrorsCount ?? 0}
- **PostgreSQL Durumu:** ${botReport?.probes?.find((p) => p.serviceName.includes('Postgre'))?.status ?? 'PASS'}
- **AI Gateway Durumu:** ${botReport?.probes?.find((p) => p.serviceName.includes('Gateway'))?.status ?? 'PASS'}
- **Aktif İnsident Sayısı:** ${botHistory?.incidents?.length ?? 0}

*Rapor Namines Admin Konsolundan otomatik üretilmiştir.*`;

    if (navigator?.clipboard?.writeText) {
      navigator.clipboard
        .writeText(md)
        .then(() => {
          showToast('Sistem durum özeti panoya kopyalandı (Markdown)', 'success');
        })
        .catch(() => {
          showToast('Panoya kopyalama başarısız oldu', 'error');
        });
    } else {
      showToast('Panoya erişim desteklenmiyor', 'error');
    }
  };

  // FAZ 4: Apply Token Filter
  const handleApplyTokenFilter = async (
    preset: '7d' | '14d' | '30d' | '90d' | 'custom',
    customStart?: string,
    customEnd?: string,
    interval?: 'daily' | 'monthly'
  ) => {
    try {
      setLoadingTokens(true);
      setTokenPreset(preset);
      const activeInterval = interval || tokenInterval;
      if (preset === 'custom') {
        const start = customStart !== undefined ? customStart : tokenStartDate;
        const end = customEnd !== undefined ? customEnd : tokenEndDate;
        const res = await adminService.getTokenStats({
          startDate: start || undefined,
          endDate: end || undefined,
          interval: activeInterval,
        });
        setTokensStats(res);
      } else {
        const days = preset === '7d' ? 7 : preset === '14d' ? 14 : preset === '30d' ? 30 : 90;
        setTokenDaysFilter(days);
        const res = await adminService.getTokenStats({ days, interval: activeInterval });
        setTokensStats(res);
      }
    } catch {
      showToast('Token istatistikleri yüklenirken hata oluştu', 'error');
    } finally {
      setLoadingTokens(false);
    }
  };

  // FAZ 4: Open Project Deep Stats
  const handleOpenProjectDeepStats = async (projectId: string) => {
    try {
      setDeepStatsModalOpen(true);
      setDeepStatsLoading(true);
      const stats = await adminService.getProjectDeepStats(projectId);
      setDeepStatsData(stats);
    } catch {
      showToast('Proje modül detayları alınamadı', 'error');
    } finally {
      setDeepStatsLoading(false);
    }
  };

  // FAZ 4: Open User Sessions History
  const handleOpenUserSessions = async (userId: string) => {
    try {
      setUserSessionsModalOpen(true);
      setUserSessionsLoading(true);
      const data = await adminService.getUserSessions(userId);
      setUserSessionsData(data);
    } catch {
      showToast('Kullanıcı oturum yolculuğu verisi alınamadı', 'error');
    } finally {
      setUserSessionsLoading(false);
    }
  };

  // Filtered Users
  const filteredUsers = useMemo(() => {
    return usersList.filter((u) => {
      const matchesSearch =
        u.userName.toLowerCase().includes(userSearchQuery.toLowerCase()) ||
        u.email.toLowerCase().includes(userSearchQuery.toLowerCase()) ||
        (u.companyName && u.companyName.toLowerCase().includes(userSearchQuery.toLowerCase()));
      const matchesPlan = userPlanFilter === 'all' || u.planCode === userPlanFilter;
      return matchesSearch && matchesPlan;
    });
  }, [usersList, userSearchQuery, userPlanFilter]);

  // Filtered Feedbacks
  const filteredFeedbacks = useMemo(() => {
    return feedbacksList.filter((f) => {
      if (feedbackCategoryFilter === 'all') return true;
      return f.category.toLowerCase() === feedbackCategoryFilter.toLowerCase();
    });
  }, [feedbacksList, feedbackCategoryFilter]);

  const selectedFeedback = useMemo(() => {
    return feedbacksList.find((f) => f.id === selectedFeedbackId) || null;
  }, [feedbacksList, selectedFeedbackId]);

  // Filtered Sessions
  const filteredSessions = useMemo(() => {
    if (!sessionsData?.sessions) return [];
    return sessionsData.sessions.filter((s) => {
      if (sessionFilter === 'all') return true;
      return s.startMode.toLowerCase().includes(sessionFilter.toLowerCase());
    });
  }, [sessionsData, sessionFilter]);

  const selectedSession = useMemo(() => {
    if (!sessionsData?.sessions) return null;
    return sessionsData.sessions.find((s) => s.sessionId === selectedSessionId) || null;
  }, [sessionsData, selectedSessionId]);

  // Filtered Projects
  const filteredProjects = useMemo(() => {
    if (!projectsData?.items) return [];
    return projectsData.items.filter((p) => {
      const matchesSearch =
        p.name.toLowerCase().includes(projectSearchQuery.toLowerCase()) ||
        p.ownerEmail.toLowerCase().includes(projectSearchQuery.toLowerCase()) ||
        p.ownerUsername.toLowerCase().includes(projectSearchQuery.toLowerCase());
      const matchesEngine =
        projectEngineFilter === 'all' ||
        p.dbType.toLowerCase() === projectEngineFilter.toLowerCase();
      return matchesSearch && matchesEngine;
    });
  }, [projectsData, projectSearchQuery, projectEngineFilter]);

  // Filtered Logs
  const filteredLogs = useMemo(() => {
    return logsList.filter((l) => {
      const matchesLevel = logLevelFilter === 'all' || l.level.toLowerCase() === logLevelFilter.toLowerCase();
      const matchesSource = logSourceFilter === 'all' || l.source.toLowerCase() === logSourceFilter.toLowerCase();
      const matchesResolved =
        logResolvedFilter === 'all' ? true : logResolvedFilter === 'unresolved' ? !l.resolved : l.resolved;
      return matchesLevel && matchesSource && matchesResolved;
    });
  }, [logsList, logLevelFilter, logSourceFilter, logResolvedFilter]);

  const selectedLog = useMemo(() => {
    return logsList.find((l) => l.id === selectedLogId) || null;
  }, [logsList, selectedLogId]);

  // Giriş Koruması (AdminGuard)
  if (!loading && !isAdmin) {
    return (
      <div className="flex min-h-screen w-full items-center justify-center bg-surface-900 p-6 font-sans text-content-primary select-none">
        <div className="w-full max-w-sm rounded-[var(--radius-modal)] border border-line bg-surface-800 p-7 shadow-xl text-center space-y-5">
          <div className="space-y-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-surface-700/60 border border-line text-[10px] font-mono tracking-widest uppercase text-content-muted">
              <span className="h-1.5 w-1.5 rounded-full bg-danger/80"></span>
              {t.accessRestrictedBadge}
            </span>
            <h1 className="text-base font-bold tracking-tight text-content-primary">
              {t.accessRestricted}
            </h1>
            <p className="text-xs text-content-muted leading-relaxed max-w-xs mx-auto">
              {t.accessRestrictedDesc}
            </p>
          </div>

          {isAuthenticated && (
            <div className="rounded-[var(--radius-card)] bg-surface-900 border border-line p-3 text-left space-y-1">
              <div className="flex items-center justify-between text-xs">
                <span className="text-content-muted text-[11px]">{t.activeAccount}</span>
                <span className="font-mono text-content-primary text-[11px] font-medium truncate max-w-[190px]">
                  {user?.email || user?.username}
                </span>
              </div>
              <p className="text-[10px] text-content-muted">
                {t.noAdminPrivilege}
              </p>
            </div>
          )}

          <div className="space-y-2 pt-1">
            <button
              onClick={() => openAuthModal()}
              className="w-full rounded-[var(--radius-control)] bg-accent px-4 py-2.5 text-xs font-bold text-accent-on transition-colors hover:bg-accent-hover cursor-pointer shadow-xs"
            >
              {isAuthenticated ? t.switchAccount : t.signInAdmin}
            </button>

            <Link
              href="/new"
              className="block w-full py-1.5 text-xs text-content-muted hover:text-content-primary transition-colors text-center"
            >
              {locale === 'en' ? 'Back to Workspace' : 'Çalışma Alanına Dön'}
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-surface-900 font-sans text-content-primary">
      {/* ─────────────────────────────────────────────────────────────
          1. SOL PANEL: Ana Gezinti / Primary Rail (Basit Başlıklar)
          ───────────────────────────────────────────────────────────── */}
      <aside className="flex w-56 shrink-0 flex-col justify-between border-r border-line bg-surface-900 select-none">
        <div>
          {/* Brand Header */}
          <div className="flex h-14 items-center justify-between border-b border-line px-4">
            <Link href="/new" className="flex items-center gap-2 group" title="Namines">
              <Logo size="sm" showWordmark={true} />
            </Link>
            <span className="rounded-full bg-accent-subtle border border-line px-2 py-0.5 text-[10px] font-mono font-bold text-accent-text">
              v1.0
            </span>
          </div>

          {/* Navigation Links - Ultra Simple Turkish Nouns */}
          <nav className="p-2.5 space-y-1">
            <button
              onClick={() => handleSelectTab('overview')}
              className={`flex w-full items-center gap-2.5 rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'overview'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <LayoutDashboard className="h-4 w-4 text-accent-text shrink-0" />
              <span>{t.tabOverview}</span>
            </button>

            <button
              onClick={() => handleSelectTab('users')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'users'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Users className="h-4 w-4 text-accent-text shrink-0" />
                <span>{t.tabUsers}</span>
              </div>
              {usersList.length > 0 && (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  {usersList.length}
                </span>
              )}
            </button>

            <button
              onClick={() => handleSelectTab('tokens')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'tokens'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Zap className="h-4 w-4 text-accent-text shrink-0" />
                <span>{t.tabTokens}</span>
              </div>
              {tokensStats && (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  AI
                </span>
              )}
            </button>

            <button
              onClick={() => handleSelectTab('sessions')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'sessions'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Clock className="h-4 w-4 text-accent-text shrink-0" />
                <span>{t.tabSessions}</span>
              </div>
              {sessionsData?.sessions && (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  {sessionsData.sessions.length}
                </span>
              )}
            </button>

            <button
              onClick={() => handleSelectTab('projects')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'projects'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <HardDrive className="h-4 w-4 text-accent-text shrink-0" />
                <span>{t.tabProjects}</span>
              </div>
              {projectsData && (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  {projectsData.totalProjects}
                </span>
              )}
            </button>

            {/* FAZ 3: Hatalar (Logs) */}
            <button
              onClick={() => handleSelectTab('logs')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'logs'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Bug className="h-4 w-4 text-accent-text shrink-0" />
                <span>{t.tabLogs}</span>
              </div>
              {logsStats && logsStats.unresolvedCount > 0 ? (
                <span className="rounded-full bg-danger/20 text-danger border border-danger/30 px-1.5 py-0.2 text-[10px] font-mono font-bold">
                  {logsStats.unresolvedCount}
                </span>
              ) : (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  0
                </span>
              )}
            </button>

            <button
              onClick={() => handleSelectTab('feedbacks')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'feedbacks'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <MessageSquareQuote className="h-4 w-4 text-accent-text shrink-0" />
                <span>{t.tabFeedbacks}</span>
              </div>
              {feedbacksList.length > 0 && (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  {feedbacksList.length}
                </span>
              )}
            </button>

            <button
              onClick={() => handleSelectTab('system')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'system'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Activity className="h-4 w-4 text-accent-text shrink-0" />
                <span>{t.tabSystem}</span>
              </div>
            </button>
          </nav>
        </div>

        {/* Bottom Quick Return to Studio */}
        <div className="border-t border-line p-3">
          <Link
            href="/new"
            className="flex w-full items-center justify-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-800 px-3 py-2 text-xs font-medium text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer"
          >
            <ArrowLeft className="h-3.5 w-3.5" />
            <span>{t.backToCanvas}</span>
          </Link>
        </div>
      </aside>

      {/* ─────────────────────────────────────────────────────────────
          2. İÇ PANEL: Sol panelin sağında açılan iç panel (Sub-Panel)
          ───────────────────────────────────────────────────────────── */}
      <section className="flex w-72 sm:w-80 shrink-0 flex-col border-r border-line bg-surface-800/40 backdrop-blur-md overflow-hidden">
        {/* Sub-Panel Header */}
        <div className="flex h-14 shrink-0 items-center justify-between border-b border-line px-4">
          <span className="text-xs font-bold uppercase tracking-wider text-content-secondary">
            {activeTab === 'overview' && t.subOverview}
            {activeTab === 'users' && t.subUsers(filteredUsers.length)}
            {activeTab === 'tokens' && t.subTokens}
            {activeTab === 'sessions' && t.subSessions(filteredSessions.length)}
            {activeTab === 'projects' && t.subProjects(filteredProjects.length)}
            {activeTab === 'logs' && t.subLogs(filteredLogs.length)}
            {activeTab === 'feedbacks' && t.subFeedbacks(filteredFeedbacks.length)}
            {activeTab === 'system' && t.subSystem}
          </span>
          <button
            onClick={() => fetchAllData()}
            disabled={refreshing}
            title={locale === 'en' ? 'Refresh' : 'Yenile'}
            className="tap-44 p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${refreshing ? 'animate-spin' : ''}`} />
          </button>
        </div>

        {/* Sub-Panel Content: Tab-Dependent */}
        {activeTab === 'overview' && (
          <div className="flex-1 overflow-y-auto p-4 space-y-4 text-xs">
            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3.5 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                {t.platformStatus}
              </span>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">{t.controlDb}</span>
                <span className="text-content-primary font-medium">
                  {t.healthy}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">{t.redis}</span>
                <span className="text-content-primary font-medium">
                  {t.connected}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">{t.aiGateway}</span>
                <span className="text-content-primary font-medium">
                  {t.active}
                </span>
              </div>
            </div>

            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3.5 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                {t.auditBot}
              </span>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">{t.healthScore}</span>
                <span className="font-mono font-medium text-content-primary">
                  %{botReport?.healthScore ?? 100}
                </span>
              </div>
              <p className="text-[10px] text-content-muted">
                {botReport?.summaryText || t.allServicesOk}
              </p>
              <button
                onClick={handleRunBotAudit}
                disabled={runningAudit}
                className="mt-1 flex w-full items-center justify-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-800 px-3 py-1.5 text-[11px] font-medium text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
              >
                <RefreshCw className={`h-3 w-3 ${runningAudit ? 'animate-spin' : ''}`} />
                {runningAudit ? t.runningAudit : t.runAudit}
              </button>
            </div>

            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3.5 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                {locale === 'en' ? 'Quick Links' : 'Hızlı Gezinti'}
              </span>
              <button
                onClick={() => handleSelectTab('users')}
                className="flex w-full items-center justify-between rounded-[var(--radius-control)] bg-surface-800 p-2 text-content-primary hover:bg-surface-700 transition-colors text-left"
              >
                <span>{locale === 'en' ? 'View Members' : 'Üyeleri İncele'}</span>
                <ChevronRight className="h-3.5 w-3.5 text-content-muted" />
              </button>
              <button
                onClick={() => handleSelectTab('tokens')}
                className="flex w-full items-center justify-between rounded-[var(--radius-control)] bg-surface-800 p-2 text-content-primary hover:bg-surface-700 transition-colors text-left"
              >
                <span>{locale === 'en' ? 'Token & Model Distribution' : 'Token & Model Dağılımı'}</span>
                <ChevronRight className="h-3.5 w-3.5 text-content-muted" />
              </button>
              <button
                onClick={() => handleSelectTab('logs')}
                className="flex w-full items-center justify-between rounded-[var(--radius-control)] bg-surface-800 p-2 text-content-primary hover:bg-surface-700 transition-colors text-left"
              >
                <span>{locale === 'en' ? 'View Errors & Logs' : 'Hataları & İstisnaları Gör'}</span>
                <ChevronRight className="h-3.5 w-3.5 text-content-muted" />
              </button>
              <button
                onClick={() => handleSelectTab('projects')}
                className="flex w-full items-center justify-between rounded-[var(--radius-control)] bg-surface-800 p-2 text-content-primary hover:bg-surface-700 transition-colors text-left"
              >
                <span>{locale === 'en' ? 'Projects & Storage Sizes' : 'Projeler & Boyutlar'}</span>
                <ChevronRight className="h-3.5 w-3.5 text-content-muted" />
              </button>
            </div>
          </div>
        )}

        {activeTab === 'users' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            {/* Search Input */}
            <div className="p-3 border-b border-line space-y-2">
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-content-muted" />
                <input
                  type="text"
                  placeholder={locale === 'en' ? 'Search user or email...' : 'Kullanıcı veya e-posta ara...'}
                  value={userSearchQuery}
                  onChange={(e) => setUserSearchQuery(e.target.value)}
                  className="w-full rounded-[var(--radius-control)] border border-line bg-surface-900 py-1.5 pl-8 pr-3 text-xs text-content-primary placeholder:text-content-muted focus:border-line-strong focus:outline-none"
                />
              </div>

              {/* Plan Filter Chips */}
              <div className="flex items-center gap-1 overflow-x-auto scrollbar-none pb-0.5">
                {['all', 'free', 'pro', 'team', 'dev'].map((plan) => (
                  <button
                    key={plan}
                    onClick={() => setUserPlanFilter(plan)}
                    className={`rounded-full px-2.5 py-0.5 text-[10px] font-mono uppercase tracking-wider transition-colors cursor-pointer whitespace-nowrap ${
                      userPlanFilter === plan
                        ? 'bg-accent text-accent-on font-bold'
                        : 'bg-surface-700 text-content-muted hover:text-content-secondary border border-line'
                    }`}
                  >
                    {plan}
                  </button>
                ))}
              </div>
            </div>

            {/* User Scrollable List */}
            <div className="flex-1 overflow-y-auto divide-y divide-line/60">
              {filteredUsers.length === 0 ? (
                <div className="p-6 text-center text-xs text-content-muted">
                  Kriterlere uygun üye bulunamadı.
                </div>
              ) : (
                filteredUsers.map((u) => {
                  const isSelected = selectedUserId === u.id;
                  return (
                    <div
                      key={u.id}
                      onClick={() => setSelectedUserId(u.id)}
                      className={`flex items-start gap-2.5 p-3 cursor-pointer transition-colors text-left ${
                        isSelected
                          ? 'bg-surface-700/80 border-l-2 border-accent text-content-primary'
                          : 'hover:bg-surface-700/40 text-content-secondary'
                      }`}
                    >
                      <div className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-surface-700 border border-line text-[10px] font-mono font-bold uppercase text-content-primary mt-0.5">
                        {u.userName ? u.userName.substring(0, 2).toUpperCase() : 'US'}
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center justify-between gap-1">
                          <span className="truncate text-xs font-semibold text-content-primary">
                            {u.userName}
                          </span>
                          <span
                            className={`rounded-full px-1.5 py-0.2 text-[10px] font-mono font-bold uppercase ${
                              u.planCode === 'dev'
                                ? 'bg-accent-subtle text-accent-text border border-line'
                                : u.planCode === 'team'
                                ? 'bg-surface-600 text-content-primary border border-line'
                                : u.planCode === 'pro'
                                ? 'bg-surface-600 text-content-primary border border-line'
                                : 'bg-surface-700 text-content-muted border border-line'
                            }`}
                          >
                            {u.planCode}
                          </span>
                        </div>
                        <p className="truncate text-[11px] text-content-muted mt-0.5">{u.email}</p>
                        <div className="flex items-center gap-2 mt-1.5 text-[10px] font-mono text-content-muted">
                          <span>{u.projectsCount} proje</span>
                          <span>·</span>
                          <span>{u.dailyUsageCount} / {u.dailyLimit} token</span>
                        </div>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}

        {activeTab === 'tokens' && (
          <div className="flex flex-1 flex-col overflow-hidden p-3 space-y-4">
            <div className="space-y-1.5">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                Zaman Aralığı
              </span>
              <div className="grid grid-cols-3 gap-1.5">
                {[7, 14, 30].map((days) => (
                  <button
                    key={days}
                    onClick={() => handleDaysChange(days)}
                    className={`rounded-[var(--radius-control)] py-1 text-center text-xs font-mono transition-colors cursor-pointer ${
                      tokenDaysFilter === days
                        ? 'bg-accent text-accent-on font-bold'
                        : 'bg-surface-700/60 text-content-muted hover:text-content-secondary border border-line'
                    }`}
                  >
                    {days} Gün
                  </button>
                ))}
              </div>
            </div>

            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                Model Dağılımı Özeti
              </span>
              {tokensStats?.modelDistribution ? (
                Object.entries(tokensStats.modelDistribution).map(([model, count]) => (
                  <div key={model} className="flex items-center justify-between text-xs">
                    <span className="truncate text-content-secondary max-w-[140px]">{model}</span>
                    <span className="font-mono font-bold text-content-primary">{count} çağrı</span>
                  </div>
                ))
              ) : (
                <div className="text-center text-xs text-content-muted py-2">Yükleniyor...</div>
              )}
            </div>

            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                En Çok Tüketenler
              </span>
              {tokensStats?.topUsers?.slice(0, 4).map((u) => (
                <div
                  key={u.id}
                  onClick={() => {
                    setSelectedUserId(u.id);
                    setActiveTab('users');
                  }}
                  className="flex items-center justify-between text-xs p-1.5 rounded-[var(--radius-control)] hover:bg-surface-800 cursor-pointer"
                >
                  <span className="truncate text-content-primary max-w-[130px]">{u.username || u.email}</span>
                  <span className="font-mono text-accent-text font-bold">{u.dailyUsage}</span>
                </div>
              ))}
            </div>
          </div>
        )}

        {activeTab === 'sessions' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            {/* Filter by Start Mode */}
            <div className="p-3 border-b border-line space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                Başlangıç Modu Filtresi
              </span>
              <div className="flex items-center gap-1 overflow-x-auto scrollbar-none pb-0.5">
                {['all', 'prompt', 'starter', 'database', 'code', 'json'].map((mode) => (
                  <button
                    key={mode}
                    onClick={() => setSessionFilter(mode)}
                    className={`rounded-full px-2.5 py-0.5 text-[10px] font-mono uppercase tracking-wider transition-colors cursor-pointer whitespace-nowrap ${
                      sessionFilter === mode
                        ? 'bg-accent text-accent-on font-bold'
                        : 'bg-surface-700 text-content-muted hover:text-content-secondary border border-line'
                    }`}
                  >
                    {mode}
                  </button>
                ))}
              </div>
            </div>

            {/* Sessions List */}
            <div className="flex-1 overflow-y-auto divide-y divide-line/60">
              {filteredSessions.length === 0 ? (
                <div className="p-6 text-center text-xs text-content-muted">
                  Kayıtlı oturum bulunamadı.
                </div>
              ) : (
                filteredSessions.map((s) => {
                  const isSelected = selectedSessionId === s.sessionId;
                  return (
                    <div
                      key={s.sessionId}
                      onClick={() => setSelectedSessionId(s.sessionId)}
                      className={`p-3 cursor-pointer transition-colors text-left ${
                        isSelected
                          ? 'bg-surface-700/80 border-l-2 border-accent text-content-primary'
                          : 'hover:bg-surface-700/40 text-content-secondary'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-1">
                        <span className="truncate text-xs font-semibold text-content-primary">
                          {s.userEmail || 'Anonim Oturum'}
                        </span>
                        <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                          {s.durationMinutes} dk
                        </span>
                      </div>
                      <div className="flex items-center justify-between mt-1 text-[10px] font-mono text-content-muted">
                        <span className="rounded-[var(--radius-control)] bg-accent-subtle/50 px-1 text-accent-text uppercase">
                          {s.startMode}
                        </span>
                        <span>{s.pagesVisited?.length || 1} sayfa</span>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}

        {activeTab === 'projects' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            {/* Search and Engine Filter */}
            <div className="p-3 border-b border-line space-y-2">
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-content-muted" />
                <input
                  type="text"
                  placeholder="Proje veya sahip ara..."
                  value={projectSearchQuery}
                  onChange={(e) => setProjectSearchQuery(e.target.value)}
                  className="w-full rounded-[var(--radius-control)] border border-line bg-surface-900 py-1.5 pl-8 pr-3 text-xs text-content-primary placeholder:text-content-muted focus:border-line-strong focus:outline-none"
                />
              </div>

              <div className="flex items-center gap-1 overflow-x-auto scrollbar-none pb-0.5">
                {['all', 'postgresql', 'mysql', 'sqlite', 'sqlserver'].map((eng) => (
                  <button
                    key={eng}
                    onClick={() => setProjectEngineFilter(eng)}
                    className={`rounded-full px-2 py-0.5 text-[10px] font-mono uppercase tracking-wider transition-colors cursor-pointer whitespace-nowrap ${
                      projectEngineFilter === eng
                        ? 'bg-accent text-accent-on font-bold'
                        : 'bg-surface-700 text-content-muted hover:text-content-secondary border border-line'
                    }`}
                  >
                    {eng}
                  </button>
                ))}
              </div>
            </div>

            {/* Project List */}
            <div className="flex-1 overflow-y-auto divide-y divide-line/60">
              {filteredProjects.length === 0 ? (
                <div className="p-6 text-center text-xs text-content-muted">
                  Kriterlere uygun proje bulunamadı.
                </div>
              ) : (
                filteredProjects.map((p) => {
                  return (
                    <div
                      key={p.id}
                      onClick={() => setSelectedProjectId(p.id)}
                      className={`p-3 cursor-pointer transition-colors text-left ${
                        selectedProjectId === p.id
                          ? 'bg-surface-700/80 border-l-2 border-accent text-content-primary'
                          : 'hover:bg-surface-700/40 text-content-secondary'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-1">
                        <span className="truncate text-xs font-semibold text-content-primary">
                          {p.name}
                        </span>
                        <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono uppercase text-content-muted">
                          {p.dbType}
                        </span>
                      </div>
                      <p className="truncate text-[11px] text-content-muted mt-0.5">{p.ownerEmail}</p>
                      <div className="flex items-center justify-between mt-1 text-[10px] font-mono text-content-muted">
                        <span>{p.tableCount} tablo</span>
                        <span>{p.estimatedMb} MB</span>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}

        {/* FAZ 3: Sub-panel for Hatalar (logs) */}
        {activeTab === 'logs' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            {/* Filter by Level */}
            <div className="p-3 border-b border-line space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                  Hata Seviyesi
                </span>
                <button
                  onClick={handleSimulateError}
                  disabled={simulatingError}
                  title="Test Hatası Simüle Et"
                  className="rounded-[var(--radius-control)] bg-danger/10 border border-danger/30 px-2 py-0.5 text-[10px] font-mono text-danger hover:bg-danger/20 transition-colors cursor-pointer"
                >
                  + Simüle Et
                </button>
              </div>

              <div className="flex items-center gap-1 overflow-x-auto scrollbar-none pb-0.5">
                {['all', 'Error', 'Warning', 'Critical', 'Info'].map((lvl) => (
                  <button
                    key={lvl}
                    onClick={() => setLogLevelFilter(lvl)}
                    className={`rounded-full px-2 py-0.5 text-[10px] font-mono uppercase tracking-wider transition-colors cursor-pointer whitespace-nowrap ${
                      logLevelFilter === lvl
                        ? 'bg-accent text-accent-on font-bold'
                        : 'bg-surface-700 text-content-muted hover:text-content-secondary border border-line'
                    }`}
                  >
                    {lvl}
                  </button>
                ))}
              </div>

              {/* Status Filter Toggle */}
              <div className="flex items-center gap-1.5 pt-1">
                <button
                  onClick={() => setLogResolvedFilter('unresolved')}
                  className={`flex-1 rounded-[var(--radius-control)] py-1 text-center text-[10px] font-mono transition-colors cursor-pointer ${
                    logResolvedFilter === 'unresolved'
                      ? 'bg-surface-700 text-content-primary font-bold border border-line-strong'
                      : 'text-content-muted hover:text-content-secondary'
                  }`}
                >
                  Açık ({logsStats?.unresolvedCount ?? 0})
                </button>
                <button
                  onClick={() => setLogResolvedFilter('all')}
                  className={`flex-1 rounded-[var(--radius-control)] py-1 text-center text-[10px] font-mono transition-colors cursor-pointer ${
                    logResolvedFilter === 'all'
                      ? 'bg-surface-700 text-content-primary font-bold border border-line-strong'
                      : 'text-content-muted hover:text-content-secondary'
                  }`}
                >
                  Tümü
                </button>
              </div>
            </div>

            {/* Logs List */}
            <div className="flex-1 overflow-y-auto divide-y divide-line/60">
              {filteredLogs.length === 0 ? (
                <div className="p-6 text-center text-xs text-content-muted">
                  Kayıtlı hata logu bulunamadı.
                </div>
              ) : (
                filteredLogs.map((l) => {
                  const isSelected = selectedLogId === l.id;
                  return (
                    <div
                      key={l.id}
                      onClick={() => setSelectedLogId(l.id)}
                      className={`p-3 cursor-pointer transition-colors text-left ${
                        isSelected
                          ? 'bg-surface-700/80 border-l-2 border-accent text-content-primary'
                          : 'hover:bg-surface-700/40 text-content-secondary'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-1">
                        <span
                          className={`rounded-full px-1.5 py-0.2 text-[10px] font-mono font-bold uppercase ${
                            l.level === 'Critical'
                              ? 'bg-danger text-content-primary border border-danger/40'
                              : l.level === 'Error'
                              ? 'bg-danger/20 text-danger border border-danger/30'
                              : l.level === 'Warning'
                              ? 'bg-warning/20 text-warning border border-warning/30'
                              : 'bg-surface-700 text-content-muted border border-line'
                          }`}
                        >
                          {l.level}
                        </span>
                        <span className="text-[10px] font-mono text-content-muted">
                          {new Date(l.timestamp).toLocaleTimeString('tr-TR')}
                        </span>
                      </div>
                      <p className="truncate text-xs font-semibold text-content-primary mt-1">
                        {l.source}
                      </p>
                      <p className="line-clamp-2 text-[11px] text-content-muted mt-0.5">
                        {l.message}
                      </p>
                      <div className="flex items-center justify-between mt-1.5 text-[10px] font-mono text-content-muted">
                        <span className="truncate max-w-[120px]">{l.path || '/api'}</span>
                        {l.resolved && (
                          <span className="text-content-secondary font-medium flex items-center gap-0.5">
                            <CheckCheck className="h-3 w-3" /> Çözüldü
                          </span>
                        )}
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}

        {activeTab === 'feedbacks' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            {/* Filter by Category */}
            <div className="p-3 border-b border-line">
              <div className="flex items-center gap-1 overflow-x-auto scrollbar-none">
                {['all', 'bug', 'idea', 'general'].map((cat) => (
                  <button
                    key={cat}
                    onClick={() => setFeedbackCategoryFilter(cat)}
                    className={`rounded-full px-2.5 py-0.5 text-[10px] font-mono uppercase tracking-wider transition-colors cursor-pointer whitespace-nowrap ${
                      feedbackCategoryFilter === cat
                        ? 'bg-accent text-accent-on font-bold'
                        : 'bg-surface-700 text-content-muted hover:text-content-secondary border border-line'
                    }`}
                  >
                    {cat}
                  </button>
                ))}
              </div>
            </div>

            {/* Feedbacks Scrollable List */}
            <div className="flex-1 overflow-y-auto divide-y divide-line/60">
              {filteredFeedbacks.length === 0 ? (
                <div className="p-6 text-center text-xs text-content-muted">
                  Geri bildirim bileti bulunamadı.
                </div>
              ) : (
                filteredFeedbacks.map((f) => {
                  const isSelected = selectedFeedbackId === f.id;
                  return (
                    <div
                      key={f.id}
                      onClick={() => setSelectedFeedbackId(f.id)}
                      className={`p-3 cursor-pointer transition-colors text-left ${
                        isSelected
                          ? 'bg-surface-700/80 border-l-2 border-accent'
                          : 'hover:bg-surface-700/40'
                      }`}
                    >
                      <div className="flex items-center justify-between gap-1">
                        <span
                          className={`rounded-full px-2 py-0.5 text-[10px] font-mono font-bold uppercase ${
                            f.category.toLowerCase() === 'bug'
                              ? 'bg-danger/20 text-danger border border-danger/30'
                              : f.category.toLowerCase() === 'idea'
                              ? 'bg-surface-700 text-content-primary border border-line'
                              : 'bg-surface-600 text-content-primary border border-line'
                          }`}
                        >
                          {f.category}
                        </span>
                        <span className="text-[10px] font-mono text-content-muted">
                          {new Date(f.createdAt).toLocaleDateString('tr-TR')}
                        </span>
                      </div>
                      <p className="mt-1.5 line-clamp-2 text-xs text-content-secondary">
                        {f.message}
                      </p>
                      <div className="mt-1.5 flex items-center justify-between text-[10px] font-mono text-content-muted">
                        <span className="truncate max-w-[150px]">{f.email || 'Anonim'}</span>
                        <span>#{f.id}</span>
                      </div>
                    </div>
                  );
                })
              )}
            </div>
          </div>
        )}

        {activeTab === 'system' && (
          <div className="flex-1 overflow-y-auto p-4 space-y-3 text-xs">
            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3.5 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                Otonom Denetim Botu
              </span>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">Durum:</span>
                <span className="font-medium text-content-primary">
                  {botReport?.overallStatus || 'Healthy'}
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">Sağlık Skoru:</span>
                <span className="font-mono font-bold text-content-primary">
                  %{botReport?.healthScore ?? 100}
                </span>
              </div>
              <button
                onClick={handleRunBotAudit}
                disabled={runningAudit}
                className="w-full mt-2 flex items-center justify-center gap-1.5 rounded-[var(--radius-control)] bg-accent px-3 py-1.5 text-xs font-bold text-accent-on transition-colors hover:bg-accent-hover cursor-pointer"
              >
                <RefreshCw className={`h-3 w-3 ${runningAudit ? 'animate-spin' : ''}`} />
                {runningAudit ? 'Taranıyor...' : 'Botu Şimdi Çalıştır'}
              </button>
            </div>

            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3 space-y-1.5">
              <span className="text-[10px] font-mono text-content-muted uppercase">Framework</span>
              <p className="font-semibold text-content-primary">.NET 8.0 ASP.NET Core</p>
            </div>
            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3 space-y-1.5">
              <span className="text-[10px] font-mono text-content-muted uppercase">Frontend</span>
              <p className="font-semibold text-content-primary">Next.js 16 + React 19</p>
            </div>
            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3 space-y-1.5">
              <span className="text-[10px] font-mono text-content-muted uppercase">Host Altyapısı</span>
              <p className="font-semibold text-content-primary">Oracle Cloud + Vercel</p>
            </div>
          </div>
        )}
      </section>

      {/* ─────────────────────────────────────────────────────────────
          3. ORTA PANEL: Sağdan Soldan Paddingli Çalışma Alanı
          (Polaris Center Workspace px-6 sm:px-10 max-w-6xl)
          ───────────────────────────────────────────────────────────── */}
      <main className="flex-1 min-w-0 overflow-y-auto px-6 sm:px-10 py-8">
        <div className="mx-auto max-w-6xl space-y-6">
          {/* Top Breadcrumb & Account Bar */}
          <header className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between pb-4 border-b border-line">
            <div>
              <div className="flex items-center gap-2 text-xs font-mono text-content-muted">
                <span>{locale === 'en' ? 'Management' : 'Yönetim'}</span>
                <span>/</span>
                <span className="text-content-primary capitalize">
                  {activeTab === 'overview' && t.tabOverview}
                  {activeTab === 'users' && t.tabUsers}
                  {activeTab === 'tokens' && t.tabTokens}
                  {activeTab === 'sessions' && t.tabSessions}
                  {activeTab === 'projects' && t.tabProjects}
                  {activeTab === 'logs' && t.tabLogs}
                  {activeTab === 'feedbacks' && t.tabFeedbacks}
                  {activeTab === 'system' && t.tabSystem}
                </span>
              </div>
              <h1 className="text-lg font-bold tracking-tight text-content-primary mt-1">
                {activeTab === 'overview' && (locale === 'en' ? 'Operational Overview & Metrics' : 'Operasyonel Genel Bakış & Metrikler')}
                {activeTab === 'users' && (locale === 'en' ? 'Member Accounts & Quota Management' : 'Üye Hesapları & Kota Yönetimi')}
                {activeTab === 'tokens' && (locale === 'en' ? 'AI Token Consumption, Models & Server Distribution' : 'Yapay Zeka Tüketimi, Modeller & Sunucu Dağılımı')}
                {activeTab === 'sessions' && (locale === 'en' ? 'User Sessions, Navigation Paths & Module Interaction' : 'Kullanıcı Oturumları, Gezinme Yolları & Modül Etkileşimi')}
                {activeTab === 'projects' && (locale === 'en' ? 'Database Projects, Table Counts & Storage' : 'Veritabanı Projeleri, Tablo Sayıları & Depolama')}
                {activeTab === 'logs' && (locale === 'en' ? 'System Error Logs & Exception Tracking' : 'Sistem Hata Günlüğü & İstisna Takibi')}
                {activeTab === 'feedbacks' && (locale === 'en' ? 'Feedback & Inquiries Inbox' : 'Geri Bildirim & İstek Gelen Kutusu')}
                {activeTab === 'system' && (locale === 'en' ? 'Autonomous System Audit Bot & Live Infrastructure Diagnostics' : 'Otonom Sistem Denetim Botu & Canlı Altyapı Teşhisi')}
              </h1>
            </div>

            <div className="flex items-center gap-2.5 sm:gap-3">
              {/* FAZ 4: Dışa Aktar Dropdown (JSON + CSV) */}
              <div className="relative inline-block text-left">
                <button
                  onClick={() => setExportDropdownOpen(!exportDropdownOpen)}
                  title={t.export}
                  className="flex items-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-800 px-2.5 py-1.5 text-xs font-medium text-content-secondary hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer shadow-xs"
                >
                  <FileDown className="h-3.5 w-3.5" />
                  <span className="hidden sm:inline">{t.export}</span>
                  <ChevronDown className="h-3 w-3 text-content-muted" />
                </button>
                {exportDropdownOpen && (
                  <>
                    <div
                      className="fixed inset-0 z-40"
                      onClick={() => setExportDropdownOpen(false)}
                    />
                    <div className="absolute right-0 mt-1.5 w-64 rounded-[var(--radius-card)] border border-line bg-surface-800 shadow-xl z-50 py-1.5 text-xs">
                      {/* Section 1: Raporlar */}
                      <div className="px-3 py-1 text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                        {t.reportsAndSummary}
                      </div>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleExportJson();
                        }}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer"
                      >
                        <FileDown className="h-3.5 w-3.5 text-accent-text shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.jsonReport}</div>
                          <div className="text-[10px] text-content-muted">{t.jsonReportDesc}</div>
                        </div>
                      </button>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleCopySystemSnapshot();
                        }}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer"
                      >
                        <Copy className="h-3.5 w-3.5 text-accent-text shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.systemStatusMd}</div>
                          <div className="text-[10px] text-content-muted">{t.systemStatusMdDesc}</div>
                        </div>
                      </button>

                      <div className="h-px bg-line my-1.5" />

                      {/* Section 2: Kullanıcı & Kullanım */}
                      <div className="px-3 py-1 text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                        {t.userAndUsageCsv}
                      </div>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleExportCsv('users');
                        }}
                        disabled={exportingCsv === 'users'}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer disabled:opacity-50"
                      >
                        <FileSpreadsheet className="h-3.5 w-3.5 text-content-muted shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.membersAndQuotas}</div>
                          <div className="text-[10px] text-content-muted">{t.membersAndQuotasDesc}</div>
                        </div>
                      </button>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleExportCsv('sessions');
                        }}
                        disabled={exportingCsv === 'sessions'}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer disabled:opacity-50"
                      >
                        <FileSpreadsheet className="h-3.5 w-3.5 text-content-muted shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.sessionsAndJourney}</div>
                          <div className="text-[10px] text-content-muted">{t.sessionsAndJourneyDesc}</div>
                        </div>
                      </button>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleExportCsv('feedbacks');
                        }}
                        disabled={exportingCsv === 'feedbacks'}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer disabled:opacity-50"
                      >
                        <FileSpreadsheet className="h-3.5 w-3.5 text-content-muted shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.feedbacksCsv}</div>
                          <div className="text-[10px] text-content-muted">{t.feedbacksCsvDesc}</div>
                        </div>
                      </button>

                      <div className="h-px bg-line my-1.5" />

                      {/* Section 3: Sistem & Veritabanı */}
                      <div className="px-3 py-1 text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                        {t.systemAndInfraCsv}
                      </div>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleExportCsv('projects');
                        }}
                        disabled={exportingCsv === 'projects'}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer disabled:opacity-50"
                      >
                        <FileSpreadsheet className="h-3.5 w-3.5 text-content-muted shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.projectsAndStorage}</div>
                          <div className="text-[10px] text-content-muted">{t.projectsAndStorageDesc}</div>
                        </div>
                      </button>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleExportCsv('logs');
                        }}
                        disabled={exportingCsv === 'logs'}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer disabled:opacity-50"
                      >
                        <FileSpreadsheet className="h-3.5 w-3.5 text-content-muted shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.errorAndSystemLogs}</div>
                          <div className="text-[10px] text-content-muted">{t.errorAndSystemLogsDesc}</div>
                        </div>
                      </button>
                      <button
                        onClick={() => {
                          setExportDropdownOpen(false);
                          handleExportCsv('tokens');
                        }}
                        disabled={exportingCsv === 'tokens'}
                        className="flex w-full items-center gap-2.5 px-3 py-1.5 text-left text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer disabled:opacity-50"
                      >
                        <FileSpreadsheet className="h-3.5 w-3.5 text-content-muted shrink-0" />
                        <div>
                          <div className="font-medium text-content-primary">{t.dailyTokenStream}</div>
                          <div className="text-[10px] text-content-muted">{t.dailyTokenStreamDesc}</div>
                        </div>
                      </button>
                    </div>
                  </>
                )}
              </div>
              <LanguageToggle />
              <ThemeToggleButton />
              <div className="h-5 w-px bg-line" />
              <div className="flex items-center gap-2 pl-0.5">
                <div className="flex h-8 w-8 items-center justify-center rounded-full bg-accent text-xs font-mono font-bold uppercase text-accent-on shadow-sm">
                  {user?.username ? user.username.substring(0, 2).toUpperCase() : 'AD'}
                </div>
                <div className="hidden sm:flex flex-col text-left">
                  <span className="text-xs font-semibold text-content-primary leading-tight max-w-[140px] truncate" title={user?.username || user?.email}>
                    {user?.username || user?.email || 'Admin'}
                  </span>
                  <span className="text-[10px] font-mono text-accent-text font-bold leading-tight">
                    {user?.isDev || user?.email === 'namines@namines.com' ? 'SuperAdmin' : 'Admin'}
                  </span>
                </div>
              </div>
            </div>
          </header>

          {/* TAB 1: GENEL BAKIŞ (OVERVIEW) */}
          {activeTab === 'overview' && (
            <div className="space-y-6">
              {/* 6 Polaris KPI Stat Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">{t.totalUsersKpi}</span>
                    <Users className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalUsers ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    {locale === 'en' ? 'Active database accounts' : 'Aktif veritabanı hesapları'}
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">{locale === 'en' ? 'Total Cloud Projects' : 'Toplam Proje Sayısı'}</span>
                    <Database className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalProjects ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    {locale === 'en' ? 'Generated & managed schemas' : 'Üretilen ve yönetilen şemalar'}
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">{locale === 'en' ? 'Total Tables' : 'Toplam Tablo Sayısı'}</span>
                    <Layers className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {projectsData?.totalTables ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    {locale === 'en' ? 'System-wide entity count' : 'Sistem genelindeki varlık sayısı'}
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">{locale === 'en' ? 'Storage Volume' : 'Depolama Hacmi'}</span>
                    <HardDrive className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {projectsData?.totalStorageMb ?? 0} MB
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    {locale === 'en' ? 'Backups & database footprint' : 'Yedekler ve veritabanı alanı'}
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">{t.todayTokensKpi}</span>
                    <Zap className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalQuotaUsedToday ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    {locale === 'en' ? 'Total AI calls consumed today' : 'Bugün harcanan toplam AI çağrısı'}
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">{t.feedbacksKpi}</span>
                    <MessageSquareQuote className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalFeedbacks ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    {locale === 'en' ? 'User bug & suggestion tickets' : 'Kullanıcı hata ve öneri biletleri'}
                  </div>
                </div>
              </div>

              {/* Plan Distribution Card */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h2 className="text-sm font-bold text-content-primary">{locale === 'en' ? 'Subscription & Plan Distribution' : 'Abonelik & Plan Dağılımı'}</h2>
                    <p className="text-xs text-content-muted mt-0.5">
                      {locale === 'en' ? 'Active tier privileges across registered users' : 'Sistemdeki kullanıcıların aktif plan ve ayrıcalık dağılımı'}
                    </p>
                  </div>
                  <Award className="h-5 w-5 text-accent-text" />
                </div>

                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3 pt-2">
                  <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3.5">
                    <span className="text-[11px] font-mono text-content-muted uppercase">Free Plan</span>
                    <div className="text-xl font-bold text-content-primary mt-1">
                      {overviewStats?.plans.free ?? 0}
                    </div>
                    <span className="text-[10px] text-content-muted">{locale === 'en' ? '15 calls/day' : '15 istek/gün'}</span>
                  </div>

                  <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3.5">
                    <span className="text-[11px] font-mono text-content-muted uppercase">Pro Solo</span>
                    <div className="text-xl font-bold text-content-primary mt-1">
                      {overviewStats?.plans.pro ?? 0}
                    </div>
                    <span className="text-[10px] text-content-muted">{locale === 'en' ? '100 calls/day' : '100 istek/gün'}</span>
                  </div>

                  <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3.5">
                    <span className="text-[11px] font-mono text-content-muted uppercase">Team</span>
                    <div className="text-xl font-bold text-content-primary mt-1">
                      {overviewStats?.plans.team ?? 0}
                    </div>
                    <span className="text-[10px] text-content-muted">500 istek/gün</span>
                  </div>

                  <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3.5">
                    <span className="text-[11px] font-mono text-accent-text uppercase">Dev / Admin</span>
                    <div className="text-xl font-bold text-accent-text mt-1">
                      {overviewStats?.plans.dev ?? 0}
                    </div>
                    <span className="text-[10px] text-accent-text">Sınırsız</span>
                  </div>
                </div>
              </div>

              {/* 2-Column Summary Cards */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                {/* AI Models Overview */}
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                  <div className="flex items-center justify-between">
                    <h3 className="text-sm font-bold text-content-primary">Model Kullanım Dağılımı</h3>
                    <Sparkles className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="space-y-3">
                    {tokensStats?.modelDistribution ? (
                      Object.entries(tokensStats.modelDistribution).map(([model, count]) => {
                        const total = Object.values(tokensStats.modelDistribution).reduce((a, b) => a + b, 0) || 1;
                        const pct = Math.round((count / total) * 100);
                        return (
                          <div key={model} className="space-y-1">
                            <div className="flex justify-between text-xs">
                              <span className="text-content-secondary font-medium">{model}</span>
                              <span className="font-mono text-content-primary">%{pct} ({count})</span>
                            </div>
                            <div className="h-1.5 w-full rounded-full bg-surface-700 overflow-hidden">
                              <div className="h-full bg-accent transition-all duration-300" style={{ width: `${pct}%` }} />
                            </div>
                          </div>
                        );
                      })
                    ) : (
                      <div className="text-center text-xs text-content-muted py-4">Veri yükleniyor...</div>
                    )}
                  </div>
                </div>

                {/* Popular DB Servers */}
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                  <div className="flex items-center justify-between">
                    <h3 className="text-sm font-bold text-content-primary">En Çok Kullanılan Sunucular</h3>
                    <Server className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="space-y-3">
                    {tokensStats?.serverDistribution ? (
                      Object.entries(tokensStats.serverDistribution).map(([server, count]) => {
                        const total = Object.values(tokensStats.serverDistribution).reduce((a, b) => a + b, 0) || 1;
                        const pct = Math.round((count / total) * 100);
                        return (
                          <div key={server} className="space-y-1">
                            <div className="flex justify-between text-xs">
                              <span className="text-content-secondary font-medium uppercase">{server}</span>
                              <span className="font-mono text-content-primary">%{pct} ({count})</span>
                            </div>
                            <div className="h-1.5 w-full rounded-full bg-surface-700 overflow-hidden">
                              <div className="h-full bg-accent transition-all duration-300" style={{ width: `${pct}%` }} />
                            </div>
                          </div>
                        );
                      })
                    ) : (
                      <div className="text-center text-xs text-content-muted py-4">Veri yükleniyor...</div>
                    )}
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* TAB 2: ÜYELER (USERS & QUOTAS) */}
          {activeTab === 'users' && (
            <div className="space-y-6">
              {loadingUserDetail ? (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-12 text-center text-xs text-content-muted">
                  Kullanıcı verileri yükleniyor...
                </div>
              ) : selectedUserDetail ? (
                <>
                  {/* User Profile Header Card */}
                  <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs">
                    <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
                      <div className="flex items-center gap-4">
                        <div className="flex h-12 w-12 items-center justify-center rounded-full bg-accent text-accent-on font-mono font-bold text-sm">
                          {selectedUserDetail.userName.substring(0, 2).toUpperCase()}
                        </div>
                        <div>
                          <div className="flex items-center gap-2">
                            <h2 className="text-base font-bold text-content-primary">
                              {selectedUserDetail.userName}
                            </h2>
                            {selectedUserDetail.isDev && (
                              <span className="rounded-full bg-accent-subtle border border-line px-2 py-0.5 text-[10px] font-mono font-bold text-accent-text">
                                Dev Admin
                              </span>
                            )}
                          </div>
                          <p className="text-xs text-content-muted mt-0.5">{selectedUserDetail.email}</p>
                          {selectedUserDetail.companyName && (
                            <p className="text-[11px] text-content-secondary mt-0.5">
                              Şirket: {selectedUserDetail.companyName}
                            </p>
                          )}
                        </div>
                      </div>

                      <div className="flex flex-wrap items-center gap-2 text-xs font-mono text-content-muted">
                        <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line px-3 py-1.5">
                          Kayıt: {new Date(selectedUserDetail.createdAt).toLocaleDateString('tr-TR')}
                        </div>
                        <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line px-3 py-1.5">
                          ID: {selectedUserDetail.id.substring(0, 8)}...
                        </div>
                        <button
                          onClick={() => handleOpenUserSessions(selectedUserDetail.id)}
                          className="flex items-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-900 px-3 py-1.5 text-xs font-medium text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
                        >
                          <Compass className="h-3.5 w-3.5 text-accent-text" />
                          <span>Oturum & Gezinme Yolculuğu</span>
                        </button>
                      </div>
                    </div>
                  </div>

                  {/* Interventions Grid */}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                    {/* Plan Interventions */}
                    <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                      <div className="flex items-center justify-between">
                        <h3 className="text-sm font-bold text-content-primary">Abonelik & Plan Değiştir</h3>
                        <UserCheck className="h-4 w-4 text-accent-text" />
                      </div>
                      <p className="text-xs text-content-muted leading-relaxed">
                        Kullanıcının planını anında yükseltin veya dev ayrıcalığı tanıyın. Kota limitleri
                        otomatik yeniden hesaplanır.
                      </p>

                      <div className="space-y-3 pt-2">
                        <label className="text-xs font-medium text-content-secondary">
                          Aktif Paket Seçimi:
                        </label>
                        <Select
                          value={editPlanCode}
                          onChange={(e) => setEditPlanCode(e.target.value)}
                          options={[
                            { value: 'free', label: 'Free Plan (15 istek / gün)' },
                            { value: 'pro', label: 'Pro Solo (100 istek / gün)' },
                            { value: 'team', label: 'Team Plan (500 istek / gün)' },
                            { value: 'dev', label: 'Developer / SuperAdmin (Sınırsız)' },
                          ]}
                        />

                        <button
                          onClick={handleUpdatePlan}
                          disabled={updatingPlan}
                          className="w-full flex items-center justify-center gap-2 rounded-[var(--radius-control)] bg-accent px-4 py-2 text-xs font-bold text-accent-on transition-colors hover:bg-accent-hover cursor-pointer disabled:opacity-50"
                        >
                          <Sparkles className="h-3.5 w-3.5" />
                          {updatingPlan ? 'Güncelleniyor...' : 'Planı Kaydet & Uygula'}
                        </button>
                      </div>
                    </div>

                    {/* Quota Interventions */}
                    <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                      <div className="flex items-center justify-between">
                        <h3 className="text-sm font-bold text-content-primary">Günlük Kota & Müdahale</h3>
                        <Zap className="h-4 w-4 text-accent-text" />
                      </div>
                      <p className="text-xs text-content-muted leading-relaxed">
                        Kullanıcının günlük harcama sayacını sıfırlayın veya acil durum bonus kredisi ekleyin.
                      </p>

                      <div className="space-y-3 pt-2">
                        <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3.5">
                          <div className="flex justify-between text-xs text-content-secondary mb-1">
                            <span>Günlük Tüketim:</span>
                            <span className="font-mono font-bold text-content-primary">
                              {selectedUserDetail.dailyUsageCount} / {selectedUserDetail.dailyLimit}
                            </span>
                          </div>
                          <div className="h-2 w-full rounded-full bg-surface-700 overflow-hidden">
                            <div
                              className="h-full bg-accent transition-all duration-300"
                              style={{
                                width: `${Math.min(
                                  100,
                                  (selectedUserDetail.dailyUsageCount /
                                    Math.max(1, selectedUserDetail.dailyLimit)) *
                                    100
                                )}%`,
                              }}
                            />
                          </div>
                          <div className="mt-2 text-[10px] text-content-muted font-mono">
                            Son Sıfırlanma: {selectedUserDetail.lastResetDate}
                          </div>
                        </div>

                        <div className="grid grid-cols-2 gap-2">
                          <button
                            onClick={() => handleResetQuota(true, 0)}
                            disabled={resettingQuota}
                            className="flex items-center justify-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-700/60 px-3 py-2 text-xs font-medium text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
                          >
                            <RefreshCw className="h-3.5 w-3.5" />
                            Kotayı Sıfırla (0)
                          </button>
                          <button
                            onClick={() => handleResetQuota(false, 100)}
                            disabled={resettingQuota}
                            className="flex items-center justify-center gap-1.5 rounded-[var(--radius-control)] bg-accent px-3 py-2 text-xs font-bold text-accent-on hover:bg-accent-hover transition-colors cursor-pointer"
                          >
                            <Zap className="h-3.5 w-3.5" />
                            +100 Bonus Token
                          </button>
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Owned Projects List */}
                  <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                    <div className="flex items-center justify-between">
                      <h3 className="text-sm font-bold text-content-primary">
                        Kullanıcının Projeleri ({selectedUserDetail.projects.length})
                      </h3>
                      <HardDrive className="h-4 w-4 text-content-muted" />
                    </div>

                    {selectedUserDetail.projects.length === 0 ? (
                      <p className="text-xs text-content-muted py-4 text-center">
                        Bu kullanıcı henüz bir veritabanı projesi oluşturmamış.
                      </p>
                    ) : (
                      <div className="divide-y divide-line/60 rounded-[var(--radius-control)] border border-line bg-surface-900 overflow-hidden">
                        {selectedUserDetail.projects.map((proj) => (
                          <div
                            key={proj.id}
                            className="flex items-center justify-between p-3 text-xs"
                          >
                            <div className="flex items-center gap-2.5">
                              <Database className="h-3.5 w-3.5 text-accent-text" />
                              <span className="font-semibold text-content-primary">{proj.name}</span>
                              <span className="rounded-full bg-surface-700 border border-line px-2 py-0.2 text-[10px] font-mono text-content-muted uppercase">
                                {proj.dbType}
                              </span>
                            </div>
                            <span className="text-[10px] font-mono text-content-muted">
                              ID: {proj.id}
                            </span>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>
                </>
              ) : (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-12 text-center text-xs text-content-muted">
                  Sol panelden bir üye seçerek detayları, projeleri ve kota müdahalelerini görüntüleyin.
                </div>
              )}
            </div>
          )}

          {/* TAB 3: TOKENLAR (AI TOKENS & MODELS) */}
          {activeTab === 'tokens' && (
            <div className="space-y-6">
              {/* 4 KPI Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Toplam AI İstekleri</span>
                    <Sparkles className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {tokensStats?.timeline?.reduce((acc, t) => acc + t.requests, 0) ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Son {tokenDaysFilter} günde yapılan çağrılar
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Gelişmiş Prompt Oranı</span>
                    <Award className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    %{tokensStats?.advancedPreferences?.ratioPercent ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    {tokensStats?.advancedPreferences?.advancedCount ?? 0} / {tokensStats?.advancedPreferences?.totalPrompts ?? 0} prompt
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">En Çok Tercih Edilen Model</span>
                    <Zap className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-lg font-bold tracking-tight text-content-primary truncate">
                    {tokensStats?.modelDistribution ? Object.keys(tokensStats.modelDistribution)[0] || 'NAI Standard' : '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    En yüksek istek hacmi
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">En Çok Tercih Edilen Server</span>
                    <Server className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-lg font-bold tracking-tight text-content-primary uppercase truncate">
                    {tokensStats?.serverDistribution ? Object.keys(tokensStats.serverDistribution)[0] || 'PostgreSQL' : '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Lider veritabanı hedefi
                  </div>
                </div>
              </div>

              {/* Timeline Chart Box with Custom Date Range & Daily/Monthly Interval Filter */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex flex-col md:flex-row md:items-center md:justify-between gap-3 pb-3 border-b border-line">
                  <div>
                    <h2 className="text-sm font-bold text-content-primary">
                      {tokenInterval === 'monthly' ? 'Aylık' : 'Günlük'} AI İstek & Token Zaman Çizelgesi
                    </h2>
                    <p className="text-xs text-content-muted mt-0.5">
                      {tokenPreset === 'custom'
                        ? `${tokenStartDate || 'Başlangıç'} - ${tokenEndDate || 'Bitiş'} aralığı`
                        : `Son ${tokenDaysFilter} gündeki`} çağrı yoğunluğu ve harcanan token hacmi
                    </p>
                  </div>

                  <div className="flex flex-wrap items-center gap-2">
                    {/* Interval Toggle */}
                    <div className="flex rounded-[var(--radius-control)] border border-line bg-surface-900 p-0.5 text-xs">
                      <button
                        onClick={() => {
                          setTokenInterval('daily');
                          handleApplyTokenFilter(tokenPreset, tokenStartDate, tokenEndDate, 'daily');
                        }}
                        className={`rounded-[var(--radius-control)] px-2.5 py-1 text-[11px] font-medium transition-colors cursor-pointer ${
                          tokenInterval === 'daily'
                            ? 'bg-accent text-accent-on font-bold shadow-xs'
                            : 'text-content-muted hover:text-content-primary'
                        }`}
                      >
                        Günlük
                      </button>
                      <button
                        onClick={() => {
                          setTokenInterval('monthly');
                          handleApplyTokenFilter(tokenPreset, tokenStartDate, tokenEndDate, 'monthly');
                        }}
                        className={`rounded-[var(--radius-control)] px-2.5 py-1 text-[11px] font-medium transition-colors cursor-pointer ${
                          tokenInterval === 'monthly'
                            ? 'bg-accent text-accent-on font-bold shadow-xs'
                            : 'text-content-muted hover:text-content-primary'
                        }`}
                      >
                        Aylık
                      </button>
                    </div>

                    {/* Presets */}
                    <div className="flex items-center gap-1">
                      {(['7d', '14d', '30d', '90d', 'custom'] as const).map((preset) => (
                        <button
                          key={preset}
                          onClick={() => {
                            if (preset === 'custom') {
                              setTokenPreset('custom');
                            } else {
                              handleApplyTokenFilter(preset);
                            }
                          }}
                          className={`rounded-[var(--radius-control)] px-2 py-1 text-[11px] font-mono transition-colors cursor-pointer ${
                            tokenPreset === preset
                              ? 'bg-surface-700 text-content-primary border border-line-strong font-bold'
                              : 'bg-surface-900 text-content-muted hover:text-content-secondary border border-line'
                          }`}
                        >
                          {preset === '7d' && '7 Gün'}
                          {preset === '14d' && '14 Gün'}
                          {preset === '30d' && '30 Gün'}
                          {preset === '90d' && '90 Gün'}
                          {preset === 'custom' && 'Özel'}
                        </button>
                      ))}
                    </div>
                  </div>
                </div>

                {/* Custom Date Inputs if 'custom' is active */}
                {tokenPreset === 'custom' && (
                  <div className="flex flex-wrap items-center gap-3 p-3 rounded-[var(--radius-control)] border border-line bg-surface-900 text-xs">
                    <div className="flex items-center gap-1.5">
                      <Calendar className="h-3.5 w-3.5 text-accent-text" />
                      <span className="text-content-muted">Başlangıç:</span>
                      <input
                        type="date"
                        value={tokenStartDate}
                        onChange={(e) => setTokenStartDate(e.target.value)}
                        className="rounded-[var(--radius-control)] border border-line bg-surface-800 px-2 py-1 text-content-primary font-mono text-[11px] focus:outline-none focus:border-line-strong"
                      />
                    </div>
                    <div className="flex items-center gap-1.5">
                      <span className="text-content-muted">Bitiş:</span>
                      <input
                        type="date"
                        value={tokenEndDate}
                        onChange={(e) => setTokenEndDate(e.target.value)}
                        className="rounded-[var(--radius-control)] border border-line bg-surface-800 px-2 py-1 text-content-primary font-mono text-[11px] focus:outline-none focus:border-line-strong"
                      />
                    </div>
                    <button
                      onClick={() => handleApplyTokenFilter('custom', tokenStartDate, tokenEndDate, tokenInterval)}
                      disabled={loadingTokens || (!tokenStartDate && !tokenEndDate)}
                      className="rounded-[var(--radius-control)] bg-accent px-3 py-1 text-[11px] font-bold text-accent-on hover:bg-accent-hover transition-colors cursor-pointer disabled:opacity-50"
                    >
                      {loadingTokens ? 'Yükleniyor...' : 'Filtreyi Uygula'}
                    </button>
                  </div>
                )}

                <div className="pt-4">
                  {tokensStats?.timeline && tokensStats.timeline.length > 0 ? (
                    <div className="flex items-end gap-2 h-44 w-full pt-6 pb-2 px-2 border-b border-line overflow-x-auto">
                      {tokensStats.timeline.map((item, idx) => {
                        const maxVal = Math.max(...tokensStats.timeline.map((t) => t.requests), 1);
                        const heightPct = Math.max(12, Math.round((item.requests / maxVal) * 100));
                        return (
                          <div key={idx} className="flex-1 flex flex-col items-center gap-1.5 min-w-[28px] group">
                            <span className="text-[10px] font-mono text-content-muted opacity-0 group-hover:opacity-100 transition-opacity">
                              {item.requests}
                            </span>
                            <div
                              className="w-full rounded-t-[var(--radius-control)] bg-accent hover:bg-accent-hover transition-all cursor-pointer"
                              style={{ height: `${heightPct}%` }}
                              title={`${item.date}: ${item.requests} istek, ${item.tokens} token`}
                            />
                            <span className="text-[10px] font-mono text-content-muted truncate max-w-[34px]">
                              {item.date.substring(5)}
                            </span>
                          </div>
                        );
                      })}
                    </div>
                  ) : (
                    <div className="text-center text-xs text-content-muted py-8">
                      Veri bulunamadı.
                    </div>
                  )}
                </div>
              </div>

              {/* Top Consumers Table */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h3 className="text-sm font-bold text-content-primary">En Çok Tüketen Kullanıcılar</h3>
                    <p className="text-xs text-content-muted mt-0.5">
                      Günlük kotasını en yoğun kullanan hesaplar ve aktif limitleri
                    </p>
                  </div>
                  <Users className="h-4 w-4 text-content-muted" />
                </div>

                <div className="divide-y divide-line/60 rounded-[var(--radius-control)] border border-line bg-surface-900 overflow-hidden">
                  <div className="grid grid-cols-12 p-3 text-[11px] font-mono uppercase text-content-muted bg-surface-850">
                    <span className="col-span-5">Kullanıcı</span>
                    <span className="col-span-3">Plan</span>
                    <span className="col-span-2">Günlük Harcama</span>
                    <span className="col-span-2 text-right">Limit</span>
                  </div>
                  {tokensStats?.topUsers?.map((u) => (
                    <div
                      key={u.id}
                      onClick={() => {
                        setSelectedUserId(u.id);
                        setActiveTab('users');
                      }}
                      className="grid grid-cols-12 items-center p-3 text-xs hover:bg-surface-800 cursor-pointer transition-colors"
                    >
                      <div className="col-span-5 min-w-0 pr-2">
                        <span className="font-semibold text-content-primary truncate block">{u.username || 'Üye'}</span>
                        <span className="text-[11px] text-content-muted truncate block">{u.email}</span>
                      </div>
                      <div className="col-span-3">
                        <span className="rounded-full bg-surface-700 border border-line px-2 py-0.5 text-[10px] font-mono uppercase text-content-muted">
                          {u.plan}
                        </span>
                      </div>
                      <div className="col-span-2 font-mono font-bold text-accent-text">
                        {u.dailyUsage}
                      </div>
                      <div className="col-span-2 text-right font-mono text-content-muted">
                        {u.dailyLimit}
                      </div>
                    </div>
                  ))}
                </div>
              </div>
            </div>
          )}

          {/* TAB 4: OTURUMLAR (SESSIONS & USER JOURNEYS) */}
          {activeTab === 'sessions' && (
            <div className="space-y-6">
              {/* 3 KPI Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Kayıtlı Toplam Etkinlik</span>
                    <Activity className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {sessionsData?.overview?.totalRecordedEvents ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Sistem telemetri tamponundaki etkileşim
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Ortalama Oturum Süresi</span>
                    <Clock className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {sessionsData?.sessions && sessionsData.sessions.length > 0
                      ? Math.round(
                          sessionsData.sessions.reduce((acc, s) => acc + s.durationMinutes, 0) /
                            sessionsData.sessions.length
                        )
                      : 0}{' '}
                    dk
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Aktif ziyaretçi oturumu başına
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">En Çok Ziyaret Edilen Modül</span>
                    <Compass className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-lg font-bold tracking-tight text-content-primary truncate">
                    /canvas (Modelleme Tuvali)
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Kullanıcıların en çok zaman geçirdiği alan
                  </div>
                </div>
              </div>

              {/* /new Start Modes & Module Clicks Breakdown */}
              <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
                {/* /new Start Mode Breakdown */}
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <h3 className="text-sm font-bold text-content-primary">/new Ekranı Başlangıç Tercihleri</h3>
                      <p className="text-xs text-content-muted mt-0.5">
                        Kullanıcılar yeni projeye ne ile başlıyor?
                      </p>
                    </div>
                    <PlayCircle className="h-4 w-4 text-accent-text" />
                  </div>

                  <div className="space-y-3 pt-2">
                    {sessionsData?.overview?.startModes ? (
                      Object.entries(sessionsData.overview.startModes).map(([mode, count]) => {
                        const total = Object.values(sessionsData.overview.startModes).reduce((a, b) => a + b, 0) || 1;
                        const pct = Math.round((count / total) * 100);
                        const labelMap: Record<string, string> = {
                          prompt: 'Doğrudan Prompt ile',
                          starter: 'Hazır Şablonlar (Starters)',
                          database: 'Canlı DB Bağlantısı (Reverse Eng.)',
                          code: 'SQL / Kod İçe Aktar',
                          json: 'JSON Şema İle',
                          canvas: 'Boş Tuval ile Başla',
                        };
                        return (
                          <div key={mode} className="space-y-1">
                            <div className="flex justify-between text-xs">
                              <span className="text-content-secondary font-medium">
                                {labelMap[mode] || mode}
                              </span>
                              <span className="font-mono text-content-primary">%{pct} ({count})</span>
                            </div>
                            <div className="h-1.5 w-full rounded-full bg-surface-700 overflow-hidden">
                              <div className="h-full bg-accent transition-all duration-300" style={{ width: `${pct}%` }} />
                            </div>
                          </div>
                        );
                      })
                    ) : (
                      <div className="text-center text-xs text-content-muted py-4">Veri yükleniyor...</div>
                    )}
                  </div>
                </div>

                {/* Module Clicks */}
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <h3 className="text-sm font-bold text-content-primary">Modül Tıklama ve Etkileşim Dağılımı</h3>
                      <p className="text-xs text-content-muted mt-0.5">
                        Modüllerde gerçekleşen toplam işlem ve tıklamalar
                      </p>
                    </div>
                    <BarChart3 className="h-4 w-4 text-accent-text" />
                  </div>

                  <div className="space-y-3 pt-2">
                    {sessionsData?.overview?.moduleClicks ? (
                      Object.entries(sessionsData.overview.moduleClicks).map(([mod, clicks]) => {
                        const total = Object.values(sessionsData.overview.moduleClicks).reduce((a, b) => a + b, 0) || 1;
                        const pct = Math.round((clicks / total) * 100);
                        return (
                          <div key={mod} className="space-y-1">
                            <div className="flex justify-between text-xs">
                              <span className="text-content-secondary font-medium font-mono">/{mod}</span>
                              <span className="font-mono text-content-primary">%{pct} ({clicks})</span>
                            </div>
                            <div className="h-1.5 w-full rounded-full bg-surface-700 overflow-hidden">
                              <div className="h-full bg-accent transition-all duration-300" style={{ width: `${pct}%` }} />
                            </div>
                          </div>
                        );
                      })
                    ) : (
                      <div className="text-center text-xs text-content-muted py-4">Veri yükleniyor...</div>
                    )}
                  </div>
                </div>
              </div>

              {/* Selected Session Details Card */}
              {selectedSession && (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                  <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-2 pb-3 border-b border-line">
                    <div>
                      <span className="text-xs font-mono text-content-muted">Oturum Detayı</span>
                      <h3 className="text-sm font-bold text-content-primary">
                        {selectedSession.userEmail || 'Anonim Oturum'}
                      </h3>
                    </div>
                    <div className="flex items-center gap-2 text-xs font-mono text-content-muted">
                      <span>Süre: {selectedSession.durationMinutes} dakika</span>
                      <span>·</span>
                      <span>Toplam Tıklama: {selectedSession.totalClicks}</span>
                    </div>
                  </div>

                  {/* Visited Pages Journey */}
                  <div className="space-y-2">
                    <span className="text-xs font-medium text-content-secondary">Ziyaret Edilen Sayfalar:</span>
                    <div className="flex flex-wrap items-center gap-2">
                      {selectedSession.pagesVisited?.map((page, idx) => (
                        <React.Fragment key={idx}>
                          <span className="rounded-[var(--radius-control)] border border-line bg-surface-900 px-2.5 py-1 text-xs font-mono text-content-primary">
                            {page}
                          </span>
                          {idx < selectedSession.pagesVisited.length - 1 && (
                            <ChevronRight className="h-3 w-3 text-content-muted" />
                          )}
                        </React.Fragment>
                      ))}
                    </div>
                  </div>

                  {/* Recent Actions */}
                  {selectedSession.recentActions && selectedSession.recentActions.length > 0 && (
                    <div className="space-y-2 pt-2">
                      <span className="text-xs font-medium text-content-secondary">Son Eylemler:</span>
                      <div className="divide-y divide-line/60 rounded-[var(--radius-control)] border border-line bg-surface-900 overflow-hidden">
                        {selectedSession.recentActions.map((act, idx) => (
                          <div key={idx} className="flex items-center justify-between p-2.5 text-xs">
                            <div className="flex items-center gap-2">
                              <span className="rounded-[var(--radius-control)] bg-accent-subtle/50 px-1.5 py-0.5 text-[10px] font-mono text-accent-text uppercase">
                                {act.module}
                              </span>
                              <span className="text-content-primary">{act.action}</span>
                            </div>
                            <span className="font-mono text-[10px] text-content-muted">
                              {new Date(act.timestamp).toLocaleTimeString('tr-TR')}
                            </span>
                          </div>
                        ))}
                      </div>
                    </div>
                  )}
                </div>
              )}
            </div>
          )}

          {/* TAB 5: PROJELER (PROJECTS & STORAGE) */}
          {activeTab === 'projects' && (
            <div className="space-y-6">
              {/* 4 KPI Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Toplam Proje</span>
                    <Database className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {projectsData?.totalProjects ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Oluşturulan tüm modeller
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Toplam Tablo</span>
                    <Layers className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {projectsData?.totalTables ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Modellenen varlıklar
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Depolama Boyutu</span>
                    <HardDrive className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {projectsData?.totalStorageMb ?? 0} MB
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Tahmini şema & disk hacmi
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Ortalama Tablo / Proje</span>
                    <BarChart3 className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {projectsData && projectsData.totalProjects > 0
                      ? Math.round(projectsData.totalTables / projectsData.totalProjects)
                      : 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Proje karmaşıklık ortalaması
                  </div>
                </div>
              </div>

              {/* Projects Table */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h3 className="text-sm font-bold text-content-primary">Tüm Projelerin Detaylı Dökümü</h3>
                    <p className="text-xs text-content-muted mt-0.5">
                      Sistemdeki tüm projeler, veritabanı motorları, tablo sayıları ve yedek durumları
                    </p>
                  </div>
                  <FolderGit2 className="h-5 w-5 text-accent-text" />
                </div>

                <div className="divide-y divide-line/60 rounded-[var(--radius-control)] border border-line bg-surface-900 overflow-hidden">
                  <div className="grid grid-cols-12 p-3 text-[11px] font-mono uppercase text-content-muted bg-surface-850">
                    <span className="col-span-4">Proje & Sahip</span>
                    <span className="col-span-2">Motor</span>
                    <span className="col-span-2">Tablo</span>
                    <span className="col-span-2">Depolama & Yedek</span>
                    <span className="col-span-2 text-right">Eylem</span>
                  </div>

                  {filteredProjects.length === 0 ? (
                    <div className="p-8 text-center text-xs text-content-muted">
                      Kriterlere uygun proje bulunamadı.
                    </div>
                  ) : (
                    filteredProjects.map((p) => (
                      <div
                        key={p.id}
                        className="grid grid-cols-12 items-center p-3 text-xs hover:bg-surface-800 transition-colors"
                      >
                        <div className="col-span-4 min-w-0 pr-2">
                          <span className="font-semibold text-content-primary truncate block">{p.name}</span>
                          <span className="text-[11px] text-content-muted truncate block">{p.ownerEmail}</span>
                        </div>
                        <div className="col-span-2">
                          <span className="rounded-full bg-surface-700 border border-line px-2 py-0.5 text-[10px] font-mono uppercase text-content-muted">
                            {p.dbType}
                          </span>
                        </div>
                        <div className="col-span-2 font-mono text-content-primary font-medium">
                          {p.tableCount} tablo
                        </div>
                        <div className="col-span-2 font-mono text-[11px]">
                          <span className="text-content-muted">{p.estimatedMb} MB</span>
                          <span className="text-content-muted mx-1">·</span>
                          {p.vaultBackupsCount > 0 ? (
                            <span className="text-content-secondary font-medium">{p.vaultBackupsCount} Yedek</span>
                          ) : (
                            <span className="text-content-muted">Yedeksiz</span>
                          )}
                        </div>
                        <div className="col-span-2 flex items-center justify-end">
                          <button
                            onClick={() => handleOpenProjectDeepStats(p.id)}
                            className="flex items-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-700/60 px-2.5 py-1 text-[11px] font-medium text-content-primary hover:bg-surface-700 hover:text-accent-text transition-colors cursor-pointer shadow-xs"
                          >
                            <Box className="h-3 w-3 text-accent-text" />
                            <span>Modül Detayları</span>
                          </button>
                        </div>
                      </div>
                    ))
                  )}
                </div>
              </div>
            </div>
          )}

          {/* TAB 6: HATALAR & LOGLAR (FAZ 3: ERROR & EXCEPTION LOGGING) */}
          {activeTab === 'logs' && (
            <div className="space-y-6">
              {/* 4 KPI Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Toplam Hata</span>
                    <AlertOctagon className="h-4 w-4 text-danger" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-danger">
                    {logsStats?.totalErrors ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Kritik ve uygulama hataları
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Açık / Çözülmemiş</span>
                    <AlertTriangle className="h-4 w-4 text-warning" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-warning">
                    {logsStats?.unresolvedCount ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    İnceleme bekleyen istisnalar
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Sistem Uyarıları</span>
                    <Bug className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {logsStats?.totalWarnings ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Gecikme ve bağlantı düşüşleri
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Kayıtlı Olay</span>
                    <Terminal className="h-4 w-4 text-content-muted" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {logsStats?.totalRecorded ?? 0}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Tampondaki toplam girdi
                  </div>
                </div>
              </div>

              {/* Selected Log Detail Card */}
              {selectedLog ? (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                  <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 pb-3 border-b border-line">
                    <div className="flex items-center gap-3">
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-xs font-mono font-bold uppercase ${
                          selectedLog.level === 'Critical'
                            ? 'bg-danger text-content-primary border border-danger/40'
                            : selectedLog.level === 'Error'
                            ? 'bg-danger/20 text-danger border border-danger/30'
                            : selectedLog.level === 'Warning'
                            ? 'bg-warning/20 text-warning border border-warning/30'
                            : 'bg-surface-700 text-content-muted border border-line'
                        }`}
                      >
                        {selectedLog.level}
                      </span>
                      <span className="text-sm font-bold text-content-primary">
                        {selectedLog.source}
                      </span>
                      {selectedLog.resolved ? (
                        <span className="rounded-full bg-surface-700 text-content-primary border border-line px-2 py-0.2 text-[10px] font-mono">
                          Çözüldü
                        </span>
                      ) : (
                        <span className="rounded-full bg-danger/20 text-danger border border-danger/30 px-2 py-0.2 text-[10px] font-mono">
                          İnceleme Bekliyor
                        </span>
                      )}
                    </div>

                    <div className="flex items-center gap-2 text-xs font-mono text-content-muted">
                      <span>{new Date(selectedLog.timestamp).toLocaleString('tr-TR')}</span>
                    </div>
                  </div>

                  {/* Context Info */}
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs">
                    <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-1">
                      <span className="text-[10px] font-mono text-content-muted uppercase">İstek Rotası</span>
                      <p className="font-mono text-content-primary">{selectedLog.path || '/api'}</p>
                    </div>
                    <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 space-y-1">
                      <span className="text-[10px] font-mono text-content-muted uppercase">Kullanıcı Hesabı</span>
                      <p className="font-mono text-content-primary">{selectedLog.userEmail || 'Anonim / Sistem'}</p>
                    </div>
                  </div>

                  {/* Error Message */}
                  <div className="space-y-1.5">
                    <span className="text-xs font-medium text-content-secondary">Hata Mesajı:</span>
                    <div className="rounded-[var(--radius-card)] bg-surface-900 border border-line p-4 text-xs font-mono text-danger leading-relaxed">
                      {selectedLog.message}
                    </div>
                  </div>

                  {/* StackTrace */}
                  {selectedLog.stackTrace && (
                    <div className="space-y-1.5">
                      <span className="text-xs font-medium text-content-secondary">Yığın İzi (Stack Trace):</span>
                      <div className="rounded-[var(--radius-card)] bg-surface-900 border border-line p-4 text-[11px] font-mono text-content-muted leading-relaxed overflow-x-auto whitespace-pre">
                        {selectedLog.stackTrace}
                      </div>
                    </div>
                  )}

                  {/* Action Buttons */}
                  <div className="flex items-center justify-between pt-3 border-t border-line">
                    <button
                      onClick={handleClearLogs}
                      className="flex items-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-900 px-3 py-1.5 text-xs text-content-muted hover:text-danger hover:bg-surface-700 transition-colors cursor-pointer"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                      Tüm Logları Temizle
                    </button>

                    {!selectedLog.resolved && (
                      <button
                        onClick={() => handleResolveLog(selectedLog.id)}
                        disabled={resolvingLog}
                        className="flex items-center gap-1.5 rounded-[var(--radius-control)] bg-accent px-4 py-2 text-xs font-bold text-accent-on transition-colors hover:bg-accent-hover cursor-pointer disabled:opacity-50"
                      >
                        <CheckCheck className="h-3.5 w-3.5" />
                        {resolvingLog ? 'Güncelleniyor...' : 'Çözüldü Olarak İşaretle'}
                      </button>
                    )}
                  </div>
                </div>
              ) : (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-12 text-center text-xs text-content-muted">
                  Sol panelden bir hata kaydı seçerek yığın izini ve sistem detaylarını görüntüleyin.
                </div>
              )}
            </div>
          )}

          {/* TAB 7: GERİ BİLDİRİM (FEEDBACKS) */}
          {activeTab === 'feedbacks' && (
            <div className="space-y-6">
              {selectedFeedback ? (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-6">
                  {/* Feedback Header */}
                  <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-3 pb-4 border-b border-line">
                    <div className="flex items-center gap-3">
                      <span
                        className={`rounded-full px-2.5 py-0.5 text-xs font-mono font-bold uppercase ${
                          selectedFeedback.category.toLowerCase() === 'bug'
                            ? 'bg-danger/20 text-danger border border-danger/30'
                            : selectedFeedback.category.toLowerCase() === 'idea'
                            ? 'bg-surface-700 text-content-primary border border-line'
                            : 'bg-surface-600 text-content-primary border border-line'
                        }`}
                      >
                        {selectedFeedback.category}
                      </span>
                      <span className="text-xs font-semibold text-content-primary">
                        Bilet #{selectedFeedback.id}
                      </span>
                    </div>

                    <div className="flex items-center gap-3 text-xs font-mono text-content-muted">
                      <span>{new Date(selectedFeedback.createdAt).toLocaleString('tr-TR')}</span>
                    </div>
                  </div>

                  {/* Submitter Info */}
                  <div className="flex items-center gap-3 rounded-[var(--radius-control)] bg-surface-900 border border-line p-3 text-xs">
                    <span className="text-content-muted">Gönderen:</span>
                    <span className="font-semibold text-content-primary">
                      {selectedFeedback.email || 'Anonim Ziyaretçi'}
                    </span>
                    {selectedFeedback.userId && (
                      <span className="text-content-muted font-mono text-[11px]">
                        (Kullanıcı ID: {selectedFeedback.userId})
                      </span>
                    )}
                  </div>

                  {/* Message Content */}
                  <div className="space-y-2">
                    <span className="text-xs font-medium text-content-secondary">Mesaj İçeriği:</span>
                    <div className="rounded-[var(--radius-card)] bg-surface-900 border border-line p-5 text-xs leading-relaxed text-content-primary whitespace-pre-wrap font-sans">
                      {selectedFeedback.message}
                    </div>
                  </div>

                  {/* Actions */}
                  <div className="flex items-center justify-end gap-3 pt-4 border-t border-line">
                    <button
                      onClick={() => handleDeleteFeedback(selectedFeedback.id)}
                      disabled={deletingFeedback}
                      className="flex items-center gap-1.5 rounded-[var(--radius-control)] border border-danger/40 bg-danger/10 px-4 py-2 text-xs font-semibold text-danger hover:bg-danger/20 transition-colors cursor-pointer disabled:opacity-50"
                    >
                      <Trash2 className="h-3.5 w-3.5" />
                      {deletingFeedback ? 'Arşivleniyor...' : 'Çözüldü Olarak İşaretle & Kaldır'}
                    </button>
                  </div>
                </div>
              ) : (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-12 text-center text-xs text-content-muted">
                  Sol panelden bir geri bildirim bileti seçerek mesajı okuyun ve yanıtlayın.
                </div>
              )}
            </div>
          )}

          {/* TAB 8: SİSTEM (SYSTEM & AUTONOMOUS AUDIT BOT) */}
          {activeTab === 'system' && (
            <div className="space-y-6">
              {/* FAZ 3: Active Incident Alert Banner if any */}
              {botHistory?.incidents && botHistory.incidents.length > 0 && (
                <div className="rounded-[var(--radius-card)] border border-warning/40 bg-warning/10 p-5 shadow-xs space-y-3">
                  <div className="flex items-center gap-2.5">
                    <AlertTriangle className="h-5 w-5 text-warning shrink-0" />
                    <div>
                      <h3 className="text-sm font-bold text-content-primary">
                        Aktif Sistem Olayı: {botHistory.incidents[0].title}
                      </h3>
                      <p className="text-xs text-content-secondary mt-0.5">
                        {botHistory.incidents[0].recommendation}
                      </p>
                    </div>
                  </div>
                </div>
              )}

              {/* Bot Status & On-Demand Run */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4">
                  <div className="space-y-1">
                    <div className="flex items-center gap-2">
                      <h2 className="text-base font-bold text-content-primary">{t.auditBot}</h2>
                    </div>
                    <p className="text-xs text-content-muted">
                      {t.auditBotDesc}
                    </p>
                  </div>

                  <button
                    onClick={handleRunBotAudit}
                    disabled={runningAudit}
                    className="flex items-center justify-center gap-2 rounded-[var(--radius-control)] bg-accent px-4 py-2 text-xs font-bold text-accent-on transition-colors hover:bg-accent-hover cursor-pointer"
                  >
                    <RefreshCw className={`h-3.5 w-3.5 ${runningAudit ? 'animate-spin' : ''}`} />
                    {runningAudit ? t.auditingNow : t.runAuditBotNow}
                  </button>
                </div>

                <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-3 text-xs">
                  <div>
                    <span className="text-content-muted">{t.lastScan}</span>
                    <span className="font-mono text-content-primary ml-2">
                      {botReport?.auditTimestamp
                        ? new Date(botReport.auditTimestamp).toLocaleString(locale === 'en' ? 'en-US' : 'tr-TR')
                        : t.notScannedYet}
                    </span>
                  </div>
                  <div className="flex items-center gap-2">
                    <span className="text-content-muted">{t.generalHealth}</span>
                    <span className="font-mono font-bold text-content-primary">
                      %{botReport?.healthScore ?? 100} ({botReport?.overallStatus || 'Healthy'})
                    </span>
                  </div>
                </div>
              </div>

              {/* Service Probes Detailed Cards */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-bold text-content-primary">{t.serviceProbes}</h3>
                  <Activity className="h-4 w-4 text-accent-text" />
                </div>

                <div className="divide-y divide-line/60 rounded-[var(--radius-control)] border border-line bg-surface-900 overflow-hidden">
                  {botReport?.probes?.map((probe, idx) => (
                    <div key={idx} className="flex flex-col sm:flex-row sm:items-center sm:justify-between p-3.5 gap-2 text-xs">
                      <div className="flex items-center gap-2.5">
                        {probe.status === 'pass' ? (
                          <CheckCircle2 className="h-4 w-4 text-content-secondary shrink-0" />
                        ) : (
                          <AlertTriangle className="h-4 w-4 text-warning shrink-0" />
                        )}
                        <div>
                          <span className="font-semibold text-content-primary">{probe.serviceName}</span>
                          <p className="text-[11px] text-content-muted">{probe.message}</p>
                        </div>
                      </div>
                      <div className="flex items-center gap-2 shrink-0">
                        <span className="font-mono text-[11px] text-content-muted">{probe.latencyMs} ms</span>
                        <span
                          className={`rounded-full px-2 py-0.5 text-[10px] font-mono font-bold uppercase ${
                            probe.status === 'pass'
                              ? 'bg-surface-700 text-content-primary border border-line'
                              : 'bg-warning/20 text-warning border border-warning/30'
                          }`}
                        >
                          {probe.status}
                        </span>
                      </div>
                    </div>
                  ))}
                </div>
              </div>

              {/* FAZ 3: Audit History Log */}
              {botHistory?.history && botHistory.history.length > 0 && (
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                  <div className="flex items-center justify-between">
                    <div>
                      <h3 className="text-sm font-bold text-content-primary">{t.auditHistory}</h3>
                      <p className="text-xs text-content-muted mt-0.5">
                        {t.auditHistoryDesc}
                      </p>
                    </div>
                    <Clock className="h-4 w-4 text-content-muted" />
                  </div>

                  <div className="divide-y divide-line/60 rounded-[var(--radius-control)] border border-line bg-surface-900 overflow-hidden">
                    {botHistory.history.map((h, idx) => (
                      <div key={idx} className="flex items-center justify-between p-3 text-xs">
                        <div className="flex items-center gap-3">
                          <span className="rounded-full bg-surface-700 text-content-primary border border-line px-2 py-0.5 text-[10px] font-mono font-bold">
                            %{h.healthScore}
                          </span>
                          <span className="text-content-primary">{h.summaryText}</span>
                        </div>
                        <span className="font-mono text-[10px] text-content-muted">
                          {new Date(h.auditTimestamp).toLocaleString('tr-TR')}
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              )}

              {/* Infrastructure Environment */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-3">
                <h3 className="text-sm font-bold text-content-primary">Sunucu & Barındırma Mimarisi</h3>
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3 text-xs">
                  <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3">
                    <span className="text-[10px] font-mono text-content-muted uppercase">Backend Host</span>
                    <p className="font-semibold text-content-primary mt-1">Oracle Cloud Infrastructure</p>
                    <span className="text-[10px] text-content-muted">Linux ARM64 · .NET 8 Kestrel</span>
                  </div>
                  <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3">
                    <span className="text-[10px] font-mono text-content-muted uppercase">Frontend Edge</span>
                    <p className="font-semibold text-content-primary mt-1">Vercel Global Edge</p>
                    <span className="text-[10px] text-content-muted">Next.js 16 · Turbopack SSR</span>
                  </div>
                  <div className="rounded-[var(--radius-control)] bg-surface-900 border border-line p-3">
                    <span className="text-[10px] font-mono text-content-muted uppercase">Control Database</span>
                    <p className="font-semibold text-content-primary mt-1">PostgreSQL + Supabase</p>
                    <span className="text-[10px] text-content-muted">EF Core Migrations & Auth</span>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </main>

      {/* ─────────────────────────────────────────────────────────────
          FAZ 4: MODAL 1 - PROJE MODÜL DERİNLİĞİ (VAULT, FLOW, GROUND, DESK, COMPILE)
          ───────────────────────────────────────────────────────────── */}
      {deepStatsModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4 sm:p-6">
          <div className="flex flex-col w-full max-w-3xl max-h-[90vh] rounded-[var(--radius-modal)] border border-line bg-surface-800 shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            {/* Header */}
            <div className="flex items-center justify-between border-b border-line px-6 py-4 bg-surface-850">
              <div className="flex items-center gap-2.5">
                <Box className="h-5 w-5 text-accent-text" />
                <div>
                  <div className="flex items-center gap-2">
                    <h3 className="text-sm font-bold text-content-primary">
                      {deepStatsData?.projectName || 'Proje'} Modül Kullanım Analizi
                    </h3>
                    {deepStatsData?.dbType && (
                      <span className="rounded-full bg-surface-700 border border-line px-2 py-0.2 text-[10px] font-mono text-content-muted uppercase">
                        {deepStatsData.dbType}
                      </span>
                    )}
                  </div>
                  <p className="text-[11px] font-mono text-content-muted mt-0.5">
                    Proje ID: {deepStatsData?.projectId} · {deepStatsData?.tableCount} Tablo · {deepStatsData?.estimatedMb} MB
                  </p>
                </div>
              </div>
              <button
                onClick={() => {
                  setDeepStatsModalOpen(false);
                  setDeepStatsData(null);
                }}
                className="tap-44 p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            {/* Body */}
            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {deepStatsLoading ? (
                <div className="py-16 text-center text-xs text-content-muted flex flex-col items-center justify-center gap-2">
                  <RefreshCw className="h-5 w-5 animate-spin text-accent-text" />
                  <span>Modül kullanım verileri derleniyor...</span>
                </div>
              ) : deepStatsData ? (
                <div className="space-y-4 text-xs">
                  {/* Summary Bar */}
                  <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                    <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3">
                      <span className="text-[10px] font-mono text-content-muted uppercase">Vault Yedek</span>
                      <p className="text-base font-bold text-content-primary mt-0.5">
                        {deepStatsData.vault.snapshotsCount} adet ({deepStatsData.vault.totalStorageMb} MB)
                      </p>
                    </div>
                    <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3">
                      <span className="text-[10px] font-mono text-content-muted uppercase">Flow Kuralları</span>
                      <p className="text-base font-bold text-content-primary mt-0.5">
                        {deepStatsData.flow.rulesCount} kural ({deepStatsData.flow.totalExecutions} tetikleme)
                      </p>
                    </div>
                    <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3">
                      <span className="text-[10px] font-mono text-content-muted uppercase">Ground Host</span>
                      <p className="text-base font-bold text-content-primary mt-0.5">
                        {deepStatsData.ground.isHosted ? 'Barındırılıyor' : 'Harici DB'}
                      </p>
                    </div>
                    <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3">
                      <span className="text-[10px] font-mono text-content-muted uppercase">Desk Sorguları</span>
                      <p className="text-base font-bold text-content-primary mt-0.5">
                        {deepStatsData.desk.totalQueriesExecuted} sorgu
                      </p>
                    </div>
                  </div>

                  {/* 5 Deep Modules Breakdown */}
                  <div className="grid grid-cols-1 md:grid-cols-2 gap-4 pt-2">
                    {/* 1. VAULT */}
                    <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-4 space-y-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Shield className="h-4 w-4 text-accent-text" />
                          <h4 className="font-bold text-content-primary">Vault (Yedekleme & İzolasyon)</h4>
                        </div>
                        <span className="rounded-full bg-surface-700 border border-line px-2 py-0.2 text-[10px] font-mono text-content-muted">
                          {deepStatsData.vault.encryption}
                        </span>
                      </div>
                      <div className="space-y-1.5 text-[11px]">
                        <div className="flex justify-between">
                          <span className="text-content-muted">Alınan Anlık Görüntü (Snapshot):</span>
                          <span className="font-mono font-medium text-content-primary">{deepStatsData.vault.snapshotsCount} adet</span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-content-muted">Kullanılan Alan:</span>
                          <span className="font-mono font-medium text-content-primary">{deepStatsData.vault.totalStorageMb} MB</span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-content-muted">Saklama Politikası:</span>
                          <span className="font-medium text-content-primary">{deepStatsData.vault.retentionPolicy}</span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-content-muted">Son Yedekleme Zamanı:</span>
                          <span className="font-mono text-content-muted">{new Date(deepStatsData.vault.lastBackupAt).toLocaleString('tr-TR')}</span>
                        </div>
                      </div>
                    </div>

                    {/* 2. FLOW */}
                    <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-4 space-y-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Workflow className="h-4 w-4 text-accent-text" />
                          <h4 className="font-bold text-content-primary">Flow (Otomasyon & Tetikleyiciler)</h4>
                        </div>
                        <span className="rounded-full bg-surface-700 border border-line px-2 py-0.2 text-[10px] font-mono text-content-muted">
                          {deepStatsData.flow.rulesCount} Kural
                        </span>
                      </div>
                      <div className="space-y-1.5 text-[11px]">
                        <div className="flex justify-between">
                          <span className="text-content-muted">Toplam Tetiklenme:</span>
                          <span className="font-mono font-medium text-content-primary">{deepStatsData.flow.totalExecutions} kez</span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-content-muted">Son Tetiklenme:</span>
                          <span className="font-mono text-content-muted">
                            {deepStatsData.flow.lastTriggeredAt ? new Date(deepStatsData.flow.lastTriggeredAt).toLocaleString('tr-TR') : 'Henüz tetiklenmedi'}
                          </span>
                        </div>
                        <div className="space-y-1 pt-1">
                          <span className="text-content-muted block">Aktif Olay Dinleyicileri:</span>
                          <div className="flex flex-wrap gap-1">
                            {deepStatsData.flow.activeTriggers?.length > 0 ? (
                              deepStatsData.flow.activeTriggers.map((t, idx) => (
                                <span key={idx} className="rounded-[var(--radius-control)] bg-surface-800 border border-line px-2 py-0.5 text-[10px] font-mono text-accent-text">
                                  {t}
                                </span>
                              ))
                            ) : (
                              <span className="text-content-muted text-[10px]">Aktif tetikleyici yok</span>
                            )}
                          </div>
                        </div>
                      </div>
                    </div>

                    {/* 3. GROUND */}
                    <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-4 space-y-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Radio className="h-4 w-4 text-accent-text" />
                          <h4 className="font-bold text-content-primary">Ground (Yönetilen Veritabanı)</h4>
                        </div>
                        <span className={`rounded-full border px-2 py-0.2 text-[10px] font-mono font-bold ${
                          deepStatsData.ground.isHosted
                            ? 'bg-surface-700 text-content-primary border-line'
                            : 'bg-surface-700 text-content-muted border-line'
                        }`}>
                          {deepStatsData.ground.status}
                        </span>
                      </div>
                      <div className="space-y-1.5 text-[11px]">
                        <div className="flex justify-between">
                          <span className="text-content-muted">Barındırma Uç Noktası:</span>
                          <span className="font-mono text-content-primary truncate max-w-[190px]">
                            {deepStatsData.ground.hostEndpoint || 'Harici Bağlantı'}
                          </span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-content-muted">Canlı Bağlantı Sayısı:</span>
                          <span className="font-mono font-medium text-content-primary">{deepStatsData.ground.activeConnections} istemci</span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-content-muted">Tahsis Edilen Alan:</span>
                          <span className="font-mono font-medium text-content-primary">{deepStatsData.ground.allocatedMb} MB</span>
                        </div>
                      </div>
                    </div>

                    {/* 4. DESK */}
                    <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-4 space-y-3">
                      <div className="flex items-center justify-between">
                        <div className="flex items-center gap-2">
                          <Terminal className="h-4 w-4 text-accent-text" />
                          <h4 className="font-bold text-content-primary">Desk (SQL Sorgulama & Analiz)</h4>
                        </div>
                        <span className="rounded-full bg-surface-700 border border-line px-2 py-0.2 text-[10px] font-mono text-content-muted">
                          {deepStatsData.desk.totalQueriesExecuted} Sorgu
                        </span>
                      </div>
                      <div className="space-y-1.5 text-[11px]">
                        <div className="flex justify-between">
                          <span className="text-content-muted">Son Çalıştırılan Sorgu:</span>
                          <span className="font-mono text-content-muted">
                            {deepStatsData.desk.lastQueryAt ? new Date(deepStatsData.desk.lastQueryAt).toLocaleString('tr-TR') : 'Sorgu yok'}
                          </span>
                        </div>
                        <div className="flex justify-between">
                          <span className="text-content-muted">CSV / JSON Dışa Aktarma:</span>
                          <span className="font-mono font-medium text-content-primary">{deepStatsData.desk.exportCount} kez</span>
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* 5. COMPILE */}
                  <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-4 space-y-2">
                    <div className="flex items-center justify-between">
                      <div className="flex items-center gap-2">
                        <FolderGit2 className="h-4 w-4 text-accent-text" />
                        <h4 className="font-bold text-content-primary">Compile & Migrasyon Geçmişi</h4>
                      </div>
                      <span className="rounded-full bg-surface-700 border border-line px-2 py-0.2 text-[10px] font-mono text-content-muted uppercase">
                        {deepStatsData.compile.lastExportFormat}
                      </span>
                    </div>
                    <div className="grid grid-cols-1 sm:grid-cols-3 gap-2 pt-1 text-[11px]">
                      <div className="flex justify-between sm:flex-col">
                        <span className="text-content-muted">DDL Üretim Adedi:</span>
                        <span className="font-mono font-medium text-content-primary">{deepStatsData.compile.ddlGeneratedCount} kez</span>
                      </div>
                      <div className="flex justify-between sm:flex-col">
                        <span className="text-content-muted">Şema Senkronizasyonu:</span>
                        <span className="font-medium text-content-secondary">Senkronize</span>
                      </div>
                      <div className="flex justify-between sm:flex-col">
                        <span className="text-content-muted">Aktif DDL Migrasyonu:</span>
                        <span className="font-mono text-content-primary">
                          {deepStatsData.compile.hasActiveMigrations ? 'Devam Eden Migrasyon Var' : 'Stabil Şema'}
                        </span>
                      </div>
                    </div>
                  </div>
                </div>
              ) : null}
            </div>

            {/* Footer */}
            <div className="flex justify-end border-t border-line px-6 py-3.5 bg-surface-850">
              <button
                onClick={() => {
                  setDeepStatsModalOpen(false);
                  setDeepStatsData(null);
                }}
                className="rounded-[var(--radius-control)] bg-surface-700 px-4 py-2 text-xs font-semibold text-content-primary hover:bg-surface-600 transition-colors cursor-pointer"
              >
                Kapat
              </button>
            </div>
          </div>
        </div>
      )}

      {/* ─────────────────────────────────────────────────────────────
          FAZ 4: MODAL 2 - KULLANICI OTURUM GEÇMİŞİ & SAYFA YOLCULUĞU
          ───────────────────────────────────────────────────────────── */}
      {userSessionsModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/60 backdrop-blur-xs p-4 sm:p-6">
          <div className="flex flex-col w-full max-w-3xl max-h-[90vh] rounded-[var(--radius-modal)] border border-line bg-surface-800 shadow-2xl overflow-hidden animate-in fade-in zoom-in-95 duration-150">
            {/* Header */}
            <div className="flex items-center justify-between border-b border-line px-6 py-4 bg-surface-850">
              <div className="flex items-center gap-2.5">
                <Compass className="h-5 w-5 text-accent-text" />
                <div>
                  <h3 className="text-sm font-bold text-content-primary">
                    Kullanıcı Oturum Geçmişi & Sayfa Gezinmeleri
                  </h3>
                  <p className="text-[11px] font-mono text-content-muted mt-0.5">
                    {userSessionsData?.userEmail || 'Kullanıcı'} · {userSessionsData?.totalSessions || 0} Oturum · Toplam {userSessionsData?.totalDurationMinutes || 0} dakika sistemde kalındı
                  </p>
                </div>
              </div>
              <button
                onClick={() => {
                  setUserSessionsModalOpen(false);
                  setUserSessionsData(null);
                }}
                className="tap-44 p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
              >
                <X className="h-4 w-4" />
              </button>
            </div>

            {/* Body */}
            <div className="flex-1 overflow-y-auto p-6 space-y-4">
              {userSessionsLoading ? (
                <div className="py-16 text-center text-xs text-content-muted flex flex-col items-center justify-center gap-2">
                  <RefreshCw className="h-5 w-5 animate-spin text-accent-text" />
                  <span>Kullanıcı oturum yolculuğu yükleniyor...</span>
                </div>
              ) : userSessionsData?.sessions && userSessionsData.sessions.length > 0 ? (
                userSessionsData.sessions.map((sess, idx) => (
                  <div key={idx} className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-4 space-y-3 text-xs">
                    <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-1 pb-2 border-b border-line">
                      <div className="flex items-center gap-2">
                        <span className="font-mono font-semibold text-content-primary">{sess.sessionId}</span>
                        <span className="rounded-full bg-accent-subtle/60 border border-line px-2 py-0.2 text-[10px] font-mono text-accent-text uppercase">
                          Başlangıç: {sess.startMode}
                        </span>
                      </div>
                      <div className="flex items-center gap-3 text-[11px] font-mono text-content-muted">
                        <span>Süre: {sess.durationMinutes} dk</span>
                        <span>·</span>
                        <span>{sess.totalClicks} Tıklama</span>
                      </div>
                    </div>

                    {/* Page Breadcrumbs Journey */}
                    <div className="space-y-1">
                      <span className="text-[10px] font-mono uppercase text-content-muted font-bold block">
                        Ziyaret Edilen Sayfalar:
                      </span>
                      <div className="flex flex-wrap items-center gap-1.5">
                        {sess.pagesVisited?.map((page, pIdx) => (
                          <React.Fragment key={pIdx}>
                            <span className="rounded-[var(--radius-control)] border border-line bg-surface-800 px-2 py-0.5 text-[11px] font-mono text-content-primary">
                              {page}
                            </span>
                            {pIdx < sess.pagesVisited.length - 1 && (
                              <ChevronRight className="h-3 w-3 text-content-muted shrink-0" />
                            )}
                          </React.Fragment>
                        ))}
                      </div>
                    </div>

                    {/* Action Logs */}
                    {sess.recentActions && sess.recentActions.length > 0 && (
                      <div className="space-y-1 pt-1">
                        <span className="text-[10px] font-mono uppercase text-content-muted font-bold block">
                          Eylem Geçmişi:
                        </span>
                        <div className="divide-y divide-line/40 rounded-[var(--radius-control)] border border-line bg-surface-800 overflow-hidden">
                          {sess.recentActions.map((act, aIdx) => (
                            <div key={aIdx} className="flex items-center justify-between p-2 text-[11px]">
                              <div className="flex items-center gap-2">
                                <span className="rounded-[var(--radius-control)] bg-surface-700 px-1.5 py-0.2 text-[10px] font-mono uppercase text-content-muted">
                                  {act.module}
                                </span>
                                <span className="font-medium text-content-primary">{act.action}</span>
                              </div>
                              <span className="font-mono text-[10px] text-content-muted">
                                {new Date(act.timestamp).toLocaleTimeString('tr-TR')}
                              </span>
                            </div>
                          ))}
                        </div>
                      </div>
                    )}
                  </div>
                ))
              ) : (
                <div className="py-12 text-center text-xs text-content-muted">
                  Bu kullanıcıya ait kaydedilmiş oturum bulunamadı.
                </div>
              )}
            </div>

            {/* Footer */}
            <div className="flex justify-end border-t border-line px-6 py-3.5 bg-surface-850">
              <button
                onClick={() => {
                  setUserSessionsModalOpen(false);
                  setUserSessionsData(null);
                }}
                className="rounded-[var(--radius-control)] bg-surface-700 px-4 py-2 text-xs font-semibold text-content-primary hover:bg-surface-600 transition-colors cursor-pointer"
              >
                Kapat
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
