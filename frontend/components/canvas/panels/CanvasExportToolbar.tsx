'use client';

import React from 'react';
import {
  ImageDown,
  FileImage,
  Loader2,
  Pencil,
  Eye,
  Camera,
  FileCode,
  Braces,
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
  FileCode2
} from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useCanvasExport } from '../../../hooks/useCanvasExport';
import { useDbaStore } from '../../../store/useDbaStore';
import { useByokStore } from '../../../store/useByokStore';
import { schemaService, scaffolderService } from '../../../services/api';

import { DatabaseSchema } from '../../../types/schema';
import { useRef, useState, useEffect } from 'react';
import * as htmlToImage from 'html-to-image';
import VisionUploadModal from './VisionUploadModal';
import { parseSqlDdl } from '../../../lib/sqlImportParser';
import { toPrismaSchema } from '../../../lib/prismaExporter';
import { useToastStore } from '../../../store/useToastStore';
import { token } from '../../../lib/designTokens';

/** Sol-alt "toolkit" FAB'ı — canvas'a yerel araçlar (Import/DBA/Edit/Export) ve
 *  üst navbardan taşınan az-kullanılan 7 araç (Explain/Settings/DB Import/
 *  Browse Data/Migration/Cross-DB/Code Import) burada, tek dikey speed-dial'da. */
