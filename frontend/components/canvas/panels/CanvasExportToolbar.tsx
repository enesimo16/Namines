'use client';

import {
  ImageDown,
  FileImage,
  Loader2,
  Pencil,
  Eye,
  Camera,
  Activity,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  FileCode,
  Braces,
  Database,
  FileText,
  Archive,
  GitBranch,
  X,
  Server
} from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useCanvasExport } from '../../../hooks/useCanvasExport';
import { useDbaStore } from '../../../store/useDbaStore';
import { schemaService, scaffolderService } from '../../../services/api';

import { DatabaseSchema } from '../../../types/schema';
import { useRef, useState, useEffect } from 'react';
import * as htmlToImage from 'html-to-image';
import Draggable from 'react-draggable';
import VisionUploadModal from './VisionUploadModal';
import { parseSqlDdl } from '../../../lib/sqlImportParser';
import { toPrismaSchema } from '../../../lib/prismaExporter';
import { useToastStore } from '../../../store/useToastStore';
import { token } from '../../../lib/designTokens';

/** Floating toolbar — sol alt köşe. Export + Edit Mode toggle + DBA drawer toggle. */
export default function CanvasExportToolbar() {
  const { projectName, schema, dbType, isEditMode, toggleEditMode, loadFromSchema } = useSchemaStore();
  const { isExporting, exportAsPng, exportAsJpeg } = useCanvasExport();
  
  // DBA store hooks
  const isPanelOpen = useDbaStore(state => state.isPanelOpen);
  const setIsPanelOpen = useDbaStore(state => state.setIsPanelOpen);
  const issues = useDbaStore(state => state.issues);

  const showToast = useToastStore(s => s.showToast);

  const [isVisionOpen, setIsVisionOpen] = useState(false);
  const [isExportDropdownOpen, setIsExportDropdownOpen] = useState(false);
  const [isLocalExporting, setIsLocalExporting] = useState(false);
  const [isCloudModalOpen, setIsCloudModalOpen] = useState(false);
  const [isSharedHostingModalOpen, setIsSharedHostingModalOpen] = useState(false);
  const [includeBiModule, setIncludeBiModule] = useState(false);
  const [isCollapsed, setIsCollapsed] = useState(true);

  const nodeRef = useRef<HTMLDivElement>(null);
  const sqlFileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const handleOpenVision = () => {
      setIsVisionOpen(true);
      setIsCollapsed(false);
    };
    window.addEventListener('namines:open-vision-modal', handleOpenVision);
    return () => window.removeEventListener('namines:open-vision-modal', handleOpenVision);
  }, []);

  useEffect(() => {
    const handler = () => sqlFileInputRef.current?.click();
    window.addEventListener('namines:import-sql', handler);
    return () => window.removeEventListener('namines:import-sql', handler);
  }, []);

  useEffect(() => {
    const handler = () => exportAsPrisma();
    window.addEventListener('namines:export-prisma', handler);
    return () => window.removeEventListener('namines:export-prisma', handler);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schema, projectName]);

  const handleSqlFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      try {
        const parsed = parseSqlDdl(reader.result as string);
        if (parsed.tables.length === 0) {
          showToast('No tables found in the SQL file.', 'error');
          return;
        }
        loadFromSchema(parsed);
        showToast(`Imported ${parsed.tables.length} table(s) from "${file.name}".`, 'success');
      } catch {
        showToast('Failed to parse SQL file. Check the syntax and try again.', 'error');
      }
    };
    reader.readAsText(file);
    // Reset so the same file can be re-imported
    e.target.value = '';
  };

  const slug = projectName.trim().replace(/\s+/g, '-').toLowerCase() || 'namines-diagram';
  const isCurrentlyExporting = isExporting || isLocalExporting;

  // Custom formats export logic
  const exportAsSvg = async () => {
    const viewport = document.querySelector('.react-flow__viewport') as HTMLElement | null;
    if (!viewport) return;
    setIsLocalExporting(true);
    try {
      const dataUrl = await htmlToImage.toSvg(viewport, {
        backgroundColor: token('--color-bg-base'),
        style: { borderRadius: '0' },
        skipFonts: true,
        fontEmbedCSS: '',
      });
      const link = document.createElement('a');
      link.href = dataUrl;
      link.download = `${slug}.svg`;
      link.click();
    } catch (err) {
      console.error('SVG export hatası:', err);
    } finally {
      setIsLocalExporting(false);
    }
  };

  const exportAsJson = () => {
    setIsLocalExporting(true);
    try {
      const currentSchema: DatabaseSchema = schema || {
        schemaId: '',
        name: projectName,
        tables: [],
        relations: []
      };
      const blob = new Blob([JSON.stringify(currentSchema, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${slug}-schema.json`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('JSON export hatası:', err);
    } finally {
      setIsLocalExporting(false);
    }
  };

  const exportAsPrisma = () => {
    setIsLocalExporting(true);
    try {
      const currentSchema: DatabaseSchema = schema || { schemaId: '', name: projectName, tables: [], relations: [] };
      const prismaText = toPrismaSchema(currentSchema);
      const blob = new Blob([prismaText], { type: 'text/plain' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${slug}.prisma`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('Prisma export error:', err);
    } finally {
      setIsLocalExporting(false);
    }
  };

  const exportForCi = () => {
    setIsLocalExporting(true);
    try {
      const currentSchema: DatabaseSchema = schema || { schemaId: '', name: projectName, tables: [], relations: [] };
      const blob = new Blob([JSON.stringify(currentSchema, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      // Sabit dosya adı — GitHub Action bu adı bekler.
      link.download = 'namines-schema.json';
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('CI snapshot export error:', err);
    } finally {
      setIsLocalExporting(false);
    }
  };

  const exportAsSql = async () => {
    setIsLocalExporting(true);
    try {
      const currentSchema: DatabaseSchema = schema || {
        schemaId: '',
        name: projectName,
        tables: [],
        relations: []
      };
      const sql = await schemaService.compileSql(currentSchema, dbType || 'MSSQL');
      const blob = new Blob([sql], { type: 'text/plain' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${slug}.sql`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('SQL export hatası:', err);
    } finally {
      setIsLocalExporting(false);
    }
  };

  const exportAsPdf = async () => {
    setIsLocalExporting(true);
    try {
      const currentSchema: DatabaseSchema = schema || {
        schemaId: '',
        name: projectName,
        tables: [],
        relations: []
      };
      const pdfBlob = await schemaService.generatePdf(currentSchema, projectName);
      const url = URL.createObjectURL(pdfBlob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${slug}-document.pdf`;
      link.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      console.error('PDF export hatası:', err);
    } finally {
      setIsLocalExporting(false);
    }
  };

  // second-phase/13-DAGITIM-HEDEFLERI.md — Plesk/cPanel (phpMyAdmin) ya da mobil
  // (SQLite) için komut satırı/Docker gerektirmeyen bir paket.
  const exportSharedHosting = async (target: 'MySQL' | 'MariaDB' | 'SQLite') => {
    setIsLocalExporting(true);
    try {
      const currentSchema: DatabaseSchema = schema || {
        schemaId: '',
        name: projectName,
        tables: [],
        relations: []
      };
      const zipBlob = await schemaService.exportSharedHosting(currentSchema, target);
      const url = URL.createObjectURL(zipBlob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${slug}-shared-hosting.zip`;
      link.click();
      URL.revokeObjectURL(url);
      setIsSharedHostingModalOpen(false);
    } catch (err) {
      console.error('Shared hosting export error:', err);
      showToast('Failed to build the shared hosting package.', 'error');
    } finally {
      setIsLocalExporting(false);
    }
  };

  const exportAsFullStackProject = async (provider: 'None' | 'AWS' | 'Azure') => {
    setIsLocalExporting(true);
    try {
      const currentSchema: DatabaseSchema = {
        ...(schema || {
          schemaId: '',
          name: projectName,
          tables: [],
          relations: []
        }),
        cloudProvider: provider,
        includeBiModule: includeBiModule
      };
      const zipBlob = await scaffolderService.exportProject(currentSchema);
      const url = URL.createObjectURL(zipBlob);
      const link = document.createElement('a');
      link.href = url;
      link.download = `${slug}-fullstack-project.zip`;
      link.click();
      URL.revokeObjectURL(url);
      setIsCloudModalOpen(false);
    } catch (err) {
      console.error('Full-stack project export error:', err);
    } finally {
      setIsLocalExporting(false);
    }
  };

  return (
    <>
      {/* Hidden SQL file input */}
      <input
        ref={sqlFileInputRef}
        type="file"
        accept=".sql"
        className="hidden"
        onChange={handleSqlFileChange}
      />

      <Draggable nodeRef={nodeRef} bounds="parent" handle=".drag-handle">
        <div 
          id="canvas-toolbar"
          ref={nodeRef} 
          className="absolute bottom-6 left-6 z-50 flex items-center gap-1.5 p-1.5 rounded-xl bg-gradient-to-r from-surface-700/90 to-surface-600/80 backdrop-blur-md border border-content-primary/12 select-none transition-all duration-300"
          style={{ width: isCollapsed ? '76px' : 'auto' }}
        >
          {/* Drag Handle */}
          <div className="drag-handle cursor-move px-1.5 text-accent-hover/50 hover:text-content-primary transition-colors shrink-0" title="Drag">
            <svg width="10" height="16" viewBox="0 0 10 16" fill="currentColor">
              <circle cx="3" cy="3" r="1.5"/><circle cx="7" cy="3" r="1.5"/>
              <circle cx="3" cy="8" r="1.5"/><circle cx="7" cy="8" r="1.5"/>
              <circle cx="3" cy="13" r="1.5"/><circle cx="7" cy="13" r="1.5"/>
            </svg>
          </div>

          {isCollapsed ? (
            /* COLLAPSED STATE (Compact mode) */
            <button
              onClick={() => setIsCollapsed(false)}
              className="p-2 rounded-lg bg-content-primary/[0.06] hover:bg-content-primary/12 border border-content-primary/12 text-content-primary transition-all shrink-0"
              title="Expand Toolbar"
            >
              <ChevronRight className="w-4 h-4" />
            </button>
          ) : (
            /* EXPANDED STATE */
            <>
              {/* Whiteboard Vision Import Button */}
              <button
                id="canvas-vision-import-btn"
                onClick={() => setIsVisionOpen(true)}
                className="group/btn flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-bold text-content-primary hover:text-content-primary hover:bg-content-primary/[0.06] transition-all border border-transparent hover:border-content-primary/12 relative shadow-sm overflow-hidden shrink-0"
                title="Upload Whiteboard Photo"
                aria-label="Import from Whiteboard"
              >
                <span className="absolute inset-0 bg-accent-hover/5 opacity-50 group-hover/btn:opacity-100 transition-opacity duration-300" />
                <Camera className="w-3.5 h-3.5 text-content-primary relative z-10" />
                <span className="relative z-10">Import</span>
              </button>

              <div className="w-px h-5 bg-content-primary/12 mx-0.5 shrink-0" />

              {/* DBA Button */}
              <button
                id="canvas-dba-inspect-btn"
                onClick={() => setIsPanelOpen(!isPanelOpen)}
                className={`group/btn flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-bold transition-all relative overflow-hidden border shrink-0 ${
                  isPanelOpen 
                    ? 'bg-content-primary/12 text-content-primary border-white/25' 
                    : 'text-success hover:text-success-text hover:bg-success-subtle border-transparent hover:border-success/30'
                }`}
                title="Inspect Database DBA Analysis"
                aria-label="DBA Analysis"
              >
                <svg className="w-3.5 h-3.5 text-success relative z-10 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 2L20 6.5V17.5L12 22L4 17.5V6.5L12 2Z" className="opacity-80" />
                  <circle cx="12" cy="12" r="3" className="fill-current text-content-primary" />
                  <path d="M12 2v7M12 15v7M4 6.5l8 5.5M20 6.5l-8 5.5M4 17.5l8-5.5M20 17.5l-8-5.5" className="opacity-60 text-success" />
                </svg>
                <span className="relative z-10">DBA</span>
                
                {issues.length > 0 && (
                  <span className="relative z-10 flex h-2 w-2">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-danger opacity-75"></span>
                    <span className="relative inline-flex rounded-full h-2 w-2 bg-danger"></span>
                  </span>
                )}
              </button>

              <div className="w-px h-5 bg-content-primary/12 mx-0.5 shrink-0" />

              {/* Edit Mode Toggle */}
              <button
                id="canvas-edit-mode-btn"
                onClick={toggleEditMode}
                className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-medium transition-all duration-300 shrink-0 ${isEditMode ? 'bg-content-primary/12 text-content-primary border border-white/15' : 'text-content-muted hover:text-content-primary hover:bg-white/5 border border-transparent'}`}
                title={isEditMode ? 'Switch to view mode' : 'Switch to edit mode'}
                aria-label={isEditMode ? 'Disable edit mode' : 'Enable edit mode'}
                aria-pressed={isEditMode}
              >
                {isEditMode ? (
                  <>
                    <Eye className="w-3.5 h-3.5" />
                    <span>View</span>
                  </>
                ) : (
                  <>
                    <Pencil className="w-3.5 h-3.5" />
                    <span>Edit</span>
                  </>
                )}
              </button>

              <div className="w-px h-5 bg-content-primary/12 mx-0.5 shrink-0" />

              {/* Export Dropdown Button */}
              <div className="relative shrink-0">
                <button
                  id="canvas-export-dropdown-btn"
                  onClick={() => setIsExportDropdownOpen(!isExportDropdownOpen)}
                  disabled={isCurrentlyExporting}
                  className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-bold transition-all border ${
                    isExportDropdownOpen
                      ? 'bg-white/[0.08] text-content-primary border-white/25 shadow-none'
                      : 'text-content-secondary hover:text-content-primary hover:bg-white/[0.06] border-transparent hover:border-content-primary/12'
                  } disabled:opacity-50 disabled:cursor-not-allowed`}
                  title="Export diagram..."
                  aria-label="Export"
                >
                  {isCurrentlyExporting ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin text-content-primary" />
                  ) : (
                    <ImageDown className="w-3.5 h-3.5" />
                  )}
                  <span>Export</span>
                  <ChevronDown className={`w-3 h-3 transition-transform duration-300 ${isExportDropdownOpen ? 'rotate-180' : ''}`} />
                </button>

                {/* Dropdown Menu (Opens upwards) */}
                {isExportDropdownOpen && (
                  <div className="absolute bottom-full left-0 mb-2 w-52 rounded-xl bg-surface-900/95 border border-content-primary/12 flex flex-col p-1.5 backdrop-blur-xl z-[100] animate-in fade-in duration-200">
                    <div className="px-2.5 py-1.5 text-[9px] font-extrabold text-content-subtle uppercase tracking-wider select-none">
                      Image Export
                    </div>
                    
                    <button
                      onClick={() => {
                        exportAsPng({ fileName: slug });
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-white/[0.06] rounded-lg transition-colors text-left"
                    >
                      <FileImage className="w-3.5 h-3.5 text-content-primary" />
                      <span>PNG Image (.png)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsJpeg({ fileName: slug });
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06] rounded-lg transition-colors text-left"
                    >
                      <FileImage className="w-3.5 h-3.5 text-content-primary" />
                      <span>JPEG Image (.jpg)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsSvg();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-lg transition-colors text-left"
                    >
                      <FileCode className="w-3.5 h-3.5 text-content-muted" />
                      <span>Vector Graphic (.svg)</span>
                    </button>

                    <div className="h-px bg-content-primary/[0.06] my-1" />

                    <div className="px-2.5 py-1.5 text-[9px] font-extrabold text-content-subtle uppercase tracking-wider select-none">
                      Data & Document
                    </div>

                    <button
                      onClick={() => {
                        exportAsSql();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-success hover:bg-success-subtle rounded-lg transition-colors text-left"
                    >
                      <Database className="w-3.5 h-3.5 text-success" />
                      <span>SQL Schema Code (.sql)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsJson();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-danger hover:bg-danger-subtle rounded-lg transition-colors text-left"
                    >
                      <Braces className="w-3.5 h-3.5 text-danger" />
                      <span>Namines Meta Schema (.json)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsPrisma();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-lg transition-colors text-left"
                      title="Export as Prisma ORM schema file"
                    >
                      <Database className="w-3.5 h-3.5 text-content-muted" />
                      <span>Prisma Schema (.prisma)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsPdf();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-lg transition-colors text-left"
                    >
                      <FileText className="w-3.5 h-3.5 text-content-muted" />
                      <span>PDF Technical Report (.pdf)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportForCi();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-lg transition-colors text-left"
                      title="Save as namines-schema.json for GitHub Actions CI diff"
                    >
                      <GitBranch className="w-3.5 h-3.5 text-content-muted" />
                      <span>CI Schema Snapshot (.json)</span>
                    </button>

                    <button
                      onClick={() => {
                        setIsCloudModalOpen(true);
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06] rounded-lg transition-colors text-left border border-transparent hover:border-content-primary/12"
                    >
                      <Archive className="w-3.5 h-3.5 text-content-primary" />
                      <span>Full-Stack Project (.zip)</span>
                    </button>

                    <button
                      onClick={() => {
                        setIsSharedHostingModalOpen(true);
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06] rounded-lg transition-colors text-left border border-transparent hover:border-content-primary/12"
                      title="Plesk/cPanel (phpMyAdmin) or mobile (SQLite) — no CLI, no Docker"
                    >
                      <Server className="w-3.5 h-3.5 text-content-primary" />
                      <span>Shared Hosting / Mobile (.zip)</span>
                    </button>

                    <div className="h-px bg-content-primary/[0.06] my-1" />

                    <div className="px-2.5 py-1.5 text-[9px] font-extrabold text-content-subtle uppercase tracking-wider select-none">
                      Import
                    </div>

                    <button
                      onClick={() => {
                        setIsExportDropdownOpen(false);
                        sqlFileInputRef.current?.click();
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-lg transition-colors text-left"
                      title="Parse a .sql DDL file and load tables onto the canvas"
                    >
                      <FileCode className="w-3.5 h-3.5 text-content-muted" />
                      <span>Import SQL DDL (.sql)</span>
                    </button>
                  </div>
                )}
              </div>

              <div className="w-px h-5 bg-content-primary/12 mx-0.5 shrink-0" />

              {/* Collapse Toggle Button */}
              <button
                onClick={() => setIsCollapsed(true)}
                className="p-2 rounded-lg hover:bg-surface-700 text-content-subtle hover:text-content-secondary transition-all shrink-0"
                title="Collapse Toolbar"
              >
                <ChevronLeft className="w-4 h-4" />
              </button>
            </>
          )}
        </div>
      </Draggable>

      {/* Vision Import Modal */}
      <VisionUploadModal
        isOpen={isVisionOpen}
        onClose={() => setIsVisionOpen(false)}
      />

      {/* Zero-to-Cloud Infrastructure Selector Modal */}
      {isCloudModalOpen && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/60 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="w-[450px] p-6 rounded-2xl bg-gradient-to-b from-surface-800/95 to-surface-900/98 border border-white/15 flex flex-col gap-5 text-sans select-none">
            {/* Title */}
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Archive className="w-5 h-5 text-content-primary animate-pulse" />
                <h3 className="text-md font-extrabold text-content-primary tracking-wide">Zero-to-Cloud Selector</h3>
              </div>
              <button
                onClick={() => setIsCloudModalOpen(false)}
                className="p-1.5 rounded-lg text-content-subtle hover:text-content-secondary hover:bg-surface-700/80 transition-colors cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Description */}
            <p className="text-xs text-content-muted leading-relaxed font-medium">
              The Namines autonomous code generator can integrate Terraform (IaC) code and GitHub Actions CI/CD pipeline files into your zip package, allowing you to deploy your clean-architecture C# API and Next.js SDK project with a single click. Select a cloud provider:
            </p>

            {/* AI-BI Premium Checkbox */}
            <div 
              onClick={() => setIncludeBiModule(!includeBiModule)}
              className={`p-3 rounded-xl border transition-all cursor-pointer flex items-center justify-between ${
                includeBiModule 
                  ? 'bg-content-primary/[0.06] border-white/25 text-content-primary' 
                  : 'bg-surface-800/20 border-surface-600 text-content-muted hover:border-surface-500'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <input
                  type="checkbox"
                  checked={includeBiModule}
                  onChange={() => {}} 
                  className="rounded border-surface-500 bg-surface-800 text-accent focus:ring-accent-hover/30 h-4 w-4"
                />
                <div>
                  <h4 className="text-xs font-bold text-content-primary">Premium: AI Data Analytics (BI) Assistant</h4>
                  <p className="text-[10px] text-content-subtle font-semibold">Embeds a ready-to-use Text-to-SQL chatbot and analytics dashboard.</p>
                </div>
              </div>
            </div>

            {/* Provider Grid */}
            <div className="flex flex-col gap-2.5">
              {/* Option 1: None */}
              <button
                onClick={() => exportAsFullStackProject('None')}
                className="flex items-center justify-between p-3.5 rounded-xl bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-surface-700 flex items-center justify-center text-content-muted group-hover:text-content-primary transition-colors font-bold text-xs">
                    None
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-content-primary">No Cloud Infrastructure</h4>
                    <p className="text-[10px] text-content-subtle font-semibold">ASP.NET Core Web API and React/Next.js Client SDK only</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-content-subtle group-hover:text-content-secondary">Select ➔</span>
              </button>

              {/* Option 2: AWS */}
              <button
                onClick={() => exportAsFullStackProject('AWS')}
                className="flex items-center justify-between p-3.5 rounded-xl bg-accent-subtle/10 hover:bg-accent-subtle/20 border border-accent-subtle/20 hover:border-accent/40 transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-accent/10 flex items-center justify-center text-accent-text font-extrabold text-xs">
                    AWS
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-accent-text">AWS Cloud Infrastructure</h4>
                    <p className="text-[10px] text-content-subtle font-semibold">Terraform (VPC, ECS Cluster, RDS PostgreSQL) + Actions CI/CD</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-accent-text group-hover:text-content-primary">Generate ➔</span>
              </button>

              {/* Option 3: Azure */}
              <button
                onClick={() => exportAsFullStackProject('Azure')}
                className="flex items-center justify-between p-3.5 rounded-xl bg-accent-subtle/10 hover:bg-accent-subtle/20 border border-accent-subtle/20 hover:border-accent/40 transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-white/[0.04] flex items-center justify-center text-content-muted font-extrabold text-xs">
                    AZ
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-content-muted">Azure Cloud Infrastructure</h4>
                    <p className="text-[10px] text-content-subtle font-semibold">Terraform (RG, App Service Plan, Web App Container, SQL Server) + CI/CD</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-content-muted group-hover:text-content-muted">Generate ➔</span>
              </button>
            </div>

            {/* Note */}
            <div className="text-[10px] text-content-subtle text-center font-semibold mt-1">
              Terraform codes are structured according to AWS/Azure security and cost best practices.
            </div>
          </div>
        </div>
      )}

      {/* Shared Hosting / Mobile Selector Modal — second-phase/13-DAGITIM-HEDEFLERI.md */}
      {isSharedHostingModalOpen && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/60 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="w-[420px] p-6 rounded-2xl bg-gradient-to-b from-surface-800/95 to-surface-900/98 border border-white/15 flex flex-col gap-5 text-sans select-none">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Server className="w-5 h-5 text-content-primary" />
                <h3 className="text-md font-extrabold text-content-primary tracking-wide">Shared Hosting / Mobile</h3>
              </div>
              <button
                onClick={() => setIsSharedHostingModalOpen(false)}
                className="p-1.5 rounded-lg text-content-subtle hover:text-content-secondary hover:bg-surface-700/80 transition-colors cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <p className="text-xs text-content-muted leading-relaxed font-medium">
              No CLI, no Docker. A .sql file (or a pre-built .db for mobile) plus a
              step-by-step README — Namines only produces the file, you upload it
              yourself via phpMyAdmin or bundle it into your app.
            </p>

            <div className="flex flex-col gap-2.5">
              <button
                onClick={() => exportSharedHosting('MySQL')}
                disabled={isCurrentlyExporting}
                className="flex items-center justify-between p-3.5 rounded-xl bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer disabled:opacity-50"
              >
                <div>
                  <h4 className="text-xs font-bold text-content-primary">MySQL (Plesk / cPanel / DirectAdmin)</h4>
                  <p className="text-[10px] text-content-subtle font-semibold">phpMyAdmin-ready SQL, utf8mb4, FOREIGN_KEY_CHECKS handled</p>
                </div>
                <span className="text-[10px] font-bold text-content-subtle group-hover:text-content-secondary">Build ➔</span>
              </button>

              <button
                onClick={() => exportSharedHosting('MariaDB')}
                disabled={isCurrentlyExporting}
                className="flex items-center justify-between p-3.5 rounded-xl bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer disabled:opacity-50"
              >
                <div>
                  <h4 className="text-xs font-bold text-content-primary">MariaDB</h4>
                  <p className="text-[10px] text-content-subtle font-semibold">Same package, MariaDB dialect</p>
                </div>
                <span className="text-[10px] font-bold text-content-subtle group-hover:text-content-secondary">Build ➔</span>
              </button>

              <button
                onClick={() => exportSharedHosting('SQLite')}
                disabled={isCurrentlyExporting}
                className="flex items-center justify-between p-3.5 rounded-xl bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer disabled:opacity-50"
              >
                <div>
                  <h4 className="text-xs font-bold text-content-primary">Mobile (SQLite)</h4>
                  <p className="text-[10px] text-content-subtle font-semibold">A real, pre-built .db file to embed in your app</p>
                </div>
                <span className="text-[10px] font-bold text-content-subtle group-hover:text-content-secondary">Build ➔</span>
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
