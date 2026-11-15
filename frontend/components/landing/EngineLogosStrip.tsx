'use client';

import { useState } from 'react';
import { Pause, Play } from 'lucide-react';
import { DeskIcon, NslIcon } from './TechIcons';

interface MarqueeItem {
  name: string;
  meta: string;
  iconSrc?: string;
  fallbackIcon?: 'desk' | 'nsl';
}

const ROW_TOP: MarqueeItem[] = [
  { name: 'PostgreSQL', meta: 'v12 – v17', iconSrc: '/icons/tech/postgresql.svg' },
  { name: 'MySQL', meta: 'v8.0 – 8.4', iconSrc: '/icons/tech/mysql.svg' },
  { name: 'SQL Server', meta: '2019 – 2022', iconSrc: '/icons/tech/sqlserver.svg' },
  { name: 'Oracle DB', meta: '19c – 23ai', iconSrc: '/icons/tech/oracle.svg' },
  { name: 'MariaDB', meta: 'v10.5+', iconSrc: '/icons/tech/mariadb.svg' },
  { name: 'SQLite', meta: 'v3.35+ Strict', iconSrc: '/icons/tech/sqlite.svg' },
  { name: 'Prisma ORM', meta: 'Schema & Client', iconSrc: '/icons/tech/prisma.svg' },
  { name: 'Drizzle ORM', meta: 'TypeScript', iconSrc: '/icons/tech/drizzle.svg' },
  { name: 'EF Core 8/9', meta: 'C# DbContext', iconSrc: '/icons/tech/efcore.svg' },
  { name: 'TypeScript SDK', meta: 'Client & Types', iconSrc: '/icons/tech/typescript.svg' },
  { name: 'Zod Schemas', meta: 'Validation', iconSrc: '/icons/tech/zod.svg' },
  { name: 'Next.js Console', meta: 'Live CRUD App', iconSrc: '/icons/tech/nextjs.svg' },
  { name: 'Docker Sandbox', meta: 'Ephemeral Verify', iconSrc: '/icons/tech/docker.svg' },
];

const ROW_BOTTOM: MarqueeItem[] = [
  { name: 'TypeORM', meta: 'Entities', iconSrc: '/icons/tech/typeorm.svg' },
  { name: 'SQLAlchemy', meta: 'Python 2.0', iconSrc: '/icons/tech/sqlalchemy.svg' },
  { name: 'Django ORM', meta: 'Models & Meta', iconSrc: '/icons/tech/django.svg' },
  { name: 'Sequelize', meta: 'Node.js ORM', iconSrc: '/icons/tech/sequelize.svg' },
  { name: 'GORM', meta: 'Go Structs', iconSrc: '/icons/tech/go.svg' },
  { name: 'Pydantic v2', meta: 'Python Models', iconSrc: '/icons/tech/pydantic.svg' },
  { name: 'GraphQL SDL', meta: 'Types & Inputs', iconSrc: '/icons/tech/graphql.svg' },
  { name: 'JSON Schema', meta: 'Draft 2020-12', iconSrc: '/icons/tech/jsonschema.svg' },
  { name: 'Protobuf', meta: 'proto3', iconSrc: '/icons/tech/protobuf.svg' },
  { name: 'Flyway', meta: 'Versioned SQL', iconSrc: '/icons/tech/flyway.svg' },
  { name: 'Liquibase', meta: 'Changelog', iconSrc: '/icons/tech/liquibase.svg' },
  { name: 'Canonical IR', meta: 'ir.json AST', iconSrc: '/icons/tech/openapi.svg' },
  { name: 'NSL Spec', meta: 'schema.nsl', fallbackIcon: 'nsl' },
  { name: 'Namines Desk', meta: 'Hosted Admin', fallbackIcon: 'desk' },
];

function MarqueeCard({ item }: { item: MarqueeItem }) {
  return (
    <div className="shrink-0 w-[148px] sm:w-[164px] h-[132px] sm:h-[144px] flex flex-col items-center justify-center text-center gap-3 p-4 rounded-[var(--radius-card)] border border-surface-500/80 bg-surface-850/90 hover:border-surface-400 transition-colors">
      <div className="w-11 h-11 rounded-[var(--radius-control)] border border-surface-500/70 bg-surface-900 flex items-center justify-center">
        {item.iconSrc ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={item.iconSrc}
            alt=""
            aria-hidden="true"
            className="w-7 h-7 object-contain"
          />
        ) : item.fallbackIcon === 'desk' ? (
          <DeskIcon className="w-7 h-7 text-accent-text" />
        ) : (
          <NslIcon className="w-7 h-7 text-accent-text" />
        )}
      </div>

      <div className="min-w-0 w-full">
        <div className="font-mono text-xs sm:text-sm font-semibold text-content-primary truncate">
          {item.name}
        </div>
        <div className="font-mono text-xs text-content-subtle truncate mt-0.5">
          {item.meta}
        </div>
      </div>
    </div>
  );
}

export default function EngineLogosStrip() {
  const [paused, setPaused] = useState(false);

  return (
    <section
      id="engines"
      aria-label="Supported SQL dialects and eject targets"
      className="relative w-full border-y border-surface-500/80 bg-surface-900/60 py-10 lg:py-12 overflow-hidden"
    >
      <div className="w-full max-w-[1400px] mx-auto px-4 sm:px-6 lg:px-8 mb-4 flex justify-end">
        <button
          type="button"
          onClick={() => setPaused((prev) => !prev)}
          aria-pressed={paused}
          aria-label={paused ? 'Resume technology marquee' : 'Pause technology marquee'}
          className="tap-44 inline-flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] border border-surface-500 bg-surface-850 hover:bg-surface-800 hover:border-surface-400 font-mono text-xs text-content-secondary hover:text-content-primary transition-colors cursor-pointer focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-focus-ring"
        >
          {paused ? (
            <>
              <Play className="w-3.5 h-3.5" />
              <span>Resume</span>
            </>
          ) : (
            <>
              <Pause className="w-3.5 h-3.5" />
              <span>Pause</span>
            </>
          )}
        </button>
      </div>

      <div
        className={`namines-marquee-track space-y-4 ${paused ? 'namines-marquee-paused' : ''}`}
      >
        <div className="overflow-hidden">
          <div className="namines-marquee-left gap-4 pr-4">
            {[...ROW_TOP, ...ROW_TOP].map((item, idx) => (
              <MarqueeCard key={`top-${item.name}-${idx}`} item={item} />
            ))}
          </div>
        </div>

        <div className="overflow-hidden">
          <div className="namines-marquee-right gap-4 pr-4">
            {[...ROW_BOTTOM, ...ROW_BOTTOM].map((item, idx) => (
              <MarqueeCard key={`bot-${item.name}-${idx}`} item={item} />
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
