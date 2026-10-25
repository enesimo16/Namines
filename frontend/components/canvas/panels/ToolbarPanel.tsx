import React, { useState, useRef, useEffect } from 'react';
import { createPortal } from 'react-dom';
import { useRouter } from 'next/navigation';
import { Users, Terminal, Link2, Loader2, BookOpen, X, ChevronDown, Copy, Check, GitPullRequest, Wand2, Moon, Sun } from 'lucide-react';
import MarkdownLite from '../../common/MarkdownLite';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useHomeThemeStore } from '../../../store/useHomeThemeStore';
import { useReactFlow } from '@xyflow/react';
import { flowToSchema } from '../../../lib/flowToSchema';
import MigrationWizard from '../../migration/MigrationWizard';
import { useMultiplayerStore } from '../../../store/useMultiplayerStore';
import { useBranchStore } from '../../../store/useBranchStore';
import { useSqlExplorerStore } from '../../../store/useSqlExplorerStore';
import { useToastStore } from '../../../store/useToastStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useAuthStore } from '../../../store/useAuthStore';
import { useAIGateway } from '../../../hooks/useAIGateway';
import { authService, schemaService, changeRequestService } from '../../../services/api';
import DbConnectionPanel from './DbConnectionPanel';
import GatewayExplorerPanel from './GatewayExplorerPanel';
import AlternativeCompareModal from './AlternativeCompareModal';
import CrossDatabasePanel from './CrossDatabasePanel';
import CodeImportPanel from './CodeImportPanel';
import { DatabaseSchema } from '../../../types/schema';

// İkon-buton — tüm toolbar bunu kullanır: tek renk aile (off-white/lacivert),
// aktif durum dışında renkli vurgu yok (bkz. FRONTEND.md §2).
// `shrink-0`: mobilde araç çubuğu yatay kaydırılan bir şerit; onsuz butonlar
// sıkışıp okunmaz hâle geliyordu.
const iconBtnBase =
  'relative shrink-0 flex items-center justify-center w-8 h-8 rounded-[var(--radius-control)] border transition-all cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed';
const iconBtnIdle =
  'bg-surface-800/90 border-line text-content-secondary hover:text-content-primary hover:border-line-strong hover:bg-surface-700/60 shadow-xs';
const iconBtnActive =
  'bg-surface-700 border-line-strong text-content-primary shadow-xs font-semibold';

