import React, { useState, useEffect, useRef } from 'react';
import { X, Key, Save, Shield, User, CreditCard, HelpCircle, LogOut, Check, Lock, BarChart3, ChevronDown, SlidersHorizontal, ArrowLeft, Database } from 'lucide-react';
import { useAIPolicyStore, AIPolicy, AiAdvancedSettings } from '../../../store/useAIPolicyStore';
import { useToastStore } from '../../../store/useToastStore';
import { useAuthStore } from '../../../store/useAuthStore';
import { useQuotaStore } from '../../../store/useQuotaStore';
import api, { authService, BillingInterval, PlanPricing } from '../../../services/api';
import { useFocusTrap } from '../../../hooks/useFocusTrap';
import MfaQrCode from './MfaQrCode';

type PriceView = { amount: number; total: number; available: boolean } | null;

/**
 * Fiyat etiketi.
 *
 * <b>Liste yüklenmediyse tutar yerine "—" gösteriliyor, varsayılan bir sayı
 * değil.</b> Yanlış bir fiyat göstermek, hiç göstermemekten kötü: kullanıcı
 * gördüğü tutarı ödeyeceğini varsayar ve farkı ancak kart ekstresinde görür.
 */
function PlanPriceTag({ price, interval }: { price: PriceView; interval: BillingInterval }) {
  if (!price) {
    return <div className="text-2xl font-bold text-content-subtle">—</div>;
  }

  const amount = Number.isInteger(price.amount) ? price.amount : price.amount.toFixed(2);

  return (
    <div>
      <div className="text-2xl font-bold text-content-primary">
        ${amount} <span className="text-xs font-normal text-content-subtle">/ month</span>
      </div>
      {interval === 'yearly' && (
        <p className="text-[10px] text-content-subtle font-medium mt-0.5">
          ${price.total} billed once a year
        </p>
      )}
      {!price.available && (
        <p className="text-[10px] text-warning-text font-medium mt-0.5">
          {interval === 'yearly'
            ? 'Yearly billing is not set up yet.'
            : 'Checkout is not set up yet.'}
        </p>
      )}
    </div>
  );
}

function upgradeLabel(plan: string, price: PriceView, interval: BillingInterval) {
  if (!price) return `Upgrade to ${plan}`;
  // Satın alınamama sebebi DÖNEME bağlı: aylık kimliği kurulu ama yıllığı
  // kurulmamış olabilir. Her iki durumda da "yearly coming soon" yazmak,
  // aylık sekmedeyken yıllıktan bahseden bir düğme demekti.
  if (!price.available) {
    return interval === 'yearly'
      ? `${plan} — yearly billing coming soon`
      : `${plan} — checkout not available yet`;
  }
  const amount = Number.isInteger(price.amount) ? price.amount : price.amount.toFixed(2);
  return interval === 'yearly'
    ? `Upgrade to ${plan} — $${price.total}/yr`
    : `Upgrade to ${plan} — $${amount}/mo`;
}

