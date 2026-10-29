'use client';

import React from 'react';
import {
  FileImage,
  Pencil,
  Eye,
  Camera,
  FileCode,
  Database,
  FileText,
  Archive,
  GitBranch,
  X,
  Server,
  Wrench,
  ShieldCheck,
  BookOpen,
  Settings,
  Table,
  History,
  Network,
  FileCode2,
  Download,
  ChevronRight
} from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useCanvasExport } from '../../../hooks/useCanvasExport';
import { useDbaStore } from '../../../store/useDbaStore';
import { schemaService, scaffolderService } from '../../../services/api';

import { DatabaseSchema } from '../../../types/schema';
import { useRef, useState, useEffect } from 'react';
import * as htmlToImage from 'html-to-image';
import VisionUploadModal from './VisionUploadModal';
import { parseSqlDdl } from '../../../lib/sqlImportParser';
import { toPrismaSchema } from '../../../lib/prismaExporter';
import { useToastStore } from '../../../store/useToastStore';
import { token } from '../../../lib/designTokens';
import FeatureGuard from '../../common/FeatureGuard';
import { useFeatureStore } from '../../../store/useFeatureStore';

const TOOLKIT_FEATURE_KEYS = [
  'tool_dba_advisor',
  'tool_schema_explain',
  'tool_reverse_code',
  'tool_live_db',
  'tool_gateway_viewer',
  'tool_migration_engine',
  'tool_virtual_relations',
  'tool_vision_ocr',
  'tool_sql_import',
  'tool_edit_mode',
  'tool_export_hub',
];

/** Sol-alt "toolkit" FAB'ı — canvas'a yerel araçlar (Import/DBA/Edit/Export) ve
 *  üst navbardan taşınan az-kullanılan 7 araç (Explain/Settings/DB Import/
 *  Browse Data/Migration/Cross-DB/Code Import) burada, tek dikey speed-dial'da. */
