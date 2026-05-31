'use client';

import { useEffect, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { X, Trash2, FolderOpen, Clock, Database, Plus, Sparkles } from 'lucide-react';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { ProjectSnapshot } from '../../types/project';
import { DbType } from '../../store/useSchemaStore';

interface ProjectSidebarProps {
  isOpen: boolean;
  onClose: () => void;
}

const DB_BADGE_COLORS: Record<DbType, string> = {
  MSSQL:      'bg-sky-500/15 text-sky-400 border-sky-500/30',
  PostgreSQL: 'bg-blue-500/15 text-blue-400 border-blue-500/30',
  MySQL:      'bg-orange-500/15 text-orange-400 border-orange-500/30',
  SQLite:     'bg-teal-500/15 text-teal-400 border-teal-500/30',
  Oracle:     'bg-red-500/15 text-red-400 border-red-500/30',
  MariaDB:    'bg-purple-500/15 text-purple-400 border-purple-500/30',
  Db2:        'bg-indigo-500/15 text-indigo-400 border-indigo-500/30',
  Firebird:   'bg-rose-500/15 text-rose-400 border-rose-500/30',
  Spanner:    'bg-emerald-500/15 text-emerald-400 border-emerald-500/30',
  Redshift:   'bg-amber-500/15 text-amber-400 border-amber-500/30',
};

function formatDate(iso: string): string {
  const d = new Date(iso);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const diffMins = Math.floor(diffMs / 60000);
  const diffHours = Math.floor(diffMins / 60);
  const diffDays = Math.floor(diffHours / 24);

  if (diffMins < 1)   return 'Az önce';
  if (diffMins < 60)  return `${diffMins} dk önce`;
  if (diffHours < 24) return `${diffHours} sa önce`;
  if (diffDays < 7)   return `${diffDays} gün önce`;
  return d.toLocaleDateString('tr-TR', { day: 'numeric', month: 'short', year: 'numeric' });
}

export default function ProjectSidebar({ isOpen, onClose }: ProjectSidebarProps) {
  const router = useRouter();
  const overlayRef = useRef<HTMLDivElement>(null);

  const { projects, loadProject, deleteProject, setActiveProjectId } = useProjectHistoryStore();
  const { loadFromSchema, setDbType, resetProject } = useSchemaStore();

  // ESC ile kapatma
  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    if (isOpen) document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [isOpen, onClose]);

  // Overlay tıklamasında kapat
  const handleOverlayClick = (e: React.MouseEvent) => {
    if (e.target === overlayRef.current) onClose();
  };

  const handleLoadProject = (project: ProjectSnapshot) => {
    setActiveProjectId(project.id);
    setDbType(project.dbType);
    loadFromSchema(project.schema, project.nodePositions);
    onClose();
    router.push('/canvas');
  };

  const handleDeleteProject = (e: React.MouseEvent, id: string) => {
    e.stopPropagation();
    if (confirm('Bu projeyi silmek istediğinize emin misiniz?')) {
      deleteProject(id);
    }
  };

  const handleNewProject = () => {
    resetProject();
    setActiveProjectId(null);
    onClose();
    router.push('/');
  };

  // Tarihe göre sıralı (en yeni önce)
  const sortedProjects = [...projects].sort(
    (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
  );

  return (
    <>
      {/* Overlay */}
      <div
        ref={overlayRef}
        onClick={handleOverlayClick}
        className={`fixed inset-0 bg-black/60 backdrop-blur-sm z-[60] transition-opacity duration-300 ${isOpen ? 'opacity-100 pointer-events-auto' : 'opacity-0 pointer-events-none'}`}
        aria-hidden={!isOpen}
      />

      {/* Drawer */}
      <aside
        className={`fixed top-0 left-0 h-full w-[340px] bg-gradient-to-b from-[#060D1A] to-[#0B152A] border-r border-indigo-500/20 z-[70] transform transition-transform duration-300 ease-[cubic-bezier(0.16,1,0.3,1)] flex flex-col ${isOpen ? 'translate-x-0 shadow-[50px_0_50px_rgba(59,130,246,0.1)]' : '-translate-x-full shadow-none'}`}
        role="dialog"
        aria-label="Proje Geçmişi"
        aria-modal="true"
      >
        {/* Background Stars Overlay */}
        <div className="absolute inset-0 pointer-events-none bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-indigo-900/20 via-transparent to-transparent opacity-60" />

        {/* Header */}
        <div className="relative px-6 pt-8 pb-4 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-3">
            <h2 className="text-xl font-bold tracking-wide bg-gradient-to-r from-cyan-300 to-indigo-300 bg-clip-text text-transparent flex items-center">
              Workspace
            </h2>
          </div>
          <div className="flex items-center gap-3">
            <div className="relative">
              <div className="absolute -inset-1 bg-indigo-500/30 blur-sm rounded-full" />
              <div className="relative w-8 h-8 rounded-full border border-indigo-400/30 bg-[#0B1120] flex items-center justify-center text-indigo-300 text-sm font-medium shadow-[0_0_15px_rgba(99,102,241,0.4)]">
                {projects.length}
              </div>
              <Sparkles className="w-3 h-3 text-cyan-300 absolute -top-1 -right-1" />
            </div>
          </div>
        </div>

        {/* New Project Button */}
        <div className="relative px-6 pb-6 pt-2 shrink-0">
          <button
            onClick={handleNewProject}
            className="w-full relative group overflow-hidden rounded-xl border border-indigo-400/40 bg-gradient-to-r from-indigo-950/80 to-purple-900/40 p-4 transition-all hover:border-indigo-400/70 hover:shadow-[0_0_20px_rgba(99,102,241,0.3)] flex items-center justify-between backdrop-blur-sm"
          >
            <div className="absolute inset-0 bg-[url('/noise.png')] opacity-10 mix-blend-overlay pointer-events-none" />
            <span className="text-indigo-100 font-medium tracking-wide relative z-10">New Project</span>
            <div className="w-6 h-6 rounded-full border border-indigo-300/30 bg-white/5 flex items-center justify-center text-indigo-200 group-hover:bg-indigo-400/20 transition-colors relative z-10">
              <Plus className="w-3.5 h-3.5" />
            </div>
          </button>
        </div>

        {/* Project List */}
        <div className="flex-1 overflow-y-auto px-6 pb-8 custom-scrollbar relative z-10 space-y-4">
          {sortedProjects.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-40 text-center">
              <div className="w-12 h-12 rounded-full border border-indigo-500/20 bg-indigo-500/5 flex items-center justify-center mb-3">
                <FolderOpen className="w-5 h-5 text-indigo-400" />
              </div>
              <p className="text-sm text-indigo-200/60 leading-relaxed">
                Henüz kayıtlı proje yok.<br />
                Şema üretince burada görünecek.
              </p>
            </div>
          ) : (
            sortedProjects.map((project) => (
              <div
                key={project.id}
                className="group relative overflow-hidden rounded-xl border border-[#1E293B] bg-gradient-to-br from-[#1E293B]/70 to-[#0F172A]/90 p-4 cursor-pointer transition-all hover:border-indigo-500/50 hover:shadow-[0_0_20px_rgba(99,102,241,0.15)] backdrop-blur-md flex flex-col gap-4"
                onClick={() => handleLoadProject(project)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => e.key === 'Enter' && handleLoadProject(project)}
              >
                {/* Decorative Wave SVG (Bottom) */}
                <div className="absolute bottom-0 left-0 w-full h-1/2 pointer-events-none opacity-[0.03] group-hover:opacity-[0.08] transition-opacity">
                  <svg viewBox="0 0 400 150" preserveAspectRatio="none" className="w-full h-full fill-indigo-400">
                    <path d="M0,50 C100,150 300,0 400,100 L400,150 L0,150 Z" />
                  </svg>
                </div>

                {/* Top Row: Icon + Title */}
                <div className="flex items-center gap-3 relative z-10">
                  <div className="w-10 h-10 rounded-xl border border-indigo-400/30 bg-indigo-900/30 flex items-center justify-center shrink-0 shadow-[0_0_10px_rgba(99,102,241,0.2)]">
                    <Database className="w-4 h-4 text-indigo-300" />
                    <Sparkles className="w-2 h-2 text-cyan-300 absolute top-2 right-2 opacity-70" />
                  </div>
                  <div className="flex-1 min-w-0">
                    <p className="text-zinc-100 font-medium truncate tracking-wide">{project.name}</p>
                  </div>
                </div>

                {/* Middle Row: Badge + Date */}
                <div className="flex items-center justify-between relative z-10">
                  <span className={`px-2.5 py-0.5 rounded-md text-xs font-medium border ${DB_BADGE_COLORS[project.dbType] ?? 'bg-zinc-800/50 border-zinc-700 text-zinc-400'}`}>
                    {project.dbType}
                  </span>
                  <div className="flex items-center gap-1.5 text-zinc-400 text-xs">
                    <Sparkles className="w-3 h-3 text-indigo-400/70" />
                    <span>{formatDate(project.updatedAt)}</span>
                  </div>
                </div>

                {/* Bottom Row: Separator & Stats & Actions */}
                <div className="relative z-10 pt-3 border-t border-indigo-500/10 flex items-center justify-between">
                  <button
                    onClick={(e) => handleDeleteProject(e, project.id)}
                    className="p-1.5 -ml-1.5 rounded-md text-zinc-500 hover:text-red-400 hover:bg-red-400/10 transition-colors opacity-0 group-hover:opacity-100 shrink-0 flex items-center gap-1.5"
                    title="Projeyi sil"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                  </button>
                  <span className="text-xs text-indigo-200/50">
                    {project.schema.tables.length} tablo · {project.schema.relations.length} ilişki
                  </span>
                </div>
              </div>
            ))
          )}
        </div>
      </aside>
    </>
  );
}
