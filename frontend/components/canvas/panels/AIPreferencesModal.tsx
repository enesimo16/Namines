import React, { useEffect, useRef, useState } from 'react';
import { X, User, CreditCard, HelpCircle, LogOut, Lock, BarChart3, SlidersHorizontal } from 'lucide-react';
import { useToastStore } from '../../../store/useToastStore';
import { useAuthStore } from '../../../store/useAuthStore';
import { useQuotaStore } from '../../../store/useQuotaStore';
import { useFocusTrap } from '../../../hooks/useFocusTrap';
import { useT, useLangAttr } from '../../../lib/i18n';
import Logo from '../../layout/Logo';
import { useOnOpen } from './preferences/useOnOpen';
import { useProfileForm } from './preferences/useProfileForm';
import { useMfaSettings } from './preferences/useMfaSettings';
import { useAiSettings } from './preferences/useAiSettings';
import { usePlans } from './preferences/usePlans';
import { useLocalStats } from './preferences/useLocalStats';
import ProfileTab from './preferences/ProfileTab';
import AccountTab from './preferences/AccountTab';
import AiTab from './preferences/AiTab';
import AnalyticsTab from './preferences/AnalyticsTab';
import PricingTab from './preferences/PricingTab';
import HelpTab from './preferences/HelpTab';

type PreferencesTab = 'profile' | 'account' | 'ai' | 'pricing' | 'help' | 'analytics';

interface AIPreferencesModalProps {
  isOpen: boolean;
  onClose: () => void;
}

const NAV_ITEMS: { id: PreferencesTab; icon: typeof User }[] = [
  { id: 'profile', icon: User },
  { id: 'account', icon: Lock },
  { id: 'ai', icon: SlidersHorizontal },
  { id: 'analytics', icon: BarChart3 },
  { id: 'pricing', icon: CreditCard },
  { id: 'help', icon: HelpCircle },
];

/**
 * Namines Hub — Hesap, AI ve Sistem Ayarları (Shopify Polaris Tasarımı).
 */
