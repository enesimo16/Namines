'use client';

import React, { useCallback, useEffect, useMemo, useRef, useState } from 'react';
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
import { useToastStore } from '../../../../store/useToastStore';
import { useT } from '../../../../lib/i18n';
import { HubRepoHeader, HubTabKey } from '../../../../components/hub/HubRepoHeader';
import { HubSchemaCodeTab } from '../../../../components/hub/HubSchemaCodeTab';
import { HubIssuesTab } from '../../../../components/hub/HubIssuesTab';
import { HubIntegrationsTab } from '../../../../components/hub/HubIntegrationsTab';
import { HubPullRequestsTab } from '../../../../components/hub/HubPullRequestsTab';
import { HubSettingsTab } from '../../../../components/hub/HubSettingsTab';

const VALID_TABS = ['schema', 'issues', 'integrations', 'prs', 'settings'];

function slugify(name: string): string {
  return name.toLowerCase().replace(/[^a-z0-9-]/g, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
}

export default function HubRepoPage() {
  const params = useParams();
  const router = useRouter();
  const t = useT();
  const showToast = useToastStore(s => s.showToast);

  const ownerParam = typeof params?.owner === 'string' ? params.owner : '';
  const slugParam = typeof params?.slug === 'string' ? params.slug : '';

  const [repo, setRepo] = useState<HubRepoDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [errorStatus, setErrorStatus] = useState<number | null>(null);
  const searchParams = useSearchParams();
  const tabParam = searchParams?.get('tab') as HubTabKey | null;
  const [activeTab, setActiveTab] = useState<HubTabKey>(tabParam && VALID_TABS.includes(tabParam) ? tabParam : 'schema');

  useEffect(() => {
    if (tabParam && VALID_TABS.includes(tabParam)) {
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

  // Yerel taslak yalnızca URL'deki slug/id projeyle BİREBİR eşleşiyorsa ve adres kullanıcının
  // kendi alanıysa geçerli. Eskiden etkin canvas şeması her adreste "bu repo" diye gösteriliyordu.
  const matchingLocalProject = useMemo(() => {
    const ownerIsMe =
      !ownerParam || ownerParam === 'me' ||
      (!!currentUser?.username && ownerParam.toLowerCase() === currentUser.username.toLowerCase());
    if (!ownerIsMe) return null;
    return projects.find(p => p.id === slugParam || slugify(p.name) === slugParam) ?? null;
  }, [projects, slugParam, ownerParam, currentUser?.username]);

  // loadRepo her autosave'de (projects değişince) yeniden çalışmasın diye güncel değerler ref'te.
  const localMatchRef = useRef(matchingLocalProject);
  const currentUserRef = useRef(currentUser);
  useEffect(() => {
    localMatchRef.current = matchingLocalProject;
    currentUserRef.current = currentUser;
  }, [matchingLocalProject, currentUser]);
  const requestSeq = useRef(0);

  const loadRepo = useCallback(async () => {
    if (!ownerParam || !slugParam) return;
    const seq = ++requestSeq.current;
    const isStale = () => seq !== requestSeq.current;
    setLoading(true);
    setError(null);
    setErrorStatus(null);

    try {
      const data = await hubService.getRepo(ownerParam, slugParam);
      if (isStale()) return;
      // Yerel dalları yalnızca AYNI projeyse birleştir
      const local = localMatchRef.current;
      const localBranches = local && local.id === data.id ? local.branches || [] : [];
      const branchMap = new Map<string, { id: string; name: string; isDefault: boolean; createdAt: string }>();
      (data.branches || []).forEach(b => branchMap.set(b.name, b));
      localBranches.forEach(b => {
        if (!branchMap.has(b.name)) {
          branchMap.set(b.name, {
            id: b.name,
            name: b.name,
            isDefault: b.name === (local?.currentBranch || 'main'),
            createdAt: b.createdAt || new Date().toISOString(),
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
      // activeProjectId BİLEREK değiştirilmiyor: canvas'taki şema başka projeye ait olabilir ve
      // autosave onu bu repoya yazardı.
    } catch (err: unknown) {
      if (isStale()) return;
      const status = (err as { response?: { status?: number } })?.response?.status ?? null;
      const local = localMatchRef.current;
      setErrorStatus(status);
      if (status === 401) {
        setError(t('hub.repo.privateLogin'));
      } else if (status === 404 && local) {
        // Proje henüz Hub'a çıkmamış: kullanıcının KENDİ yerel projesi için salt yerel taslak göster.
        const activeLocalBranch = local.currentBranch || 'main';
        const rawBranches = (local.branches && local.branches.length > 0)
          ? local.branches
          : [{ name: 'main', schema: local.schema, createdAt: new Date().toISOString() }];

        const hasMain = rawBranches.some(b => b.name === 'main');
        const listToMap = hasMain
          ? rawBranches
          : [{ name: 'main', schema: local.schema, createdAt: new Date().toISOString() }, ...rawBranches];

        const synthBranches = listToMap.map(b => ({
          id: b.name,
          name: b.name,
          isDefault: b.name === activeLocalBranch,
          createdAt: b.createdAt || new Date().toISOString(),
          schema: b.schema || local.schema,
        }));

        const user = currentUserRef.current;
        const synthRepo: HubRepoDetail = {
          id: local.id,
          name: local.name,
          slug: slugParam !== 'me' ? slugParam : local.id,
          description: `${local.dbType || 'Relational'} architecture repository`,
          visibility: ProjectVisibility.Private,
          category: 'Backend',
          tags: local.dbType,
          dbType: local.dbType || 'PostgreSQL',
          starCount: 0,
          forkCount: 0,
          forkedFromProjectId: null,
          forkedFrom: null,
          linkedGithubRepo: null,
          autoApproveSafeChanges: true,
          createdAt: local.createdAt || new Date().toISOString(),
          updatedAt: local.updatedAt || new Date().toISOString(),
          owner: {
            id: user?.username || 'me',
            username: user?.username || ownerParam || 'me',
            email: user?.email,
          },
          caller: {
            isStarred: false,
            role: 'owner',
            canEdit: true,
          },
          stats: {
            tableCount: local.schema?.tables?.length || 0,
            relationCount: local.schema?.relations?.length || 0,
            openIssuesCount: 0,
            closedIssuesCount: 0,
            openPrCount: 0,
            closedPrCount: 0,
          },
          branches: synthBranches,
          schema: local.schema || { schemaId: '', name: local.name, tables: [], relations: [] },
          nodePositionsJson: JSON.stringify(local.nodePositions || {}),
          isLocalDraft: true,
        };

        setRepo(synthRepo);
        setError(null);
        useActiveProjectStore.getState().setScopedHubRepo({
          id: synthRepo.id,
          owner: synthRepo.owner.username,
          slug: synthRepo.slug || slugParam,
          name: synthRepo.name,
        });

        // Girişliyse yerel projeyi (Private) buluta senkronla; sonraki açılışta gerçek repo gelir.
        if (useAuthStore.getState().isAuthenticated) {
          syncAllToCloud().catch(() => {});
        }
      } else if (status === 404) {
        setError(t('hub.repo.notFound'));
      } else {
        setError(t('hub.repo.loadFailed'));
      }
    } finally {
      if (!isStale()) setLoading(false);
    }
  }, [ownerParam, slugParam, isAuthenticated, syncAllToCloud, t]);

  useEffect(() => {
    loadRepo();
  }, [loadRepo]);

  const handlePublishToHub = async (visibility: ProjectVisibility) => {
    const local = matchingLocalProject;
    if (!local) return;
    try {
      setPublishing(true);
      // 1. Canvas'taki şema yalnızca ETKİN proje bu proje ise yerel snapshot'a yazılır
      if (activeProjectId === local.id && localSchema && localSchema.tables?.length > 0) {
        saveCurrentProject(localSchema, [], localSchema.name || local.name, localDbType || 'PostgreSQL');
      }
      // 2. Projeleri buluta senkronla (PostgreSQL CloudProjects tablosu)
      await syncAllToCloud();

      // 3. Görünürlüğü ve gerekirse slug'ı güncelle
      let visibilityApplied = true;
      try {
        await hubService.updateSettings(local.id, {
          visibility,
          slug: slugParam !== 'me' && slugParam !== local.id ? slugParam : undefined,
        });
      } catch {
        visibilityApplied = false;
      }
      if (!visibilityApplied) showToast(t('hub.repo.publishVisibilityFailed'), 'warning');

      // 4. Repoyu tekrar sorgula
      await loadRepo();
    } catch (e) {
      console.error('[Hub] Publish failed:', e);
      showToast(t('hub.repo.publishFailed'), 'error');
    } finally {
      setPublishing(false);
    }
  };

  if (loading) {
    return (
      <div className="min-h-screen bg-surface-950 flex flex-col items-center justify-center gap-3 text-ink-muted">
        <Loader2 className="w-6 h-6 animate-spin text-accent-text" />
        <span className="font-mono text-xs">{t('hub.repo.loading')}</span>
      </div>
    );
  }

  if (error || !repo) {
    // Eğer bu adres kullanıcının yerel projesiyle ilişkiliyse dostane yayınlama ekranı göster
    if (matchingLocalProject && errorStatus === 401) {
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
                    {t('hub.repo.localDraft')}
                  </span>
                </div>
                <span className="text-xs text-ink-muted font-mono">
                  {t('hub.repo.tablesCount', { db: matchingLocalProject.dbType, count: matchingLocalProject.schema?.tables?.length ?? 0 })}
                </span>
              </div>
            </div>

            <div className="p-3.5 bg-surface-900 border border-line rounded-[var(--radius-control)] space-y-1.5">
              <div className="flex items-center gap-2 text-warning text-xs font-semibold">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{t('hub.repo.notPublishedTitle')}</span>
              </div>
              <p className="text-xs text-ink-secondary leading-relaxed">
                {t('hub.repo.notPublishedBody')}
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
                    <span>{t('hub.repo.publishPrivate')}</span>
                  </button>
                  <button
                    type="button"
                    disabled={publishing}
                    onClick={() => handlePublishToHub(ProjectVisibility.Public)}
                    className="flex-1 inline-flex items-center justify-center gap-2 px-4 py-2.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line-strong shadow-xs transition-all disabled:opacity-50 cursor-pointer"
                  >
                    {publishing ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Globe className="w-3.5 h-3.5 text-accent-text" />}
                    <span>{t('hub.repo.publishPublic')}</span>
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
                  <span>{t('hub.repo.signInSync')}</span>
                </button>
              )}

              <div className="flex items-center justify-between pt-2 text-xs">
                <Link
                  href="/canvas"
                  className="inline-flex items-center gap-1.5 text-ink-muted hover:text-ink-primary transition-colors"
                >
                  <ArrowLeft className="w-3.5 h-3.5" />
                  <span>{t('hub.repo.backToCanvas')}</span>
                </Link>
                <Link
                  href="/hub"
                  className="text-accent-text hover:underline transition-colors font-medium"
                >
                  {t('hub.repo.exploreHub')}
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
          <h2 className="text-base font-semibold text-ink-primary">{t('hub.repo.accessError')}</h2>
          <p className="text-xs text-ink-secondary">{error || t('hub.repo.notFoundFallback')}</p>
          <div className="pt-2">
            <Link
              href="/hub"
              className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-medium rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line transition-all"
            >
              <ArrowLeft className="w-3.5 h-3.5" />
              <span>{t('hub.repo.backToHub')}</span>
            </Link>
          </div>
        </div>
      </div>
    );
  }

  const canEdit = !!repo.caller?.canEdit;
  const visibleTab: HubTabKey = activeTab === 'settings' && !canEdit ? 'schema' : activeTab;

  // Slug değişince eski adres 404 olur: yeni adrese geç
  const handleRepoUpdated = (updated: Partial<HubRepoDetail>) => {
    setRepo(prev => (prev ? { ...prev, ...updated } : null));
    if (updated.slug && updated.slug !== slugParam) {
      const owner = repo.owner?.username || ownerParam;
      router.replace(`/hub/${encodeURIComponent(owner)}/${encodeURIComponent(updated.slug)}?tab=settings`);
    }
  };

  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary flex flex-col font-sans">
      {/* Top GitHub-form Header & Sub-navigation */}
      <HubRepoHeader
        key={repo.id}
        repo={repo}
        activeTab={visibleTab}
        onTabChange={setActiveTab}
        onRepoUpdated={updated => setRepo(prev => (prev ? { ...prev, ...updated } : null))}
      />

      {/* Main Tab Content */}
      <main className="flex-1 max-w-6xl w-full mx-auto px-4 sm:px-6 py-6">
        {visibleTab === 'schema' && <HubSchemaCodeTab repo={repo} />}
        {visibleTab === 'issues' && (
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
        {visibleTab === 'integrations' && <HubIntegrationsTab repo={repo} />}
        {visibleTab === 'prs' && <HubPullRequestsTab repo={repo} />}
        {visibleTab === 'settings' && canEdit && (
          <HubSettingsTab
            repo={repo}
            onRepoUpdated={handleRepoUpdated}
          />
        )}
      </main>
    </div>
  );
}
