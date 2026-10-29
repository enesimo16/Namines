'use client';

import { Suspense, useCallback, useEffect, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import {
  Table2,
  Loader2,
  Database,
  ArrowRight,
  HardDrive,
} from 'lucide-react';
import { authService } from '../../services/api';
import { useAuthStore } from '../../store/useAuthStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useActiveProjectStore } from '../../store/useActiveProjectStore';
import { ModuleSymbol, ModuleWordmark } from '../../components/layout/ModuleLogo';
import { ProjectDeskStudio } from '../../components/desk/ProjectDeskStudio';
import type { CloudProjectDto } from '../../types/api';
import {
  PageShell,
  PageHeader,
  Card,
  Button,
  EmptyState,
  DataTable,
  Td,
} from '../../components/workspace/WorkspaceKit';

export default function DeskPage() {
  return (
    <Suspense
      fallback={
        <PageShell width="max-w-6xl">
          <Card>
            <div className="flex items-center gap-2 px-5 py-8 text-xs text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" /> Loading Desk Data Studio…
            </div>
          </Card>
        </PageShell>
      }
    >
      <DeskPageContent />
    </Suspense>
  );
}

function DeskPageContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const projectParam = searchParams.get('project');

  const isAuthenticated = useAuthStore((s) => s.isAuthenticated);
  const currentUser = useAuthStore((s) => s.user);
  const openAuthModal = useAuthModalStore((s) => s.open);

  const localProjects = useProjectHistoryStore((s) => s.projects);
  const { scopedProjectId, scopedProjectName, scopedHubRepo, setScopedProject, clearProjectSession } =
    useActiveProjectStore();

  const [projects, setProjects] = useState<CloudProjectDto[] | null>(null);

  // Sync projects list
  const loadProjects = useCallback(async () => {
    if (!isAuthenticated) return;
    try {
      const list = await authService.getCloudProjects();
      setProjects(list);
    } catch {
      setProjects([]);
    }
  }, [isAuthenticated]);

  useEffect(() => {
    loadProjects();
  }, [loadProjects]);

  // URL ?project=<id> synchronization
  useEffect(() => {
    if (projectParam) {
      const cloudMatch = projects?.find((p) => p.id === projectParam);
      const localMatch = localProjects.find((p) => p.id === projectParam);
      const resolvedName =
        cloudMatch?.name || localMatch?.name || scopedProjectName || scopedHubRepo?.name || 'Project Studio';
      // activeProjectId bilerek değiştirilmiyor (autosave başka projenin şemasını ezmesin)
      setScopedProject(projectParam, resolvedName);
    } else {
      clearProjectSession();
    }
  }, [projectParam, projects, localProjects, scopedProjectName, scopedHubRepo, setScopedProject, clearProjectSession]);

  // Determine current active project ID (URL param or session)
  const targetProjectId = projectParam || scopedProjectId;

  // ─────────────────────────────────────────────────────────────────────────
  // MODE A: PROJECT-SCOPED DESK STUDIO (/desk?project=<id>)
  // ─────────────────────────────────────────────────────────────────────────
  if (targetProjectId) {
    const cloudMatch = projects?.find((p) => p.id === targetProjectId);
    const localMatch = localProjects.find((p) => p.id === targetProjectId);
    const projectName =
      cloudMatch?.name || localMatch?.name || scopedProjectName || scopedHubRepo?.name || 'Project Studio';
    const resolvedOwner = scopedHubRepo?.owner || currentUser?.username || 'me';

    return (
      <ProjectDeskStudio
        projectId={targetProjectId}
        projectName={projectName}
        resolvedOwner={resolvedOwner}
        projectDto={cloudMatch || null}
        onExitProject={() => clearProjectSession()}
      />
    );
  }

  // ─────────────────────────────────────────────────────────────────────────
  // MODE B: DESK PROJECT SELECTOR (/desk without ?project=)
  // ─────────────────────────────────────────────────────────────────────────
  const combinedProjects = [
    ...(projects || []).map((p) => ({
      id: p.id,
      name: p.name,
      engine: p.dbType || 'PostgreSQL',
      hasConnection: p.hasConnection,
      tables: p.tableCount ?? 0,
      updatedAt: p.updatedAt,
    })),
    ...localProjects
      .filter((lp) => !(projects || []).some((cp) => cp.id === lp.id))
      .map((lp) => ({
        id: lp.id,
        name: lp.name,
        engine: lp.dbType || 'PostgreSQL',
        hasConnection: false,
        tables: lp.schema?.tables?.length ?? 0,
        updatedAt: lp.updatedAt ? new Date(lp.updatedAt).toISOString() : null,
      })),
  ];

  return (
    <PageShell width="max-w-6xl">
      <div id="desk-header">
        <PageHeader
          icon={Table2}
          title="Namines Desk — Data & Schema Studio"
          description="High-density relational data browser, inline record management, and direct SQL execution."
        />
      </div>

      {!isAuthenticated && (
        <Card>
          <div className="flex flex-col sm:flex-row items-start sm:items-center justify-between gap-4 p-2">
            <div>
              <p className="text-sm font-semibold text-content-primary">Sign in to access your projects</p>
              <p className="text-xs text-content-muted mt-0.5">
                Authentication connects your studio to cloud projects, managed sandboxes, and Vault credentials.
              </p>
            </div>
            <Button variant="primary" onClick={() => openAuthModal()}>
              Sign In
            </Button>
          </div>
        </Card>
      )}

      <Card
        title="Projects"
        description="Select any project workspace to open its relational data studio."
        flush
      >
        {combinedProjects.length === 0 ? (
          <EmptyState
            icon={Table2}
            title="No projects found"
            description="Create or design a project first on Canvas to explore its records in Desk."
            action={
              <Button variant="primary" onClick={() => router.push('/canvas')}>
                Open Canvas
              </Button>
            }
          />
        ) : (
          <DataTable
            caption="Desk Projects"
            columns={[
              { label: 'Project Name' },
              { label: 'Database Engine' },
              { label: 'Tables' },
              { label: 'Connection' },
              { label: '', align: 'right' },
            ]}
          >
            {combinedProjects.map((proj) => (
              <tr
                key={proj.id}
                className="hover:bg-surface-700/30 transition-colors cursor-pointer group"
                onClick={() => router.push(`/desk?project=${encodeURIComponent(proj.id)}`)}
              >
                <Td>
                  <div className="flex items-center gap-2">
                    <Database className="w-4 h-4 text-content-muted group-hover:text-content-primary transition-colors shrink-0" />
                    <span className="font-semibold text-content-primary text-xs">{proj.name}</span>
                  </div>
                </Td>
                <Td>
                  <span className="text-xs font-mono text-content-secondary">{proj.engine}</span>
                </Td>
                <Td>
                  <span className="text-xs text-content-muted">{proj.tables} tables</span>
                </Td>
                <Td>
                  {proj.hasConnection ? (
                    <span className="inline-flex items-center gap-1 text-[11px] font-medium text-accent-text">
                      <HardDrive className="w-3 h-3" /> Connected
                    </span>
                  ) : (
                    <span className="text-[11px] text-content-muted">Design mode</span>
                  )}
                </Td>
                <Td align="right">
                  <Button
                    size="sm"
                    variant="secondary"
                    icon={ArrowRight}
                    onClick={() => {
                      router.push(`/desk?project=${encodeURIComponent(proj.id)}`);
                    }}
                  >
                    Open Studio
                  </Button>
                </Td>
              </tr>
            ))}
          </DataTable>
        )}
      </Card>
    </PageShell>
  );
}
