'use client';

import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  Settings,
  Globe,
  Lock,
  Users,
  Save,
  Loader2,
  Shield,
  GitBranch,
  RefreshCw,
  ExternalLink,
  AlertTriangle,
  Trash2,
  Database,
  Tag,
  Check,
  X,
} from 'lucide-react';
import type { DatabaseSchema } from '../../types/schema';
import type { HubRepoDetail } from '../../types/hub';
import { ProjectVisibility, normalizeVisibility } from '../../types/hub';
import { hubService } from '../../services/hubService';
import { sourceService } from '../../services/api';
import { trackHubGithubSync } from '../../lib/analytics';
import { useToastStore } from '../../store/useToastStore';
import { Select } from '../ui/Select';

interface HubSettingsTabProps {
  repo: HubRepoDetail;
  onRepoUpdated?: (updated: Partial<HubRepoDetail>) => void;
}

const CATEGORIES = [
  'General',
  'FinTech',
  'SaaS',
  'E-Commerce',
  'ERP',
  'AI',
  'Healthcare',
  'Enterprise',
  'Analytics',
  'Logistics',
];

const DB_ENGINES = [
  'PostgreSQL',
  'MySQL',
  'SQLite',
  'SQL Server',
  'MariaDB',
  'CockroachDB',
];