export default function CanvasExportToolbar() {
  const { projectName, schema, dbType, isEditMode, toggleEditMode, loadFromSchema } = useSchemaStore();
  const { isExporting, exportAsPng, exportAsJpeg } = useCanvasExport();
  // Yalnızca "AI Settings" mini dairesinde "ayarlı" noktasını göstermek için —
  // asıl AI/BYOK ayarları hâlâ ToolbarPanel.tsx'in yönettiği modalda yaşıyor
  // (bkz. aşağıdaki custom event notu).
  const apiKey = useByokStore(s => s.apiKey);
  
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

  // Dial dışına tıklayınca kapan — Share/More dropdown'larıyla aynı desen.
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (dialRef.current && !dialRef.current.contains(e.target as Node)) {
        setIsDialOpen(false);
        setIsExportDropdownOpen(false);
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
      <div id="canvas-toolbar" ref={dialRef} className="fixed bottom-6 left-6 z-50 select-none">
        {/* `w-[184px]` ZORUNLU: bu kapsayıcı absolute-içinde-absolute (shrink-
            to-fit) olduğu için, pod'ların `grid-cols-4` / `1fr` sütunları
            belirsiz bir genişliğe bölüşmeye çalışıyor ve tarayıcı sütunları
            neredeyse sıfıra küçültüp daireleri üst üste bindiriyordu (ölçüldü:
            36px'lik daireler yalnızca ~10px arayla diziliyordu). Sabit
            genişlik, 1fr'lere bölüşecek somut bir taban veriyor: 4×36 + 3×8
            (gap) + 2×6 (pod padding) = 180px, +4px pay.

            <b>REGRESYON.</b> Önceki turda buraya "kısa viewport'larda üst
            araç çubuğuna değebiliyor" diye bir `max-h` + `overflow-y-auto` +
            `overflow-x-visible` güvenlik payı eklenmişti. CSS'in kendi kuralı
            gereği overflow-x/y çifti "biri auto biri visible" olamaz — visible
            olan sessizce auto'ya döner (spec: computed overflow-x, visible
            paired with non-visible overflow-y). Sonuç: istenmeyen bir YATAY
            kaydırma çubuğu (görünür, teal renkli — `--color-accent-hover`
            global scrollbar-color'dan) pod'ların altında belirdi.

            İki pod'un gerçek yüksekliği ~160px — 620px'lik güvenlik payı
            zaten hiçbir gerçek ekranda gerekmiyordu. Kaldırıldı, sabit
            yükseklik kaldı. */}
        <div className="absolute bottom-[52px] left-0 w-[184px] flex flex-col gap-2">

          {/* ── Pod 1 — üst navbardan taşınan 7 araç (kullanıcı talebi: "az
              kullanılanları sol alt toolbara al"). 4 sütun × 2 satır.

              <b>REGRESYON.</b> Pod'un kendi arka plan/kenarlığı `isDialOpen`e
              hiç bağlı değildi — yalnızca İÇİNDEKİ 11 daire ayrı ayrı
              soluyordu. Dial kapanınca daireler görünmez oluyordu ama boş
              yuvarlatılmış dikdörtgen ARKA PLANI tuvalde asılı kalıyordu
              ("toolbarı kapatınca divler kalıyor" geri bildirimi). Pod'un
              kendisi de aynı opacity geçişini almalı. */}
          <div
            className="grid grid-cols-4 gap-2 p-1.5 rounded-[var(--radius-card)] bg-surface-800/70 border border-content-primary/8"
            style={{
              opacity: isDialOpen ? 1 : 0,
              pointerEvents: isDialOpen ? 'auto' : 'none',
              transition: 'opacity 150ms var(--ease-out)',
            }}
          >
            <DialMini
              open={isDialOpen}
              delayIndex={0}
              label="Explain schema with AI"
              onClick={() => { window.dispatchEvent(new CustomEvent('namines:explain-schema')); setIsDialOpen(false); }}
              icon={<BookOpen className="w-4 h-4" />}
            />
            <DialMini
              open={isDialOpen}
              delayIndex={1}
              label="AI & BYOK Settings"
              onClick={() => { window.dispatchEvent(new CustomEvent('namines:open-ai-settings')); setIsDialOpen(false); }}
              icon={<Settings className="w-4 h-4" />}
              dot={apiKey ? 'success' : undefined}
            />
            <DialMini
              open={isDialOpen}
              delayIndex={2}
              label="Import from a live database"
              onClick={() => { window.dispatchEvent(new CustomEvent('namines:open-db-connect')); setIsDialOpen(false); }}
              icon={<Database className="w-4 h-4" />}
            />
            <DialMini
              open={isDialOpen}
              delayIndex={3}
              label="Browse live data (read-only)"
              onClick={() => { window.dispatchEvent(new CustomEvent('namines:open-gateway')); setIsDialOpen(false); }}
              icon={<Table className="w-4 h-4" />}
            />
            <DialMini
              open={isDialOpen}
              delayIndex={4}
              label="Migration Engine"
              onClick={() => { window.dispatchEvent(new CustomEvent('namines:open-migration')); setIsDialOpen(false); }}
              icon={<History className="w-4 h-4" />}
            />
            <DialMini
              open={isDialOpen}
              delayIndex={5}
              label="Cross-database relations"
              onClick={() => { window.dispatchEvent(new CustomEvent('namines:open-cross-db')); setIsDialOpen(false); }}
              icon={<Network className="w-4 h-4" />}
            />
            <DialMini
              open={isDialOpen}
              delayIndex={6}
              label="Extract schema from code"
              onClick={() => { window.dispatchEvent(new CustomEvent('namines:open-code-import')); setIsDialOpen(false); }}
              icon={<FileCode2 className="w-4 h-4" />}
            />
          </div>

          {/* ── Pod 2 — tuval-yerel 4 araç. FAB'a en yakın, en sık kullanılan
              küme. Export son sütunda: yandaki dropdown'ı sağa taşırken pod
              içindeki komşu daireye binmiyor. ── */}
          <div
            className="grid grid-cols-4 gap-2 p-1.5 rounded-[var(--radius-card)] bg-surface-800/70 border border-content-primary/8"
            style={{
              opacity: isDialOpen ? 1 : 0,
              pointerEvents: isDialOpen ? 'auto' : 'none',
              transition: 'opacity 150ms var(--ease-out)',
            }}
          >
            <DialMini
              open={isDialOpen}
              delayIndex={7}
              label="Import from Image"
              onClick={() => { setIsVisionOpen(true); setIsDialOpen(false); }}
              icon={<Camera className="w-4 h-4" />}
              btnId="canvas-vision-import-btn"
            />
            <DialMini
              open={isDialOpen}
              delayIndex={8}
              label="DBA Analysis"
              onClick={() => setIsPanelOpen(!isPanelOpen)}
              active={isPanelOpen}
              dot={issues.length > 0 ? 'alert' : undefined}
              btnId="canvas-dba-inspect-btn"
              icon={<ShieldCheck className="w-4 h-4" />}
            />
            <DialMini
              open={isDialOpen}
              delayIndex={9}
              label={isEditMode ? 'Switch to view mode' : 'Switch to edit mode'}
              onClick={toggleEditMode}
              active={isEditMode}
              btnId="canvas-edit-mode-btn"
              icon={isEditMode ? <Eye className="w-4 h-4" /> : <Pencil className="w-4 h-4" />}
            />
            <div className="relative">
              <DialMini
                open={isDialOpen}
                delayIndex={10}
                label="Export diagram…"
                onClick={() => setIsExportDropdownOpen(!isExportDropdownOpen)}
                active={isExportDropdownOpen}
                btnId="canvas-export-dropdown-btn"
                disabled={isCurrentlyExporting}
                icon={isCurrentlyExporting ? <Loader2 className="w-4 h-4 animate-spin" /> : <ImageDown className="w-4 h-4" />}
              />

              {/* Dropdown Menu — dial'ın sağına açılıyor */}
              {isExportDropdownOpen && (
              <div
                className="absolute bottom-0 left-full ml-2 w-52 max-h-[min(480px,calc(100vh-32px))] overflow-y-auto rounded-[var(--radius-card)] bg-surface-900/95 border border-content-primary/12 flex flex-col p-1.5 backdrop-blur-xl z-[100] animate-in fade-in duration-200"
              >
                    <div className="px-2.5 py-1.5 text-micro font-extrabold text-content-subtle uppercase tracking-wider select-none">
                      Image Export
                    </div>
                    
                    <button
                      onClick={() => {
                        exportAsPng({ fileName: slug });
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-white/[0.06] rounded-[var(--radius-control)] transition-colors text-left"
                    >
                      <FileImage className="w-3.5 h-3.5 text-content-primary" />
                      <span>PNG Image (.png)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsJpeg({ fileName: slug });
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06] rounded-[var(--radius-control)] transition-colors text-left"
                    >
                      <FileImage className="w-3.5 h-3.5 text-content-primary" />
                      <span>JPEG Image (.jpg)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsSvg();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-colors text-left"
                    >
                      <FileCode className="w-3.5 h-3.5 text-content-muted" />
                      <span>Vector Graphic (.svg)</span>
                    </button>

                    <div className="h-px bg-content-primary/[0.06] my-1" />

                    <div className="px-2.5 py-1.5 text-micro font-extrabold text-content-subtle uppercase tracking-wider select-none">
                      Data & Document
                    </div>

                    <button
                      onClick={() => {
                        exportAsSql();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-success hover:bg-success-subtle rounded-[var(--radius-control)] transition-colors text-left"
                    >
                      <Database className="w-3.5 h-3.5 text-success" />
                      <span>SQL Schema Code (.sql)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsJson();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-danger hover:bg-danger-subtle rounded-[var(--radius-control)] transition-colors text-left"
                    >
                      <Braces className="w-3.5 h-3.5 text-danger" />
                      <span>Namines Meta Schema (.json)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportAsPrisma();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-colors text-left"
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
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-colors text-left"
                    >
                      <FileText className="w-3.5 h-3.5 text-content-muted" />
                      <span>PDF Technical Report (.pdf)</span>
                    </button>

                    <button
                      onClick={() => {
                        exportForCi();
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-colors text-left"
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
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06] rounded-[var(--radius-control)] transition-colors text-left border border-transparent hover:border-content-primary/12"
                    >
                      <Archive className="w-3.5 h-3.5 text-content-primary" />
                      <span>Full-Stack Project (.zip)</span>
                    </button>

                    <button
                      onClick={() => {
                        setIsSharedHostingModalOpen(true);
                        setIsExportDropdownOpen(false);
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-primary hover:bg-content-primary/[0.06] rounded-[var(--radius-control)] transition-colors text-left border border-transparent hover:border-content-primary/12"
                      title="Plesk/cPanel (phpMyAdmin) or mobile (SQLite) — no CLI, no Docker"
                    >
                      <Server className="w-3.5 h-3.5 text-content-primary" />
                      <span>Shared Hosting / Mobile (.zip)</span>
                    </button>

                    <div className="h-px bg-content-primary/[0.06] my-1" />

                    <div className="px-2.5 py-1.5 text-micro font-extrabold text-content-subtle uppercase tracking-wider select-none">
                      Import
                    </div>

                    <button
                      onClick={() => {
                        setIsExportDropdownOpen(false);
                        sqlFileInputRef.current?.click();
                      }}
                      className="flex items-center gap-2 w-full px-2.5 py-2 text-xs font-semibold text-content-secondary hover:text-content-muted hover:bg-white/[0.04] rounded-[var(--radius-control)] transition-colors text-left"
                      title="Parse a .sql DDL file and load tables onto the canvas"
                    >
                      <FileCode className="w-3.5 h-3.5 text-content-muted" />
                      <span>Import SQL DDL (.sql)</span>
                    </button>
                  </div>
              )}
            </div>
          </div>
        </div>

        {/* Ana FAB — daireye basınca dial açılır/kapanır; ikon + (plus) 45°
            dönüp X'e dönüşür, ayrı bir "kapat" ikonu taşımaya gerek kalmadan. */}
        <button
          onClick={() => setIsDialOpen(v => !v)}
          className={`relative z-10 w-11 h-11 rounded-full flex items-center justify-center border shadow-[0_6px_20px_color-mix(in srgb,var(--color-scrim)_55%,transparent)] transition-all duration-200 cursor-pointer ${
            isDialOpen
              ? 'bg-content-primary border-content-primary text-surface-900'
              : 'bg-surface-700 border-content-primary/14 text-content-primary hover:border-accent-hover/50 hover:bg-surface-600'
          }`}
          title={isDialOpen ? 'Close toolkit' : 'Open toolkit: import, DBA, edit mode, export'}
          aria-label={isDialOpen ? 'Close toolkit' : 'Open toolkit'}
          aria-expanded={isDialOpen}
        >
          <Wrench className={`w-[18px] h-[18px] transition-transform duration-200 ${isDialOpen ? 'rotate-45' : ''}`} />
        </button>
      </div>

      {/* Vision Import Modal */}
      <VisionUploadModal
        isOpen={isVisionOpen}
        onClose={() => setIsVisionOpen(false)}
      />

      {/* Zero-to-Cloud Infrastructure Selector Modal */}
      {isCloudModalOpen && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/60 backdrop-blur-sm animate-in fade-in duration-200">
          <div className="w-[450px] p-6 rounded-[var(--radius-modal)] bg-gradient-to-b from-surface-800/95 to-surface-900/98 border border-white/15 flex flex-col gap-5 text-sans select-none">
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
                  ? 'bg-content-primary/[0.06] border-white/25 text-content-primary' 
                  : 'bg-surface-800/20 border-surface-600 text-content-muted hover:border-surface-500'
              }`}
            >
              <div className="flex items-center gap-2.5">
                <input
                  type="checkbox"
                  checked={includeBiModule}
                  onChange={() => {}} 
                  className="rounded-[var(--radius-control)] border-surface-500 bg-surface-800 text-accent focus:ring-accent-hover/30 h-4 w-4"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-accent-subtle/10 hover:bg-accent-subtle/20 border border-accent-subtle/20 hover:border-accent/40 transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-[var(--radius-control)] bg-accent/10 flex items-center justify-center text-accent-text font-extrabold text-xs">
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-accent-subtle/10 hover:bg-accent-subtle/20 border border-accent-subtle/20 hover:border-accent/40 transition-all text-left group cursor-pointer"
              >
                <div className="flex items-center gap-3">
                  <div className="w-8 h-8 rounded-[var(--radius-control)] bg-white/[0.04] flex items-center justify-center text-content-muted font-extrabold text-xs">
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
          <div className="w-[420px] p-6 rounded-[var(--radius-modal)] bg-gradient-to-b from-surface-800/95 to-surface-900/98 border border-white/15 flex flex-col gap-5 text-sans select-none">
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer disabled:opacity-50"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer disabled:opacity-50"
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
                className="flex items-center justify-between p-3.5 rounded-[var(--radius-card)] bg-surface-800/40 hover:bg-surface-700/40 border border-surface-600 hover:border-surface-500 transition-all text-left group cursor-pointer disabled:opacity-50"
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

// ── Speed-dial mini daire ────────────────────────────────────────────────
// `#canvas-toolbar`'daki 11 araç ortak bir görünüm paylaşıyor: 36px GÖRSEL
// daire (`tap-44` ile 44px dokunma hedefi — FRONTEND.md §6), hover'da
// ÜSTTE açılan etiket, dial kapalıyken görünmez+küçük, açılınca kademeli
// (staggered) beliriyor. Etiket sağda değil ÜSTTE: iki pod artık 4 sütunlu
// bir grid, sağdaki komşu daireye binmemesi için (bkz. Toolbar Redesign
// Options artifact'te Option 2'nin canlı testinde bulunan hata: paylaşılmayan
// taban stil, bir varyantın ikonlarını arka plansız bıraktı — aynı riski
// tekrarlamamak için taban stil hâlâ tek yerden).
function DialMini({
  open,
  delayIndex,
  label,
  onClick,
  icon,
  active,
  dot,
  disabled,
  btnId,
}: {
  open: boolean;
  delayIndex: number;
  label: string;
  onClick: () => void;
  icon: React.ReactNode;
  active?: boolean;
  /** 'alert': kırmızı, nabız atan (DBA sorunları gibi dikkat gerektiren durumlar).
   *  'success': sabit yeşil nokta (bir şeyin "ayarlı/aktif" olduğunu gösterir,
   *  alarm değildir — bkz. AI & BYOK Settings). */
  dot?: 'alert' | 'success';
  disabled?: boolean;
  btnId?: string;
}) {
  return (
    <div
      className="group/mini relative"
      style={{
        opacity: open ? 1 : 0,
        transform: open ? 'translateY(0) scale(1)' : 'translateY(8px) scale(0.7)',
        pointerEvents: open ? 'auto' : 'none',
        transition: `opacity 200ms var(--ease-out), transform 200ms var(--ease-out)`,
        transitionDelay: open ? `${delayIndex * 35}ms` : '0ms',
      }}
    >
      <button
        id={btnId}
        onClick={onClick}
        disabled={disabled}
        className={`tap-44 relative w-9 h-9 rounded-full flex items-center justify-center border shadow-md transition-all cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed ${
          active
            ? 'bg-content-primary/12 border-white/25 text-content-primary'
            : 'bg-surface-700 border-content-primary/12 text-content-muted hover:text-content-primary hover:border-accent-hover/50 hover:bg-surface-600'
        }`}
        aria-label={label}
      >
        {icon}
        {dot === 'alert' && (
          <span className="absolute top-0 right-0 flex h-2 w-2">
            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-danger opacity-75" />
            <span className="relative inline-flex rounded-full h-2 w-2 bg-danger" />
          </span>
        )}
        {dot === 'success' && (
          <span className="absolute top-0 right-0 w-2 h-2 rounded-full bg-success" />
        )}
      </button>

      {/* Hover etiketi — ÜSTTE açılır (4 sütunlu grid'de sağa açsaydı komşu
          sütundaki daireye binerdi). z-20: pod arka planının üstünde kalsın. */}
      <span
        className="absolute bottom-full mb-2 left-1/2 -translate-x-1/2 z-20 whitespace-nowrap px-2.5 py-1.5 rounded-[var(--radius-control)] bg-surface-800 border border-content-primary/12 text-xs font-semibold text-content-secondary opacity-0 group-hover/mini:opacity-100 transition-opacity duration-150 pointer-events-none shadow-md"
      >
        {label}
      </span>
    </div>
  );
}
