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
  ZodIcon,
  PydanticIcon,
  JsonSchemaIcon,
  TypeOrmIcon,
  SqlAlchemyIcon,
  DjangoIcon,
  SequelizeIcon,
  FlywayIcon,
  LiquibaseIcon,
  ProtobufIcon,
  OpenApiIcon,
} from './TechIcons';

interface OrbitLogo {
  icon: typeof PostgresIcon;
  name: string;
  className: string;
  animIndex: number;
}

const ORBIT_LOGOS: OrbitLogo[] = [
  // Far left outer column
  { icon: TypeScriptIcon, name: 'TypeScript', className: 'top-6 left-2 sm:left-6', animIndex: 0 },
  { icon: DockerIcon, name: 'Docker', className: 'top-[28%] left-3 sm:left-10', animIndex: 2 },
  { icon: MySQLIcon, name: 'MySQL', className: 'top-[54%] left-1 sm:left-5', animIndex: 1 },
  { icon: OracleIcon, name: 'Oracle', className: 'bottom-16 left-4 sm:left-12', animIndex: 3 },
  { icon: MariaDbIcon, name: 'MariaDB', className: 'bottom-4 left-2 sm:left-6', animIndex: 0 },

  // Mid-left scatter
  { icon: NextJsIcon, name: 'Next.js', className: 'top-14 left-20 sm:left-32', animIndex: 1 },
  { icon: EfCoreIcon, name: 'EF Core', className: 'top-[40%] left-16 sm:left-28', animIndex: 3 },
  { icon: ZodIcon, name: 'Zod', className: 'bottom-28 left-20 sm:left-36', animIndex: 0 },
  { icon: PythonIcon, name: 'Python', className: 'top-4 left-[20%]', animIndex: 3 },
  { icon: SqlAlchemyIcon, name: 'SQLAlchemy', className: 'top-[26%] left-[21%]', animIndex: 1 },
  { icon: DjangoIcon, name: 'Django', className: 'bottom-20 left-[22%]', animIndex: 2 },
  { icon: SqliteIcon, name: 'SQLite', className: 'bottom-4 left-[26%]', animIndex: 2 },
  { icon: FlywayIcon, name: 'Flyway', className: 'top-10 left-[32%]', animIndex: 0 },

  // Center bottom
  { icon: SqlServerIcon, name: 'SQL Server', className: 'bottom-2 left-[42%]', animIndex: 3 },
  { icon: OpenApiIcon, name: 'OpenAPI', className: 'bottom-2 right-[42%]', animIndex: 1 },

  // Mid-right scatter
  { icon: LiquibaseIcon, name: 'Liquibase', className: 'top-10 right-[32%]', animIndex: 2 },
  { icon: GoIcon, name: 'Go', className: 'top-4 right-[20%]', animIndex: 0 },
  { icon: DrizzleIcon, name: 'Drizzle', className: 'bottom-4 right-[26%]', animIndex: 1 },
  { icon: ProtobufIcon, name: 'Protobuf', className: 'top-[26%] right-[21%]', animIndex: 3 },
  { icon: SequelizeIcon, name: 'Sequelize', className: 'bottom-20 right-[22%]', animIndex: 0 },
  { icon: RustIcon, name: 'Rust', className: 'top-14 right-20 sm:right-32', animIndex: 1 },
  { icon: TypeOrmIcon, name: 'TypeORM', className: 'top-[40%] right-16 sm:right-28', animIndex: 2 },
  { icon: RedisIcon, name: 'Redis', className: 'bottom-28 right-20 sm:right-36', animIndex: 2 },

  // Far right outer column
  { icon: PostgresIcon, name: 'PostgreSQL', className: 'top-6 right-2 sm:right-6', animIndex: 2 },
  { icon: PydanticIcon, name: 'Pydantic', className: 'top-[28%] right-3 sm:right-10', animIndex: 0 },
  { icon: PrismaIcon, name: 'Prisma', className: 'top-[54%] right-1 sm:right-5', animIndex: 3 },
  { icon: JsonSchemaIcon, name: 'JSON Schema', className: 'bottom-16 right-4 sm:right-12', animIndex: 1 },
  { icon: GraphQLIcon, name: 'GraphQL', className: 'bottom-4 right-2 sm:right-6', animIndex: 0 },
];

export default function OrbitingClosingCTA() {
  return (
    <section className="relative z-10 w-full py-16 sm:py-32 overflow-hidden">
      {/* Top soft atmospheric fade from preceding section */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 top-0 h-24 sm:h-36 bg-gradient-to-b from-surface-900 via-surface-900/80 to-transparent z-20"
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
        {/* Widely Dispersed Bare Tech Icons (Continuous Floating Animation on wide desktops) */}
        <div className="absolute inset-0 pointer-events-none hidden xl:block" aria-hidden="true">
          {ORBIT_LOGOS.map((item, idx) => {
            const duration = 3.6 + (idx % 4) * 0.7;
            const delay = (idx * 0.3) % 2.2;
            const animName = `orbitDrift${item.animIndex}`;

            return (
              <div
                key={item.name}
                className={`absolute ${item.className} flex items-center justify-center`}
                style={{
                  animation: `${animName} ${duration}s ease-in-out ${delay}s infinite alternate`,
                }}
                title={item.name}
              >
                <item.icon className="w-7 h-7 sm:w-8 sm:h-8 text-content-muted/75 opacity-85 transition-transform duration-300 hover:scale-125" />
              </div>
            );
          })}
        </div>

        {/* Soft, Free-Floating CTA Typography */}
        <div className="relative z-10 max-w-3xl mx-auto py-8 sm:py-16 flex flex-col items-center text-center gap-5">
          {/* Large Background-Free Namines U3-B Logo */}
          <div className="relative mb-2 flex items-center justify-center">
            <div
              aria-hidden="true"
              className="absolute inset-0 rounded-full blur-3xl opacity-55 animate-pulse"
              style={{ backgroundColor: 'var(--namines-teal-glow)' }}
            />
            <img
              src="/icons/namines.svg"
              alt="Namines"
              className="relative z-10 w-20 h-20 sm:w-24 sm:h-24 drop-shadow-2xl select-none"
            />
          </div>

          <h2 className="text-3xl sm:text-4xl lg:text-5xl font-extrabold tracking-tight text-content-primary">
            Start building with Namines
          </h2>

          <p className="text-xs sm:text-sm md:text-base text-content-muted max-w-md leading-relaxed">
            Zero drift, zero downtime, zero guesswork.
          </p>

          <div className="mt-2 w-full sm:w-auto">
            <Link
              href="/new"
              className="w-full sm:w-auto inline-flex items-center justify-center gap-2 bg-accent hover:bg-accent-hover text-accent-on font-bold py-3.5 px-8 rounded-[var(--radius-control)] transition-all text-xs uppercase tracking-wider shadow-xl shadow-accent/20 hover:scale-105 active:scale-95"
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
