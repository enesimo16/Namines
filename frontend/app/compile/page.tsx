'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, Code2 } from 'lucide-react';
import { useSchemaStore, DbType } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { schemaService } from '../../services/api';

import DbTypeSelector from '../../components/compile/DbTypeSelector';
import SqlPreview from '../../components/compile/SqlPreview';
import DataDictionaryPreview from '../../components/compile/DataDictionaryPreview';
import ReadmePreview from '../../components/compile/ReadmePreview';
import DockerProgressModal from '../../components/compile/DockerProgressModal';
import MermaidPreview from '../../components/compile/MermaidPreview';
import DownloadHubPanel from '../../components/compile/DownloadHubPanel';
import DockerSandboxPanel from '../../components/compile/DockerSandboxPanel';
import SmartSeedPanel from '../../components/compile/SmartSeedPanel';
import EfCorePreview from '../../components/compile/EfCorePreview';
import { useDockerJob } from '../../hooks/useDockerJob';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { 
  generateClassDiagram, 
  generateFlowchart, 
  generateMindmap, 
  generateStateDiagram,
  generateSequenceDiagram,
  generateGanttChart,
  generatePieChart,
  generateGitGraph,
  generateUserJourney,
  generateTimeline,
  generateQuadrantChart,
  generateRequirementDiagram
} from '../../utils/diagramGenerators';

