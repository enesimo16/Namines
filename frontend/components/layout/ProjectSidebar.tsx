'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { X, Trash2, FolderOpen, Database, Plus } from 'lucide-react';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { ProjectSnapshot } from '../../types/project';

interface ProjectSidebarProps {
  isOpen: boolean;
  onClose: () => void;
}

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

  const { projects, deleteProject, setActiveProjectId } = useProjectHistoryStore();
  const { loadFromSchema, setDbType, resetProject, recordGenerationSource } = useSchemaStore();

  const [projectToDelete, setProjectToDelete] = useState<string | null>(null);

  useEffect(() => {
    const handleKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose();
    };
    if (isOpen) document.addEventListener('keydown', handleKey);
    return () => document.removeEventListener('keydown', handleKey);
  }, [isOpen, onClose]);

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
    // Üretim kaynağını da geri yükle — "Alternatif üret" (second-phase/09) buna
    // bağlı. Yüklenmezse buton, workspace'ten açılan HER projede ölü kalır.
    recordGenerationSource(project.generationPrompt ?? '', project.generationAnswers ?? null);
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
    router.push('/new');
  };

  const sortedProjects = [...projects].sort(
    (a, b) => new Date(b.updatedAt).getTime() - new Date(a.updatedAt).getTime()
  );

  return (
    <>
      {/* Overlay */}
      <div
        ref={overlayRef}
        onClick={handleOverlayClick}
        className={`fixed inset-0 bg-scrim/60 backdrop-blur-sm z-drawer-backdrop transition-opacity duration-300 ${isOpen ? 'opacity-100 pointer-events-auto' : 'opacity-0 pointer-events-none'}`}
        aria-hidden={!isOpen}
      />

      {/* Drawer — mobilde ekran genişliğine göre daralır */}
      <aside
        className={`fixed top-0 left-0 h-full w-[88vw] max-w-[340px] bg-surface-900/98 backdrop-blur-2xl border-r border-line-strong z-drawer transform transition-transform duration-300 ease-[cubic-bezier(0.16,1,0.3,1)] flex flex-col shadow-2xl ${isOpen ? 'translate-x-0' : '-translate-x-full'}`}
        role="dialog"
        aria-label="Project History"
        aria-modal="true"
      >
        {/* Header */}
        <div className="px-5 pt-5 pb-3.5 flex items-center justify-between shrink-0 border-b border-line">
          <div className="flex items-center gap-2.5">
            <h2 className="text-sm font-bold text-content-primary">Workspace</h2>
            <span className="w-5 h-5 rounded-full border border-line bg-surface-800 flex items-center justify-center text-content-secondary text-[10px] font-mono font-bold">
              {projects.length}
            </span>
          </div>
          <button
            onClick={onClose}
            className="tap-44 w-7 h-7 flex items-center justify-center text-content-muted hover:text-content-primary rounded-full border border-line bg-surface-800/80 hover:bg-surface-700/80 transition-colors cursor-pointer"
            aria-label="Close workspace"
          >
            <X className="w-3.5 h-3.5" />
          </button>
        </div>

        {/* New Project Button */}
        <div className="px-5 py-3.5 shrink-0">
          <button
            onClick={handleNewProject}
            className="w-full rounded-full border border-line-strong bg-accent hover:bg-accent-hover text-accent-on py-2.5 px-4 transition-all flex items-center justify-between text-xs font-mono font-bold uppercase tracking-wider shadow-sm cursor-pointer active:scale-[0.98]"
          >
            <span>New Project</span>
            <Plus className="w-3.5 h-3.5 text-accent-on" />
          </button>
        </div>

        {/* Project List */}
        <div className="flex-1 overflow-y-auto px-5 pb-6 space-y-2.5">
          {sortedProjects.length === 0 ? (
            <div className="flex flex-col items-center justify-center h-48 text-center">
              <div className="w-10 h-10 rounded-full border border-line bg-surface-800 flex items-center justify-center mb-3">
                <FolderOpen className="w-4 h-4 text-content-muted" />
              </div>
              <p className="text-xs text-content-muted leading-relaxed">
                No saved projects yet.<br />
                They will appear here once you generate a schema.
              </p>
            </div>
          ) : (
            sortedProjects.map((project) => (
              <div
                key={project.id}
                className="group rounded-[var(--radius-card)] border border-line-strong bg-surface-800/80 hover:bg-surface-800 hover:border-line-subtle p-3.5 cursor-pointer transition-all flex flex-col gap-2.5 shadow-sm hover:-translate-y-0.5"
                onClick={() => handleLoadProject(project)}
                role="button"
                tabIndex={0}
                onKeyDown={(e) => e.key === 'Enter' && handleLoadProject(project)}
              >
                <div className="flex items-center gap-2.5">
                  <div className="w-8 h-8 rounded-full border border-line bg-surface-700 flex items-center justify-center shrink-0">
                    <Database className="w-3.5 h-3.5 text-content-primary" />
                  </div>
                  <p className="text-content-primary text-xs font-semibold truncate flex-1 min-w-0">{project.name}</p>
                </div>

                <div className="flex items-center justify-between">
                  <span className="px-2 py-0.5 rounded-full text-[10px] font-mono font-semibold border border-line bg-surface-700/80 text-content-primary">
                    {project.dbType}
                  </span>
                  <span className="text-content-muted text-[10px] font-mono">{formatDate(project.updatedAt)}</span>
                </div>

                <div className="pt-2 border-t border-line flex items-center justify-between">
                  <button
                    onClick={(e) => handleDeleteProject(e, project.id)}
                    className="tap-44 p-1 -ml-1 rounded-full text-content-muted hover:text-danger-text hover:bg-surface-700 transition-colors opacity-0 group-hover:opacity-100 shrink-0 cursor-pointer"
                    title="Delete project"
                    aria-label="Delete project"
                  >
                    <Trash2 className="w-3.5 h-3.5" />
                  </button>
                  <span className="text-[10px] font-mono text-content-muted">
                    {project.schema?.tables?.length ?? 0} tables · {project.schema?.relations?.length ?? 0} relations
                  </span>
                </div>
              </div>
            ))
          )}
        </div>
      </aside>

      {/* Delete Confirmation Modal */}
      {projectToDelete && (
        <div className="fixed inset-0 z-modal flex items-center justify-center p-4 bg-scrim/70 backdrop-blur-sm animate-fade-in">
          <div className="relative w-full max-w-sm bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] shadow-2xl overflow-hidden flex flex-col p-5">
            <div className="flex items-center justify-between pb-3 border-b border-line">
              <div className="flex items-center gap-2 text-danger-text">
                <Trash2 className="w-4 h-4 shrink-0" />
                <h3 className="text-xs font-bold uppercase tracking-wider font-mono">Delete Project</h3>
              </div>
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  setProjectToDelete(null);
                }}
                className="p-1 hover:bg-surface-800 rounded-full text-content-muted hover:text-content-primary transition-all cursor-pointer"
                aria-label="Close"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="my-4 flex flex-col gap-1.5">
              <span className="text-sm font-semibold text-content-primary">
                Are you sure you want to delete this project?
              </span>
              <p className="text-xs text-content-muted leading-relaxed">
                This action is permanent and cannot be undone. All database tables and branch history for this project will be deleted from your storage.
              </p>
            </div>

            <div className="flex gap-2.5">
              <button
                onClick={(e) => {
                  e.stopPropagation();
                  setProjectToDelete(null);
                }}
                className="flex-1 py-2 px-4 rounded-full border border-line bg-surface-800 hover:bg-surface-700 text-content-primary font-mono text-xs font-semibold transition-all cursor-pointer"
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
                className="flex-1 py-2 px-4 rounded-full bg-danger-subtle hover:bg-danger/25 text-danger-text border border-danger/30 font-mono text-xs font-semibold transition-all cursor-pointer"
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