export default function ToolbarPanel() {
  const router = useRouter();
  const { theme, toggle: toggleTheme } = useHomeThemeStore();
  const schema = useSchemaStore(s => s.schema);
  // Şemaya bağlı eylemlerin görünürlüğü (bkz. aşağıdaki not).
  const hasTables = (schema?.tables.length ?? 0) > 0;
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const autoLayout = useSchemaStore(s => s.autoLayout);
  const dbType = useSchemaStore(s => s.dbType);
  const naiModel = useSchemaStore(s => s.naiModel);
  const lastGenerationPrompt = useSchemaStore(s => s.lastGenerationPrompt);
  const lastGenerationAnswers = useSchemaStore(s => s.lastGenerationAnswers);
  const { checkAccess } = useAIGateway();
  const { getNodes, getEdges } = useReactFlow();
  const [alternativeSchema, setAlternativeSchema] = useState<DatabaseSchema | null>(null);
  const [isCrossDbOpen, setIsCrossDbOpen] = useState(false);
  const [isCodeImportOpen, setIsCodeImportOpen] = useState(false);
  const [isMigrationOpen, setIsMigrationOpen] = useState(false);
  const [isDbConnectOpen, setIsDbConnectOpen] = useState(false);
  const [isGatewayOpen, setIsGatewayOpen] = useState(false);
  const [isShareOpen, setIsShareOpen] = useState(false);
  const shareRef = useRef<HTMLDivElement>(null);

  const isConnected = useMultiplayerStore(s => s.isConnected);
  const roomId = useMultiplayerStore(s => s.roomId);
  const isDiffMode = useBranchStore(s => s.isDiffMode);
  const isSqlExplorerOpen = useSqlExplorerStore(state => state.isOpen);
  const toggleSqlExplorer = useSqlExplorerStore(state => state.toggleOpen);

  const showToast = useToastStore(state => state.showToast);

  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const [isSharing, setIsSharing] = useState(false);
  // Yalnızca setter kullanılıyor — "Explain schema" düğmesi (dolayısıyla
  // yükleniyor göstergesi) CanvasExportToolbar.tsx'in FAB'ına taşındı, bu
  // dosyada artık okunan bir spinner yok, ama handleExplainSchema hâlâ
  // isExplaining'i true/false yapıyor (ileride buraya bir gösterge eklenmek
  // istenirse diye tutuldu).
  const [, setIsExplaining] = useState(false);
  const [explanation, setExplanation] = useState<string | null>(null);
  const [explanationCopied, setExplanationCopied] = useState(false);
  const [isRequestingReview, setIsRequestingReview] = useState(false);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (shareRef.current && !shareRef.current.contains(e.target as Node)) {
        setIsShareOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const handleExplainSchema = async () => {
    if (!schema || schema.tables.length === 0) {
      showToast('Add some tables first before explaining the schema.', 'warning');
      return;
    }
    setIsExplaining(true);
    showToast('Generating AI schema explanation...', 'info');
    try {
      const readme = await schemaService.generateReadme(schema, 'en');
      setExplanation(readme);
    } catch {
      showToast('Failed to generate schema explanation. Please try again.', 'error');
    } finally {
      setIsExplaining(false);
    }
  };

  // Explain/DB Import/Browse Data/Migration/Cross-DB/Code Import — az kullanılan
  // bu 6 aracın DÜĞMESİ artık üst araç çubuğunda değil, sol-alt "toolkit"
  // FAB'ında (bkz. CanvasExportToolbar.tsx). State + modal + iş mantığı burada
  // KALDI (taşımak gereksiz churn olurdu) — FAB sadece `namines:open-ai-settings`
  // ile aynı desende bir custom event fırlatıyor, burası dinleyip açıyor.
  useEffect(() => {
    const openExplain = () => handleExplainSchema();
    const openDbConnect = () => setIsDbConnectOpen(true);
    const openGateway = () => setIsGatewayOpen(true);
    const openMigration = () => setIsMigrationOpen(true);
    const openCrossDb = () => setIsCrossDbOpen(true);
    const openCodeImport = () => setIsCodeImportOpen(true);
    window.addEventListener('namines:explain-schema', openExplain);
    window.addEventListener('namines:open-db-connect', openDbConnect);
    window.addEventListener('namines:open-gateway', openGateway);
    window.addEventListener('namines:open-migration', openMigration);
    window.addEventListener('namines:open-cross-db', openCrossDb);
    window.addEventListener('namines:open-code-import', openCodeImport);
    return () => {
      window.removeEventListener('namines:explain-schema', openExplain);
      window.removeEventListener('namines:open-db-connect', openDbConnect);
      window.removeEventListener('namines:open-gateway', openGateway);
      window.removeEventListener('namines:open-migration', openMigration);
      window.removeEventListener('namines:open-cross-db', openCrossDb);
      window.removeEventListener('namines:open-code-import', openCodeImport);
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleApprove = () => {
    // Diff görünümü SALT-OKUNUR. processedNodes bu modda karşılaştırılan branch'ten
    // sanal "silinmiş tablo" node'ları enjekte eder; getNodes() bunları da döndürür ve
    // flowToSchema hepsini schema.tables'a geri yazarak silinmiş tabloları diriltir.
    // Bu modda senkronize etmeden geç.
    if (!isDiffMode) {
      const updatedSchema = flowToSchema(schema, getNodes(), getEdges());
      if (updatedSchema) {
        const nodePositions = Object.fromEntries(
          getNodes().map(n => [n.id, { x: n.position.x, y: n.position.y }])
        );
        loadFromSchema(updatedSchema, nodePositions, true);
      }
    }
    router.push('/compile');
  };

  // "Approve" artık üst gezinmedeki "Build" öğesi (WorkspaceNav) — tıklama bir olayla
  // buraya geliyor çünkü onay, React Flow düğüm konumlarını okuyor.
  useEffect(() => {
    const run = () => handleApprove();
    window.addEventListener('namines:approve-diagram', run);
    return () => window.removeEventListener('namines:approve-diagram', run);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schema, isDiffMode]);

  // Masaüstünde (lg+) eylemler Header'daki yuvaya portal ile basılır; dar ekranda
  // başlığın altında kaydırılan şerit olarak kalır (bkz. aşağıdaki not).
  const [actionSlot, setActionSlot] = useState<HTMLElement | null>(null);
  const [isDesktop, setIsDesktop] = useState(false);
  useEffect(() => {
    const query = window.matchMedia('(min-width: 1024px)');
    const update = () => {
      setIsDesktop(query.matches);
      setActionSlot(document.getElementById('canvas-actions-slot'));
    };
    update();
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);

  const handleShareReadOnly = async () => {
    if (!isAuthenticated) {
      showToast('Sign in to share your schema.', 'warning');
      return;
    }
    if (!activeProjectId) {
      showToast('Save your project first (generate a schema and it will auto-save).', 'warning');
      return;
    }
    setIsSharing(true);
    try {
      const state = useProjectHistoryStore.getState();
      const activeProject = state.projects.find(p => p.id === activeProjectId);
      if (activeProject && activeProject.schema) {
        const syncData = [{
          id: activeProject.id,
          name: activeProject.name,
          dbType: activeProject.dbType,
          schemaJson: JSON.stringify(activeProject.schema),
          nodePositionsJson: JSON.stringify(activeProject.nodePositions ?? {}),
        }];
        await authService.syncProjects(syncData);
      }

      const { token } = await authService.createShareLink(activeProjectId);
      const shareUrl = `${window.location.origin}/share/${token}`;
      await navigator.clipboard.writeText(shareUrl);
      showToast('Read-only share link copied to clipboard!', 'success');
      setIsShareOpen(false);
    } catch {
      showToast('Failed to generate share link. Try again.', 'error');
    } finally {
      setIsSharing(false);
    }
  };

  const handleRequestReview = async () => {
    if (!schema || schema.tables.length === 0) {
      showToast('Add some tables first before requesting a review.', 'warning');
      return;
    }
    setIsRequestingReview(true);
    try {
      let projId = activeProjectId;
      const currentSchema = flowToSchema(schema, getNodes(), getEdges()) || schema;
      if (!projId) {
        useProjectHistoryStore.getState().saveCurrentProject(currentSchema, getNodes(), schema?.name || 'Untitled Project', dbType);
        projId = useProjectHistoryStore.getState().activeProjectId;
      }

      const { id } = await changeRequestService.createQuick(projId || 'local-project', currentSchema);
      showToast('Change request created! Opening review…', 'success');
      router.push(`/review/${id}`);
    } catch {
      showToast('Failed to create change request. Please try again.', 'error');
    } finally {
      setIsRequestingReview(false);
    }
  };

  // second-phase/09-SEMA-ALTERNATIFLERI.md — aynı prompt+cevaplarla ikinci bir
  // tur çalıştırır, sonucu doğrudan uygulamaz: kullanıcı A/B karşılaştırmasında
  // seçim yapana kadar canvas değişmez.
  const handleGenerateAlternative = async () => {
    if (!lastGenerationPrompt) {
      showToast('Generate a schema from a prompt first — alternatives replay that same prompt.', 'warning');
      return;
    }
    if (!checkAccess('Generate Alternative')) return;

    try {
      const alt = await schemaService.generateSchema(
        lastGenerationPrompt, dbType, naiModel, undefined, undefined, lastGenerationAnswers ?? undefined
      );
      setAlternativeSchema(alt);
    } catch {
      showToast('Failed to generate an alternative. Please try again.', 'error');
    } finally {
      // Tetikleyici düğme artık prompt çubuğunda (RegionalPromptPanel) ve kendi
      // "meşgul" göstergesini tutuyor — bitişi ona bildir. Başarı ve hata
      // yolunun İKİSİ de buradan geçer (`finally`), yoksa hata durumunda
      // oradaki spinner sonsuza kadar dönerdi.
      window.dispatchEvent(new CustomEvent('namines:alternative-done'));
    }
  };

  // Prompt çubuğundaki düğmeden gelen tetikleme. Bu `useEffect` bilinçli olarak
  // `handleGenerateAlternative`'ın TANIMINDAN SONRA duruyor: yukarıdaki toplu
  // event effect'ine eklenseydi, fonksiyon henüz bildirilmemiş olacağı için
  // eslint'in `react-hooks/immutability` kuralı "declared before it is used"
  // hatası verirdi (aynı hata CanvasExportToolbar'da `exportAsPrisma` için
  // hâlâ açık duruyor — burada tekrarlamamak için ayrı effect).
  useEffect(() => {
    const run = () => handleGenerateAlternative();
    window.addEventListener('namines:generate-alternative', run);
    return () => window.removeEventListener('namines:generate-alternative', run);
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [lastGenerationPrompt, lastGenerationAnswers, dbType, naiModel]);

  const handleTidyUp = () => {
    autoLayout();
    window.dispatchEvent(new CustomEvent('namines:fit-view'));
    showToast('Diagram layout tidied', 'success');
  };

  const shareRoomLink = async () => {
    let activeRoomId = roomId || new URLSearchParams(window.location.search).get('roomId');
    if (!activeRoomId) {
      const rand = (typeof crypto !== 'undefined' && crypto.randomUUID)
        ? crypto.randomUUID()
        : Math.random().toString(36).substring(2, 11);
      activeRoomId = `room-${rand}`;
    } else if (!activeRoomId.startsWith('room-')) {
      activeRoomId = `room-${activeRoomId}`;
    }

    const shareUrl = `${window.location.protocol}//${window.location.host}/canvas?roomId=${activeRoomId}`;
    if (new URLSearchParams(window.location.search).get('roomId') !== activeRoomId) {
      window.history.pushState({ path: shareUrl }, '', shareUrl);
      try { sessionStorage.setItem('namines_own_room', activeRoomId); } catch { /* ignore */ }
    }

    try {
      await navigator.clipboard.writeText(shareUrl);
      showToast('Live session link copied to clipboard! Invite anyone to co-edit in real time.', 'success');
    } catch {
      showToast('Live session link: ' + shareUrl, 'info');
    }
    setIsShareOpen(false);
  };

  const actions = (
    <>
        {/* Share — tek giriş noktası: salt-okunur link ve Live Share (varsa) burada birleşti */}
        <div className="relative" ref={shareRef}>
          <button
            onClick={() => setIsShareOpen(v => !v)}
            className={`${iconBtnBase} w-auto px-3 gap-1.5 ${isShareOpen ? iconBtnActive : iconBtnIdle}`}
            title="Share"
            aria-label="Share"
          >
            <Link2 className="w-4 h-4" />
            <span className="text-xs font-semibold">Share</span>
            
            <ChevronDown className={`w-3 h-3 transition-transform ${isShareOpen ? 'rotate-180' : ''}`} />
          </button>

          {isShareOpen && (
            <div className="absolute right-0 top-full mt-1.5 w-60 rounded-[var(--radius-card)] border border-line-strong bg-surface-800/95 backdrop-blur-xl p-1 shadow-2xl z-50 flex flex-col gap-0.5 animate-dropdown-in">
              <button
                onClick={handleShareReadOnly}
                disabled={isSharing}
                className="flex items-center gap-2.5 px-2.5 py-2 rounded-[var(--radius-control)] text-left text-xs font-medium text-content-primary hover:bg-surface-700/60 transition-colors disabled:opacity-50 cursor-pointer"
              >
                {isSharing ? <Loader2 className="w-3.5 h-3.5 animate-spin shrink-0" /> : <Copy className="w-3.5 h-3.5 shrink-0" />}
                <span>
                  <span className="block font-medium">Copy read-only link</span>
                  <span className="block text-[10px] text-content-muted">Anyone with the link can view</span>
                </span>
              </button>
              <button
                onClick={shareRoomLink}
                className="flex items-center gap-2.5 px-2.5 py-2 rounded-[var(--radius-control)] text-left text-xs font-medium text-content-primary hover:bg-surface-700/60 transition-colors cursor-pointer"
              >
                <Users className="w-3.5 h-3.5 shrink-0" />
                <span>
                  <span className="block font-medium">Copy live session link</span>
                  <span className="block text-[10px] text-content-muted">
                    Real-time co-editing room
                  </span>
                </span>
              </button>
            </div>
          )}
        </div>

        {/* Theme Toggle (Dark / Light) — Share'in hemen yanında */}
        <button
          onClick={toggleTheme}
          type="button"
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
          aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
        >
          {theme === 'dark' ? (
            <Moon className="w-4 h-4 text-accent-text" />
          ) : (
            <Sun className="w-4 h-4 text-accent" />
          )}
        </button>

        {/* SQL Console */}
        <button
          id="sql-explorer-btn"
          onClick={toggleSqlExplorer}
          className={`${iconBtnBase} ${isSqlExplorerOpen ? iconBtnActive : iconBtnIdle}`}
          title="Open Live SQL Console"
          aria-label="Open Live SQL Console"
        >
          <Terminal className="w-4 h-4" />
        </button>

        {/*
          ŞEMAYA BAĞLI EYLEMLER — yalnızca tuvalde tablo varken.
        */}
        {hasTables && (
        <>
        {/* Tidy Up — dagre ile otomatik yerleşim */}
        <button
          id="tidy-up-btn"
          onClick={handleTidyUp}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="Tidy up: re-arrange tables to minimize crossing connections"
          aria-label="Tidy up table layout"
        >
          <Wand2 className="w-4 h-4" />
        </button>

        {/* Request Review — "Database PR" akışını başlatır (bkz. new-phase/29) */}
        <button
          id="request-review-btn"
          onClick={handleRequestReview}
          disabled={isRequestingReview}
          className={`${iconBtnBase} xl:w-auto xl:px-3 xl:gap-1.5 ${iconBtnIdle}`}
          title="Request a database change review"
          aria-label="Request a database change review"
        >
          {isRequestingReview ? <Loader2 className="w-4 h-4 animate-spin" /> : <GitPullRequest className="w-4 h-4" />}
          <span className="hidden xl:inline text-xs font-semibold">Request Review</span>
        </button>

        </>
        )}
    </>
  );

  return (
    <>
      {/*
        Araç çubuğu MOBİLDE VE TABLETTE başlığın ALTINDA, tam genişlikte ve
        yatay kaydırılan bir şerit; yalnızca `lg:` (1024px+) üstünde eski
        sabit-konumlu desktop şeridine dönüyor.

        Önceden her boyutta `fixed top-2.5 right-6` idi: 375px'te on düğme
        başlık şeridinin (56px) üstüne biniyor, logo ve proje adı okunmaz hâle
        geliyordu. Alta almak da olmuyor — orada AI giriş kutusu var. Başlığın
        hemen altı, iki çakışmayı da önleyen tek yer.

        Kırılma noktası `md`(768px) DEĞİL `lg`(1024px): tablet dikey modda
        (768px) ~10 düğme + Approve CTA'sı sabit-konumlu şeritte sığmıyordu —
        Approve düğmesi ekranın sağından TAMAMEN taşıyordu, birincil eylem
        tıklanamaz hâldeydi. 1024px altında hâlâ kaydırılan şerit kullanılıyor.
      */}
      {isDesktop && actionSlot ? (
        createPortal(actions, actionSlot)
      ) : (
        <div
          className="fixed z-[60] flex items-center gap-1.5 top-[60px] left-2 right-2 overflow-x-auto scrollbar-none
            rounded-[var(--radius-card)] border border-line bg-surface-800/95 backdrop-blur-xl p-1.5 shadow-lg"
        >
          {actions}
        </div>
      )}

      {/* Migration Wizard */}
      <MigrationWizard
        isOpen={isMigrationOpen}
        onClose={() => setIsMigrationOpen(false)}
      />

      {/* DB Connection / Import */}
      <DbConnectionPanel
        isOpen={isDbConnectOpen}
        onClose={() => setIsDbConnectOpen(false)}
      />

      {/* G14 — Minimal Gateway: read-only data browser */}
      <GatewayExplorerPanel
        isOpen={isGatewayOpen}
        onClose={() => setIsGatewayOpen(false)}
      />

      {/* Cross-database relations — second-phase/10-COKLU-DB.md */}
      <CrossDatabasePanel isOpen={isCrossDbOpen} onClose={() => setIsCrossDbOpen(false)} />

      {/* Schema from code — second-phase/11-KODDAN-SEMA.md */}
      <CodeImportPanel isOpen={isCodeImportOpen} onClose={() => setIsCodeImportOpen(false)} />

      {/* Alternative compare — second-phase/09-SEMA-ALTERNATIFLERI.md */}
      {alternativeSchema && schema && (
        <AlternativeCompareModal
          current={flowToSchema(schema, getNodes(), getEdges()) || schema}
          alternative={alternativeSchema}
          onClose={() => setAlternativeSchema(null)}
          onKeepCurrent={() => setAlternativeSchema(null)}
          onKeepAlternative={() => {
            loadFromSchema(alternativeSchema, undefined, true);
            setAlternativeSchema(null);
            showToast('Switched to the alternative schema.', 'success');
          }}
        />
      )}

      {/* AI Schema Explanation Modal — ham markdown yerine gerçek render (bkz. MarkdownLite) */}
      {explanation !== null && (
        <div className="fixed inset-0 z-[300] flex items-center justify-center bg-scrim/70 backdrop-blur-sm" onClick={() => setExplanation(null)}>
          <div
            className="bg-surface-800 border border-line-strong rounded-[var(--radius-modal)] shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] w-full max-w-xl mx-4 flex flex-col animate-in fade-in zoom-in-95 duration-150"
            style={{ maxHeight: '80vh' }}
            onClick={e => e.stopPropagation()}
          >
            <div className="flex items-center justify-between px-5 pt-4 pb-3 border-b border-line">
              <div className="flex items-center gap-2">
                <BookOpen className="w-4 h-4 text-content-muted" />
                <span className="text-content-primary font-semibold text-sm">Schema Explanation</span>
              </div>
              <div className="flex items-center gap-1">
                <button
                  onClick={() => {
                    navigator.clipboard.writeText(explanation);
                    setExplanationCopied(true);
                    setTimeout(() => setExplanationCopied(false), 1500);
                  }}
                  className="p-1.5 text-content-subtle hover:text-content-primary rounded-[var(--radius-control)] hover:bg-surface-700/60 transition-colors cursor-pointer"
                  aria-label="Copy explanation"
                  title="Copy as Markdown"
                >
                  {explanationCopied ? <Check className="w-3.5 h-3.5 text-success-text" /> : <Copy className="w-3.5 h-3.5" />}
                </button>
                <button onClick={() => setExplanation(null)} className="p-1.5 text-content-subtle hover:text-content-primary rounded-[var(--radius-control)] hover:bg-surface-700/60 transition-colors cursor-pointer" aria-label="Close">
                  <X className="w-4 h-4" />
                </button>
              </div>
            </div>
            <div className="overflow-y-auto px-5 py-4">
              <MarkdownLite text={explanation} />
            </div>
          </div>
        </div>
      )}
    </>
  );
}