export default function CanvasExportToolbar() {
  const { projectName, schema, dbType, isEditMode, toggleEditMode, loadFromSchema } = useSchemaStore();
  const { isExporting, exportAsPng } = useCanvasExport();
  // `isFeatureEnabled` kararlı bir fonksiyon: ona abone olmak bayraklar gelince
  // yeniden render tetiklemezdi. Sayıyı seçici içinde hesaplayıp ona abone ol.
  const enabledToolsCount = useFeatureStore(
    (s) => TOOLKIT_FEATURE_KEYS.filter((k) => s.isFeatureEnabled(k)).length,
  );

  // DBA store hooks
  const isPanelOpen = useDbaStore(state => state.isPanelOpen);
  const setIsPanelOpen = useDbaStore(state => state.setIsPanelOpen);
  const issues = useDbaStore(state => state.issues);

  const showToast = useToastStore(s => s.showToast);

  const [isVisionOpen, setIsVisionOpen] = useState(false);
  const [isLocalExporting, setIsLocalExporting] = useState(false);
  const [isCloudModalOpen, setIsCloudModalOpen] = useState(false);
  const [isSharedHostingModalOpen, setIsSharedHostingModalOpen] = useState(false);
  const [includeBiModule, setIncludeBiModule] = useState(false);
  const [isExportFlyoutOpen, setIsExportFlyoutOpen] = useState(false);
  // Dikey speed-dial: kapalıyken tek yuvarlak, açılınca küçük yuvarlaklar
  // YUKARI doğru açılıyor (bkz. Toolbar Redesign Options artifact, seçenek 1 —
  // kullanıcı onayı). Eskiden yatayda genişleyen bir div + onun altında ayrı
  // bir "Export" dropdown'ı vardı; ikisi de artık aynı dikey desende.
  const [isDialOpen, setIsDialOpen] = useState(false);

  const dialRef = useRef<HTMLDivElement>(null);
  const sqlFileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const handleOpenVision = () => {
      setIsVisionOpen(true);
      setIsDialOpen(true);
    };
    window.addEventListener('namines:open-vision-modal', handleOpenVision);
    return () => window.removeEventListener('namines:open-vision-modal', handleOpenVision);
  }, []);

  // Event listener for automated interactive tour
  useEffect(() => {
    const handleOpenToolkit = () => setIsDialOpen(true);
    const handleCloseToolkit = () => {
      setIsDialOpen(false);
      setIsExportFlyoutOpen(false);
    };
    const handleOpenExportFlyout = () => {
      setIsDialOpen(true);
      setIsExportFlyoutOpen(true);
    };

    window.addEventListener('namines:open-toolkit', handleOpenToolkit);
    window.addEventListener('namines:close-toolkit', handleCloseToolkit);
    window.addEventListener('namines:open-export-flyout', handleOpenExportFlyout);

    return () => {
      window.removeEventListener('namines:open-toolkit', handleOpenToolkit);
      window.removeEventListener('namines:close-toolkit', handleCloseToolkit);
      window.removeEventListener('namines:open-export-flyout', handleOpenExportFlyout);
    };
  }, []);

  // Dial dışına tıklayınca kapan — Tour aktifken kapanmasın
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (document.body.getAttribute('data-tour-active') === 'true') return;
      if (dialRef.current && !dialRef.current.contains(e.target as Node)) {
        setIsDialOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  useEffect(() => {
    const handler = () => sqlFileInputRef.current?.click();
    window.addEventListener('namines:import-sql', handler);
    return () => window.removeEventListener('namines:import-sql', handler);
  }, []);

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

  /**
   * `namines:export-prisma` olayini dinler.
   *
   * **Bu efekt neden BURADA, dosyanin en ustunde DEGIL:** `exportAsPrisma`
   * asagida `const` ile tanimli. Efekt yukarida dururken TDZ ihlali vardi --
   * calisma zamaninda patlamiyordu (kapanis, mount'tan sonra cagriliyor) ama
   * fonksiyon yukari tasindigi an ya da efekt senkron cagrildigi an
   * `ReferenceError` verecekti. Bildirimden sonraya tasimak ihtimali yok ediyor.
   */
  useEffect(() => {
    const handler = () => exportAsPrisma();
    window.addEventListener('namines:export-prisma', handler);
    return () => window.removeEventListener('namines:export-prisma', handler);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schema, projectName]);

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

      {/* Dikey speed-dial "toolkit" — bkz. Toolbar Redesign Options artifact,
          seçenek 1 (kullanıcı onayı). Eskiden yatayda genişleyen bir şerit +
          onun İÇİNDE ayrı bir Export dropdown vardı; şimdi kapalıyken 40px'lik
          tek daire, açılınca 11 araç YUKARI doğru açılıyor.

          <b>İlk sürüm 11'i TEK dikey sütunda dizdi — "çok fazla oldular" geri
          bildirimi geldi.</b> Uzun bir liste, sayıca aynı kalsa da GÖRSEL
          olarak "bir sürü şey" hissi veriyordu. Çözüm sayıyı azaltmak değil
          (hepsi gerçek özellik), YERLEŞİMİ değiştirmek: tek sütun yerine iki
          küçük 4-sütunlu "pod" (grid) — aynı 11 araç artık 2 kompakt küme
          gibi okunuyor, tek uzun şerit gibi değil. Yükseklik ~500px'ten
          ~160px'e indi.

          Pod 1 (üstte, FAB'dan uzak) = üst navbardan taşınan 7 az-kullanılan
          araç. Pod 2 (altta, FAB'a yakın) = tuval-yerel 4 araç (Import/DBA/
          Edit/Export). `id="canvas-toolbar"` sabit kaldı — TourOverlay.tsx bu
          ID'yi hedefliyor. */}
      <div id="canvas-toolbar" ref={dialRef} className="fixed bottom-[72px] left-3 lg:bottom-6 lg:left-6 z-50 select-none">
        {/* ── Zengin Shopify Polaris Toolkit Menü Çekmecesi (Scrollsuz, hizalı, yan flyout destekli) ── */}
        <div
          className={`absolute bottom-[50px] left-0 w-[calc(100vw-24px)] sm:w-[340px] flex flex-col rounded-[var(--radius-modal)] bg-surface-900/98 border border-line-strong backdrop-blur-2xl shadow-2xl overflow-visible z-[100] transition-all duration-200 ${
            isDialOpen
              ? 'opacity-100 scale-100 translate-y-0 pointer-events-auto'
              : 'opacity-0 scale-95 translate-y-2 pointer-events-none'
          }`}
        >
          {/* Header with Title */}
          <div className="flex items-center justify-between px-3 py-2 border-b border-line bg-surface-800/80 rounded-t-[var(--radius-modal)]">
            <span className="text-[11px] font-bold text-content-primary uppercase tracking-wider font-mono">
              Toolkit
            </span>
            <button
              onClick={() => setIsDialOpen(false)}
              className="tap-44 text-content-muted hover:text-content-primary transition-colors p-1 rounded-full hover:bg-surface-700 cursor-pointer"
              title="Close toolkit"
              aria-label="Close toolkit"
            >
              <X className="w-3.5 h-3.5" />
            </button>
          </div>

          {/* Araç Listesi — Kompakt, tek hizada, dikey scroll gerektirmeyen yapı */}
          <div className="p-1.5 space-y-1 max-h-[calc(100dvh-150px)] sm:max-h-none overflow-y-auto sm:overflow-visible">
            {/* DBA Sorun Analizi */}
            <FeatureGuard featureKey="tool_dba_advisor">
              <button
                id="canvas-dba-inspect-btn"
                onClick={() => {
                  setIsPanelOpen(!isPanelOpen);
                  setIsDialOpen(false);
                }}
                className={`w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] border transition-all text-left cursor-pointer group ${
                  isPanelOpen
                    ? 'bg-surface-700 border-line-strong'
                    : 'bg-surface-800/50 hover:bg-surface-800 border-line hover:border-line-strong'
                }`}
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <ShieldCheck className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">DBA Health & Optimization</div>
                    <div className="text-micro text-content-muted truncate">Index audit, keys, security & AI fixes</div>
                  </div>
                </div>
                {issues.length > 0 ? (
                  <span className="shrink-0 px-1.5 py-0.5 rounded-full text-micro font-bold bg-danger-subtle text-danger-text border border-danger/30">
                    {issues.length}
                  </span>
                ) : (
                  <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                    Open ➔
                  </span>
                )}
              </button>
            </FeatureGuard>

            {/* AI ile Şemayı Açıkla */}
            <FeatureGuard featureKey="tool_schema_explain">
              <button
                id="canvas-explain-schema-btn"
                onClick={() => {
                  window.dispatchEvent(new CustomEvent('namines:explain-schema'));
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <BookOpen className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">Explain Schema with AI</div>
                    <div className="text-micro text-content-muted truncate">Natural language summary of tables</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  Run ➔
                </span>
              </button>
            </FeatureGuard>

            {/* Koddan Şema Çıkar */}
            <FeatureGuard featureKey="tool_reverse_code">
              <button
                id="canvas-code-import-btn"
                onClick={() => {
                  window.dispatchEvent(new CustomEvent('namines:open-code-import'));
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <FileCode2 className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">Reverse Engineer from Code</div>
                    <div className="text-micro text-content-muted truncate">Import from C# EF, TS, Python</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  Import ➔
                </span>
              </button>
            </FeatureGuard>


            {/* Canlı DB Bağlantısı */}
            <FeatureGuard featureKey="tool_live_db">
              <button
                id="canvas-db-connect-btn"
                onClick={() => {
                  window.dispatchEvent(new CustomEvent('namines:open-db-connect'));
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <Database className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">Connect Live Database</div>
                    <div className="text-micro text-content-muted truncate">Postgres, MySQL, SQLite introspection</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  Connect ➔
                </span>
              </button>
            </FeatureGuard>

            {/* Canlı Verileri İncele (Gateway) */}
            <FeatureGuard featureKey="tool_gateway_viewer">
              <button
                id="canvas-gateway-btn"
                onClick={() => {
                  window.dispatchEvent(new CustomEvent('namines:open-gateway'));
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <Table className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">Browse Table Records</div>
                    <div className="text-micro text-content-muted truncate">Read-only live data gateway viewer</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  View ➔
                </span>
              </button>
            </FeatureGuard>

            {/* Migrasyon Motoru */}
            <FeatureGuard featureKey="tool_migration_engine">
              <button
                id="canvas-migration-btn"
                onClick={() => {
                  window.dispatchEvent(new CustomEvent('namines:open-migration'));
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <History className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">DDL Migration Engine</div>
                    <div className="text-micro text-content-muted truncate">Generate versions & migration diffs</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  Manage ➔
                </span>
              </button>
            </FeatureGuard>

            {/* Çapraz DB İlişkileri */}
            <FeatureGuard featureKey="tool_virtual_relations">
              <button
                id="canvas-cross-db-btn"
                onClick={() => {
                  window.dispatchEvent(new CustomEvent('namines:open-cross-db'));
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <Network className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">Cross-Database Virtual Relations</div>
                    <div className="text-micro text-content-muted truncate">Connect tables across disparate engines</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  Link ➔
                </span>
              </button>
            </FeatureGuard>

            {/* Vision / Görselden Şema */}
            <FeatureGuard featureKey="tool_vision_ocr">
              <button
                id="canvas-vision-import-btn"
                onClick={() => {
                  setIsVisionOpen(true);
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <Camera className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">Import from Diagram Image</div>
                    <div className="text-micro text-content-muted truncate">OCR & Vision from whiteboard or ERD</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  Upload ➔
                </span>
              </button>
            </FeatureGuard>

            {/* SQL DDL Dosyası Yükle */}
            <FeatureGuard featureKey="tool_sql_import">
              <button
                id="canvas-sql-import-btn"
                onClick={() => {
                  sqlFileInputRef.current?.click();
                  setIsDialOpen(false);
                }}
                className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-800 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    <FileCode className="w-3.5 h-3.5" />
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">Import SQL DDL (.sql)</div>
                    <div className="text-micro text-content-muted truncate">Load CREATE TABLE script to canvas</div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                  File ➔
                </span>
              </button>
            </FeatureGuard>

            {/* Düzenleme Modu */}
            <FeatureGuard featureKey="tool_edit_mode">
              <button
                id="canvas-edit-mode-btn"
                onClick={toggleEditMode}
                className={`w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] border transition-all text-left cursor-pointer group ${
                  isEditMode
                    ? 'bg-surface-700 border-line-strong'
                    : 'bg-surface-800/50 hover:bg-surface-800 border-line hover:border-line-strong'
                }`}
              >
                <div className="flex items-center gap-2.5 min-w-0">
                  <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                    {isEditMode ? <Eye className="w-3.5 h-3.5" /> : <Pencil className="w-3.5 h-3.5" />}
                  </div>
                  <div className="min-w-0">
                    <div className="text-xs font-bold text-content-primary truncate">
                      {isEditMode ? 'Visual Edit Mode (Active)' : 'Protected View Mode'}
                    </div>
                    <div className="text-micro text-content-muted truncate">
                      {isEditMode ? 'Click columns to modify types' : 'Click to enable editing'}
                    </div>
                  </div>
                </div>
                <span className="shrink-0 text-micro font-semibold px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-700 text-content-primary border border-line">
                  {isEditMode ? 'Active' : 'Toggle'}
                </span>
              </button>
            </FeatureGuard>

            {/* ── Tek Satır Dışa Aktarım & Dağıtım (Üzerine Gelince / Tıklayınca Yanda Açılan Flyout) ── */}
            <FeatureGuard featureKey="tool_export_hub">
              <div
                id="canvas-export-dropdown-btn"
                className="relative group/export pt-1 border-t border-line"
                onMouseEnter={() => setIsExportFlyoutOpen(true)}
                onMouseLeave={() => setIsExportFlyoutOpen(false)}
              >
                <button
                  type="button"
                  onClick={() => setIsExportFlyoutOpen(v => !v)}
                  className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/70 hover:bg-surface-700 border border-line-strong transition-all text-left cursor-pointer"
                >
                  <div className="flex items-center gap-2.5 min-w-0">
                    <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                      <Download className="w-3.5 h-3.5" />
                    </div>
                    <div className="min-w-0">
                      <div className="text-xs font-bold text-content-primary truncate">Export & Package Schema</div>
                      <div className="text-micro text-content-muted truncate">PNG, SVG, SQL, Prisma, PDF, Full-Stack .zip</div>
                    </div>
                  </div>
                  <ChevronRight className={`w-4 h-4 text-content-muted transition-transform shrink-0 ${isExportFlyoutOpen ? 'rotate-90 sm:rotate-0 sm:translate-x-0.5 text-content-primary' : ''}`} />
                </button>

                {/* Yanda Açılan (Side Flyout) Alt Menü */}
                <div
                  className={`${
                    isExportFlyoutOpen
                      ? 'block sm:opacity-100 sm:scale-100 sm:pointer-events-auto'
                      : 'hidden sm:block sm:opacity-0 sm:scale-95 sm:pointer-events-none'
                  } mt-1.5 sm:mt-0 sm:absolute sm:left-full sm:bottom-0 sm:pl-2 z-[110] transition-all duration-150`}
                >
                  <div className="w-full sm:w-[300px] p-2 rounded-[var(--radius-modal)] bg-surface-900/98 border border-line-strong shadow-2xl backdrop-blur-2xl space-y-1.5">
                    <div className="px-1.5 py-0.5 text-[10px] font-bold uppercase tracking-wider text-content-muted">
                      Quick Exports
                    </div>
                    <div className="grid grid-cols-2 gap-1.5">
                      <button
                        id="export-png-btn"
                        onClick={() => {
                          exportAsPng({ fileName: slug });
                          setIsDialOpen(false);
                        }}
                        className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-800/60 hover:bg-surface-700 border border-line hover:border-line-strong text-xs font-semibold text-content-primary transition-all text-left cursor-pointer"
                      >
                        <FileImage className="w-3.5 h-3.5 text-content-primary shrink-0" />
                        <span className="truncate">PNG Image</span>
                      </button>

                      <button
                        id="export-svg-btn"
                        onClick={() => {
                          exportAsSvg();
                          setIsDialOpen(false);
                        }}
                        className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-800/60 hover:bg-surface-700 border border-line hover:border-line-strong text-xs font-semibold text-content-primary transition-all text-left cursor-pointer"
                      >
                        <FileCode className="w-3.5 h-3.5 text-content-primary shrink-0" />
                        <span className="truncate">Vector (SVG)</span>
                      </button>

                      <button
                        id="export-sql-btn"
                        onClick={() => {
                          exportAsSql();
                          setIsDialOpen(false);
                        }}
                        className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-800/60 hover:bg-surface-700 border border-line hover:border-line-strong text-xs font-semibold text-content-primary transition-all text-left cursor-pointer"
                      >
                        <Database className="w-3.5 h-3.5 text-success-text shrink-0" />
                        <span className="truncate">SQL Code (.sql)</span>
                      </button>

                      <button
                        id="export-prisma-btn"
                        onClick={() => {
                          exportAsPrisma();
                          setIsDialOpen(false);
                        }}
                        className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-800/60 hover:bg-surface-700 border border-line hover:border-line-strong text-xs font-semibold text-content-primary transition-all text-left cursor-pointer"
                      >
                        <Database className="w-3.5 h-3.5 text-content-primary shrink-0" />
                        <span className="truncate">Prisma Schema</span>
                      </button>

                      <button
                        onClick={() => {
                          exportAsPdf();
                          setIsDialOpen(false);
                        }}
                        className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-800/60 hover:bg-surface-700 border border-line hover:border-line-strong text-xs font-semibold text-content-primary transition-all text-left cursor-pointer"
                      >
                        <FileText className="w-3.5 h-3.5 text-content-primary shrink-0" />
                        <span className="truncate">PDF Report</span>
                      </button>

                      <button
                        onClick={() => {
                          exportForCi();
                          setIsDialOpen(false);
                        }}
                        className="flex items-center gap-2 p-2 rounded-[var(--radius-control)] bg-surface-800/60 hover:bg-surface-700 border border-line hover:border-line-strong text-xs font-semibold text-content-primary transition-all text-left cursor-pointer"
                      >
                        <GitBranch className="w-3.5 h-3.5 text-content-primary shrink-0" />
                        <span className="truncate">CI Snapshot</span>
                      </button>
                    </div>

                    <div className="pt-1 border-t border-line space-y-1">
                      <button
                        id="export-zip-btn"
                        onClick={() => {
                          setIsCloudModalOpen(true);
                          setIsDialOpen(false);
                        }}
                        className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/80 hover:bg-surface-700 border border-line-strong transition-all text-left cursor-pointer group"
                      >
                        <div className="flex items-center gap-2 min-w-0">
                          <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                            <Archive className="w-3.5 h-3.5 text-content-primary" />
                          </div>
                          <div className="min-w-0">
                            <div className="text-xs font-bold text-content-primary truncate">Full-Stack Project (.zip)</div>
                            <div className="text-micro text-content-muted truncate">C# Web API + Next.js SDK + Cloud IaC</div>
                          </div>
                        </div>
                        <span className="shrink-0 text-micro font-bold text-content-primary">
                          Deploy ➔
                        </span>
                      </button>

                      <button
                        onClick={() => {
                          setIsSharedHostingModalOpen(true);
                          setIsDialOpen(false);
                        }}
                        className="w-full flex items-center justify-between px-2.5 py-1.5 rounded-[var(--radius-card)] bg-surface-800/50 hover:bg-surface-700 border border-line hover:border-line-strong transition-all text-left cursor-pointer group"
                      >
                        <div className="flex items-center gap-2 min-w-0">
                          <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary shrink-0">
                            <Server className="w-3.5 h-3.5 text-content-primary" />
                          </div>
                          <div className="min-w-0">
                            <div className="text-xs font-bold text-content-primary truncate">Shared Hosting / Mobile</div>
                            <div className="text-micro text-content-muted truncate">phpMyAdmin SQL or mobile SQLite .db</div>
                          </div>
                        </div>
                        <span className="shrink-0 text-micro font-semibold text-content-muted group-hover:text-content-primary">
                          Build ➔
                        </span>
                      </button>
                    </div>
                  </div>
                </div>
              </div>
            </FeatureGuard>
          </div>
        </div>

        {/* ── Ana FAB — Shopify Polaris Action Pill & Animated Toolkit Trigger ── */}
        <div className="flex items-center gap-2">
          <button
            id="canvas-toolkit-fab"
            onClick={() => setIsDialOpen(v => !v)}
            className={`relative z-10 h-11 px-3.5 rounded-full flex items-center gap-2.5 border transition-all duration-200 cursor-pointer group shadow-xl ${
              isDialOpen
                ? 'bg-content-primary border-content-primary text-surface-900 shadow-2xl'
                : 'bg-surface-800/95 border-line-strong text-content-primary hover:border-line-strong hover:bg-surface-700 shadow-lg'
            }`}
            title={isDialOpen ? 'Close toolkit' : 'Open toolkit: 11 database tools, AI analysis, export'}
            aria-label={isDialOpen ? 'Close toolkit' : 'Open toolkit'}
            aria-expanded={isDialOpen}
          >
            <div className="relative flex items-center justify-center">
              <Wrench className={`w-4 h-4 transition-transform duration-300 ${isDialOpen ? 'rotate-90' : 'group-hover:rotate-12'}`} />
              {issues.length > 0 && !isDialOpen && (
                <span className="absolute -top-1 -right-1 flex h-2 w-2">
                  <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-danger opacity-75" />
                  <span className="relative inline-flex rounded-full h-2 w-2 bg-danger" />
                </span>
              )}
            </div>

            <span className="text-xs font-bold tracking-tight">Toolkit</span>

            {/* Dikkat çeken dinamik rozet: "11 tools" veya "X alerts" */}
            <span
              className={`text-micro font-bold px-2 py-0.5 rounded-full transition-colors ${
                isDialOpen
                  ? 'bg-surface-900/15 text-surface-900'
                  : issues.length > 0
                  ? 'bg-danger-subtle text-danger-text border border-danger/30'
                  : 'bg-surface-700 text-content-secondary border border-line group-hover:text-content-primary group-hover:border-line-strong'
              }`}
            >
              {issues.length > 0 ? `${issues.length} alerts` : `${enabledToolsCount} tools`}
            </span>
          </button>
        </div>
      </div>

      {/* Vision Import Modal */}
      <VisionUploadModal
        isOpen={isVisionOpen}
        onClose={() => setIsVisionOpen(false)}
      />

      {/* Zero-to-Cloud Infrastructure Selector Modal */}
      {isCloudModalOpen && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/60 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="w-[450px] max-w-[calc(100vw-2rem)] max-h-[90vh] overflow-y-auto p-5 sm:p-6 rounded-[var(--radius-modal)] bg-surface-900 border border-line-strong shadow-2xl flex flex-col gap-5 text-sans select-none">
            {/* Title */}
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Archive className="w-5 h-5 text-content-primary animate-pulse" />
                <h3 className="text-md font-extrabold text-content-primary tracking-wide">Zero-to-Cloud Selector</h3>
              </div>
              <button
                onClick={() => setIsCloudModalOpen(false)}
                className="p-1.5 rounded-[var(--radius-control)] text-content-subtle hover:text-content-secondary hover:bg-surface-700/80 transition-colors cursor-pointer"
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
              className={`p-3 rounded-[var(--radius-card)] border transition-all cursor-pointer flex items-center justify-between ${
                includeBiModule 
                  ? 'bg-surface-800 border-line-strong text-content-primary' 
                  : 'bg-surface-800/40 border-line text-content-muted hover:border-line-strong'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <input
                  type="checkbox"
                  aria-label="Premium: AI Data Analytics (BI) Assistant"
                  checked={includeBiModule}
                  onChange={() => {}} 
                  className="rounded-[var(--radius-control)] border-line bg-surface-800 text-accent focus:ring-accent-hover/30 h-4 w-4"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-line hover:border-line-strong transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-muted group-hover:text-content-primary transition-colors font-bold text-xs">
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-line hover:border-line-strong transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary font-extrabold text-xs">
                    AWS
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-content-primary">AWS Cloud Infrastructure</h4>
                    <p className="text-[10px] text-content-subtle font-semibold">Terraform (VPC, ECS Cluster, RDS PostgreSQL) + Actions CI/CD</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-content-subtle group-hover:text-content-primary">Generate ➔</span>
              </button>

              {/* Option 3: Azure */}
              <button
                onClick={() => exportAsFullStackProject('Azure')}
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-line hover:border-line-strong transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-[var(--radius-control)] bg-surface-700 flex items-center justify-center text-content-primary font-extrabold text-xs">
                    AZ
                  </div>
                  <div>
                    <h4 className="text-xs font-bold text-content-primary">Azure Cloud Infrastructure</h4>
                    <p className="text-[10px] text-content-subtle font-semibold">Terraform (RG, App Service Plan, Web App Container, SQL Server) + CI/CD</p>
                  </div>
                </div>
                <span className="text-[10px] font-bold text-content-subtle group-hover:text-content-primary">Generate ➔</span>
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
          <div className="w-[420px] max-w-[calc(100vw-2rem)] max-h-[90vh] overflow-y-auto p-5 sm:p-6 rounded-[var(--radius-modal)] bg-surface-900 border border-line-strong shadow-2xl flex flex-col gap-5 text-sans select-none">
            <div className="flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Server className="w-5 h-5 text-content-primary" />
                <h3 className="text-md font-extrabold text-content-primary tracking-wide">Shared Hosting / Mobile</h3>
              </div>
              <button
                onClick={() => setIsSharedHostingModalOpen(false)}
                className="p-1.5 rounded-[var(--radius-control)] text-content-subtle hover:text-content-secondary hover:bg-surface-700/80 transition-colors cursor-pointer"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-line hover:border-line-strong transition-all text-left group cursor-pointer disabled:opacity-50"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-line hover:border-line-strong transition-all text-left group cursor-pointer disabled:opacity-50"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-line hover:border-line-strong transition-all text-left group cursor-pointer disabled:opacity-50"
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
