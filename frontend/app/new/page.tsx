'use client';

import { useState, useRef, useEffect } from 'react';
import type { DatabaseSchema } from '../../types/schema';
import { useRouter } from 'next/navigation';
import { Loader2, X, Link as LinkIcon, ChevronDown, Check, Wand2, Sparkles, Image as ImageIcon } from 'lucide-react';
import { schemaService, sourceService } from '../../services/api';
import { useSchemaStore, type DbType } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useQuotaStore } from '../../store/useQuotaStore';
import { useToastStore } from '../../store/useToastStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import VoiceRecorder from '../../components/landing/VoiceRecorder';
import ClarifyDialog from '../../components/landing/ClarifyDialog';
import ProductionScreen from '../../components/landing/ProductionScreen';
import PlanScreen from '../../components/landing/PlanScreen';
import { streamSchemaGeneration, AgentStepEvent, AgentResultEvent } from '../../lib/sseSchemaStream';
import { ClarifyResponse, NaiModelOption, DEFAULT_NAI_MODELS } from '../../types/nai';
import { SourceDropdown } from '../../components/prompt/SourceDropdown';
import { GithubScanModal } from '../../components/prompt/GithubScanModal';
import { StarterPickerModal } from '../../components/prompt/StarterPickerModal';
import { JsonShapeModal } from '../../components/prompt/JsonShapeModal';
import CodeImportPanel from '../../components/canvas/panels/CodeImportPanel';
import DbConnectionPanel from '../../components/canvas/panels/DbConnectionPanel';
import { type RepositoryScanResult, type SchemaSourceDescriptor, DEFAULT_SCHEMA_SOURCES } from '../../types/source';
import { useT, useLangAttr } from '../../lib/i18n';

