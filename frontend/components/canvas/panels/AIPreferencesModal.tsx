import React, { useEffect, useRef, useState } from 'react';
import { X, User, CreditCard, HelpCircle, LogOut, Lock, BarChart3, SlidersHorizontal, Database } from 'lucide-react';
import { useToastStore } from '../../../store/useToastStore';
import { useAuthStore } from '../../../store/useAuthStore';
import { useQuotaStore } from '../../../store/useQuotaStore';
import { useFocusTrap } from '../../../hooks/useFocusTrap';
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

const NAV_ITEMS: { id: PreferencesTab; label: string; icon: typeof User }[] = [
  { id: 'profile', label: 'Profile Settings', icon: User },
  { id: 'account', label: 'Account & Tokens', icon: Lock },
  { id: 'ai', label: 'AI Configurations', icon: SlidersHorizontal },
  { id: 'analytics', label: 'System Analytics', icon: BarChart3 },
  { id: 'pricing', label: 'Pricing', icon: CreditCard },
  { id: 'help', label: 'Help & FAQ', icon: HelpCircle },
];

const HEADINGS: Record<PreferencesTab, { title: string; subtitle: string }> = {
  profile: { title: 'User Profile Settings', subtitle: 'Configure developer identity and portfolio information.' },
  account: { title: 'Account Credentials & Tokens', subtitle: 'View access details, quotas, and manage API tokens.' },
  ai: { title: 'AI Services Routing', subtitle: 'Configure custom LLM routing and advanced parameters.' },
  analytics: { title: 'System Usage & Analytics', subtitle: 'Visual summary of database compiles, seeding logs, and AI request cycles.' },
  pricing: { title: 'Membership Plans', subtitle: 'Explore plan differences and upgrade.' },
  help: { title: 'Help & FAQ', subtitle: 'Frequently Asked Questions & support guidelines.' },
};

/**
 * Hesap, AI ve plan ayarları.
 *
 * Sekmelerin state'i BU bileşende (alt hook'larda) duruyor, sekme
 * bileşenlerinde değil: sekme değiştirmek bileşeni söküp yeniden takıyor ve
 * state orada olsaydı kaydedilmemiş profil düzenlemeleri kaybolur, açılışta
 * bir kez yapılan yüklemeler her geçişte tekrarlanırdı.
 */
