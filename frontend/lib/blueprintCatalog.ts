import type { ComponentType, SVGProps } from 'react';
import { TEMPLATES, type SchemaTemplate, type TemplateSize } from './templates';
import {
  PostgresIcon,
  MySQLIcon,
  SqlServerIcon,
  SqliteIcon,
  DockerIcon,
  PrismaIcon,
  EfCoreIcon,
  DrizzleIcon,
  TypeScriptIcon,
} from '../components/landing/TechIcons';

export type BannerTheme = 'cyan' | 'dark' | 'amber' | 'mint' | 'ice';

export interface BlueprintMeta {
  key: string;
  slug: string;
  bannerTheme: BannerTheme;
  stack: string[];
  tags: string[];
  icon: ComponentType<SVGProps<SVGSVGElement>>;
}

export interface EnrichedBlueprint extends SchemaTemplate {
  slug: string;
  bannerTheme: BannerTheme;
  stack: string[];
  tags: string[];
  icon: ComponentType<SVGProps<SVGSVGElement>>;
  tableCount: number;
  relationCount: number;
}

export const BLUEPRINT_METAS: Record<string, Omit<BlueprintMeta, 'key'>> = {
  ecommerce: {
    slug: 'ecommerce store',
    bannerTheme: 'amber',
    stack: ['PostgreSQL', 'MySQL', 'Prisma', 'Docker'],
    tags: ['E-Commerce', 'Retail', 'Payments'],
    icon: PostgresIcon,
  },
  saas: {
    slug: 'saas billing',
    bannerTheme: 'cyan',
    stack: ['PostgreSQL', 'SQLite', 'Drizzle', 'TypeScript'],
    tags: ['SaaS', 'Multi-tenant', 'Billing'],
    icon: DrizzleIcon,
  },
  cms: {
    slug: 'headless cms',
    bannerTheme: 'dark',
    stack: ['PostgreSQL', 'MySQL', 'Docker'],
    tags: ['CMS', 'Publishing', 'Content'],
    icon: DockerIcon,
  },
  crm: {
    slug: 'crm pipeline',
    bannerTheme: 'ice',
    stack: ['PostgreSQL', 'MSSQL', 'Docker'],
    tags: ['CRM', 'Sales', 'Enterprise'],
    icon: SqlServerIcon,
  },
  healthcare: {
    slug: 'health telemetry',
    bannerTheme: 'mint',
    stack: ['PostgreSQL', 'MSSQL', 'Docker'],
    tags: ['Healthcare', 'Clinical', 'Compliance'],
    icon: PostgresIcon,
  },
  lms: {
    slug: 'learning portal',
    bannerTheme: 'cyan',
    stack: ['PostgreSQL', 'MySQL', 'TypeScript'],
    tags: ['Education', 'Courses', 'Certifications'],
    icon: TypeScriptIcon,
  },
  banking: {
    slug: 'fintech ledger',
    bannerTheme: 'dark',
    stack: ['PostgreSQL', 'MSSQL', 'Docker', 'EF Core'],
    tags: ['Fintech', 'Ledger', 'Compliance'],
    icon: EfCoreIcon,
  },
  logistics: {
    slug: 'fleet tracking',
    bannerTheme: 'amber',
    stack: ['PostgreSQL', 'MySQL', 'Docker'],
    tags: ['Logistics', 'Fleet', 'Supply Chain'],
    icon: DockerIcon,
  },
  hr: {
    slug: 'hr workforce',
    bannerTheme: 'ice',
    stack: ['PostgreSQL', 'MySQL', 'MSSQL'],
    tags: ['HR', 'Payroll', 'Enterprise'],
    icon: MySQLIcon,
  },
  booking: {
    slug: 'booking engine',
    bannerTheme: 'mint',
    stack: ['PostgreSQL', 'SQLite', 'Docker'],
    tags: ['Booking', 'Reservations', 'Scheduling'],
    icon: SqliteIcon,
  },
  social: {
    slug: 'social network',
    bannerTheme: 'cyan',
    stack: ['PostgreSQL', 'MySQL', 'Docker'],
    tags: ['Social', 'Feeds', 'Messaging'],
    icon: PostgresIcon,
  },
  helpdesk: {
    slug: 'support desk',
    bannerTheme: 'dark',
    stack: ['PostgreSQL', 'MySQL', 'Docker'],
    tags: ['Support', 'Helpdesk', 'Ticketing'],
    icon: MySQLIcon,
  },
  auth: {
    slug: 'auth identity',
    bannerTheme: 'cyan',
    stack: ['PostgreSQL', 'SQLite', 'Docker'],
    tags: ['Auth & Security', 'IAM', 'Sessions'],
    icon: PostgresIcon,
  },
  tasks: {
    slug: 'task kanban',
    bannerTheme: 'amber',
    stack: ['PostgreSQL', 'SQLite', 'TypeScript'],
    tags: ['Productivity', 'Kanban', 'Projects'],
    icon: TypeScriptIcon,
  },
  links: {
    slug: 'link shortener',
    bannerTheme: 'mint',
    stack: ['PostgreSQL', 'SQLite', 'Docker'],
    tags: ['Analytics', 'Redirects', 'Utilities'],
    icon: SqliteIcon,
  },
  newsletter: {
    slug: 'newsletter feed',
    bannerTheme: 'dark',
    stack: ['PostgreSQL', 'MySQL', 'Docker'],
    tags: ['Marketing', 'Email', 'Audience'],
    icon: DockerIcon,
  },
  feedback: {
    slug: 'feedback board',
    bannerTheme: 'ice',
    stack: ['PostgreSQL', 'SQLite', 'Docker'],
    tags: ['Product', 'Feedback', 'Roadmaps'],
    icon: SqliteIcon,
  },
  bookmarks: {
    slug: 'bookmark vault',
    bannerTheme: 'cyan',
    stack: ['PostgreSQL', 'SQLite'],
    tags: ['Curation', 'Vault', 'Personal'],
    icon: PostgresIcon,
  },
  marketplace: {
    slug: 'marketplace hub',
    bannerTheme: 'amber',
    stack: ['PostgreSQL', 'MySQL', 'Docker', 'Prisma'],
    tags: ['Marketplace', 'Multi-vendor', 'Escrow'],
    icon: PrismaIcon,
  },
  erp: {
    slug: 'erp enterprise',
    bannerTheme: 'dark',
    stack: ['PostgreSQL', 'MSSQL', 'Docker', 'EF Core'],
    tags: ['ERP', 'Supply Chain', 'Enterprise'],
    icon: SqlServerIcon,
  },
};

