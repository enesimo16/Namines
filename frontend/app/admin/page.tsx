'use client';

import React, { useState, useEffect, useCallback, useMemo } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  LayoutDashboard,
  Users,
  MessageSquareQuote,
  ShieldCheck,
  ShieldAlert,
  Database,
  RefreshCw,
  Search,
  Sparkles,
  Zap,
  Trash2,
  CheckCircle2,
  Clock,
  ArrowLeft,
  ChevronRight,
  UserCheck,
  HardDrive,
  Activity,
  Layers,
  Award,
  Lock,
} from 'lucide-react';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useToastStore } from '../../store/useToastStore';
import {
  adminService,
  type AdminOverviewStats,
  type AdminUserItem,
  type AdminUserDetail,
  type AdminFeedbackItem,
} from '../../services/adminService';
import { Select } from '../../components/ui/Select';
import Logo from '../../components/layout/Logo';
import LanguageToggle from '../../components/layout/LanguageToggle';
import ThemeToggleButton from '../../components/landing/ThemeToggleButton';

type AdminTab = 'overview' | 'users' | 'feedbacks' | 'system';

export default function AdminPage() {
  const router = useRouter();
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

  const [activeTab, setActiveTab] = useState<AdminTab>('overview');
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

  // Feedbacks Data
  const [feedbacksList, setFeedbacksList] = useState<AdminFeedbackItem[]>([]);
  const [feedbackCategoryFilter, setFeedbackCategoryFilter] = useState<string>('all');
  const [selectedFeedbackId, setSelectedFeedbackId] = useState<number | null>(null);
  const [deletingFeedback, setDeletingFeedback] = useState(false);

  // Initial Load
  const fetchAllData = useCallback(async () => {
    if (!isAdmin) return;
    try {
      setRefreshing(true);
      const [stats, usersRes, fbRes] = await Promise.allSettled([
        adminService.getOverview(),
        adminService.getUsers({ pageSize: 100 }),
        adminService.getFeedbacks({ pageSize: 100 }),
      ]);

      if (stats.status === 'fulfilled') {
        setOverviewStats(stats.value);
      }
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
    } catch {
      showToast('Veriler güncellenirken hata oluştu', 'error');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [isAdmin, selectedUserId, selectedFeedbackId, showToast]);

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

  // Actions
  const handleUpdatePlan = async () => {
    if (!selectedUserId) return;
    try {
      setUpdatingPlan(true);
      const result = await adminService.updateUserPlan(selectedUserId, editPlanCode);
      showToast(result.message || 'Kullanıcı planı başarıyla güncellendi', 'success');
      // Refresh user detail & list
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

  // Giriş Koruması (AdminGuard)
  if (!loading && !isAdmin) {
    return (
      <div className="flex min-h-screen w-full items-center justify-center bg-surface-900 p-6 font-sans text-content-primary select-none">
        <div className="w-full max-w-sm rounded-[var(--radius-modal)] border border-line bg-surface-800 p-7 shadow-xl text-center space-y-5">
          <div className="space-y-2">
            <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full bg-surface-700/60 border border-line text-[10px] font-mono tracking-widest uppercase text-content-muted">
              <span className="h-1.5 w-1.5 rounded-full bg-danger/80"></span>
              Erişim Kısıtlı
            </span>
            <h1 className="text-base font-bold tracking-tight text-content-primary">
              Yönetim Konsolu
            </h1>
            <p className="text-xs text-content-muted leading-relaxed max-w-xs mx-auto">
              Bu operasyonel alan yalnızca yetkili geliştirici ve yönetici hesaplarına açıktır.
            </p>
          </div>

          {isAuthenticated && (
            <div className="rounded-[var(--radius-card)] bg-surface-900 border border-line p-3 text-left space-y-1">
              <div className="flex items-center justify-between text-xs">
                <span className="text-content-muted text-[11px]">Aktif Hesap</span>
                <span className="font-mono text-content-primary text-[11px] font-medium truncate max-w-[190px]">
                  {user?.email || user?.username}
                </span>
              </div>
              <p className="text-[10px] text-content-muted">
                Bu hesaba ait yönetici yetkisi bulunmuyor.
              </p>
            </div>
          )}

          <div className="space-y-2 pt-1">
            <button
              onClick={() => openAuthModal()}
              className="w-full rounded-[var(--radius-control)] bg-accent px-4 py-2.5 text-xs font-bold text-accent-on transition-colors hover:bg-accent-hover cursor-pointer shadow-xs"
            >
              {isAuthenticated ? 'Farklı Bir Hesapla Giriş Yap' : 'Yetkili Girişi Yap'}
            </button>

            <Link
              href="/canvas"
              className="block w-full py-1.5 text-xs text-content-muted hover:text-content-primary transition-colors text-center"
            >
              Çalışma Alanına Dön
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex h-screen w-screen overflow-hidden bg-surface-900 font-sans text-content-primary">
      {/* ─────────────────────────────────────────────────────────────
          1. SOL PANEL: Ana Gezinti / Primary Rail (Shopify Polaris Nav)
          ───────────────────────────────────────────────────────────── */}
      <aside className="flex w-60 shrink-0 flex-col justify-between border-r border-line bg-surface-900 select-none">
        <div>
          {/* Brand Header */}
          <div className="flex h-14 items-center justify-between border-b border-line px-4">
            <Link href="/canvas" className="flex items-center gap-2 group" title="Namines">
              <Logo size="sm" showWordmark={true} />
            </Link>
            <span className="rounded-full bg-accent-subtle border border-line px-2 py-0.5 text-[10px] font-mono font-bold text-accent-text">
              v1.0
            </span>
          </div>

          {/* Navigation Links */}
          <nav className="p-3 space-y-1">
            <button
              onClick={() => setActiveTab('overview')}
              className={`flex w-full items-center gap-2.5 rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'overview'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <LayoutDashboard className="h-4 w-4 text-accent-text" />
              <span>Genel Bakış (Overview)</span>
            </button>

            <button
              onClick={() => setActiveTab('users')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'users'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <Users className="h-4 w-4 text-accent-text" />
                <span>Kullanıcılar & Kotalar</span>
              </div>
              {usersList.length > 0 && (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  {usersList.length}
                </span>
              )}
            </button>

            <button
              onClick={() => setActiveTab('feedbacks')}
              className={`flex w-full items-center justify-between rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'feedbacks'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <MessageSquareQuote className="h-4 w-4 text-accent-text" />
                <span>Geri Bildirimler</span>
              </div>
              {feedbacksList.length > 0 && (
                <span className="rounded-full bg-surface-700 border border-line px-1.5 py-0.2 text-[10px] font-mono text-content-muted">
                  {feedbacksList.length}
                </span>
              )}
            </button>

            <button
              onClick={() => setActiveTab('system')}
              className={`flex w-full items-center gap-2.5 rounded-[var(--radius-control)] px-3 py-2 text-xs font-medium transition-colors cursor-pointer ${
                activeTab === 'system'
                  ? 'bg-surface-800 text-content-primary border border-line-strong font-semibold shadow-xs'
                  : 'text-content-muted hover:bg-surface-800/50 hover:text-content-secondary'
              }`}
            >
              <Activity className="h-4 w-4 text-accent-text" />
              <span>Sistem & Telemetri</span>
            </button>
          </nav>
        </div>

        {/* Bottom Quick Return to Studio */}
        <div className="border-t border-line p-3">
          <Link
            href="/canvas"
            className="flex w-full items-center justify-center gap-1.5 rounded-[var(--radius-control)] border border-line bg-surface-800 px-3 py-2 text-xs font-medium text-content-secondary hover:bg-surface-700 hover:text-content-primary transition-colors cursor-pointer"
          >
            <ArrowLeft className="h-3.5 w-3.5" />
            <span>Studio / Tuvale Dön</span>
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
            {activeTab === 'overview' && 'Operasyon Özeti'}
            {activeTab === 'users' && `Kullanıcılar (${filteredUsers.length})`}
            {activeTab === 'feedbacks' && `Biletler (${filteredFeedbacks.length})`}
            {activeTab === 'system' && 'Servis Durumları'}
          </span>
          <button
            onClick={() => fetchAllData()}
            disabled={refreshing}
            title="Yenile"
            className="tap-44 p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${refreshing ? 'animate-spin' : ''}`} />
          </button>
        </div>

        {/* Sub-Panel Content: Tab-Dependent */}
        {activeTab === 'users' && (
          <div className="flex flex-1 flex-col overflow-hidden">
            {/* Search Input */}
            <div className="p-3 border-b border-line space-y-2">
              <div className="relative">
                <Search className="absolute left-2.5 top-2.5 h-3.5 w-3.5 text-content-muted" />
                <input
                  type="text"
                  placeholder="Kullanıcı veya e-posta ara..."
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
                  Kriterlere uygun kullanıcı bulunamadı.
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
                                ? 'bg-success/20 text-success border border-success/30'
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
                              ? 'bg-success/20 text-success border border-success/30'
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

        {activeTab === 'overview' && (
          <div className="flex-1 overflow-y-auto p-4 space-y-4 text-xs">
            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3.5 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                Platform Canlı Durum
              </span>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">PostgreSQL Control DB</span>
                <span className="flex items-center gap-1.5 text-success font-medium">
                  <span className="h-1.5 w-1.5 rounded-full bg-success"></span>
                  Sağlıklı
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">SignalR Redis Backplane</span>
                <span className="flex items-center gap-1.5 text-success font-medium">
                  <span className="h-1.5 w-1.5 rounded-full bg-success"></span>
                  Bağlı
                </span>
              </div>
              <div className="flex items-center justify-between">
                <span className="text-content-secondary">AI Provider Gateway</span>
                <span className="flex items-center gap-1.5 text-success font-medium">
                  <span className="h-1.5 w-1.5 rounded-full bg-success"></span>
                  Aktif
                </span>
              </div>
            </div>

            <div className="rounded-[var(--radius-card)] border border-line bg-surface-900/60 p-3.5 space-y-2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-muted font-bold">
                Hızlı Eylemler
              </span>
              <button
                onClick={() => setActiveTab('users')}
                className="flex w-full items-center justify-between rounded-[var(--radius-control)] bg-surface-800 p-2 text-content-primary hover:bg-surface-700 transition-colors text-left"
              >
                <span>Tüm Kullanıcıları Yönet</span>
                <ChevronRight className="h-3.5 w-3.5 text-content-muted" />
              </button>
              <button
                onClick={() => setActiveTab('feedbacks')}
                className="flex w-full items-center justify-between rounded-[var(--radius-control)] bg-surface-800 p-2 text-content-primary hover:bg-surface-700 transition-colors text-left"
              >
                <span>Geri Bildirimleri İncele</span>
                <ChevronRight className="h-3.5 w-3.5 text-content-muted" />
              </button>
            </div>
          </div>
        )}

        {activeTab === 'system' && (
          <div className="flex-1 overflow-y-auto p-4 space-y-3 text-xs">
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
          {/* Top Breadcrumb & Live Status Bar */}
          <header className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between pb-4 border-b border-line">
            <div>
              <div className="flex items-center gap-2 text-xs font-mono text-content-muted">
                <span>Operations</span>
                <span>/</span>
                <span className="text-content-primary capitalize">{activeTab}</span>
              </div>
              <h1 className="text-lg font-bold tracking-tight text-content-primary mt-1">
                {activeTab === 'overview' && 'Operasyonel Genel Bakış & Metrikler'}
                {activeTab === 'users' && 'Kullanıcı Hesapları & Kota Yönetimi'}
                {activeTab === 'feedbacks' && 'Geri Bildirim & İstek Gelen Kutusu'}
                {activeTab === 'system' && 'Sistem Altyapısı & Canlılık Durumu'}
              </h1>
            </div>

            <div className="flex items-center gap-2.5 sm:gap-3">
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

          {/* TAB 1: OVERVIEW */}
          {activeTab === 'overview' && (
            <div className="space-y-6">
              {/* 4 Polaris KPI Stat Cards */}
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Toplam Kayıtlı Kullanıcı</span>
                    <Users className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalUsers ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Aktif veritabanı hesapları
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Toplam Proje Sayısı</span>
                    <Database className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalProjects ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Üretilen ve yönetilen şemalar
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Geri Bildirimler</span>
                    <MessageSquareQuote className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalFeedbacks ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Kullanıcı hata ve öneri biletleri
                  </div>
                </div>

                <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-5 shadow-xs">
                  <div className="flex items-center justify-between text-content-muted">
                    <span className="text-xs font-medium">Bugünkü AI Tüketimi</span>
                    <Zap className="h-4 w-4 text-accent-text" />
                  </div>
                  <div className="mt-3 text-2xl font-bold tracking-tight text-content-primary">
                    {overviewStats?.totalQuotaUsedToday ?? '...'}
                  </div>
                  <div className="mt-1 text-[11px] text-content-muted">
                    Bugün harcanan toplam AI çağrısı
                  </div>
                </div>
              </div>

              {/* Plan Distribution Card */}
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h2 className="text-sm font-bold text-content-primary">Abonelik & Plan Dağılımı</h2>
                    <p className="text-xs text-content-muted mt-0.5">
                      Sistemdeki kullanıcıların aktif plan ve ayrıcalık dağılımı
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
                    <span className="text-[10px] text-content-muted">15 istek/gün</span>
                  </div>

                  <div className="rounded-[var(--radius-control)] border border-line bg-surface-900 p-3.5">
                    <span className="text-[11px] font-mono text-content-muted uppercase">Pro Solo</span>
                    <div className="text-xl font-bold text-content-primary mt-1">
                      {overviewStats?.plans.pro ?? 0}
                    </div>
                    <span className="text-[10px] text-content-muted">100 istek/gün</span>
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
            </div>
          )}

          {/* TAB 2: USERS & QUOTAS */}
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
                        {/* Custom Shopify Polaris Dropdown */}
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
                  Sol panelden bir kullanıcı seçerek detayları, projeleri ve kota müdahalelerini görüntüleyin.
                </div>
              )}
            </div>
          )}

          {/* TAB 3: FEEDBACKS */}
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
                            ? 'bg-success/20 text-success border border-success/30'
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

          {/* TAB 4: SYSTEM */}
          {activeTab === 'system' && (
            <div className="space-y-6">
              <div className="rounded-[var(--radius-card)] border border-line bg-surface-800 p-6 shadow-xs space-y-4">
                <div className="flex items-center justify-between">
                  <div>
                    <h2 className="text-sm font-bold text-content-primary">Sunucu & Veri Katmanı</h2>
                    <p className="text-xs text-content-muted mt-0.5">
                      Bağlantılı servislerin durum ve yanıt süreleri
                    </p>
                  </div>
                  <CheckCircle2 className="h-5 w-5 text-success" />
                </div>

                <div className="divide-y divide-line/60 rounded-[var(--radius-control)] border border-line bg-surface-900 overflow-hidden">
                  <div className="flex items-center justify-between p-3.5 text-xs">
                    <div className="flex items-center gap-2.5">
                      <Database className="h-4 w-4 text-accent-text" />
                      <div>
                        <span className="font-semibold text-content-primary">Control DB (PostgreSQL)</span>
                        <p className="text-[11px] text-content-muted">
                          Kullanıcılar, yetkiler, projeler ve denetim logları
                        </p>
                      </div>
                    </div>
                    <span className="rounded-full bg-success/20 text-success border border-success/30 px-2 py-0.5 text-[10px] font-mono">
                      Online · 12ms
                    </span>
                  </div>

                  <div className="flex items-center justify-between p-3.5 text-xs">
                    <div className="flex items-center gap-2.5">
                      <Layers className="h-4 w-4 text-accent-text" />
                      <div>
                        <span className="font-semibold text-content-primary">SignalR Canvas Hub</span>
                        <p className="text-[11px] text-content-muted">
                          Canlı çoklu kullanıcı oturumu ve imleç senkronizasyonu
                        </p>
                      </div>
                    </div>
                    <span className="rounded-full bg-success/20 text-success border border-success/30 px-2 py-0.5 text-[10px] font-mono">
                      Online · Backplane Aktif
                    </span>
                  </div>

                  <div className="flex items-center justify-between p-3.5 text-xs">
                    <div className="flex items-center gap-2.5">
                      <Sparkles className="h-4 w-4 text-accent-text" />
                      <div>
                        <span className="font-semibold text-content-primary">AI Inference Gateway</span>
                        <p className="text-[11px] text-content-muted">
                          Namines AI, Claude, OpenAI, Groq ve DeepSeek yönlendirici
                        </p>
                      </div>
                    </div>
                    <span className="rounded-full bg-success/20 text-success border border-success/30 px-2 py-0.5 text-[10px] font-mono">
                      Sağlıklı · Rate Limit Yok
                    </span>
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>
      </main>
    </div>
  );
}
