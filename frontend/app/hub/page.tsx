'use client';

import React, { useState, useEffect, useRef } from 'react';
import Link from 'next/link';
import {
  Search,
  Star,
  GitFork,
  Database,
  Filter,
  Plus,
  Loader2,
  Globe,
  Table as TableIcon,
  Sparkles,
  ChevronDown,
  Check,
  ShieldCheck,
  Layers,
  ArrowUpDown,
  X,
  HardDrive,
  Zap,
  KeyRound,
} from 'lucide-react';
import type { HubRepoSummary } from '../../types/hub';
import { hubService } from '../../services/hubService';
import { useAuthStore } from '../../store/useAuthStore';
import { useActiveProjectStore } from '../../store/useActiveProjectStore';

const CATEGORIES = [
  'All Categories',
  'FinTech',
  'SaaS',
  'E-Commerce',
  'ERP',
  'AI',
  'Healthcare',
  'Enterprise',
  'General',
];

const ENGINES = ['All Engines', 'PostgreSQL', 'MySQL', 'SQLite', 'SQL Server', 'MariaDB'];

type SortOption = 'popular' | 'recent' | 'forks';

export default function HubExplorePage() {
  const { isAuthenticated } = useAuthStore();

  const [repos, setRepos] = useState<HubRepoSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [selectedCategory, setSelectedCategory] = useState('All Categories');
  const [selectedEngine, setSelectedEngine] = useState('All Engines');
  const [sortBy, setSortBy] = useState<SortOption>('popular');

  // Custom Dropdown Open States
  const [isCategoryOpen, setIsCategoryOpen] = useState(false);
  const [isEngineOpen, setIsEngineOpen] = useState(false);
  const [isSortOpen, setIsSortOpen] = useState(false);

  const categoryRef = useRef<HTMLDivElement>(null);
  const engineRef = useRef<HTMLDivElement>(null);
  const sortRef = useRef<HTMLDivElement>(null);

  // Close dropdowns when clicking outside
  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (categoryRef.current && !categoryRef.current.contains(e.target as Node)) {
        setIsCategoryOpen(false);
      }
      if (engineRef.current && !engineRef.current.contains(e.target as Node)) {
        setIsEngineOpen(false);
      }
      if (sortRef.current && !sortRef.current.contains(e.target as Node)) {
        setIsSortOpen(false);
      }
    }
    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  useEffect(() => {
    useActiveProjectStore.getState().clearProjectSession();
  }, []);

  // Tek efekt: debounce + iptal bayrağı. Yavaş eski yanıt, yeni aramanın sonucunu ezemez.
  useEffect(() => {
    let cancelled = false;
    const timer = setTimeout(async () => {
      setLoading(true);
      try {
        const res = await hubService.explore({
          q: search.trim() || undefined,
          category: selectedCategory !== 'All Categories' ? selectedCategory : undefined,
          dbType: selectedEngine !== 'All Engines' ? selectedEngine : undefined,
          sort: sortBy === 'forks' ? 'popular' : sortBy,
        });
        if (cancelled) return;

        let items = res.items;
        if (sortBy === 'forks') {
          items = [...items].sort((a, b) => b.forkCount - a.forkCount);
        }
        setRepos(items);
      } catch {
        // Empty fallback
      } finally {
        if (!cancelled) setLoading(false);
      }
    }, 250);
    return () => {
      cancelled = true;
      clearTimeout(timer);
    };
  }, [search, selectedCategory, selectedEngine, sortBy]);

  const sortLabel = () => {
    switch (sortBy) {
      case 'popular':
        return 'Most Starred';
      case 'forks':
        return 'Most Forked';
      case 'recent':
      default:
        return 'Recently Created';
    }
  };

  return (
    <div className="min-h-[calc(100vh-56px)] bg-surface-900 text-content-primary flex flex-col font-sans">
      {/* Top Polaris Global Control Strip */}
      <div className="border-b border-line bg-surface-900/90 backdrop-blur-md">
        <div className="max-w-6xl mx-auto px-4 sm:px-6 pt-6 sm:pt-8 pb-3 flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] bg-surface-800 border border-line text-xs font-mono font-medium text-content-primary">
              <Globe className="w-3.5 h-3.5 text-accent-text" />
              <span>Global Hub Directory</span>
            </div>
          </div>

          <div className="flex items-center gap-3">
            <Link
              href="/canvas"
              className="inline-flex items-center gap-1.5 px-3.5 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-primary border border-line-strong shadow-xs transition-all cursor-pointer"
            >
              <Plus className="w-3.5 h-3.5" />
              <span>New Architecture</span>
            </Link>
          </div>
        </div>
      </div>

      {/* Main Container */}
      <main className="max-w-6xl mx-auto px-4 sm:px-6 py-6 flex-1 space-y-6 w-full">
        {/* Polaris Hero Banner Card */}
        <div className="rounded-[var(--radius-card)] border border-line bg-surface-850 p-6 sm:p-8 text-center shadow-xs space-y-3">
          <div className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full text-xs font-mono bg-accent/20 border border-accent/40 text-accent-text">
            <Sparkles className="w-3.5 h-3.5" />
            <span>Architecture Directory &amp; Blueprints</span>
          </div>

          <h1 className="text-xl sm:text-2xl font-bold tracking-tight text-ink-primary font-sans">
            Explore Production Database Schemas
          </h1>

          <p className="text-xs sm:text-sm text-ink-secondary max-w-xl mx-auto leading-relaxed font-sans">
            Relational database architectures and ERP schemas shared by the Namines community.
          </p>
        </div>

        {/* Polaris Filter Toolbar */}
        <div className="p-3.5 bg-surface-850 border border-line rounded-[var(--radius-card)] shadow-xs flex flex-col md:flex-row items-stretch md:items-center justify-between gap-3">
          {/* Search Input */}
          <div className="relative flex-1 min-w-[220px]">
            <Search className="w-3.5 h-3.5 absolute left-3 top-1/2 -translate-y-1/2 text-ink-muted" />
            <input
              type="text"
              placeholder="Search architectures, tables, or tags..."
              value={search}
              onChange={e => setSearch(e.target.value)}
              className="w-full pl-9 pr-8 py-1.5 text-xs font-sans rounded-[var(--radius-control)] bg-surface-900 border border-line text-ink-primary placeholder:text-ink-muted focus:outline-hidden focus:border-accent"
            />
            {search && (
              <button
                type="button"
                onClick={() => setSearch('')}
                className="absolute right-2.5 top-1/2 -translate-y-1/2 text-ink-muted hover:text-ink-primary"
              >
                <X className="w-3 h-3" />
              </button>
            )}
          </div>

          {/* Dropdown Filters Group */}
          <div className="flex items-center flex-wrap gap-2 text-xs">
            {/* 1. Category Dropdown */}
            <div className="relative" ref={categoryRef}>
              <button
                type="button"
                onClick={() => {
                  setIsCategoryOpen(prev => !prev);
                  setIsEngineOpen(false);
                  setIsSortOpen(false);
                }}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-750 border border-line text-xs font-sans text-ink-primary transition-all cursor-pointer shadow-xs"
              >
                <Filter className="w-3.5 h-3.5 text-accent-text" />
                <span className="font-medium truncate max-w-[120px]">{selectedCategory}</span>
                <ChevronDown
                  className={`w-3.5 h-3.5 text-ink-muted transition-transform duration-150 ${
                    isCategoryOpen ? 'rotate-180 text-ink-primary' : ''
                  }`}
                />
              </button>

              {isCategoryOpen && (
                <div className="absolute right-0 sm:left-0 top-full mt-1.5 z-40 w-52 max-h-64 overflow-y-auto rounded-[var(--radius-control)] bg-surface-800 border border-line shadow-2xl py-1 text-xs font-sans divide-y divide-line/30">
                  {CATEGORIES.map(cat => (
                    <button
                      key={cat}
                      type="button"
                      onClick={() => {
                        setSelectedCategory(cat);
                        setIsCategoryOpen(false);
                      }}
                      className={`w-full flex items-center justify-between px-3 py-2 text-left transition-colors cursor-pointer ${
                        selectedCategory === cat
                          ? 'bg-surface-700 text-ink-primary font-semibold'
                          : 'text-ink-secondary hover:text-ink-primary hover:bg-surface-750'
                      }`}
                    >
                      <span className="truncate">{cat}</span>
                      {selectedCategory === cat && <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />}
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* 2. Database Engine Dropdown */}
            <div className="relative" ref={engineRef}>
              <button
                type="button"
                onClick={() => {
                  setIsEngineOpen(prev => !prev);
                  setIsCategoryOpen(false);
                  setIsSortOpen(false);
                }}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-750 border border-line text-xs font-sans text-ink-primary transition-all cursor-pointer shadow-xs"
              >
                <Database className="w-3.5 h-3.5 text-accent-text" />
                <span className="font-medium truncate max-w-[120px]">{selectedEngine}</span>
                <ChevronDown
                  className={`w-3.5 h-3.5 text-ink-muted transition-transform duration-150 ${
                    isEngineOpen ? 'rotate-180 text-ink-primary' : ''
                  }`}
                />
              </button>

              {isEngineOpen && (
                <div className="absolute right-0 sm:left-0 top-full mt-1.5 z-40 w-48 max-h-64 overflow-y-auto rounded-[var(--radius-control)] bg-surface-800 border border-line shadow-2xl py-1 text-xs font-sans divide-y divide-line/30">
                  {ENGINES.map(eng => (
                    <button
                      key={eng}
                      type="button"
                      onClick={() => {
                        setSelectedEngine(eng);
                        setIsEngineOpen(false);
                      }}
                      className={`w-full flex items-center justify-between px-3 py-2 text-left transition-colors cursor-pointer ${
                        selectedEngine === eng
                          ? 'bg-surface-700 text-ink-primary font-semibold'
                          : 'text-ink-secondary hover:text-ink-primary hover:bg-surface-750'
                      }`}
                    >
                      <span className="truncate">{eng}</span>
                      {selectedEngine === eng && <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />}
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* 3. Sort Order Dropdown */}
            <div className="relative" ref={sortRef}>
              <button
                type="button"
                onClick={() => {
                  setIsSortOpen(prev => !prev);
                  setIsCategoryOpen(false);
                  setIsEngineOpen(false);
                }}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-750 border border-line text-xs font-sans text-ink-primary transition-all cursor-pointer shadow-xs"
              >
                <ArrowUpDown className="w-3.5 h-3.5 text-accent-text" />
                <span className="font-medium">{sortLabel()}</span>
                <ChevronDown
                  className={`w-3.5 h-3.5 text-ink-muted transition-transform duration-150 ${
                    isSortOpen ? 'rotate-180 text-ink-primary' : ''
                  }`}
                />
              </button>

              {isSortOpen && (
                <div className="absolute right-0 top-full mt-1.5 z-40 w-48 rounded-[var(--radius-control)] bg-surface-800 border border-line shadow-2xl py-1 text-xs font-sans divide-y divide-line/30">
                  <button
                    type="button"
                    onClick={() => {
                      setSortBy('popular');
                      setIsSortOpen(false);
                    }}
                    className={`w-full flex items-center justify-between px-3 py-2 text-left transition-colors cursor-pointer ${
                      sortBy === 'popular'
                        ? 'bg-surface-700 text-ink-primary font-semibold'
                        : 'text-ink-secondary hover:text-ink-primary hover:bg-surface-750'
                    }`}
                  >
                    <span>Most Starred</span>
                    {sortBy === 'popular' && <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />}
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setSortBy('forks');
                      setIsSortOpen(false);
                    }}
                    className={`w-full flex items-center justify-between px-3 py-2 text-left transition-colors cursor-pointer ${
                      sortBy === 'forks'
                        ? 'bg-surface-700 text-ink-primary font-semibold'
                        : 'text-ink-secondary hover:text-ink-primary hover:bg-surface-750'
                    }`}
                  >
                    <span>Most Forked</span>
                    {sortBy === 'forks' && <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />}
                  </button>
                  <button
                    type="button"
                    onClick={() => {
                      setSortBy('recent');
                      setIsSortOpen(false);
                    }}
                    className={`w-full flex items-center justify-between px-3 py-2 text-left transition-colors cursor-pointer ${
                      sortBy === 'recent'
                        ? 'bg-surface-700 text-ink-primary font-semibold'
                        : 'text-ink-secondary hover:text-ink-primary hover:bg-surface-750'
                    }`}
                  >
                    <span>Recently Created</span>
                    {sortBy === 'recent' && <Check className="w-3.5 h-3.5 text-accent-text shrink-0" />}
                  </button>
                </div>
              )}
            </div>
          </div>
        </div>

        {/* Blueprint Repositories Grid */}
        {loading ? (
          <div className="flex items-center justify-center p-16 text-ink-muted text-xs gap-2">
            <Loader2 className="w-5 h-5 animate-spin text-accent-text" />
            <span className="font-sans">Loading verified architectures...</span>
          </div>
        ) : repos.length === 0 ? (
          <div className="text-center py-16 px-4 space-y-3 bg-surface-850 rounded-[var(--radius-card)] border border-line shadow-xs">
            <Database className="w-10 h-10 text-ink-muted mx-auto" />
            <h3 className="text-sm font-semibold text-ink-primary font-sans">No public schemas found</h3>
            <p className="text-xs text-ink-muted max-w-sm mx-auto font-sans">
              No architectures currently match your filter criteria or quality gate standards (minimum 4 tables, 2 relations, and 0 linter errors).
            </p>
            <div className="pt-2">
              <Link
                href="/canvas"
                className="inline-flex items-center gap-1.5 px-4 py-2 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-650 text-ink-primary border border-line-strong shadow-xs transition-all"
              >
                <span>Design the first one</span>
              </Link>
            </div>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
            {repos.map(r => (
              <Link
                key={r.id}
                href={`/hub/${encodeURIComponent(r.owner.username)}/${encodeURIComponent(
                  r.slug || r.id
                )}`}
                className="group flex flex-col justify-between p-5 rounded-[var(--radius-card)] bg-surface-850 border border-line hover:border-line-strong hover:bg-surface-800 transition-all shadow-xs"
              >
                <div className="space-y-2.5">
                  <div className="flex items-start justify-between gap-2">
                    <span className="font-mono text-xs text-accent-text group-hover:underline truncate">
                      {r.owner.username}/{r.slug || r.name}
                    </span>
                    <span className="text-[10px] font-mono px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800 text-ink-secondary border border-line shrink-0">
                      {r.dbType || 'PostgreSQL'}
                    </span>
                  </div>

                  <h3 className="text-sm font-semibold text-ink-primary font-sans group-hover:text-ink-primary">
                    {r.name}
                  </h3>

                  <p className="text-xs text-ink-secondary line-clamp-2 leading-relaxed font-sans">
                    {r.description || 'No description provided.'}
                  </p>
                </div>

                <div className="pt-4 border-t border-line/60 mt-4 flex items-center justify-between text-xs text-ink-muted font-mono">
                  <div className="flex items-center gap-3">
                    <span className="flex items-center gap-1" title="Tables">
                      <TableIcon className="w-3.5 h-3.5" />
                      {r.stats?.tableCount ?? 0}
                    </span>
                    <span className="flex items-center gap-1" title="Stars">
                      <Star className="w-3.5 h-3.5" />
                      {r.starCount}
                    </span>
                    <span className="flex items-center gap-1" title="Forks">
                      <GitFork className="w-3.5 h-3.5" />
                      {r.forkCount}
                    </span>
                  </div>

                  <span className="inline-flex items-center gap-1 text-[11px] font-medium text-ink-secondary">
                    <ShieldCheck className="w-3.5 h-3.5 text-accent-text" />
                    <span>{r.dbType}</span>
                  </span>
                </div>
              </Link>
            ))}
          </div>
        )}
      </main>
    </div>
  );
}