export function HubSettingsTab({ repo, onRepoUpdated }: HubSettingsTabProps) {
  const router = useRouter();
  const { showToast } = useToastStore();

  // General Settings
  const [name, setName] = useState(repo.name || '');
  const [slug, setSlug] = useState(repo.slug || '');
  const [description, setDescription] = useState(repo.description || '');
  const [visibility, setVisibility] = useState<ProjectVisibility>(
    normalizeVisibility(repo.visibility)
  );

  // Engine & Architecture
  const [dbType, setDbType] = useState(repo.dbType || 'PostgreSQL');
  const [category, setCategory] = useState(repo.category || 'General');
  const [tags, setTags] = useState(repo.tags || '');

  // GitHub Integration
  const [linkedGithubRepo, setLinkedGithubRepo] = useState(repo.linkedGithubRepo || '');
  const [isSyncingGithub, setIsSyncingGithub] = useState(false);

  // Governance & Quality Gates
  const [autoApproveSafeChanges, setAutoApproveSafeChanges] = useState(
    Boolean(repo.autoApproveSafeChanges)
  );
  const [strict3NF, setStrict3NF] = useState(true);

  // Saving states
  const [isSavingGeneral, setIsSavingGeneral] = useState(false);
  const [isSavingArchitecture, setIsSavingArchitecture] = useState(false);
  const [isSavingGovernance, setIsSavingGovernance] = useState(false);

  // Danger Zone - Delete Modal
  const [isDeleteModalOpen, setIsDeleteModalOpen] = useState(false);
  const [deleteConfirmationInput, setDeleteConfirmationInput] = useState('');
  const [isDeleting, setIsDeleting] = useState(false);

  const expectedSlug = (repo.slug || '').trim();
  const expectedName = (repo.name || '').trim();
  const displayConfirmTarget = expectedSlug || expectedName;

  const isDeleteMatch =
    deleteConfirmationInput.trim().length > 0 &&
    (deleteConfirmationInput.trim().toLowerCase() === expectedSlug.toLowerCase() ||
      deleteConfirmationInput.trim().toLowerCase() === expectedName.toLowerCase());

  // Save General Profile & Visibility
  const handleSaveGeneral = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSavingGeneral(true);
    try {
      const updated = await hubService.updateSettings(repo.id, {
        name: name.trim() || undefined,
        slug: slug.trim() || undefined,
        description: description.trim() || undefined,
        visibility,
      });

      onRepoUpdated?.(updated);
      showToast('General settings saved successfully.', 'success');
    } catch {
      showToast('Failed to update general settings. Check slug uniqueness.', 'error');
    } finally {
      setIsSavingGeneral(false);
    }
  };

  // Save Architecture & Engine
  const handleSaveArchitecture = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSavingArchitecture(true);
    try {
      const updated = await hubService.updateSettings(repo.id, {
        dbType,
        category,
        tags: tags.trim() || undefined,
      });

      onRepoUpdated?.(updated);
      showToast('Architecture & Engine settings saved.', 'success');
    } catch {
      showToast('Failed to update architecture settings.', 'error');
    } finally {
      setIsSavingArchitecture(false);
    }
  };

  // Save Governance & Quality Gates
  const handleSaveGovernance = async (e: React.FormEvent) => {
    e.preventDefault();
    setIsSavingGovernance(true);
    try {
      const updated = await hubService.updateSettings(repo.id, {
        autoApproveSafeChanges,
      });

      onRepoUpdated?.(updated);
      showToast('Governance rules updated successfully.', 'success');
    } catch {
      showToast('Failed to update governance rules.', 'error');
    } finally {
      setIsSavingGovernance(false);
    }
  };

  // GitHub Sync Trigger
  const handleSyncGithub = async () => {
    if (!linkedGithubRepo) {
      showToast('Please enter a GitHub repository (e.g. owner/repo) first.', 'warning');
      return;
    }
    setIsSyncingGithub(true);
    try {
      const url = linkedGithubRepo.startsWith('http')
        ? linkedGithubRepo
        : `https://github.com/${linkedGithubRepo}`;
      const res = await sourceService.scanRepository(url);
      trackHubGithubSync(repo.id, linkedGithubRepo);

      await hubService.updateSettings(repo.id, {
        linkedGithubRepo: linkedGithubRepo.trim(),
      });
      onRepoUpdated?.({ linkedGithubRepo: linkedGithubRepo.trim() });

      const tableCount = (res.schema as DatabaseSchema)?.tables?.length ?? 0;
      showToast(
        `Successfully scanned ${res.parsedFiles.length} schema file(s) and ${tableCount} tables from GitHub!`,
        'success'
      );
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : 'Failed to scan repository from GitHub.';
      showToast(msg, 'error');
    } finally {
      setIsSyncingGithub(false);
    }
  };

  // Danger Zone - Toggle Visibility
  const handleToggleVisibility = async () => {
    const next =
      visibility === ProjectVisibility.Public
        ? ProjectVisibility.Private
        : ProjectVisibility.Public;

    setVisibility(next);
    try {
      const updated = await hubService.updateSettings(repo.id, { visibility: next });
      onRepoUpdated?.(updated);
      showToast(
        `Repository visibility changed to ${next === ProjectVisibility.Public ? 'Public' : 'Private'}.`,
        'info'
      );
    } catch {
      showToast('Failed to update repository visibility.', 'error');
    }
  };

  // Danger Zone - Delete Repository
  const handleDeleteRepository = async () => {
    if (!isDeleteMatch || isDeleting) return;

    setIsDeleting(true);
    try {
      await hubService.deleteRepo(repo.id);
      showToast(`Repository "${repo.name}" was permanently deleted.`, 'info');
      setIsDeleteModalOpen(false);
      router.push('/hub');
    } catch {
      showToast('Failed to delete repository. Check permissions.', 'error');
      setIsDeleting(false);
    }
  };

  const tagList = tags
    .split(',')
    .map(t => t.trim())
    .filter(Boolean);

  return (
    <div className="w-full space-y-6 font-sans">
      {/* 1. General Profile & Visibility (Shopify Polaris Card) */}
      <form
        onSubmit={handleSaveGeneral}
        className="bg-surface-850 rounded-[var(--radius-card)] border border-line overflow-hidden shadow-xs"
      >
        <div className="p-5 border-b border-line bg-surface-800/40">
          <h2 className="text-sm font-semibold text-content-primary flex items-center gap-2">
            <Settings className="w-4 h-4 text-accent-text" />
            <span>General Repository Settings</span>
          </h2>
          <p className="text-xs text-content-muted mt-1">
            Configure public identity, URL naming, and access permissions for this architecture.
          </p>
        </div>

        <div className="p-5 space-y-5">
          {/* Name & Slug */}
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium text-content-secondary mb-1.5">
                Repository Name
              </label>
              <input
                type="text"
                value={name}
                onChange={e => setName(e.target.value)}
                placeholder="Enterprise ERP Core"
                required
                className="w-full px-3 py-2 text-xs rounded-[var(--radius-control)] bg-surface-900 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-accent"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-content-secondary mb-1.5">
                URL Slug
              </label>
              <div className="flex items-center rounded-[var(--radius-control)] bg-surface-900 border border-line overflow-hidden focus-within:border-accent">
                <span className="px-2.5 py-2 text-xs font-mono text-content-muted bg-surface-800 border-r border-line shrink-0">
                  /hub/{repo.owner.username}/
                </span>
                <input
                  type="text"
                  value={slug}
                  onChange={e => setSlug(e.target.value)}
                  placeholder="enterprise-erp-core"
                  className="w-full px-2.5 py-2 text-xs font-mono bg-transparent text-content-primary focus:outline-hidden"
                />
              </div>
            </div>
          </div>

          {/* Description */}
          <div>
            <label className="block text-xs font-medium text-content-secondary mb-1.5">
              Description
            </label>
            <textarea
              rows={3}
              value={description}
              onChange={e => setDescription(e.target.value)}
              placeholder="Describe the business domain, schema design patterns, and normalization goals..."
              className="w-full px-3 py-2 text-xs rounded-[var(--radius-control)] bg-surface-900 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-accent resize-y font-sans leading-relaxed"
            />
          </div>

          {/* Visibility Options Radio Cards */}
          <div className="space-y-2">
            <label className="block text-xs font-medium text-content-secondary">
              Repository Visibility
            </label>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              {/* Public */}
              <button
                type="button"
                onClick={() => setVisibility(ProjectVisibility.Public)}
                className={`p-3.5 rounded-[var(--radius-control)] border text-left cursor-pointer transition-all ${
                  visibility === ProjectVisibility.Public
                    ? 'bg-surface-700/80 border-accent text-content-primary shadow-xs ring-1 ring-accent'
                    : 'bg-surface-900/60 border-line text-content-muted hover:text-content-secondary hover:border-line-strong'
                }`}
              >
                <div className="flex items-center justify-between mb-1.5">
                  <div className="flex items-center gap-2 font-medium text-xs text-content-primary">
                    <Globe className="w-3.5 h-3.5 text-accent-text" />
                    <span>Public</span>
                  </div>
                  {visibility === ProjectVisibility.Public && (
                    <Check className="w-3.5 h-3.5 text-accent-text" />
                  )}
                </div>
                <p className="text-[11px] leading-relaxed text-content-muted">
                  Visible to everyone on Namines Hub. Community members can view, star, and fork this architecture.
                </p>
              </button>

              {/* Team */}
              <button
                type="button"
                onClick={() => setVisibility(ProjectVisibility.Team)}
                className={`p-3.5 rounded-[var(--radius-control)] border text-left cursor-pointer transition-all ${
                  visibility === ProjectVisibility.Team
                    ? 'bg-surface-700/80 border-accent text-content-primary shadow-xs ring-1 ring-accent'
                    : 'bg-surface-900/60 border-line text-content-muted hover:text-content-secondary hover:border-line-strong'
                }`}
              >
                <div className="flex items-center justify-between mb-1.5">
                  <div className="flex items-center gap-2 font-medium text-xs text-content-primary">
                    <Users className="w-3.5 h-3.5 text-accent-text" />
                    <span>Team</span>
                  </div>
                  {visibility === ProjectVisibility.Team && (
                    <Check className="w-3.5 h-3.5 text-accent-text" />
                  )}
                </div>
                <p className="text-[11px] leading-relaxed text-content-muted">
                  Accessible only to organization members and assigned workspace collaborators.
                </p>
              </button>

              {/* Private */}
              <button
                type="button"
                onClick={() => setVisibility(ProjectVisibility.Private)}
                className={`p-3.5 rounded-[var(--radius-control)] border text-left cursor-pointer transition-all ${
                  visibility === ProjectVisibility.Private
                    ? 'bg-surface-700/80 border-accent text-content-primary shadow-xs ring-1 ring-accent'
                    : 'bg-surface-900/60 border-line text-content-muted hover:text-content-secondary hover:border-line-strong'
                }`}
              >
                <div className="flex items-center justify-between mb-1.5">
                  <div className="flex items-center gap-2 font-medium text-xs text-content-primary">
                    <Lock className="w-3.5 h-3.5 text-content-muted" />
                    <span>Private</span>
                  </div>
                  {visibility === ProjectVisibility.Private && (
                    <Check className="w-3.5 h-3.5 text-accent-text" />
                  )}
                </div>
                <p className="text-[11px] leading-relaxed text-content-muted">
                  Restricted strictly to you. Hidden from Hub discovery, explore feeds, and search indices.
                </p>
              </button>
            </div>
          </div>
        </div>

        {/* Action Footer */}
        <div className="px-5 py-3 bg-surface-800/40 border-t border-line flex justify-end">
          <button
            type="submit"
            disabled={isSavingGeneral}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-primary border border-line-strong shadow-xs transition-all disabled:opacity-50 cursor-pointer"
          >
            {isSavingGeneral ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
            <span>Save General Settings</span>
          </button>
        </div>
      </form>

      {/* 2. Architecture & Engine Settings */}
      <form
        onSubmit={handleSaveArchitecture}
        className="bg-surface-850 rounded-[var(--radius-card)] border border-line overflow-hidden shadow-xs"
      >
        <div className="p-5 border-b border-line bg-surface-800/40">
          <h2 className="text-sm font-semibold text-content-primary flex items-center gap-2">
            <Database className="w-4 h-4 text-accent-text" />
            <span>Architecture &amp; Engine</span>
          </h2>
          <p className="text-xs text-content-muted mt-1">
            Specify the primary database engine dialect and classify your domain for the directory.
          </p>
        </div>

        <div className="p-5 space-y-4">
          <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
            <div>
              <label className="block text-xs font-medium text-content-secondary mb-1.5">
                Target Database Engine
              </label>
              <Select
                value={dbType}
                onChange={e => setDbType(e.target.value)}
                options={DB_ENGINES.map(eng => ({ value: eng, label: eng }))}
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-content-secondary mb-1.5">
                Industry Category
              </label>
              <Select
                value={category}
                onChange={e => setCategory(e.target.value)}
                options={CATEGORIES.map(cat => ({ value: cat, label: cat }))}
              />
            </div>
          </div>

          <div>
            <label className="block text-xs font-medium text-content-secondary mb-1.5">
              Tags &amp; Keywords (comma separated)
            </label>
            <input
              type="text"
              value={tags}
              onChange={e => setTags(e.target.value)}
              placeholder="postgresql, 3nf, multi-tenant, billing, ledger"
              className="w-full px-3 py-2 text-xs rounded-[var(--radius-control)] bg-surface-900 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-accent"
            />
            {tagList.length > 0 && (
              <div className="flex flex-wrap gap-1.5 mt-2">
                {tagList.map(tag => (
                  <span
                    key={tag}
                    className="inline-flex items-center gap-1 px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800 border border-line text-[11px] font-mono text-content-secondary"
                  >
                    <Tag className="w-2.5 h-2.5 text-accent-text" />
                    <span>{tag}</span>
                  </span>
                ))}
              </div>
            )}
          </div>
        </div>

        <div className="px-5 py-3 bg-surface-800/40 border-t border-line flex justify-end">
          <button
            type="submit"
            disabled={isSavingArchitecture}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-primary border border-line-strong shadow-xs transition-all disabled:opacity-50 cursor-pointer"
          >
            {isSavingArchitecture ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
            <span>Save Architecture Settings</span>
          </button>
        </div>
      </form>

      {/* 3. GitHub Integration & Source Sync */}
      <div className="bg-surface-850 rounded-[var(--radius-card)] border border-line overflow-hidden shadow-xs">
        <div className="p-5 border-b border-line bg-surface-800/40">
          <h2 className="text-sm font-semibold text-content-primary flex items-center gap-2">
            <GitBranch className="w-4 h-4 text-accent-text" />
            <span>GitHub Integration &amp; Continuous Sync</span>
          </h2>
          <p className="text-xs text-content-muted mt-1">
            Connect a GitHub repository to automatically introspect SQL migrations and Prisma schemas.
          </p>
        </div>

        <div className="p-5 space-y-4">
          <div>
            <label className="block text-xs font-medium text-content-secondary mb-1.5">
              Linked GitHub Repository
            </label>
            <div className="flex items-center rounded-[var(--radius-control)] bg-surface-900 border border-line overflow-hidden focus-within:border-accent">
              <span className="px-2.5 py-2 text-xs font-mono text-content-muted bg-surface-800 border-r border-line">
                <GitBranch className="w-3.5 h-3.5 text-accent-text" />
              </span>
              <input
                type="text"
                value={linkedGithubRepo}
                onChange={e => setLinkedGithubRepo(e.target.value)}
                placeholder="owner/repository (e.g. namines/enterprise-erp-core)"
                className="w-full px-2.5 py-2 text-xs font-mono bg-transparent text-content-primary placeholder:text-content-muted focus:outline-hidden"
              />
            </div>

            {linkedGithubRepo && (
              <div className="flex items-center gap-3 mt-3">
                <button
                  type="button"
                  onClick={handleSyncGithub}
                  disabled={isSyncingGithub}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs rounded-[var(--radius-control)] border border-line bg-surface-700 hover:bg-surface-600 text-content-primary font-medium transition-colors cursor-pointer disabled:opacity-50 shadow-xs"
                >
                  <RefreshCw className={`w-3.5 h-3.5 text-accent-text ${isSyncingGithub ? 'animate-spin' : ''}`} />
                  <span>{isSyncingGithub ? 'Scanning GitHub Schema...' : 'Sync Schema from GitHub'}</span>
                </button>
                <a
                  href={
                    linkedGithubRepo.startsWith('http')
                      ? linkedGithubRepo
                      : `https://github.com/${linkedGithubRepo}`
                  }
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 text-xs text-content-muted hover:text-accent-text transition-colors"
                >
                  <span>View on GitHub</span>
                  <ExternalLink className="w-3.5 h-3.5" />
                </a>
              </div>
            )}
          </div>
        </div>
      </div>

      {/* 4. Governance & Quality Gates */}
      <form
        onSubmit={handleSaveGovernance}
        className="bg-surface-850 rounded-[var(--radius-card)] border border-line overflow-hidden shadow-xs"
      >
        <div className="p-5 border-b border-line bg-surface-800/40">
          <h2 className="text-sm font-semibold text-content-primary flex items-center gap-2">
            <Shield className="w-4 h-4 text-accent-text" />
            <span>Governance &amp; Quality Gates</span>
          </h2>
          <p className="text-xs text-content-muted mt-1">
            Enforce normalization rules and automated migration approval policies.
          </p>
        </div>

        <div className="p-5 space-y-4">
          {/* Strict 3NF Gate */}
          <div className="flex items-start justify-between gap-4 p-3.5 rounded-[var(--radius-control)] bg-surface-900/50 border border-line">
            <div className="space-y-1">
              <h4 className="text-xs font-semibold text-content-primary">
                Enforce Strict 3NF Normalization
              </h4>
              <p className="text-xs text-content-muted leading-relaxed">
                Flags missing primary keys, redundant composite attributes, and unindexed foreign keys before permitting branch merges.
              </p>
            </div>
            <label className="relative inline-flex items-center cursor-pointer shrink-0 mt-0.5">
              <input
                type="checkbox"
                checked={strict3NF}
                onChange={e => setStrict3NF(e.target.checked)}
                className="sr-only peer"
              />
              <div className="w-9 h-5 bg-surface-700 peer-focus:outline-hidden rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-surface-900 after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-surface-900 after:border-line after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-content-primary"></div>
            </label>
          </div>

          {/* Auto-Approve Safe Migrations */}
          <div className="flex items-start justify-between gap-4 p-3.5 rounded-[var(--radius-control)] bg-surface-900/50 border border-line">
            <div className="space-y-1">
              <h4 className="text-xs font-semibold text-content-primary">
                Auto-Approve Safe Schema Changes
              </h4>
              <p className="text-xs text-content-muted leading-relaxed">
                Non-destructive changes (adding nullable columns, creating non-unique indexes, or adding isolated tables) bypass manual approval.
              </p>
            </div>
            <label className="relative inline-flex items-center cursor-pointer shrink-0 mt-0.5">
              <input
                type="checkbox"
                checked={autoApproveSafeChanges}
                onChange={e => setAutoApproveSafeChanges(e.target.checked)}
                className="sr-only peer"
              />
              <div className="w-9 h-5 bg-surface-700 peer-focus:outline-hidden rounded-full peer peer-checked:after:translate-x-full peer-checked:after:border-surface-900 after:content-[''] after:absolute after:top-[2px] after:left-[2px] after:bg-surface-900 after:border-line after:border after:rounded-full after:h-4 after:w-4 after:transition-all peer-checked:bg-content-primary"></div>
            </label>
          </div>
        </div>

        <div className="px-5 py-3 bg-surface-800/40 border-t border-line flex justify-end">
          <button
            type="submit"
            disabled={isSavingGovernance}
            className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-primary border border-line-strong shadow-xs transition-all disabled:opacity-50 cursor-pointer"
          >
            {isSavingGovernance ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Save className="w-3.5 h-3.5" />}
            <span>Save Governance Rules</span>
          </button>
        </div>
      </form>

      {/* 5. Danger Zone (Shopify Polaris High-Contrast Red Card) */}
      <div className="rounded-[var(--radius-card)] border border-danger/40 bg-surface-850 overflow-hidden shadow-xs">
        <div className="p-4 sm:p-5 border-b border-danger/20 bg-danger-subtle/30 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-[var(--radius-control)] bg-danger/15 border border-danger/30 flex items-center justify-center text-danger">
              <AlertTriangle className="w-4 h-4" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-danger uppercase tracking-wider font-mono">
                Danger Zone
              </h2>
              <p className="text-[11px] text-content-muted">
                Irreversible actions affecting repository accessibility, branches, and database storage.
              </p>
            </div>
          </div>
        </div>

        <div className="divide-y divide-line">
          {/* Row 1: Change Visibility */}
          <div className="p-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div className="space-y-1">
              <h4 className="text-xs font-semibold text-content-primary">
                Change Repository Visibility
              </h4>
              <p className="text-xs text-content-muted">
                This repository is currently{' '}
                <span className="font-semibold text-content-primary underline">
                  {visibility}
                </span>
                . {visibility === ProjectVisibility.Public
                  ? 'Switching to Private will immediately hide it from all public directory feeds.'
                  : 'Switching to Public will allow anyone on Namines Hub to view, star, and fork it.'}
              </p>
            </div>
            <button
              type="button"
              onClick={handleToggleVisibility}
              className="px-3.5 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] border border-line-strong bg-surface-700 hover:bg-surface-600 text-content-primary transition-all cursor-pointer shrink-0 shadow-xs"
            >
              Make {visibility === ProjectVisibility.Public ? 'Private' : 'Public'}
            </button>
          </div>

          {/* Row 2: Delete Repository */}
          <div className="p-5 flex flex-col sm:flex-row sm:items-center justify-between gap-4">
            <div className="space-y-1">
              <h4 className="text-xs font-semibold text-danger">
                Delete This Repository
              </h4>
              <p className="text-xs text-content-muted leading-relaxed">
                Once deleted, all database schemas, visual coordinates, migration PRs, issues, and discussions will be permanently erased.
              </p>
            </div>
            <button
              type="button"
              onClick={() => {
                setDeleteConfirmationInput('');
                setIsDeleteModalOpen(true);
              }}
              className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-danger-subtle hover:bg-danger/25 text-danger-text border border-danger/40 transition-all cursor-pointer shrink-0 shadow-xs"
            >
              <Trash2 className="w-3.5 h-3.5" />
              <span>Delete this repository</span>
            </button>
          </div>
        </div>
      </div>

      {/* Delete Confirmation Modal (Shopify Polaris Modal) */}
      {isDeleteModalOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-scrim/80 backdrop-blur-xs">
          <div className="w-full max-w-md bg-surface-800 border border-line-strong rounded-[var(--radius-card)] p-6 shadow-2xl space-y-4">
            <div className="flex items-center justify-between border-b border-line pb-3">
              <h3 className="text-sm font-bold text-danger flex items-center gap-2">
                <AlertTriangle className="w-4 h-4 text-danger" />
                <span>Delete Repository Confirmation</span>
              </h3>
              <button
                type="button"
                onClick={() => setIsDeleteModalOpen(false)}
                className="text-content-muted hover:text-content-primary cursor-pointer p-1"
              >
                <X className="w-4 h-4" />
              </button>
            </div>

            <div className="p-3 bg-danger-subtle/40 border border-danger/30 rounded-[var(--radius-control)] text-xs text-danger-text leading-relaxed">
              This action <strong>cannot be undone</strong>. This will permanently delete the{' '}
              <span className="font-mono font-bold text-content-primary">
                {repo.owner.username}/{displayConfirmTarget}
              </span>{' '}
              repository, branches, schema history, and all issues.
            </div>

            <div className="space-y-2">
              <label className="block text-xs font-medium text-content-secondary">
                Please type <span className="font-mono font-bold text-content-primary select-all">{displayConfirmTarget}</span> to confirm:
              </label>
              <input
                type="text"
                autoFocus
                value={deleteConfirmationInput}
                onChange={e => setDeleteConfirmationInput(e.target.value)}
                placeholder={displayConfirmTarget}
                className="w-full px-3 py-2 text-xs font-mono rounded-[var(--radius-control)] bg-surface-900 border border-line text-content-primary placeholder:text-content-muted focus:outline-hidden focus:border-danger"
              />
              {deleteConfirmationInput.trim().length > 0 && !isDeleteMatch && (
                <p className="text-[11px] text-danger font-mono">
                  Input does not match repository name ({displayConfirmTarget})
                </p>
              )}
            </div>

            <div className="pt-2 flex items-center justify-end gap-2.5">
              <button
                type="button"
                onClick={() => setIsDeleteModalOpen(false)}
                className="px-3.5 py-1.5 text-xs font-medium rounded-[var(--radius-control)] border border-line bg-surface-700 hover:bg-surface-650 text-content-primary transition-colors cursor-pointer"
              >
                Cancel
              </button>
              <button
                type="button"
                onClick={handleDeleteRepository}
                disabled={!isDeleteMatch || isDeleting}
                className={`inline-flex items-center gap-1.5 px-4 py-2 text-xs font-bold rounded-[var(--radius-control)] border transition-all shadow-xs cursor-pointer ${
                  isDeleteMatch
                    ? 'bg-danger hover:bg-danger-hover text-surface-900 border-danger active:scale-98'
                    : 'bg-surface-700/60 text-content-muted border-line cursor-not-allowed opacity-50'
                }`}
              >
                {isDeleting ? (
                  <Loader2 className="w-3.5 h-3.5 animate-spin" />
                ) : (
                  <Trash2 className="w-3.5 h-3.5" />
                )}
                <span>I understand the consequences, delete this repository</span>
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}