'use client';

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
  icon: typeof PostgresIcon;
}

const ENGINES_ROW_1: EngineItem[] = [
  { name: 'PostgreSQL', category: 'v12 - v17', icon: PostgresIcon },
  { name: 'MySQL', category: 'v8.0+', icon: MySQLIcon },
  { name: 'SQL Server', category: '2019 - 2022', icon: SqlServerIcon },
  { name: 'Oracle DB', category: '19c - 23ai', icon: OracleIcon },
  { name: 'MariaDB', category: 'v10.5+', icon: MariaDbIcon },
  { name: 'SQLite', category: 'v3.35+', icon: SqliteIcon },
];

const ENGINES_ROW_2: EngineItem[] = [
  { name: 'Prisma ORM', category: 'Schema & Client', icon: PrismaIcon },
  { name: 'EF Core 8/9', category: 'C# DbContext', icon: EfCoreIcon },
  { name: 'Drizzle ORM', category: 'TypeScript', icon: DrizzleIcon },
  { name: 'Docker Sandbox', category: 'Ephemeral DB', icon: DockerIcon },
  { name: 'TypeScript SDK', category: 'Client Gen', icon: TypeScriptIcon },
  { name: 'Namines Desk', category: 'Hosted Admin', icon: DeskIcon },
];

export default function EngineLogosStrip() {
  return (
    <section id="engines" className="w-full border-y border-surface-500 bg-surface-900/40 py-12 sm:py-16">
      <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 text-center">
        {/* Monospace Header */}
        {/* h2: bu bir BOLUM basligi ve sayfadaki tek h1'in hemen altinda.
            Onceden h3 idi ve h1 -> h3 atlamasi uretiyordu; ekran okuyucu
            baslik listesinde araya bir seviye eksik goruluyordu (WCAG 1.3.1).
            Gorsel boyut sinifla belirleniyor, etiket degisimi gorunumu
            ETKILEMIYOR. */}
        <h2 className="font-mono text-xs uppercase tracking-[0.25em] text-content-subtle font-semibold mb-8 sm:mb-10">
          SUPPORTING 6 PRODUCTION ENGINES &amp; MODERN FRAMEWORKS
        </h2>

        {/* Row 1 — Engines */}
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3 sm:gap-4 mb-3 sm:mb-4">
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
            </div>
          ))}
        </div>

        {/* Row 2 — Ecosystem / ORM / Tooling */}
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-6 gap-3 sm:gap-4">
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
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
