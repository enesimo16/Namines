'use client';

import { useState, useRef, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Loader2, X, Link as LinkIcon, Image as ImageIcon, ChevronDown, Check, Wand2 } from 'lucide-react';
import { schemaService } from '../services/api';
import { useSchemaStore } from '../store/useSchemaStore';
import { useToastStore } from '../store/useToastStore';
import { useAuthModalStore } from '../store/useAuthModalStore';
import VoiceRecorder from '../components/landing/VoiceRecorder';

export default function LandingPage() {
  const [prompt, setPrompt] = useState('');
  const [image, setImage] = useState<File | null>(null);
  const [referenceUrl, setReferenceUrl] = useState('');
  const [showUrlInput, setShowUrlInput] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  
  const modelDropdownRef = useRef<HTMLDivElement>(null);
  const dbDropdownRef = useRef<HTMLDivElement>(null);
  const [modelDropdownOpen, setModelDropdownOpen] = useState(false);
  const [dbDropdownOpen, setDbDropdownOpen] = useState(false);

  const router = useRouter();
  // V2: dbType artık global store'dan geliyor
  const { 
    setIsGenerating, 
    loadFromSchema, 
    isGenerating, 
    aiProvider, 
    modelName, 
    dbType, 
    setDbType,
    setProviderAndModel 
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

  // Unified model list: Groq Cloud + Gemini + Ollama Local
  const allModels = [
    // ── Groq Cloud ────────────────────────────────────────
    { id: 'llama-3.1-8b-instant',      label: 'Llama 3.1 (8B)',         provider: 'Groq',   group: 'Groq Cloud' },
    { id: 'llama-3.3-70b-versatile',   label: 'Llama 3.3 (70B)',        provider: 'Groq',   group: 'Groq Cloud' },
    { id: 'mixtral-8x7b-32768',        label: 'Mixtral 8x7B',           provider: 'Groq',   group: 'Groq Cloud' },
    { id: 'openai/gpt-oss-120b',       label: 'GPT-OSS 120B (Ultra)',   provider: 'Groq',   group: 'Groq Cloud' },
    // ── Google Gemini ─────────────────────────────────────
    { id: 'gemini-1.5-flash',          label: 'Gemini 2.5 Flash',       provider: 'Gemini', group: 'Google Gemini' },
    { id: 'gemini-1.5-pro',            label: 'Gemini 2.5 Pro',         provider: 'Gemini', group: 'Google Gemini' },
    // ── Local (Ollama / qwen fixed) ─────────────────────
    { id: 'qwen2.5-coder:7b',          label: 'Qwen 2.5 Coder 7B',     provider: 'Ollama', group: 'Local Engine' },
  ];

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

  const handleGenerate = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!prompt.trim()) return;

    try {
      setIsGenerating(true);
      const schema = await schemaService.generateSchema(prompt, dbType, aiProvider, modelName, image, referenceUrl);
      loadFromSchema(schema);
      router.push('/canvas');
    } catch (error: any) {
      console.error('Failed to generate schema:', error);
      if (error?.response?.status === 401) {
        // Guest: AI şema üretimi giriş gerektiriyor → net mesaj + login modalı.
        showToast('Please log in to generate a schema.', 'warning');
        useAuthModalStore.getState().open();
      } else {
        showToast('An error occurred while generating the schema. Please try again.', 'error');
      }
      setIsGenerating(false);
    }
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
                  className={`w-7 h-7 rounded-lg glass-button flex items-center justify-center transition-all ${showUrlInput || referenceUrl ? 'text-content-primary' : 'text-content-muted hover:text-content-primary'}`}
                  title="Add Link"
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
                  <div className="flex items-center gap-2">
                    <LinkIcon className="w-3.5 h-3.5 text-content-muted" />
                    <input
                      type="url"
                      value={referenceUrl}
                      onChange={(e) => setReferenceUrl(e.target.value)}
                      placeholder="https://example.com/schema-docs"
                      className="flex-1 bg-transparent text-sm text-content-primary placeholder:text-content-muted focus:outline-none"
                      disabled={isGenerating}
                    />
                    {referenceUrl && (
                      <button type="button" onClick={() => setReferenceUrl('')} className="text-content-muted hover:text-content-primary">
                        <X className="w-4 h-4" />
                      </button>
                    )}
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

                {/* Unified AI Model Select */}
                <div className="relative flex-1 min-w-[120px] sm:flex-initial sm:w-[205px]" ref={modelDropdownRef}>
                  <button
                    type="button"
                    disabled={isGenerating}
                    onClick={() => setModelDropdownOpen(!modelDropdownOpen)}
                    className="flex items-center justify-between glass-input rounded-lg pl-3 pr-3.5 py-2 text-sm text-content-primary focus:ring-0 cursor-pointer w-full font-medium text-left select-none disabled:opacity-50 disabled:cursor-not-allowed hover:bg-surface-700/70"
                  >
                    <span className="truncate">
                      {allModels.find(m => m.id === modelName)?.label || modelName}
                    </span>
                    <ChevronDown className={`w-3 h-3 text-content-muted transition-transform duration-200 ${modelDropdownOpen ? 'rotate-180' : ''}`} />
                  </button>

                  {modelDropdownOpen && (
                    <div className="absolute left-0 bottom-full mb-2 w-full max-h-none h-auto overflow-visible rounded-xl border border-content-primary/15 bg-surface-800/95 backdrop-blur-xl p-2 shadow-[0_-8px_32px_rgba(0,0,0,0.4)] z-50 flex flex-col gap-1 select-none animate-dropdown-in">
                      {allModels.map(m => {
                        const isSelected = m.id === modelName;
                        return (
                          <button
                            key={m.id}
                            type="button"
                            onClick={() => {
                              setProviderAndModel(m.provider as 'Groq' | 'Ollama' | 'Gemini', m.id);
                              setModelDropdownOpen(false);
                            }}
                            className={`flex items-center justify-between px-3 py-1.5 rounded-lg text-xs font-medium cursor-pointer transition-all text-left ${
                              isSelected
                                ? 'bg-white/[0.08] text-content-primary border-l-2 border-white/40 pl-2'
                                : 'text-content-muted hover:bg-white/[0.04] hover:text-content-primary'
                            }`}
                          >
                            <span>{m.label}</span>
                            {isSelected && (
                                <Check className="w-3 h-3 text-content-primary" />
                            )}
                          </button>
                        );
                      })}
                    </div>
                  )}
                </div>

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
                disabled={isGenerating || !prompt.trim()}
                className="w-full md:w-auto bg-content-primary hover:bg-content-secondary text-surface-900 font-semibold py-2.5 px-5 rounded-xl transition-all duration-200 flex items-center justify-center gap-2 shrink-0 disabled:opacity-50 disabled:cursor-not-allowed text-sm"
              >
                {isGenerating ? (
                  <>
                    <Loader2 className="w-4 h-4 animate-spin" />
                    <span>Generating...</span>
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
    </div>
  );
}
