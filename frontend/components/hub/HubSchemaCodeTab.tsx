'use client';

import React, { useState, useMemo, useEffect, useRef, useCallback } from 'react';
import { createPortal } from 'react-dom';
import {
  ReactFlow,
  Background,
  BackgroundVariant,
  useReactFlow,
  ReactFlowProvider,
  Node,
  Edge,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import {
  GitBranch,
  Layers,
  Search,
  FileCode,
  FileText,
  Copy,
  Check,
  CodeXml,
  Maximize2,
  Minimize2,
  X,
  Eye,
  Code2,
  ChevronDown,
  Plus,
  Workflow,
  Sparkles,
  Table as TableIcon,
  Download,
  Blocks,
  Boxes,
  Terminal,
} from 'lucide-react';
import type { HubRepoDetail } from '../../types/hub';
import type { DatabaseSchema, SchemaTable } from '../../types/schema';
import { HubTableNode } from './HubTableNode';
import { HubFlowNode } from './HubFlowNode';
import { schemaService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { fetchAutomationRules } from '../../lib/automationApi';
import type { AutomationRule } from '../../store/useAutomationStore';
import { useAuthStore } from '../../store/useAuthStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import MermaidPreview from '../compile/MermaidPreview';
import {
  MERMAID_DIAGRAM_TYPES,
  generateMermaidByType,
  BUILD_CODE_TARGETS,
  generateBuildArtifact,
} from '../../lib/buildArtifactGenerators';

interface HubSchemaCodeTabProps {
  repo: HubRepoDetail;
}

type GeneratedFileType =
  | 'DIAGRAM'
  | 'schema.sql'
  | 'DbContext.cs'
  | 'schema.prisma'
  | 'openapi.json'
  | 'README.md'
  | string;

const nodeTypes = {
  hubTable: HubTableNode,
  hubFlowNode: HubFlowNode,
};

function extractModules(tables: SchemaTable[]): Map<string, SchemaTable[]> {
  const map = new Map<string, SchemaTable[]>();
  for (const table of tables) {
    const parts = table.name.split('_');
    const moduleName = parts.length > 1 && parts[0].length >= 3 ? parts[0] : 'core';
    const list = map.get(moduleName) ?? [];
    list.push(table);
    map.set(moduleName, list);
  }
  return map;
}

function generateMermaidErDiagram(schema: DatabaseSchema): string {
  const lines: string[] = ['erDiagram'];
  (schema.tables || []).forEach((t) => {
    lines.push(`    ${t.name} {`);
    (t.columns || []).forEach((c) => {
      const typeStr = c.type.replace(/[^a-zA-Z0-9_]/g, '_');
      const pkStr = c.isPK ? 'PK' : '';
      lines.push(`        ${typeStr} ${c.name} ${pkStr}`.trimEnd());
    });
    lines.push('    }');
  });
  (schema.relations || []).forEach((r) => {
    const src = schema.tables.find((t) => t.id === r.sourceTableId)?.name;
    const tgt = schema.tables.find((t) => t.id === r.targetTableId)?.name;
    if (src && tgt) {
      lines.push(`    ${src} ||--o{ ${tgt} : "references"`);
    }
  });
  return lines.join('\n');
}

function InnerDiagramCanvas({
  nodes,
  edges,
  onNodeClick,
  focusedTableId,
}: {
  nodes: Node[];
  edges: Edge[];
  onNodeClick: (tableId: string) => void;
  focusedTableId: string | null;
}) {
  const { fitView } = useReactFlow();

  useEffect(() => {
    if (focusedTableId) {
      fitView({ nodes: [{ id: focusedTableId }], duration: 600, padding: 0.35 });
    }
  }, [focusedTableId, fitView]);

  return (
    <div className="w-full h-full relative select-none">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        fitView
        fitViewOptions={{ padding: 0.25, maxZoom: 1.1 }}
        onlyRenderVisibleElements={nodes.length > 50}
        onNodeClick={(_, node) => onNodeClick(node.id)}
        minZoom={0.2}
        maxZoom={2.5}
        proOptions={{ hideAttribution: true }}
      >
        <Background variant={BackgroundVariant.Dots} gap={16} size={1} color="var(--color-line-strong)" />
      </ReactFlow>
    </div>
  );
}

function DiagramControlsBar({
  isFullscreen,
  onToggleFullscreen,
}: {
  isFullscreen?: boolean;
  onToggleFullscreen?: () => void;
}) {
  const { fitView, zoomIn, zoomOut } = useReactFlow();

  return (
    <div className="flex items-center gap-1 font-mono">
      <button
        type="button"
        onClick={() => fitView({ padding: 0.25, duration: 400 })}
        className="px-2.5 py-1 text-[11px] font-medium rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-secondary hover:text-content-primary border border-line transition-all flex items-center gap-1 cursor-pointer"
        title="Fit View"
      >
        <Maximize2 className="w-3 h-3 text-accent-text" />
        <span>Fit</span>
      </button>
      <button
        type="button"
        onClick={() => zoomIn({ duration: 250 })}
        className="w-7 h-6 flex items-center justify-center text-[11px] font-mono font-medium rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-secondary hover:text-content-primary border border-line transition-all cursor-pointer"
        title="Zoom In"
      >
        +
      </button>
      <button
        type="button"
        onClick={() => zoomOut({ duration: 250 })}
        className="w-7 h-6 flex items-center justify-center text-[11px] font-mono font-medium rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-secondary hover:text-content-primary border border-line transition-all cursor-pointer"
        title="Zoom Out"
      >
        -
      </button>
      {onToggleFullscreen && (
        <button
          type="button"
          onClick={onToggleFullscreen}
          className="px-2.5 py-1 text-[11px] font-medium rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-primary border border-line-strong transition-all flex items-center gap-1.5 cursor-pointer shadow-xs"
          title={isFullscreen ? 'Exit Fullscreen (Esc)' : 'Fullscreen (F11 view)'}
        >
          {isFullscreen ? <Minimize2 className="w-3 h-3 text-accent-text" /> : <Maximize2 className="w-3 h-3 text-accent-text" />}
          <span>{isFullscreen ? 'Exit' : 'Fullscreen'}</span>
        </button>
      )}
    </div>
  );
}

function renderInlineMarkdown(text: string, keyPrefix: string): React.ReactNode[] {
  const parts = text.split(/(\*\*[^*]+\*\*|`[^`]+`)/g).filter(Boolean);
  return parts.map((part, i) => {
    if (part.startsWith('**') && part.endsWith('**')) {
      return (
        <strong key={`${keyPrefix}-${i}`} className="text-content-primary font-bold">
          {part.slice(2, -2)}
        </strong>
      );
    }
    if (part.startsWith('`') && part.endsWith('`')) {
      return (
        <code
          key={`${keyPrefix}-${i}`}
          className="bg-surface-750 border border-line rounded-[var(--radius-control)] px-1.5 py-0.5 text-[11px] font-mono text-accent-text"
        >
          {part.slice(1, -1)}
        </code>
      );
    }
    return <React.Fragment key={`${keyPrefix}-${i}`}>{part}</React.Fragment>;
  });
}

function ReadmeMarkdownPreview({ content }: { content: string }) {
  const lines = content.split('\n');
  const blocks: React.ReactNode[] = [];
  let listItems: string[] = [];

  const flushList = (key: string) => {
    if (listItems.length === 0) return;
    blocks.push(
      <ul key={key} className="space-y-1.5 pl-5 my-2.5 list-disc text-xs text-content-secondary leading-relaxed">
        {listItems.map((item, idx) => (
          <li key={idx} className="marker:text-accent-text">
            {renderInlineMarkdown(item, `${key}-li-${idx}`)}
          </li>
        ))}
      </ul>
    );
    listItems = [];
  };

  lines.forEach((line, idx) => {
    const key = `rm-${idx}`;
    const trimmed = line.trim();

    if (trimmed.startsWith('# ')) {
      flushList(`${key}-flush`);
      blocks.push(
        <h1 key={key} className="text-lg sm:text-xl font-bold font-sans text-content-primary pb-2 mb-3 border-b border-line">
          {renderInlineMarkdown(trimmed.slice(2), key)}
        </h1>
      );
      return;
    }

    if (trimmed.startsWith('## ')) {
      flushList(`${key}-flush`);
      blocks.push(
        <h2 key={key} className="text-sm sm:text-base font-semibold font-sans text-content-primary mt-5 mb-2 pb-1 border-b border-line/60">
          {renderInlineMarkdown(trimmed.slice(3), key)}
        </h2>
      );
      return;
    }

    if (trimmed.startsWith('### ')) {
      flushList(`${key}-flush`);
      blocks.push(
        <h3 key={key} className="text-xs sm:text-sm font-semibold font-sans text-content-primary mt-3.5 mb-1">
          {renderInlineMarkdown(trimmed.slice(4), key)}
        </h3>
      );
      return;
    }

    if (trimmed.startsWith('- ') || trimmed.startsWith('* ')) {
      listItems.push(trimmed.slice(2));
      return;
    }

    flushList(`${key}-flush`);
    if (trimmed === '') {
      blocks.push(<div key={key} className="h-1.5" />);
    } else {
      blocks.push(
        <p key={key} className="text-xs text-content-secondary leading-relaxed">
          {renderInlineMarkdown(trimmed, key)}
        </p>
      );
    }
  });

  flushList('rm-flush-end');

  return (
    <div className="p-5 sm:p-7 space-y-2 font-sans select-text bg-surface-850">
      {blocks}
    </div>
  );
}

export function HubSchemaCodeTab({ repo }: HubSchemaCodeTabProps) {
  const { showToast } = useToastStore();

  // 1. Resolve Active Branches & Active Branch Name
  const branches = useMemo(() => {
    if (repo.branches && repo.branches.length > 0) return repo.branches;
    return [{ id: 'main', name: 'main', isDefault: true, createdAt: new Date().toISOString() }];
  }, [repo.branches]);

  const defaultBranchName = useMemo(() => {
    return branches.find((b) => b.isDefault)?.name || branches[0]?.name || 'main';
  }, [branches]);

  const [selectedBranch, setSelectedBranch] = useState(defaultBranchName);
  const [isBranchDropdownOpen, setIsBranchDropdownOpen] = useState(false);
  const branchDropdownRef = useRef<HTMLDivElement>(null);

  // Sync selected branch if repo branches change
  useEffect(() => {
    if (defaultBranchName) {
      setSelectedBranch(defaultBranchName);
    }
  }, [defaultBranchName]);

  // 2. Resolve Active Branch Schema
  const currentBranchSchema: DatabaseSchema = useMemo(() => {
    // A. Check if the active branch in repo has schema attached
    const targetBranch = branches.find((b) => b.name === selectedBranch);
    if (targetBranch?.schema?.tables?.length) {
      return targetBranch.schema;
    }
    // B. Check if local history store has this branch
    const localProj = useProjectHistoryStore.getState().projects.find((p) => p.id === repo.id);
    const localBranch = localProj?.branches?.find((b) => b.name === selectedBranch);
    if (localBranch?.schema?.tables?.length) {
      return localBranch.schema;
    }
    // C. Fallback to repo schema
    return repo.schema || { schemaId: repo.id, name: repo.name, tables: [], relations: [] };
  }, [branches, selectedBranch, repo.id, repo.name, repo.schema]);

  const tables = currentBranchSchema.tables || [];
  const relations = currentBranchSchema.relations || [];

  // Automation / Flow integration
  // Kurallar kullanıcının KENDİ projesine ait: başkasının reposunda (ya da yerel taslakta) gösterilmez.
  // Eskiden global store'daki, görüntüleyenin son açtığı projenin kuralları her repoda görünüyordu.
  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const canSeeFlows = !!repo.caller?.canEdit && isAuthenticated && !repo.isLocalDraft;
  const [repoRules, setRepoRules] = useState<{ repoId: string; rules: AutomationRule[] } | null>(null);
  useEffect(() => {
    if (!canSeeFlows) return;
    let cancelled = false;
    fetchAutomationRules(repo.id)
      .then((rules) => { if (!cancelled) setRepoRules({ repoId: repo.id, rules }); })
      .catch(() => { /* kural yoksa akış görünümü boş kalır */ });
    return () => { cancelled = true; };
  }, [canSeeFlows, repo.id]);
  const rules = useMemo(
    () => (canSeeFlows && repoRules?.repoId === repo.id ? repoRules.rules : []),
    [canSeeFlows, repoRules, repo.id],
  );
  const projectFlows = useMemo(() => {
    return rules.filter((r) => !r.scopeTableId || tables.some((t) => t.id === r.scopeTableId));
  }, [rules, tables]);

  // Add Artifact Modal State
  const [isAddModalOpen, setIsAddModalOpen] = useState(false);
  const [addModalTab, setAddModalTab] = useState<'mermaid' | 'build'>('mermaid');
  const [selectedMermaidType, setSelectedMermaidType] = useState<string>('ER');
  const [selectedBuildTarget, setSelectedBuildTarget] = useState<string>('sdk.typescript');
  const [customFiles, setCustomFiles] = useState<{ key: string; label: string; tag?: string; icon: typeof FileCode }[]>([]);

  // Preview content for modal
  const currentMermaidPreview = useMemo(() => {
    return generateMermaidByType(currentBranchSchema, selectedMermaidType);
  }, [currentBranchSchema, selectedMermaidType]);

  const currentBuildPreview = useMemo(() => {
    return generateBuildArtifact(currentBranchSchema, selectedBuildTarget, repo.dbType);
  }, [currentBranchSchema, selectedBuildTarget, repo.dbType]);

  // Module filter state
  const [selectedModule, setSelectedModule] = useState<string>('all');
  const [isModuleDropdownOpen, setIsModuleDropdownOpen] = useState(false);
  const moduleDropdownRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (branchDropdownRef.current && !branchDropdownRef.current.contains(event.target as globalThis.Node)) {
        setIsBranchDropdownOpen(false);
      }
      if (moduleDropdownRef.current && !moduleDropdownRef.current.contains(event.target as globalThis.Node)) {
        setIsModuleDropdownOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const [tableFilter, setTableFilter] = useState('');
  const [canvasSearchQuery, setCanvasSearchQuery] = useState('');
  const [focusedTableId, setFocusedTableId] = useState<string | null>(null);
  const [activeVisualizerView, setActiveVisualizerView] = useState<'ERD' | 'FLOWS'>('ERD');

  const [activeFile, setActiveFile] = useState<GeneratedFileType>('DIAGRAM');
  // Derlenmiş dosya önbelleği şemaya bağlı: dal/şema değişince eski dalın çıktısı gösterilmesin.
  type FileMap = Partial<Record<GeneratedFileType, string>>;
  const [fileCache, setFileCache] = useState<{ source: DatabaseSchema | null; files: FileMap }>({ source: null, files: {} });
  // Kullanıcının "Add" ile eklediği dosyalar o anki şemadan üretilmiş anlık görüntü; önbellekle silinmez.
  const [customContents, setCustomContents] = useState<FileMap>({});
  const fileContents = useMemo<FileMap>(
    () => ({ ...(fileCache.source === currentBranchSchema ? fileCache.files : {}), ...customContents }),
    [fileCache, currentBranchSchema, customContents],
  );
  const setFileContents = useCallback(
    (update: (prev: FileMap) => FileMap) =>
      setFileCache((prev) => ({
        source: currentBranchSchema,
        files: update(prev.source === currentBranchSchema ? prev.files : {}),
      })),
    [currentBranchSchema],
  );
  const [loadingFile, setLoadingFile] = useState(false);
  const [isCopied, setIsCopied] = useState(false);
  const [isFullscreen, setIsFullscreen] = useState(false);
  const [readmeViewMode, setReadmeViewMode] = useState<'preview' | 'code'>('preview');
  const [mermaidViewMode, setMermaidViewMode] = useState<'visualizer' | 'code'>('visualizer');

  // Client-side mounted state for React portal
  const [isMounted, setIsMounted] = useState(false);
  useEffect(() => {
    setIsMounted(true);
  }, []);

  // Lock body scroll and handle ESC key during fullscreen
  useEffect(() => {
    if (isFullscreen) {
      document.body.style.overflow = 'hidden';
    } else {
      document.body.style.overflow = '';
    }
    function handleKeyDown(e: KeyboardEvent) {
      if (e.key === 'Escape' && isFullscreen) {
        setIsFullscreen(false);
      }
    }
    window.addEventListener('keydown', handleKeyDown);
    return () => {
      document.body.style.overflow = '';
      window.removeEventListener('keydown', handleKeyDown);
    };
  }, [isFullscreen]);

  // Module categorization
  const moduleMap = useMemo(() => extractModules(tables), [tables]);
  const moduleOptions = useMemo(() => {
    const list = Array.from(moduleMap.entries()).map(([name, tbls]) => ({
      name,
      count: tbls.length,
    }));
    return list.sort((a, b) => b.count - a.count);
  }, [moduleMap]);

  // Filter tables by module and canvas search query
  const visibleTables = useMemo(() => {
    let result = tables;
    if (selectedModule !== 'all') {
      const allowed = new Set(moduleMap.get(selectedModule)?.map((t) => t.id) || []);
      result = result.filter((t) => allowed.has(t.id));
    }
    if (canvasSearchQuery.trim()) {
      const q = canvasSearchQuery.toLowerCase().trim();
      result = result.filter(
        (t) => t.name.toLowerCase().includes(q) || t.columns.some((c) => c.name.toLowerCase().includes(q)),
      );
    }
    return result;
  }, [tables, selectedModule, moduleMap, canvasSearchQuery]);

  // Sidebar table filter
  const sidebarFilteredTables = useMemo(() => {
    if (!tableFilter.trim()) return tables;
    const q = tableFilter.toLowerCase().trim();
    return tables.filter(
      (t) => t.name.toLowerCase().includes(q) || t.columns.some((c) => c.name.toLowerCase().includes(q)),
    );
  }, [tables, tableFilter]);

  // ReactFlow Nodes & Edges Generation
  const { flowNodes, flowEdges } = useMemo(() => {
    if (activeVisualizerView === 'FLOWS' && projectFlows.length > 0) {
      const nodes: Node[] = projectFlows.map((rule, idx) => {
        const targetTable = tables.find((t) => t.id === rule.scopeTableId);
        return {
          id: rule.id,
          type: 'hubFlowNode',
          position: { x: (idx % 3) * 360 + 40, y: Math.floor(idx / 3) * 260 + 40 },
          data: {
            title: rule.name,
            subtitle: rule.triggerType,
            category: 'flow',
            badge: rule.enabled ? 'Active Flow' : 'Disabled',
            description: targetTable ? `Attached to table ${targetTable.name}` : 'Global workspace automation',
            metrics: [
              { label: 'Trigger', value: rule.triggerType },
              { label: 'Steps', value: `${rule.actions.length} Actions` },
            ],
            isFocused: focusedTableId === rule.id,
          },
        };
      });

      const edges: Edge[] = [];
      projectFlows.forEach((rule, idx) => {
        if (idx > 0 && idx % 3 !== 0) {
          edges.push({
            id: `e-flow-${projectFlows[idx - 1].id}-${rule.id}`,
            source: projectFlows[idx - 1].id,
            target: rule.id,
            animated: true,
            style: { stroke: 'var(--color-accent-text)', strokeWidth: 1.5 },
          });
        }
      });
      return { flowNodes: nodes, flowEdges: edges };
    }

    const visibleTableIds = new Set(visibleTables.map((t) => t.id));
    const colsCount = Math.max(1, Math.ceil(Math.sqrt(visibleTables.length * 1.5)));
    const colWidth = 360;
    const rowHeight = 390;

    const nodes: Node[] = visibleTables.map((t, idx) => {
      const col = idx % colsCount;
      const row = Math.floor(idx / colsCount);
      return {
        id: t.id,
        type: 'hubTable',
        position: { x: col * colWidth + 40, y: row * rowHeight + 40 },
        data: { table: t, isFocused: focusedTableId === t.id },
      };
    });

    const edges: Edge[] = [];
    for (const rel of relations) {
      if (visibleTableIds.has(rel.sourceTableId) && visibleTableIds.has(rel.targetTableId)) {
        edges.push({
          id: `e-${rel.sourceTableId}-${rel.targetTableId}-${rel.sourceColumnId}`,
          source: rel.sourceTableId,
          target: rel.targetTableId,
          animated: focusedTableId === rel.sourceTableId || focusedTableId === rel.targetTableId,
          style: {
            stroke:
              focusedTableId === rel.sourceTableId || focusedTableId === rel.targetTableId
                ? 'var(--color-accent-text)'
                : 'var(--color-line-strong)',
            strokeWidth: focusedTableId === rel.sourceTableId || focusedTableId === rel.targetTableId ? 2 : 1.2,
            strokeDasharray: '4 4',
          },
        });
      }
    }

    return { flowNodes: nodes, flowEdges: edges };
  }, [activeVisualizerView, visibleTables, relations, focusedTableId, tables, projectFlows]);

  // On-demand code compilation
  useEffect(() => {
    if (activeFile === 'DIAGRAM') return;
    if (fileContents[activeFile]) return;

    let isSubscribed = true;
    const loadContent = async () => {
      try {
        setLoadingFile(true);
        let content = '';

        if (activeFile === 'schema.sql') {
          const sql = await schemaService.compileSql(currentBranchSchema, repo.dbType || 'PostgreSQL');
          content = sql || '-- No DDL generated for this schema.';
        } else if (activeFile === 'schema.prisma') {
          content = `// Auto-generated Prisma Schema for ${repo.name}\ndatasource db {\n  provider = "${(repo.dbType || 'postgresql').toLowerCase()}"\n  url      = env("DATABASE_URL")\n}\n\ngenerator client {\n  provider = "prisma-client-js"\n}\n\n${tables.map((t) => `model ${t.name} {\n${t.columns.map((c) => `  ${c.name.padEnd(16)} ${c.isPK ? 'Int      @id @default(autoincrement())' : 'String'}`).join('\n')}\n}`).join('\n\n')}`;
        } else if (activeFile === 'README.md') {
          const relLines = relations
            .map((r) => {
              const src = tables.find((t) => t.id === r.sourceTableId)?.name || r.sourceTableId;
              const tgt = tables.find((t) => t.id === r.targetTableId)?.name || r.targetTableId;
              return `- \`${src}.${r.sourceColumnId}\` ➔ \`${tgt}.${r.targetColumnId}\``;
            })
            .join('\n');
          content = `# ${repo.name}\n\n${repo.description || 'Production-ready database architecture.'}\n\n## Dialect & Specifications\n- Database Engine: **${repo.dbType || 'PostgreSQL'}**\n- Active Branch: **${selectedBranch}**\n\n## Table Architecture (${tables.length})\n${tables.map((t) => `- **\`${t.name}\`** (${t.columns.length} columns)`).join('\n')}\n\n## Relationships & Foreign Keys (${relations.length})\n${relLines}`;
        } else if (activeFile === 'DbContext.cs') {
          content = `// Auto-generated EF Core DbContext for ${repo.name} (${repo.dbType || 'PostgreSQL'})\nusing Microsoft.EntityFrameworkCore;\n\nnamespace ${repo.name.replace(/[^a-zA-Z0-9]/g, '')}.Data;\n\npublic class AppDbContext : DbContext\n{\n    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }\n\n${tables.map((t) => `    public DbSet<${t.name}> ${t.name} { get; set; } = null!;`).join('\n')}\n}`;
        } else if (activeFile === 'openapi.json') {
          content = JSON.stringify(
            {
              openapi: '3.0.0',
              info: {
                title: `${repo.name} REST API`,
                version: '1.0.0',
                description: repo.description || 'Generated REST Gateway specification',
              },
              paths: Object.fromEntries(
                tables.map((t) => [
                  `/api/${t.name}`,
                  {
                    get: { summary: `List ${t.name} records`, responses: { '200': { description: 'Success' } } },
                    post: { summary: `Insert into ${t.name}`, responses: { '201': { description: 'Created' } } },
                  },
                ]),
              ),
            },
            null,
            2,
          );
        } else if (activeFile === 'diagram.mmd') {
          content = generateMermaidErDiagram(currentBranchSchema);
        }

        if (isSubscribed && content) {
          setFileContents((prev) => ({ ...prev, [activeFile]: content }));
        }
      } catch {
        if (isSubscribed) {
          setFileContents((prev) => ({
            ...prev,
            [activeFile]: `// Failed to compile ${activeFile} for this schema.`,
          }));
        }
      } finally {
        if (isSubscribed) setLoadingFile(false);
      }
    };

    loadContent();
    return () => {
      isSubscribed = false;
      // İptal edilen yükleme loadingFile'ı true bırakmasın (yeni çalışma erken dönerse takılırdı)
      setLoadingFile(false);
    };
  }, [activeFile, currentBranchSchema, repo, tables, fileContents, relations, selectedBranch, setFileContents]);

  const handleCopy = () => {
    const text = fileContents[activeFile];
    if (text) {
      navigator.clipboard.writeText(text);
      setIsCopied(true);
      showToast(`Copied ${activeFile}`, 'success');
      setTimeout(() => setIsCopied(false), 2000);
    }
  };

  const handleAddMermaid = (typeId: string) => {
    const typeDef = MERMAID_DIAGRAM_TYPES.find((t) => t.id === typeId) || MERMAID_DIAGRAM_TYPES[0];
    const mmdKey = `diagram-${typeDef.id.toLowerCase()}.${typeDef.fileExtension}`;
    const mmdContent = generateMermaidByType(currentBranchSchema, typeDef.id);
    setCustomFiles((prev) => [
      ...prev.filter((f) => f.key !== mmdKey),
      { key: mmdKey, label: mmdKey, tag: 'Mermaid', icon: CodeXml },
    ]);
    setCustomContents((prev) => ({ ...prev, [mmdKey]: mmdContent }));
    setActiveFile(mmdKey);
    setIsAddModalOpen(false);
    showToast(`Mermaid ${typeDef.name} diagram added to visualizer!`, 'success');
  };

  const handleDownloadMermaid = (typeId: string) => {
    const typeDef = MERMAID_DIAGRAM_TYPES.find((t) => t.id === typeId) || MERMAID_DIAGRAM_TYPES[0];
    const mmdContent = generateMermaidByType(currentBranchSchema, typeDef.id);
    const blob = new Blob([mmdContent], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `${repo.name}-${typeDef.id.toLowerCase()}.${typeDef.fileExtension}`;
    a.click();
    URL.revokeObjectURL(url);
    showToast(`Downloaded ${typeDef.name} (${typeDef.fileExtension})`, 'success');
  };

  const handleAddBuildCode = (targetId: string) => {
    const { fileName, content, tag } = generateBuildArtifact(currentBranchSchema, targetId, repo.dbType);
    setCustomFiles((prev) => [
      ...prev.filter((f) => f.key !== fileName),
      { key: fileName, label: fileName, tag, icon: FileCode },
    ]);
    setCustomContents((prev) => ({ ...prev, [fileName]: content }));
    setActiveFile(fileName);
    setIsAddModalOpen(false);
    showToast(`${fileName} compiled and added from Build!`, 'success');
  };

  const fileDefinitions: { key: GeneratedFileType; label: string; tag?: string; icon: typeof FileCode }[] = [
    { key: 'DIAGRAM', label: 'DIAGRAM (Schema ERD)', icon: CodeXml },
    { key: 'schema.sql', label: 'schema.sql', tag: repo.dbType || 'SQL', icon: FileCode },
    { key: 'DbContext.cs', label: 'DbContext.cs', tag: 'EF Core', icon: FileCode },
    { key: 'schema.prisma', label: 'schema.prisma', tag: 'Prisma', icon: FileCode },
    { key: 'openapi.json', label: 'openapi.json', tag: 'OpenAPI', icon: FileCode },
    { key: 'README.md', label: 'README.md', tag: 'Docs', icon: FileText },
    ...customFiles,
  ];

  return (
    <div className="space-y-6">
      {/* Main 2-Column Grid: Left Visualizer & Files, Right About & Tables */}
      <div className="flex flex-col lg:flex-row gap-6 items-start w-full">
        {/* Left / Center Main Column */}
        <div className="flex-1 w-full min-w-0 space-y-5">
          {/* 1. GitHub-style File Explorer Card */}
          <div className="rounded-[var(--radius-card)] border border-line bg-surface-850 overflow-hidden shadow-xs">
            {/* Unified Branch Header Bar (Minimal: Only Branch Selector on left, Add on right) */}
            <div className="px-3.5 py-2.5 bg-surface-800 border-b border-line flex items-center justify-between gap-3 text-xs font-mono">
              {/* Left: Branch Selector Dropdown */}
              <div className="relative" ref={branchDropdownRef}>
                <button
                  type="button"
                  onClick={() => setIsBranchDropdownOpen((prev) => !prev)}
                  className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 border border-line text-content-primary text-xs font-mono font-semibold transition-colors cursor-pointer shadow-xs"
                >
                  <GitBranch className="w-3.5 h-3.5 text-accent-text shrink-0" />
                  <span>{selectedBranch}</span>
                  <ChevronDown
                    className={`w-3 h-3 text-content-muted transition-transform ${
                      isBranchDropdownOpen ? 'rotate-180' : ''
                    }`}
                  />
                </button>

                {isBranchDropdownOpen && (
                  <div className="absolute left-0 top-full mt-1.5 w-52 rounded-[var(--radius-card)] bg-surface-800 border border-line-strong shadow-xl p-1.5 z-40 text-xs font-mono space-y-0.5">
                    <div className="px-2 py-1 text-[10px] uppercase font-bold text-content-muted tracking-wider">
                      Switch branch
                    </div>
                    {branches.map((b) => (
                      <button
                        key={b.id || b.name}
                        type="button"
                        onClick={() => {
                          setSelectedBranch(b.name);
                          setIsBranchDropdownOpen(false);
                        }}
                        className={`w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-control)] text-left transition-colors cursor-pointer ${
                          b.name === selectedBranch
                            ? 'bg-surface-700 text-content-primary font-bold'
                            : 'text-content-secondary hover:text-content-primary hover:bg-surface-750'
                        }`}
                      >
                        <span className="truncate">{b.name}</span>
                        {b.name === selectedBranch && <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />}
                      </button>
                    ))}
                  </div>
                )}
              </div>

              {/* Right: Add Artifact from Build & Timestamp */}
              <div className="flex items-center gap-2 shrink-0">
                <button
                  type="button"
                  onClick={() => setIsAddModalOpen(true)}
                  className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-primary border border-line-strong transition-all cursor-pointer shadow-xs"
                >
                  <Plus className="w-3.5 h-3.5 text-accent-text" />
                  <span>Add</span>
                </button>
                <span className="text-[11px] text-content-muted">
                  {new Date(repo.updatedAt).toLocaleDateString()}
                </span>
              </div>
            </div>

            {/* File List Rows (Clean Polaris style — NO black left border, NO 'Interactive' tag) */}
            <div className="divide-y divide-line text-xs font-mono">
              {fileDefinitions.map((f) => {
                const Icon = f.icon;
                const isSelected = activeFile === f.key;
                return (
                  <button
                    key={f.key}
                    type="button"
                    onClick={() => setActiveFile(f.key)}
                    className={`w-full flex items-center justify-between px-4 py-2.5 transition-colors text-left cursor-pointer ${
                      isSelected
                        ? 'bg-surface-700/70 text-content-primary font-semibold'
                        : 'hover:bg-surface-800/80 text-content-secondary hover:text-content-primary'
                    }`}
                  >
                    <div className="flex items-center gap-2.5 truncate">
                      <Icon className={`w-4 h-4 shrink-0 ${isSelected ? 'text-accent-text' : 'text-content-muted'}`} />
                      <span className={`truncate ${isSelected ? 'text-content-primary font-bold' : 'text-content-secondary'}`}>
                        {f.label}
                      </span>
                    </div>
                    {f.tag && (
                      <span className="text-[11px] text-content-muted shrink-0 px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-750 border border-line">
                        {f.tag}
                      </span>
                    )}
                  </button>
                );
              })}
            </div>
          </div>

          {/* 2. Selected File Preview / Visualizer Box */}
          {activeFile === 'DIAGRAM' ? (
            <div className="rounded-[var(--radius-card)] border border-line bg-surface-850 overflow-hidden shadow-xs">
              <ReactFlowProvider>
                {/* Visualizer Header Bar (Sleek, Single-Line, Cleaned) */}
                <div className="px-4 py-2.5 bg-surface-800 border-b border-line flex flex-wrap items-center justify-between gap-3 text-xs">
                  <div className="flex items-center flex-wrap gap-2.5">
                    <div className="flex items-center gap-1.5 font-semibold text-content-primary">
                      <CodeXml className="w-4 h-4 text-accent-text" />
                      <span>Visualizer</span>
                    </div>

                    {/* If flows exist, allow toggling Schema ERD vs Flows */}
                    {projectFlows.length > 0 && (
                      <div className="flex items-center rounded-[var(--radius-control)] bg-surface-750 p-0.5 border border-line text-xs font-mono">
                        <button
                          type="button"
                          onClick={() => setActiveVisualizerView('ERD')}
                          className={`px-2 py-0.5 rounded-[calc(var(--radius-control)-2px)] transition-all flex items-center gap-1 cursor-pointer ${
                            activeVisualizerView === 'ERD'
                              ? 'bg-surface-850 text-content-primary font-semibold shadow-xs'
                              : 'text-content-secondary hover:text-content-primary'
                          }`}
                        >
                          <span>ERD ({tables.length})</span>
                        </button>
                        <button
                          type="button"
                          onClick={() => setActiveVisualizerView('FLOWS')}
                          className={`px-2 py-0.5 rounded-[calc(var(--radius-control)-2px)] transition-all flex items-center gap-1 cursor-pointer ${
                            activeVisualizerView === 'FLOWS'
                              ? 'bg-surface-850 text-content-primary font-semibold shadow-xs'
                              : 'text-content-secondary hover:text-content-primary'
                          }`}
                        >
                          <Workflow className="w-3 h-3 text-accent-text" />
                          <span>Flows ({projectFlows.length})</span>
                        </button>
                      </div>
                    )}

                    {/* Module Filter Dropdown (if multiple modules) */}
                    {moduleOptions.length > 1 && (
                      <div className="relative" ref={moduleDropdownRef}>
                        <button
                          type="button"
                          onClick={() => setIsModuleDropdownOpen((prev) => !prev)}
                          className="inline-flex items-center gap-1.5 px-2.5 py-1 text-xs font-mono rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 border border-line text-content-primary transition-all cursor-pointer shadow-xs"
                        >
                          <Layers className="w-3 h-3 text-accent-text" />
                          <span className="truncate max-w-[100px]">
                            {selectedModule === 'all' ? 'All Modules' : selectedModule}
                          </span>
                          <ChevronDown
                            className={`w-3 h-3 text-content-muted transition-transform duration-150 ${
                              isModuleDropdownOpen ? 'rotate-180 text-content-primary' : ''
                            }`}
                          />
                        </button>

                        {isModuleDropdownOpen && (
                          <div className="absolute left-0 top-full mt-1.5 z-40 w-48 max-h-56 overflow-y-auto rounded-[var(--radius-control)] bg-surface-800 border border-line shadow-2xl py-1 text-xs font-mono divide-y divide-line/30 scrollbar-none">
                            <button
                              type="button"
                              onClick={() => {
                                setSelectedModule('all');
                                setIsModuleDropdownOpen(false);
                              }}
                              className={`w-full flex items-center justify-between px-3 py-1.5 text-left transition-colors cursor-pointer ${
                                selectedModule === 'all'
                                  ? 'bg-surface-700 text-content-primary font-semibold'
                                  : 'text-content-secondary hover:text-content-primary hover:bg-surface-750'
                              }`}
                            >
                              <span>All Modules ({tables.length})</span>
                              {selectedModule === 'all' && (
                                <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />
                              )}
                            </button>
                            {moduleOptions.map((m) => (
                              <button
                                key={m.name}
                                type="button"
                                onClick={() => {
                                  setSelectedModule(m.name);
                                  setIsModuleDropdownOpen(false);
                                }}
                                className={`w-full flex items-center justify-between px-3 py-1.5 text-left transition-colors cursor-pointer ${
                                  selectedModule === m.name
                                    ? 'bg-surface-700 text-content-primary font-semibold'
                                    : 'text-content-secondary hover:text-content-primary hover:bg-surface-750'
                                }`}
                              >
                                <span className="truncate">
                                  {m.name} ({m.count})
                                </span>
                                {selectedModule === m.name && (
                                  <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />
                                )}
                              </button>
                            ))}
                          </div>
                        )}
                      </div>
                    )}
                  </div>

                  {/* Right side controls: Search in Canvas + Fit & Fullscreen */}
                  <div className="flex items-center flex-wrap gap-2">
                    <div className="relative">
                      <Search className="w-3 h-3 absolute left-2 top-1/2 -translate-y-1/2 text-content-muted" />
                      <input
                        type="text"
                        placeholder="Search table in canvas..."
                        value={canvasSearchQuery}
                        onChange={(e) => setCanvasSearchQuery(e.target.value)}
                        className="pl-6.5 pr-2 py-1 text-xs font-mono rounded-[var(--radius-control)] bg-surface-700 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden w-36 sm:w-44"
                      />
                    </div>

                    <DiagramControlsBar
                      isFullscreen={false}
                      onToggleFullscreen={() => setIsFullscreen(true)}
                    />
                  </div>
                </div>

                {/* Bounded ReactFlow Viewport */}
                <div className="h-[560px] w-full relative bg-surface-900">
                  <InnerDiagramCanvas
                    nodes={flowNodes}
                    edges={flowEdges}
                    onNodeClick={(id) => setFocusedTableId(id)}
                    focusedTableId={focusedTableId}
                  />
                </div>
              </ReactFlowProvider>
            </div>
          ) : (
            <div className="rounded-[var(--radius-card)] border border-line bg-surface-850 overflow-hidden shadow-xs font-mono text-xs">
              {/* Code / Markdown Header */}
              <div className="px-4 py-2.5 bg-surface-800 border-b border-line flex items-center justify-between">
                <div className="flex items-center gap-3">
                  <span className="text-content-primary font-semibold flex items-center gap-2">
                    <FileCode className="w-4 h-4 text-accent-text" />
                    <span>{activeFile}</span>
                  </span>

                  {activeFile === 'README.md' && (
                    <div className="flex items-center rounded-[var(--radius-control)] bg-surface-750 p-0.5 border border-line text-xs font-mono">
                      <button
                        type="button"
                        onClick={() => setReadmeViewMode('preview')}
                        className={`px-2 py-0.5 rounded-[calc(var(--radius-control)-2px)] transition-all flex items-center gap-1 cursor-pointer ${
                          readmeViewMode === 'preview'
                            ? 'bg-surface-850 text-content-primary font-semibold shadow-xs'
                            : 'text-content-secondary hover:text-content-primary'
                        }`}
                      >
                        <Eye className="w-3 h-3 text-accent-text" />
                        <span>Preview</span>
                      </button>
                      <button
                        type="button"
                        onClick={() => setReadmeViewMode('code')}
                        className={`px-2 py-0.5 rounded-[calc(var(--radius-control)-2px)] transition-all flex items-center gap-1 cursor-pointer ${
                          readmeViewMode === 'code'
                            ? 'bg-surface-850 text-content-primary font-semibold shadow-xs'
                            : 'text-content-secondary hover:text-content-primary'
                        }`}
                      >
                        <Code2 className="w-3 h-3" />
                        <span>Code</span>
                      </button>
                    </div>
                  )}

                  {activeFile.endsWith('.mmd') && (
                    <div className="flex items-center rounded-[var(--radius-control)] bg-surface-750 p-0.5 border border-line text-xs font-mono">
                      <button
                        type="button"
                        onClick={() => setMermaidViewMode('visualizer')}
                        className={`px-2 py-0.5 rounded-[calc(var(--radius-control)-2px)] transition-all flex items-center gap-1 cursor-pointer ${
                          mermaidViewMode === 'visualizer'
                            ? 'bg-surface-850 text-content-primary font-semibold shadow-xs'
                            : 'text-content-secondary hover:text-content-primary'
                        }`}
                      >
                        <Eye className="w-3 h-3 text-accent-text" />
                        <span>Visualizer</span>
                      </button>
                      <button
                        type="button"
                        onClick={() => setMermaidViewMode('code')}
                        className={`px-2 py-0.5 rounded-[calc(var(--radius-control)-2px)] transition-all flex items-center gap-1 cursor-pointer ${
                          mermaidViewMode === 'code'
                            ? 'bg-surface-850 text-content-primary font-semibold shadow-xs'
                            : 'text-content-secondary hover:text-content-primary'
                        }`}
                      >
                        <Code2 className="w-3 h-3" />
                        <span>Code</span>
                      </button>
                    </div>
                  )}

                  <span className="text-[11px] text-content-muted font-normal">
                    {fileContents[activeFile]?.split('\n').length ?? 0} lines
                  </span>
                </div>

                <div className="flex items-center gap-2">
                  <button
                    type="button"
                    onClick={handleCopy}
                    className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-secondary hover:text-content-primary border border-line text-[11px] font-mono transition-colors cursor-pointer"
                  >
                    {isCopied ? <Check className="w-3 h-3 text-accent-text" /> : <Copy className="w-3 h-3" />}
                    <span>{isCopied ? 'Copied' : 'Copy'}</span>
                  </button>
                </div>
              </div>

              {/* Code / Markdown Viewport */}
              <div className="h-[560px] overflow-y-auto p-4 bg-surface-900 leading-relaxed select-text">
                {loadingFile ? (
                  <div className="flex items-center justify-center py-20 text-content-muted gap-2">
                    <span className="w-4 h-4 border-2 border-accent-text border-t-transparent rounded-full animate-spin" />
                    <span>Compiling {activeFile}…</span>
                  </div>
                ) : activeFile === 'README.md' && readmeViewMode === 'preview' ? (
                  <ReadmeMarkdownPreview content={fileContents['README.md'] || ''} />
                ) : activeFile.endsWith('.mmd') && mermaidViewMode === 'visualizer' ? (
                  <div className="w-full h-full min-h-[520px] relative bg-surface-900 rounded-[var(--radius-control)]">
                    <MermaidPreview mermaidCode={fileContents[activeFile] || ''} />
                  </div>
                ) : (
                  <pre className="font-mono text-xs text-content-primary whitespace-pre overflow-x-auto">
                    <code>{fileContents[activeFile] || '// Empty file'}</code>
                  </pre>
                )}
              </div>
            </div>
          )}
        </div>

        {/* Right Column: About & Tables (Exact bottom alignment with Visualizer) */}
        <div className="w-full lg:w-72 xl:w-80 shrink-0 flex flex-col space-y-4">
          {/* 1. About Card */}
          <div className="rounded-[var(--radius-card)] border border-line bg-surface-850 p-4 space-y-3.5 shadow-xs shrink-0">
            <h3 className="text-xs font-bold text-content-primary flex items-center gap-2 uppercase tracking-wider font-mono">
              <Layers className="w-3.5 h-3.5 text-accent-text" />
              <span>About</span>
            </h3>
            <p className="text-xs text-content-secondary leading-relaxed">
              {repo.description || 'Production-ready database architecture with normalized schemas.'}
            </p>

            <div className="pt-2 border-t border-line space-y-2 text-xs font-mono">
              <div className="flex items-center justify-between text-content-muted">
                <span>Architecture:</span>
                <span className="text-content-primary font-semibold">v1.0</span>
              </div>
              <div className="flex items-center justify-between text-content-muted">
                <span>Entities:</span>
                <span className="text-content-primary">{tables.length} tables, {relations.length} relations</span>
              </div>
              <div className="flex items-center justify-between text-content-muted">
                <span>Database Engine:</span>
                <span className="text-content-primary font-semibold">{repo.dbType || 'PostgreSQL'}</span>
              </div>
              <div className="flex items-center justify-between text-content-muted">
                <span>Community:</span>
                <span className="text-content-primary">
                  ★ {repo.starCount} · ⑂ {repo.forkCount}
                </span>
              </div>
            </div>
          </div>

          {/* 2. Tables Directory: Exact height (lg:h-[660px]) so total height equals left column (886px) */}
          <div className="rounded-[var(--radius-card)] border border-line bg-surface-850 p-4 shadow-xs flex flex-col lg:h-[660px] overflow-hidden">
            <div className="flex items-center justify-between pb-3 shrink-0">
              <h3 className="text-xs font-bold text-content-primary flex items-center gap-2 uppercase tracking-wider font-mono">
                <TableIcon className="w-3.5 h-3.5 text-accent-text" />
                <span>Tables ({tables.length})</span>
              </h3>
            </div>

            {/* Quick table filter */}
            <div className="relative mb-3 shrink-0">
              <Search className="w-3.5 h-3.5 absolute left-2.5 top-1/2 -translate-y-1/2 text-content-muted" />
              <input
                type="text"
                placeholder="Filter tables..."
                value={tableFilter}
                onChange={(e) => setTableFilter(e.target.value)}
                className="w-full pl-8 pr-3 py-1.5 text-xs font-mono rounded-[var(--radius-control)] bg-surface-800 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden"
              />
            </div>

            {/* Scrollable Tables List: strictly scrolls inside the card with bottom border perfectly locked */}
            <div className="flex-1 overflow-y-auto space-y-1 divide-y divide-line/40 text-xs font-mono pr-1 scrollbar-none min-h-0">
              {sidebarFilteredTables.map((t) => (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => {
                    setActiveFile('DIAGRAM');
                    setFocusedTableId(t.id);
                  }}
                  className={`w-full flex items-center justify-between py-1.5 px-2 rounded-[var(--radius-control)] transition-colors text-left cursor-pointer ${
                    focusedTableId === t.id
                      ? 'bg-surface-700 text-content-primary font-semibold'
                      : 'hover:bg-surface-800 text-content-secondary hover:text-content-primary'
                  }`}
                >
                  <span className="truncate flex items-center gap-1.5">
                    <span className="text-content-muted">#</span>
                    <span className="truncate">{t.name}</span>
                  </span>
                  <span className="text-[10px] text-content-muted shrink-0 ml-1.5">
                    {t.columns.length}
                  </span>
                </button>
              ))}
            </div>
          </div>
        </div>
      </div>

      {/* Add Artifact from Build & Compiler Modal */}
      {isAddModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-surface-950/85 backdrop-blur-md animate-in fade-in duration-150">
          <div className="w-full max-w-5xl h-[640px] rounded-[var(--radius-card)] bg-surface-900 border border-line-strong shadow-2xl p-6 flex flex-col space-y-4 text-content-primary">
            {/* Modal Header (Shopify Polaris Style: Minimal, High-Contrast) */}
            <div className="flex items-center justify-between pb-3 border-b border-line shrink-0">
              <h3 className="font-bold text-base text-content-primary tracking-tight">Add Artifact</h3>
              <button
                type="button"
                onClick={() => setIsAddModalOpen(false)}
                className="text-content-muted hover:text-content-primary p-1 rounded-[var(--radius-control)] hover:bg-surface-800 transition-colors cursor-pointer"
                aria-label="Close dialog"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Navigation Sub-Tabs */}
            <div className="flex items-center gap-1.5 border-b border-line pb-2.5 shrink-0">
              <button
                type="button"
                onClick={() => setAddModalTab('mermaid')}
                className={`px-3.5 py-1.5 rounded-[var(--radius-control)] text-xs font-semibold transition-colors cursor-pointer ${
                  addModalTab === 'mermaid'
                    ? 'bg-surface-750 text-content-primary border border-line-strong shadow-xs'
                    : 'text-content-muted hover:text-content-primary'
                }`}
              >
                Mermaid Diagrams
              </button>

              <button
                type="button"
                onClick={() => setAddModalTab('build')}
                className={`px-3.5 py-1.5 rounded-[var(--radius-control)] text-xs font-semibold transition-colors cursor-pointer ${
                  addModalTab === 'build'
                    ? 'bg-surface-750 text-content-primary border border-line-strong shadow-xs'
                    : 'text-content-muted hover:text-content-primary'
                }`}
              >
                Compiler Targets
              </button>
            </div>

            {/* Tab 1: Mermaid Diagrams */}
            {addModalTab === 'mermaid' && (
              <div className="flex-1 min-h-0 grid grid-cols-1 md:grid-cols-12 gap-5">
                {/* Left Selector Column (Clean single-line Polaris list) */}
                <div className="md:col-span-4 h-full flex flex-col space-y-1 overflow-y-auto pr-1">
                  {MERMAID_DIAGRAM_TYPES.map((type) => (
                    <button
                      key={type.id}
                      type="button"
                      onClick={() => setSelectedMermaidType(type.id)}
                      className={`w-full text-left px-3 py-2 rounded-[var(--radius-control)] text-xs transition-colors flex items-center justify-between cursor-pointer ${
                        selectedMermaidType === type.id
                          ? 'bg-surface-750 text-content-primary font-semibold border border-line-strong shadow-xs'
                          : 'text-content-secondary hover:text-content-primary hover:bg-surface-800/60'
                      }`}
                    >
                      <span>{type.name}</span>
                      <span className="text-micro font-mono text-content-muted">.{type.fileExtension}</span>
                    </button>
                  ))}
                </div>

                {/* Right Preview Column */}
                <div className="md:col-span-8 h-full flex flex-col rounded-[var(--radius-card)] bg-surface-950 border border-line overflow-hidden">
                  <div className="px-3.5 py-2 bg-surface-850 border-b border-line flex items-center justify-between text-xs font-mono text-content-muted shrink-0">
                    <span className="text-content-secondary font-medium">diagram-{selectedMermaidType.toLowerCase()}.mmd</span>
                    <button
                      type="button"
                      onClick={() => {
                        navigator.clipboard.writeText(currentMermaidPreview);
                        showToast('Mermaid code copied!', 'success');
                      }}
                      className="hover:text-content-primary transition-colors flex items-center gap-1.5 cursor-pointer text-xs"
                    >
                      <Copy className="w-3.5 h-3.5" />
                      <span>Copy</span>
                    </button>
                  </div>
                  <pre className="flex-1 min-h-0 p-4 text-xs font-mono text-content-secondary overflow-y-auto select-text whitespace-pre leading-relaxed">
                    {currentMermaidPreview}
                  </pre>
                  <div className="p-3 bg-surface-850 border-t border-line flex items-center justify-between gap-2 shrink-0">
                    <button
                      type="button"
                      onClick={() => handleDownloadMermaid(selectedMermaidType)}
                      className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-750 hover:bg-surface-700 text-content-primary border border-line-strong shadow-xs transition-all cursor-pointer"
                    >
                      <Download className="w-3.5 h-3.5 text-content-secondary" />
                      <span>Download .mmd</span>
                    </button>
                    <button
                      type="button"
                      onClick={() => handleAddMermaid(selectedMermaidType)}
                      className="inline-flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-primary border border-line-strong shadow-xs transition-all cursor-pointer"
                    >
                      <Plus className="w-3.5 h-3.5 text-accent-text" />
                      <span>Add to Visualizer</span>
                    </button>
                  </div>
                </div>
              </div>
            )}

            {/* Tab 2: Build Compiler Targets */}
            {addModalTab === 'build' && (
              <div className="flex-1 min-h-0 grid grid-cols-1 md:grid-cols-12 gap-5">
                {/* Left Selector Column (Clean single-line Polaris list) */}
                <div className="md:col-span-4 h-full flex flex-col space-y-1 overflow-y-auto pr-1">
                  {BUILD_CODE_TARGETS.map((target) => (
                    <button
                      key={target.id}
                      type="button"
                      onClick={() => setSelectedBuildTarget(target.id)}
                      className={`w-full text-left px-3 py-2 rounded-[var(--radius-control)] text-xs transition-colors flex items-center justify-between cursor-pointer ${
                        selectedBuildTarget === target.id
                          ? 'bg-surface-750 text-content-primary font-semibold border border-line-strong shadow-xs'
                          : 'text-content-secondary hover:text-content-primary hover:bg-surface-800/60'
                      }`}
                    >
                      <span>{target.name}</span>
                      <span className="text-micro font-mono text-content-muted">{target.fileName}</span>
                    </button>
                  ))}
                </div>

                {/* Right Preview Column */}
                <div className="md:col-span-8 h-full flex flex-col rounded-[var(--radius-card)] bg-surface-950 border border-line overflow-hidden">
                  <div className="px-3.5 py-2 bg-surface-850 border-b border-line flex items-center justify-between text-xs font-mono text-content-muted shrink-0">
                    <span className="text-content-secondary font-medium">{currentBuildPreview.fileName}</span>
                    <button
                      type="button"
                      onClick={() => {
                        navigator.clipboard.writeText(currentBuildPreview.content);
                        showToast(`Copied ${currentBuildPreview.fileName}`, 'success');
                      }}
                      className="hover:text-content-primary transition-colors flex items-center gap-1.5 cursor-pointer text-xs"
                    >
                      <Copy className="w-3.5 h-3.5" />
                      <span>Copy</span>
                    </button>
                  </div>
                  <pre className="flex-1 min-h-0 p-4 text-xs font-mono text-content-secondary overflow-y-auto select-text whitespace-pre leading-relaxed">
                    {currentBuildPreview.content}
                  </pre>
                  <div className="p-3 bg-surface-850 border-t border-line flex items-center justify-end gap-2 shrink-0">
                    <button
                      type="button"
                      onClick={() => handleAddBuildCode(selectedBuildTarget)}
                      className="inline-flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-primary border border-line-strong shadow-xs transition-all cursor-pointer"
                    >
                      <Plus className="w-3.5 h-3.5 text-accent-text" />
                      <span>Add {currentBuildPreview.fileName} to Repo</span>
                    </button>
                  </div>
                </div>
              </div>
            )}

            {/* Footer */}
            <div className="flex items-center justify-end pt-3 border-t border-line shrink-0">
              <button
                type="button"
                onClick={() => setIsAddModalOpen(false)}
                className="px-4 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-750 hover:bg-surface-700 text-content-primary border border-line-strong shadow-xs transition-all cursor-pointer"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Fullscreen Overlay: Portal directly to document.body with z-[99999] so it cleanly covers everything */}
      {isMounted && isFullscreen && activeFile === 'DIAGRAM' && createPortal(
        <div className="fixed inset-0 z-[99999] bg-surface-950 flex flex-col w-screen h-screen">
          <ReactFlowProvider>
            {/* Solid Fullscreen Header Bar */}
            <div className="h-12 bg-surface-900 border-b border-line px-4 flex items-center justify-between text-xs font-mono shrink-0 select-none shadow-md z-10">
              <div className="flex items-center gap-2">
                <CodeXml className="w-4 h-4 text-accent-text" />
                <span className="font-bold text-content-primary">Visualizer — Fullscreen Canvas</span>
                <span className="text-content-muted hidden sm:inline">({selectedBranch} branch)</span>
              </div>
              <div className="flex items-center gap-2.5">
                <div className="relative hidden md:block">
                  <Search className="w-3 h-3 absolute left-2 top-1/2 -translate-y-1/2 text-content-muted" />
                  <input
                    type="text"
                    placeholder="Search table in canvas..."
                    value={canvasSearchQuery}
                    onChange={(e) => setCanvasSearchQuery(e.target.value)}
                    className="pl-6.5 pr-2 py-1 text-xs font-mono rounded-[var(--radius-control)] bg-surface-800 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden w-48"
                  />
                </div>
                <DiagramControlsBar isFullscreen={true} />
                <button
                  type="button"
                  onClick={() => setIsFullscreen(false)}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-bold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-content-primary border border-line-strong transition-all cursor-pointer shadow-xs ml-1"
                >
                  <X className="w-3.5 h-3.5 text-accent-text" />
                  <span>Exit Fullscreen</span>
                </button>
              </div>
            </div>
            {/* Fullscreen Canvas Viewport */}
            <div className="flex-1 w-full h-full relative bg-surface-950">
              <InnerDiagramCanvas
                nodes={flowNodes}
                edges={flowEdges}
                onNodeClick={(id) => setFocusedTableId(id)}
                focusedTableId={focusedTableId}
              />
            </div>
          </ReactFlowProvider>
        </div>,
        document.body,
      )}
    </div>
  );
}
