'use client';

import Link from 'next/link';
import { ArrowRight } from 'lucide-react';
import {
  PostgresIcon,
  MySQLIcon,
  SqliteIcon,
  SqlServerIcon,
  DockerIcon,
  PythonIcon,
  GoIcon,
  RustIcon,
  NextJsIcon,
  TypeScriptIcon,
  PrismaIcon,
  EfCoreIcon,
  DrizzleIcon,
  GraphQLIcon,
  RedisIcon,
} from './TechIcons';

interface OrbitLogo {
  icon: typeof PostgresIcon;
  name: string;
  className: string;
  animIndex: number;
}

const ORBIT_LOGOS: OrbitLogo[] = [
  // Far left outer column
  { icon: TypeScriptIcon, name: 'TypeScript', className: 'top-8 left-2 sm:top-10 sm:left-8', animIndex: 0 },
  { icon: DockerIcon, name: 'Docker', className: 'top-1/2 -translate-y-12 left-1 sm:left-4', animIndex: 2 },
  { icon: MySQLIcon, name: 'MySQL', className: 'bottom-8 left-3 sm:bottom-12 sm:left-10', animIndex: 1 },

  // Mid left scatter
  { icon: NextJsIcon, name: 'Next.js', className: 'top-20 left-16 sm:top-16 sm:left-36', animIndex: 1 },
  { icon: EfCoreIcon, name: 'EF Core', className: 'bottom-24 left-20 sm:bottom-28 sm:left-40', animIndex: 0 },
  { icon: PythonIcon, name: 'Python', className: 'top-4 left-1/4 sm:top-6 sm:left-[24%]', animIndex: 3 },
  { icon: SqliteIcon, name: 'SQLite', className: 'bottom-4 left-1/3 sm:bottom-6 sm:left-[26%]', animIndex: 2 },

  // Center bottom
  { icon: SqlServerIcon, name: 'SQL Server', className: 'bottom-1 left-1/2 -translate-x-1/2 translate-y-3', animIndex: 3 },

  // Mid right scatter
  { icon: GoIcon, name: 'Go', className: 'top-4 right-1/4 sm:top-6 sm:right-[24%]', animIndex: 0 },
  { icon: DrizzleIcon, name: 'Drizzle', className: 'bottom-4 right-1/3 sm:bottom-6 sm:right-[26%]', animIndex: 1 },
  { icon: RustIcon, name: 'Rust', className: 'top-20 right-16 sm:top-16 sm:right-36', animIndex: 1 },
  { icon: RedisIcon, name: 'Redis', className: 'bottom-24 right-20 sm:bottom-28 sm:right-40', animIndex: 2 },

  // Far right outer column
  { icon: PostgresIcon, name: 'PostgreSQL', className: 'top-8 right-2 sm:top-10 sm:right-8', animIndex: 2 },
  { icon: PrismaIcon, name: 'Prisma', className: 'top-1/2 -translate-y-12 right-1 sm:right-4', animIndex: 3 },
  { icon: GraphQLIcon, name: 'GraphQL', className: 'bottom-8 right-3 sm:bottom-12 sm:right-10', animIndex: 0 },
];

export default function OrbitingClosingCTA() {
  return (
    <section className="relative z-10 w-full py-28 sm:py-44 overflow-hidden">
      {/* Top soft atmospheric fade from preceding section */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 top-0 h-32 sm:h-44 bg-gradient-to-b from-surface-900 via-surface-900/80 to-transparent z-20"
      />

      {/* Bottom soft atmospheric fade into footer */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 bottom-0 h-32 sm:h-44 bg-gradient-to-t from-surface-900 via-surface-900/80 to-transparent z-20"
      />

      {/* Inline styles for continuous floating animations */}
      <style>{`
        @keyframes orbitDrift0 {
          0% { transform: translate(0px, 0px); }
          50% { transform: translate(14px, -18px); }
          100% { transform: translate(-10px, 12px); }
        }
        @keyframes orbitDrift1 {
          0% { transform: translate(0px, 0px); }
          50% { transform: translate(-16px, 15px); }
          100% { transform: translate(12px, -12px); }
        }
        @keyframes orbitDrift2 {
          0% { transform: translate(0px, 0px); }
          50% { transform: translate(12px, 16px); }
          100% { transform: translate(-14px, -10px); }
        }
        @keyframes orbitDrift3 {
          0% { transform: translate(0px, 0px); }
          50% { transform: translate(-15px, -14px); }
          100% { transform: translate(10px, 14px); }
        }
      `}</style>

      {/* Background Soft Glow */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 flex items-center justify-center opacity-25"
      >
        <div
          className="w-[700px] h-[700px] rounded-full blur-[180px]"
          style={{ backgroundColor: 'var(--namines-teal-glow)' }}
        />
      </div>

      <div className="relative w-full max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        {/* Widely Dispersed Tech Badges (Continuous Floating Animation) */}
        <div className="absolute inset-0 pointer-events-none hidden md:block" aria-hidden="true">
          {ORBIT_LOGOS.map((item, idx) => {
            const duration = 3.6 + (idx % 4) * 0.7;
            const delay = (idx * 0.3) % 2.2;
            const animName = `orbitDrift${item.animIndex}`;

            return (
              <div
                key={item.name}
                className={`absolute ${item.className} w-11 h-11 sm:w-13 sm:h-13`}
              >
                <div
                  className="w-full h-full rounded-[var(--radius-card)] bg-surface-800/80 border border-surface-500/50 shadow-lg flex items-center justify-center backdrop-blur-md transition-transform duration-300 hover:scale-120"
                  style={{
                    animation: `${animName} ${duration}s ease-in-out ${delay}s infinite alternate`,
                  }}
                  title={item.name}
                >
                  <item.icon className="w-5 h-5 sm:w-5.5 sm:h-5.5 text-accent-text" />
                </div>
              </div>
            );
          })}
        </div>

        {/* Soft, Free-Floating CTA Typography (No boxed card div) */}
        <div className="relative z-10 max-w-3xl mx-auto py-12 sm:py-20 flex flex-col items-center text-center gap-5">
          <h2 className="text-4xl sm:text-5xl lg:text-6xl font-extrabold tracking-tight text-content-primary">
            Start building with Namines
          </h2>

          <p className="text-sm sm:text-base text-content-muted max-w-md leading-relaxed">
            Zero drift, zero downtime, zero guesswork.
          </p>

          <div className="mt-2">
            <Link
              href="/new"
              className="inline-flex items-center justify-center gap-2 bg-accent hover:bg-accent-hover text-accent-on font-bold py-3.5 px-8 rounded-[var(--radius-control)] transition-all text-xs uppercase tracking-wider shadow-xl shadow-accent/20 hover:scale-105 active:scale-95"
            >
              <span>Deploy your schema for free</span>
              <ArrowRight className="w-4 h-4" />
            </Link>
          </div>
        </div>
      </div>
    </section>
  );
}
