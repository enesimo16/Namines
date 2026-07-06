'use client';

import { useEffect, useRef, useState } from 'react';
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

  if (diffMins < 1)   return 'Just now';
  if (diffMins < 60)  return `${diffMins}m ago`;
  if (diffHours < 24) return `${diffHours}h ago`;
  if (diffDays < 7)   return `${diffDays}d ago`;
  return d.toLocaleDateString('en-US', { day: 'numeric', month: 'short', year: 'numeric' });
}

export default function ProjectSidebar({ isOpen, onClose }: ProjectSidebarProps) {
  const router = useRouter();
  const overlayRef = useRef<HTMLDivElement>(null);

  const { projects, loadProject, deleteProject, setActiveProjectId } = useProjectHistoryStore();
  const { loadFromSchema, setDbType, resetProject } = useSchemaStore();

  const [projectToDelete, setProjectToDelete] = useState<string | null>(null);

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
    if (project.schema) {
      project.schema.name = project.name;
    }
    loadFromSchema(project.schema, project.nodePositions, false);
    onClose();
    router.push('/canvas');
  };

  const handleDeleteProject = (e: React.MouseEvent, id: string) => {
    e.stopPropagation();
    setProjectToDelete(id);
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
        aria-label="Project History"
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
            <div className="absolute inset-0 bg-[url('data:image/svg+xml,%3Csvg%20viewBox=%220%200%20200%20200%22%20xmlns=%22http://www.w3.org/2000/svg%22%3E%3Cfilter%20id=%22noiseFilter%22%3E%3CfeTurbulence%20type=%22fractalNoise%22%20baseFrequency=%220.65%22%20numOctaves=%223%22%20stitchTiles=%22stitch%22/%3E%3C/filter%3E%3Crect%20width=%22100%25%22%20height=%22100%25%22%20filter=%22url(%23noiseFilter)%22/%3E%3C/svg%3E')] opacity-10 mix-blend-overlay pointer-events-none" />
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
                No saved projects yet.<br />
                They will appear here once you generate a schema.
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
                    title="Delete project"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                  </button>
                  <span className="text-xs text-indigo-200/50">
                    {project.schema.tables.length} tables · {project.schema.relations.length} relations
                  </span>
                </div>
              </div>
            ))
          )}
        </div>
      </aside>

      {/* Workspace Delete Confirmation Modal */}
      {projectToDelete && (
        <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-black/80 backdrop-blur-sm animate-fade-in">
          {/* Glow Backdrop Orbs */}
          <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[400px] h-[400px] bg-gradient-to-tr from-rose-500/10 to-indigo-500/10 rounded-full blur-[80px] pointer-events-none -z-10" />

          {/* Main Glassmorphic Container */}
          <div className="relative w-full max-w-md bg-[#09111F]/95 backdrop-blur-2xl border border-red-500/20 rounded-3xl shadow-[0_20px_60px_rgba(0,0,0,0.85)] overflow-hidden flex flex-col p-6 pb-8 animate-in zoom-in-95 duration-200 animate-none">
            {/* Header */}
            <div className="flex items-center justify-between pb-4 border-b border-zinc-800">
              <div className="flex items-center gap-2 text-red-400">
                <Trash2 className="w-5 h-5 shrink-0" />
                <h3 className="text-sm font-extrabold uppercase tracking-wider text-red-100">
                  Delete Project
                </h3>
              </div>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  setProjectToDelete(null);
                }}
                className="p-1 hover:bg-white/5 rounded-lg text-zinc-400 hover:text-white transition-all cursor-pointer"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            {/* Content */}
            <div className="my-5 flex flex-col gap-2">
              <span className="text-sm font-bold text-zinc-100 leading-snug">
                Are you sure you want to delete this project?
              </span>
              <p className="text-[11px] text-zinc-400 leading-relaxed font-semibold">
                This action is permanent and cannot be undone. All database tables and branch history for this project will be deleted from your storage.
              </p>
            </div>

            {/* Actions */}
            <div className="flex gap-3">
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  setProjectToDelete(null);
                }}
                className="flex-1 py-3 px-4 rounded-xl border border-zinc-700/60 bg-[#121824] hover:bg-zinc-800 text-zinc-300 hover:text-white font-bold text-xs tracking-wider uppercase transition-all duration-200 flex items-center justify-center cursor-pointer"
              >
                Cancel
              </button>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  if (projectToDelete) {
                    deleteProject(projectToDelete);
                    setProjectToDelete(null);
                  }
                }}
                className="flex-1 py-3 px-4 rounded-xl bg-red-600 hover:bg-red-500 text-white font-extrabold text-xs tracking-wider uppercase transition-all duration-300 shadow-[0_4px_15px_rgba(239,68,68,0.3)] hover:scale-[1.02] active:scale-[0.98] flex items-center justify-center cursor-pointer"
              >
                Delete
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