/** Tüm şablonları zenginleştirilmiş katalog biçiminde getirir */
export function getEnrichedBlueprints(): EnrichedBlueprint[] {
  return TEMPLATES.map(tpl => {
    const meta = BLUEPRINT_METAS[tpl.key] ?? {
      slug: tpl.key.replace(/-/g, ' '),
      bannerTheme: 'cyan' as BannerTheme,
      stack: ['PostgreSQL', 'Docker'],
      tags: ['SaaS', 'Database'],
      icon: PostgresIcon,
    };

    return {
      ...tpl,
      slug: meta.slug,
      bannerTheme: meta.bannerTheme,
      stack: meta.stack,
      tags: meta.tags,
      icon: meta.icon,
      tableCount: tpl.schema.tables.length,
      relationCount: tpl.schema.relations.length,
    };
  });
}

/** Filtreleme için mevcut stack seçenekleri ve frekansları */
export function getStackCounts(blueprints: EnrichedBlueprint[]): { name: string; count: number }[] {
  const map = new Map<string, number>();
  for (const b of blueprints) {
    for (const s of b.stack) {
      map.set(s, (map.get(s) ?? 0) + 1);
    }
  }
  return [...map.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
}

/** Filtreleme için mevcut tag seçenekleri ve frekansları */
export function getTagCounts(blueprints: EnrichedBlueprint[]): { name: string; count: number }[] {
  const map = new Map<string, number>();
  for (const b of blueprints) {
    for (const t of b.tags) {
      map.set(t, (map.get(t) ?? 0) + 1);
    }
  }
  return [...map.entries()]
    .map(([name, count]) => ({ name, count }))
    .sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
}

/** Ölçek filtreleri ve sayıları */
export function getScaleCounts(blueprints: EnrichedBlueprint[]): { size: TemplateSize; label: string; count: number }[] {
  const labels: Record<TemplateSize, string> = {
    mini: 'Quick start',
    standard: 'Full product',
    large: 'Enterprise',
  };
  return (['mini', 'standard', 'large'] as TemplateSize[]).map(size => ({
    size,
    label: labels[size],
    count: blueprints.filter(b => b.size === size).length,
  }));
}
