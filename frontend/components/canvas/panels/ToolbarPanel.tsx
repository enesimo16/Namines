import React, { useState, useRef, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { ArrowRight, History, Users, Terminal, Settings, Link2, Loader2, Database, BookOpen, X, ChevronDown, Copy, Check, GitPullRequest, Table, Sparkles, Network, FileCode2 } from 'lucide-react';
import MarkdownLite from '../../common/MarkdownLite';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useReactFlow } from '@xyflow/react';
import { flowToSchema } from '../../../lib/flowToSchema';
import MigrationWizard from '../../migration/MigrationWizard';
import { useMultiplayerStore } from '../../../store/useMultiplayerStore';
import { useBranchStore } from '../../../store/useBranchStore';
import { useSqlExplorerStore } from '../../../store/useSqlExplorerStore';
import { useToastStore } from '../../../store/useToastStore';
import { useByokStore } from '../../../store/useByokStore';
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
const iconBtnBase =
  'relative flex items-center justify-center w-9 h-9 rounded-lg border transition-all cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed';
const iconBtnIdle =
  'bg-surface-700 border-content-primary/10 text-content-muted hover:text-content-primary hover:border-content-primary/20 hover:bg-surface-600';
const iconBtnActive =
  'bg-white/[0.08] border-white/25 text-content-primary';

