'use client';

import React, { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  Star,
  GitFork,
  Rocket,
  CodeXml,
  CircleDot,
  GitPullRequest,
  Settings as SettingsIcon,
  Lock,
  Users,
  Globe,
  Loader2,
  Check,
  Layers,
} from 'lucide-react';
import type { HubRepoDetail } from '../../types/hub';
import { ProjectVisibility, normalizeVisibility } from '../../types/hub';
import { hubService } from '../../services/hubService';
import { useAuthStore } from '../../store/useAuthStore';
import { useToastStore } from '../../store/useToastStore';
import { useSchemaStore, type DbType } from '../../store/useSchemaStore';
import { useAuthModalStore } from '../../store/useAuthModalStore';
import { useT } from '../../lib/i18n';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { trackHubRepoStarred, trackHubRepoForked } from '../../lib/analytics';
import { ModuleSymbol, ModuleWordmark } from '../layout/ModuleLogo';

export type HubTabKey = 'schema' | 'issues' | 'integrations' | 'prs' | 'settings';

interface HubRepoHeaderProps {
  repo: HubRepoDetail;
  activeTab: HubTabKey;
  onTabChange: (tab: HubTabKey) => void;
  onRepoUpdated?: (updated: Partial<HubRepoDetail>) => void;
}

