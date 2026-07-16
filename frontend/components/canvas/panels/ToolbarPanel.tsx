import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import { CheckCircle, Sparkles, History, Users, Terminal, Activity, Settings, Link2, Loader2, Database, BookOpen, X } from 'lucide-react';
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
import { authService, schemaService } from '../../../services/api';
import DbConnectionPanel from './DbConnectionPanel';

export default function ToolbarPanel() {
  const router = useRouter();
  // Dar selector'lar — selector'suz çağrı tüm store'a abone olur ve her
  // değişiklikte bu paneli gereksiz yere yeniden render eder.
  const schema = useSchemaStore(s => s.schema);
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);
  const { getNodes, getEdges } = useReactFlow();
  const [isMigrationOpen, setIsMigrationOpen] = useState(false);
  const [isDbConnectOpen, setIsDbConnectOpen] = useState(false);

  // Multiplayer and SQL Explorer stores
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
        // Pozisyonları koru: nodePositions verilmezse loadFromSchema node'ları
        // schemaToFlow'un ham ızgarasına (col*400, row*300) geri döker ve
        // kullanıcının elle yaptığı tüm yerleşim kaybolur.
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
      // Force sync with cloud first to ensure project is saved in database
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
    } catch {
      showToast('Failed to generate share link. Try again.', 'error');
    } finally {
      setIsSharing(false);
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
  };

  return (
    <>
      <div className="fixed top-[8px] right-6 z-[60] flex items-center gap-3">
        {/* AI Explain Schema button */}
        <button
          onClick={handleExplainSchema}
          disabled={isExplaining}
          className="relative flex items-center justify-center bg-surface-700/90 hover:bg-surface-600 text-amber-400 hover:text-amber-200 w-10 h-10 rounded-xl transition-all border border-amber-500/20 hover:border-amber-500/40 shadow-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
          title="Explain schema with AI"
        >
          {isExplaining ? <Loader2 className="w-4 h-4 animate-spin" /> : <BookOpen className="w-4 h-4" />}
        </button>

        {/* AI Settings button */}
        <button
          onClick={openAiSettings}
          className="relative flex items-center justify-center bg-surface-700/90 hover:bg-surface-600 text-zinc-400 hover:text-zinc-200 w-10 h-10 rounded-xl transition-all border border-indigo-500/20 hover:border-indigo-500/40 shadow-md cursor-pointer"
          title="AI & BYOK Settings"
        >
          <Settings className="w-4 h-4" />
          {apiKey && (
            <span className="absolute top-1.5 right-1.5 flex h-2 w-2">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
            </span>
          )}
        </button>

        {/* Read-only share link */}
        <button
          onClick={handleShareReadOnly}
          disabled={isSharing}
          className="relative flex items-center justify-center gap-2 bg-surface-700/90 hover:bg-surface-600 text-emerald-400 hover:text-emerald-200 px-4 py-2 rounded-xl text-sm font-bold transition-all border border-emerald-500/20 hover:border-emerald-500/40 shadow-md h-10 disabled:opacity-50 disabled:cursor-not-allowed"
          title="Copy read-only share link"
        >
          {isSharing
            ? <Loader2 className="w-4 h-4 animate-spin" />
            : <Link2 className="w-4 h-4" />}
          <span className="tracking-wide">Share</span>
        </button>

        {/* Live Share (SignalR Room Share) */}
        {isConnected && (
          <button
            onClick={shareRoomLink}
            className="group relative flex items-center justify-center gap-2 bg-surface-700/90 hover:bg-surface-600 text-pink-400 hover:text-pink-200 px-4 py-2 rounded-xl text-sm font-bold transition-all border border-pink-500/20 hover:border-pink-500/40 shadow-md h-10 animate-none"
            title="Copy Simultaneous Workspace Link"
          >
            <Users className="w-4 h-4 text-pink-400" />
            <span className="tracking-wide">Live Share</span>
            <span className="relative flex h-2 w-2">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
            </span>
          </button>
        )}

        {/* SQL Console (SQL Explorer) */}
        <button
          onClick={toggleSqlExplorer}
          className={`group relative flex items-center justify-center gap-2 px-4 py-2 rounded-xl text-sm font-bold transition-all border shadow-md h-10 animate-none ${
            isSqlExplorerOpen
              ? 'bg-indigo-600/30 text-indigo-200 border-indigo-500/40 shadow-[0_0_15px_rgba(99,102,241,0.25)]'
              : 'bg-surface-700/90 hover:bg-surface-600 text-indigo-400 hover:text-indigo-200 border-indigo-500/20'
          }`}
          title="Open Live SQL Console"
        >
          <Terminal className="w-4 h-4 text-indigo-400" />
          <span className="tracking-wide">SQL Console</span>
        </button>

        {/* Import from live DB */}
        <button
          onClick={() => setIsDbConnectOpen(true)}
          className="group relative flex items-center justify-center gap-2 bg-surface-700/90 hover:bg-surface-600 text-violet-400 hover:text-violet-200 px-4 py-2 rounded-xl text-sm font-bold transition-all border border-violet-500/20 hover:border-violet-500/40 shadow-md h-10"
          title="Import schema from a live database"
        >
          <Database className="w-4 h-4" />
          <span className="tracking-wide">Import DB</span>
        </button>

        {/* Migration Button */}
        <button
          onClick={() => setIsMigrationOpen(true)}
          className="group relative flex items-center justify-center gap-2 bg-surface-700/90 hover:bg-surface-600 text-indigo-300 hover:text-white px-4 py-2 rounded-xl text-sm font-bold transition-all border border-indigo-500/30 hover:border-indigo-400/50 shadow-md h-10 animate-none"
          title="Open Migration Engine Panel"
        >
          <History className="w-4 h-4 text-indigo-400" />
          <span className="tracking-wide">Migration</span>
        </button>

        {/* Approve Diagram Button */}
        <button
          id="approve-diagram-btn"
          onClick={handleApprove}
          className="group relative flex items-center justify-center gap-2 bg-gradient-to-r from-primary-glow to-primary-glow-hover hover:from-primary-glow-hover hover:to-indigo-400 text-white px-5 py-2 rounded-xl text-sm font-bold transition-all border border-primary-glow-hover/40 shadow-neon overflow-hidden h-10 animate-none"
        >
          {/* Starry Background */}
          <div 
            className="absolute inset-0 opacity-40 mix-blend-screen pointer-events-none"
            style={{
              backgroundImage: 'radial-gradient(1px 1px at 20% 30%, rgba(255,255,255,0.9), transparent), radial-gradient(1px 1px at 80% 40%, rgba(255,255,255,0.8), transparent), radial-gradient(1.5px 1.5px at 40% 70%, rgba(255,255,255,0.9), transparent), radial-gradient(1px 1px at 70% 80%, rgba(255,255,255,0.7), transparent)'
            }}
          />

          {/* Sliding Star Animation on Hover */}
          <div className="absolute top-1/2 -translate-y-1/2 -left-8 group-hover:left-[120%] duration-[1200ms] transition-all ease-in-out z-20 pointer-events-none drop-shadow-[0_0_8px_rgba(255,255,255,0.8)]">
            <Sparkles className="w-5 h-5 text-indigo-100 animate-pulse animate-none" />
          </div>
          
          {/* Shine effect overlay */}
          <div className="absolute top-0 -left-[100%] group-hover:left-[100%] duration-[1200ms] transition-all ease-in-out w-12 h-full bg-gradient-to-r from-transparent via-white/20 to-transparent skew-x-[30deg] z-10 pointer-events-none" />

          <CheckCircle className="w-4 h-4 relative z-10 animate-none" />
          <span className="relative z-10 tracking-wide">Approve Diagram</span>
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

      {/* AI Schema Explanation Modal */}
      {explanation !== null && (
        <div className="fixed inset-0 z-[300] flex items-center justify-center bg-black/60 backdrop-blur-sm" onClick={() => setExplanation(null)}>
          <div
            className="bg-surface-800 border border-surface-500 rounded-2xl shadow-2xl w-full max-w-2xl mx-4 flex flex-col animate-in fade-in zoom-in-95 duration-150"
            style={{ maxHeight: '80vh' }}
            onClick={e => e.stopPropagation()}
          >
            <div className="flex items-center justify-between px-6 pt-5 pb-4 border-b border-surface-600">
              <div className="flex items-center gap-2">
                <BookOpen className="w-5 h-5 text-amber-400" />
                <span className="text-content-primary font-semibold text-base">Schema Explanation</span>
              </div>
              <button onClick={() => setExplanation(null)} className="text-content-muted hover:text-content-primary transition-colors cursor-pointer">
                <X className="w-5 h-5" />
              </button>
            </div>
            <div className="overflow-y-auto px-6 py-5">
              <pre className="text-content-secondary text-xs leading-relaxed whitespace-pre-wrap font-sans">{explanation}</pre>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