export default function NewProjectPage() {
  const t = useT();
  const lang = useLangAttr();
  const [prompt, setPrompt] = useState('');
  const [image, setImage] = useState<File | null>(null);
  const [apiSpecUrl, setApiSpecUrl] = useState('');
  const [showUrlInput, setShowUrlInput] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  
  const modelDropdownRef = useRef<HTMLDivElement>(null);
  const dbDropdownRef = useRef<HTMLDivElement>(null);
  const [modelDropdownOpen, setModelDropdownOpen] = useState(false);
  const [dbDropdownOpen, setDbDropdownOpen] = useState(false);

  // Netleştirme adımı: doluysa sorular gösteriliyor, üretim henüz başlamadı.
  const [clarify, setClarify] = useState<ClarifyResponse | null>(null);
  const [isClarifying, setIsClarifying] = useState(false);
  // Plan modu: netleştirme cevaplarından sonra, üretimden önceki son adım.
  const [planAnswers, setPlanAnswers] = useState<Record<string, string> | null>(null);
  // Üretim ekranı: hattın canlı adımları. bkz. second-phase/04-LOADING-EKRANI.md
  const [productionSteps, setProductionSteps] = useState<AgentStepEvent[]>([]);
  const [showProduction, setShowProduction] = useState(false);

  /**
   * Gelişmiş mod: plan turu + otomatik onarım turları.
   *
   * Varsayılan KAPALI — kapalıyken de şema denetimden geçiyor, yalnızca model
   * bulguları düzeltmek için ek kota harcamıyor (bkz. GenerateRequest.Advanced).
   */
  const [advanced, setAdvanced] = useState(false);

  /** Hat bittiğinde dönen özet — kalan bulgular kullanıcıya gösterilecek. */
  const [agentSummary, setAgentSummary] = useState<AgentResultEvent['agent'] | null>(null);
  const [models, setModels] = useState<NaiModelOption[]>(DEFAULT_NAI_MODELS);
  const [sources, setSources] = useState<SchemaSourceDescriptor[]>(DEFAULT_SCHEMA_SOURCES);
  const [githubModalOpen, setGithubModalOpen] = useState(false);
  // "+ Add source" menüsündeki her kaynağın kendi ekranı. Hepsi prompt
  // YAZMADAN şema üretebiliyor — ürünün tek girişi bir cümle yazmak olmamalı.
  const [starterModalOpen, setStarterModalOpen] = useState(false);
  const [codeModalOpen, setCodeModalOpen] = useState(false);
  const [dbModalOpen, setDbModalOpen] = useState(false);
  const [jsonModalOpen, setJsonModalOpen] = useState(false);

  const router = useRouter();
  // V2: dbType artık global store'dan geliyor
  const { 
    setIsGenerating, 
    loadFromSchema, 
    setProjectName,
    resetProject,
    isGenerating, 
    naiModel,
    dbType, 
    setDbType,
    setNaiModel 
  } = useSchemaStore();
  const isAuthenticated = useAuthStore(state => state.isAuthenticated);
  const authUser = useAuthStore(state => state.user);
  const quotaPlan = useQuotaStore(state => state.plan);
  const isUnlimitedAccount = Boolean(authUser?.isDev) || authUser?.type === 'Admin' || quotaPlan === 'Dev';
  const showToast = useToastStore(state => state.showToast);

  // Yeni proje ekranına (/new) girildiğinde önceki açık proje oturumunu (örn. ADANA)
  // sıfırla ki yeni üretilen şema önceki projenin üstüne yazılmasın.
  useEffect(() => {
    useProjectHistoryStore.getState().setActiveProjectId(null);
    resetProject();
  }, [resetProject]);

  // Handle Stripe redirect callbacks (?upgrade=success / ?upgrade=canceled)
  useEffect(() => {
    if (typeof window === 'undefined') return;
    const params = new URLSearchParams(window.location.search);
    const upgradeParam = params.get('upgrade');
    if (upgradeParam === 'success') {
      showToast('Subscription activated! You are now a Pro Member.', 'success');
      // Clean URL
      window.history.replaceState({}, '', '/');
    } else if (upgradeParam === 'canceled') {
      showToast('Upgrade canceled. You can try again anytime.', 'info');
      window.history.replaceState({}, '', '/');
    }
  }, [showToast]);

  // Click outside dropdowns listener
  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (modelDropdownRef.current && !modelDropdownRef.current.contains(event.target as Node)) {
        setModelDropdownOpen(false);
      }
      if (dbDropdownRef.current && !dbDropdownRef.current.contains(event.target as Node)) {
        setDbDropdownOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => {
      document.removeEventListener('mousedown', handleClickOutside);
    };
  }, []);

  // Model listesi SUNUCUDAN güncellenir: sunucudan gelirse dinamik kota/plan
  // kısıtları uygulanır; sunucuya ulaşılamazsa DEFAULT_NAI_MODELS korunur.
  // Dev / Admin hesaplarında tüm modeller sınırsız olarak açık tutulur.
  useEffect(() => {
    let cancelled = false;
    const normalizeModels = (list: NaiModelOption[]) =>
      isUnlimitedAccount
        ? list.map(m => ({ ...m, available: true, unavailableReason: null }))
        : list;

    if (isUnlimitedAccount) {
      setModels(prev => normalizeModels(prev));
    }

    schemaService.naiModels()
      .then(list => {
        if (!cancelled && list?.length > 0) {
          setModels(normalizeModels(list));
        }
      })
      .catch(() => { /* varsayılan katalog korunur */ });
    return () => { cancelled = true; };
  }, [isAuthenticated, authUser?.email, isUnlimitedAccount]);

  // Kaynak listesi de SUNUCUDAN güncellenir: sunucuya ulaşılamazsa
  // DEFAULT_SCHEMA_SOURCES korunur, menü ve şerit asla kaybolmaz.
  useEffect(() => {
    let cancelled = false;
    sourceService.catalog()
      .then(list => { if (!cancelled && list?.length > 0) setSources(list); })
      .catch(() => { /* varsayılan katalog korunur */ });
    return () => { cancelled = true; };
  }, []);

  const selectedModel = models.find(m => m.id === naiModel) ?? models.find(m => m.isDefault) ?? models[0];

  /**
   * Menüden seçilen kaynağı ekranına yönlendirir.
   *
   * <b>Hepsi prompt YAZMADAN şema üretiyor.</b> Ürünün tek girişi bir cümle
   * yazmak olmamalı: hazır şema, kod dosyası, canlı veritabanı, depo, API
   * tanımı ve resim — altısı da doğrudan canvas'a çıkıyor.
   */
  const handleSourceSelect = (id: string) => {
    if (id === 'github') { setGithubModalOpen(true); return; }
    if (id === 'starter') { setStarterModalOpen(true); return; }
    if (id === 'code') { setCodeModalOpen(true); return; }
    if (id === 'dbconnect') { setDbModalOpen(true); return; }
    if (id === 'openapi') { setShowUrlInput(true); return; }
    if (id === 'image') { fileInputRef.current?.click(); return; }
    if (id === 'jsonshape') { setJsonModalOpen(true); return; }

    // Buraya düşmek, katalogda ekranı olmayan bir kaynak var demek. Sessizce
    // hiçbir şey yapmamak menüyü bozuk gösterirdi.
    showToast('That source has no screen yet.', 'info');
  };

  /** Şema geldi: yeni proje olarak işaretleyip canvas'a geç. */
  const goToCanvasWithCurrentSchema = () => {
    useProjectHistoryStore.getState().setActiveProjectId(null);
    setGithubModalOpen(false);
    setStarterModalOpen(false);
    setCodeModalOpen(false);
    setDbModalOpen(false);
    setJsonModalOpen(false);
    router.push('/canvas');
  };


  useEffect(() => {
    const container = document.getElementById('stars-container');
    if (!container) return;
    
    function createStar() {
      const star = document.createElement('div');
      star.classList.add('shooting-star');
      const isTopEdge = Math.random() > 0.4; // 60% chance to spawn at the top
      
      if (isTopEdge) {
        // Spawn randomly across the entire screen width
        star.style.top = '-100px';
        star.style.left = `${Math.random() * window.innerWidth}px`;
      } else {
        // Spawn along the upper right edge
        star.style.top = `${Math.random() * (window.innerHeight * 0.5)}px`;
        star.style.left = `${window.innerWidth + 50}px`;
      }
      const duration = 2 + Math.random() * 3;
      star.style.animationDuration = `${duration}s`;
      container?.appendChild(star);
      setTimeout(() => {
        if (container?.contains(star)) {
          star.remove();
        }
      }, duration * 1000);
    }

    const intervalId = setInterval(createStar, 400);
    for(let i=0; i<12; i++) {
      setTimeout(createStar, i * 300);
    }

    // Static Twinkling Stars
    for (let i = 0; i < 80; i++) {
      const staticStar = document.createElement('div');
      staticStar.classList.add('static-star');
      staticStar.style.top = `${Math.random() * 100}vh`;
      staticStar.style.left = `${Math.random() * 100}vw`;
      
      const size = Math.random() * 2 + 0.5;
      staticStar.style.width = `${size}px`;
      staticStar.style.height = `${size}px`;
      
      if (Math.random() > 0.5) {
        staticStar.style.animation = `twinkle ${Math.random() * 3 + 2}s infinite alternate`;
        staticStar.style.animationDelay = `${Math.random() * 5}s`;
      } else {
        staticStar.style.opacity = `${Math.random() * 0.4 + 0.1}`;
      }
      
      container?.appendChild(staticStar);
    }

    return () => {
      clearInterval(intervalId);
      if (container) container.innerHTML = '';
    };
  }, []);

  const handleImageUpload = (e: React.ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      setImage(e.target.files[0]);
    }
  };

  /**
   * Üretim ARTIK doğrudan başlamıyor: önce bedava netleştirme adımı geliyor.
   *
   * Tek bir cümleden şema üretmek, modelin boşlukları kendi başına doldurması
   * demekti — kullanıcı sonucu ancak ekranda yanlış tabloları görünce fark
   * ediyordu. Sorular sunucuda anahtar kelimeden çıkıyor, bu istek AI
   * kullanmıyor ve kotayı hiç etkilemiyor.
   */
  const handleGenerate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!prompt.trim()) return;

    setIsClarifying(true);
    try {
      setClarify(await schemaService.clarify(prompt));
    } catch (error) {
      // Netleştirme bir iyileştirme, zorunluluk değil: bu adım düşerse
      // kullanıcının asıl isteği (şema üretmek) düşmemeli.
      console.error('Clarify step failed, generating directly:', error);
      await runGeneration({});
    } finally {
      setIsClarifying(false);
    }
  };

  const runGeneration = async (answers: Record<string, string>) => {
    setIsGenerating(true);
    setClarify(null);
    setPlanAnswers(null);
    setProductionSteps([]);
    setAgentSummary(null);
    setShowProduction(true);

    const formData = schemaService.buildGenerateFormData(
      prompt, dbType, naiModel, image, apiSpecUrl, answers, advanced,
    );

    await streamSchemaGeneration(formData, {
      onStep: (step) => setProductionSteps(prev => [...prev, step]),

      onResult: (result) => {
        useProjectHistoryStore.getState().setActiveProjectId(null);
        loadFromSchema(result.schema as DatabaseSchema);
        // second-phase/09-SEMA-ALTERNATIFLERI.md — canvas'taki "Alternatif üret"
        // bu prompt+cevapları tekrar kullanacak, o yüzden burada saklanıyor.
        useSchemaStore.getState().recordGenerationSource(prompt, answers);
        // Kalan bulgular kullanıcıya gösteriliyor (bkz. ProductionScreen).
        // Bunu atlamak, sunucunun "bulguları gizleme" kararını sessizce geri
        // almak olurdu: kullanıcı bozuk şemayı veritabanı reddedince öğrenirdi.
        setAgentSummary(result.agent);
        setIsGenerating(false);
        // Üretim ekranı "Devam et" ile kapanana kadar açık kalır — kullanıcı
        // ne olduğunu okuyabilsin diye canvas'a hemen atlanmıyor. Kapanınca
        // yönlendiriliyor (bkz. ProductionScreen onClose).
      },

      onError: (error) => {
        console.error('Failed to generate schema:', error);
        setShowProduction(false);
        setIsGenerating(false);

        if (error.httpStatus === 401) {
          // Guest: AI şema üretimi giriş gerektiriyor → net mesaj + login modalı.
          showToast('Please log in to generate a schema.', 'warning');
          useAuthModalStore.getState().open();
        } else if (error.httpStatus === 429 || error.retryAfterSeconds) {
          // Bu bir arıza değil, bir sınır — "bir hata oluştu" demek yanıltıcı olurdu.
          showToast(
            error.retryAfterSeconds
              ? `AI is busy right now. Try again in ${error.retryAfterSeconds} seconds.`
              : 'Your daily AI budget is used up. It resets tomorrow.',
            'warning'
          );
        } else if (error.code === 'AI_NOT_CONFIGURED' || error.httpStatus === 503) {
          // Kurulum eksikliği, geçici arıza değil — "tekrar deneyin" demek yanlış
          // yönlendirme olurdu, kullanıcı aynı duvara tekrar çarpardı.
          showToast(
            error.message || 'AI is not configured on this server yet.',
            'warning',
          );
        } else {
          showToast('An error occurred while generating the schema. Please try again.', 'error');
        }
      },
    });
  };

  const closeProduction = () => {
    setShowProduction(false);
    if (!isGenerating) router.push('/canvas');
  };

  // `justify-center` YOK, yerine içerikte `my-auto` (aşağıdaki <main>).
  // Flex'te ortalanan bir içerik kabından uzun olduğunda taşma İKİ yandan
  // oluyor ve üst kenar erişilemez hâle geliyor — kaynak şeridi eklendikten
  // sonra hero başlığı tam olarak böyle kırpıldı. Otomatik kenar boşluğu yer
  // varken ortalıyor, yer yokken kırpmadan akıtıyor.
  return (
    <div lang={lang} className="relative font-sans text-content-primary flex-1 flex flex-col items-center overflow-x-hidden min-h-[calc(100vh-56px)] py-8 pb-20">
      {/* Background Effects */}
      <div aria-hidden="true" className="ocean-wave pointer-events-none z-0">
        <div className="wave wave1"></div>
        <div className="wave wave2"></div>
        <div className="wave wave3"></div>
      </div>
      <div aria-hidden="true" id="stars-container" className="fixed inset-0 pointer-events-none z-0 overflow-hidden"></div>

      {/* Main Content Container */}
      <main className="relative z-10 my-auto w-full max-w-[var(--w-app)] px-4 sm:px-6 lg:px-8 flex flex-col items-center">
        {/* Hero Section — minimalist, ikon kutusu kaldırıldı (bkz. FRONTEND.md) */}
        <div className="text-center mb-6 sm:mb-10">
          <h1 className="font-mono text-2xl sm:text-5xl lg:text-6xl font-semibold tracking-tight mb-2.5 sm:mb-3 text-content-primary">
            {t('newProjectPage.heroTitle')}
          </h1>
          <p className="text-content-secondary text-xs sm:text-base max-w-[48ch] mx-auto leading-relaxed">
            {t('newProjectPage.heroSubtitle')}
          </p>
        </div>

        {/* Form Card — Shopify stili yuvarlağımsı ve genişletilmiş prompt alanı */}
        <div
          className={`w-full max-w-4xl bg-surface-900/95 border border-line-strong rounded-[var(--radius-modal)] p-4 sm:p-8 relative overflow-visible shadow-2xl backdrop-blur-xl group transition-all duration-200 ${advanced ? 'prompt-panel-advanced' : ''}`}
        >
          <form onSubmit={handleGenerate} className="relative">
            {/* Textarea Section — daha geniş ve ferah Shopify tasarım */}
            <div className="mb-4 relative">
              <textarea
                id="prompt-input"
                aria-label="Describe the database schema you want to generate"
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                placeholder={t('newProjectPage.placeholder')}
                className="w-full h-36 sm:h-52 p-3.5 sm:p-6 pb-14 sm:pb-16 rounded-[var(--radius-card)] bg-surface-800/90 border border-line-strong resize-none text-content-primary placeholder:text-content-muted text-sm sm:text-base leading-relaxed focus:outline-none focus:border-content-primary/30 focus:ring-1 focus:ring-content-primary/20 transition-all"
                disabled={isGenerating}
              ></textarea>

              <div className="absolute bottom-3.5 right-3.5 flex items-center gap-1.5">
                <input
                  type="file"
                  ref={fileInputRef}
                  onChange={handleImageUpload}
                  accept=".png,.jpg,.jpeg"
                  className="hidden"
                />
                <button
                  type="button"
                  disabled={isGenerating}
                  onClick={() => fileInputRef.current?.click()}
                  className="p-2 rounded-full text-content-muted hover:text-content-primary hover:bg-surface-700/60 transition-colors disabled:opacity-40 disabled:cursor-not-allowed cursor-pointer"
                  title="Upload diagram image"
                  aria-label="Upload diagram image"
                >
                  <ImageIcon className="w-4 h-4" />
                </button>
                <VoiceRecorder
                  disabled={isGenerating}
                  onTranscription={(text) => setPrompt(prev => prev ? `${prev} ${text}` : text)}
                />
              </div>
            </div>

            {/* Extended Inputs Area */}
            {(showUrlInput || image) && (
              <div className="flex flex-col gap-3 p-3.5 bg-surface-800/60 rounded-[var(--radius-card)] border border-line-strong mb-4">
                {showUrlInput && (
                  <div className="flex flex-col gap-1.5">
                    <div className="flex items-center gap-2">
                      <LinkIcon className="w-3.5 h-3.5 text-content-secondary shrink-0" />
                      <input
                        aria-label={t('newProjectPage.apiSpecUrl')}
                        type="url"
                        value={apiSpecUrl}
                        onChange={(e) => setApiSpecUrl(e.target.value)}
                        placeholder="https://api.example.com/openapi.json or /graphql"
                        className="flex-1 bg-transparent text-sm text-content-primary placeholder:text-content-muted focus:outline-none"
                        disabled={isGenerating}
                      />
                      {apiSpecUrl && (
                        <button type="button" onClick={() => setApiSpecUrl('')} className="text-content-muted hover:text-content-primary">
                          <X className="w-4 h-4" />
                        </button>
                      )}
                    </div>
                    <p className="text-[10px] text-content-muted pl-5">
                      {t('newProjectPage.apiSpecHint')}
                    </p>
                  </div>
                )}

                {image && (
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-[var(--radius-control)] overflow-hidden border border-line-strong shrink-0">
                      <img src={URL.createObjectURL(image)} alt="Preview" className="w-full h-full object-cover" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm text-content-primary truncate">{image.name}</p>
                      <p className="text-xs text-content-muted">{(image.size / 1024).toFixed(1)} KB</p>
                    </div>
                    <button type="button" onClick={() => setImage(null)} className="p-1 text-content-muted hover:text-danger-text transition-colors">
                      <X className="w-5 h-5" />
                    </button>
                  </div>
                )}
              </div>
            )}

            {/* Options & Submit Section — Shopify Toolset stili dengeli alt bar */}
            <div className="flex flex-wrap items-center justify-between gap-2.5 sm:gap-3 mt-3 w-full">
              <div className="grid grid-cols-2 sm:flex sm:flex-wrap items-center gap-2 w-full sm:w-auto sm:flex-1 min-w-0">
                {/* Şablon & Kaynak seçici (Start without writing) */}
                <SourceDropdown
                  sources={sources}
                  onSelect={handleSourceSelect}
                  disabled={isGenerating}
                />

                {/* Namines AI model seçici */}
                {models.length > 0 && (
                  <div className="relative min-w-0 sm:flex-1 sm:min-w-[130px]" ref={modelDropdownRef}>
                    <button
                      id="new-model-dropdown-btn"
                      type="button"
                      disabled={isGenerating}
                      onClick={() => setModelDropdownOpen(!modelDropdownOpen)}
                      className="flex items-center justify-between rounded-full border border-line-strong bg-surface-800/80 hover:border-line-subtle hover:bg-surface-700/80 px-3.5 sm:px-4 py-2 text-xs font-mono font-medium text-content-primary cursor-pointer w-full text-left select-none disabled:opacity-50 disabled:cursor-not-allowed h-[42px] transition-colors shadow-sm"
                    >
                      <span className="truncate">
                        {selectedModel?.displayName || 'Namines AI'}
                      </span>
                      <ChevronDown className={`w-3.5 h-3.5 text-content-muted transition-transform duration-200 shrink-0 ml-1.5 ${modelDropdownOpen ? 'rotate-180' : ''}`} />
                    </button>

                    {modelDropdownOpen && (
                      <div className="absolute right-0 sm:left-0 sm:right-auto bottom-full mb-2 w-[260px] sm:w-[280px] max-w-[calc(100vw-2rem)] rounded-[var(--radius-card)] border border-line-strong bg-surface-900/98 backdrop-blur-2xl p-1.5 shadow-2xl z-50 flex flex-col gap-1 select-none animate-dropdown-in">
                        {models.map(m => {
                          const isSelected = m.id === naiModel;
                          return (
                            <button
                              key={m.id}
                              type="button"
                              disabled={!m.available}
                              onClick={() => {
                                setNaiModel(m.id);
                                setModelDropdownOpen(false);
                              }}
                              className={`flex items-start justify-between gap-2 px-3 py-2 rounded-[var(--radius-control)] cursor-pointer transition-colors text-left ${
                                !m.available
                                  ? 'opacity-40 cursor-not-allowed text-content-muted'
                                  : isSelected
                                  ? 'bg-surface-700/90 text-content-primary font-semibold'
                                  : 'text-content-secondary hover:bg-surface-800 hover:text-content-primary'
                              }`}
                            >
                              <span className="min-w-0">
                                <span className="block text-xs font-medium">{m.displayName}</span>
                                <span className="block text-[10px] text-content-muted leading-snug">{m.description}</span>
                                <span className="block text-[10px] text-content-muted mt-0.5">
                                  {m.available ? t('newProjectPage.budgetMultiplier', { count: m.costMultiplier }) : t('newProjectPage.paidPlansOnly')}
                                </span>
                              </span>
                              {isSelected && m.available && (
                                <Check className="w-3.5 h-3.5 text-content-primary shrink-0 mt-0.5" />
                              )}
                            </button>
                          );
                        })}
                      </div>
                    )}
                  </div>
                )}

                {/* Gelişmiş mod — Luminous Shopify Buton */}
                <button
                  id="new-advanced-toggle-btn"
                  type="button"
                  disabled={isGenerating}
                  onClick={() => setAdvanced(!advanced)}
                  aria-pressed={advanced}
                  title={advanced ? t('newProjectPage.advancedActiveHint') : t('newProjectPage.advancedPassiveHint')}
                  className={`flex items-center justify-center sm:justify-start gap-1.5 rounded-full px-3.5 sm:px-4 h-[42px] text-xs font-mono font-medium transition-all shrink-0 w-full sm:w-auto disabled:opacity-50 disabled:cursor-not-allowed cursor-pointer ${
                    advanced
                      ? 'advanced-toggle-active'
                      : 'border border-line-strong bg-surface-800/90 text-content-primary hover:border-line-subtle hover:bg-surface-700/90 shadow-sm'
                  }`}
                >
                  <Sparkles className={`w-3.5 h-3.5 shrink-0 ${advanced ? 'text-accent-on' : 'text-content-secondary'}`} />
                  <span className="truncate">{t('newProjectPage.advanced')}</span>
                </button>

                {/* Database Select */}
                <div className="relative min-w-0 sm:flex-1 sm:min-w-[130px]" ref={dbDropdownRef}>
                  <button
                    id="new-db-dropdown-btn"
                    type="button"
                    disabled={isGenerating}
                    onClick={() => setDbDropdownOpen(!dbDropdownOpen)}
                    className="flex items-center justify-between rounded-full border border-line-strong bg-surface-800/80 hover:border-line-subtle hover:bg-surface-700/80 px-3.5 sm:px-4 py-2 text-xs font-mono font-medium text-content-primary cursor-pointer w-full text-left select-none disabled:opacity-50 disabled:cursor-not-allowed h-[42px] transition-colors shadow-sm"
                  >
                    <span className="truncate">
                      {dbType === 'MSSQL' ? 'SQL Server' :
                       dbType === 'PostgreSQL' ? 'PostgreSQL' :
                       dbType === 'MySQL' ? 'MySQL' :
                       dbType === 'SQLite' ? 'SQLite' :
                       dbType === 'Oracle' ? 'Oracle' :
                       dbType === 'MariaDB' ? 'MariaDB' : dbType}
                    </span>
                    <ChevronDown className={`w-3.5 h-3.5 text-content-muted transition-transform duration-200 shrink-0 ml-1.5 ${dbDropdownOpen ? 'rotate-180' : ''}`} />
                  </button>

                  {dbDropdownOpen && (
                    <div className="absolute right-0 sm:left-0 sm:right-auto bottom-full mb-2 w-full min-w-[175px] max-w-[calc(100vw-2rem)] max-h-none h-auto overflow-visible rounded-[var(--radius-card)] border border-line-strong bg-surface-900/98 backdrop-blur-2xl p-1.5 shadow-2xl z-50 flex flex-col gap-0.5 select-none animate-dropdown-in">
                      {[
                        { value: 'MSSQL', label: 'SQL Server' },
                        { value: 'PostgreSQL', label: 'PostgreSQL' },
                        { value: 'MySQL', label: 'MySQL' },
                        { value: 'SQLite', label: 'SQLite' },
                        { value: 'Oracle', label: 'Oracle' },
                        { value: 'MariaDB', label: 'MariaDB' }
                      ].map(db => {
                        const isSelected = dbType === db.value;
                        return (
                          <button
                            key={db.value}
                            type="button"
                            onClick={() => {
                              setDbType(db.value as DbType);
                              setDbDropdownOpen(false);
                            }}
                            className={`flex items-center justify-between px-3 py-2 rounded-[var(--radius-control)] text-xs font-mono cursor-pointer transition-colors text-left ${
                              isSelected
                                ? 'bg-surface-700/90 text-content-primary font-semibold'
                                : 'text-content-secondary hover:bg-surface-800 hover:text-content-primary'
                            }`}
                          >
                            <span>{db.label}</span>
                            {isSelected && (
                              <Check className="w-3.5 h-3.5 text-content-primary shrink-0" />
                            )}
                          </button>
                        );
                      })}
                    </div>
                  )}
                </div>
              </div>

              {/* Generate Button — Shopify White CTA */}
              <button
                type="submit"
                disabled={isGenerating || isClarifying || !prompt.trim()}
                className="h-[42px] w-full sm:w-auto px-7 bg-accent hover:bg-accent-hover text-accent-on font-mono font-bold rounded-full transition-all duration-150 flex items-center justify-center gap-2 shrink-0 disabled:opacity-50 disabled:cursor-not-allowed text-xs uppercase tracking-wider cursor-pointer shadow-md active:scale-[0.98]"
              >
                {isGenerating || isClarifying ? (
                  <>
                    <Loader2 className="w-3.5 h-3.5 animate-spin text-accent-on" />
                    <span>{isClarifying ? t('newProjectPage.thinking') : t('newProjectPage.generating')}</span>
                  </>
                ) : (
                  <>
                    <Wand2 className="w-3.5 h-3.5 text-accent-on" />
                    <span>{t('newProjectPage.generate')}</span>
                  </>
                )}
              </button>
            </div>
          </form>
        </div>
      </main>

      {clarify && !planAnswers && (
        <ClarifyDialog
          data={clarify}
          isGenerating={isGenerating}
          onCancel={() => setClarify(null)}
          // Doğrudan üretmiyor — cevapları Plan ekranına devrediyor. Plan modu
          // bir iyileştirme olduğu için burada çökerse (bkz. handleGenerate'in
          // catch'i) o yol zaten runGeneration'ı doğrudan çağırıyor.
          onSubmit={(answers) => setPlanAnswers(answers)}
        />
      )}

      {planAnswers && (
        <PlanScreen
          prompt={prompt}
          initialAnswers={planAnswers}
          isGenerating={isGenerating}
          onCancel={() => setPlanAnswers(null)}
          onApprove={(answers) => {
            setClarify(null);
            runGeneration(answers);
          }}
        />
      )}

      {starterModalOpen && (
        <StarterPickerModal
          onPick={(template) => {
            // Ağ yok, kota yok, bekleme yok: şema zaten elimizde.
            loadFromSchema(template.schema);
            // Ad da şablondan geliyor: aksi hâlde yeni proje bir önceki
            // projenin adını miras alıyor ve çalışma alanında içeriği
            // tamamen farklı, aynı adlı ikinci bir proje beliriyor
            // (canlı doğrulamada görüldü).
            setProjectName(template.label);
            goToCanvasWithCurrentSchema();
          }}
          onClose={() => setStarterModalOpen(false)}
        />
      )}

      {/* Canvas'taki panellerin AYNISI — ikinci bir kopya yazmak, iki ekranın
          ayrışması demekti. `onApplied` yalnızca şema gerçekten geldiğinde
          çalışıyor, iptal edildiğinde değil. */}
      <CodeImportPanel
        isOpen={codeModalOpen}
        onClose={() => setCodeModalOpen(false)}
        onApplied={goToCanvasWithCurrentSchema}
      />

      <DbConnectionPanel
        isOpen={dbModalOpen}
        onClose={() => setDbModalOpen(false)}
        onApplied={goToCanvasWithCurrentSchema}
      />

      {jsonModalOpen && (
        <JsonShapeModal
          onInfer={(responses) => schemaService.inferShapes(responses)}
          onApply={(inferredSchema) => {
            loadFromSchema(inferredSchema);
            setProjectName('Inferred model');
            goToCanvasWithCurrentSchema();
          }}
          onClose={() => setJsonModalOpen(false)}
        />
      )}

      {githubModalOpen && (
        <GithubScanModal
          onScan={(repoUrl, branch) => sourceService.scanRepository(repoUrl, branch)}
          onImport={(result: RepositoryScanResult) => {
            // Üretim akışının bitişiyle aynı: şema store'a yüklenip canvas'a
            // gidiliyor. Ayrı bir yol açmak, iki farklı "şema hazır" davranışı
            // demek olurdu.
            useProjectHistoryStore.getState().setActiveProjectId(null);
            loadFromSchema(result.schema as DatabaseSchema, undefined, false);
            goToCanvasWithCurrentSchema();
          }}
          onClose={() => setGithubModalOpen(false)}
        />
      )}

      {showProduction && (
        <ProductionScreen
          steps={productionSteps}
          isRunning={isGenerating}
          summary={agentSummary}
          onClose={closeProduction}
        />
      )}
    </div>
  );
}
