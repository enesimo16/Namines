'use client';

import { useState, useRef, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { Loader2, X } from 'lucide-react';
import { schemaService } from '../services/api';
import { useSchemaStore } from '../store/useSchemaStore';
import { useToastStore } from '../store/useToastStore';
import VoiceRecorder from '../components/landing/VoiceRecorder';
import AIProviderSelector from '../components/landing/AIProviderSelector';

export default function LandingPage() {
  const [prompt, setPrompt] = useState('');
  const [image, setImage] = useState<File | null>(null);
  const [referenceUrl, setReferenceUrl] = useState('');
  const [showUrlInput, setShowUrlInput] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  
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

  const groqModels = [
    { id: 'llama-3.3-70b-versatile', label: 'Llama 3.3 (70B)' },
    { id: 'llama-3.1-8b-instant', label: 'Llama 3.1 (8B)' },
    { id: 'mixtral-8x7b-32768', label: 'Mixtral 8x7B' }
  ];

  const ollamaModels = [
    { id: 'qwen2.5-coder', label: 'Qwen 2.5 Coder' },
    { id: 'deepseek-coder', label: 'DeepSeek Coder' },
    { id: 'sqlcoder', label: 'SQLCoder' }
  ];

  const models = aiProvider === 'Groq' ? groqModels : ollamaModels;

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
    } catch (error) {
      console.error('Failed to generate schema:', error);
      showToast('An error occurred while generating the schema. Please try again.', 'error');
      setIsGenerating(false);
    }
  };

  return (
    <div className="relative font-sans text-white flex-1 flex flex-col items-center justify-center overflow-hidden h-[calc(100vh-56px)]">
      {/* Background Effects */}
      <div aria-hidden="true" className="ocean-wave">
        <div className="wave wave1"></div>
        <div className="wave wave2"></div>
        <div className="wave wave3"></div>
      </div>
      <div aria-hidden="true" id="stars-container"></div>

      {/* Main Content Container */}
      <main className="relative z-10 w-full max-w-4xl px-4 flex flex-col items-center justify-center py-4">
        {/* Hero Section */}
        <div className="text-center mb-16">
          <div className="inline-flex items-center justify-center w-20 h-20 rounded-full bg-gradient-to-br from-indigo-500/20 to-cyan-500/20 text-cyan-400 mb-6 shadow-[0_0_30px_rgba(6,182,212,0.3)] glass-panel border border-cyan-500/30">
            <svg className="w-14 h-14 drop-shadow-[0_0_15px_rgba(99,102,241,0.5)] animate-none" viewBox="0 0 100 100" fill="none">
              <circle cx="50" cy="50" r="46" stroke="url(#circle-grad-landing)" strokeWidth="3" fill="#090B11" />
              <path d="M20,62 C32,48 42,66 52,52 C62,38 72,56 84,42 L84,82 L20,82 Z" fill="url(#wave-grad-landing)" opacity="0.8" />
              <path d="M16,68 C28,56 38,74 50,62 C62,50 72,68 84,56 L84,84 L16,84 Z" fill="url(#wave-grad-2-landing)" opacity="0.4" />
              {/* Stars */}
              <circle cx="35" cy="30" r="1.5" fill="#FFF" />
              <circle cx="65" cy="25" r="2" fill="#FFF" />
              <circle cx="50" cy="20" r="1" fill="#FFF" />
              <circle cx="75" cy="35" r="1.2" fill="#FFF" />
              <defs>
                <linearGradient id="circle-grad-landing" x1="0" y1="0" x2="100" y2="100">
                  <stop offset="0%" stopColor="#06b6d4" />
                  <stop offset="50%" stopColor="#818cf8" />
                  <stop offset="100%" stopColor="#a855f7" />
                </linearGradient>
                <linearGradient id="wave-grad-landing" x1="50" y1="30" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#06b6d4" stopOpacity="0.8" />
                  <stop offset="100%" stopColor="#1e1b4b" stopOpacity="0.1" />
                </linearGradient>
                <linearGradient id="wave-grad-2-landing" x1="50" y1="40" x2="50" y2="90" gradientUnits="userSpaceOnUse">
                  <stop offset="0%" stopColor="#818cf8" stopOpacity="0.6" />
                  <stop offset="100%" stopColor="#0f172a" stopOpacity="0" />
                </linearGradient>
              </defs>
            </svg>
          </div>
          <h1 className="text-5xl font-extrabold tracking-tight mb-4 text-transparent bg-clip-text bg-gradient-to-r from-white to-gray-400 drop-shadow-md">
            Namines
          </h1>
          <p className="text-gray-400 text-lg font-medium">
            Design interactive database architectures in seconds with artificial intelligence.
          </p>
        </div>

        {/* Form Card */}
        <div className="w-full max-w-3xl glass-panel rounded-3xl p-6 sm:p-8 relative overflow-hidden group">
          <div className="absolute -inset-1 bg-gradient-to-r from-indigo-500 to-purple-600 rounded-3xl blur opacity-10 group-hover:opacity-20 transition duration-1000 group-hover:duration-200"></div>
          
          <form onSubmit={handleGenerate} className="relative">
            {/* Textarea Section */}
            <div className="mb-4 relative">
              <textarea
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                placeholder="e.g. Design an e-commerce database similar to Amazon, where users can add products to carts and place orders..."
                className="w-full h-40 p-4 rounded-xl glass-input resize-none placeholder-gray-500 text-sm leading-relaxed"
                disabled={isGenerating}
              ></textarea>
              
              <div className="absolute bottom-3 right-3 flex gap-2">
                <button
                  type="button"
                  onClick={() => setShowUrlInput(!showUrlInput)}
                  className={`w-8 h-8 rounded-lg glass-button flex items-center justify-center transition-all ${showUrlInput || referenceUrl ? 'text-indigo-400' : 'text-gray-400 hover:text-white'}`}
                  title="Add Link"
                >
                  <i className="fa-solid fa-link text-xs"></i>
                </button>
                <button
                  type="button"
                  onClick={() => fileInputRef.current?.click()}
                  className={`w-8 h-8 rounded-lg glass-button flex items-center justify-center transition-all ${image ? 'text-indigo-400' : 'text-gray-400 hover:text-white'}`}
                  title="Add Image"
                >
                  <i className="fa-regular fa-image text-xs"></i>
                </button>
                <input
                  type="file"
                  ref={fileInputRef}
                  onChange={handleImageUpload}
                  accept=".png,.jpg,.jpeg"
                  className="hidden"
                />
                <div className="w-8 h-8 rounded-lg glass-button flex items-center justify-center transition-all overflow-hidden relative">
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
              <div className="flex flex-col gap-3 p-3 bg-ocean-dark/80 rounded-xl border border-white/5 mb-6">
                {showUrlInput && (
                  <div className="flex items-center gap-2">
                    <i className="fa-solid fa-link text-xs text-gray-500"></i>
                    <input
                      type="url"
                      value={referenceUrl}
                      onChange={(e) => setReferenceUrl(e.target.value)}
                      placeholder="https://example.com/schema-docs"
                      className="flex-1 bg-transparent text-sm text-gray-300 placeholder:text-gray-600 focus:outline-none"
                      disabled={isGenerating}
                    />
                    {referenceUrl && (
                      <button type="button" onClick={() => setReferenceUrl('')} className="text-gray-500 hover:text-gray-300">
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
                      <p className="text-sm text-gray-300 truncate">{image.name}</p>
                      <p className="text-xs text-gray-500">{(image.size / 1024).toFixed(1)} KB</p>
                    </div>
                    <button type="button" onClick={() => setImage(null)} className="p-1 text-gray-500 hover:text-red-400 transition-colors">
                      <X className="w-5 h-5" />
                    </button>
                  </div>
                )}
              </div>
            )}

            {/* Options & Submit Section */}
            <div className="flex flex-wrap md:flex-nowrap items-center justify-between gap-4 mt-6">
              <div className="flex flex-wrap items-center gap-3 w-full md:w-auto">
                {/* AIProviderSelector */}
                <AIProviderSelector />

                {/* Model Select */}
                <div className="relative">
                  <select
                    value={modelName}
                    onChange={(e) => setProviderAndModel(aiProvider, e.target.value)}
                    disabled={isGenerating}
                    className="appearance-none glass-input rounded-lg pl-3 pr-8 py-2 text-sm text-gray-300 focus:ring-0 cursor-pointer w-[145px] font-medium"
                  >
                    {models.map(m => (
                      <option key={m.id} value={m.id}>{m.label}</option>
                    ))}
                  </select>
                  <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-2 text-gray-400">
                    <i className="fa-solid fa-chevron-down text-xs"></i>
                  </div>
                </div>

                {/* Database Select */}
                <div className="relative">
                  <select 
                    value={dbType}
                    onChange={(e) => setDbType(e.target.value as any)}
                    disabled={isGenerating}
                    className="appearance-none glass-input rounded-lg pl-3 pr-8 py-2 text-sm text-gray-300 focus:ring-0 cursor-pointer w-[125px] font-medium"
                  >
                    <option value="MSSQL">SQL Server</option>
                    <option value="PostgreSQL">PostgreSQL</option>
                    <option value="MySQL">MySQL</option>
                    <option value="SQLite">SQLite</option>
                    <option value="Oracle">Oracle</option>
                    <option value="MariaDB">MariaDB</option>
                    <option value="Db2">IBM Db2</option>
                    <option value="Firebird">Firebird</option>
                    <option value="Spanner">Google Spanner</option>
                    <option value="Redshift">Amazon Redshift</option>
                  </select>
                  <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-2 text-gray-400">
                    <i className="fa-solid fa-chevron-down text-xs"></i>
                  </div>
                </div>
              </div>

              {/* Generate Button */}
              <button
                type="submit"
                disabled={isGenerating || !prompt.trim()}
                className="w-full md:w-auto bg-gradient-to-r from-indigo-600 to-purple-600 hover:from-indigo-500 hover:to-purple-500 text-white font-medium py-3 px-6 rounded-xl shadow-neon hover:shadow-neon-hover transition-all duration-300 flex items-center justify-center gap-2 border border-indigo-400/30 shrink-0 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {isGenerating ? (
                  <>
                    <Loader2 className="w-5 h-5 animate-spin" />
                    <span>Generating...</span>
                  </>
                ) : (
                  <>
                    <i className="fa-solid fa-wand-magic-sparkles"></i>
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
