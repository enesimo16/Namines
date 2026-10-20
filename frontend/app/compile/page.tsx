'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  ArrowLeft,
  FileCode2,
  Boxes,
  Layers,
  Blocks,
  GitFork,
  Database,
  BookOpenText,
  FileText,
  Container,
  Download,
  Rocket,
  Sparkles,
  CheckCircle2,
  Search,
  Hash,
  Lock
} from 'lucide-react';
import { useSchemaStore, DbType } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useFeatureStore } from '../../store/useFeatureStore';
import FeatureGuard from '../../components/common/FeatureGuard';
import { decideCanvasBoot } from '../../lib/canvasBoot';
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
import LaunchPanel from '../../components/compile/LaunchPanel';
import { IconButton } from '../../components/compile/PanelKit';
import Select from '../../components/ui/Select';
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

type TabCategory = 'DEPLOY' | 'CODE' | 'DOCS';

type TabId = 'SQL' | 'EF' | 'PRISMA' | 'EJECT' | 'ER' | 'MOCK' | 'DICTIONARY' | 'README' | 'SANDBOX' | 'LAUNCH';

interface TabItem {
  id: TabId;
  label: string;
  badge?: string;
  icon: typeof FileCode2;
  category: TabCategory;
}

const TABS: TabItem[] = [
  { id: 'LAUNCH',     label: 'Launch to Cloud', badge: 'Cloud',   icon: Rocket,       category: 'DEPLOY' },
  { id: 'SANDBOX',    label: 'Docker Sandbox',  badge: 'Test',    icon: Container,    category: 'DEPLOY' },
  { id: 'SQL',        label: 'DDL Script',      badge: '.sql',    icon: FileCode2,    category: 'CODE' },
  { id: 'EF',         label: 'EF Core',         badge: '.cs',     icon: Boxes,        category: 'CODE' },
  { id: 'PRISMA',     label: 'Prisma',          badge: '.prisma', icon: Layers,       category: 'CODE' },
  { id: 'EJECT',      label: 'Export to…',      badge: '19 SDKs', icon: Blocks,       category: 'CODE' },
  { id: 'ER',         label: 'Mermaid ER',      badge: '13 Types',icon: GitFork,      category: 'DOCS' },
  { id: 'DICTIONARY', label: 'Data Dictionary', badge: '.pdf',    icon: BookOpenText, category: 'DOCS' },
  { id: 'README',     label: 'README.md',       badge: 'AI Docs', icon: FileText,     category: 'DOCS' },
  { id: 'MOCK',       label: 'Test Data',       badge: 'Mock',    icon: Database,     category: 'DOCS' },
];

const CATEGORIES: { id: TabCategory; label: string }[] = [
  { id: 'DEPLOY', label: 'Deployment & Runtime' },
  { id: 'CODE',   label: 'Code & Schemas' },
  { id: 'DOCS',   label: 'Documentation & Data' },
];

const TAB_FEATURE_KEYS: Record<TabId, string> = {
  LAUNCH: 'compile_launch',
  SANDBOX: 'compile_sandbox',
  SQL: 'compile_sql',
  EF: 'compile_ef',
  PRISMA: 'compile_prisma',
  EJECT: 'compile_eject',
  ER: 'compile_mermaid',
  DICTIONARY: 'compile_dictionary',
  README: 'compile_readme',
  MOCK: 'compile_mock',
};

