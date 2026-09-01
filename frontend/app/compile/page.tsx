'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowLeft, FileCode2, Boxes, Layers, Blocks, GitFork, Database, BookOpenText, FileText, Container, Download, PanelsTopLeft, ExternalLink } from 'lucide-react';
import { useSchemaStore, DbType } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { schemaService } from '../../services/api';

import DbTypeSelector from '../../components/compile/DbTypeSelector';
import SqlPreview from '../../components/compile/SqlPreview';
import DataDictionaryPreview from '../../components/compile/DataDictionaryPreview';
import ReadmePreview from '../../components/compile/ReadmePreview';
import MermaidPreview from '../../components/compile/MermaidPreview';
import DockerSandboxPanel from '../../components/compile/DockerSandboxPanel';
import SmartSeedPanel from '../../components/compile/SmartSeedPanel';
import EfCorePreview from '../../components/compile/EfCorePreview';
import PrismaPreview from '../../components/compile/PrismaPreview';
import EjectPanel from '../../components/compile/EjectPanel';
import { IconButton } from '../../components/compile/PanelKit';
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

type TabId = 'SQL' | 'EF' | 'PRISMA' | 'EJECT' | 'ER' | 'MOCK' | 'DICTIONARY' | 'README' | 'SANDBOX';

const TABS: { id: TabId; label: string; icon: typeof FileCode2 }[] = [
  { id: 'SQL',        label: 'DDL Script',       icon: FileCode2 },
  { id: 'EF',         label: 'EF Core',          icon: Boxes },
  { id: 'PRISMA',     label: 'Prisma',           icon: Layers },
  { id: 'EJECT',      label: 'Export to…',       icon: Blocks },
  { id: 'ER',         label: 'Mermaid ER',       icon: GitFork },
  { id: 'MOCK',       label: 'Test Data',        icon: Database },
  { id: 'DICTIONARY', label: 'Data Dictionary',  icon: BookOpenText },
  { id: 'README',     label: 'README.md',        icon: FileText },
  { id: 'SANDBOX',    label: 'Docker Sandbox',   icon: Container },
];

/**
 * Compile ekranı — üç sütunlu konsol düzeni: sol dar ikon+etiket navigasyonu,
 * ortada içerik, sağda daraltılabilir "Schema Info" paneli (tablo/kolon/ilişki
 * sayımı + motor rozeti — sekmeler arasında taşınan bağlam tek yerde, her panel
 * onu tekrar çizmiyor). Aktif sekme artık paletin "minimal lacivert" aksan
 * ailesini (--color-accent-subtle/--color-accent-text) kullanıyor — önceki
 * sürüm her yerde aynı nötr `bg-white/[0.1]` overlay'ini kullanıyordu, bu yüzden
 * "aktif" durumu diğer hover durumlarından ayırt edilmiyordu. Dikey nav +
 * yoğunlaştırılmış boşluklar sayesinde 8 sekmenin hepsi de scroll olmadan
 * tek ekrana sığıyor (1024px yükseklikte bile).
 */
