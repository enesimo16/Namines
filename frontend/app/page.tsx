'use client';

import { useState, useRef, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Loader2, X, Link as LinkIcon, Image as ImageIcon, ChevronDown, Check, Wand2 } from 'lucide-react';
import { schemaService } from '../services/api';
import { useSchemaStore } from '../store/useSchemaStore';
import { useToastStore } from '../store/useToastStore';
import { useAuthModalStore } from '../store/useAuthModalStore';
import VoiceRecorder from '../components/landing/VoiceRecorder';
import ClarifyDialog from '../components/landing/ClarifyDialog';
import ProductionScreen from '../components/landing/ProductionScreen';
import PlanScreen from '../components/landing/PlanScreen';
import { streamSchemaGeneration, AgentStepEvent } from '../lib/sseSchemaStream';
import { ClarifyResponse, NaiModelOption } from '../types/nai';

export default function LandingPage() {
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
  const [models, setModels] = useState<NaiModelOption[]>([]);

  const router = useRouter();
  // V2: dbType artık global store'dan geliyor
  const { 
    setIsGenerating, 
    loadFromSchema, 
    isGenerating, 
    naiModel,
    dbType, 
    setDbType,
    setNaiModel 
  } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);

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

  // Model listesi SUNUCUDAN geliyor: hangi modelin hangi plana açık olduğunu
  // istemcide tekrar yazmak, iki kaynağın ayrışması demekti. Liste alınamazsa
  // seçici gizleniyor ve varsayılan model kullanılıyor — model seçememek şema
  // üretimini engellememeli.
  useEffect(() => {
    let cancelled = false;
    schemaService.naiModels()
      .then(list => { if (!cancelled) setModels(list); })
      .catch(() => { /* sessiz: seçici olmadan da üretim çalışıyor */ });
    return () => { cancelled = true; };
  }, []);

  const selectedModel = models.find(m => m.id === naiModel);


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
    setShowProduction(true);

    const formData = schemaService.buildGenerateFormData(prompt, dbType, naiModel, image, apiSpecUrl, answers);

    await streamSchemaGeneration(formData, {
      onStep: (step) => setProductionSteps(prev => [...prev, step]),

      onResult: (result) => {
        loadFromSchema(result.schema as any);
        // second-phase/09-SEMA-ALTERNATIFLERI.md — canvas'taki "Alternatif üret"
        // bu prompt+cevapları tekrar kullanacak, o yüzden burada saklanıyor.
        useSchemaStore.getState().recordGenerationSource(prompt, answers);
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

  return (
    <div className="relative font-sans text-content-primary flex-1 flex flex-col items-center justify-center overflow-hidden min-h-[calc(100vh-56px)] py-8">
      {/* Background Effects */}
      <div aria-hidden="true" className="ocean-wave">
        <div className="wave wave1"></div>
        <div className="wave wave2"></div>
        <div className="wave wave3"></div>
      </div>
      <div aria-hidden="true" id="stars-container"></div>

      {/* Main Content Container */}
      <main className="relative z-10 w-full max-w-4xl px-4 flex flex-col items-center justify-center">
        {/* Hero Section — minimalist, ikon kutusu kaldırıldı (bkz. FRONTEND.md) */}
        <div className="text-center mb-8 sm:mb-10">
          <h1 className="font-mono text-3xl sm:text-4xl font-bold tracking-tight mb-3 text-content-primary">
            Namines
          </h1>
          <p className="text-content-primary text-sm sm:text-base font-medium max-w-md mx-auto">
            Design interactive database architectures in seconds with artificial intelligence.
          </p>
        </div>

        {/* Form Card */}
        <div className="w-full max-w-2xl glass-panel rounded-2xl p-4 sm:p-6 relative overflow-visible group">
          <form onSubmit={handleGenerate} className="relative">
            {/* Textarea Section */}
            <div className="mb-4 relative">
              <textarea
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                placeholder="e.g. Design an e-commerce database similar to Amazon, where users can add products to carts and place orders..."
                className="w-full h-24 sm:h-28 p-3 rounded-xl glass-input resize-none placeholder-content-muted text-sm leading-relaxed"
                disabled={isGenerating}
              ></textarea>

              <div className="absolute bottom-2.5 right-2.5 flex gap-1.5">
                <button
                  type="button"
                  onClick={() => setShowUrlInput(!showUrlInput)}
                  className={`w-7 h-7 rounded-lg glass-button flex items-center justify-center transition-all ${showUrlInput || apiSpecUrl ? 'text-content-primary' : 'text-content-muted hover:text-content-primary'}`}
                  title="Infer from a GraphQL or OpenAPI/Swagger URL"
                >
                  <LinkIcon className="w-3.5 h-3.5" />
                </button>
                <button
                  type="button"
                  onClick={() => fileInputRef.current?.click()}
                  className={`w-7 h-7 rounded-lg glass-button flex items-center justify-center transition-all ${image ? 'text-content-primary' : 'text-content-muted hover:text-content-primary'}`}
                  title="Add Image"
                >
                  <ImageIcon className="w-3.5 h-3.5" />
                </button>
                <input
                  type="file"
                  ref={fileInputRef}
                  onChange={handleImageUpload}
                  accept=".png,.jpg,.jpeg"
                  className="hidden"
                />
                <div className="w-7 h-7 rounded-lg glass-button flex items-center justify-center transition-all overflow-hidden relative">
                  <div className="absolute inset-0 flex items-center justify-center scale-[0.8]">
                    <VoiceRecorder
                      disabled={isGenerating}
                      onTranscription={(text) => setPrompt(prev => prev ? `${prev} ${text}` : text)}
                    />
                  </div>
                </div>
              </div>
            </div>

            {/* Extended Inputs Area */}
            {(showUrlInput || image) && (
              <div className="flex flex-col gap-3 p-3 bg-surface-800/80 rounded-xl border border-white/[0.04] mb-6">
                {showUrlInput && (
                  <div className="flex flex-col gap-1.5">
                    <div className="flex items-center gap-2">
                      <LinkIcon className="w-3.5 h-3.5 text-content-muted shrink-0" />
                      <input
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
                    {/* Bir sitenin GERÇEK veritabanını dışarıdan okumak mümkün
                        değil — çıkarılan şey API'nin dışa açtığı bir tahmin.
                        second-phase/06-VERI-KAYNAKLARI.md */}
                    <p className="text-[10px] text-content-muted pl-5">
                      We read the API&apos;s schema (GraphQL or OpenAPI), not the actual database — it&apos;s a guess to start from.
                    </p>
                  </div>
                )}

                {image && (
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 rounded-lg overflow-hidden border border-white/10 shrink-0">
                      <img src={URL.createObjectURL(image)} alt="Preview" className="w-full h-full object-cover" />
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-sm text-content-primary truncate">{image.name}</p>
                      <p className="text-xs text-content-muted">{(image.size / 1024).toFixed(1)} KB</p>
                    </div>
                    <button type="button" onClick={() => setImage(null)} className="p-1 text-content-muted hover:text-danger transition-colors">
                      <X className="w-5 h-5" />
                    </button>
                  </div>
                )}
              </div>
            )}

            {/* Options & Submit Section */}
            <div className="flex flex-wrap items-center justify-between gap-3 mt-4">
              <div className="flex items-center gap-2 sm:gap-3 w-full md:w-auto shrink-0">

                {/* Namines AI model seçici (new-phase/36 §3).
                    Sağlayıcı adları (Groq/Gemini/Ollama) ve model kimlikleri
                    ARTIK GÖSTERİLMİYOR: kullanıcının "llama-3.3-70b" ile
                    "mixtral" arasında seçim yapması gereken bir karar değildi,
                    ve sağlayıcı o modeli kaldırdığında ürün bozulmuş
                    görünüyordu. */}
                {models.length > 0 && (
                <div className="relative flex-1 min-w-[120px] sm:flex-initial sm:w-[205px]" ref={modelDropdownRef}>
                  <button
                    type="button"
                    disabled={isGenerating}
                    onClick={() => setModelDropdownOpen(!modelDropdownOpen)}
                    className="flex items-center justify-between glass-input rounded-lg pl-3 pr-3.5 py-2 text-sm text-content-primary focus:ring-0 cursor-pointer w-full font-medium text-left select-none disabled:opacity-50 disabled:cursor-not-allowed hover:bg-surface-700/70"
                  >
                    <span className="truncate">
                      {selectedModel?.displayName || 'Namines AI'}
                    </span>
                    <ChevronDown className={`w-3 h-3 text-content-muted transition-transform duration-200 ${modelDropdownOpen ? 'rotate-180' : ''}`} />
                  </button>

                  {modelDropdownOpen && (
                    <div className="absolute left-0 bottom-full mb-2 w-[260px] rounded-xl border border-content-primary/15 bg-surface-800/95 backdrop-blur-xl p-2 shadow-[0_-8px_32px_rgba(0,0,0,0.4)] z-50 flex flex-col gap-1 select-none animate-dropdown-in">
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
                            className={`flex items-start justify-between gap-2 px-3 py-2 rounded-lg cursor-pointer transition-all text-left ${
                              !m.available
                                ? 'opacity-40 cursor-not-allowed text-content-muted'
                                : isSelected
                                ? 'bg-white/[0.08] text-content-primary border-l-2 border-white/40 pl-2'
                                : 'text-content-muted hover:bg-white/[0.04] hover:text-content-primary'
                            }`}
                          >
                            <span className="min-w-0">
                              <span className="block text-xs font-medium">{m.displayName}</span>
                              <span className="block text-[10px] opacity-70 leading-snug">{m.description}</span>
                              {/* Maliyet çarpanı gösteriliyor: kullanıcı bütçesini
                                  daha hızlı tükettiğini faturayı görünce değil,
                                  seçerken bilmeli. */}
                              <span className="block text-[10px] opacity-50 mt-0.5">
                                {m.available ? `Uses ${m.costMultiplier}× budget` : 'Available on paid plans'}
                              </span>
                            </span>
                            {isSelected && m.available && (
                              <Check className="w-3 h-3 text-content-primary shrink-0 mt-0.5" />
                            )}
                          </button>
                        );
                      })}
                    </div>
                  )}
                </div>
                )}

                {/* Database Select */}
                <div className="relative flex-1 min-w-[140px] sm:flex-initial sm:w-[215px]" ref={dbDropdownRef}>
                  <button
                    type="button"
                    disabled={isGenerating}
                    onClick={() => setDbDropdownOpen(!dbDropdownOpen)}
                    className="flex items-center justify-between glass-input rounded-lg pl-3 pr-3.5 py-2 text-sm text-content-primary focus:ring-0 cursor-pointer w-full font-medium text-left select-none disabled:opacity-50 disabled:cursor-not-allowed hover:bg-surface-700/70"
                  >
                    <span className="truncate">
                      {dbType === 'MSSQL' ? 'SQL Server' :
                       dbType === 'PostgreSQL' ? 'PostgreSQL' :
                       dbType === 'MySQL' ? 'MySQL' :
                       dbType === 'SQLite' ? 'SQLite' :
                       dbType === 'Oracle' ? 'Oracle' :
                       dbType === 'MariaDB' ? 'MariaDB' :
                       dbType === 'Db2' ? 'IBM Db2' :
                       dbType === 'Firebird' ? 'Firebird' :
                       dbType === 'Spanner' ? 'Google Spanner' :
                       dbType === 'Redshift' ? 'Amazon Redshift' : dbType}
                    </span>
                    <ChevronDown className={`w-3 h-3 text-content-muted transition-transform duration-200 ${dbDropdownOpen ? 'rotate-180' : ''}`} />
                  </button>

                  {dbDropdownOpen && (
                    <div className="absolute left-0 bottom-full mb-2 w-full max-h-none h-auto overflow-visible rounded-xl border border-content-primary/15 bg-surface-800/95 backdrop-blur-xl p-2 shadow-[0_-8px_32px_rgba(0,0,0,0.4)] z-50 flex flex-col gap-0.5 select-none animate-dropdown-in">
                      {[
                        { value: 'MSSQL', label: 'SQL Server' },
                        { value: 'PostgreSQL', label: 'PostgreSQL' },
                        { value: 'MySQL', label: 'MySQL' },
                        { value: 'SQLite', label: 'SQLite' },
                        { value: 'Oracle', label: 'Oracle' },
                        { value: 'MariaDB', label: 'MariaDB' },
                        { value: 'Db2', label: 'IBM Db2' },
                        { value: 'Firebird', label: 'Firebird' },
                        { value: 'Spanner', label: 'Google Spanner' },
                        { value: 'Redshift', label: 'Amazon Redshift' }
                      ].map(db => {
                        const isSelected = dbType === db.value;
                        return (
                          <button
                            key={db.value}
                            type="button"
                            onClick={() => {
                              setDbType(db.value as any);
                              setDbDropdownOpen(false);
                            }}
                            className={`flex items-center justify-between px-3 py-1.5 rounded-lg text-xs font-medium cursor-pointer transition-all text-left ${
                              isSelected
                                ? 'bg-white/[0.08] text-content-primary border-l-2 border-white/40 pl-2'
                                : 'text-content-muted hover:bg-white/[0.04] hover:text-content-primary'
                            }`}
                          >
                            <span>{db.label}</span>
                            {isSelected && (
                                <Check className="w-3 h-3 text-content-primary" />
                            )}
                          </button>
                        );
                      })}
                    </div>
                  )}
                </div>
              </div>

              {/* Generate Button */}
              <button
                type="submit"
                disabled={isGenerating || isClarifying || !prompt.trim()}
                className="w-full md:w-auto bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2.5 px-5 rounded-xl transition-all duration-200 flex items-center justify-center gap-2 shrink-0 disabled:opacity-50 disabled:cursor-not-allowed text-sm"
              >
                {isGenerating || isClarifying ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    <span>{isClarifying ? 'Thinking...' : 'Generating...'}</span>
                  </>
                ) : (
                  <>
                    <Wand2 className="w-4 h-4" />
                    <span>Generate Schema</span>
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

      {showProduction && (
        <ProductionScreen
          steps={productionSteps}
          isRunning={isGenerating}
          onClose={closeProduction}
        />
      )}
    </div>
  );
}