export default function CompilePage() {
  const router = useRouter();
  const { schema, dbType, setDbType, projectName } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const { isFeatureEnabled, getFeatureState } = useFeatureStore();

  const [sql, setSql] = useState('');
  const [mermaidCode, setMermaidCode] = useState('');
  const [diagramType, setDiagramType] = useState<
    'ER' | 'CLASS' | 'FLOW' | 'MINDMAP' | 'STATE' | 'SEQUENCE' | 'GANTT' | 'PIE' | 'GIT' | 'JOURNEY' | 'TIMELINE' | 'QUADRANT' | 'REQUIREMENT'
  >('ER');
  const [activeTab, setActiveTab] = useState<TabId>('SQL');
  const [isLoading, setIsLoading] = useState(false);
  const [isExportingSvg, setIsExportingSvg] = useState(false);
  const [focusTable, setFocusTable] = useState<string>('');
  const [tableFilterQuery, setTableFilterQuery] = useState('');

  // Hydration & boot restoration
  const hasHydrated = useProjectHistoryStore(s => s.hasHydrated);
  const projects = useProjectHistoryStore(s => s.projects);
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  useEffect(() => {
    if (schema) return;
    const decision = decideCanvasBoot({
      hasSchema: false, joinedSharedRoom: false, hasHydrated, activeProjectId, projects,
    });
    if (decision.action === 'wait') return;
    if (decision.action === 'restore') {
      loadFromSchema(decision.schema, decision.nodePositions, true);
      return;
    }
    router.push('/new');
  }, [schema, hasHydrated, activeProjectId, projects, loadFromSchema, router]);

  // Compile SQL & generate Mermaid
  useEffect(() => {
    if (!schema) return;

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

  // Ground PostgreSQL DDL preparation for Launch
  const [launchDdl, setLaunchDdl] = useState('');
  const [launchDdlStatus, setLaunchDdlStatus] = useState<'loading' | 'ready' | 'error'>('loading');
  const [launchDdlAttempt, setLaunchDdlAttempt] = useState(0);

  useEffect(() => {
    if (!schema) return;
    let ignore = false;
    setLaunchDdlStatus('loading');
    schemaService.compileSql(schema, 'PostgreSQL').then(generated => {
      if (ignore) return;
      if (!generated.trim()) {
        setLaunchDdl('');
        setLaunchDdlStatus('error');
        return;
      }
      setLaunchDdl(generated);
      setLaunchDdlStatus('ready');
    }).catch(() => {
      if (ignore) return;
      setLaunchDdl('');
      setLaunchDdlStatus('error');
    });
    return () => { ignore = true; };
  }, [schema, launchDdlAttempt]);

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
      showToast('Mermaid SVG downloaded successfully!', 'success');
    } catch (err) {
      console.error('SVG export error:', err);
      showToast('Failed to export diagram SVG.', 'error');
    } finally {
      setIsExportingSvg(false);
    }
  };

  const handleTableClick = (tableName: string) => {
    setFocusTable(tableName);
    if (activeTab !== 'SQL') {
      setActiveTab('SQL');
    }
  };

  if (!schema) return null;

  const activeMeta = TABS.find(t => t.id === activeTab)!;
  const totalColumns = schema.tables.reduce((sum, t) => sum + t.columns.length, 0);

  const filteredTables = schema.tables.filter(t =>
    t.name.toLowerCase().includes(tableFilterQuery.toLowerCase())
  );

  return (
    <div className="h-[calc(100vh-56px)] bg-surface-900 text-content-primary flex flex-col lg:flex-row font-sans overflow-hidden">
      
      {/* ── Left Sidebar Navigation (Shopify Polaris Grouped Style) ─────────── */}
      <aside className="shrink-0 bg-surface-800 border-b lg:border-b-0 lg:border-r border-line-strong flex flex-col lg:w-56 shadow-xs select-none">
        
        {/* Back to Canvas Button & Schema Title */}
        <div className="hidden lg:block p-3 border-b border-line">
          <button
            onClick={() => router.push('/canvas')}
            className="tap-44 flex items-center gap-1.5 w-full px-2.5 py-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700/60 transition-colors cursor-pointer text-xs font-medium mb-2.5"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            <span>Back to Canvas</span>
          </button>
          
          <div className="flex items-center justify-between gap-1.5">
            <h1 className="text-xs font-bold uppercase tracking-wider text-content-primary truncate leading-tight" title={schema.name}>
              {schema.name || 'Untitled Schema'}
            </h1>
          </div>
        </div>

        {/* Mobile Header Bar */}
        <div className="flex lg:hidden items-center justify-between gap-2 h-11 px-3 border-b border-line shrink-0">
          <button
            onClick={() => router.push('/canvas')}
            aria-label="Back to Canvas"
            className="tap-44 flex items-center gap-1 text-content-muted hover:text-content-primary text-xs shrink-0"
          >
            <ArrowLeft className="w-3.5 h-3.5" />
            <span>Canvas</span>
          </button>
          <span className="text-xs font-bold text-content-primary truncate min-w-0">
            {schema.name || 'Untitled Schema'}
          </span>
          <div className="w-32 shrink-0">
            <Select
              value={dbType}
              onChange={(e) => setDbType(e.target.value as DbType)}
              disabled={isLoading}
              aria-label="Target database engine"
              className="text-[11px] font-mono"
            >
              <option value="MSSQL">SQL Server</option>
              <option value="PostgreSQL">PostgreSQL</option>
              <option value="MySQL">MySQL</option>
              <option value="SQLite">SQLite</option>
              <option value="Oracle">Oracle</option>
              <option value="MariaDB">MariaDB</option>
            </Select>
          </div>
        </div>

        {/* Navigation Tabs List */}
        <nav className="flex lg:flex-1 lg:flex-col overflow-x-auto lg:overflow-x-visible lg:overflow-y-auto gap-1 lg:gap-0 lg:space-y-4 p-2.5 scrollbar-none">
          
          {/* Mobile Horizontal Pill Strip */}
          <div className="flex lg:hidden gap-1">
            {TABS.filter(t => {
              const key = TAB_FEATURE_KEYS[t.id];
              const state = getFeatureState(key);
              if (!isFeatureEnabled(key) && state.mode === 'Hidden') return false;
              return true;
            }).map(tab => {
              const Icon = tab.icon;
              const isActive = activeTab === tab.id;
              const isEnabled = isFeatureEnabled(TAB_FEATURE_KEYS[tab.id]);
              return (
                <button
                  key={tab.id}
                  onClick={() => setActiveTab(tab.id)}
                  className={`tap-44 relative shrink-0 flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] text-xs transition-all cursor-pointer ${
                    isActive
                      ? 'bg-surface-700 text-content-primary font-bold border border-line-strong shadow-xs'
                      : 'text-content-muted hover:text-content-primary hover:bg-surface-700/50 font-medium'
                  }`}
                >
                  <Icon className={`w-3.5 h-3.5 shrink-0 ${isActive ? 'text-namines-teal-vibrant' : 'text-content-muted'}`} />
                  <span>{tab.label}</span>
                  {!isEnabled && <Lock className="w-3 h-3 text-warning-text shrink-0" />}
                </button>
              );
            })}
          </div>

          {/* Desktop Categorized Navigation */}
          <div className="hidden lg:flex flex-col space-y-4">
            {CATEGORIES.map(category => {
              const categoryTabs = TABS.filter(t => {
                if (t.category !== category.id) return false;
                const key = TAB_FEATURE_KEYS[t.id];
                const state = getFeatureState(key);
                if (!isFeatureEnabled(key) && state.mode === 'Hidden') return false;
                return true;
              });

              if (categoryTabs.length === 0) return null;

              return (
                <div key={category.id} className="space-y-1">
                  <div className="px-2.5 py-0.5 text-micro font-bold text-content-muted uppercase tracking-wider select-none">
                    {category.label}
                  </div>
                  <div className="space-y-0.5">
                    {categoryTabs.map(tab => {
                      const Icon = tab.icon;
                      const isActive = activeTab === tab.id;
                      const isEnabled = isFeatureEnabled(TAB_FEATURE_KEYS[tab.id]);
                      return (
                        <button
                          key={tab.id}
                          id={`compile-tab-${tab.id}`}
                          onClick={() => setActiveTab(tab.id)}
                          className={`tap-44 relative w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-control)] text-xs transition-all cursor-pointer ${
                            isActive
                              ? 'bg-surface-700/90 text-content-primary font-semibold border border-line-strong shadow-xs'
                              : 'text-content-muted hover:text-content-primary hover:bg-surface-700/40 font-medium'
                          }`}
                        >
                          <div className="flex items-center gap-2 min-w-0">
                            {isActive && (
                              <span className="absolute left-0 top-1.5 bottom-1.5 w-0.5 rounded-full bg-namines-teal-vibrant" />
                            )}
                            <Icon className={`w-3.5 h-3.5 shrink-0 ${isActive ? 'text-namines-teal-vibrant' : 'text-content-muted'}`} />
                            <span className="truncate whitespace-nowrap">{tab.label}</span>
                            {!isEnabled && <Lock className="w-3 h-3 text-warning-text shrink-0" />}
                          </div>

                          {tab.badge && (
                            <span className="text-[10px] font-mono text-content-muted/70 bg-surface-800/80 px-1.5 py-0.5 rounded-[var(--radius-control)] border border-line/60 shrink-0 whitespace-nowrap ml-1.5">
                              {tab.badge}
                            </span>
                          )}
                        </button>
                      );
                    })}
                  </div>
                </div>
              );
            })}
          </div>
        </nav>

        {/* Sidebar Footer: Engine Switcher */}
        <div id="compile-engine-selector" className="hidden lg:block p-2.5 border-t border-line bg-surface-800/60">
          <div className="text-micro font-bold text-content-muted uppercase tracking-wider mb-1 px-1">
            Target Engine
          </div>
          <DbTypeSelector selectedDb={dbType} onSelect={(v) => setDbType(v as DbType)} disabled={isLoading} />
        </div>
      </aside>

      {/* ── Center Content Column (The Middle Showcase) ─────────────────────── */}
      <div className="flex-1 min-w-0 min-h-0 flex flex-col">
        
        {/* Top Header Bar (Shopify Polaris Console Header) */}
        <div className="flex flex-wrap items-center justify-between gap-x-3 gap-y-2 shrink-0 min-h-12 px-4 py-2 bg-surface-800/95 border-b border-line-strong select-none">
          
          {/* Breadcrumb & Tab Name */}
          <div className="flex items-center gap-2.5 min-w-0">
            <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 border border-line-strong flex items-center justify-center text-content-primary shrink-0 shadow-xs">
              <activeMeta.icon className="w-3.5 h-3.5 text-namines-teal-vibrant" />
            </div>
            <div className="min-w-0 flex items-center gap-2">
              <span className="text-xs font-bold text-content-primary whitespace-nowrap">{activeMeta.label}</span>
              <span className="text-[10px] font-mono text-content-muted bg-surface-700/80 px-2 py-0.5 rounded-full border border-line truncate max-w-[140px]">
                {schema.name || 'Untitled Schema'}
              </span>
            </div>
          </div>

          {/* Right Header Controls (Diagram switcher, etc.) */}
          <div className="flex items-center gap-2 shrink-0">
            {activeTab === 'ER' && (
              <div id="compile-diagram-controls" className="flex items-center gap-2">
                <div className="w-48 shrink-0">
                  <Select
                    value={diagramType}
                    onChange={(e) => updateDiagram(e.target.value as typeof diagramType)}
                    aria-label="Diagram type"
                    className="text-xs font-medium"
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
                  </Select>
                </div>
                <IconButton
                  icon={Download}
                  label="Download diagram as SVG"
                  onClick={handleExportSvg}
                  busy={isExportingSvg}
                  disabled={!mermaidCode}
                />
              </div>
            )}
          </div>
        </div>

        {/* ── Main Content Area (Wrapped in Spacious Viewport) ───────────────── */}
        <div id="compile-main-viewport" className="flex-1 min-h-0 p-3 lg:p-4 bg-surface-900 flex flex-col overflow-hidden relative">
          
          {isLoading && (
            <div className="absolute inset-0 z-30 bg-surface-900/60 backdrop-blur-sm flex items-center justify-center">
              <div className="flex flex-col items-center gap-2">
                <div className="animate-spin rounded-full h-7 w-7 border-2 border-surface-600 border-t-namines-teal-vibrant" />
                <span className="text-xs font-mono text-content-secondary">Compiling Schema…</span>
              </div>
            </div>
          )}

          <FeatureGuard featureKey={TAB_FEATURE_KEYS[activeTab]}>
            {activeTab === 'SQL' && <SqlPreview sql={sql} focusTable={focusTable} />}
            {activeTab === 'EF' && <EfCorePreview schema={schema} />}
            {activeTab === 'PRISMA' && <PrismaPreview schema={schema} dbType={dbType} />}
            {activeTab === 'EJECT' && <EjectPanel schema={schema} dbType={dbType} />}
            {activeTab === 'ER' && (
              <div className="w-full h-full relative">
                <MermaidPreview mermaidCode={mermaidCode} />
              </div>
            )}
            {activeTab === 'DICTIONARY' && <DataDictionaryPreview schema={schema} projectName={projectName} />}
            {activeTab === 'README' && <ReadmePreview schema={schema} />}
            {activeTab === 'MOCK' && <SmartSeedPanel schema={schema} dbType={dbType} />}
            {activeTab === 'SANDBOX' && <DockerSandboxPanel schema={schema} dbType={dbType} sql={sql} />}
            {activeTab === 'LAUNCH' && (
              <LaunchPanel
                projectId={activeProjectId}
                projectName={projectName}
                ddlScript={launchDdl}
                ddlStatus={launchDdlStatus}
                onRetryDdl={() => setLaunchDdlAttempt(n => n + 1)}
                schema={schema}
              />
            )}
          </FeatureGuard>
        </div>
      </div>

      {/* ── Right Schema Inspector Rail (Shopify Polaris Metric & Inventory) ─── */}
      <aside id="compile-schema-inspector" className="w-60 shrink-0 bg-surface-800/95 border-l border-line-strong hidden xl:flex flex-col overflow-hidden shadow-xs select-none">
        
        {/* Rail Header */}
        <div className="px-3.5 py-3 border-b border-line flex items-center justify-between">
          <p className="text-micro font-bold text-content-muted uppercase tracking-wider">Schema Inspector</p>
          <span className="text-[10px] font-mono font-bold text-content-primary bg-surface-700 border border-line-strong px-2 py-0.5 rounded-[var(--radius-control)] shadow-xs">
            {dbType}
          </span>
        </div>

        {/* Metric Tiles Grid */}
        <div className="p-3 border-b border-line grid grid-cols-3 gap-2 bg-surface-700/20">
          <div className="bg-surface-800 p-2 rounded-[var(--radius-control)] border border-line text-center">
            <p className="text-sm font-bold text-content-primary font-mono leading-none">{schema.tables.length}</p>
            <p className="text-micro text-content-muted mt-1 uppercase tracking-wide">Tables</p>
          </div>
          <div className="bg-surface-800 p-2 rounded-[var(--radius-control)] border border-line text-center">
            <p className="text-sm font-bold text-content-primary font-mono leading-none">{totalColumns}</p>
            <p className="text-micro text-content-muted mt-1 uppercase tracking-wide">Columns</p>
          </div>
          <div className="bg-surface-800 p-2 rounded-[var(--radius-control)] border border-line text-center">
            <p className="text-sm font-bold text-content-primary font-mono leading-none">{schema.relations.length}</p>
            <p className="text-micro text-content-muted mt-1 uppercase tracking-wide">Relations</p>
          </div>
        </div>

        {/* Search & Filter Tables */}
        <div className="p-2.5 border-b border-line">
          <div className="relative w-full">
            <Search className="w-3 h-3 text-content-muted absolute left-2 top-1/2 -translate-y-1/2 pointer-events-none" />
            <input
              type="text"
              value={tableFilterQuery}
              onChange={e => setTableFilterQuery(e.target.value)}
              placeholder="Filter tables..."
              className="w-full bg-surface-700/60 border border-line rounded-[var(--radius-control)] h-7 pl-6 pr-2 text-[11px] text-content-primary placeholder:text-content-muted/60 focus:outline-none focus:border-focus-ring"
            />
          </div>
        </div>

        {/* Table Inventory List with Jump Capability */}
        <div className="flex-1 min-h-0 flex flex-col overflow-hidden p-2">
          <div className="flex items-center justify-between px-2 py-1">
            <p className="text-micro font-bold text-content-muted uppercase tracking-wider">Table Directory</p>
            <span className="text-[10px] font-mono text-content-muted">{filteredTables.length}</span>
          </div>
          
          <div className="flex-1 min-h-0 overflow-y-auto space-y-1 pr-1">
            {filteredTables.map(t => (
              <button
                key={t.id}
                onClick={() => handleTableClick(t.name)}
                className={`tap-44 w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-control)] text-xs transition-all text-left cursor-pointer ${
                  focusTable === t.name
                    ? 'bg-surface-700 text-namines-teal-vibrant border border-line-strong font-semibold'
                    : 'bg-surface-700/30 hover:bg-surface-700/70 text-content-secondary hover:text-content-primary border border-transparent hover:border-line'
                }`}
                title={`Jump to ${t.name}`}
              >
                <div className="flex items-center gap-1.5 min-w-0">
                  <Hash className="w-3 h-3 text-content-muted shrink-0" />
                  <span className="font-mono truncate">{t.name}</span>
                </div>
                <span className="text-content-muted font-mono text-[10px] shrink-0 ml-1.5 px-1.5 py-0.2 rounded-full bg-surface-700 border border-line">
                  {t.columns.length}
                </span>
              </button>
            ))}
          </div>
        </div>
      </aside>
    </div>
  );
}