export default function CompilePage() {
  const router = useRouter();
  const { schema, dbType, setDbType, projectName } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const { getActiveSandbox } = useProjectHistoryStore();

  const [sql, setSql] = useState('');
  const [mermaidCode, setMermaidCode] = useState('');
  const [diagramType, setDiagramType] = useState<
    'ER' | 'CLASS' | 'FLOW' | 'MINDMAP' | 'STATE' | 'SEQUENCE' | 'GANTT' | 'PIE' | 'GIT' | 'JOURNEY' | 'TIMELINE' | 'QUADRANT' | 'REQUIREMENT'
  >('ER');
  const [activeTab, setActiveTab] = useState<TabId>('SQL');
  const [isLoading, setIsLoading] = useState(false);
  const [isExportingSvg, setIsExportingSvg] = useState(false);

  useEffect(() => {
    if (!schema) {
      router.push('/');
      return;
    }

    let ignore = false;
    const fetchSql = async () => {
      setIsLoading(true);
      try {
        const generatedSql = await schemaService.compileSql(schema, dbType);
        if (ignore) return;
        setSql(generatedSql);

        const mCode = await schemaService.generateMermaid(schema);
        if (ignore) return;
        setMermaidCode(mCode);
      } catch (error) {
        if (ignore) return;
        console.error("Failed to compile SQL", error);
        setSql('-- Error generating SQL');
      } finally {
        if (!ignore) setIsLoading(false);
      }
    };

    fetchSql();
    return () => { ignore = true; };
  }, [schema, dbType, router]);

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

  const activeMeta = TABS.find(t => t.id === activeTab)!;

  const totalColumns = schema.tables.reduce((sum, t) => sum + t.columns.length, 0);

  return (
    <div className="h-[calc(100vh-56px)] bg-surface-900 text-content-primary flex flex-col lg:flex-row font-sans overflow-hidden">

      {/* Sol navigasyon — MASAÜSTÜNDE (lg+) dikey sidebar, DAR EKRANDA yatay
          kaydırılan sekme şeridi (bkz. ToolbarPanel.tsx'in aynı deseni —
          proje zaten mobil araç çubuklarını böyle çözüyor).

          <b>REGRESYON.</b> Bu kabuk hiç responsive DEĞİLDİ: sidebar sabit
          `w-48` idi, 390px genişlikte viewport'un YARISINI yiyip içerik
          sütununu tek satırlık koda sığmayan bir şeride sıkıştırıyordu
          ("approve'dan sonrası bütün panellerde div sorunu" geri bildirimi —
          canlı ölçtüm: sidebar 390px'in ~200px'ini kaplıyordu). */}
      <aside className="shrink-0 bg-surface-800 border-b lg:border-b-0 lg:border-r border-surface-500 flex flex-col lg:w-48">
        {/* Masaüstü: "Geri dön" ayrı satır + proje adı bloğu. Dar ekranda bu
            ikisi TEK kompakt satıra iner — sekme şeridi zaten kendi satırını
            alacak, iki ayrı blok dar ekranda gereksiz dikey yer yerdi. */}
        <div className="hidden lg:block p-2.5">
          <button
            onClick={() => router.push('/canvas')}
            className="flex items-center gap-1.5 w-full px-2 py-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-white/[0.06] transition-colors cursor-pointer text-[11px] font-medium"
          >
            <ArrowLeft className="w-3 h-3" />
            <span>Back to Diagram</span>
          </button>
        </div>
        {/* "{N} tables · {M} rel." satırı buradan silindi — sağdaki "Schema
            Info" rayında (aşağıda, TABLES/COLUMNS/RELATIONS kartları) zaten
            var, tekrar tekrarıydı ("zaten sağda var" geri bildirimi). */}
        <div className="hidden lg:block px-3 pb-2.5 border-b border-surface-500">
          <h1 className="text-[13px] font-bold text-content-primary truncate leading-tight" title={schema.name}>{schema.name || 'Untitled Schema'}</h1>
        </div>

        <div className="flex lg:hidden items-center gap-2 h-10 px-2 border-b border-surface-500 shrink-0">
          <button
            onClick={() => router.push('/canvas')}
            aria-label="Back to Diagram"
            title="Back to Diagram"
            className="tap-44 shrink-0 flex items-center justify-center w-7 h-7 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-white/[0.06] transition-colors cursor-pointer"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
          </button>
          <h1 className="text-[12px] font-bold text-content-primary truncate min-w-0 flex-1" title={schema.name}>{schema.name || 'Untitled Schema'}</h1>
        </div>

        <nav className="flex lg:flex-1 lg:flex-col overflow-x-auto lg:overflow-x-visible lg:overflow-y-auto gap-1 lg:gap-0 lg:space-y-0.5 p-1.5 scrollbar-none">
          {TABS.map(tab => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => setActiveTab(tab.id)}
                className={`relative shrink-0 lg:shrink lg:w-full flex items-center gap-2 pl-2.5 pr-2.5 lg:pr-2 py-1.5 rounded-[var(--radius-control)] text-[11px] font-medium whitespace-nowrap transition-colors cursor-pointer ${
                  isActive
                    ? 'bg-accent-subtle text-accent-text'
                    : 'text-content-muted hover:text-content-secondary hover:bg-white/[0.04]'
                }`}
              >
                {/* Masaüstünde SOL kenar çubuğu, dar ekranın yatay şeridinde ALT
                    kenar çubuğu — dikey/yatay akışa göre aynı "aktif" dilini konuşur. */}
                {isActive && <span className="hidden lg:block absolute left-0 top-1 bottom-1 w-0.5 rounded-full bg-accent-hover" />}
                {isActive && <span className="lg:hidden absolute left-1.5 right-1.5 bottom-0 h-0.5 rounded-full bg-accent-hover" />}
                <Icon className="w-3.5 h-3.5 shrink-0" />
                <span className="lg:truncate">{tab.label}</span>
              </button>
            );
          })}

          {/* Namines Desk — AYRI mikroservis (services/desk), ayri port.
              Sekme DEGIL bir BAGLANTI: bu sayfanin icinde render edilemez,
              kendi uygulamasi. "Developer Package" sekmesi (indirilebilir
              Streamlit/Next.js paketleri) bunun yerine kaldirildi:
              indirilen bir panel yerine barindirilan bir panel veriyoruz. */}
          <a
            href={process.env.NEXT_PUBLIC_DESK_URL ?? 'http://localhost:3200'}
            target="_blank"
            rel="noopener noreferrer"
            className="relative shrink-0 lg:shrink lg:w-full flex items-center gap-2 pl-2.5 pr-2.5 lg:pr-2 py-1.5 mt-1 lg:mt-2 rounded-[var(--radius-control)] text-[11px] font-medium whitespace-nowrap transition-colors cursor-pointer text-content-muted hover:text-content-secondary hover:bg-white/[0.04] border border-content-primary/10"
            title="Namines Desk — veritabaniniz icin barindirilan CRUD arayuzu (ayri uygulama)"
          >
            <PanelsTopLeft className="w-3.5 h-3.5 shrink-0" />
            <span className="lg:truncate">Namines Desk</span>
            <span className="text-micro font-bold uppercase tracking-wider text-accent-text bg-accent-subtle px-1.5 py-0.5 rounded-full shrink-0">beta</span>
            <ExternalLink className="w-3 h-3 shrink-0 ml-auto opacity-60" />
          </a>
        </nav>
      </aside>

      {/* Orta içerik sütunu */}
      <div className="flex-1 min-w-0 min-h-0 flex flex-col">

        {/* Ortak üst şerit — panel başına tekrar başlık çizmek yerine tek yerde.
            `flex-wrap` + `min-h`: DDL/ER kontrolleri (DB seçici, diyagram tipi +
            indir butonu) dar ekranda sığmazsa ikinci satıra sarsın. */}
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-1.5 shrink-0 min-h-10 px-3 lg:px-4 py-1.5 bg-surface-800 border-b border-surface-500">
          <div className="flex items-center gap-1.5 text-[11px] font-semibold text-accent-text">
            <activeMeta.icon className="w-3.5 h-3.5" />
            <span>{activeMeta.label}</span>
          </div>

          <div className="flex items-center gap-3">
            {activeTab === 'SQL' && <DbTypeSelector selectedDb={dbType} onSelect={(v) => setDbType(v as DbType)} disabled={isLoading} />}
            {activeTab === 'ER' && (
              <>
                <select
                  value={diagramType}
                  onChange={(e) => updateDiagram(e.target.value as any)}
                  aria-label="Diagram type"
                  /* h-9: yanındaki `IconButton` de h-9 — ikisi 32/36 olarak
                     ayrı yüksekliklerdeyken aynı satırda gözle görülür şekilde
                     kayıyorlardı (Mermaid ER şeridi ekran görüntüsü). */
                  className="bg-surface-600 border border-surface-500 rounded-[var(--radius-control)] h-9 pl-2.5 pr-6 text-[11px] text-content-secondary focus:outline-none focus-visible:ring-1 focus-visible:ring-[var(--color-focus-ring)] cursor-pointer"
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
                <IconButton icon={Download} label="Download diagram as SVG" onClick={handleExportSvg} busy={isExportingSvg} disabled={!mermaidCode} />
              </>
            )}
          </div>
        </div>

        {/* Content area */}
        <div className="flex-1 min-h-0 p-3 bg-surface-900" style={{ display: 'flex', flexDirection: 'column' }}>
          {activeTab === 'SANDBOX' && <DockerSandboxPanel schema={schema} dbType={dbType} sql={sql} />}
          {activeTab === 'DICTIONARY' && <DataDictionaryPreview schema={schema} projectName={projectName} />}
          {activeTab === 'README' && <ReadmePreview schema={schema} />}
          {activeTab !== 'SANDBOX' && activeTab !== 'DICTIONARY' && activeTab !== 'README' && (
            <div className="flex-1 min-h-0 relative">
              {isLoading && (
                <div className="absolute inset-0 z-20 bg-surface-900/60 backdrop-blur-sm flex items-center justify-center rounded-[var(--radius-card)]">
                  <div className="animate-spin rounded-full h-6 w-6 border-2 border-surface-500 border-t-content-primary" />
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
              {activeTab === 'PRISMA' && <PrismaPreview schema={schema} dbType={dbType} />}
              {activeTab === 'EJECT' && <EjectPanel schema={schema} dbType={dbType} />}
            </div>
          )}
        </div>
      </div>

      {/* Sağ bilgi rayı — sekmeler arasında taşınan şema bağlamı tek yerde,
          her panel bunu tekrar çizmesin diye ayrı bir sütuna alındı. */}
      <aside className="w-56 shrink-0 bg-surface-800 border-l border-surface-500 hidden xl:flex flex-col overflow-hidden">
        <div className="px-3.5 py-2.5 border-b border-surface-500">
          <p className="text-[10px] font-bold text-content-muted uppercase tracking-wider">Schema Info</p>
        </div>
        <div className="px-3.5 py-3 border-b border-surface-500 grid grid-cols-3 gap-2">
          <div>
            <p className="text-base font-bold text-content-primary font-mono leading-none">{schema.tables.length}</p>
            <p className="text-micro text-content-muted mt-1 uppercase tracking-wide">Tables</p>
          </div>
          <div>
            <p className="text-base font-bold text-content-primary font-mono leading-none">{totalColumns}</p>
            <p className="text-micro text-content-muted mt-1 uppercase tracking-wide">Columns</p>
          </div>
          <div>
            <p className="text-base font-bold text-content-primary font-mono leading-none">{schema.relations.length}</p>
            <p className="text-micro text-content-muted mt-1 uppercase tracking-wide">Relations</p>
          </div>
        </div>
        <div className="px-3.5 py-2.5 border-b border-surface-500 flex items-center justify-between">
          <span className="text-[10px] text-content-muted font-medium">Target Engine</span>
          <span className="text-[10px] font-mono font-semibold text-accent-text bg-accent-subtle px-2 py-0.5 rounded-[var(--radius-control)]">{dbType}</span>
        </div>
        <div className="flex-1 min-h-0 overflow-y-auto px-2 py-2">
          <p className="text-micro font-bold text-content-muted uppercase tracking-wider px-1.5 mb-1.5">Tables</p>
          <div className="space-y-0.5">
            {schema.tables.map(t => (
              <div key={t.id} className="flex items-center justify-between px-1.5 py-1 rounded-[var(--radius-control)] text-[11px] hover:bg-white/[0.04]">
                <span className="text-content-secondary font-mono truncate">{t.name}</span>
                <span className="text-content-muted font-mono text-[10px] shrink-0 ml-2">{t.columns.length}</span>
              </div>
            ))}
          </div>
        </div>
      </aside>
    </div>
  );
}