export default function CompilePage() {
  const router = useRouter();
  const { schema, dbType, setDbType, projectName } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const { getActiveSandbox } = useProjectHistoryStore();

  // dbType artık global store'dan geliyor (V2)
  const [sql, setSql] = useState('');
  const [mockData, setMockData] = useState('');
  const [mermaidCode, setMermaidCode] = useState('');
  const [diagramType, setDiagramType] = useState<
    'ER' | 'CLASS' | 'FLOW' | 'MINDMAP' | 'STATE' | 'SEQUENCE' | 'GANTT' | 'PIE' | 'GIT' | 'JOURNEY' | 'TIMELINE' | 'QUADRANT' | 'REQUIREMENT'
  >('ER');
  const [activeTab, setActiveTab] = useState<'SQL' | 'EF' | 'ER' | 'MOCK' | 'DICTIONARY' | 'README' | 'SANDBOX' | 'ADMIN'>('SQL');
  const [isLoading, setIsLoading] = useState(false);
  const [isDockerModalOpen, setIsDockerModalOpen] = useState(false);
  const [isExportingSvg, setIsExportingSvg] = useState(false);

  const dockerJob = useDockerJob();

  useEffect(() => {
    if (!schema) {
      router.push('/');
      return;
    }

    const fetchSql = async () => {
      setIsLoading(true);
      try {
        const generatedSql = await schemaService.compileSql(schema, dbType);
        setSql(generatedSql);

        // Fetch mermaid code
        const mCode = await schemaService.generateMermaid(schema);
        setMermaidCode(mCode);
      } catch (error) {
        console.error("Failed to compile SQL", error);
        setSql('-- Error generating SQL');
      } finally {
        setIsLoading(false);
      }
    };

    fetchSql();
  }, [schema, dbType, router]);

  useEffect(() => {
    const container = document.getElementById('compile-stars-container');
    if (!container) return;
    
    function createStar() {
      const star = document.createElement('div');
      star.classList.add('shooting-star');
      
      const startY = Math.random() * window.innerHeight * 0.4;
      const startX = window.innerWidth + Math.random() * 200;
      
      star.style.top = `${startY}px`;
      star.style.right = `${window.innerWidth - startX}px`;
      
      const duration = 2.5 + Math.random() * 3;
      star.style.animationDuration = `${duration}s`;
      
      container?.appendChild(star);
      
      setTimeout(() => {
        if (container?.contains(star)) {
          star.remove();
        }
      }, duration * 1000);
    }

    const intervalId = setInterval(createStar, 1800);
    for(let i=0; i<3; i++) {
      setTimeout(createStar, i * 800);
    }

    return () => {
      clearInterval(intervalId);
      if (container) container.innerHTML = '';
    };
  }, []);

  const loadMockData = async () => {
    if (!schema || mockData) return;
    setIsLoading(true);
    try {
      const data = await schemaService.generateMockData(schema);
      setMockData(data);
    } catch (error) {
      console.error("Failed to generate mock data", error);
      setMockData('-- Error generating mock data');
    } finally {
      setIsLoading(false);
    }
  };

  const handleTabChange = (tab: 'SQL' | 'EF' | 'ER' | 'MOCK' | 'DICTIONARY' | 'README' | 'SANDBOX' | 'ADMIN') => {
    setActiveTab(tab);
    if (tab === 'MOCK' && !mockData) {
      loadMockData();
    }
  };

  const updateDiagram = async (
    type: 'ER' | 'CLASS' | 'FLOW' | 'MINDMAP' | 'STATE' | 'SEQUENCE' | 'GANTT' | 'PIE' | 'GIT' | 'JOURNEY' | 'TIMELINE' | 'QUADRANT' | 'REQUIREMENT'
  ) => {
    if (!schema) return;
    setDiagramType(type);
    if (type === 'ER') {
      try {
        const mCode = await schemaService.generateMermaid(schema);
        setMermaidCode(mCode);
      } catch (e) { console.error(e); }
    } else if (type === 'CLASS') {
      setMermaidCode(generateClassDiagram(schema));
    } else if (type === 'FLOW') {
      setMermaidCode(generateFlowchart(schema));
    } else if (type === 'MINDMAP') {
      setMermaidCode(generateMindmap(schema));
    } else if (type === 'STATE') {
      setMermaidCode(generateStateDiagram(schema));
    } else if (type === 'SEQUENCE') {
      setMermaidCode(generateSequenceDiagram(schema));
    } else if (type === 'GANTT') {
      setMermaidCode(generateGanttChart(schema));
    } else if (type === 'PIE') {
      setMermaidCode(generatePieChart(schema));
    } else if (type === 'GIT') {
      setMermaidCode(generateGitGraph(schema));
    } else if (type === 'JOURNEY') {
      setMermaidCode(generateUserJourney(schema));
    } else if (type === 'TIMELINE') {
      setMermaidCode(generateTimeline(schema));
    } else if (type === 'QUADRANT') {
      setMermaidCode(generateQuadrantChart(schema));
    } else if (type === 'REQUIREMENT') {
      setMermaidCode(generateRequirementDiagram(schema));
    }
  };

  /** Render edilen SVG element'ini yakalar ve .svg dosyası olarak indirir */
  const handleExportSvg = async () => {
    const svgEl = document.querySelector<SVGElement>('.mermaid-preview-container svg');
    if (!svgEl) {
      showToast('SVG has not been rendered yet, please wait.', 'warning');
      return;
    }
    setIsExportingSvg(true);
    try {
      const serializer = new XMLSerializer();
      const svgStr = serializer.serializeToString(svgEl);
      const blob = new Blob([svgStr], { type: 'image/svg+xml;charset=utf-8' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${schema?.name ?? 'mermaid-diagram'}-${diagramType.toLowerCase()}.svg`;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('SVG export error:', err);
    } finally {
      setIsExportingSvg(false);
    }
  };

  if (!schema) return null;

  return (
    <div className="min-h-screen bg-[#030208] text-zinc-200 flex flex-col font-sans relative overflow-hidden">
      {/* Dynamic Starry Sky Gradient */}
      <div className="absolute inset-0 pointer-events-none bg-gradient-to-b from-[#05040d] via-[#080718] to-[#020205] z-0" />
      <div className="absolute inset-0 pointer-events-none bg-[url('/noise.png')] opacity-[0.012] mix-blend-overlay z-0" />

      {/* Inline styles for Premium Wave & Twinkle Animations */}
      <style dangerouslySetInnerHTML={{__html: `
        @keyframes floatWavePurple {
          0%, 100% { transform: translateY(0) scaleY(1) skewX(0deg); }
          50% { transform: translateY(-25px) scaleY(1.08) skewX(1deg); }
        }
        @keyframes floatWaveTeal {
          0%, 100% { transform: translateY(0) scaleY(1) skewX(0deg); }
          50% { transform: translateY(18px) scaleY(0.93) skewX(-1.5deg); }
        }
        @keyframes floatWaveIndigo {
          0%, 100% { transform: translateY(0) scaleY(1) scaleX(1); }
          50% { transform: translateY(-12px) scaleY(1.04) scaleX(1.03); }
        }
        @keyframes floatWaveCyan {
          0%, 100% { transform: translateY(0) scaleY(1) rotate(0deg); }
          50% { transform: translateY(15px) scaleY(0.96) rotate(0.5deg); }
        }
        .animate-wave-purple {
          animation: floatWavePurple 16s ease-in-out infinite;
          transform-origin: bottom center;
        }
        .animate-wave-teal {
          animation: floatWaveTeal 19s ease-in-out infinite;
          transform-origin: bottom center;
        }
        .animate-wave-indigo {
          animation: floatWaveIndigo 23s ease-in-out infinite;
          transform-origin: bottom center;
        }
        .animate-wave-cyan {
          animation: floatWaveCyan 27s ease-in-out infinite;
          transform-origin: bottom center;
        }
        .shooting-star {
          position: absolute;
          width: 2px;
          height: 120px;
          background: linear-gradient(to bottom, rgba(255,255,255,0) 0%, rgba(255,255,255,1) 100%);
          top: -120px;
          right: 0;
          transform: rotate(45deg);
          z-index: 1;
          pointer-events: none;
          animation: shoot 4s linear infinite;
          opacity: 0;
          box-shadow: 0 0 10px rgba(255,255,255,0.8);
        }
        @keyframes shoot {
          0% {
            transform: translate(0, 0) rotate(45deg);
            opacity: 1;
          }
          100% {
            transform: translate(-1800px, 1800px) rotate(45deg);
            opacity: 0;
          }
        }
      `}} />

      {/* Vector Deep Space Waves & Nebulas (Stitch 4a845bd2 Premium Vector Representation) */}
      <svg 
        className="absolute bottom-0 left-0 w-full h-[65%] pointer-events-none z-0 mix-blend-screen"
        viewBox="0 0 1440 600" 
        fill="none" 
        xmlns="http://www.w3.org/2000/svg" 
        preserveAspectRatio="none"
      >
        <defs>
          {/* Gradients */}
          <linearGradient id="purpleWaveGrad" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="#1e0b36" stopOpacity="0.85" />
            <stop offset="40%" stopColor="#581c87" stopOpacity="0.45" />
            <stop offset="70%" stopColor="#3b0764" stopOpacity="0.65" />
            <stop offset="100%" stopColor="#0f051d" stopOpacity="0.9" />
          </linearGradient>
          
          <linearGradient id="tealWaveGrad" x1="0%" y1="100%" x2="100%" y2="0%">
            <stop offset="0%" stopColor="#042f2e" stopOpacity="0.75" />
            <stop offset="35%" stopColor="#0d9488" stopOpacity="0.4" />
            <stop offset="65%" stopColor="#0f766e" stopOpacity="0.5" />
            <stop offset="100%" stopColor="#115e59" stopOpacity="0.1" />
          </linearGradient>

          <linearGradient id="indigoWaveGrad" x1="100%" y1="0%" x2="0%" y2="100%">
            <stop offset="0%" stopColor="#1e1b4b" stopOpacity="0.8" />
            <stop offset="50%" stopColor="#4338ca" stopOpacity="0.3" />
            <stop offset="100%" stopColor="#312e81" stopOpacity="0.75" />
          </linearGradient>

          <linearGradient id="cyanWaveGrad" x1="0%" y1="0%" x2="100%" y2="0%">
            <stop offset="10%" stopColor="#083344" stopOpacity="0.7" />
            <stop offset="50%" stopColor="#0891b2" stopOpacity="0.35" />
            <stop offset="85%" stopColor="#0e7490" stopOpacity="0.55" />
            <stop offset="100%" stopColor="#164e63" stopOpacity="0.1" />
          </linearGradient>

          {/* Smooth Soft Glow Filter */}
          <filter id="softGlowFilter" x="-10%" y="-10%" width="120%" height="120%">
            <feGaussianBlur stdDeviation="30" result="blur" />
            <feComposite in="SourceGraphic" in2="blur" operator="over" />
          </filter>

          <filter id="heavyGlowFilter" x="-20%" y="-20%" width="140%" height="140%">
            <feGaussianBlur stdDeviation="55" />
          </filter>
        </defs>

        {/* Dynamic Wave 1: Mor/Purple (Deep Base) */}
        <path 
          d="M0,600 L0,180 C200,90 420,290 700,240 C980,190 1200,420 1440,320 L1440,600 Z" 
          fill="url(#purpleWaveGrad)" 
          filter="url(#softGlowFilter)"
          className="animate-wave-purple"
        />

        {/* Dynamic Wave 2: Teal/Cyan (Vibrant Midground) */}
        <path 
          d="M0,600 L0,390 C180,430 380,310 650,380 C920,450 1180,140 1440,200 L1440,600 Z" 
          fill="url(#tealWaveGrad)" 
          filter="url(#softGlowFilter)"
          className="animate-wave-teal"
        />

        {/* Dynamic Wave 3: Indigo (Atmospheric Depth) */}
        <path 
          d="M0,600 L0,290 C300,180 580,480 880,330 C1150,200 1280,390 1440,240 L1440,600 Z" 
          fill="url(#indigoWaveGrad)" 
          filter="url(#softGlowFilter)"
          className="animate-wave-indigo"
        />

        {/* Dynamic Wave 4: Cyan (Brilliant Foreground Highlight) */}
        <path 
          d="M0,600 L0,480 C250,380 480,520 720,440 C980,350 1220,220 1440,110 L1440,600 Z" 
          fill="url(#cyanWaveGrad)" 
          filter="url(#softGlowFilter)"
          className="animate-wave-cyan"
        />

        {/* Ambient Nebula Light Blurs behind components */}
        <circle cx="200" cy="400" r="280" fill="#a855f7" fillOpacity="0.08" filter="url(#heavyGlowFilter)" />
        <circle cx="1200" cy="250" r="320" fill="#0d9488" fillOpacity="0.06" filter="url(#heavyGlowFilter)" />
        <circle cx="700" cy="450" r="250" fill="#4f46e5" fillOpacity="0.07" filter="url(#heavyGlowFilter)" />
      </svg>

      {/* Cosmic Sparkles & Shooting Stars */}
      <div className="absolute inset-0 pointer-events-none opacity-90 z-0">
        {/* Shimmering Vector Stars */}
        <div className="absolute top-[8%] left-[12%] w-1.5 h-1.5 bg-yellow-100 rounded-full animate-ping duration-[4000ms] blur-[0.2px]" />
        <div className="absolute top-[22%] left-[82%] w-2 h-2 bg-white/95 rounded-full blur-[0.5px] animate-pulse duration-[2500ms]" />
        <div className="absolute top-[78%] left-[15%] w-1.5 h-1.5 bg-yellow-200/90 rounded-full blur-[0.2px] animate-pulse duration-[3500ms]" />
        <div className="absolute top-[14%] left-[64%] w-2 h-2 bg-cyan-200/80 rounded-full blur-[0.4px] animate-pulse duration-[5000ms]" />
        <div className="absolute top-[58%] left-[38%] w-1 h-1 bg-white/90 rounded-full animate-pulse duration-[3000ms]" />
        <div className="absolute top-[84%] left-[68%] w-2 h-2 bg-purple-200/85 rounded-full blur-[0.5px] animate-pulse duration-[6000ms]" />
        <div className="absolute top-[40%] left-[5%] w-1 h-1 bg-white/70 rounded-full animate-pulse duration-[3500ms]" />
        <div className="absolute top-[68%] left-[90%] w-1.5 h-1.5 bg-white/80 rounded-full" />
        <div className="absolute top-[48%] left-[52%] w-1 h-1 bg-yellow-100/90 rounded-full blur-[0.2px] animate-pulse duration-[2800ms]" />

        {/* High-fidelity Vector Shooting Stars */}
        <div className="absolute top-12 left-[8%] w-[180px] h-[1.5px] bg-gradient-to-r from-transparent via-cyan-300/35 to-transparent rotate-[-22deg] pointer-events-none blur-[0.3px]" />
        <div className="absolute top-44 left-[72%] w-[150px] h-[1.5px] bg-gradient-to-r from-transparent via-purple-400/30 to-transparent rotate-[-22deg] pointer-events-none blur-[0.3px]" />
        <div className="absolute top-[62%] left-[28%] w-[130px] h-[1px] bg-gradient-to-r from-transparent via-indigo-400/25 to-transparent rotate-[-22deg] pointer-events-none blur-[0.3px]" />
        
        {/* Dynamic shooting stars engine target container */}
        <div id="compile-stars-container" className="absolute inset-0 pointer-events-none overflow-hidden" />
      </div>

      {/* V2: Global Header zaten layout.tsx'te render ediliyor.
          Bu sayfada sadece içerik üstten başlıyor (has-header layout wrapper'dan geliyor). */}

      <main className="flex-1 flex flex-col max-w-[1650px] w-full mx-auto p-6 gap-6 relative z-10" style={{ height: 'calc(100vh - 52px)', overflow: 'hidden' }}>

        {/* Left Column: Preview */}
        <div className="flex-1 flex flex-col gap-4 min-w-0 h-full">
          {/* Back to Diagram Button - Top Left */}
          <div className="flex items-center justify-between shrink-0">
            <button
              onClick={() => router.push('/canvas')}
              className="flex items-center gap-2 pl-3 pr-3.5 py-1.5 rounded-lg border border-indigo-500/25 bg-indigo-500/5 hover:bg-indigo-500/10 text-indigo-300 hover:text-white transition-all duration-300 shadow-[0_0_12px_rgba(99,102,241,0.08)] cursor-pointer active:scale-95 group font-bold text-xs select-none"
              title="Go Back to Diagram Canvas"
            >
              <ArrowLeft className="w-3.5 h-3.5 group-hover:-translate-x-0.5 transition-transform duration-300 text-indigo-400" />
              <span>Back to Diagram</span>
            </button>
          </div>

          {/* Schema Info and Tabs */}
          <div className="flex items-center justify-between shrink-0 border-b border-zinc-900 pb-2">
            <div className="flex flex-col gap-2">
              <h2 className="text-base font-extrabold text-white tracking-wide">{schema.name || 'Untitled Schema'}</h2>
              <div className="flex gap-4">
                {(['SQL', 'EF', 'ER', 'MOCK', 'DICTIONARY', 'README', 'SANDBOX', 'ADMIN'] as const).map(tab => (
                  <button
                    key={tab}
                    onClick={() => handleTabChange(tab)}
                    className={`text-xs pb-1.5 border-b-2 transition-all font-semibold uppercase tracking-wider ${
                      activeTab === tab 
                        ? 'border-indigo-500 text-indigo-400 font-bold' 
                        : 'border-transparent text-zinc-500 hover:text-zinc-300'
                    }`}
                  >
                    {tab === 'SQL' ? 'DDL Script' : tab === 'EF' ? 'EF Core' : tab === 'ER' ? 'Mermaid ER' : tab === 'MOCK' ? 'Test Data' : tab === 'DICTIONARY' ? 'Data Dictionary' : tab === 'README' ? 'README.md' : tab === 'SANDBOX' ? 'Docker Sandbox' : 'Developer Package'}
                  </button>
                ))}
              </div>
            </div>
            <div className="flex items-center gap-4">
              {activeTab === 'SQL' && <DbTypeSelector selectedDb={dbType} onSelect={(v) => setDbType(v as DbType)} disabled={isLoading} />}
              {activeTab === 'ER' && (
                <div className="flex gap-1 bg-zinc-900/80 p-1 rounded-lg border border-zinc-800 items-center">
                  <select
                    value={diagramType}
                    onChange={(e) => updateDiagram(e.target.value as any)}
                    className="bg-zinc-950 border-0 text-zinc-300 text-xs rounded-md px-2 py-1 focus:outline-none cursor-pointer max-w-[170px]"
                  >
                    <option value="ER">ER Diagram</option>
                    <option value="CLASS">Class Diagram</option>
                    <option value="FLOW">Flowchart</option>
                    <option value="MINDMAP">Mind Map</option>
                    <option value="STATE">State Diagram</option>
                    <option value="SEQUENCE">Sequence Diagram</option>
                    <option value="GANTT">Gantt Chart</option>
                    <option value="PIE">Pie Chart</option>
                    <option value="GIT">Git Branch Diagram</option>
                    <option value="JOURNEY">User Journey</option>
                    <option value="TIMELINE">Timeline</option>
                    <option value="QUADRANT">Quadrant Chart</option>
                    <option value="REQUIREMENT">Requirement Diagram</option>
                  </select>
                  <div className="w-px h-4 bg-zinc-700" />
                  <button
                    id="mermaid-export-svg-btn"
                    onClick={handleExportSvg}
                    disabled={isExportingSvg || !mermaidCode}
                    className="flex items-center gap-1 px-2 py-1 text-xs text-zinc-400 hover:text-white hover:bg-zinc-800 rounded-md transition-colors disabled:opacity-50"
                    title="Download SVG"
                    aria-label="Download SVG"
                  >
                    {isExportingSvg ? (
                      <svg className="w-3.5 h-3.5 animate-spin" viewBox="0 0 24 24" fill="none">
                        <circle className="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" strokeWidth="4" />
                        <path className="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8H4z" />
                      </svg>
                    ) : (
                      <svg className="w-3.5 h-3.5" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                        <path d="M12 15V3m0 12-4-4m4 4 4-4M2 17l.621 2.485A2 2 0 004.561 21h14.878a2 2 0 001.94-1.515L22 17" />
                      </svg>
                    )}
                    <span>SVG</span>
                  </button>
                </div>
              )}
            </div>
          </div>

          {/* Content area — fills remaining vertical space */}
          <div className="flex-1 min-h-0" style={{ display: 'flex', flexDirection: 'column' }}>
            {activeTab === 'ADMIN' && <DownloadHubPanel schema={schema} dbType={dbType} />}
            {activeTab === 'SANDBOX' && <DockerSandboxPanel schema={schema} dbType={dbType} sql={sql} />}
            {activeTab === 'DICTIONARY' && <DataDictionaryPreview schema={schema} projectName={projectName} />}
            {activeTab === 'README' && <ReadmePreview schema={schema} />}
            {activeTab !== 'ADMIN' && activeTab !== 'SANDBOX' && activeTab !== 'DICTIONARY' && activeTab !== 'README' && (
              <div className="flex-1 min-h-0 relative">
                {isLoading && (
                  <div className="absolute inset-0 z-20 bg-zinc-950/50 backdrop-blur-sm flex items-center justify-center rounded-xl border border-zinc-800">
                    <div className="animate-spin rounded-full h-8 w-8 border-b-2 border-indigo-500"></div>
                  </div>
                )}
                {activeTab === 'SQL' && <SqlPreview sql={sql} />}
                {activeTab === 'MOCK' && <SmartSeedPanel schema={schema} dbType={dbType} />}
                {activeTab === 'ER' && (
                  <div className="flex flex-col h-full relative">
                    <div className="mermaid-preview-container flex-1 min-h-0">
                      <MermaidPreview mermaidCode={mermaidCode} />
                    </div>
                  </div>
                )}
                {activeTab === 'EF' && <EfCorePreview schema={schema} />}
              </div>
            )}
          </div>
        </div>

      </main>

      <DockerProgressModal
        isOpen={isDockerModalOpen}
        onClose={() => {
          setIsDockerModalOpen(false);
          if (dockerJob.status === 'done' || dockerJob.status === 'error') {
            dockerJob.reset();
          }
        }}
        status={dockerJob.status}
        logs={dockerJob.logs}
        downloadUrl={dockerJob.downloadUrl}
      />
    </div>
  );
}