export default function ToolbarPanel() {
  const router = useRouter();
  const schema = useSchemaStore(s => s.schema);
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const dbType = useSchemaStore(s => s.dbType);
  const naiModel = useSchemaStore(s => s.naiModel);
  const lastGenerationPrompt = useSchemaStore(s => s.lastGenerationPrompt);
  const lastGenerationAnswers = useSchemaStore(s => s.lastGenerationAnswers);
  const { checkAccess } = useAIGateway();
  const { getNodes, getEdges } = useReactFlow();
  const [isGeneratingAlternative, setIsGeneratingAlternative] = useState(false);
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

  const apiKey = useByokStore(s => s.apiKey);
  const showToast = useToastStore(state => state.showToast);

  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const [isSharing, setIsSharing] = useState(false);
  const [isExplaining, setIsExplaining] = useState(false);
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

  const openAiSettings = () => {
    window.dispatchEvent(new CustomEvent('namines:open-ai-settings'));
  };

  const handleExplainSchema = async () => {
    if (!schema || schema.tables.length === 0) {
      showToast('Add some tables first before explaining the schema.', 'warning');
      return;
    }
    setIsExplaining(true);
    try {
      const readme = await schemaService.generateReadme(schema, 'en');
      setExplanation(readme);
    } catch {
      showToast('Failed to generate schema explanation. Please try again.', 'error');
    } finally {
      setIsExplaining(false);
    }
  };

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
    if (!isAuthenticated) {
      showToast('Sign in to request a database change review.', 'warning');
      return;
    }
    if (!activeProjectId) {
      showToast('Save your project first (generate a schema and it will auto-save).', 'warning');
      return;
    }
    if (!schema || schema.tables.length === 0) {
      showToast('Add some tables first before requesting a review.', 'warning');
      return;
    }
    setIsRequestingReview(true);
    try {
      const currentSchema = flowToSchema(schema, getNodes(), getEdges()) || schema;
      const { id } = await changeRequestService.createQuick(activeProjectId, currentSchema);
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

    setIsGeneratingAlternative(true);
    try {
      const alt = await schemaService.generateSchema(
        lastGenerationPrompt, dbType, naiModel, undefined, undefined, lastGenerationAnswers ?? undefined
      );
      setAlternativeSchema(alt);
    } catch {
      showToast('Failed to generate an alternative. Please try again.', 'error');
    } finally {
      setIsGeneratingAlternative(false);
    }
  };

  const shareRoomLink = () => {
    if (!roomId) return;
    const shareUrl = window.location.protocol + '//' + window.location.host + window.location.pathname + '?roomId=' + roomId;
    navigator.clipboard.writeText(shareUrl)
      .then(() => {
        showToast('Live Share link copied to clipboard! You can invite other designers to this room.', 'success');
      })
      .catch(() => {
        showToast('Share link: ' + shareUrl, 'info');
      });
    setIsShareOpen(false);
  };

  return (
    <>
      <div className="fixed top-2.5 right-6 z-[60] flex items-center gap-1.5">
        {/* AI Explain Schema */}
        <button
          onClick={handleExplainSchema}
          disabled={isExplaining}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="Explain schema with AI"
          aria-label="Explain schema with AI"
        >
          {isExplaining ? <Loader2 className="w-4 h-4 animate-spin" /> : <BookOpen className="w-4 h-4" />}
        </button>

        {/* AI Settings */}
        <button
          onClick={openAiSettings}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="AI & BYOK Settings"
          aria-label="AI & BYOK Settings"
        >
          <Settings className="w-4 h-4" />
          {apiKey && (
            <span className="absolute top-1 right-1 w-1.5 h-1.5 rounded-full bg-success" />
          )}
        </button>

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
            {isConnected && <span className="w-1.5 h-1.5 rounded-full bg-success" />}
            <ChevronDown className={`w-3 h-3 transition-transform ${isShareOpen ? 'rotate-180' : ''}`} />
          </button>

          {isShareOpen && (
            <div className="absolute right-0 top-full mt-2 w-56 rounded-xl border border-content-primary/15 bg-surface-800/95 backdrop-blur-xl p-1.5 shadow-[0_8px_32px_rgba(0,0,0,0.4)] z-50 flex flex-col gap-0.5 animate-dropdown-in">
              <button
                onClick={handleShareReadOnly}
                disabled={isSharing}
                className="flex items-center gap-2.5 px-3 py-2 rounded-lg text-left text-xs font-medium text-content-primary hover:bg-white/[0.04] hover:text-content-primary transition-all disabled:opacity-50"
              >
                {isSharing ? <Loader2 className="w-3.5 h-3.5 animate-spin shrink-0" /> : <Copy className="w-3.5 h-3.5 shrink-0" />}
                <span>
                  <span className="block">Copy read-only link</span>
                  <span className="block text-[10px] text-content-muted">Anyone with the link can view</span>
                </span>
              </button>
              <button
                onClick={shareRoomLink}
                disabled={!isConnected}
                className="flex items-center gap-2.5 px-3 py-2 rounded-lg text-left text-xs font-medium text-content-primary hover:bg-white/[0.04] hover:text-content-primary transition-all disabled:opacity-40 disabled:cursor-not-allowed"
              >
                <Users className="w-3.5 h-3.5 shrink-0" />
                <span>
                  <span className="block">Copy live session link</span>
                  <span className="block text-[10px] text-content-muted">
                    {isConnected ? 'Real-time co-editing room' : 'Connect to a room first'}
                  </span>
                </span>
              </button>
            </div>
          )}
        </div>

        {/* SQL Console */}
        <button
          onClick={toggleSqlExplorer}
          className={`${iconBtnBase} ${isSqlExplorerOpen ? iconBtnActive : iconBtnIdle}`}
          title="Open Live SQL Console"
          aria-label="Open Live SQL Console"
        >
          <Terminal className="w-4 h-4" />
        </button>

        {/* Import from live DB */}
        <button
          onClick={() => setIsDbConnectOpen(true)}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="Import schema from a live database"
          aria-label="Import schema from a live database"
        >
          <Database className="w-4 h-4" />
        </button>

        {/* G14 — Minimal Gateway: browse live data read-only */}
        <button
          onClick={() => setIsGatewayOpen(true)}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="Browse live data (read-only)"
          aria-label="Browse live data (read-only)"
        >
          <Table className="w-4 h-4" />
        </button>

        {/* Migration */}
        <button
          onClick={() => setIsMigrationOpen(true)}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="Open Migration Engine Panel"
          aria-label="Open Migration Engine Panel"
        >
          <History className="w-4 h-4" />
        </button>

        {/* Cross-database relations — second-phase/10-COKLU-DB.md */}
        <button
          onClick={() => setIsCrossDbOpen(true)}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="Cross-database relations (links to other projects)"
          aria-label="Cross-database relations"
        >
          <Network className="w-4 h-4" />
        </button>

        {/* Schema from code — second-phase/11-KODDAN-SEMA.md */}
        <button
          onClick={() => setIsCodeImportOpen(true)}
          className={`${iconBtnBase} ${iconBtnIdle}`}
          title="Extract schema from Prisma / EF Core code"
          aria-label="Extract schema from code"
        >
          <FileCode2 className="w-4 h-4" />
        </button>

        {/* Generate Alternative — second-phase/09-SEMA-ALTERNATIFLERI.md.
            Maliyet buton metninde açık: bu ikinci bir üretim turu. */}
        <button
          onClick={handleGenerateAlternative}
          disabled={isGeneratingAlternative || !lastGenerationPrompt}
          className={`${iconBtnBase} w-auto px-3 gap-1.5 ${iconBtnIdle}`}
          title={lastGenerationPrompt ? 'Generate an alternative schema (~1 round)' : 'Generate a schema from a prompt first'}
          aria-label="Generate an alternative schema"
        >
          {isGeneratingAlternative ? <Loader2 className="w-4 h-4 animate-spin" /> : <Sparkles className="w-4 h-4" />}
          <span className="text-xs font-semibold">Alternative (~1 round)</span>
        </button>

        {/* Request Review — "Database PR" akışını başlatır (bkz. new-phase/29) */}
        <button
          onClick={handleRequestReview}
          disabled={isRequestingReview}
          className={`${iconBtnBase} w-auto px-3 gap-1.5 ${iconBtnIdle}`}
          title="Request a database change review"
          aria-label="Request a database change review"
        >
          {isRequestingReview ? <Loader2 className="w-4 h-4 animate-spin" /> : <GitPullRequest className="w-4 h-4" />}
          <span className="text-xs font-semibold">Request Review</span>
        </button>

        <div className="h-5 w-px bg-content-primary/10 mx-0.5" />

        {/* Approve Diagram — tek CTA: off-white/off-black, sonraki adıma geçişi
            ok ile belirtir, dokunsal geri bildirim için active:scale. */}
        <button
          id="approve-diagram-btn"
          onClick={handleApprove}
          className="group flex items-center justify-center gap-1.5 bg-content-primary hover:bg-content-primary-hover active:scale-[0.97] text-surface-900 pl-4 pr-3 h-9 rounded-lg text-xs font-semibold transition-all cursor-pointer"
        >
          <span>Approve</span>
          <ArrowRight className="w-3.5 h-3.5 transition-transform group-hover:translate-x-0.5" />
        </button>
      </div>

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
            className="bg-surface-800 border border-content-primary/12 rounded-2xl shadow-[0_20px_60px_rgba(0,0,0,0.6)] w-full max-w-xl mx-4 flex flex-col animate-in fade-in zoom-in-95 duration-150"
            style={{ maxHeight: '80vh' }}
            onClick={e => e.stopPropagation()}
          >
            <div className="flex items-center justify-between px-5 pt-4 pb-3 border-b border-content-primary/10">
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
                  className="p-1.5 text-content-subtle hover:text-content-primary rounded-md hover:bg-white/[0.06] transition-colors cursor-pointer"
                  aria-label="Copy explanation"
                  title="Copy as Markdown"
                >
                  {explanationCopied ? <Check className="w-3.5 h-3.5 text-success-text" /> : <Copy className="w-3.5 h-3.5" />}
                </button>
                <button onClick={() => setExplanation(null)} className="p-1.5 text-content-subtle hover:text-content-primary rounded-md hover:bg-white/[0.06] transition-colors cursor-pointer" aria-label="Close">
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