export function HubRepoHeader({
  repo,
  activeTab,
  onTabChange,
  onRepoUpdated,
}: HubRepoHeaderProps) {
  const router = useRouter();
  const t = useT();
  const { isAuthenticated } = useAuthStore();
  const { showToast } = useToastStore();

  const [isStarred, setIsStarred] = useState(repo.caller?.isStarred ?? false);
  const [starCount, setStarCount] = useState(repo.starCount);
  const [isStarring, setIsStarring] = useState(false);

  const [isForkModalOpen, setIsForkModalOpen] = useState(false);
  const [forkName, setForkName] = useState(`${repo.name}-fork`);
  const [isForking, setIsForking] = useState(false);

  const handleToggleStar = async () => {
    if (!isAuthenticated) {
      showToast('Please log in to star this repository.', 'warning');
      return;
    }
    if (isStarring) return;

    setIsStarring(true);
    try {
      const res = await hubService.toggleStar(repo.id);
      setIsStarred(res.isStarred);
      setStarCount(res.starCount);
      onRepoUpdated?.({ starCount: res.starCount, caller: { ...repo.caller, isStarred: res.isStarred } });
      showToast(res.isStarred ? 'Starred repository' : 'Unstarred repository', 'success');
      trackHubRepoStarred(repo.id, res.isStarred);
    } catch {
      showToast('Could not update star. Please try again.', 'error');
    } finally {
      setIsStarring(false);
    }
  };

  const handleFork = async (e: React.FormEvent) => {
    e.preventDefault();
    if (isForking) return;

    // Fork sunucu tarafı bir Hub işlemi: oturum yoksa yerel taklit üretmiyoruz.
    if (!isAuthenticated) {
      showToast(t('hub.loginToFork'), 'warning');
      useAuthModalStore.getState().open();
      return;
    }

    setIsForking(true);
    const cleanForkName = (forkName || `${repo.name}-fork`).trim();

    try {
      const res = await hubService.fork(repo.id, cleanForkName);
      const targetSlug = res?.slug || res?.id;
      const targetOwner = res?.owner?.username || useAuthStore.getState().user?.username;
      if (!targetSlug || !targetOwner) throw new Error('Fork response is missing the new repository address.');

      showToast(t('hub.forkCreated', { name: cleanForkName }), 'success');
      trackHubRepoForked(repo.id);
      setIsForkModalOpen(false);
      onRepoUpdated?.({ forkCount: (repo.forkCount || 0) + 1 });

      router.push(`/hub/${encodeURIComponent(targetOwner)}/${encodeURIComponent(targetSlug)}`);
    } catch {
      // Başarısızlıkta yerel kopya/yönlendirme yok; modal açık kalır, kullanıcı tekrar dener.
      showToast(t('hub.forkFailed'), 'error');
    } finally {
      setIsForking(false);
    }
  };

  const handleOpenInStudio = () => {
    try {
      if (repo.schema) {
        // Yerelde olmayan/başkasının deposu açılırken etkin proje bırakılıyor; aksi hâlde
        // canvas autosave'i bu şemayı daha önce açık olan projenin üzerine yazardı.
        const ownLocal = repo.caller?.canEdit &&
          useProjectHistoryStore.getState().projects.some((p) => p.id === repo.id);
        if (ownLocal) {
          showToast(t('hub.openedInStudio', { name: repo.name }), 'info');
          router.push(`/canvas?project=${encodeURIComponent(repo.id)}`);
          return;
        }
        useProjectHistoryStore.getState().setActiveProjectId(null);
        useSchemaStore.getState().loadFromSchema(repo.schema);
        if (repo.dbType) {
          useSchemaStore.getState().setDbType(repo.dbType as DbType);
        }
      }
      showToast(t('hub.openedInStudio', { name: repo.name }), 'info');
      router.push('/canvas');
    } catch {
      router.push('/canvas');
    }
  };

  const visibilityPill = () => {
    const v = normalizeVisibility(repo.visibility);
    switch (v) {
      case ProjectVisibility.Public:
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 text-[11px] font-medium rounded-full bg-surface-700 text-ink-primary border border-line">
            <Globe className="w-3 h-3 text-accent-text" /> Public
          </span>
        );
      case ProjectVisibility.Team:
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 text-[11px] font-medium rounded-full bg-surface-700 text-ink-primary border border-line">
            <Users className="w-3 h-3 text-accent-text" /> Team
          </span>
        );
      case ProjectVisibility.Private:
      default:
        return (
          <span className="inline-flex items-center gap-1 px-2.5 py-0.5 text-[11px] font-medium rounded-full bg-surface-700 text-ink-primary border border-line">
            <Lock className="w-3 h-3 text-ink-muted" /> Private
          </span>
        );
    }
  };

  return (
    <header className="border-b border-line bg-surface-900/60 backdrop-blur-md sticky top-0 z-30">
      <div className="max-w-6xl mx-auto px-4 sm:px-6 pt-6 sm:pt-8 pb-0">
        {/* Top breadcrumb & Actions */}
        <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div className="space-y-1">
            <div className="flex items-center flex-wrap gap-2 text-sm sm:text-base">
              <ModuleSymbol module="hub" size={20} />
              <Link
                href="/hub"
                className="group inline-flex items-center text-ink-muted hover:text-ink-primary transition-colors cursor-pointer"
              >
                <ModuleWordmark module="hub" height={13} className="text-ink-muted group-hover:text-ink-primary transition-colors" />
              </Link>
              <span className="text-ink-muted">/</span>
              <span className="text-ink-secondary font-mono">{repo.owner.username}</span>
              <span className="text-ink-muted">/</span>
              <h1 className="font-sans font-bold text-ink-primary tracking-tight text-base sm:text-lg">
                {repo.name || repo.slug}
              </h1>
              {repo.slug && repo.slug !== repo.name && (
                <span className="text-[11px] font-mono text-ink-muted px-1.5 py-0.5 rounded-[var(--radius-control)] bg-surface-800 border border-line">
                  {repo.slug}
                </span>
              )}
              {visibilityPill()}
            </div>

            {repo.forkedFrom && (
              <p className="text-xs text-ink-muted">
                forked from{' '}
                <Link
                  href={`/hub/${encodeURIComponent(repo.forkedFrom.ownerUsername)}/${encodeURIComponent(
                    repo.forkedFrom.slug || repo.forkedFrom.id
                  )}`}
                  className="text-accent-text hover:underline font-mono"
                >
                  {repo.forkedFrom.ownerUsername}/{repo.forkedFrom.name}
                </Link>
              </p>
            )}
          </div>

          {/* Action buttons */}
          <div className="flex items-center gap-2 self-start sm:self-center">
            {/* Star button */}
            <button
              onClick={handleToggleStar}
              disabled={isStarring}
              className={`inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-[var(--radius-control)] border transition-all cursor-pointer ${
                isStarred
                  ? 'bg-surface-700 border-accent text-accent-text shadow-xs'
                  : 'bg-surface-800 border-line text-ink-secondary hover:text-ink-primary hover:border-line-strong'
              }`}
            >
              <Star
                className={`w-3.5 h-3.5 ${
                  isStarred ? 'fill-accent-text text-accent-text' : 'text-ink-muted'
                }`}
              />
              <span>{isStarred ? 'Starred' : 'Star'}</span>
              <span className="ml-1 px-1.5 py-0.2 rounded-[var(--radius-control)] bg-surface-900 border border-line text-[11px] font-mono text-ink-muted">
                {starCount}
              </span>
            </button>

            {/* Fork button */}
            <button
              onClick={() => setIsForkModalOpen(true)}
              className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-medium rounded-[var(--radius-control)] border border-line bg-surface-800 text-ink-secondary hover:text-ink-primary hover:border-line-strong transition-all cursor-pointer"
            >
              <GitFork className="w-3.5 h-3.5 text-ink-muted" />
              <span>Fork</span>
              <span className="ml-1 px-1.5 py-0.2 rounded-[var(--radius-control)] bg-surface-900 border border-line text-[11px] font-mono text-ink-muted">
                {repo.forkCount}
              </span>
            </button>


          </div>
        </div>

        {/* Sub-nav Tabs */}
        <nav className="flex items-center gap-1 mt-5 -mb-px overflow-x-auto border-t border-line/60 pt-1">
          <button
            onClick={() => onTabChange('schema')}
            className={`inline-flex items-center gap-2 px-3 py-2 text-xs font-medium border-b-2 transition-all cursor-pointer ${
              activeTab === 'schema'
                ? 'border-accent text-ink-primary font-semibold'
                : 'border-transparent text-ink-muted hover:text-ink-secondary'
            }`}
          >
            <CodeXml className="w-4 h-4 text-accent-text" />
            <span>Schema &amp; Code</span>
          </button>

          <button
            onClick={() => onTabChange('issues')}
            className={`inline-flex items-center gap-2 px-3 py-2 text-xs font-medium border-b-2 transition-all cursor-pointer ${
              activeTab === 'issues'
                ? 'border-accent text-ink-primary font-semibold'
                : 'border-transparent text-ink-muted hover:text-ink-secondary'
            }`}
          >
            <CircleDot className="w-4 h-4 text-ink-muted" />
            <span>Issues</span>
            {repo.stats.openIssuesCount > 0 && (
              <span className="px-1.5 py-0.2 text-[10px] font-mono rounded-full bg-surface-700 text-ink-secondary border border-line">
                {repo.stats.openIssuesCount}
              </span>
            )}
          </button>

          <button
            onClick={() => onTabChange('integrations')}
            className={`inline-flex items-center gap-2 px-3 py-2 text-xs font-medium border-b-2 transition-all cursor-pointer ${
              activeTab === 'integrations'
                ? 'border-accent text-ink-primary font-semibold'
                : 'border-transparent text-ink-muted hover:text-ink-secondary'
            }`}
          >
            <Layers className="w-4 h-4 text-accent-text" />
            <span>Integrations</span>
          </button>

          <button
            onClick={() => onTabChange('prs')}
            className={`inline-flex items-center gap-2 px-3 py-2 text-xs font-medium border-b-2 transition-all cursor-pointer ${
              activeTab === 'prs'
                ? 'border-accent text-ink-primary font-semibold'
                : 'border-transparent text-ink-muted hover:text-ink-secondary'
            }`}
          >
            <GitPullRequest className="w-4 h-4 text-ink-muted" />
            <span>Pull Requests</span>
            {repo.stats.openPrCount > 0 && (
              <span className="px-1.5 py-0.2 text-[10px] font-mono rounded-full bg-surface-700 text-ink-secondary border border-line">
                {repo.stats.openPrCount}
              </span>
            )}
          </button>

          {repo.caller?.canEdit && (
            <button
              onClick={() => onTabChange('settings')}
              className={`inline-flex items-center gap-2 px-3 py-2 text-xs font-medium border-b-2 transition-all cursor-pointer ${
                activeTab === 'settings'
                  ? 'border-accent text-ink-primary font-semibold'
                  : 'border-transparent text-ink-muted hover:text-ink-secondary'
              }`}
            >
              <SettingsIcon className="w-4 h-4 text-ink-muted" />
              <span>Settings</span>
            </button>
          )}
        </nav>
      </div>

      {/* Fork Modal */}
      {isForkModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-scrim/80 backdrop-blur-xs">
          <div className="w-full max-w-md bg-surface-800 border border-line-strong rounded-[var(--radius-card)] p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between">
              <h2 className="text-base font-semibold text-ink-primary font-sans flex items-center gap-2">
                <GitFork className="w-4 h-4 text-accent-text" /> Fork Repository
              </h2>
              <button
                type="button"
                onClick={() => setIsForkModalOpen(false)}
                className="text-ink-muted hover:text-ink-primary text-xs cursor-pointer"
              >
                Cancel
              </button>
            </div>

            <p className="text-xs text-ink-secondary leading-relaxed font-sans">
              A fork is a copy of a repository. Forking allows you to freely experiment with changes
              without affecting the original schema.
            </p>

            <form onSubmit={handleFork} className="space-y-4">
              <div>
                <label className="block text-xs font-medium text-ink-secondary mb-1">
                  Repository Name
                </label>
                <input
                  type="text"
                  value={forkName}
                  onChange={(e) => setForkName(e.target.value)}
                  required
                  className="w-full px-3 py-2 text-xs font-mono rounded-[var(--radius-control)] bg-surface-900 border border-line text-ink-primary focus:outline-hidden focus:border-accent"
                />
              </div>

              <div className="flex justify-end gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setIsForkModalOpen(false)}
                  className="px-3.5 py-1.5 text-xs rounded-[var(--radius-control)] border border-line bg-surface-700 hover:bg-surface-650 text-ink-secondary hover:text-ink-primary transition-colors cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={isForking}
                  className="inline-flex items-center gap-1.5 px-4 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line-strong shadow-xs disabled:opacity-50 transition-all cursor-pointer"
                >
                  {isForking ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Check className="w-3.5 h-3.5" />}
                  <span>Create fork</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}
    </header>
  );
}
