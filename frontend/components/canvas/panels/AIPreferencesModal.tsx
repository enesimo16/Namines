import React, { useState, useEffect, useRef } from 'react';
import { X, Sparkles, Key, Save, Shield, Settings, User, CreditCard, HelpCircle, LogOut, Check, Lock, Plus, Trash2, Copy, ExternalLink, Globe, BarChart3, Activity, ChevronDown, SlidersHorizontal, Cpu, ArrowLeft, Database } from 'lucide-react';
import { useAIPolicyStore, AIPolicy } from '../../../store/useAIPolicyStore';
import { useByokStore } from '../../../store/useByokStore';
import { useToastStore } from '../../../store/useToastStore';
import { useAuthStore } from '../../../store/useAuthStore';
import { useQuotaStore } from '../../../store/useQuotaStore';
import { authService } from '../../../services/api';
import { useFocusTrap } from '../../../hooks/useFocusTrap';

interface AIPreferencesModalProps {
  isOpen: boolean;
  onClose: () => void;
}

interface ApiToken {
  id: string;
  name: string;
  token: string;
  createdAt: string;
}

// Custom select dropdown component styled with dark ocean-wave theme
interface CustomSelectOption<T> {
  value: T;
  label: string;
}

interface CustomSelectProps<T> {
  value: T;
  onChange: (value: T) => void;
  options: CustomSelectOption<T>[];
  className?: string;
  openUpward?: boolean;
}

