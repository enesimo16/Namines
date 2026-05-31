import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import { CheckCircle, Sparkles, History, Users, Terminal, Activity } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useReactFlow } from '@xyflow/react';
import { flowToSchema } from '../../../lib/flowToSchema';
import MigrationWizard from '../../migration/MigrationWizard';
import { useMultiplayerStore } from '../../../store/useMultiplayerStore';
import { useSqlExplorerStore } from '../../../store/useSqlExplorerStore';
import { useToastStore } from '../../../store/useToastStore';

export default function ToolbarPanel() {
  const router = useRouter();
  const { schema, loadFromSchema } = useSchemaStore();
  const { getNodes, getEdges } = useReactFlow();
  const [isMigrationOpen, setIsMigrationOpen] = useState(false);

  // Multiplayer and SQL Explorer stores
  const { isConnected, roomId } = useMultiplayerStore();
  const isSqlExplorerOpen = useSqlExplorerStore(state => state.isOpen);
  const toggleSqlExplorer = useSqlExplorerStore(state => state.toggleOpen);

  const showToast = useToastStore(state => state.showToast);

  const handleApprove = () => {
    // Sync current UI state back to schema before leaving
    const updatedSchema = flowToSchema(schema, getNodes(), getEdges());
    if (updatedSchema) {
      loadFromSchema(updatedSchema);
    }
    router.push('/compile');
  };

  const shareRoomLink = () => {
    if (!roomId) return;
    const shareUrl = window.location.protocol + '//' + window.location.host + window.location.pathname + '?roomId=' + roomId;
    navigator.clipboard.writeText(shareUrl)
      .then(() => {
        showToast('Canlı Paylaşım linki panoya kopyalandı! Diğer tasarımcıları bu odaya çağırabilirsiniz.', 'success');
      })
      .catch(() => {
        showToast('Paylaşım linki: ' + shareUrl, 'info');
      });
  };

  return (
    <>
      <div className="fixed top-[8px] right-6 z-[60] flex items-center gap-3">
        {/* Canlı Paylaşım (SignalR Room Share) */}
        {isConnected && (
          <button
            onClick={shareRoomLink}
            className="group relative flex items-center justify-center gap-2 bg-[#0F172A]/90 hover:bg-[#1E293B] text-pink-400 hover:text-pink-200 px-4 py-2 rounded-[10px] text-[14px] font-bold transition-all border border-pink-500/20 hover:border-pink-500/40 shadow-md h-10"
            title="Eşzamanlı Çalışma Odası Linkini Kopyala"
          >
            <Users className="w-4 h-4 text-pink-400" />
            <span className="tracking-wide">Canlı Paylaşım</span>
            <span className="relative flex h-2 w-2">
              <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
              <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
            </span>
          </button>
        )}

        {/* SQL Konsolu (SQL Explorer) */}
        <button
          onClick={toggleSqlExplorer}
          className={`group relative flex items-center justify-center gap-2 px-4 py-2 rounded-[10px] text-[14px] font-bold transition-all border shadow-md h-10 ${
            isSqlExplorerOpen
              ? 'bg-indigo-600/30 text-indigo-200 border-indigo-500/40 shadow-[0_0_15px_rgba(99,102,241,0.25)]'
              : 'bg-[#0F172A]/90 hover:bg-[#1E293B] text-indigo-400 hover:text-indigo-200 border-indigo-500/20'
          }`}
          title="Canlı SQL Konsolunu Aç"
        >
          <Terminal className="w-4 h-4 text-indigo-400" />
          <span className="tracking-wide">SQL Konsolu</span>
        </button>

        {/* Migration Butonu */}
        <button
          onClick={() => setIsMigrationOpen(true)}
          className="group relative flex items-center justify-center gap-2 bg-[#0F172A]/90 hover:bg-[#1E293B] text-indigo-300 hover:text-white px-4 py-2 rounded-[10px] text-[14px] font-bold transition-all border border-indigo-500/30 hover:border-indigo-400/50 shadow-md h-10"
          title="Migration Engine Panelini Aç"
        >
          <History className="w-4 h-4 text-indigo-400" />
          <span className="tracking-wide">Migration</span>
        </button>

        {/* Diyagramı Onayla Butonu */}
        <button
          onClick={handleApprove}
          className="group relative flex items-center justify-center gap-2 bg-gradient-to-r from-[#4f46e5] to-[#6366f1] hover:from-[#5b4ff8] hover:to-[#818cf8] text-white px-5 py-2 rounded-[10px] text-[14px] font-bold transition-all border border-[#818cf8]/40 shadow-[0_0_15px_rgba(79,70,229,0.5)] overflow-hidden h-10"
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
            <Sparkles className="w-5 h-5 text-indigo-100 animate-pulse" />
          </div>
          
          {/* Shine effect overlay */}
          <div className="absolute top-0 -left-[100%] group-hover:left-[100%] duration-[1200ms] transition-all ease-in-out w-12 h-full bg-gradient-to-r from-transparent via-white/20 to-transparent skew-x-[30deg] z-10 pointer-events-none" />

          <CheckCircle className="w-4 h-4 relative z-10" />
          <span className="relative z-10 tracking-wide">Diyagramı Onayla</span>
        </button>
      </div>

      {/* Migration Engine Sihirbazı */}
      <MigrationWizard
        isOpen={isMigrationOpen}
        onClose={() => setIsMigrationOpen(false)}
      />
    </>
  );
}
