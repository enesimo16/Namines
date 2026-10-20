'use client';

import { useState } from 'react';
import {
  PostgresIcon,
  MySQLIcon,
  SqlServerIcon,
  OracleIcon,
  MariaDbIcon,
  SqliteIcon,
  PrismaIcon,
  EfCoreIcon,
  DrizzleIcon,
  DockerIcon,
  TypeScriptIcon,
  DeskIcon,
} from './TechIcons';

interface EngineItem {
  name: string;
  category: string;
  feature: string;
  icon: typeof PostgresIcon;
}

const ENGINES_ROW_1: EngineItem[] = [
  { name: 'PostgreSQL', category: 'v12 - v17', feature: 'pgvector & JSONB', icon: PostgresIcon },
  { name: 'MySQL', category: 'v8.0 - v8.4 LTS', feature: 'InnoDB & Generated Cols', icon: MySQLIcon },
  { name: 'SQL Server', category: '2019 - 2022', feature: 'T-SQL & Temporal', icon: SqlServerIcon },
  { name: 'Oracle DB', category: '19c - 23ai', feature: 'SYS_GUID & JSON Dual', icon: OracleIcon },
  { name: 'MariaDB', category: 'v10.5+', feature: 'Temporal & Check Constr.', icon: MariaDbIcon },
  { name: 'SQLite', category: 'v3.35+', feature: 'Strict Mode & Fast In-Mem', icon: SqliteIcon },
];

const ENGINES_ROW_2: EngineItem[] = [
  { name: 'Prisma ORM', category: 'Schema & Client', feature: 'AST Drift Sync', icon: PrismaIcon },
  { name: 'EF Core 8/9', category: 'C# DbContext', feature: 'ModelSnapshot Gen', icon: EfCoreIcon },
  { name: 'Drizzle ORM', category: 'TypeScript', feature: 'Zero-Overhead Queries', icon: DrizzleIcon },
  { name: 'Docker Sandbox', category: 'Ephemeral DB', feature: 'Zero-Risk DDL Validation', icon: DockerIcon },
  { name: 'TypeScript SDK', category: 'Client Gen', feature: 'Strict Zod Schemas', icon: TypeScriptIcon },
  { name: 'Namines Desk', category: 'Hosted Admin', feature: 'Zero-Code CRUD Console', icon: DeskIcon },
];

export default function EngineLogosStrip() {
  const [activeMobileCategory, setActiveMobileCategory] = useState<'engines' | 'frameworks'>('engines');

  return (
    <section id="engines" className="w-full border-y border-surface-500 bg-surface-900/40 py-10 sm:py-14">
      <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 text-center">
        {/* Monospace Header */}
        <h2 className="font-mono text-xs uppercase tracking-[0.25em] text-content-subtle font-semibold mb-6 sm:mb-8">
          SUPPORTING 6 PRODUCTION ENGINES &amp; MODERN FRAMEWORKS
        </h2>

        {/* Mobile & Tablet Toggle Switcher (Eliminates visual clutter & endless card stacking) */}
        <div className="lg:hidden flex items-center justify-center gap-2 mb-6">
          <button
            type="button"
            onClick={() => setActiveMobileCategory('engines')}
            className={`tap-44 px-3.5 py-1.5 rounded-[var(--radius-control)] text-xs font-mono font-bold transition-all cursor-pointer ${
              activeMobileCategory === 'engines'
                ? 'bg-accent text-accent-on shadow-sm'
                : 'bg-surface-800 text-content-muted hover:text-content-primary border border-surface-600/60'
            }`}
          >
            6 SQL Engines
          </button>
          <button
            type="button"
            onClick={() => setActiveMobileCategory('frameworks')}
            className={`tap-44 px-3.5 py-1.5 rounded-[var(--radius-control)] text-xs font-mono font-bold transition-all cursor-pointer ${
              activeMobileCategory === 'frameworks'
                ? 'bg-accent text-accent-on shadow-sm'
                : 'bg-surface-800 text-content-muted hover:text-content-primary border border-surface-600/60'
            }`}
          >
            6 Frameworks &amp; Tools
          </button>
        </div>

        {/* Mobile & Tablet View (Compact 6-Card Grid of the Selected Category) */}
        <div className="lg:hidden grid grid-cols-2 sm:grid-cols-3 gap-2.5 sm:gap-3">
          {(activeMobileCategory === 'engines' ? ENGINES_ROW_1 : ENGINES_ROW_2).map((item) => (
            <div
              key={item.name}
              className="flex flex-col items-center justify-center p-3 rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/50 shadow-sm"
            >
              <item.icon className="w-5 h-5 text-accent-text mb-1.5" />
              <span className="text-xs font-semibold text-content-primary">
                {item.name}
              </span>
              <span className="text-[10px] font-mono text-content-subtle mt-0.5">
                {item.category}
              </span>
              <span className="text-[10px] font-mono text-accent-text mt-1 opacity-90 truncate max-w-full">
                {item.feature}
              </span>
            </div>
          ))}
        </div>

        {/* Desktop View (Full 12-Card Grid across 2 Distinct Rows) */}
        <div className="hidden lg:block space-y-3">
          {/* Row 1 — Engines */}
          <div className="grid grid-cols-6 gap-4">
            {ENGINES_ROW_1.map((item) => (
              <div
                key={item.name}
                className="flex flex-col items-center justify-center p-4 rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/50 hover:border-surface-400 transition-all group shadow-sm hover:shadow-md"
              >
                <item.icon className="w-5 h-5 text-accent-text group-hover:scale-110 transition-transform mb-2" />
                <span className="text-xs font-semibold text-content-secondary group-hover:text-content-primary transition-colors">
                  {item.name}
                </span>
                <span className="text-[10px] font-mono text-content-subtle mt-0.5">
                  {item.category}
                </span>
                <span className="text-[10px] font-mono text-accent-text mt-1 opacity-80 group-hover:opacity-100 transition-opacity truncate max-w-full">
                  {item.feature}
                </span>
              </div>
            ))}
          </div>

          {/* Row 2 — Ecosystem / ORM / Tooling */}
          <div className="grid grid-cols-6 gap-4">
            {ENGINES_ROW_2.map((item) => (
              <div
                key={item.name}
                className="flex flex-col items-center justify-center p-4 rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/50 hover:border-surface-400 transition-all group shadow-sm hover:shadow-md"
              >
                <item.icon className="w-5 h-5 text-accent-text group-hover:scale-110 transition-transform mb-2" />
                <span className="text-xs font-semibold text-content-secondary group-hover:text-content-primary transition-colors">
                  {item.name}
                </span>
                <span className="text-[10px] font-mono text-content-subtle mt-0.5">
                  {item.category}
                </span>
                <span className="text-[10px] font-mono text-accent-text mt-1 opacity-80 group-hover:opacity-100 transition-opacity truncate max-w-full">
                  {item.feature}
                </span>
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