export default function AIPreferencesModal({ isOpen, onClose }: AIPreferencesModalProps) {
  const { user, logout, isAuthenticated } = useAuthStore();
  const fetchQuota = useQuotaStore(state => state.fetchQuota);
  const showToast = useToastStore(state => state.showToast);
  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(isOpen, modalRef);

  const [activeTab, setActiveTab] = useState<PreferencesTab>('profile');
  const [openFaq, setOpenFaq] = useState<number | null>(null);
  // Modal her açıldığında açık SSS maddesi kapanıyor.
  useOnOpen(isOpen, () => setOpenFaq(null));

  const profile = useProfileForm(isOpen, isAuthenticated);
  const mfa = useMfaSettings(isOpen, isAuthenticated);
  const ai = useAiSettings(isOpen, onClose);
  const plans = usePlans(isOpen, isAuthenticated);
  const stats = useLocalStats(isOpen);

  useEffect(() => {
    if (isOpen && isAuthenticated) fetchQuota();
  }, [isOpen, isAuthenticated, fetchQuota]);

  if (!isOpen) return null;

  const handleLogout = () => {
    logout();
    showToast('Logged out successfully.', 'info');
    onClose();
  };

  const heading = HEADINGS[activeTab];

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4">
      <div
        className="absolute inset-0 bg-surface-900/80 backdrop-blur-sm"
        onClick={onClose}
      />

      <div ref={modalRef} role="dialog" aria-modal="true" aria-labelledby="ai-pref-title" className="relative w-full max-w-6xl h-[92dvh] md:h-[calc(100dvh-64px)] md:max-h-[880px] bg-surface-800 border border-content-primary/15 rounded-[var(--radius-modal)] flex flex-col md:flex-row overflow-hidden animate-in zoom-in-95 duration-200 text-content-primary">

        {/* Left Sidebar */}
        <div className="w-full md:w-64 bg-surface-700 border-b md:border-b-0 md:border-r border-content-primary/15 p-5 flex flex-col justify-between shrink-0">
          <div className="space-y-6">
            <div className="flex items-center gap-2.5">
              <div className="w-8 h-8 rounded-[var(--radius-control)] bg-white/[0.06] flex items-center justify-center shrink-0">
                <Database className="w-4 h-4 text-content-muted" />
              </div>
              <div>
                <h3 className="text-xs font-extrabold text-content-primary tracking-wide uppercase">
                  Namines Hub
                </h3>
                <p className="text-micro text-content-subtle font-bold tracking-wider uppercase">Settings & Panel</p>
              </div>
            </div>

            {/* Mobilde sekmeler yatay kayıyor. `-mx-5 px-5`: kaydırma kabı
                kenardan kenara uzanıyor, yoksa son sekme panelin iç boşluğunda
                kesiliyor ve listenin devam ettiğine dair hiçbir işaret kalmıyordu
                — kullanıcı Pricing sekmesinin var olduğunu göremiyordu. */}
            <nav className="flex flex-row md:flex-col gap-1 -mx-5 px-5 md:mx-0 md:px-0 overflow-x-auto md:overflow-x-visible pb-2 md:pb-0 scrollbar-none">
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
                    className={`flex items-center gap-2.5 px-3 py-2.5 rounded-[var(--radius-control)] text-xs font-semibold transition-all cursor-pointer select-none whitespace-nowrap ${
                      isActive
                        ? 'bg-white/[0.1] text-content-primary'
                        : 'text-content-muted hover:text-content-secondary hover:bg-white/[0.04]'
                    }`}
                  >
                    <Icon className="w-4 h-4 shrink-0" />
                    <span>{item.label}</span>
                  </button>
                );
              })}
            </nav>
          </div>

          {isAuthenticated && (
            <button
              onClick={handleLogout}
              className="hidden md:flex items-center justify-center gap-2 w-full py-2.5 bg-white/[0.06] hover:bg-danger-text/10 text-content-muted hover:text-danger-text text-xs font-semibold rounded-[var(--radius-control)] transition-all cursor-pointer active:scale-95"
            >
              <LogOut className="w-4 h-4" />
              <span>Log Out</span>
            </button>
          )}
        </div>

        {/* Right Content Pane */}
        <div className="flex-1 flex flex-col h-full overflow-hidden">

          {/* Header */}
          <div className="flex justify-between items-center px-6 py-4 border-b border-content-primary/15 bg-surface-700 shrink-0">
            <div>
              <h4 id="ai-pref-title" className="text-sm font-bold text-content-primary">
                {heading.title}
              </h4>
              <p className="text-[11px] text-content-subtle mt-0.5">
                {heading.subtitle}
              </p>
            </div>
            <button
              onClick={onClose}
              className="p-1.5 rounded-[var(--radius-control)] text-content-subtle hover:text-content-secondary hover:bg-white/[0.06] transition-all cursor-pointer"
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
                userType={user?.type}
                planTier={plans.planTier}
                mfa={mfa}
                onLogout={handleLogout}
              />
            )}
            {activeTab === 'ai' && <AiTab ai={ai} />}
            {activeTab === 'analytics' && <AnalyticsTab stats={stats} />}
            {activeTab === 'pricing' && <PricingTab plans={plans} />}
            {activeTab === 'help' && (
              <HelpTab openFaq={openFaq} onToggle={(idx) => setOpenFaq(openFaq === idx ? null : idx)} />
            )}
          </div>

          {/* Footer */}
          <div className="px-6 py-3 bg-surface-700 border-t border-content-primary/15 flex justify-center items-center text-[10px] text-content-subtle font-mono tracking-widest shrink-0">
            <span>Darvell Labs</span>
          </div>

        </div>
      </div>
    </div>
  );
}
