'use client';

import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import {
  PostgresIcon,
  MySQLIcon,
  SqliteIcon,
  SqlServerIcon,
  OracleIcon,
  MariaDbIcon,
  DockerIcon,
  PrismaIcon,
  EfCoreIcon,
  DrizzleIcon,
  TypeScriptIcon,
  PythonIcon,
  GoIcon,
  RustIcon,
  NextJsIcon,
  GraphQLIcon,
  OpenApiIcon,
  RedisIcon,
} from './TechIcons';

interface TechItem {
  name: string;
  icon: typeof PostgresIcon;
}

const ROW_1: TechItem[] = [
  { name: 'PostgreSQL', icon: PostgresIcon },
  { name: 'Docker', icon: DockerIcon },
  { name: 'Prisma', icon: PrismaIcon },
  { name: 'TypeScript', icon: TypeScriptIcon },
  { name: 'Python', icon: PythonIcon },
  { name: 'Next.js', icon: NextJsIcon },
  { name: 'MySQL', icon: MySQLIcon },
  { name: 'Go', icon: GoIcon },
  { name: 'Redis', icon: RedisIcon },
];

const ROW_2: TechItem[] = [
  { name: 'SQLite', icon: SqliteIcon },
  { name: 'SQL Server', icon: SqlServerIcon },
  { name: 'Drizzle', icon: DrizzleIcon },
  { name: 'EF Core', icon: EfCoreIcon },
  { name: 'Rust', icon: RustIcon },
  { name: 'Oracle', icon: OracleIcon },
  { name: 'MariaDB', icon: MariaDbIcon },
  { name: 'GraphQL', icon: GraphQLIcon },
  { name: 'OpenAPI', icon: OpenApiIcon },
];

export default function StackRibbon() {
  return (
    <section
      id="engines"
      className="w-full border-y border-surface-500/80 py-12 sm:py-16 overflow-hidden relative bg-surface-900"
      style={{
        backgroundImage: 'var(--namines-teal-ribbon)',
      }}
    >
      <style>{`
        @keyframes ribbonScrollLeft {
          0% { transform: translateX(0); }
          100% { transform: translateX(-50%); }
        }
        @keyframes ribbonScrollRight {
          0% { transform: translateX(-50%); }
          100% { transform: translateX(0); }
        }
      `}</style>

      <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 flex flex-col lg:flex-row items-center justify-between gap-8 lg:gap-12">
        {/* Left Headline & CTA (Render.com Style) */}
        <div className="flex flex-col items-start max-w-md shrink-0 z-10">
          <h2 className="text-2xl sm:text-3xl lg:text-4xl font-bold tracking-tight text-content-primary mb-6 leading-snug">
            Whatever your stack, it compiles with Namines.
          </h2>
          <Link
            href="/demo"
            className="inline-flex items-center gap-2 px-5 py-2.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on font-bold text-xs uppercase tracking-wider transition-all shadow-lg hover:scale-105 cursor-pointer"
          >
            <span>View Blueprints</span>
            <ArrowRight className="w-3.5 h-3.5" />
          </Link>
        </div>

        {/* Right Animated Marquee Tiles (2 Endless Rows, Render.com Style) */}
        <div
          className="relative flex-1 w-full overflow-hidden py-2"
          style={{
            maskImage: 'linear-gradient(to right, transparent 0%, black 12%, black 100%)',
            WebkitMaskImage: 'linear-gradient(to right, transparent 0%, black 12%, black 100%)',
          }}
        >
          {/* Row 1 — Gliding Continuously Left */}
          <div
            className="flex gap-3 mb-3 w-max hover:[animation-play-state:paused]"
            style={{
              animation: 'ribbonScrollLeft 26s linear infinite',
            }}
          >
            {[...ROW_1, ...ROW_1, ...ROW_1].map((item, idx) => (
              <div
                key={`r1-${item.name}-${idx}`}
                className="w-14 h-14 sm:w-16 sm:h-16 shrink-0 rounded-[var(--radius-card)] bg-[color-mix(in_oklch,var(--accent)_35%,var(--surface-800))] border border-[color-mix(in_oklch,var(--accent)_55%,var(--surface-600))] flex items-center justify-center shadow-md transition-transform duration-300 hover:scale-110 hover:border-accent cursor-pointer group"
                title={item.name}
              >
                <item.icon className="w-7 h-7 sm:w-8 sm:h-8 text-content-primary transition-transform group-hover:scale-110" />
              </div>
            ))}
          </div>

          {/* Row 2 — Gliding Continuously Right */}
          <div
            className="flex gap-3 w-max hover:[animation-play-state:paused]"
            style={{
              animation: 'ribbonScrollRight 30s linear infinite',
            }}
          >
            {[...ROW_2, ...ROW_2, ...ROW_2].map((item, idx) => (
              <div
                key={`r2-${item.name}-${idx}`}
                className="w-14 h-14 sm:w-16 sm:h-16 shrink-0 rounded-[var(--radius-card)] bg-[color-mix(in_oklch,var(--accent)_35%,var(--surface-800))] border border-[color-mix(in_oklch,var(--accent)_55%,var(--surface-600))] flex items-center justify-center shadow-md transition-transform duration-300 hover:scale-110 hover:border-accent cursor-pointer group"
                title={item.name}
              >
                <item.icon className="w-7 h-7 sm:w-8 sm:h-8 text-content-primary transition-transform group-hover:scale-110" />
              </div>
            ))}
          </div>
        </div>
      </div>
    </section>
  );
}