interface AIPreferencesModalProps {
  isOpen: boolean;
  onClose: () => void;
}

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
        className="w-full flex items-center justify-between bg-surface-600 border border-content-primary/15 hover:border-content-primary/25 text-content-secondary text-xs font-semibold py-2 px-3.5 rounded-[var(--radius-control)] cursor-pointer transition-all select-none focus:border-focus-ring"
      >
        <span className="truncate">{selectedOption ? selectedOption.label : value}</span>
        <ChevronDown className={`w-3.5 h-3.5 text-content-subtle transition-transform duration-200 shrink-0 ml-1.5 ${isOpen ? 'rotate-180' : ''}`} />
      </button>

      {isOpen && (
        <div
          className={`absolute left-0 w-full min-w-[200px] max-h-[240px] overflow-y-auto rounded-[var(--radius-control)] border border-content-primary/15 bg-surface-600 p-1.5 shadow-2xl z-[999] flex flex-col gap-0.5 select-none ${
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
                className={`flex items-center justify-between w-full px-3 py-2 rounded-[var(--radius-control)] text-xs font-semibold cursor-pointer transition-all text-left select-none ${
                  isSelected
                    ? 'bg-white/[0.1] text-content-primary'
                    : 'text-content-secondary hover:bg-white/[0.05] hover:text-content-primary'
                }`}
              >
                <span className="truncate">{opt.label}</span>
                {isSelected && (
                  <Check className="w-3.5 h-3.5 text-content-primary shrink-0 ml-1.5" />
                )}
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

// Üç Namines AI modeli (new-phase/36 §3). Sekiz sağlayıcı modeli yerine üç
// kendi adımız duruyor.
//
// Bunun sebebi kozmetik değil: yapılandırmadaki bir Groq modeli bir gün
// kaldırıldı ve o modeli seçmiş kullanıcıların şema üretimi tamamen durdu.
// Ad bizim olunca üstteki değişiklik sunucuda tek satırda kapanıyor.
//
// Sayılar eski AIMode değerleri: sunucu bunları üç modele indirgiyor
// (Low→flash, Medium→standard, Ultra→pro). Kayıtlı eski tercihler
// atılmıyor, karşılığına çevriliyor.
const policyOptions = [
  { value: 1, label: 'NAI v1 Flash · fast, light' },
  { value: 2, label: 'NAI v1 · balanced' },
  { value: 4, label: 'NAI v1 Pro · deepest reasoning' }
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

// RESTRICT varsayılan, CASCADE değil: bu kod tabanının kuralı, varsayılanın
// asla veri kaybına doğru düşmemesi (bkz. ReferentialActionSql).
const fkActionOptions = [
  { value: 'restrict', label: 'ON DELETE RESTRICT (Default, safest)' },
  { value: 'set_null', label: 'ON DELETE SET NULL' },
  { value: 'cascade', label: 'ON DELETE CASCADE (deletes children)' }
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
  const { user, logout, isAuthenticated } = useAuthStore();
  const { dailyLimit, used, remaining, resetAt, fetchQuota } = useQuotaStore();
  const showToast = useToastStore(state => state.showToast);
  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(isOpen, modalRef);

  const [activeTab, setActiveTab] = useState<'profile' | 'account' | 'ai' | 'pricing' | 'help' | 'analytics'>('profile');
  const [localPolicy, setLocalPolicy] = useState<AIPolicy>({ ...policy });
  const [openFaq, setOpenFaq] = useState<number | null>(null);

  // MFA (TOTP). `mfaSetup` yalnizca kurulum sirasinda dolu; icindeki
  // `sharedKey` KULLANICININ SIRRI ve hicbir ucuncu tarafa gonderilmiyor
  // (or. bir QR uretme servisine) -- bu yuzden QR goruntusu YOK, elle
  // girilebilir anahtar ve otpauth:// baglantisi var.
  const [mfaEnabled, setMfaEnabled] = useState<boolean | null>(null);
  const [mfaRecoveryLeft, setMfaRecoveryLeft] = useState(0);
  const [mfaSetup, setMfaSetup] = useState<{ sharedKey: string; otpauthUri: string } | null>(null);
  const [mfaCode, setMfaCode] = useState('');
  const [mfaBusy, setMfaBusy] = useState(false);
  // Kurtarma kodlari SUNUCUDAN BIR KEZ geliyor; kullanici kaydedene kadar
  // ekranda tutuluyor ve baska hicbir yere yazilmiyor.
  const [mfaRecoveryCodes, setMfaRecoveryCodes] = useState<string[] | null>(null);

  /**
   * MFA durumunu ceker.
   *
   * Kullandigi `useEffect`'ten ONCE tanimli olmasi bilincli: sonrasinda
   * tanimlandiginda lint "bildiriminden once erisiliyor" diyordu. Calisma
   * zamaninda sorun cikmiyordu (efekt render'dan sonra kosar) ama siralama
   * degistigi gun cikacakti. Ihtiyac duydugu setState'lerin hepsi yukarida.
   */
  const refreshMfaStatus = async () => {
    try {
      const status = await authService.mfa.status();
      setMfaEnabled(status.enabled);
      setMfaRecoveryLeft(status.recoveryCodesLeft);
    } catch {
      // Giris yapilmamis ya da uc erisilemez: durumu bilinmiyor birak.
      setMfaEnabled(null);
    }
  };

  // Modal her acildiginda acik SSS maddesi kapaniyor.
  //
  // Bu, efekt icinde `setOpenFaq(null)` olarak yaziliydi; efekt icindeki
  // SENKRON setState fazladan bir render turu tetikliyor (React bunu
  // "cascading render" diye isaretliyor). React'in bu durum icin onerdigi
  // desen, degisimi RENDER sirasinda yakalamak: acilis anini bir onceki
  // degerle karsilastirip yalnizca gecis aninda sifirla.
  const [wasOpen, setWasOpen] = useState(isOpen);
  if (isOpen !== wasOpen) {
    setWasOpen(isOpen);
    if (isOpen) setOpenFaq(null);
  }
  const [showAdvancedScreen, setShowAdvancedScreen] = useState(false);
  const [isUpgrading, setIsUpgrading] = useState(false);
  const [subscriptionStatus, setSubscriptionStatus] = useState<string | null>(null);
  const [planTier, setPlanTier] = useState<string | null>(null);
  // Fiyatlar sunucudan; liste alınamazsa kartlar fiyatsız gösterilmiyor —
  // tutarı bilmeden "Upgrade" düğmesine bastırmak, kullanıcıyı ne ödeyeceğini
  // bilmediği bir ödeme akışına sokmak olurdu.
  const [plans, setPlans] = useState<PlanPricing[] | null>(null);
  const [interval, setInterval] = useState<BillingInterval>('monthly');

  // Developer Profile Identity States
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


  /**
   * Sunucudan gelen gelişmiş tercihleri forma yansıtır.
   *
   * Kullandığı `useEffect`'ten ÖNCE tanımlı olması bilinçli: sonrasında
   * tanımlandığında lint "bildiriminden önce erişiliyor" diyordu. Çalışma
   * zamanında sorun çıkmıyordu (efekt render'dan sonra koşar) ama sıralama
   * değiştiği gün çıkacaktı. İhtiyaç duyduğu setState'lerin hepsi yukarıda.
   */
  function applyAdvanced(a?: AiAdvancedSettings) {
    if (!a) return;
    setSeedDomain(a.seedDomain);
    setDocLevel(a.docLevel);
    setScaffoldVersion(a.scaffoldVersion);
    setDbaSeverity(a.dbaSeverity);
    setTemperature(a.temperature);
    setPromptStyle(a.promptStyle);
    setNamingConvention(a.namingConvention);
    setFkAction(a.fkAction);
    setMaxTokens(a.maxTokens);
    setAutoIndex(a.autoIndex);
    setSqlPrettyPrint(a.sqlPrettyPrint);
  }


  useEffect(() => {
    if (isOpen) {
      fetchPolicy().then(() => {
        const fresh = useAIPolicyStore.getState().policy;
        setLocalPolicy({ ...fresh });
        // Sunucudaki gelişmiş tercihler forma basılıyor. Bu satır olmadan
        // kullanıcı ayarı kaydediyor, modalı kapatıp açıyor ve eski değeri
        // görüyordu — kaydın işe yaramadığı izlenimi veriyordu.
        applyAdvanced(fresh.advanced);
      });
      if (isAuthenticated) {
        fetchQuota();
        // MFA durumu: arayuz "Enable" mi "Disable" mi gosterecegini bilmeli.
        refreshMfaStatus();
      }

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

      // Gelişmiş ayarlar artık SUNUCUDAN geliyor. Eskiden yalnızca localStorage'a
      // yazılıyor ve hiçbir yerde okunmuyorlardı — on bir ayarın tamamı süstü.
      // localStorage yalnızca ilk açılışta bir kez göç için okunuyor; sunucudan
      // yanıt gelince applyAdvanced() üzerine yazıyor.
      setSeedDomain(localStorage.getItem('namines-ai-seed-domain') || 'general');
      setDocLevel(localStorage.getItem('namines-ai-doc-level') || 'standard');
      setScaffoldVersion(localStorage.getItem('namines-ai-scaffold-version') || '.net8');
      setDbaSeverity(localStorage.getItem('namines-ai-dba-severity') || 'warning');
      setTemperature(localStorage.getItem('namines-ai-temperature') || '0.2');
      setPromptStyle(localStorage.getItem('namines-ai-prompt-style') || 'clean');
      setNamingConvention(localStorage.getItem('namines-ai-naming-convention') || 'snake_case');
      setFkAction(localStorage.getItem('namines-ai-fk-action') || 'restrict');
      setMaxTokens(localStorage.getItem('namines-ai-max-tokens') || '4096');
      setSqlPrettyPrint(localStorage.getItem('namines-ai-sql-pretty') || 'true');
      setAutoIndex(localStorage.getItem('namines-ai-auto-index') || 'true');

      // Kaldirilan sahte jeton ozelliginin biraktigi veri temizleniyor: jetonlar
      // hicbir zaman gecerli degildi, birakilmasi kullaniciyi yaniltmaya devam ederdi.
      try { localStorage.removeItem('namines-api-tokens'); } catch { /* private mode */ }

      setStatsSchemas(Number(localStorage.getItem('namines-stats-schemas') || '0'));
      setStatsAiRequests(Number(localStorage.getItem('namines-stats-ai-requests') || '0'));
      setStatsDbaAudits(Number(localStorage.getItem('namines-stats-dba-audits') || '0'));
      setStatsMockRecords(Number(localStorage.getItem('namines-stats-mock-records') || '0'));


      // Fiyat listesi giriş gerektirmiyor — çıkış yapmış biri de ne ödeyeceğini
      // görebilmeli.
      authService.getPlans()
        .then(setPlans)
        .catch(() => setPlans(null));

      if (isAuthenticated) {
        authService.getSubscriptionStatus()
          .then(data => { if (data) setSubscriptionStatus(data.status); })
          .catch(() => {});
        // Pro/Team ayrımı subscription/status'te yok (yalnızca active/inactive
        // diyor); hangi kart "You are on this plan" göstersin, quota/status'teki
        // Plan alanından geliyor.
        api.get('/quota/status')
          .then(res => setPlanTier(res.data?.plan ?? null))
          .catch(() => {});
      }
    }
  }, [isOpen, isAuthenticated, fetchPolicy, fetchQuota]);

  const handleUpgrade = async (plan: 'pro' | 'team' = 'pro') => {
    setIsUpgrading(true);
    try {
      if (!isAuthenticated) { showToast('Please log in to upgrade.', 'warning'); return; }
      const data = await authService.createCheckoutSession(plan, interval);
      if (data.redirect === 'portal') {
        await handleManageSubscription();
      } else if (data.url) {
        // `location.href = ...` yerine `assign(...)`: lint kuralı href'e
        // atamayı "değiştirilemez değere yazma" olarak işaretliyor ve haklı —
        // assign() aynı işi yapan açık API.
        window.location.assign(data.url);
      }
    } catch {
      showToast('Checkout could not be initiated. Please try again.', 'error');
    } finally {
      setIsUpgrading(false);
    }
  };

  /** Seçili döneme ait fiyat kaydı; liste yüklenmediyse null. */
  const priceOf = (plan: 'pro' | 'team') =>
    plans?.find(p => p.plan === plan)?.prices.find(pr => pr.interval === interval) ?? null;

  /**
   * Kart başlığındaki tutar. Yıllıkta AYA DÜŞEN tutar gösteriliyor, altında
   * yıllık toplam: aylık $15 ile yıllık $150'yi yan yana koymak, karşılaştırmayı
   * kullanıcıya zihinden böldürmek olurdu.
   */
  const priceLabel = (plan: 'pro' | 'team') => {
    const price = priceOf(plan);
    if (!price) return null;
    const amount = interval === 'yearly' ? price.monthlyEquivalentUsd : price.amountUsd;
    return { amount, total: price.amountUsd, available: price.available };
  };

  const discountOf = (plan: 'pro' | 'team') =>
    plans?.find(p => p.plan === plan)?.yearlyDiscountPercent ?? null;

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
    localStorage.setItem('namines-full-name', fullName);
    localStorage.setItem('namines-company', companyName);
    localStorage.setItem('namines-github', githubUrl);
    localStorage.setItem('namines-linkedin', linkedinUrl);
    localStorage.setItem('namines-website', websiteUrl);

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



  /** Formdaki gelişmiş tercihleri sunucuya gidecek şekle çevirir. */
  const collectAdvanced = (): AiAdvancedSettings => ({
    seedDomain, docLevel, scaffoldVersion, dbaSeverity, temperature,
    promptStyle, namingConvention, fkAction, maxTokens, autoIndex, sqlPrettyPrint,
  });


  const handleMfaSetup = async () => {
    setMfaBusy(true);
    try {
      setMfaSetup(await authService.mfa.setup());
      setMfaCode('');
    } catch {
      showToast('Two-factor setup could not be started.', 'error');
    } finally {
      setMfaBusy(false);
    }
  };

  const handleMfaEnable = async (e: React.FormEvent) => {
    e.preventDefault();
    setMfaBusy(true);
    try {
      const result = await authService.mfa.enable(mfaCode);
      // Kodlar EKRANDA gosteriliyor; kullanici kapatana kadar duruyor.
      setMfaRecoveryCodes(result.recoveryCodes ?? []);
      setMfaSetup(null);
      setMfaCode('');
      await refreshMfaStatus();
      showToast('Two-factor authentication is on.', 'success');
    } catch {
      // Sunucu kodu neden reddettigini soylemiyor (dogru): sayac tabanli
      // ipucu vermek saldirgana yardim ederdi. Kullaniciya en olasi sebep
      // gosteriliyor.
      showToast('That code is not valid. Check your device clock and try again.', 'error');
    } finally {
      setMfaBusy(false);
    }
  };

  const handleMfaDisable = async (e: React.FormEvent) => {
    e.preventDefault();
    setMfaBusy(true);
    try {
      await authService.mfa.disable(mfaCode);
      setMfaCode('');
      await refreshMfaStatus();
      showToast('Two-factor authentication is off.', 'info');
    } catch {
      showToast('That code is not valid.', 'error');
    } finally {
      setMfaBusy(false);
    }
  };

  const handleSavePolicy = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      // Gelişmiş tercihler de aynı istekte gidiyor: ayrı bir uç olsaydı biri
      // başarılı biri başarısız olabilir ve kullanıcı yarısı kaydedilmiş bir
      // yapılandırmayla kalırdı.
      await updatePolicy({ ...localPolicy, advanced: collectAdvanced() });
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
    } catch {
      showToast('Failed to save AI Policies.', 'error');
    }
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
      q: 'How do daily quotas and credit deductions work?',
      a: (
        <div className="space-y-4 text-content-secondary">
          <p className="text-[10px] text-content-muted">
            Free members receive a daily <strong className="text-content-primary">100% cloud credit bar</strong>. Daily usage is calculated based on the base cost of the AI feature and the selected AI Model&apos;s routing multiplier. If credits are exhausted, features automatically fall back to the local engine to ensure uninterrupted usage.
          </p>

          <div className="space-y-2">
            <h5 className="text-[10px] font-bold text-content-secondary uppercase tracking-wider">Base Feature Cost</h5>
            <div className="border border-content-primary/15 rounded-[var(--radius-control)] overflow-hidden bg-surface-600">
              <table className="w-full text-left border-collapse text-[10px]">
                <thead>
                  <tr className="bg-white/[0.04] border-b border-content-primary/15 text-content-muted font-bold uppercase">
                    <th className="py-1.5 px-3">Feature</th>
                    <th className="py-1.5 px-3 text-right">Base Cost</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-content-primary/8 text-content-secondary">
                  <tr><td className="py-1.5 px-3 font-medium">Schema Generation (Prompt)</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">5%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">Smart Seeding</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">5%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">Schema Revision</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">8%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">DBA Linter & Diagnostics</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">8%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">Backend Scaffolding</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">10%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">Schema Migration</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">10%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">Documentation & Reports</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">10%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">Vision / Reverse Engineer</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">15%</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">Voice Input / Transcription</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">5%</td></tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="space-y-2">
            <h5 className="text-[10px] font-bold text-content-secondary uppercase tracking-wider">AI Model Multiplier</h5>
            <div className="border border-content-primary/15 rounded-[var(--radius-control)] overflow-hidden bg-surface-600">
              <table className="w-full text-left border-collapse text-[10px]">
                <thead>
                  <tr className="bg-white/[0.04] border-b border-content-primary/15 text-content-muted font-bold uppercase">
                    <th className="py-1.5 px-3">Routing Engine</th>
                    <th className="py-1.5 px-3 text-right">Multiplier</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-content-primary/8 text-content-secondary">
                  <tr><td className="py-1.5 px-3 font-medium">NAI v1 Flash</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">0.5x</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">NAI v1</td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">1x</td></tr>
                  <tr><td className="py-1.5 px-3 font-medium">NAI v1 Pro <span className="opacity-60">(paid plans)</span></td><td className="py-1.5 px-3 text-right font-mono text-accent-text font-bold">2x</td></tr>
                </tbody>
              </table>
            </div>
          </div>

          <div className="bg-surface-600 rounded-[var(--radius-control)] p-2.5 text-micro leading-relaxed text-content-muted">
            <strong>Calculation Example:</strong> Running a <span className="text-content-primary font-bold">DBA Audit (8% base)</span> with the <span className="text-content-primary font-bold">Medium engine (2x)</span> will deduct <span className="text-accent-text font-bold">16%</span> from your daily quota. Running it with the Default engine costs <span className="text-accent-text font-bold">0%</span>.
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

  const navItems: { id: typeof activeTab; label: string; icon: typeof User }[] = [
    { id: 'profile', label: 'Profile Settings', icon: User },
    { id: 'account', label: 'Account & Tokens', icon: Lock },
    { id: 'ai', label: 'AI Configurations', icon: SlidersHorizontal },
    { id: 'analytics', label: 'System Analytics', icon: BarChart3 },
    { id: 'pricing', label: 'Pricing', icon: CreditCard },
    { id: 'help', label: 'Help & FAQ', icon: HelpCircle },
  ];

  const inputClass = "w-full px-3.5 py-2 bg-surface-600 border border-content-primary/15 rounded-[var(--radius-control)] text-xs text-content-primary placeholder-content-subtle focus:outline-none focus:border-focus-ring transition-all";
  const cardClass = "bg-surface-700 border border-content-primary/15 rounded-[var(--radius-card)]";
  const primaryBtnClass = "bg-content-primary hover:bg-content-primary-hover text-surface-900 font-semibold";

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
              {navItems.map(item => {
                const Icon = item.icon;
                const isActive = activeTab === item.id;
                return (
                  <button
                    key={item.id}
                    type="button"
                    onClick={() => {
                      setActiveTab(item.id);
                      if (item.id === 'ai') setShowAdvancedScreen(false);
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
              onClick={() => {
                logout();
                showToast('Logged out successfully.', 'info');
                onClose();
              }}
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
                {activeTab === 'profile' && 'User Profile Settings'}
                {activeTab === 'account' && 'Account Credentials & Tokens'}
                {activeTab === 'ai' && 'AI Services Routing'}
                {activeTab === 'analytics' && 'System Usage & Analytics'}
                {activeTab === 'pricing' && 'Membership Plans'}
                {activeTab === 'help' && 'Help & FAQ'}
              </h4>
              <p className="text-[11px] text-content-subtle mt-0.5">
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
              className="p-1.5 rounded-[var(--radius-control)] text-content-subtle hover:text-content-secondary hover:bg-white/[0.06] transition-all cursor-pointer"
            >
              <X className="w-4 h-4" />
            </button>
          </div>

          {/* Tab Content Panel */}
          <div className="flex-1 p-6 overflow-y-auto space-y-6 bg-surface-900">

            {/* 1. Profile Tab */}
            {activeTab === 'profile' && (
              <div className="space-y-6">
                {!isAuthenticated ? (
                  <div className="flex flex-col items-center justify-center text-center py-12 space-y-4">
                    <div className="p-4 bg-white/[0.06] rounded-full">
                      <User className="w-10 h-10 text-content-subtle" />
                    </div>
                    <div className="space-y-1">
                      <h3 className="text-sm font-bold text-content-primary">Not Logged In</h3>
                      <p className="text-xs text-content-muted max-w-xs">Please log in to manage your developer profile information.</p>
                    </div>
                  </div>
                ) : (
                  <form onSubmit={handleSaveDeveloperSettings} className={`${cardClass} p-5 space-y-5`}>
                    <div className="flex items-center gap-2 border-b border-content-primary/10 pb-3">
                      <User className="w-4 h-4 text-content-muted" />
                      <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">Developer Profile Information</h4>
                    </div>

                    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                      <div className="space-y-1.5">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Full Name</label>
                        <input aria-label="Full Name" type="text" value={fullName} onChange={(e) => setFullName(e.target.value)} placeholder="John Doe" className={inputClass} />
                      </div>
                      <div className="space-y-1.5">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Company / Organization</label>
                        <input aria-label="Company / Organization" type="text" value={companyName} onChange={(e) => setCompanyName(e.target.value)} placeholder="Acme Corp" className={inputClass} />
                      </div>
                      <div className="space-y-1.5">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Location</label>
                        <input aria-label="Location" type="text" value={location} onChange={(e) => setLocation(e.target.value)} placeholder="Istanbul, Turkey" className={inputClass} />
                      </div>
                      <div className="space-y-1.5">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">GitHub Profile URL</label>
                        <input aria-label="GitHub Profile URL" type="text" value={githubUrl} onChange={(e) => setGithubUrl(e.target.value)} placeholder="https://github.com/username" className={inputClass} />
                      </div>
                      <div className="space-y-1.5">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">LinkedIn Profile URL</label>
                        <input aria-label="LinkedIn Profile URL" type="text" value={linkedinUrl} onChange={(e) => setLinkedinUrl(e.target.value)} placeholder="https://linkedin.com/in/username" className={inputClass} />
                      </div>
                      <div className="space-y-1.5">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Twitter / X Profile URL</label>
                        <input aria-label="Twitter / X Profile URL" type="text" value={twitterUrl} onChange={(e) => setTwitterUrl(e.target.value)} placeholder="https://x.com/username" className={inputClass} />
                      </div>
                      <div className="space-y-1.5 md:col-span-2">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Portfolio Website</label>
                        <input aria-label="Portfolio Website" type="text" value={websiteUrl} onChange={(e) => setWebsiteUrl(e.target.value)} placeholder="https://myportfolio.com" className={inputClass} />
                      </div>
                      <div className="space-y-1.5 md:col-span-2">
                        <label className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Short Bio</label>
                        <textarea aria-label="Short Bio" value={bio} onChange={(e) => setBio(e.target.value)} placeholder="Full-stack developer passionate about databases and scalable architecture..." rows={3} className={`${inputClass} resize-none`} />
                      </div>
                    </div>

                    <div className="flex items-center justify-between pt-2">
                      <p className="text-[10px] text-content-subtle font-medium">
                        {isAuthenticated ? 'Saved to cloud + local cache.' : 'Log in to enable cloud sync.'}
                      </p>
                      <button
                        type="submit"
                        disabled={isSavingProfile}
                        className={`flex items-center gap-1.5 px-4 py-2 disabled:opacity-60 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer active:scale-95 ${primaryBtnClass}`}
                      >
                        <Save className="w-3.5 h-3.5" />
                        <span>{isSavingProfile ? 'Saving...' : 'Save Profile'}</span>
                      </button>
                    </div>
                  </form>
                )}
              </div>
            )}

            {/* 2. Account & Tokens Tab */}
            {activeTab === 'account' && (
              <div className="space-y-6">
                {!isAuthenticated ? (
                  <div className="flex flex-col items-center justify-center text-center py-12 space-y-4">
                    <div className="p-4 bg-white/[0.06] rounded-full">
                      <Lock className="w-10 h-10 text-content-subtle" />
                    </div>
                    <div className="space-y-1">
                      <h3 className="text-sm font-bold text-content-primary">Not Logged In</h3>
                      <p className="text-xs text-content-muted max-w-xs">Please log in to manage two-factor authentication and account access.</p>
                    </div>
                  </div>
                ) : (
                  <div className="space-y-6">
                    {(() => {
                      const remainingPercent = Math.round((remaining / dailyLimit) * 100);
                      // Gerçek plan quota/status'ten geliyor (Free/Pro/Team/Dev).
                      // user.type yalnızca "corporate mi bireysel mi" diyen eski
                      // ikili alan — Dev hesabı hep "individual" kalıyor, o yüzden
                      // rozet her zaman "Free" gösteriyordu.
                      const isFreeUser = planTier !== 'Pro' && planTier !== 'Team' && planTier !== 'Dev';
                      const planLabel = planTier === 'Dev' ? 'Dev — Unlimited'
                        : planTier === 'Team' ? 'Team Member'
                        : planTier === 'Pro' ? 'Pro Member'
                        : 'Free Member';

                      return (
                        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                          <div className={`${isFreeUser ? 'md:col-span-1' : 'md:col-span-3'} ${cardClass} p-4 flex flex-col justify-between`}>
                            <div className="space-y-1">
                              <span className="text-[10px] font-semibold text-content-subtle uppercase tracking-wider">Account Level</span>
                              <p className="text-sm font-bold text-content-primary truncate">{user?.username}</p>
                            </div>
                            <div className="mt-4">
                              <span className="inline-block px-2.5 py-1 rounded-full text-micro font-bold uppercase tracking-wider bg-white/[0.08] text-content-secondary">
                                {planLabel}
                              </span>
                            </div>
                          </div>

                          {isFreeUser && (
                            <div className={`md:col-span-2 ${cardClass} p-4 space-y-3`}>
                              <div className="flex justify-between items-center text-[10px] font-semibold text-content-muted uppercase tracking-wider">
                                <span>Daily Cloud Credits</span>
                                <div className="flex items-center gap-2">
                                  {resetAt && (
                                    <span className="text-micro text-content-subtle font-medium normal-case tracking-normal">
                                      (resets at {new Date(resetAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })} {new Date(resetAt).toLocaleDateString([], { month: 'short', day: 'numeric' })})
                                    </span>
                                  )}
                                  <span className="text-content-primary font-bold">{remainingPercent}% left</span>
                                </div>
                              </div>
                              <div className="w-full h-2 bg-surface-600 rounded-full overflow-hidden">
                                <div className="h-full bg-content-secondary transition-all duration-500" style={{ width: `${remainingPercent}%` }} />
                              </div>
                              <p className="text-[10px] text-content-subtle leading-normal">
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
                    <div className={`${cardClass} p-5 space-y-3`}>
                      <div className="flex items-center gap-2 border-b border-content-primary/10 pb-3">
                        <Lock className="w-4 h-4 text-content-muted" />
                        <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">License Authorization Levels</h4>
                      </div>

                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-3">
                        <div className="flex items-center justify-between p-3 bg-surface-600 rounded-[var(--radius-control)]">
                          <span className="text-[10px] font-semibold text-content-secondary uppercase tracking-wider">SignalR Realtime Multiplayer</span>
                          <span className={`text-micro font-bold px-2 py-0.5 rounded-[var(--radius-control)] ${user?.type === 'corporate' ? 'bg-success-text/10 text-success-text' : 'bg-white/[0.04] text-content-subtle'}`}>
                            {user?.type === 'corporate' ? 'ENABLED' : 'DISABLED'}
                          </span>
                        </div>
                        <div className="flex items-center justify-between p-3 bg-surface-600 rounded-[var(--radius-control)]">
                          <span className="text-[10px] font-semibold text-content-secondary uppercase tracking-wider">Automated DBA Linter</span>
                          <span className={`text-micro font-bold px-2 py-0.5 rounded-[var(--radius-control)] ${user?.type === 'corporate' ? 'bg-success-text/10 text-success-text' : 'bg-white/[0.08] text-content-secondary'}`}>
                            {user?.type === 'corporate' ? 'UNLIMITED' : '100% DAILY'}
                          </span>
                        </div>
                        <div className="flex items-center justify-between p-3 bg-surface-600 rounded-[var(--radius-control)]">
                          <span className="text-[10px] font-semibold text-content-secondary uppercase tracking-wider">Cloud Workspace Sync</span>
                          <span className="text-micro font-bold px-2 py-0.5 rounded-[var(--radius-control)] bg-success-text/10 text-success-text">ACTIVE</span>
                        </div>
                      </div>
                    </div>

                    {/* Cok faktorlu dogrulama.
                        QR kodu TAMAMEN TARAYICIDA uretiliyor (MfaQrCode) --
                        sir hicbir ucuncu tarafa gitmiyor. Hazir chart API'leri
                        otpauth baglantisini URL'de disariya gonderdigi icin
                        kullanilamaz: MFA kurarken sirri bir yabanciya vermek
                        kurulumun amacini yok eder.
                        Elle girilebilir anahtar KALDIRILMADI: QR uretimi
                        basarisiz olursa ya da kamera yoksa tek yol o. */}
                    <div className={`${cardClass} p-5 space-y-4`}>
                      <div className="flex items-center gap-2 border-b border-content-primary/10 pb-3">
                        <Shield className="w-4 h-4 text-content-muted" />
                        <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">
                          Two-Factor Authentication
                        </h4>
                        {mfaEnabled === true && (
                          <span className="ml-auto text-[10px] font-semibold text-accent-text">ON</span>
                        )}
                      </div>

                      {mfaEnabled === null ? (
                        <p className="text-xs text-content-subtle italic">Checking status…</p>
                      ) : mfaRecoveryCodes ? (
                        <div className="space-y-3">
                          <p className="text-xs text-content-primary font-semibold">
                            Save these recovery codes now — they are shown only once.
                          </p>
                          <p className="text-[10px] text-content-subtle leading-normal">
                            Each code works once. They are the only way back into your account if you lose
                            your authenticator device.
                          </p>
                          <div className="grid grid-cols-2 gap-1.5 bg-surface-800 p-3 rounded-[var(--radius-control)] font-mono text-xs text-content-secondary select-all">
                            {mfaRecoveryCodes.map(code => <span key={code}>{code}</span>)}
                          </div>
                          <button
                            type="button"
                            onClick={() => setMfaRecoveryCodes(null)}
                            className={`px-4 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer ${primaryBtnClass}`}
                          >
                            I have saved them
                          </button>
                        </div>
                      ) : mfaEnabled ? (
                        <form onSubmit={handleMfaDisable} className="space-y-3">
                          <p className="text-xs text-content-secondary leading-relaxed">
                            Two-factor authentication is protecting this account.
                            {mfaRecoveryLeft > 0 && ` ${mfaRecoveryLeft} recovery code${mfaRecoveryLeft === 1 ? '' : 's'} left.`}
                          </p>
                          <p className="text-[10px] text-content-subtle leading-normal">
                            Turning it off requires a current code — so a stolen session cannot remove your
                            protection.
                          </p>
                          <div className="flex gap-2">
                            <input
                              type="text"
                              inputMode="numeric"
                              autoComplete="one-time-code"
                              value={mfaCode}
                              onChange={(e) => setMfaCode(e.target.value)}
                              placeholder="6-digit code"
                              aria-label="Authentication code to turn off two-factor"
                              className={`flex-1 ${inputClass}`}
                            />
                            <button
                              type="submit"
                              disabled={mfaBusy || !mfaCode.trim()}
                              className="px-4 py-2 text-xs rounded-[var(--radius-control)] bg-danger-text/10 text-danger-text hover:bg-danger-text/20 transition-colors cursor-pointer disabled:opacity-50"
                            >
                              {mfaBusy ? 'Working…' : 'Turn off'}
                            </button>
                          </div>
                        </form>
                      ) : mfaSetup ? (
                        <form onSubmit={handleMfaEnable} className="space-y-3">
                          <p className="text-[10px] font-semibold text-content-secondary uppercase tracking-wider">
                            Step 1 — add this key to your authenticator app
                          </p>
                          <MfaQrCode otpauthUri={mfaSetup.otpauthUri} />
                          <p className="text-[10px] text-content-subtle leading-normal">
                            Scan the code, or type the key below by hand.
                          </p>
                          <div className="font-mono text-xs bg-surface-800 p-2.5 rounded-[var(--radius-control)] select-all text-content-secondary break-all">
                            {mfaSetup.sharedKey}
                          </div>
                          <a
                            href={mfaSetup.otpauthUri}
                            className="inline-block text-[10px] text-accent-text hover:underline"
                          >
                            Or open it in your authenticator app
                          </a>
                          <p className="text-[10px] font-semibold text-content-secondary uppercase tracking-wider pt-1">
                            Step 2 — enter the code it shows
                          </p>
                          <div className="flex gap-2">
                            <input
                              type="text"
                              inputMode="numeric"
                              autoComplete="one-time-code"
                              value={mfaCode}
                              onChange={(e) => setMfaCode(e.target.value)}
                              placeholder="6-digit code"
                              aria-label="Authentication code to turn on two-factor"
                              className={`flex-1 ${inputClass}`}
                            />
                            <button
                              type="submit"
                              disabled={mfaBusy || !mfaCode.trim()}
                              className={`px-4 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer disabled:opacity-50 ${primaryBtnClass}`}
                            >
                              {mfaBusy ? 'Checking…' : 'Verify'}
                            </button>
                          </div>
                          <button
                            type="button"
                            onClick={() => { setMfaSetup(null); setMfaCode(''); }}
                            className="text-[10px] text-content-subtle hover:text-content-secondary cursor-pointer"
                          >
                            Cancel
                          </button>
                        </form>
                      ) : (
                        <div className="space-y-3">
                          <p className="text-xs text-content-secondary leading-relaxed">
                            Add a second step to sign-in using an authenticator app. A leaked password alone
                            will no longer be enough to reach your databases.
                          </p>
                          <button
                            type="button"
                            onClick={handleMfaSetup}
                            disabled={mfaBusy}
                            className={`px-4 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer disabled:opacity-50 ${primaryBtnClass}`}
                          >
                            {mfaBusy ? 'Starting…' : 'Turn on'}
                          </button>
                        </div>
                      )}
                    </div>

                    {/* API anahtarlari BILEREK burada YONETILMIYOR.
                        Buradaki eski ekran jetonu tarayicida Math.random() ile
                        uretip yalnizca localStorage'a yaziyordu: sunucuya hic
                        gitmiyor, hicbir yerde dogrulanmiyor ve "revoke" hicbir
                        seyi iptal etmiyordu -- tutulmayan bir guvenlik sozu.
                        Gercek anahtar sistemi Gateway'de (sunucuda uretilen,
                        hash'i saklanan, tablo bazli izinli, denetim kaydi
                        tutulan anahtarlar). Kullanici oraya yonlendiriliyor. */}
                    <div className={`${cardClass} p-5 space-y-3`}>
                      <div className="flex items-center gap-2 border-b border-content-primary/10 pb-3">
                        <Key className="w-4 h-4 text-content-muted" />
                        <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">API Keys</h4>
                      </div>
                      <p className="text-xs text-content-secondary leading-relaxed">
                        API keys are managed per project, not per account &mdash; each key carries its own
                        table-level read/write permissions and its own audit trail.
                      </p>
                      <p className="text-[10px] text-content-subtle leading-normal">
                        Open your project in Namines Desk and go to <span className="font-semibold text-content-secondary">API Keys</span> to
                        create, scope, or revoke a key.
                      </p>
                    </div>

                    <button
                      onClick={() => {
                        logout();
                        showToast('Logged out successfully.', 'info');
                        onClose();
                      }}
                      className="flex md:hidden items-center justify-center gap-2 w-full py-2.5 bg-white/[0.06] hover:bg-danger-text/10 text-content-muted hover:text-danger-text text-xs font-semibold rounded-[var(--radius-control)] transition-all cursor-pointer"
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
              <div className="space-y-6 animate-in fade-in duration-200">
                {!showAdvancedScreen ? (
                  <div className="space-y-6">
                    <form onSubmit={handleSavePolicy} className="space-y-6">
                      <div className={`${cardClass} px-5 py-2`}>
                        <div className="py-4 flex items-center justify-between border-b border-content-primary/10 mb-4 gap-4">
                          <div className="flex items-center gap-2">
                            <span className="w-2 h-2 rounded-full bg-content-muted shrink-0" />
                            <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">AI Services Engine Routing</h4>
                          </div>
                          <button
                            type="button"
                            onClick={() => setShowAdvancedScreen(true)}
                            className="flex items-center gap-1.5 text-[10px] font-semibold text-content-muted hover:text-content-secondary bg-white/[0.05] hover:bg-white/[0.08] px-2.5 py-1.5 rounded-[var(--radius-control)] shrink-0 transition-colors cursor-pointer"
                          >
                            <SlidersHorizontal className="w-3.5 h-3.5" />
                            <span>Advanced Settings</span>
                          </button>
                        </div>

                        {/* Cost Multiplier Legend */}
                        <div className="py-3 px-4 bg-surface-600 rounded-[var(--radius-control)] mb-5 text-[10px] space-y-2">
                          <p className="font-semibold text-content-secondary uppercase tracking-wider">AI Cost Multiplier Legend</p>
                          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-2">
                            {[
                              ['NAI v1 Flash', '0.5x'], ['NAI v1', '1x'], ['NAI v1 Pro', '2x'],
                            ].map(([label, val]) => (
                              <div key={label} className="p-1.5 bg-surface-800 rounded-[var(--radius-control)] text-center">
                                <span className="block font-bold text-content-secondary">{label}</span>
                                <span className="text-content-muted font-medium">{val}</span>
                              </div>
                            ))}
                          </div>
                        </div>

                        <div className="divide-y divide-content-primary/8">
                          {policyFields.map((field) => (
                            <div key={field.key} className="grid grid-cols-12 gap-4 items-center py-4">
                              <div className="col-span-12 md:col-span-8 space-y-0.5">
                                <h5 className="text-xs font-bold text-content-primary">{field.label}</h5>
                                <p className="text-[10px] text-content-subtle leading-normal">
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
                        className={`w-full py-2.5 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer flex items-center justify-center gap-1.5 active:scale-[0.99] ${primaryBtnClass}`}
                      >
                        <span>{isLoading ? 'Saving...' : 'Save AI Routing'}</span>
                      </button>
                    </form>

                  </div>
                ) : (
                  // Advanced settings screen
                  <div className="space-y-6 animate-in fade-in duration-200">
                    <div className="flex items-center justify-between border-b border-content-primary/10 pb-4 gap-4">
                      <button
                        type="button"
                        onClick={() => setShowAdvancedScreen(false)}
                        className="flex items-center gap-2 px-3 py-2 text-xs font-semibold text-content-muted hover:text-content-secondary bg-white/[0.05] hover:bg-white/[0.08] rounded-[var(--radius-control)] transition-all cursor-pointer select-none active:scale-95"
                      >
                        <ArrowLeft className="w-4 h-4" />
                        <span>Back to AI Routing</span>
                      </button>
                      <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">
                        Advanced AI Tuning & Generation Parameters
                      </h4>
                    </div>

                    <div className="flex gap-2.5 p-3.5 bg-white/[0.05] rounded-[var(--radius-control)]">
                      <Shield className="w-4 h-4 shrink-0 text-content-muted mt-0.5" />
                      <div className="text-[10px] leading-normal text-content-secondary font-medium">
                        <span className="font-bold text-content-primary uppercase">Caution:</span> Altering default prompt settings, delete action policies, or temperature variables can result in unexpected compilation failures or syntax differences. Proceed with caution.
                      </div>
                    </div>

                    <form
                      onSubmit={async (e) => {
                        e.preventDefault();
                        // Sunucuya da yazılıyor: bu ayarlar şema üretiminde
                        // gerçekten okunuyor, yalnızca tarayıcıda kalırlarsa
                        // hiçbir etkileri olmaz (eski davranış buydu).
                        try {
                          await updatePolicy({ ...localPolicy, advanced: collectAdvanced() });
                        } catch {
                          showToast('Advanced settings could not be saved to the server.', 'error');
                          return;
                        }
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
                      <div className={`${cardClass} p-5`}>
                        <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
                          {[
                            { label: 'Seeding Mock Domain', desc: 'Vertical semantic context for mock table values.', value: seedDomain, onChange: setSeedDomain, options: seedDomainOptions },
                            { label: 'Documentation Technical Detail', desc: 'Complexity depth of generated READMEs & documentation.', value: docLevel, onChange: setDocLevel, options: docLevelOptions },
                            { label: 'Target Scaffolder Framework', desc: 'C# classes compiler compilation target framework.', value: scaffoldVersion, onChange: setScaffoldVersion, options: scaffoldOptions },
                            { label: 'DBA Diagnostic Severity', desc: 'Severity threshold filter for diagnostics reports.', value: dbaSeverity, onChange: setDbaSeverity, options: dbaSeverityOptions },
                            { label: 'AI Temperature (Creativity)', desc: 'Controls randomness vs structural correctness.', value: temperature, onChange: setTemperature, options: tempOptions },
                            { label: 'Code Presentation Style', desc: 'Inline comments, naming density, and formatting.', value: promptStyle, onChange: setPromptStyle, options: promptStyleOptions },
                            { label: 'SQL Schema Naming Standard', desc: 'Case formats applied to tables and columns.', value: namingConvention, onChange: setNamingConvention, options: namingOptions },
                            { label: 'Foreign Key Action Rule', desc: 'Referential integrity behavior on parent row delete.', value: fkAction, onChange: setFkAction, options: fkActionOptions },
                            { label: 'Context Output Token Limit', desc: 'Limits maximum text tokens in compiled files.', value: maxTokens, onChange: setMaxTokens, options: maxTokensOptions },
                            { label: 'Auto Foreign Key Indexes', desc: 'Automatically suggest index DDL on relationships.', value: autoIndex, onChange: setAutoIndex, options: autoIndexOptions },
                          ].map((f) => (
                            <div key={f.label} className="space-y-1.5">
                              <div className="flex flex-col">
                                <span className="text-[10px] font-bold text-content-primary">{f.label}</span>
                                <span className="text-[10px] text-content-subtle mt-0.5">{f.desc}</span>
                              </div>
                              <CustomSelect value={f.value} onChange={f.onChange} options={f.options} className="w-full" />
                            </div>
                          ))}

                          <div className="space-y-1.5 md:col-span-2">
                            <div className="flex flex-col">
                              <span className="text-[10px] font-bold text-content-primary">SQL Formatting (Pretty Print)</span>
                              <span className="text-[10px] text-content-subtle mt-0.5">Ensures code output block is parsed and formatted.</span>
                            </div>
                            <CustomSelect value={sqlPrettyPrint} onChange={setSqlPrettyPrint} options={sqlPrettyOptions} className="w-full" />
                          </div>
                        </div>
                      </div>

                      <button type="submit" className={`w-full py-2.5 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer flex items-center justify-center gap-1.5 active:scale-[0.99] ${primaryBtnClass}`}>
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
              <div className="space-y-6 animate-in fade-in duration-200">
                <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
                  <div className={`${cardClass} p-4 space-y-1`}>
                    <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">Schemas Compiled</span>
                    <p className="text-2xl font-bold text-content-primary">{statsSchemas}</p>
                    <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">Local WASM Compiler</div>
                  </div>
                  <div className={`${cardClass} p-4 space-y-1`}>
                    <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">AI Credits Used</span>
                    <p className="text-2xl font-bold text-content-primary">{Math.round((used / dailyLimit) * 100)}%</p>
                    <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">{Math.round((remaining / dailyLimit) * 100)}% Remaining Today</div>
                  </div>
                  <div className={`${cardClass} p-4 space-y-1`}>
                    <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">DBA Audits Run</span>
                    <p className="text-2xl font-bold text-content-primary">{statsDbaAudits}</p>
                    <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">Linter Session</div>
                  </div>
                  <div className={`${cardClass} p-4 space-y-1`}>
                    <span className="text-micro font-semibold text-content-subtle uppercase tracking-wider">Mock Rows Seeded</span>
                    <p className="text-2xl font-bold text-content-primary">{statsMockRecords}</p>
                    <div className="text-micro font-semibold text-content-subtle uppercase tracking-wide">Smart Seeding Engine</div>
                  </div>
                </div>

                <div className="space-y-6 flex flex-col w-full">
                  {/* AI Engine Routing Allocation */}
                  <div className={`${cardClass} p-5 space-y-4 w-full`}>
                    <div className="flex justify-between items-center text-[10px] font-semibold text-content-muted uppercase tracking-wider">
                      <span>AI Engine Allocation Share</span>
                      <span className="text-content-subtle">COMPILATION BREAKDOWN</span>
                    </div>
                    <div className="w-full h-5 rounded-[var(--radius-control)] overflow-hidden flex bg-surface-600">
                      <div className="h-full bg-white/[0.15]" style={{ width: '55%' }} title="Local Engine: 55%" />
                      <div className="h-full bg-white/[0.25]" style={{ width: '25%' }} title="NAI v1: 25%" />
                      <div className="h-full bg-white/[0.4]" style={{ width: '15%' }} title="NAI v1 Pro: 15%" />
                      <div className="h-full bg-white/[0.6]" style={{ width: '5%' }} title="Custom: 5%" />
                    </div>
                    <div className="flex flex-wrap gap-x-6 gap-y-2 text-[10px] font-semibold text-content-muted uppercase tracking-wide">
                      <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.15]" /><span>Local (55%)</span></div>
                      <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.25]" /><span>NAI v1 (25%)</span></div>
                      <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.4]" /><span>NAI v1 Pro (15%)</span></div>
                      <div className="flex items-center gap-2"><div className="w-3 h-3 rounded-[var(--radius-control)] bg-white/[0.6]" /><span>Custom (5%)</span></div>
                    </div>
                  </div>

                  {/* Database Dialect Target Distribution */}
                  <div className={`${cardClass} p-5 space-y-4 w-full`}>
                    <span className="text-[10px] font-semibold text-content-muted uppercase tracking-wider block">Target Database Compilations</span>
                    <div className="space-y-3">
                      {[
                        ['PostgreSQL', 45], ['Microsoft SQL Server', 25], ['MySQL / MariaDB', 18], ['SQLite (Embedded)', 12],
                      ].map(([label, pct]) => (
                        <div key={label as string} className="space-y-1.5">
                          <div className="flex justify-between text-xs font-semibold text-content-secondary">
                            <span>{label}</span>
                            <span className="text-content-muted">{pct}%</span>
                          </div>
                          <div className="w-full h-2 bg-surface-600 rounded-full overflow-hidden">
                            <div className="h-full bg-content-secondary rounded-full" style={{ width: `${pct}%` }} />
                          </div>
                        </div>
                      ))}
                    </div>
                  </div>

                  {/* Weekly Performance Metrics */}
                  <div className={`${cardClass} p-5 space-y-4 w-full`}>
                    <span className="text-[10px] font-semibold text-content-muted uppercase tracking-wider block">Weekly Performance Metrics</span>
                    <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                      <div className="p-3.5 bg-surface-600 rounded-[var(--radius-control)] space-y-2">
                        <div className="flex justify-between text-[11px] font-semibold uppercase text-content-secondary">
                          <span>WASM Compiler Success Rate</span>
                          <span className="text-success-text font-bold">100%</span>
                        </div>
                        <div className="w-full h-2 bg-surface-800 rounded-full overflow-hidden">
                          <div className="h-full bg-success-text rounded-full" style={{ width: '100%' }} />
                        </div>
                      </div>
                      <div className="p-3.5 bg-surface-600 rounded-[var(--radius-control)] space-y-2">
                        <div className="flex justify-between text-[11px] font-semibold uppercase text-content-secondary">
                          <span>DBA Integrity Rating</span>
                          <span className="text-content-secondary font-bold">94.8%</span>
                        </div>
                        <div className="w-full h-2 bg-surface-800 rounded-full overflow-hidden">
                          <div className="h-full bg-content-secondary rounded-full" style={{ width: '94.8%' }} />
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Operations History Log */}
                  <div className={`${cardClass} p-5 space-y-4 w-full`}>
                    <div className="border-b border-content-primary/10 pb-3">
                      <h4 className="text-xs font-bold text-content-primary uppercase tracking-wider">Recent Operations Audit Log</h4>
                    </div>
                    <div className="space-y-2.5 max-h-[200px] overflow-y-auto pr-1">
                      {statsAiRequests === 0 && statsDbaAudits === 0 && statsSchemas === 0 ? (
                        <div className="flex flex-col items-center justify-center py-8 text-center gap-1.5">
                          <span className="text-content-muted text-xs font-semibold">No activity yet</span>
                          <span className="text-content-subtle text-[10px]">Your operations will appear here as you use Namines.</span>
                        </div>
                      ) : (
                        <>
                          <div className="flex items-center justify-between text-xs p-3 bg-surface-600 hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-all">
                            <span className="font-semibold text-content-secondary">Local WASM compiler schema generation</span>
                            <span className="text-micro bg-success-text/10 text-success-text px-2.5 py-0.5 rounded-[var(--radius-control)] font-bold">SUCCESS</span>
                          </div>
                          <div className="flex items-center justify-between text-xs p-3 bg-surface-600 hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-all">
                            <span className="font-semibold text-content-secondary">{Math.round((used / dailyLimit) * 100)}% daily credits checked</span>
                            <span className="text-micro bg-white/[0.08] text-content-secondary px-2.5 py-0.5 rounded-[var(--radius-control)] font-bold">INFO</span>
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
              <div className="space-y-4">
              {/* Aylık / Yıllık geçişi. Yıllık, 12 ayı tek işlemde tahsil ediyor:
                  Stripe'ın işlem başına sabit $0,30'u aylıkta her ay tekrar
                  kesiliyordu (bkz. second-phase/16-KOTA-VE-MALIYET.md). */}
              <div className="flex items-center justify-center gap-2">
                <div className="inline-flex rounded-[var(--radius-card)] bg-surface-700 border border-surface-500 p-1">
                  {(['monthly', 'yearly'] as BillingInterval[]).map(opt => (
                    <button
                      key={opt}
                      type="button"
                      onClick={() => setInterval(opt)}
                      className={`px-4 py-1.5 rounded-[var(--radius-control)] text-[11px] font-bold transition-all cursor-pointer ${
                        interval === opt
                          ? 'bg-content-primary text-surface-900'
                          : 'text-content-muted hover:text-content-primary'
                      }`}
                    >
                      {opt === 'monthly' ? 'Monthly' : 'Yearly'}
                    </button>
                  ))}
                </div>
                {/* İndirim oranı da sunucudan hesaplanıyor — fiyatlardan biri
                    değişip etiket elle güncellenmezse, ekranda gerçek olmayan
                    bir indirim durur. */}
                {discountOf('pro') !== null && (
                  <span className="text-[10px] font-bold text-success-text bg-success-text/10 px-2.5 py-1 rounded-[var(--radius-control)]">
                    Save {discountOf('pro')}% yearly
                  </span>
                )}
              </div>
              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                {/* Free Plan Card */}
                <div className={`${cardClass} p-5 flex flex-col justify-between`}>
                  <div className="space-y-4">
                    <div className="space-y-1">
                      <span className="text-[10px] uppercase font-semibold text-content-subtle tracking-wider">Plan</span>
                      <h4 className="text-sm font-bold text-content-primary">Free Member</h4>
                      <p className="text-[10px] text-content-subtle leading-normal font-medium">Ideal for individual developers and builders.</p>
                    </div>
                    <div className="text-2xl font-bold text-content-primary">$0 <span className="text-xs font-normal text-content-subtle">/ forever</span></div>
                    <div className="h-px bg-content-primary/10" />
                    <ul className="space-y-2 text-[11px] text-content-secondary font-medium">
                      {[
                        // Ücretsiz tavan SABİT DEĞİL: paylaşılan havuz doldukça
                        // adil pay düşüyor (bkz. AiQuotaService.CalculateFreeUserCap).
                        // Karta "20K" yazmak, gösterilen kotanın uygulanandan
                        // farklı olması demekti — bu hata bir kez zaten yaşandı.
                        //
                        // Gerçek sayı YALNIZCA kullanıcı zaten Free'deyse yazılıyor:
                        // `dailyLimit` OTURUMU AÇAN kişinin tavanı, planın değil.
                        // Pro bir kullanıcıya Free kartında 200K göstermek, Dev
                        // hesabına da "2147484K" göstermek olurdu.
                        planTier === 'Free' && dailyLimit > 0
                          ? `${(dailyLimit / 1000).toFixed(0)}K AI tokens / day — today's fair share`
                          : 'A daily AI budget shared fairly across free users',
                        'NAI v1 Flash and NAI v1 models', 'All 6 database engines + SQL export', 'DBA linter & schema diagnostics', '1 external database connection', '3 ephemeral test runs / day'].map(f => (
                        <li key={f} className="flex items-center gap-2">
                          <Check className="w-3.5 h-3.5 text-success-text shrink-0" />
                          <span>{f}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                </div>

                {/* Pro Plan Card */}
                <div className="bg-surface-600 border border-content-primary/20 rounded-[var(--radius-card)] p-5 flex flex-col justify-between relative overflow-hidden">
                  <div className="absolute top-0 right-0 bg-white/[0.1] text-content-primary text-micro font-bold uppercase tracking-wider px-3 py-1 rounded-bl-lg">
                    Recommended
                  </div>
                  <div className="space-y-4">
                    <div className="space-y-1">
                      <span className="text-[10px] uppercase font-semibold text-content-subtle tracking-wider">Plan</span>
                      <h4 className="text-sm font-bold text-content-primary">Pro Member</h4>
                      <p className="text-[10px] text-content-subtle leading-normal font-medium">For engineering teams and professionals.</p>
                    </div>
                    <PlanPriceTag price={priceLabel('pro')} interval={interval} />
                    <div className="h-px bg-content-primary/10" />
                    <ul className="space-y-2 text-[11px] text-content-secondary font-medium">
                      {['200K AI tokens / day — 10x Free', 'NAI v1 Pro model unlocked', '2 branch databases + change review', '20 ephemeral test runs / day', '3 external database connections', 'Gateway API: 600 req/min'].map(f => (
                        <li key={f} className="flex items-center gap-2">
                          <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />
                          <span>{f}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                  <div className="mt-5 flex flex-col gap-2">
                    {subscriptionStatus === 'active' && planTier !== 'Team' ? (
                      <>
                        <div className="flex items-center gap-2 text-[11px] text-success-text font-semibold bg-success-text/10 rounded-[var(--radius-control)] px-4 py-2.5">
                          <Check className="w-4 h-4" />
                          You are on the Pro plan
                        </div>
                        <button
                          type="button"
                          onClick={handleManageSubscription}
                          disabled={isUpgrading}
                          className="w-full flex items-center justify-center gap-2 bg-white/[0.06] hover:bg-white/[0.1] text-content-secondary text-[11px] font-semibold rounded-[var(--radius-control)] px-4 py-2.5 transition-all disabled:opacity-50 cursor-pointer"
                        >
                          <CreditCard className="w-3.5 h-3.5" />
                          {isUpgrading ? 'Opening portal...' : 'Manage Subscription'}
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        onClick={() => handleUpgrade('pro')}
                        disabled={isUpgrading || priceLabel('pro')?.available === false}
                        className={`w-full flex items-center justify-center gap-2 text-[11px] rounded-[var(--radius-control)] px-4 py-3 transition-all disabled:opacity-50 disabled:cursor-wait cursor-pointer ${primaryBtnClass}`}
                      >
                        {isUpgrading ? 'Redirecting to Stripe...' : upgradeLabel('Pro', priceLabel('pro'), interval)}
                      </button>
                    )}
                    <p className="text-center text-[10px] text-content-subtle font-medium">Secured by Stripe · Cancel anytime · PCI-DSS compliant</p>
                  </div>
                </div>

                {/* Team Plan Card */}
                <div className={`${cardClass} p-5 flex flex-col justify-between`}>
                  <div className="space-y-4">
                    <div className="space-y-1">
                      <span className="text-[10px] uppercase font-semibold text-content-subtle tracking-wider">Plan</span>
                      <h4 className="text-sm font-bold text-content-primary">Team</h4>
                      <p className="text-[10px] text-content-subtle leading-normal font-medium">For teams that need higher limits and shared branch databases.</p>
                    </div>
                    <PlanPriceTag price={priceLabel('team')} interval={interval} />
                    <div className="h-px bg-content-primary/10" />
                    <ul className="space-y-2 text-[11px] text-content-secondary font-medium">
                      {['Everything in Pro', '3 seats — you + 2 invited members', 'Shared workspace: projects visible to all', 'Team activity feed — see who changed what', '20 branch databases, unlimited test runs', 'Gateway API: 3,000 req/min'].map(f => (
                        <li key={f} className="flex items-center gap-2">
                          <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />
                          <span>{f}</span>
                        </li>
                      ))}
                    </ul>
                  </div>
                  <div className="mt-5 flex flex-col gap-2">
                    {planTier === 'Team' ? (
                      <>
                        <div className="flex items-center gap-2 text-[11px] text-success-text font-semibold bg-success-text/10 rounded-[var(--radius-control)] px-4 py-2.5">
                          <Check className="w-4 h-4" />
                          You are on the Team plan
                        </div>
                        <button
                          type="button"
                          onClick={handleManageSubscription}
                          disabled={isUpgrading}
                          className="w-full flex items-center justify-center gap-2 bg-white/[0.06] hover:bg-white/[0.1] text-content-secondary text-[11px] font-semibold rounded-[var(--radius-control)] px-4 py-2.5 transition-all disabled:opacity-50 cursor-pointer"
                        >
                          <CreditCard className="w-3.5 h-3.5" />
                          {isUpgrading ? 'Opening portal...' : 'Manage Subscription'}
                        </button>
                      </>
                    ) : (
                      <button
                        type="button"
                        onClick={() => handleUpgrade('team')}
                        disabled={isUpgrading || priceLabel('team')?.available === false}
                        className="w-full flex items-center justify-center gap-2 bg-white/[0.06] hover:bg-white/[0.1] text-content-secondary text-[11px] font-semibold rounded-[var(--radius-control)] px-4 py-3 transition-all disabled:opacity-50 disabled:cursor-wait cursor-pointer"
                      >
                        {isUpgrading ? 'Redirecting to Stripe...' : upgradeLabel('Team', priceLabel('team'), interval)}
                      </button>
                    )}
                    <p className="text-center text-[10px] text-content-subtle font-medium">Secured by Stripe · Cancel anytime · PCI-DSS compliant</p>
                  </div>
                </div>
              </div>
              </div>
            )}

            {/* 6. Help & FAQ Tab */}
            {activeTab === 'help' && (
              <div className="space-y-2.5">
                {faqs.map((faq, idx) => (
                  <div key={idx} className={`${cardClass} overflow-hidden`}>
                    <button
                      type="button"
                      onClick={() => setOpenFaq(openFaq === idx ? null : idx)}
                      className="w-full flex justify-between items-center px-4 py-3.5 text-left text-xs font-semibold text-content-primary hover:bg-white/[0.03] transition-colors cursor-pointer select-none"
                    >
                      <span>{faq.q}</span>
                      <span className="text-content-muted font-bold text-sm leading-none">{openFaq === idx ? '−' : '+'}</span>
                    </button>
                    {openFaq === idx && (
                      <div className="px-4 pb-4 text-[11px] text-content-secondary leading-relaxed border-t border-content-primary/10 pt-2.5 animate-in fade-in duration-200">
                        {faq.a}
                      </div>
                    )}
                  </div>
                ))}
              </div>
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
