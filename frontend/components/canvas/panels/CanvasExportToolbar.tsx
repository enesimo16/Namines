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
  X
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

/** Floating toolbar — sol alt köşe. Export + Edit Mode toggle + DBA drawer toggle. */
export default function CanvasExportToolbar() {
  const { projectName, schema, dbType, isEditMode, toggleEditMode } = useSchemaStore();
  const { isExporting, exportAsPng, exportAsJpeg } = useCanvasExport();
  
  // DBA store hooks
  const isPanelOpen = useDbaStore(state => state.isPanelOpen);
  const setIsPanelOpen = useDbaStore(state => state.setIsPanelOpen);
  const issues = useDbaStore(state => state.issues);

  const [isVisionOpen, setIsVisionOpen] = useState(false);
  const [isExportDropdownOpen, setIsExportDropdownOpen] = useState(false);
  const [isLocalExporting, setIsLocalExporting] = useState(false);
  const [isCloudModalOpen, setIsCloudModalOpen] = useState(false);
  const [includeBiModule, setIncludeBiModule] = useState(false);
  const [isCollapsed, setIsCollapsed] = useState(true);
  
  const nodeRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const handleOpenVision = () => {
      setIsVisionOpen(true);
      setIsCollapsed(false);
    };
    window.addEventListener('namines:open-vision-modal', handleOpenVision);
    return () => window.removeEventListener('namines:open-vision-modal', handleOpenVision);
  }, []);

  const slug = projectName.trim().replace(/\s+/g, '-').toLowerCase() || 'namines-diagram';
  const isCurrentlyExporting = isExporting || isLocalExporting;

  // Custom formats export logic
  const exportAsSvg = async () => {
    const viewport = document.querySelector('.react-flow__viewport') as HTMLElement | null;
    if (!viewport) return;
    setIsLocalExporting(true);
    try {
      const dataUrl = await htmlToImage.toSvg(viewport, {
        backgroundColor: '#09090b',
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
      <Draggable nodeRef={nodeRef} bounds="parent" handle=".drag-handle">
        <div 
          id="canvas-toolbar"
          ref={nodeRef} 
          className="absolute bottom-6 left-6 z-50 flex items-center gap-1.5 p-1.5 rounded-xl bg-gradient-to-r from-[#0F172A]/90 to-[#1E293B]/80 backdrop-blur-md border border-indigo-500/20 shadow-[0_0_20px_rgba(59,130,246,0.15)] select-none transition-all duration-300"
          style={{ width: isCollapsed ? '76px' : 'auto' }}
        >
          {/* Drag Handle */}
          <div className="drag-handle cursor-move px-1.5 text-indigo-500/50 hover:text-indigo-400 transition-colors shrink-0" title="Drag">
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
              className="p-2 rounded-lg bg-indigo-500/10 hover:bg-indigo-500/20 border border-indigo-500/20 text-indigo-300 transition-all shrink-0"
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
                className="group/btn flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-bold text-indigo-300 hover:text-indigo-100 hover:bg-indigo-500/10 transition-all border border-transparent hover:border-indigo-500/20 relative shadow-sm overflow-hidden shrink-0"
                title="Upload Whiteboard Photo"
                aria-label="Import from Whiteboard"
              >
                <span className="absolute inset-0 bg-indigo-500/5 opacity-50 group-hover/btn:opacity-100 transition-opacity duration-300" />
                <Camera className="w-3.5 h-3.5 text-indigo-400 relative z-10" />
                <span className="relative z-10">Import</span>
              </button>

              <div className="w-px h-5 bg-indigo-500/20 mx-0.5 shrink-0" />

              {/* DBA Button */}
              <button
                id="canvas-dba-inspect-btn"
                onClick={() => setIsPanelOpen(!isPanelOpen)}
                className={`group/btn flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-bold transition-all relative overflow-hidden border shrink-0 ${
                  isPanelOpen 
                    ? 'bg-indigo-500/20 text-indigo-300 border-indigo-500/40 shadow-[0_0_15px_rgba(99,102,241,0.25)]' 
                    : 'text-emerald-400 hover:text-emerald-200 hover:bg-emerald-500/10 border-transparent hover:border-emerald-500/20'
                }`}
                title="Inspect Database DBA Analysis"
                aria-label="DBA Analysis"
              >
                <svg className="w-3.5 h-3.5 text-emerald-400 relative z-10 shrink-0" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 2L20 6.5V17.5L12 22L4 17.5V6.5L12 2Z" className="opacity-80" />
                  <circle cx="12" cy="12" r="3" className="fill-current text-cyan-400" />
                  <path d="M12 2v7M12 15v7M4 6.5l8 5.5M20 6.5l-8 5.5M4 17.5l8-5.5M20 17.5l-8-5.5" className="opacity-60 text-emerald-400" />
                </svg>
                <span className="relative z-10">DBA</span>
                
                {issues.length > 0 && (
                  <span className="relative z-10 flex h-2 w-2">
                    <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-rose-400 opacity-75"></span>
                    <span className="relative inline-flex rounded-full h-2 w-2 bg-rose-500"></span>
                  </span>
                )}
              </button>

              <div className="w-px h-5 bg-indigo-500/20 mx-0.5 shrink-0" />

              {/* Edit Mode Toggle */}
              <button
                id="canvas-edit-mode-btn"
                onClick={toggleEditMode}
                className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-medium transition-all duration-300 shrink-0 ${isEditMode ? 'bg-indigo-500/20 text-indigo-300 border border-indigo-500/30 shadow-[0_0_15px_rgba(99,102,241,0.25)]' : 'text-zinc-400 hover:text-zinc-200 hover:bg-white/5 border border-transparent'}`}
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

              <div className="w-px h-5 bg-indigo-500/20 mx-0.5 shrink-0" />

              {/* Export Dropdown Button */}
              <div className="relative shrink-0">
                <button
                  id="canvas-export-dropdown-btn"
                  onClick={() => setIsExportDropdownOpen(!isExportDropdownOpen)}
                  disabled={isCurrentlyExporting}
                  className={`flex items-center gap-2 px-3 py-2 rounded-lg text-xs font-bold transition-all border ${
                    isExportDropdownOpen
                      ? 'bg-cyan-500/20 text-cyan-300 border-cyan-500/40 shadow-[0_0_15px_rgba(6,182,212,0.25)]'
                      : 'text-zinc-300 hover:text-cyan-300 hover:bg-cyan-500/10 border-transparent hover:border-cyan-500/20'
                  } disabled:opacity-50 disabled:cursor-not-allowed`}
                  title="Export diagram..."
                  aria-label="Export"
                >
                  {isCurrentlyExporting ? (
                    <Loader2 className="w-3.5 h-3.5 animate-spin text-cyan-400" />
                  ) : (
                    <ImageDown className="w-3.5 h-3.5" />
                  )}
                  <span>Export</span>
                  <ChevronDown className={`w-3 h-3 transition-transform duration-300 ${isExportDropdownOpen ? 'rotate-180' : ''}`} />
                </button>

                {/* Dropdown Menu (Opens upwards) */}
                {isExportDropdownOpen && (
                  <div className="absolute bottom-full left-0 mb-2 w-52 rounded-xl bg-zinc-950/95 border border-indigo-500/20 shadow-[0_0_25px_rgba(99,102,241,0.3)] flex flex-col p-1.5 backdrop-blur-xl z-[100] animate-in fade-in duration-200">
                    <div className="px-2.5 py-1.5 text-[9px] font-extrabold text-zinc-500 uppercase tracking-wider select-none">
                      Image Export
                    </div>
                    
                    <button
                      onClick={() => {
                        exportAsPng({ fileName: slug });
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-zinc-300 hover:text-cyan-300 hover:bg-cyan-500/10 rounded-lg transition-colors text-left"
                    >
                      <FileImage className="w-3.5 h-3.5 text-cyan-400" />
                      <span>PNG Image (.png)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsJpeg({ fileName: slug });
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-zinc-300 hover:text-indigo-300 hover:bg-indigo-500/10 rounded-lg transition-colors text-left"
                    >
                      <FileImage className="w-3.5 h-3.5 text-indigo-400" />
                      <span>JPEG Image (.jpg)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsSvg();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-zinc-300 hover:text-amber-400 hover:bg-amber-500/10 rounded-lg transition-colors text-left"
                    >
                      <FileCode className="w-3.5 h-3.5 text-amber-400" />
                      <span>Vector Graphic (.svg)</span>
                    </button>

                    <div className="h-px bg-indigo-500/10 my-1" />

                    <div className="px-2.5 py-1.5 text-[9px] font-extrabold text-zinc-500 uppercase tracking-wider select-none">
                      Data & Document
                    </div>

                    <button
                      onClick={() => {
                        exportAsSql();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-zinc-300 hover:text-emerald-400 hover:bg-emerald-500/10 rounded-lg transition-colors text-left"
                    >
                      <Database className="w-3.5 h-3.5 text-emerald-400" />
                      <span>SQL Schema Code (.sql)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsJson();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-zinc-300 hover:text-rose-400 hover:bg-rose-500/10 rounded-lg transition-colors text-left"
                    >
                      <Braces className="w-3.5 h-3.5 text-rose-400" />
                      <span>Namines Meta Schema (.json)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsPdf();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-zinc-300 hover:text-sky-400 hover:bg-sky-500/10 rounded-lg transition-colors text-left"
                    >
                      <FileText className="w-3.5 h-3.5 text-sky-400" />
                      <span>PDF Technical Report (.pdf)</span>
                    </button>

                    <button
                      onClick={() => {
                        setIsCloudModalOpen(true);
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-zinc-300 hover:text-indigo-400 hover:bg-indigo-500/10 rounded-lg transition-colors text-left border border-transparent hover:border-indigo-500/20"
                    >
                      <Archive className="w-3.5 h-3.5 text-indigo-400" />
                      <span>Full-Stack Project (.zip)</span>
                    </button>
                  </div>
                )}
              </div>

              <div className="w-px h-5 bg-indigo-500/20 mx-0.5 shrink-0" />

              {/* Collapse Toggle Button */}
              <button
                onClick={() => setIsCollapsed(true)}
                className="p-2 rounded-lg hover:bg-zinc-800 text-zinc-500 hover:text-zinc-300 transition-all shrink-0"
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
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/60 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="w-[450px] p-6 rounded-2xl bg-gradient-to-b from-zinc-900/95 to-zinc-950/98 border border-indigo-500/30 shadow-[0_0_35px_rgba(99,102,241,0.25)] flex flex-col gap-5 text-sans select-none">
            {/* Title */}
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Archive className="w-5 h-5 text-indigo-400 animate-pulse" />
                <h3 className="text-md font-extrabold text-white tracking-wide">Zero-to-Cloud Selector</h3>
              </div>
              <button
                onClick={() => setIsCloudModalOpen(false)}
                className="p-1.5 rounded-lg text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800/80 transition-colors cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Description */}
            <p className="text-xs text-zinc-400 leading-relaxed font-medium">
              The Namines autonomous code generator can integrate Terraform (IaC) code and GitHub Actions CI/CD pipeline files into your zip package, allowing you to deploy your clean-architecture C# API and Next.js SDK project with a single click. Select a cloud provider:
            </p>

            {/* AI-BI Premium Checkbox */}
            <div 
              onClick={() => setIncludeBiModule(!includeBiModule)}
              className={`p-3 rounded-xl border transition-all cursor-pointer flex items-center justify-between ${
                includeBiModule 
                  ? 'bg-indigo-500/10 border-indigo-500/40 shadow-[0_0_12px_rgba(99,102,241,0.15)] text-indigo-300' 
                  : 'bg-zinc-900/20 border-zinc-800 text-zinc-400 hover:border-zinc-700'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <input
                  type="checkbox"
                  checked={includeBiModule}
                  onChange={() => {}} 
                  className="rounded border-zinc-700 bg-zinc-900 text-indigo-600 focus:ring-indigo-500/30 h-4 w-4"
                />
                <div>
                  <h4 className="text-xs font-bold text-zinc-200">Premium: AI Data Analytics (BI) Assistant</h4>
                  <p className="text-[10px] text-zinc-500 font-semibold">Embeds a ready-to-use Text-to-SQL chatbot and analytics dashboard.</p>
                </div>
              </div>
            </div>

            {/* Provider Grid */}
            <div className="flex flex-col gap-2.5">
              {/* Option 1: None */}
              <button
                onClick={() => exportAsFullStackProject('None')}
                className="flex items-center justify-between p-3.5 rounded-xl bg-zinc-900/40 hover:bg-zinc-800/40 border border-zinc-800 hover:border-zinc-700 transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-zinc-800 flex items-center justify-center text-zinc-400 group-hover:text-zinc-200 transition-colors font-bold text-xs">
                    None
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-zinc-200">No Cloud Infrastructure</h4>
                    <p className="text-[10px] text-zinc-500 font-semibold">ASP.NET Core Web API and React/Next.js Client SDK only</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-zinc-500 group-hover:text-zinc-300">Select ➔</span>
              </button>

              {/* Option 2: AWS */}
              <button
                onClick={() => exportAsFullStackProject('AWS')}
                className="flex items-center justify-between p-3.5 rounded-xl bg-orange-950/10 hover:bg-orange-950/20 border border-orange-900/20 hover:border-orange-500/40 shadow-[0_0_15px_rgba(249,115,22,0.03)] hover:shadow-[0_0_20px_rgba(249,115,22,0.1)] transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-orange-500/10 flex items-center justify-center text-orange-500 font-extrabold text-xs">
                    AWS
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-orange-400">AWS Cloud Infrastructure</h4>
                    <p className="text-[10px] text-zinc-500 font-semibold">Terraform (VPC, ECS Cluster, RDS PostgreSQL) + Actions CI/CD</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-orange-500 group-hover:text-orange-400">Generate ➔</span>
              </button>

              {/* Option 3: Azure */}
              <button
                onClick={() => exportAsFullStackProject('Azure')}
                className="flex items-center justify-between p-3.5 rounded-xl bg-sky-950/10 hover:bg-sky-950/20 border border-sky-900/20 hover:border-sky-500/40 shadow-[0_0_15px_rgba(14,165,233,0.03)] hover:shadow-[0_0_20px_rgba(14,165,233,0.1)] transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-lg bg-sky-500/10 flex items-center justify-center text-sky-500 font-extrabold text-xs">
                    AZ
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-sky-400">Azure Cloud Infrastructure</h4>
                    <p className="text-[10px] text-zinc-500 font-semibold">Terraform (RG, App Service Plan, Web App Container, SQL Server) + CI/CD</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-sky-500 group-hover:text-sky-400">Generate ➔</span>
              </button>
            </div>

            {/* Note */}
            <div className="text-[10px] text-zinc-500 text-center font-semibold mt-1">
              Terraform codes are structured according to AWS/Azure security and cost best practices.
            </div>
          </div>
        </div>
      )}
    </>
  );
}