function CustomSelect<T extends string | number>({
  value,
  onChange,
  options,
  className = '',
  openUpward = false
}: CustomSelectProps<T>) {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const selectedOption = options.find(opt => opt.value === value);

  return (
    <div className={`relative ${className}`} ref={containerRef}>
      <button
        type="button"
        onClick={() => setIsOpen(!isOpen)}
        className="w-full flex items-center justify-between bg-[#e8f0fa] border border-sky-200 hover:border-sky-350 text-sky-955 text-xs font-semibold py-2 px-3.5 rounded-xl cursor-pointer transition-all duration-200 select-none shadow-[0_1px_2px_rgba(0,0,0,0.05)] focus:border-sky-500 focus:ring-1 focus:ring-sky-500/20"
      >
        <span className="truncate">{selectedOption ? selectedOption.label : value}</span>
        <ChevronDown className={`w-3.5 h-3.5 text-sky-500 transition-transform duration-200 shrink-0 ml-1.5 ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {isOpen && (
        <div 
          className={`absolute left-0 w-full min-w-[200px] max-h-[240px] overflow-y-auto rounded-xl border border-sky-200 bg-[#e8f0fa]/95 backdrop-blur-xl p-1.5 shadow-2xl z-[999] flex flex-col gap-0.5 select-none ${
            openUpward ? 'bottom-full mb-2' : 'top-full mt-2'
          }`}
        >
          {options.map((opt) => {
            const isSelected = opt.value === value;
            return (
              <button
                key={opt.value}
                type="button"
                onClick={() => {
                  onChange(opt.value);
                  setIsOpen(false);
                }}
                className={`flex items-center justify-between w-full px-3 py-2 rounded-lg text-xs font-semibold cursor-pointer transition-all text-left select-none ${
                  isSelected
                    ? 'bg-sky-50 text-sky-700 border-l-2 border-sky-500 pl-2'
                    : 'text-sky-900 hover:bg-sky-50/50 hover:text-sky-950'
                }`}
              >
                <span className="truncate">{opt.label}</span>
                {isSelected && (
                  <Check className="w-3.5 h-3.5 text-sky-600 shrink-0 ml-1.5" />
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

const policyOptions = [
  { value: 0, label: 'Default (Namines)' },
  { value: 1, label: 'Low · Llama 3.2 3B' },
  { value: 2, label: 'Medium · Llama 3.1 8B' },
  { value: 3, label: 'High · Llama 3.3 70B' },
  { value: 6, label: 'High+ · Mixtral 8x7B' },
  { value: 7, label: 'High+ · Gemini 2.5 Flash' },
  { value: 4, label: 'Ultra · GPT-OSS 120B' },
  { value: 8, label: 'Ultra · Gemini 2.5 Pro' },
  { value: 5, label: 'Custom' }
];

const seedDomainOptions = [
  { value: 'general', label: 'General Purpose Domain' },
  { value: 'e-commerce', label: 'E-Commerce & Retail' },
  { value: 'crm', label: 'CRM & Sales Pipelines' },
  { value: 'healthcare', label: 'Healthcare & Clinical ERP' },
  { value: 'fintech', label: 'Fintech & Ledger Auditing' }
];

const docLevelOptions = [
  { value: 'standard', label: 'Standard Level Summary' },
  { value: 'deep', label: 'Deep Architectural Specifications' },
  { value: 'exec', label: 'Executive High-Level Abstract' }
];

const scaffoldOptions = [
  { value: '.net8', label: '.NET 8.0 Entity Framework Core' },
  { value: '.net9', label: '.NET 9.0 Entity Framework Core' },
  { value: 'dapper', label: 'Dapper Lightweight ORM Models' },
  { value: 'ado', label: 'Raw ADO.NET SQL Command Builder' }
];

const dbaSeverityOptions = [
  { value: 'warning', label: 'Warnings and Critical Issues' },
  { value: 'critical', label: 'Critical Vulnerability Blockers Only' },
  { value: 'all', label: 'Verbose Analysis (Include Warnings & Info)' }
];

const tempOptions = [
  { value: '0.0', label: '0.0 (Strictly Deterministic)' },
  { value: '0.2', label: '0.2 (Balanced & Optimal - Recommended)' },
  { value: '0.7', label: '0.7 (Creative Architecture Ideas)' }
];

const promptStyleOptions = [
  { value: 'clean', label: 'Clean Code (Decoupled, Modular)' },
  { value: 'minimalist', label: 'Minimalist (Compact, No Comments)' },
  { value: 'documented', label: 'Richly Documented (Verbose Comments)' }
];

const namingOptions = [
  { value: 'snake_case', label: 'snake_case (PostgreSQL / MySQL)' },
  { value: 'PascalCase', label: 'PascalCase (SQL Server / EF Core)' },
  { value: 'camelCase', label: 'camelCase (MongoDB / Web APIs)' }
];

const fkActionOptions = [
  { value: 'cascade', label: 'ON DELETE CASCADE (Default)' },
  { value: 'restrict', label: 'ON DELETE RESTRICT' },
  { value: 'set_null', label: 'ON DELETE SET NULL' }
];

const maxTokensOptions = [
  { value: '2048', label: '2048 Tokens (Short, Quick Compilation)' },
  { value: '4096', label: '4096 Tokens (Standard, Highly Recommended)' },
  { value: '8192', label: '8192 Tokens (Max Context - Rich Schema)' }
];

const autoIndexOptions = [
  { value: 'true', label: 'Enable Auto-Indexing Suggestions' },
  { value: 'false', label: 'Disable (Explicit Indexing Only)' }
];

const sqlPrettyOptions = [
  { value: 'true', label: 'Enable SQL Pretty Formatting & Indentation' },
  { value: 'false', label: 'Disable Formatting (Raw Fast Stream Output)' }
];

export default function AIPreferencesModal({ isOpen, onClose }: AIPreferencesModalProps) {
  const { policy, updatePolicy, isLoading, fetchPolicy } = useAIPolicyStore();
  const { apiKey, provider, setApiKey, setProvider, clearApiKey } = useByokStore();
  const { user, logout, isAuthenticated } = useAuthStore();
  const { dailyLimit, used, remaining, resetAt, fetchQuota } = useQuotaStore();
  const showToast = useToastStore(state => state.showToast);
  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(isOpen, modalRef);

  const [activeTab, setActiveTab] = useState<'profile' | 'account' | 'ai' | 'pricing' | 'help' | 'analytics'>('profile');
  const [localPolicy, setLocalPolicy] = useState<AIPolicy>({ ...policy });
  const [inputKey, setInputKey] = useState(apiKey || '');
  const [selectedProvider, setSelectedProvider] = useState(provider);
  const [isSavedKey, setIsSavedKey] = useState(!!apiKey);
  const [openFaq, setOpenFaq] = useState<number | null>(null);
  const [isAdvancedUnlocked, setIsAdvancedUnlocked] = useState(false);
  const [showAdvancedScreen, setShowAdvancedScreen] = useState(false);
  const [isUpgrading, setIsUpgrading] = useState(false);
  const [subscriptionStatus, setSubscriptionStatus] = useState<string | null>(null);

  // Developer Profile Identity States (No Professional Role, No Emojis)
  const [fullName, setFullName] = useState('');
  const [companyName, setCompanyName] = useState('');
  const [githubUrl, setGithubUrl] = useState('');
  const [linkedinUrl, setLinkedinUrl] = useState('');
  const [websiteUrl, setWebsiteUrl] = useState('');
  const [twitterUrl, setTwitterUrl] = useState('');
  const [bio, setBio] = useState('');
  const [location, setLocation] = useState('');
  const [isSavingProfile, setIsSavingProfile] = useState(false);

  // Advanced AI Sub-Settings States
  const [seedDomain, setSeedDomain] = useState('general');
  const [docLevel, setDocLevel] = useState('standard');
  const [scaffoldVersion, setScaffoldVersion] = useState('.net8');
  const [dbaSeverity, setDbaSeverity] = useState('warning');
  const [temperature, setTemperature] = useState('0.2');
  const [promptStyle, setPromptStyle] = useState('clean');
  const [namingConvention, setNamingConvention] = useState('snake_case');
  const [fkAction, setFkAction] = useState('cascade');
  const [maxTokens, setMaxTokens] = useState('4096');
  const [sqlPrettyPrint, setSqlPrettyPrint] = useState('true');
  const [autoIndex, setAutoIndex] = useState('true');

  // Analytics & Stats States — derived from real quota store data, not fake hardcoded values
  const [statsSchemas, setStatsSchemas] = useState(0);
  const [statsAiRequests, setStatsAiRequests] = useState(0);
  const [statsDbaAudits, setStatsDbaAudits] = useState(0);
  const [statsMockRecords, setStatsMockRecords] = useState(0);

  // Token Operations States
  const [tokens, setTokens] = useState<ApiToken[]>([]);
  const [newTokenName, setNewTokenName] = useState('');
  const [generatedToken, setGeneratedToken] = useState<string | null>(null);

  useEffect(() => {
    if (isOpen) {
      fetchPolicy().then(() => {
        setLocalPolicy({ ...useAIPolicyStore.getState().policy });
      });
      if (isAuthenticated) {
        fetchQuota();
      }
      setInputKey(apiKey || '');
      setSelectedProvider(provider);
      setIsSavedKey(!!apiKey);
      setOpenFaq(null);
      setIsAdvancedUnlocked(false);

      // Load Profile Identity Details — fetch from backend first, fallback to localStorage
      if (isAuthenticated) {
        authService.getProfile()
          .then(data => {
            if (data) {
              setFullName(data.fullName || localStorage.getItem('namines-full-name') || '');
              setCompanyName(data.companyName || localStorage.getItem('namines-company') || '');
              setGithubUrl(data.githubUrl || localStorage.getItem('namines-github') || '');
              setLinkedinUrl(data.linkedinUrl || localStorage.getItem('namines-linkedin') || '');
              setWebsiteUrl(data.websiteUrl || localStorage.getItem('namines-website') || '');
              setTwitterUrl(data.twitterUrl || '');
              setBio(data.bio || '');
              setLocation(data.location || '');
            }
          })
          .catch(() => {
            setFullName(localStorage.getItem('namines-full-name') || '');
            setCompanyName(localStorage.getItem('namines-company') || '');
            setGithubUrl(localStorage.getItem('namines-github') || '');
            setLinkedinUrl(localStorage.getItem('namines-linkedin') || '');
            setWebsiteUrl(localStorage.getItem('namines-website') || '');
          });
      } else {
        setFullName(localStorage.getItem('namines-full-name') || '');
        setCompanyName(localStorage.getItem('namines-company') || '');
        setGithubUrl(localStorage.getItem('namines-github') || '');
        setLinkedinUrl(localStorage.getItem('namines-linkedin') || '');
        setWebsiteUrl(localStorage.getItem('namines-website') || '');
      }

      // Load Advanced AI Sub-Settings
      setSeedDomain(localStorage.getItem('namines-ai-seed-domain') || 'general');
      setDocLevel(localStorage.getItem('namines-ai-doc-level') || 'standard');
      setScaffoldVersion(localStorage.getItem('namines-ai-scaffold-version') || '.net8');
      setDbaSeverity(localStorage.getItem('namines-ai-dba-severity') || 'warning');
      setTemperature(localStorage.getItem('namines-ai-temperature') || '0.2');
      setPromptStyle(localStorage.getItem('namines-ai-prompt-style') || 'clean');
      setNamingConvention(localStorage.getItem('namines-ai-naming-convention') || 'snake_case');
      setFkAction(localStorage.getItem('namines-ai-fk-action') || 'cascade');
      setMaxTokens(localStorage.getItem('namines-ai-max-tokens') || '4096');
      setSqlPrettyPrint(localStorage.getItem('namines-ai-sql-pretty') || 'true');
      setAutoIndex(localStorage.getItem('namines-ai-auto-index') || 'true');

      // Load Analytics & Stats — only from localStorage, never from hardcoded fake values
      setStatsSchemas(Number(localStorage.getItem('namines-stats-schemas') || '0'));
      setStatsAiRequests(Number(localStorage.getItem('namines-stats-ai-requests') || '0'));
      setStatsDbaAudits(Number(localStorage.getItem('namines-stats-dba-audits') || '0'));
      setStatsMockRecords(Number(localStorage.getItem('namines-stats-mock-records') || '0'));

      // Load API Tokens
      const storedTokens = localStorage.getItem('namines-api-tokens');
      if (storedTokens) {
        try {
          setTokens(JSON.parse(storedTokens));
        } catch {
          setTokens([]);
        }
      } else {
        setTokens([]);
      }
      setGeneratedToken(null);
      setNewTokenName('');

      // Fetch subscription status from backend
      if (isAuthenticated) {
        authService.getSubscriptionStatus()
          .then(data => { if (data) setSubscriptionStatus(data.status); })
          .catch(() => {});
      }
    }
  }, [isOpen, apiKey, provider, isAuthenticated, fetchPolicy, fetchQuota]);

  // Stripe Checkout — redirects user to Stripe's hosted payment page
  const handleUpgrade = async () => {
    setIsUpgrading(true);
    try {
      if (!isAuthenticated) { showToast('Please log in to upgrade.', 'warning'); return; }
      const data = await authService.createCheckoutSession();
      if (data.redirect === 'portal') {
        await handleManageSubscription();
      } else if (data.url) {
        window.location.href = data.url;
      }
    } catch (err) {
      showToast('Checkout could not be initiated. Please try again.', 'error');
    } finally {
      setIsUpgrading(false);
    }
  };

  // Stripe Customer Portal — lets user manage billing, cancel, update card
  const handleManageSubscription = async () => {
    setIsUpgrading(true);
    try {
      if (!isAuthenticated) { showToast('Please log in to manage billing.', 'warning'); return; }
      const data = await authService.createBillingPortal();
      if (data.url) window.open(data.url, '_blank');
    } catch {
      showToast('Could not open billing portal. Please try again.', 'error');
    } finally {
      setIsUpgrading(false);
    }
  };

  if (!isOpen) return null;

  const handleSaveDeveloperSettings = async (e: React.FormEvent) => {
    e.preventDefault();
    // Always save to localStorage as cache
    localStorage.setItem('namines-full-name', fullName);
    localStorage.setItem('namines-company', companyName);
    localStorage.setItem('namines-github', githubUrl);
    localStorage.setItem('namines-linkedin', linkedinUrl);
    localStorage.setItem('namines-website', websiteUrl);

    // Persist to backend if authenticated
    if (isAuthenticated) {
      setIsSavingProfile(true);
      try {
        await authService.updateProfile({ fullName, companyName, githubUrl, linkedinUrl, websiteUrl, twitterUrl, bio, location });
        showToast('Profile saved to cloud successfully.', 'success');
      } catch {
        showToast('Profile saved locally. Cloud sync failed.', 'warning');
      } finally {
        setIsSavingProfile(false);
      }
    } else {
      showToast('Profile saved locally. Log in to sync to cloud.', 'info');
    }
  };

  const handleGenerateToken = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTokenName.trim()) {
      showToast('Please enter a name for the token.', 'warning');
      return;
    }
    const rawToken = 'nam_pat_' + Array.from({ length: 32 }, () => Math.floor(Math.random() * 16).toString(16)).join('');
    const newToken: ApiToken = {
      id: Math.random().toString(36).substring(2, 9),
      name: newTokenName.trim(),
      token: rawToken,
      createdAt: new Date().toLocaleDateString('en-US', { year: 'numeric', month: 'short', day: 'numeric' })
    };
    const updated = [...tokens, newToken];
    setTokens(updated);
    localStorage.setItem('namines-api-tokens', JSON.stringify(updated));
    setGeneratedToken(rawToken);
    setNewTokenName('');
    showToast('Personal access token created.', 'success');
  };

  const handleRevokeToken = (id: string) => {
    const updated = tokens.filter(t => t.id !== id);
    setTokens(updated);
    localStorage.setItem('namines-api-tokens', JSON.stringify(updated));
    showToast('Token revoked successfully.', 'info');
  };

  const copyToClipboard = (text: string) => {
    navigator.clipboard.writeText(text);
    showToast('Copied to clipboard.', 'success');
  };

  const handleSavePolicy = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      const usesByok = Object.entries(localPolicy)
        .filter(([key]) => key !== 'schemaGeneration')
        .some(([_, val]) => val === 5);
        
      if (usesByok && !apiKey && !inputKey.trim()) {
        showToast('Please save your BYOK API key below before selecting BYOK routing.', 'warning');
        return;
      }

      // Save policy + advanced settings
      await updatePolicy(localPolicy);
      localStorage.setItem('namines-ai-seed-domain', seedDomain);
      localStorage.setItem('namines-ai-doc-level', docLevel);
      localStorage.setItem('namines-ai-scaffold-version', scaffoldVersion);
      localStorage.setItem('namines-ai-dba-severity', dbaSeverity);
      localStorage.setItem('namines-ai-temperature', temperature);
      localStorage.setItem('namines-ai-prompt-style', promptStyle);
      localStorage.setItem('namines-ai-naming-convention', namingConvention);
      localStorage.setItem('namines-ai-fk-action', fkAction);
      localStorage.setItem('namines-ai-max-tokens', maxTokens);
      localStorage.setItem('namines-ai-sql-pretty', sqlPrettyPrint);
      localStorage.setItem('namines-ai-auto-index', autoIndex);

      showToast('AI Routing and advanced settings updated.', 'success');
      onClose();
    } catch (err) {
      showToast('Failed to save AI Policies.', 'error');
    }
  };

  const handleSaveKey = (e: React.FormEvent) => {
    e.preventDefault();
    if (!inputKey.trim()) {
      clearApiKey();
      setIsSavedKey(false);
      showToast('BYOK Key removed.', 'info');
      return;
    }
    setApiKey(inputKey.trim());
    setProvider(selectedProvider);
    setIsSavedKey(true);
    showToast('BYOK Key successfully saved.', 'success');
  };

  const handleClearKey = () => {
    clearApiKey();
    setInputKey('');
    setIsSavedKey(false);
    showToast('BYOK Key deleted.', 'info');
  };

  const policyFields = [
    { key: 'smartSeed', label: 'Smart Seeding', desc: 'Mock database records generator (requires AI for domain-aware data).' },
    { key: 'documentation', label: 'Documentation & Reports', desc: 'PDF data dictionary and README files.' },
    { key: 'scaffolding', label: 'Backend Scaffolding Architecture', desc: 'C# DbContext and Model classes.' },
    { key: 'schemaRevision', label: 'Schema Revision', desc: 'AI-powered regional and global schema revision prompts.' },
    { key: 'dbaAnalysis', label: 'DBA Linter & Diagnostics', desc: 'Performance and security analyzer (requires AI — local engine not supported).' },
    { key: 'migration', label: 'Schema Migration', desc: 'C# EF Core migration code generator and parser.' },
    { key: 'voice', label: 'Voice Input & Transcription', desc: 'Whisper-powered speech-to-text transcription for prompts.' }
  ] as const;

  const faqs = [
    {
      q: 'Is my BYOK API key secure?',
      a: (
        <p>
          Yes. Your BYOK API keys are encrypted client-side using obfuscation routines and stored solely in your browser's local storage. They are never sent to or stored on our servers, ensuring maximum privacy.
        </p>
      )
    },
    {
      q: 'How do daily quotas and credit deductions work?',
      a: (
        <div className="space-y-4 text-sky-800">
          <p className="text-[10px] text-sky-750">
            Free members receive a daily <strong className="text-sky-950">100% cloud credit bar</strong>. Daily usage is calculated based on the base cost of the AI feature and the selected AI Model's routing multiplier. If credits are exhausted, features automatically fall back to the local engine to ensure uninterrupted usage.
          </p>
          
          <div className="space-y-2">
            <h5 className="text-[10px] font-bold text-sky-900 uppercase tracking-wider">Base Feature Cost</h5>
            <div className="border border-sky-200 rounded-lg overflow-hidden bg-sky-50/30">
              <table className="w-full text-left border-collapse text-[10px]">
                <thead>
                  <tr className="bg-sky-100/50 border-b border-sky-200 text-sky-850 font-bold uppercase">
                    <th className="py-1.5 px-3">Feature</th>
                    <th className="py-1.5 px-3 text-right">Base Cost</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-sky-150 text-sky-900">
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Schema Generation (Prompt)</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">5%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Smart Seeding</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">5%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Schema Revision</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">8%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">DBA Linter & Diagnostics</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">8%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Backend Scaffolding</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">10%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Schema Migration</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">10%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Documentation & Reports</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">10%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Vision / Reverse Engineer</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">15%</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Voice Input / Transcription</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">5%</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="space-y-2">
            <h5 className="text-[10px] font-bold text-sky-900 uppercase tracking-wider">AI Model Multiplier</h5>
            <div className="border border-sky-200 rounded-lg overflow-hidden bg-sky-50/30">
              <table className="w-full text-left border-collapse text-[10px]">
                <thead>
                  <tr className="bg-sky-100/50 border-b border-sky-200 text-sky-850 font-bold uppercase">
                    <th className="py-1.5 px-3">Routing Engine</th>
                    <th className="py-1.5 px-3 text-right">Multiplier</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-sky-150 text-sky-900">
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Default (Namines) / Local Fallback</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">0x (0%)</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Low — Llama 3.2 3B</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">1x</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Medium — Llama 3.1 8B</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">2x</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">High — Llama 3.3 70B</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">4x</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">High+ — Mixtral 8x7B</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">5x</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">High+ — Gemini 2.5 Flash</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-655 font-bold">5x</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Ultra — GPT-OSS 120B (via Groq)</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">6x</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Ultra — Gemini 2.5 Pro</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-655 font-bold">6x</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 px-3 font-medium">Custom</td>
                    <td className="py-1.5 px-3 text-right font-mono text-sky-600 font-bold">0x (0%)</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="bg-sky-50 border border-sky-200 rounded-lg p-2.5 text-[9.5px] leading-relaxed text-sky-800">
            <strong>Calculation Example:</strong> Running a <span className="text-sky-950 font-bold">DBA Audit (8% base)</span> with the <span className="text-sky-950 font-bold">Medium engine (2x)</span> will deduct <span className="text-sky-600 font-bold">16%</span> from your daily quota. Running it with the Default engine costs <span className="text-sky-600 font-bold">0%</span>.
          </div>
        </div>
      )
    },
    {
      q: 'What is the "Default (Namines)" option?',
      a: (
        <p>
          This option routes your requests through the default Namines local/cloud engine. It runs optimized, standard template generation and local heuristic rules on your browser or free cloud cluster at zero token cost.
        </p>
      )
    },
    {
      q: 'How does cloud sync and backup work?',
      a: (
        <p>
          Your projects are automatically stored in the local WASM database. If you have an active account, your designs are securely uploaded and backed up to our cloud storage, enabling seamless transition across devices.
        </p>
      )
    }
  ];

  return (
    <div className="fixed inset-0 z-[9999] flex items-center justify-center p-4">
      {/* Light Sky Blue Backdrop */}
      <div 
        className="absolute inset-0 bg-sky-950/20 backdrop-blur-sm transition-opacity duration-205"
        onClick={onClose}
      />

      {/* Main Container - Light Ice Blue & Sky Glow Shadows */}
      <div ref={modalRef} role="dialog" aria-modal="true" aria-labelledby="ai-pref-title" className="relative w-full max-w-4xl h-[90vh] md:h-[650px] bg-gradient-to-br from-[#f0f9ff] via-[#e0f2fe] to-[#bae6fd] border border-sky-300/40 shadow-[0_20px_50px_rgba(14,165,233,0.15)] rounded-2xl flex flex-col md:flex-row overflow-hidden animate-in zoom-in-95 duration-200 text-sky-950">
        
        {/* Left Sidebar */}
        <div className="w-full md:w-64 bg-[#e8f0fa]/80 border-b md:border-b-0 md:border-r border-sky-200/50 p-6 flex flex-col justify-between shrink-0">
          <div className="space-y-6">
            {/* Logo Header - Unified Namines Brand Icon */}
            <div className="flex items-center gap-2">
              <svg className="w-6 h-6 drop-shadow-[0_2px_4px_rgba(14,165,233,0.3)] shrink-0" viewBox="0 0 100 100" fill="none">
                <circle cx="50" cy="50" r="46" stroke="url(#circle-grad-modal)" strokeWidth="3" fill="#FFFFFF" />
                <path d="M20,62 C32,48 42,66 52,52 C62,38 72,56 84,42 L84,82 L20,82 Z" fill="url(#wave-grad-modal)" opacity="0.8" />
                <path d="M16,68 C28,56 38,74 50,62 C62,50 72,68 84,56 L84,84 L16,84 Z" fill="url(#wave-grad-2-modal)" opacity="0.4" />
                <circle cx="35" cy="30" r="1.5" fill="#0EA5E9" />
                <circle cx="65" cy="25" r="2" fill="#0EA5E9" />
                <circle cx="50" cy="20" r="1" fill="#0EA5E9" />
                <circle cx="75" cy="35" r="1.2" fill="#0EA5E9" />
                <defs>
                  <linearGradient id="circle-grad-modal" x1="0" y1="0" x2="100" y2="100">
                    <stop offset="0%" stopColor="#06b6d4" />
                    <stop offset="50%" stopColor="#0ea5e9" />
                    <stop offset="100%" stopColor="#14b8a6" />
                  </linearGradient>
                  <linearGradient id="wave-grad-modal" x1="50" y1="30" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                    <stop offset="0%" stopColor="#06b6d4" stopOpacity="0.8" />
                    <stop offset="100%" stopColor="#e0f2fe" stopOpacity="0.1" />
                  </linearGradient>
                  <linearGradient id="wave-grad-2-modal" x1="50" y1="40" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                    <stop offset="0%" stopColor="#0ea5e9" stopOpacity="0.6" />
                    <stop offset="100%" stopColor="#e0f2fe" stopOpacity="0" />
                  </linearGradient>
                </defs>
              </svg>
              <div>
                <h3 className="text-sm font-extrabold text-sky-955 tracking-wide uppercase">
                  Namines Hub
                </h3>
                <p className="text-[9px] text-sky-600 font-bold tracking-wider uppercase">Settings & Panel</p>
              </div>
            </div>
 
            {/* Nav Tabs */}
            <nav className="flex flex-row md:flex-col gap-1.5 overflow-x-auto md:overflow-x-visible pb-2 md:pb-0 scrollbar-none">
              <button
                type="button"
                onClick={() => setActiveTab('profile')}
                className={`flex items-center gap-2.5 px-3.5 py-2.5 rounded-xl text-xs font-bold transition-all cursor-pointer select-none whitespace-nowrap ${
                  activeTab === 'profile'
                    ? 'bg-sky-100/80 text-sky-700 border border-sky-200/50 shadow-[0_2px_8px_rgba(14,165,233,0.08)]'
                    : 'text-sky-600 hover:text-sky-900 hover:bg-sky-50/40'
                }`}
              >
                <User className="w-4 h-4 shrink-0" />
                <span>Profile Settings</span>
              </button>
              <button
                type="button"
                onClick={() => setActiveTab('account')}
                className={`flex items-center gap-2.5 px-3.5 py-2.5 rounded-xl text-xs font-bold transition-all cursor-pointer select-none whitespace-nowrap ${
                  activeTab === 'account'
                    ? 'bg-sky-100/80 text-sky-700 border border-sky-200/50 shadow-[0_2px_8px_rgba(14,165,233,0.08)]'
                    : 'text-sky-600 hover:text-sky-900 hover:bg-sky-50/40'
                }`}
              >
                <Lock className="w-4 h-4 shrink-0" />
                <span>Account & Tokens</span>
              </button>
              <button
                type="button"
                onClick={() => {
                  setActiveTab('ai');
                  setShowAdvancedScreen(false);
                }}
                className={`flex items-center gap-2.5 px-3.5 py-2.5 rounded-xl text-xs font-bold transition-all cursor-pointer select-none whitespace-nowrap ${
                  activeTab === 'ai'
                    ? 'bg-sky-100/80 text-sky-700 border border-sky-200/50 shadow-[0_2px_8px_rgba(14,165,233,0.08)]'
                    : 'text-sky-600 hover:text-sky-900 hover:bg-sky-50/40'
                }`}
              >
                <span className="w-2 h-2 rounded-full bg-sky-500 shrink-0" />
                <span>AI Configurations</span>
              </button>
              <button
                type="button"
                onClick={() => setActiveTab('analytics')}
                className={`flex items-center gap-2.5 px-3.5 py-2.5 rounded-xl text-xs font-bold transition-all cursor-pointer select-none whitespace-nowrap ${
                  activeTab === 'analytics'
                    ? 'bg-sky-100/80 text-sky-700 border border-sky-200/50 shadow-[0_2px_8px_rgba(14,165,233,0.08)]'
                    : 'text-sky-600 hover:text-sky-900 hover:bg-sky-50/40'
                }`}
              >
                <BarChart3 className="w-4 h-4 shrink-0" />
                <span>System Analytics</span>
              </button>
              <button
                type="button"
                onClick={() => setActiveTab('pricing')}
                className={`flex items-center gap-2.5 px-3.5 py-2.5 rounded-xl text-xs font-bold transition-all cursor-pointer select-none whitespace-nowrap ${
                  activeTab === 'pricing'
                    ? 'bg-sky-100/80 text-sky-700 border border-sky-200/50 shadow-[0_2px_8px_rgba(14,165,233,0.08)]'
                    : 'text-sky-600 hover:text-sky-900 hover:bg-sky-50/40'
                }`}
              >
                <CreditCard className="w-4 h-4 shrink-0" />
                <span>Pricing</span>
              </button>
              <button
                type="button"
                onClick={() => setActiveTab('help')}
                className={`flex items-center gap-2.5 px-3.5 py-2.5 rounded-xl text-xs font-bold transition-all cursor-pointer select-none whitespace-nowrap ${
                  activeTab === 'help'
                    ? 'bg-sky-100/80 text-sky-700 border border-sky-200/50 shadow-[0_2px_8px_rgba(14,165,233,0.08)]'
                    : 'text-sky-600 hover:text-sky-900 hover:bg-sky-50/40'
                }`}
              >
                <HelpCircle className="w-4 h-4 shrink-0" />
                <span>Help & FAQ</span>
              </button>
            </nav>
          </div>
 
          {/* Sidebar Footer Logout Button */}
          {isAuthenticated && (
            <button
              onClick={() => {
                logout();
                showToast('Logged out successfully.', 'info');
                onClose();
              }}
              className="hidden md:flex items-center justify-center gap-2 w-full py-2.5 bg-[#e8f0fa] hover:bg-red-50 border border-sky-200 hover:border-red-200 text-sky-600 hover:text-red-500 text-xs font-bold tracking-wider uppercase rounded-xl transition-all cursor-pointer active:scale-95"
            >
              <LogOut className="w-4 h-4" />
              <span>Log Out</span>
            </button>
          )}
        </div>
 
        {/* Right Content Pane */}
        <div className="flex-1 flex flex-col h-full overflow-hidden bg-transparent">
          
          {/* Header */}
          <div className="flex justify-between items-center px-8 py-5 border-b border-sky-100 bg-[#e8f0fa]/90 shrink-0">
            <div>
              <h4 id="ai-pref-title" className="text-sm font-extrabold text-sky-955 uppercase tracking-wider">
                {activeTab === 'profile' && 'User Profile Settings'}
                {activeTab === 'account' && 'Account Credentials & Tokens'}
                {activeTab === 'ai' && 'AI Services Routing'}
                {activeTab === 'analytics' && 'System Usage & Analytics'}
                {activeTab === 'pricing' && 'Membership Plans'}
                {activeTab === 'help' && 'Help & FAQ'}
              </h4>
              <p className="text-[10px] text-sky-600/85 font-bold tracking-wide uppercase mt-0.5">
                {activeTab === 'profile' && 'Configure developer identity and portfolio information.'}
                {activeTab === 'account' && 'View access details, quotas, and manage API tokens.'}
                {activeTab === 'ai' && 'Configure custom LLM routing and advanced parameters.'}
                {activeTab === 'analytics' && 'Visual summary of database compiles, seeding logs, and AI request cycles.'}
                {activeTab === 'pricing' && 'Explore plan differences and upgrade.'}
                {activeTab === 'help' && 'Frequently Asked Questions & support guidelines.'}
              </p>
            </div>
            <button
              onClick={onClose}
              className="p-1.5 rounded-full text-sky-400 hover:text-sky-700 hover:bg-sky-50 transition-all cursor-pointer"
            >
              <X className="w-4.5 h-4.5" />
            </button>
          </div>
 
          {/* Tab Content Panel */}
          <div className="flex-1 p-8 overflow-y-auto custom-scrollbar space-y-6">
            
            {/* 1. Profile Tab (Developer Identity - No Emojis, No Role) */}
            {activeTab === 'profile' && (
              <div className="space-y-6 text-sky-900">
                {!isAuthenticated ? (
                  <div className="flex flex-col items-center justify-center text-center py-12 space-y-4">
                    <div className="p-4 bg-[#e8f0fa]/95 border border-sky-200 rounded-full shadow-[0_4px_12px_rgba(14,165,233,0.08)]">
                      <User className="w-12 h-12 text-sky-500" />
                    </div>
                    <div className="space-y-1">
                      <h3 className="text-sm font-bold text-sky-950">Not Logged In</h3>
                      <p className="text-xs text-sky-600 max-w-xs font-semibold">Please log in to manage your developer profile information.</p>
                    </div>
                  </div>
                ) : (
                  <form onSubmit={handleSaveDeveloperSettings} className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-6 space-y-5 shadow-[0_4px_20px_rgba(14,165,233,0.03)]">
                    <div className="flex items-center gap-2 border-b border-sky-100 pb-3">
                      <User className="w-4 h-4 text-sky-600" />
                      <h4 className="text-xs font-extrabold text-sky-950 uppercase tracking-wider font-sans">Developer Profile Information</h4>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      {/* Full Name */}
                      <div className="space-y-1.5">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">Full Name</label>
                        <input
                          type="text"
                          value={fullName}
                          onChange={(e) => setFullName(e.target.value)}
                          placeholder="John Doe"
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                        />
                      </div>

                      {/* Company Name */}
                      <div className="space-y-1.5">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">Company / Organization</label>
                        <input
                          type="text"
                          value={companyName}
                          onChange={(e) => setCompanyName(e.target.value)}
                          placeholder="Acme Corp"
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                        />
                      </div>

                      {/* Location */}
                      <div className="space-y-1.5">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">Location</label>
                        <input
                          type="text"
                          value={location}
                          onChange={(e) => setLocation(e.target.value)}
                          placeholder="Istanbul, Turkey"
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                        />
                      </div>

                      {/* GitHub Profile URL */}
                      <div className="space-y-1.5">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">GitHub Profile URL</label>
                        <input
                          type="text"
                          value={githubUrl}
                          onChange={(e) => setGithubUrl(e.target.value)}
                          placeholder="https://github.com/username"
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                        />
                      </div>

                      {/* LinkedIn Profile URL */}
                      <div className="space-y-1.5">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">LinkedIn Profile URL</label>
                        <input
                          type="text"
                          value={linkedinUrl}
                          onChange={(e) => setLinkedinUrl(e.target.value)}
                          placeholder="https://linkedin.com/in/username"
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                        />
                      </div>

                      {/* Twitter / X URL */}
                      <div className="space-y-1.5">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">Twitter / X Profile URL</label>
                        <input
                          type="text"
                          value={twitterUrl}
                          onChange={(e) => setTwitterUrl(e.target.value)}
                          placeholder="https://x.com/username"
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                        />
                      </div>

                      {/* Portfolio Website */}
                      <div className="space-y-1.5 md:col-span-2">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">Portfolio Website</label>
                        <input
                          type="text"
                          value={websiteUrl}
                          onChange={(e) => setWebsiteUrl(e.target.value)}
                          placeholder="https://myportfolio.com"
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                        />
                      </div>

                      {/* Bio */}
                      <div className="space-y-1.5 md:col-span-2">
                        <label className="text-[9px] font-bold text-sky-700 uppercase tracking-wider">Short Bio</label>
                        <textarea
                          value={bio}
                          onChange={(e) => setBio(e.target.value)}
                          placeholder="Full-stack developer passionate about databases and scalable architecture..."
                          rows={3}
                          className="w-full px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200 resize-none"
                        />
                      </div>
                    </div>

                    <div className="flex items-center justify-between pt-2">
                      <p className="text-[9px] text-sky-500/80 font-semibold">
                        {isAuthenticated ? 'Saved to cloud + local cache.' : 'Log in to enable cloud sync.'}
                      </p>
                      <button
                        type="submit"
                        disabled={isSavingProfile}
                        className="flex items-center gap-1.5 px-4.5 py-2 bg-sky-600 hover:bg-sky-500 disabled:opacity-60 text-white text-xs font-bold uppercase tracking-wider rounded-xl transition-all cursor-pointer shadow-[0_2px_10px_rgba(14,165,233,0.15)] border border-sky-400/20 active:scale-95 duration-150"
                      >
                        <Save className="w-3.5 h-3.5" />
                        <span>{isSavingProfile ? 'Saving...' : 'Save Profile'}</span>
                      </button>
                    </div>
                  </form>
                )}
              </div>
            )}

            {/* 2. Account & Tokens Tab (No Emojis, Access levels + Token generation) */}
            {activeTab === 'account' && (
              <div className="space-y-6 text-sky-900">
                {!isAuthenticated ? (
                  <div className="flex flex-col items-center justify-center text-center py-12 space-y-4">
                    <div className="p-4 bg-[#e8f0fa]/95 border border-sky-200 rounded-full shadow-[0_4px_12px_rgba(14,165,233,0.08)]">
                      <Lock className="w-12 h-12 text-sky-500" />
                    </div>
                    <div className="space-y-1">
                      <h3 className="text-sm font-bold text-sky-950">Not Logged In</h3>
                      <p className="text-xs text-sky-600 max-w-xs font-semibold">Please log in to manage access levels and API tokens.</p>
                    </div>
                  </div>
                ) : (
                  <div className="space-y-6">
                    {/* Visual Account Badge + Quota */}
                    {(() => {
                      const remainingPercent = Math.round((remaining / dailyLimit) * 100);
                      const isFreeUser = user?.type !== 'corporate';

                      return (
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                          {/* Account Badge Card */}
                          <div className={`${isFreeUser ? 'md:col-span-1' : 'md:col-span-3'} bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-4.5 flex flex-col justify-between shadow-[0_4px_16px_rgba(14,165,233,0.03)]`}>
                            <div className="space-y-1">
                              <span className="text-[9px] font-bold text-sky-600 uppercase tracking-wider">Account Level</span>
                              <p className="text-sm font-bold text-sky-950 truncate">{user?.username}</p>
                            </div>
                            <div className="mt-4">
                              <span className={`inline-block px-3 py-1 rounded-full text-[9px] font-black uppercase tracking-wider ${
                                user?.type === 'corporate'
                                  ? 'bg-amber-50 text-amber-700 border border-amber-200'
                                  : 'bg-sky-50 text-sky-700 border border-sky-200'
                              }`}>
                                {user?.type === 'corporate' ? 'Pro Member' : 'Free Member'}
                              </span>
                            </div>
                          </div>

                          {/* Quota Progress Card - Always Rendered for Free Users */}
                          {isFreeUser && (
                            <div className="md:col-span-2 bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-4.5 space-y-3 shadow-[0_4px_16px_rgba(14,165,233,0.03)]">
                              <div className="flex justify-between items-center text-[10px] font-bold text-sky-700 uppercase tracking-wider">
                                <span>Daily Cloud Credits</span>
                                <div className="flex items-center gap-2">
                                  {resetAt && (
                                    <span className="text-[9px] text-sky-550 font-extrabold normal-case tracking-normal">
                                      (resets at {new Date(resetAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} {new Date(resetAt).toLocaleDateString([], { month: 'short', day: 'numeric' })})
                                    </span>
                                  )}
                                  <span className="text-sky-955 font-bold">
                                    {remainingPercent}% left
                                  </span>
                                </div>
                              </div>
                              <div className="w-full h-2 bg-sky-100/50 rounded-full overflow-hidden border border-sky-200/50">
                                <div 
                                  className="h-full bg-gradient-to-r from-sky-400 via-sky-500 to-sky-600 transition-all duration-500"
                                  style={{ width: `${remainingPercent}%` }}
                                />
                              </div>
                              <p className="text-[9.5px] text-sky-700/85 font-medium leading-normal">
                                {remainingPercent <= 20 
                                  ? `Warning: Your daily cloud credits are running low (${remainingPercent}% remaining). Default local engine compiles code when exhausted.`
                                  : `Your daily cloud credits quota is active (${remainingPercent}% remaining). Choose higher tier models in AI Configurations tab.`
                                  }
                              </p>
                            </div>
                          )}
                        </div>
                      );
                    })()}

                    {/* License Authorization Levels */}
                    <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-5 space-y-4 shadow-[0_4px_16px_rgba(14,165,233,0.03)]">
                      <div className="flex items-center gap-2 border-b border-sky-100 pb-3">
                        <Lock className="w-4 h-4 text-sky-650" />
                        <h4 className="text-xs font-extrabold text-sky-950 uppercase tracking-wider">License Authorization Levels</h4>
                      </div>

                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3.5">
                        <div className="flex items-center justify-between p-3.5 bg-sky-50/45 border border-sky-100 rounded-xl">
                          <span className="text-[10px] font-bold text-sky-800 uppercase tracking-wider">SignalR Realtime Multiplayer</span>
                          <span className={`text-[9px] font-extrabold px-2 py-0.5 rounded ${
                            user?.type === 'corporate' ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-sky-100/40 text-sky-500/60 border border-sky-200/40'
                          }`}>
                            {user?.type === 'corporate' ? 'ENABLED' : 'DISABLED'}
                          </span>
                        </div>
                        <div className="flex items-center justify-between p-3.5 bg-sky-50/45 border border-sky-100 rounded-xl">
                          <span className="text-[10px] font-bold text-sky-800 uppercase tracking-wider">Automated DBA Linter</span>
                          <span className={`text-[9px] font-extrabold px-2 py-0.5 rounded ${
                            user?.type === 'corporate' ? 'bg-emerald-50 text-emerald-700 border border-emerald-200' : 'bg-sky-50 text-sky-700 border border-sky-200'
                          }`}>
                            {user?.type === 'corporate' ? 'UNLIMITED' : '100% DAILY'}
                          </span>
                        </div>
                        <div className="flex items-center justify-between p-3.5 bg-sky-50/45 border border-sky-100 rounded-xl">
                          <span className="text-[10px] font-bold text-sky-800 uppercase tracking-wider">Cloud Workspace Sync</span>
                          <span className="text-[9px] font-extrabold px-2 py-0.5 rounded bg-emerald-50 text-emerald-700 border border-emerald-200">
                            ACTIVE
                          </span>
                        </div>
                        <div className="flex items-center justify-between p-3.5 bg-sky-50/45 border border-sky-100 rounded-xl">
                          <span className="text-[10px] font-bold text-sky-800 uppercase tracking-wider">API Quota Bypass (BYOK)</span>
                          <span className="text-[9px] font-extrabold px-2 py-0.5 rounded bg-emerald-50 text-emerald-700 border border-emerald-200">
                            SUPPORTED
                          </span>
                        </div>
                      </div>
                    </div>

                    {/* Personal Access Tokens */}
                    <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-5 space-y-4 shadow-[0_4px_16px_rgba(14,165,233,0.03)]">
                      <div className="flex items-center gap-2 border-b border-sky-100 pb-3">
                        <Key className="w-4 h-4 text-sky-650" />
                        <h4 className="text-xs font-extrabold text-sky-950 uppercase tracking-wider">Personal Access Tokens</h4>
                      </div>

                      <form onSubmit={handleGenerateToken} className="space-y-3">
                        <div className="space-y-1">
                          <span className="text-[10px] font-bold text-sky-850 uppercase tracking-wider">Generate New Token</span>
                          <p className="text-[10px] text-sky-600 leading-normal">Authenticate programmatic tools or webhook requests with unique API tokens.</p>
                        </div>
                        <div className="flex gap-2">
                          <input
                            type="text"
                            value={newTokenName}
                            onChange={(e) => setNewTokenName(e.target.value)}
                            placeholder="e.g., CI/CD deploy runner"
                            className="flex-1 px-3.5 py-2 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-950 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all duration-200"
                          />
                          <button
                            type="submit"
                            className="flex items-center gap-1.5 px-4 py-2 bg-sky-600 hover:bg-sky-550 text-white text-xs font-bold uppercase tracking-wider rounded-xl transition-all cursor-pointer shadow-sm border border-sky-300/40"
                          >
                            <Plus className="w-4 h-4" />
                            <span>Generate</span>
                          </button>
                        </div>
                      </form>

                      {generatedToken && (
                        <div className="bg-sky-50 border border-sky-200 rounded-xl p-4 space-y-2.5">
                          <div className="flex justify-between items-center">
                            <span className="text-xs font-bold text-sky-700">Token Generated Successfully</span>
                            <button
                              type="button"
                              onClick={() => copyToClipboard(generatedToken)}
                              className="flex items-center gap-1 px-2.5 py-1 bg-[#e8f0fa] hover:bg-sky-50 border border-sky-250 rounded-lg text-[10px] font-bold text-sky-700 transition-colors cursor-pointer"
                            >
                              <Copy className="w-3.5 h-3.5 text-sky-500" />
                              <span>Copy Token</span>
                            </button>
                          </div>
                          <div className="font-mono text-xs bg-sky-100/50 border border-sky-200 p-2.5 rounded-lg select-all text-sky-900 break-all">
                            {generatedToken}
                          </div>
                          <p className="text-[10px] text-sky-500/80 font-semibold">Make sure to copy this access token. It will not be shown again.</p>
                        </div>
                      )}

                      <div className="space-y-2.5 pt-2">
                        <span className="text-[10px] font-bold text-sky-850 uppercase tracking-wider">Active Tokens</span>
                        {tokens.length === 0 ? (
                          <p className="text-xs text-sky-500 italic">No access tokens active.</p>
                        ) : (
                          <div className="border border-sky-200 bg-sky-50/10 rounded-xl overflow-hidden">
                            <table className="w-full text-left border-collapse text-xs">
                                <thead>
                                  <tr className="bg-sky-100/50 border-b border-sky-200 text-sky-800 font-bold tracking-wider text-[9px] uppercase">
                                    <th className="py-2.5 px-4">Name</th>
                                    <th className="py-2.5 px-4">Created</th>
                                    <th className="py-2.5 px-4 text-right">Action</th>
                                  </tr>
                                </thead>
                                <tbody className="divide-y divide-sky-150">
                                  {tokens.map((tok) => (
                                    <tr key={tok.id} className="hover:bg-sky-50 text-sky-955">
                                      <td className="py-3 px-4 font-bold">{tok.name}</td>
                                      <td className="py-3 px-4 text-sky-600/80">{tok.createdAt}</td>
                                      <td className="py-2.5 px-4 text-right">
                                        <button
                                          type="button"
                                          onClick={() => handleRevokeToken(tok.id)}
                                          className="p-1.5 text-red-500 hover:text-red-600 hover:bg-red-50 rounded-lg transition-colors cursor-pointer"
                                          title="Revoke Token"
                                        >
                                          <Trash2 className="w-4.5 h-4.5" />
                                        </button>
                                      </td>
                                    </tr>
                                  ))}
                                </tbody>
                            </table>
                          </div>
                        )}
                      </div>
                    </div>

                    {/* Mobile Log Out */}
                    <button
                      onClick={() => {
                        logout();
                        showToast('Logged out successfully.', 'info');
                        onClose();
                      }}
                      className="flex md:hidden items-center justify-center gap-2 w-full py-2.5 bg-[#e8f0fa] hover:bg-red-50 border border-sky-200 hover:border-red-200 text-sky-600 hover:text-red-500 text-xs font-bold tracking-wider uppercase rounded-xl transition-all cursor-pointer"
                    >
                      <LogOut className="w-4 h-4" />
                      <span>Log Out</span>
                    </button>
                  </div>
                )}
              </div>
            )}
            
            {/* 3. AI Settings Tab */}
            {activeTab === 'ai' && (
              <div className="space-y-6 text-sky-900 animate-in fade-in duration-200">
                {!showAdvancedScreen ? (
                  // SCREEN A: Main AI Settings Routing & BYOK Credentials
                  <div className="space-y-6">
                    <form onSubmit={handleSavePolicy} className="space-y-6">
                      {/* AI Services Routing Card */}
                      <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl px-6 py-2 shadow-[0_4px_16px_rgba(14,165,233,0.03)]">
                        <div className="py-4 flex items-center justify-between border-b border-sky-100 mb-4 gap-4">
                          <div className="flex items-center gap-2">
                            <span className="w-2.5 h-2.5 rounded-full bg-sky-500 shrink-0" />
                            <h4 className="text-xs font-extrabold text-sky-955 uppercase tracking-wider font-sans">AI Services Engine Routing</h4>
                          </div>
                          <button
                            type="button"
                            onClick={() => setShowAdvancedScreen(true)}
                            className="flex items-center gap-1.5 text-[10px] font-bold text-sky-600 hover:text-sky-850 bg-sky-50/50 hover:bg-sky-50 px-2.5 py-1.5 border border-sky-200 hover:border-sky-300 rounded-lg shrink-0 transition-colors cursor-pointer"
                          >
                            <SlidersHorizontal className="w-3.5 h-3.5" />
                            <span>Advanced Settings</span>
                          </button>
                        </div>

                        {/* Cost Multiplier Legend */}
                        <div className="py-3 px-4 bg-sky-50/50 border border-sky-100 rounded-xl mb-6 text-[10px] space-y-2">
                          <p className="font-bold text-sky-850 uppercase tracking-wider">AI Cost Multiplier Legend</p>
                          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-2">
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">Default</span>
                              <span className="text-sky-600 font-bold">0% (Local)</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">Low</span>
                              <span className="text-sky-600 font-bold">1x · Llama 3.2</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">Medium</span>
                              <span className="text-sky-600 font-bold">2x · Llama 3.1</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">High</span>
                              <span className="text-sky-600 font-bold">4x · Llama 3.3</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">High+</span>
                              <span className="text-sky-600 font-bold">5x · Mixtral</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">High+ G</span>
                              <span className="text-teal-600 font-bold">5x · Flash</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">Ultra</span>
                              <span className="text-sky-600 font-bold">6x · GPT-OSS</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">Ultra G</span>
                              <span className="text-teal-600 font-bold">6x · Gem Pro</span>
                            </div>
                            <div className="p-1.5 bg-[#e8f0fa] rounded border border-sky-200/70 text-center">
                              <span className="block font-black text-sky-900">Custom</span>
                              <span className="text-sky-600 font-bold">0% (Custom)</span>
                            </div>
                          </div>
                        </div>

                        <div className="divide-y divide-sky-100">
                          {/* Dynamic Policy Routing Fields */}
                          {policyFields.map((field) => (
                            <div key={field.key} className="grid grid-cols-12 gap-4 items-center py-4.5">
                              <div className="col-span-12 md:col-span-8 space-y-0.5">
                                <h5 className="text-xs font-extrabold text-sky-900">{field.label}</h5>
                                <p className="text-[10px] text-sky-600/90 leading-normal">
                                  {field.key === 'dbaAnalysis' 
                                    ? 'Performance and security analyzer (falls back to local rules engine on Default/Namines).'
                                    : field.key === 'schemaRevision'
                                      ? 'AI-powered regional and global schema revision prompts (falls back to local rules on Default/Namines).'
                                      : field.desc
                                  }
                                </p>
                              </div>
                              <div className="col-span-12 md:col-span-4 flex justify-end">
                                <CustomSelect
                                  value={localPolicy[field.key]}
                                  onChange={(val) => setLocalPolicy({ ...localPolicy, [field.key]: val })}
                                  options={policyOptions}
                                  className="w-full max-w-[220px]"
                                />
                              </div>
                            </div>
                          ))}
                        </div>
                      </div>

                      <button
                        type="submit"
                        disabled={isLoading}
                        className="w-full py-2.5 bg-sky-600 hover:bg-sky-550 text-white font-bold text-xs tracking-wider uppercase rounded-xl shadow-md border border-sky-300/40 transition-all duration-200 cursor-pointer flex items-center justify-center gap-1.5 active:scale-[0.99]"
                      >
                        <span>{isLoading ? 'Saving...' : 'Save AI Routing'}</span>
                      </button>
                    </form>

                    {/* BYOK Section */}
                    <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-5 space-y-4 shadow-[0_4px_16px_rgba(14,165,233,0.03)]">
                      <div className="space-y-1">
                        <h3 className="text-xs font-extrabold text-sky-955 tracking-tight">BYOK Credentials</h3>
                        <p className="text-[10px] text-sky-600 leading-normal">Supply custom API tokens to completely bypass default platform request quotas.</p>
                      </div>

                      <form onSubmit={handleSaveKey} className="space-y-3.5">
                        <div className="flex gap-1.5">
                          {(['groq', 'openai', 'anthropic', 'gemini'] as const).map((prov) => (
                            <button
                              key={prov}
                              type="button"
                              onClick={() => {
                                if (!isSavedKey) setSelectedProvider(prov);
                              }}
                              disabled={isSavedKey}
                              className={`flex-1 py-2 text-[9px] font-black uppercase tracking-wider rounded-xl transition-all border cursor-pointer ${
                                selectedProvider === prov
                                  ? 'bg-sky-100 border-sky-300 text-sky-700 shadow-sm'
                                  : 'bg-sky-50/40 border-sky-200 text-sky-600 hover:bg-sky-50/90 hover:text-sky-850'
                              }`}
                            >
                              {prov}
                            </button>
                          ))}
                        </div>

                        <div className="space-y-1">
                          <div className="relative">
                            <input
                              type="password"
                              value={inputKey}
                              onChange={(e) => setInputKey(e.target.value)}
                              disabled={isSavedKey}
                              placeholder={isSavedKey ? "••••••••••••••••••••" : `Enter ${selectedProvider.toUpperCase()} Key`}
                              className="w-full px-3.5 py-2.5 bg-sky-50/40 border border-sky-200 rounded-xl text-xs text-sky-955 placeholder-sky-350 focus:outline-none focus:border-sky-500 focus:bg-[#e8f0fa] focus:ring-1 focus:ring-sky-500/20 transition-all font-mono"
                            />
                            <div className="absolute right-3.5 top-1/2 -translate-y-1/2 text-sky-500">
                              <Key className="w-3.5 h-3.5" />
                            </div>
                          </div>
                        </div>

                        {isSavedKey ? (
                          <div className="flex gap-2">
                            <div className="flex-1 py-2 px-3 bg-emerald-50 border border-emerald-250 rounded-xl flex items-center gap-2 text-emerald-700 text-xs font-bold font-mono">
                              <Shield className="w-3.5 h-3.5 text-emerald-600" />
                              <span>Decryption Key Locked</span>
                            </div>
                            <button
                              type="button"
                              onClick={handleClearKey}
                              className="px-4 bg-sky-50 hover:bg-red-50 border border-sky-250 hover:border-red-200 text-sky-700 hover:text-red-500 text-xs font-bold rounded-xl transition-all cursor-pointer"
                            >
                              Delete Key
                            </button>
                          </div>
                        ) : (
                          <button
                            type="submit"
                            className="w-full py-2.5 bg-sky-600 hover:bg-sky-550 text-white font-bold text-xs tracking-wider uppercase rounded-xl shadow-sm border border-sky-300/40 transition-all duration-200 cursor-pointer"
                          >
                            Save API Key
                          </button>
                        )}
                      </form>
                    </div>
                  </div>
                ) : (
                  // SCREEN B: Exclusive Advanced Settings (Translated to English)
                  <div className="space-y-6 animate-in fade-in duration-200">
                    {/* Header Controls */}
                    <div className="flex items-center justify-between border-b border-sky-100 pb-4 gap-4">
                      <button
                        type="button"
                        onClick={() => setShowAdvancedScreen(false)}
                        className="flex items-center gap-2 px-3.5 py-2 text-xs font-bold text-sky-700 hover:text-sky-900 bg-[#e8f0fa] hover:bg-sky-50 border border-sky-200 rounded-xl shadow-[0_1px_3px_rgba(0,0,0,0.05)] transition-all cursor-pointer select-none active:scale-95"
                      >
                        <ArrowLeft className="w-4 h-4 text-sky-500" />
                        <span>Back to AI Routing</span>
                      </button>
                      <h4 className="text-xs font-extrabold text-sky-955 uppercase tracking-wider font-sans">
                        Advanced AI Tuning & Generation Parameters
                      </h4>
                    </div>

                    {/* Caution Warning Banner */}
                    <div className="flex gap-2.5 p-3.5 bg-amber-50 border border-amber-200 text-amber-900 rounded-xl">
                      <Shield className="w-4 h-4 shrink-0 text-amber-600 mt-0.5" />
                      <div className="text-[9.5px] leading-normal font-semibold">
                        <span className="font-extrabold uppercase text-amber-700">Caution:</span> Altering default prompt settings, delete action policies, or temperature variables can result in unexpected compilation failures or syntax differences. Proceed with caution.
                      </div>
                    </div>

                    {/* Settings Form */}
                    <form 
                      onSubmit={(e) => {
                        e.preventDefault();
                        // Save to localStorage
                        localStorage.setItem('namines-ai-seed-domain', seedDomain);
                        localStorage.setItem('namines-ai-doc-level', docLevel);
                        localStorage.setItem('namines-ai-scaffold-version', scaffoldVersion);
                        localStorage.setItem('namines-ai-dba-severity', dbaSeverity);
                        localStorage.setItem('namines-ai-temperature', temperature);
                        localStorage.setItem('namines-ai-prompt-style', promptStyle);
                        localStorage.setItem('namines-ai-naming-convention', namingConvention);
                        localStorage.setItem('namines-ai-fk-action', fkAction);
                        localStorage.setItem('namines-ai-max-tokens', maxTokens);
                        localStorage.setItem('namines-ai-sql-pretty', sqlPrettyPrint);
                        localStorage.setItem('namines-ai-auto-index', autoIndex);
                        showToast('Advanced AI configurations saved successfully.', 'success');
                      }} 
                      className="space-y-6"
                    >
                      <div className="bg-[#e8f0fa]/95 border border-sky-200/80 rounded-2xl p-6 shadow-[0_4px_20px_rgba(14,165,233,0.04)]">
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
                          
                          {/* Smart Seeding Domain */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">Seeding Mock Domain</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Vertical semantic context for mock table values.</span>
                            </div>
                            <CustomSelect
                              value={seedDomain}
                              onChange={(val) => setSeedDomain(val)}
                              options={seedDomainOptions}
                              className="w-full"
                            />
                          </div>

                          {/* Documentation Technical Level */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">Documentation Technical Detail</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Complexity depth of generated READMEs & documentation.</span>
                            </div>
                            <CustomSelect
                              value={docLevel}
                              onChange={(val) => setDocLevel(val)}
                              options={docLevelOptions}
                              className="w-full"
                            />
                          </div>

                          {/* Scaffolding Framework Version */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">Target Scaffolder Framework</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">C# classes compiler compilation target framework.</span>
                            </div>
                            <CustomSelect
                              value={scaffoldVersion}
                              onChange={(val) => setScaffoldVersion(val)}
                              options={scaffoldOptions}
                              className="w-full"
                            />
                          </div>

                          {/* DBA Diagnostic Severity filter */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">DBA Diagnostic Severity</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Severity threshold filter for diagnostics reports.</span>
                            </div>
                            <CustomSelect
                              value={dbaSeverity}
                              onChange={(val) => setDbaSeverity(val)}
                              options={dbaSeverityOptions}
                              className="w-full"
                            />
                          </div>

                          {/* AI Temperature / Creativity */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">AI Temperature (Creativity)</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Controls randomness vs structural correctness.</span>
                            </div>
                            <CustomSelect
                              value={temperature}
                              onChange={(val) => setTemperature(val)}
                              options={tempOptions}
                              className="w-full"
                            />
                          </div>

                          {/* AI Prompt Coding Style */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">Code Presentation Style</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Inline comments, naming density, and formatting.</span>
                            </div>
                            <CustomSelect
                              value={promptStyle}
                              onChange={(val) => setPromptStyle(val)}
                              options={promptStyleOptions}
                              className="w-full"
                            />
                          </div>

                          {/* Database Naming Conventions */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">SQL Schema Naming Standard</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Case formats applied to tables and columns.</span>
                            </div>
                            <CustomSelect
                              value={namingConvention}
                              onChange={(val) => setNamingConvention(val)}
                              options={namingOptions}
                              className="w-full"
                            />
                          </div>

                          {/* Foreign Key Delete Rule */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">Foreign Key Action Rule</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Referential integrity behavior on parent row delete.</span>
                            </div>
                            <CustomSelect
                              value={fkAction}
                              onChange={(val) => setFkAction(val)}
                              options={fkActionOptions}
                              className="w-full"
                            />
                          </div>

                          {/* Context Window / Max Tokens limit */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">Context Output Token Limit</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Limits maximum text tokens in compiled files.</span>
                            </div>
                            <CustomSelect
                              value={maxTokens}
                              onChange={(val) => setMaxTokens(val)}
                              options={maxTokensOptions}
                              className="w-full"
                            />
                          </div>

                          {/* Auto-Index Generation */}
                          <div className="space-y-1.5">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">Auto Foreign Key Indexes</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Automatically suggest index DDL on relationships.</span>
                            </div>
                            <CustomSelect
                              value={autoIndex}
                              onChange={(val) => setAutoIndex(val)}
                              options={autoIndexOptions}
                              className="w-full"
                            />
                          </div>

                          {/* SQL Pretty Print formatter */}
                          <div className="space-y-1.5 md:col-span-2">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-extrabold text-sky-900">SQL Formatting (Pretty Print)</span>
                              <span className="text-[9.5px] text-sky-600/85 mt-0.5">Ensures code output block is parsed and formatted.</span>
                            </div>
                            <CustomSelect
                              value={sqlPrettyPrint}
                              onChange={(val) => setSqlPrettyPrint(val)}
                              options={sqlPrettyOptions}
                              className="w-full"
                            />
                          </div>

                        </div>
                      </div>

                      <button
                        type="submit"
                        className="w-full py-2.5 bg-sky-600 hover:bg-sky-500 text-white font-bold text-xs tracking-wider uppercase rounded-xl shadow-md border border-sky-400/20 transition-all duration-200 cursor-pointer flex items-center justify-center gap-1.5 active:scale-[0.99]"
                      >
                        <Save className="w-4 h-4" />
                        <span>Save Advanced Settings</span>
                      </button>
                    </form>
                  </div>
                )}
              </div>
            )}
             {/* 4. System Analytics & Stats Tab */}
            {activeTab === 'analytics' && (
              <div className="space-y-6 text-sky-900 animate-in fade-in duration-200">
                {/* Stats Summary Widgets */}
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                  {/* Card 1: Schemas Compiled */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/60 rounded-2xl p-5 shadow-[0_4px_16px_rgba(14,165,233,0.03)] flex items-center justify-between hover:scale-[1.02] transition-all duration-200">
                    <div className="space-y-1">
                      <span className="text-[9px] font-bold text-sky-600 uppercase tracking-wider">Schemas Compiled</span>
                      <p className="text-2xl font-black text-sky-955">{statsSchemas}</p>
                      <div className="text-[8px] font-bold text-emerald-600 uppercase tracking-wide">Local WASM Compiler</div>
                    </div>
                  </div>

                  {/* Card 2: AI Credits Used */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/60 rounded-2xl p-5 shadow-[0_4px_16px_rgba(14,165,233,0.03)] flex items-center justify-between hover:scale-[1.02] transition-all duration-200">
                    <div className="space-y-1">
                      <span className="text-[9px] font-bold text-sky-600 uppercase tracking-wider">AI Credits Used</span>
                      <p className="text-2xl font-black text-sky-600">{Math.round((used / dailyLimit) * 100)}%</p>
                      <div className="text-[8px] font-bold text-sky-500 uppercase tracking-wide">{Math.round((remaining / dailyLimit) * 100)}% Remaining Today</div>
                    </div>
                  </div>

                  {/* Card 3: DBA Audits Run */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/60 rounded-2xl p-5 shadow-[0_4px_16px_rgba(14,165,233,0.03)] flex items-center justify-between hover:scale-[1.02] transition-all duration-200">
                    <div className="space-y-1">
                      <span className="text-[9px] font-bold text-sky-600 uppercase tracking-wider">DBA Audits Run</span>
                      <p className="text-2xl font-black text-amber-600">{statsDbaAudits}</p>
                      <div className="text-[8px] font-bold text-amber-500 uppercase tracking-wide font-semibold">Linter Session</div>
                    </div>
                  </div>

                  {/* Card 4: Mock Rows Seeded */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/60 rounded-2xl p-5 shadow-[0_4px_16px_rgba(14,165,233,0.03)] flex items-center justify-between hover:scale-[1.02] transition-all duration-200">
                    <div className="space-y-1">
                      <span className="text-[9px] font-bold text-sky-600 uppercase tracking-wider">Mock Rows Seeded</span>
                      <p className="text-2xl font-black text-teal-600">{statsMockRecords}</p>
                      <div className="text-[8px] font-bold text-teal-500 uppercase tracking-wide font-semibold">Smart Seeding Engine</div>
                    </div>
                  </div>
                </div>

                {/* Dashboard Visualization Column Layout (alt alta hepsini büyük) */}
                <div className="space-y-6 flex flex-col w-full">
                  {/* AI Engine Routing Allocation */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-6 shadow-[0_4px_16px_rgba(14,165,233,0.03)] space-y-4 w-full">
                    <div className="flex justify-between items-center text-[10px] font-bold text-sky-750 uppercase tracking-wider">
                      <span>AI Engine Allocation Share</span>
                      <span className="text-sky-600 text-[9px] font-extrabold">COMPILATION BREAKDOWN</span>
                    </div>
                    
                    {/* Stacked bar display - Larger h-7 for readability */}
                    <div className="w-full h-7 rounded-lg overflow-hidden flex shadow-inner border border-sky-200/70 bg-sky-100/50">
                      <div className="h-full bg-sky-300 hover:opacity-90 transition-opacity" style={{ width: '55%' }} title="Local Engine: 55%" />
                      <div className="h-full bg-sky-400 hover:opacity-90 transition-opacity" style={{ width: '25%' }} title="Llama 8B: 25%" />
                      <div className="h-full bg-sky-600/90 hover:opacity-90 transition-opacity" style={{ width: '15%' }} title="Llama 70B: 15%" />
                      <div className="h-full bg-teal-500 hover:opacity-90 transition-opacity" style={{ width: '5%' }} title="Custom: 5%" />
                    </div>

                    {/* Legend - Clean spacing */}
                    <div className="flex flex-wrap gap-x-6 gap-y-2 text-[10px] font-extrabold text-sky-700 uppercase tracking-wide">
                      <div className="flex items-center gap-2">
                        <div className="w-3.5 h-3.5 rounded bg-sky-300" />
                        <span>Local (55%)</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <div className="w-3.5 h-3.5 rounded bg-sky-400" />
                        <span>Llama 8B (25%)</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <div className="w-3.5 h-3.5 rounded bg-sky-600/90" />
                        <span>Llama 70B (15%)</span>
                      </div>
                      <div className="flex items-center gap-2">
                        <div className="w-3.5 h-3.5 rounded bg-teal-500" />
                        <span>Custom (5%)</span>
                      </div>
                    </div>
                  </div>

                  {/* Database Dialect Target Distribution */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-6 shadow-[0_4px_16px_rgba(14,165,233,0.03)] space-y-4 w-full">
                    <span className="text-[10px] font-bold text-sky-750 uppercase tracking-wider block">Target Database Compilations</span>
                    
                    {/* Horizontal progress rows layout with larger spacing and width */}
                    <div className="space-y-4">
                      {/* Postgres */}
                      <div className="space-y-1.5">
                        <div className="flex justify-between text-xs font-extrabold text-sky-850">
                          <span>PostgreSQL</span>
                          <span className="text-sky-600">45%</span>
                        </div>
                        <div className="w-full h-3 bg-sky-100/50 rounded-full overflow-hidden border border-sky-200/40">
                          <div className="h-full bg-sky-500 rounded-full" style={{ width: '45%' }} />
                        </div>
                      </div>

                      {/* SQL Server */}
                      <div className="space-y-1.5">
                        <div className="flex justify-between text-xs font-extrabold text-sky-850">
                          <span>Microsoft SQL Server</span>
                          <span className="text-sky-600">25%</span>
                        </div>
                        <div className="w-full h-3 bg-sky-100/50 rounded-full overflow-hidden border border-sky-200/40">
                          <div className="h-full bg-sky-500 rounded-full" style={{ width: '25%' }} />
                        </div>
                      </div>

                      {/* MySQL */}
                      <div className="space-y-1.5">
                        <div className="flex justify-between text-xs font-extrabold text-sky-850">
                          <span>MySQL / MariaDB</span>
                          <span className="text-sky-600">18%</span>
                        </div>
                        <div className="w-full h-3 bg-sky-100/50 rounded-full overflow-hidden border border-sky-200/40">
                          <div className="h-full bg-sky-550 rounded-full" style={{ width: '18%' }} />
                        </div>
                      </div>

                      {/* SQLite */}
                      <div className="space-y-1.5">
                        <div className="flex justify-between text-xs font-extrabold text-sky-850">
                          <span>SQLite (Embedded)</span>
                          <span className="text-sky-600">12%</span>
                        </div>
                        <div className="w-full h-3 bg-sky-100/50 rounded-full overflow-hidden border border-sky-200/40">
                          <div className="h-full bg-sky-400 rounded-full" style={{ width: '12%' }} />
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Weekly Performance Metrics */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-6 shadow-[0_4px_16px_rgba(14,165,233,0.03)] space-y-4 w-full">
                    <span className="text-[10px] font-bold text-sky-750 uppercase tracking-wider block">Weekly Performance Metrics</span>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
                      <div className="p-4 bg-sky-50/40 rounded-xl border border-sky-200/60 space-y-2.5">
                        <div className="flex justify-between text-[11px] font-extrabold uppercase text-sky-850">
                          <span>WASM Compiler Success Rate</span>
                          <span className="text-emerald-700 font-black">100%</span>
                        </div>
                        <div className="w-full h-2.5 bg-sky-100/60 rounded-full overflow-hidden border border-sky-200/40">
                          <div className="h-full bg-emerald-500 rounded-full" style={{ width: '100%' }} />
                        </div>
                      </div>
                      <div className="p-4 bg-sky-50/40 rounded-xl border border-sky-200/60 space-y-2.5">
                        <div className="flex justify-between text-[11px] font-extrabold uppercase text-sky-850">
                          <span>DBA Integrity Rating</span>
                          <span className="text-sky-750 font-black">94.8%</span>
                        </div>
                        <div className="w-full h-2.5 bg-sky-100/60 rounded-full overflow-hidden border border-sky-200/40">
                          <div className="h-full bg-sky-500 rounded-full" style={{ width: '94.8%' }} />
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Operations History Log */}
                  <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-6 shadow-[0_4px_16px_rgba(14,165,233,0.03)] space-y-4 w-full">
                    <div className="border-b border-sky-100 pb-3">
                      <h4 className="text-xs font-extrabold text-sky-955 uppercase tracking-wider font-sans">Recent Operations Audit Log</h4>
                    </div>
                    <div className="space-y-3 max-h-[200px] overflow-y-auto custom-scrollbar pr-1">
                      {statsAiRequests === 0 && statsDbaAudits === 0 && statsSchemas === 0 ? (
                        <div className="flex flex-col items-center justify-center py-8 text-center gap-1.5">
                          <span className="text-sky-600 text-xs font-semibold">No activity yet</span>
                          <span className="text-sky-500 text-[10px]">Your operations will appear here as you use Namines.</span>
                        </div>
                      ) : (
                        <>
                          <div className="flex items-center justify-between text-xs p-3.5 bg-sky-50/50 hover:bg-sky-50 border border-sky-200/50 rounded-xl transition-all duration-150">
                            <span className="font-bold text-sky-800">Local WASM compiler schema generation</span>
                            <span className="text-[9px] bg-emerald-50 text-emerald-700 px-2.5 py-0.5 rounded border border-emerald-200 font-black">SUCCESS</span>
                          </div>
                          <div className="flex items-center justify-between text-xs p-3.5 bg-sky-50/50 hover:bg-sky-50 border border-sky-200/50 rounded-xl transition-all duration-150">
                            <span className="font-bold text-sky-800">{Math.round((used / dailyLimit) * 100)}% daily credits checked</span>
                            <span className="text-[9px] bg-sky-100 text-sky-700 px-2.5 py-0.5 rounded border border-sky-200 font-black">INFO</span>
                          </div>
                        </>
                      )}
                    </div>
                  </div>
                </div>
              </div>
            )}
            {/* 5. Pricing Tab */}
            {activeTab === 'pricing' && (
              <div className="grid grid-cols-1 md:grid-cols-2 gap-5 pt-1 text-sky-900">
                {/* Free Plan Card */}
                <div className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-2xl p-5 flex flex-col justify-between hover:border-sky-350 transition-all shadow-[0_4px_16px_rgba(14,165,233,0.03)]">
                  <div className="space-y-4">
                    <div className="space-y-1">
                      <span className="text-[10px] uppercase font-bold text-sky-600 tracking-wider">Plan</span>
                      <h4 className="text-sm font-extrabold text-sky-955">Free Member</h4>
                      <p className="text-[10px] text-sky-600/90 leading-normal font-semibold">Ideal for individual developers and builders.</p>
                    </div>
                    <div className="text-2xl font-black text-sky-950">$0 <span className="text-xs font-normal text-sky-500">/ forever</span></div>
                    <div className="h-px bg-sky-100" />
                    <ul className="space-y-2.5 text-[11px] text-sky-850 font-medium">
                      <li className="flex items-center gap-2">
                        <Check className="w-3.5 h-3.5 text-emerald-600 shrink-0" />
                        <span>100% daily cloud credits bar</span>
                      </li>
                      <li className="flex items-center gap-2">
                        <Check className="w-3.5 h-3.5 text-emerald-600 shrink-0" />
                        <span>Access to Medium (Llama 8B) models</span>
                      </li>
                      <li className="flex items-center gap-2">
                        <Check className="w-3.5 h-3.5 text-emerald-600 shrink-0" />
                        <span>Local SQLite (WASM) compiler</span>
                      </li>
                      <li className="flex items-center gap-2">
                        <Check className="w-3.5 h-3.5 text-emerald-600 shrink-0" />
                        <span>Basic DBA linting & diagnostics</span>
                      </li>
                    </ul>
                  </div>
                </div>

                {/* Pro Plan Card ($5 / month - Emoji-Free) */}
                <div className="bg-sky-50/70 border border-sky-300/80 rounded-2xl p-5 flex flex-col justify-between hover:border-sky-400/80 transition-all relative overflow-hidden shadow-md">
                  <div className="absolute top-0 right-0 bg-sky-600 text-white text-[8px] font-black uppercase tracking-wider px-3 py-1 rounded-bl-xl border-l border-b border-sky-500/20">
                    Recommended
                  </div>
                  <div className="space-y-4">
                    <div className="space-y-1">
                      <span className="text-[10px] uppercase font-bold text-sky-600 tracking-wider">Plan</span>
                      <h4 className="text-sm font-extrabold text-sky-955">Pro Member</h4>
                      <p className="text-[10px] text-sky-600/90 leading-normal font-semibold">For engineering teams and professionals.</p>
                    </div>
                    <div className="text-2xl font-black text-sky-655">$5 <span className="text-xs font-normal text-sky-600">/ month</span></div>
                    <div className="h-px bg-sky-200/60" />
                    <ul className="space-y-2.5 text-[11px] text-sky-850 font-semibold">
                      <li className="flex items-center gap-2">
                        <Sparkles className="w-3.5 h-3.5 text-sky-600 shrink-0" />
                        <span className="font-extrabold text-sky-955">Unlimited AI requests</span>
                      </li>
                      <li className="flex items-center gap-2">
                        <Sparkles className="w-3.5 h-3.5 text-sky-600 shrink-0" />
                        <span>High (Llama 70B) & Ultra (BYOK) model tiers</span>
                      </li>
                      <li className="flex items-center gap-2">
                        <Sparkles className="w-3.5 h-3.5 text-sky-600 shrink-0" />
                        <span>SignalR multiplayer team collaboration</span>
                      </li>
                      <li className="flex items-center gap-2">
                        <Sparkles className="w-3.5 h-3.5 text-sky-600 shrink-0" />
                        <span>Full cloud database backups & history sync</span>
                      </li>
                      <li className="flex items-center gap-2">
                        <Sparkles className="w-3.5 h-3.5 text-sky-600 shrink-0" />
                        <span>Priority support & Slack channel access</span>
                      </li>
                    </ul>
                  </div>
                  {/* Pro Plan CTA Buttons */}
                  <div className="mt-5 flex flex-col gap-2">
                    {subscriptionStatus === 'active' ? (
                      <>
                        <div className="flex items-center gap-2 text-[11px] text-emerald-700 font-bold bg-emerald-50 border border-emerald-200 rounded-xl px-4 py-2.5">
                          <Check className="w-4 h-4" />
                          You are on the Pro plan
                        </div>
                        <button
                          type="button"
                          onClick={handleManageSubscription}
                          disabled={isUpgrading}
                          className="w-full flex items-center justify-center gap-2 border border-sky-300 text-sky-700 text-[11px] font-bold rounded-xl px-4 py-2.5 hover:bg-sky-100/50 transition-all disabled:opacity-50 cursor-pointer"
                        >
                          <CreditCard className="w-3.5 h-3.5" />
                          {isUpgrading ? 'Opening portal...' : 'Manage Subscription'}
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        onClick={handleUpgrade}
                        disabled={isUpgrading}
                        className="w-full flex items-center justify-center gap-2 bg-sky-600 hover:bg-sky-500 text-white text-[11px] font-bold rounded-xl px-4 py-3 transition-all shadow-[0_2px_10px_rgba(14,165,233,0.15)] disabled:opacity-50 disabled:cursor-wait cursor-pointer border border-sky-455/20"
                      >
                        {isUpgrading ? 'Redirecting to Stripe...' : 'Upgrade to Pro — $5/mo'}
                      </button>
                    )}
                    <p className="text-center text-[10px] text-sky-500/80 font-medium">Secured by Stripe · Cancel anytime · PCI-DSS compliant</p>
                  </div>
                </div>
              </div>
            )}

            {/* 6. Help & FAQ Tab */}
            {activeTab === 'help' && (
              <div className="space-y-3 pt-1 text-sky-900">
                {faqs.map((faq, idx) => (
                  <div key={idx} className="bg-[#e8f0fa]/90 border border-sky-200/80 rounded-xl overflow-hidden transition-all shadow-[0_4px_12px_rgba(14,165,233,0.02)]">
                    <button
                      type="button"
                      onClick={() => setOpenFaq(openFaq === idx ? null : idx)}
                      className="w-full flex justify-between items-center px-4 py-3.5 text-left text-xs font-bold text-sky-950 hover:text-sky-900 hover:bg-sky-50/50 transition-colors cursor-pointer select-none"
                    >
                      <span>{faq.q}</span>
                      <span className="text-sky-600 font-extrabold text-sm leading-none">{openFaq === idx ? '−' : '+'}</span>
                    </button>
                    {openFaq === idx && (
                      <div className="px-4 pb-4 text-[11px] text-sky-850 leading-relaxed border-t border-sky-100 pt-2.5 animate-in fade-in duration-200 bg-sky-50/25">
                        {faq.a}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}

          </div>

          {/* Footer */}
          <div className="px-8 py-3.5 bg-[#e8f0fa]/80 border-t border-sky-200/80 flex justify-center items-center text-[10px] text-sky-500/60 font-mono tracking-widest shrink-0">
            <span>Darvell Labs</span>
          </div>

        </div>
      </div>
    </div>
  );
}
