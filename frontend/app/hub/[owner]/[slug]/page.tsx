'use client';

import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { Suspense } from 'react';
import { Loader2, AlertCircle, ArrowLeft, Database, Lock, Globe, Sparkles } from 'lucide-react';
import Link from 'next/link';
import type { HubRepoDetail } from '../../../../types/hub';
import { ProjectVisibility } from '../../../../types/hub';
import { hubService } from '../../../../services/hubService';
import { useProjectHistoryStore } from '../../../../store/useProjectHistoryStore';
import { useActiveProjectStore } from '../../../../store/useActiveProjectStore';
import { useAuthStore } from '../../../../store/useAuthStore';
import { useSchemaStore } from '../../../../store/useSchemaStore';
import { HubRepoHeader, HubTabKey } from '../../../../components/hub/HubRepoHeader';
import { HubSchemaCodeTab } from '../../../../components/hub/HubSchemaCodeTab';
import { HubIssuesTab } from '../../../../components/hub/HubIssuesTab';
import { HubIntegrationsTab } from '../../../../components/hub/HubIntegrationsTab';
import { HubPullRequestsTab } from '../../../../components/hub/HubPullRequestsTab';
import { HubSettingsTab } from '../../../../components/hub/HubSettingsTab';

export default function HubRepoPage() {
  const params = useParams();
  const router = useRouter();

  const ownerParam = typeof params?.owner === 'string' ? params.owner : '';
  const slugParam = typeof params?.slug === 'string' ? params.slug : '';

  const [repo, setRepo] = useState<HubRepoDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const searchParams = useSearchParams();
  const tabParam = searchParams?.get('tab') as HubTabKey | null;
  const [activeTab, setActiveTab] = useState<HubTabKey>(tabParam && ['schema', 'issues', 'integrations', 'prs', 'settings'].includes(tabParam) ? tabParam : 'schema');

  useEffect(() => {
    if (tabParam && ['schema', 'issues', 'integrations', 'prs', 'settings'].includes(tabParam)) {
      setActiveTab(tabParam);
    }
  }, [tabParam]);
  const [publishing, setPublishing] = useState(false);

  const projects = useProjectHistoryStore(s => s.projects);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const syncAllToCloud = useProjectHistoryStore(s => s.syncAllToCloud);
  const saveCurrentProject = useProjectHistoryStore(s => s.saveCurrentProject);
  const isAuthenticated = useAuthStore(s => s.isAuthenticated);
  const currentUser = useAuthStore(s => s.user);
  const localSchema = useSchemaStore(s => s.schema);
  const localDbType = useSchemaStore(s => s.dbType);

  // Yerel çalışma alanında eşleşen proje tespiti (loadRepo'dan önce)
  const matchingLocalProject = useMemo(() => {
    return projects.find(p => {
      const cleanSlug = p.name.toLowerCase().replace(/[^a-z0-9\-]/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
      return p.id === slugParam || cleanSlug === slugParam || (activeProjectId && p.id === activeProjectId);
    }) || (localSchema && localSchema.tables && localSchema.tables.length > 0 ? {
      id: activeProjectId || 'current-project',
      name: localSchema.name || 'Aktif Şema',
      dbType: localDbType || 'PostgreSQL',
      schema: localSchema,
      nodePositions: {},
      branches: [],
      currentBranch: 'main',
    } : null);
  }, [projects, slugParam, activeProjectId, localSchema, localDbType]);

  const loadRepo = useCallback(async () => {
    if (!ownerParam || !slugParam) return;
    setLoading(true);
    setError(null);

    try {
      const data = await hubService.getRepo(ownerParam, slugParam);
      // Merge with any local branches
      const localBranches = matchingLocalProject?.branches || [];
      const branchMap = new Map<string, { id: string; name: string; isDefault: boolean; createdAt: string }>();
      (data.branches || []).forEach(b => branchMap.set(b.name, b));
      localBranches.forEach(b => {
        if (!branchMap.has(b.name)) {
          branchMap.set(b.name, {
            id: b.name,
            name: b.name,
            isDefault: b.name === (matchingLocalProject?.currentBranch || 'main'),
            createdAt: (b as any).createdAt || new Date().toISOString(),
          });
        }
      });
      data.branches = Array.from(branchMap.values());

      setRepo(data);
      useActiveProjectStore.getState().setScopedHubRepo({
        id: data.id,
        owner: data.owner?.username || ownerParam,
        slug: data.slug || slugParam,
        name: data.name,
      });
      useProjectHistoryStore.getState().setActiveProjectId(data.id);
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 401) {
        setError('Bu depo gizlidir. Görüntülemek için lütfen giriş yapın.');
      } else if (matchingLocalProject) {
        // Proje direkt Hub'da Private repo olarak hazır gelir (bkz. kural: otomatik private hub)
        const activeLocalBranch = matchingLocalProject.currentBranch || 'main';
        const rawBranches = (matchingLocalProject.branches && matchingLocalProject.branches.length > 0)
          ? matchingLocalProject.branches
          : [{ name: 'main', schema: matchingLocalProject.schema, createdAt: new Date().toISOString() }];
        
        const hasMain = rawBranches.some(b => b.name === 'main');
        const listToMap = hasMain
          ? rawBranches
          : [{ name: 'main', schema: matchingLocalProject.schema, createdAt: new Date().toISOString() }, ...rawBranches];

        const synthBranches = listToMap.map(b => ({
          id: b.name,
          name: b.name,
          isDefault: b.name === activeLocalBranch,
          createdAt: (b as any).createdAt || new Date().toISOString(),
          schema: b.schema || matchingLocalProject.schema,
        }));

        const synthRepo: HubRepoDetail = {
          id: matchingLocalProject.id,
          name: matchingLocalProject.name,
          slug: slugParam !== 'me' ? slugParam : matchingLocalProject.id,
          description: `${matchingLocalProject.dbType || 'Relational'} architecture repository`,
          visibility: ProjectVisibility.Private,
          category: 'Backend',
          tags: matchingLocalProject.dbType,
          dbType: matchingLocalProject.dbType || 'PostgreSQL',
          starCount: 0,
          forkCount: 0,
          forkedFromProjectId: null,
          forkedFrom: null,
          linkedGithubRepo: null,
          autoApproveSafeChanges: true,
          createdAt: (matchingLocalProject as any).createdAt || new Date().toISOString(),
          updatedAt: (matchingLocalProject as any).updatedAt || new Date().toISOString(),
          owner: {
            id: currentUser?.username || 'me',
            username: currentUser?.username || ownerParam || 'me',
            email: currentUser?.email,
          },
          caller: {
            isStarred: false,
            role: 'owner',
            canEdit: true,
          },
          stats: {
            tableCount: matchingLocalProject.schema?.tables?.length || 0,
            relationCount: matchingLocalProject.schema?.relations?.length || 0,
            openIssuesCount: 0,
            closedIssuesCount: 0,
            openPrCount: 0,
            closedPrCount: 0,
          },
          branches: synthBranches,
          schema: matchingLocalProject.schema || { schemaId: '', name: matchingLocalProject.name, tables: [], relations: [] },
          nodePositionsJson: JSON.stringify(matchingLocalProject.nodePositions || {}),
        };

        setRepo(synthRepo);
        useActiveProjectStore.getState().setScopedHubRepo({
          id: synthRepo.id,
          owner: synthRepo.owner.username,
          slug: synthRepo.slug || slugParam,
          name: synthRepo.name,
        });

        // Background auto-sync if authenticated
        if (isAuthenticated) {
          syncAllToCloud().catch(() => {});
        }
      } else {
        setError('Depo bulunamadı veya henüz Hub üzerinde yayınlanmadı.');
      }
    } finally {
      setLoading(false);
    }
  }, [ownerParam, slugParam, matchingLocalProject, currentUser, isAuthenticated, syncAllToCloud]);

  useEffect(() => {
    loadRepo();
  }, [loadRepo]);



  const handlePublishToHub = async (visibility: ProjectVisibility) => {
    try {
      setPublishing(true);
      // 1. Önce aktif projenin yerel snapshot'ını tazele
      if (localSchema && localSchema.tables?.length > 0) {
        saveCurrentProject(localSchema, [], localSchema.name || 'Yeni Proje', localDbType || 'PostgreSQL');
      }
      // 2. Projeleri buluta senkronla (PostgreSQL CloudProjects tablosu)
      await syncAllToCloud();

      // 3. Görünürlüğü ve gerekirse slug'ı güncelle
      if (matchingLocalProject?.id) {
        try {
          await hubService.updateSettings(matchingLocalProject.id, {
            visibility,
            slug: slugParam !== 'me' ? slugParam : undefined,
          });
        } catch {
          // Varsayılan ayarlarla devam
        }
      }

      // 4. Repoyu tekrar sorgula
      await loadRepo();
    } catch (e) {
      console.error('[Hub] Publish failed:', e);
    } finally {
      setPublishing(false);
    }
  };

  if (loading) {
    return (
      <div className="min-h-screen bg-surface-950 flex flex-col items-center justify-center gap-3 text-ink-muted">
        <Loader2 className="w-6 h-6 animate-spin text-accent-text" />
        <span className="font-mono text-xs">Depo mimarisi ve şema yükleniyor...</span>
      </div>
    );
  }

  if (error || !repo) {
    // Eğer bu adres kullanıcının yerel projesiyle ilişkiliyse dostane yayınlama ekranı göster
    if (matchingLocalProject) {
      return (
        <div className="min-h-screen bg-surface-950 flex flex-col items-center justify-center p-6 text-center">
          <div className="max-w-lg w-full bg-surface-850 border border-line rounded-[var(--radius-card)] p-8 shadow-2xl space-y-5 text-left">
            <div className="flex items-center gap-3.5">
              <div className="w-11 h-11 rounded-[var(--radius-card)] bg-surface-800 border border-line flex items-center justify-center text-ink-primary shrink-0">
                <Database className="w-5 h-5 text-accent-text" />
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2">
                  <h2 className="text-base font-semibold text-ink-primary truncate">
                    {matchingLocalProject.name}
                  </h2>
                  <span className="inline-flex items-center px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-medium bg-surface-700 text-ink-muted border border-line">
                    Yerel Taslak
                  </span>
                </div>
                <span className="text-xs text-ink-muted font-mono">
                  {matchingLocalProject.dbType} • {matchingLocalProject.schema?.tables?.length ?? 0} Tablo
                </span>
              </div>
            </div>

            <div className="p-3.5 bg-surface-900 border border-line rounded-[var(--radius-control)] space-y-1.5">
              <div className="flex items-center gap-2 text-warning text-xs font-semibold">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>Bu proje henüz Namines Hub&apos;a aktarılmadı</span>
              </div>
              <p className="text-xs text-ink-secondary leading-relaxed">
                Mevcut şema çalışmanız tarayıcınızın yerel depolama alanında kayıtlı. Hub&apos;a aktararak mimariyi sürümleyebilir, şema kodlarını inceleyebilir, Issue ve Pull Request açabilirsiniz.
              </p>
            </div>

            <div className="space-y-3 pt-1">
              {isAuthenticated ? (
                <div className="flex flex-col sm:flex-row gap-2.5">
                  <button
                    type="button"
                    disabled={publishing}
                    onClick={() => handlePublishToHub(ProjectVisibility.Private)}
                    className="flex-1 inline-flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-medium rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-750 text-ink-secondary hover:text-ink-primary border border-line hover:border-line-strong transition-all disabled:opacity-50 cursor-pointer"
                  >
                    {publishing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Lock className="w-3.5 h-3.5 text-ink-muted" />}
                    <span>Özel Repo Olarak Yayınla</span>
                  </button>
                  <button
                    type="button"
                    disabled={publishing}
                    onClick={() => handlePublishToHub(ProjectVisibility.Public)}
                    className="flex-1 inline-flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line-strong shadow-xs transition-all disabled:opacity-50 cursor-pointer"
                  >
                    {publishing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Globe className="w-3.5 h-3.5 text-accent-text" />}
                    <span>Public Vitrine Yayınla</span>
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  onClick={() => {
                    window.dispatchEvent(new CustomEvent('namines:open-auth-modal'));
                  }}
                  className="w-full inline-flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line-strong transition-all shadow-xs cursor-pointer"
                >
                  <Sparkles className="w-3.5 h-3.5 text-accent-text" />
                  <span>Giriş Yap ve Hub&apos;a Senkronla</span>
                </button>
              )}

              <div className="flex items-center justify-between pt-2 text-xs">
                <Link
                  href="/canvas"
                  className="inline-flex items-center gap-1.5 text-ink-muted hover:text-ink-primary transition-colors"
                >
                  <ArrowLeft className="w-3.5 h-3.5" />
                  <span>Canvas Çalışma Alanına Dön</span>
                </Link>
                <Link
                  href="/hub"
                  className="text-accent-text hover:underline transition-colors font-medium"
                >
                  Genel Hub Vitrinini Keşfet →
                </Link>
              </div>
            </div>
          </div>
        </div>
      );
    }

    return (
      <div className="min-h-screen bg-surface-950 flex flex-col items-center justify-center p-6 text-center">
        <div className="max-w-md bg-surface-850 border border-line rounded-[var(--radius-card)] p-8 shadow-2xl space-y-4">
          <AlertCircle className="w-10 h-10 text-danger mx-auto" />
          <h2 className="text-base font-semibold text-ink-primary">Depo Erişim Hatası</h2>
          <p className="text-xs text-ink-secondary">{error || 'Depo bulunamadı veya silinmiş olabilir.'}</p>
          <div className="pt-2">
            <Link
              href="/hub"
              className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-medium rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line transition-all"
            >
              <ArrowLeft className="w-3.5 h-3.5" />
              <span>Hub Keşif Vitrinine Dön</span>
            </Link>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary flex flex-col font-sans">
      {/* Top GitHub-form Header & Sub-navigation */}
      <HubRepoHeader
        repo={repo}
        activeTab={activeTab}
        onTabChange={setActiveTab}
        onRepoUpdated={updated => setRepo(prev => (prev ? { ...prev, ...updated } : null))}
      />

      {/* Main Tab Content */}
      <main className="flex-1 max-w-6xl w-full mx-auto px-4 sm:px-6 py-6">
        {activeTab === 'schema' && <HubSchemaCodeTab repo={repo} />}
        {activeTab === 'issues' && (
          <HubIssuesTab
            repo={repo}
            onIssueCountChanged={(open, closed) =>
              setRepo(prev =>
                prev
                  ? {
                      ...prev,
                      stats: { ...prev.stats, openIssuesCount: open, closedIssuesCount: closed },
                    }
                  : null
              )
            }
          />
        )}
        {activeTab === 'integrations' && <HubIntegrationsTab repo={repo} />}
        {activeTab === 'prs' && <HubPullRequestsTab repo={repo} />}
        {activeTab === 'settings' && (
          <HubSettingsTab
            repo={repo}
            onRepoUpdated={updated => setRepo(prev => (prev ? { ...prev, ...updated } : null))}
          />
        )}
      </main>
    </div>
  );
}