export default function AIPreferencesModal({ isOpen, onClose }: AIPreferencesModalProps) {
  const { user, logout, isAuthenticated } = useAuthStore();
  const fetchQuota = useQuotaStore(state => state.fetchQuota);
  const showToast = useToastStore(state => state.showToast);
  const t = useT();
  const lang = useLangAttr();
  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(isOpen, modalRef);

  const [activeTab, setActiveTab] = useState<PreferencesTab>('profile');
  const [openFaq, setOpenFaq] = useState<number | null>(null);
  useOnOpen(isOpen, () => setOpenFaq(null));

  const profile = useProfileForm(isOpen, isAuthenticated);
  const mfa = useMfaSettings(isOpen, isAuthenticated);
  const ai = useAiSettings(isOpen, onClose);
  const plans = usePlans(isOpen, isAuthenticated);
  const stats = useLocalStats(isOpen);

  useEffect(() => {
    if (isOpen && isAuthenticated) fetchQuota();
  }, [isOpen, isAuthenticated, fetchQuota]);

  useEffect(() => {
    if (!isOpen) return;
    const onKey = (event: KeyboardEvent) => { if (event.key === 'Escape') onClose(); };
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  const handleAccountDeleted = () => {
    logout();
    onClose();
    if (typeof window !== 'undefined') window.location.assign('/');
  };

  const handleLogout = () => {
    logout();
    showToast(t('header.loggedOut'), 'info');
    onClose();
  };

  return (
    <div className="fixed inset-0 z-[10000] flex items-center justify-center p-4">
      {/* Backdrop */}
      <div
        className="absolute inset-0 bg-surface-900/80 backdrop-blur-md"
        onClick={onClose}
      />

      {/* Main Modal Shell — Shopify Polaris Style */}
      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby="ai-pref-title"
        className="relative w-full max-w-5xl h-[88dvh] md:h-[calc(100dvh-80px)] md:max-h-[820px] bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] flex flex-col md:flex-row overflow-hidden shadow-2xl animate-in zoom-in-95 duration-200 text-content-primary"
      >
        {/* Left Sidebar */}
        <div
          lang={lang}
          className="w-full md:w-60 bg-surface-850/80 border-b md:border-b-0 md:border-r border-line p-4 flex flex-col justify-between shrink-0"
        >
          <div className="space-y-5">
            {/* Header Brand */}
            <div className="flex items-center gap-3 px-2 py-1">
              <Logo size="sm" showWordmark={false} />
              <div className="min-w-0">
                <h3 className="text-xs font-bold text-content-primary tracking-wide">
                  {t('preferences.hub')}
                </h3>
                <p className="text-[10px] text-content-muted font-mono truncate">
                  {t('preferences.hubSubtitle')}
                </p>
              </div>
            </div>

            {/* Navigation Tabs */}
            <nav className="flex flex-row md:flex-col gap-1 -mx-4 px-4 md:mx-0 md:px-0 overflow-x-auto md:overflow-x-visible pb-1 md:pb-0 scrollbar-none">
              {NAV_ITEMS.map(item => {
                const Icon = item.icon;
                const isActive = activeTab === item.id;
                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => {
                      setActiveTab(item.id);
                      if (item.id === 'ai') ai.setShowAdvancedScreen(false);
                    }}
                    className={`flex items-center gap-2.5 px-3 py-2 rounded-[var(--radius-control)] text-xs font-medium transition-colors cursor-pointer select-none whitespace-nowrap ${
                      isActive
                        ? 'bg-surface-800 text-content-primary font-semibold shadow-xs border border-line'
                        : 'text-content-secondary hover:text-content-primary hover:bg-surface-800/60'
                    }`}
                  >
                    <Icon className={`w-4 h-4 shrink-0 ${isActive ? 'text-accent-text' : 'text-content-muted'}`} />
                    <span>{t(`preferences.tabs.${item.id}.nav`)}</span>
                  </button>
                );
              })}
            </nav>
          </div>

          {/* Logout button */}
          {isAuthenticated && (
            <button
              onClick={handleLogout}
              className="hidden md:flex items-center justify-center gap-2 w-full py-2 bg-surface-800 hover:bg-danger-subtle text-content-muted hover:text-danger border border-line hover:border-danger/20 text-xs font-medium rounded-[var(--radius-control)] transition-all cursor-pointer active:scale-95"
            >
              <LogOut className="w-3.5 h-3.5" />
              <span>{t('preferences.logOut')}</span>
            </button>
          )}
        </div>

        {/* Right Content Pane */}
        <div className="flex-1 flex flex-col h-full overflow-hidden">
          {/* Header */}
          <div
            lang={lang}
            className="flex justify-between items-center px-6 py-4 border-b border-line bg-surface-850/50 shrink-0"
          >
            <div>
              <h4 id="ai-pref-title" className="text-sm font-semibold text-content-primary">
                {t(`preferences.tabs.${activeTab}.title`)}
              </h4>
              <p className="text-[11px] text-content-muted mt-0.5">
                {t(`preferences.tabs.${activeTab}.subtitle`)}
              </p>
            </div>
            <button
              onClick={onClose}
              aria-label={t('preferences.close')}
              className="p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-800 transition-colors cursor-pointer"
            >
              <X className="w-4 h-4" />
            </button>
          </div>

          {/* Tab Content Panel */}
          <div className="flex-1 p-6 overflow-y-auto space-y-6 bg-surface-900">
            {activeTab === 'profile' && <ProfileTab isAuthenticated={isAuthenticated} form={profile} />}
            {activeTab === 'account' && (
              <AccountTab
                isAuthenticated={isAuthenticated}
                username={user?.username}
                email={user?.email}
                userType={user?.type}
                planTier={plans.planTier}
                mfa={mfa}
                onLogout={handleLogout}
                onAccountDeleted={handleAccountDeleted}
              />
            )}
            {activeTab === 'ai' && <AiTab ai={ai} />}
            {activeTab === 'analytics' && <AnalyticsTab stats={stats} />}
            {activeTab === 'pricing' && <PricingTab plans={plans} />}
            {activeTab === 'help' && (
              <HelpTab openFaq={openFaq} onToggle={(idx) => setOpenFaq(openFaq === idx ? null : idx)} />
            )}
          </div>
        </div>
      </div>
    </div>
  );
}
