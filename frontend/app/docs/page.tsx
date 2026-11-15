'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  BookOpen,
  Terminal,
  Server,
  Braces,
  Database,
  GitPullRequest,
  ShieldCheck,
  Copy,
  Check,
  Search,
  ArrowRight,
  Layers,
  Lock,
  AlertTriangle,
} from 'lucide-react';
import Footer from '../../components/layout/Footer';
import { IsometricIrPipeline } from '../../components/landing/IsometricVisuals';

interface DocSectionMeta {
  id: string;
  group: string;
  title: string;
  summary: string;
  icon: typeof BookOpen;
  keywords: string[];
}

const DOC_SECTIONS: DocSectionMeta[] = [
  {
    id: 'quickstart',
    group: 'Getting Started',
    title: 'Quickstart Guide',
    summary: 'Go from prompt, existing repo, or live database to verified DDL in under two minutes.',
    icon: BookOpen,
    keywords: ['quickstart', 'install', 'start', 'prompt', 'introspect', 'workflow'],
  },
  {
    id: 'cli-mcp',
    group: 'Integrations',
    title: 'CLI & Universal MCP Server',
    summary: 'Zero-install MCP server for Claude Code, Cursor, and VS Code.',
    icon: Terminal,
    keywords: ['cli', 'mcp', 'npx', 'claude', 'cursor', 'vscode', 'tools'],
  },
  {
    id: 'gateway',
    group: 'API Reference',
    title: 'Gateway REST API',
    summary: 'Scoped read/write REST endpoints, API key permissions, and audit logging.',
    icon: Server,
    keywords: ['gateway', 'rest', 'api', 'key', 'query', 'rpc', 'http'],
  },
  {
    id: 'ir',
    group: 'Core Engine',
    title: 'Canonical IR (ir.json)',
    summary: 'Single deterministic intermediate representation driving all 6 dialects and 19 eject targets.',
    icon: Braces,
    keywords: ['ir', 'json', 'canonical', 'schema', 'identity', 'enum', 'generated', 'collation'],
  },
  {
    id: 'dialects',
    group: 'Core Engine',
    title: 'SQL Dialect Matrix',
    summary: 'Exact feature mapping and safe NO ACTION fallbacks across 6 SQL engines.',
    icon: Database,
    keywords: ['postgres', 'mysql', 'mssql', 'oracle', 'mariadb', 'sqlite', 'dialect', 'cascade'],
  },
  {
    id: 'github-bot',
    group: 'Automation',
    title: 'GitHub PR Bot & CI/CD',
    summary: '3-way schema merge, impact scoring, and automated PR comments.',
    icon: GitPullRequest,
    keywords: ['github', 'pr', 'ci', 'cd', 'bot', 'workflow', 'diff', 'impact'],
  },
  {
    id: 'sandbox',
    group: 'Security',
    title: 'Sandbox & Security',
    summary: 'Ephemeral Docker verification, zero-persistence introspection, and SSRF protection.',
    icon: ShieldCheck,
    keywords: ['sandbox', 'docker', 'security', 'ssrf', 'vault', 'encryption'],
  },
];

const MCP_CONFIGS: Record<'claude' | 'cursor' | 'vscode', { label: string; path: string; code: string }> = {
  claude: {
    label: 'Claude Code',
    path: '.mcp.json (project root)',
    code: `{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"],
      "env": {
        "NAMINES_API_KEY": "nmn_live_your_scoped_key"
      }
    }
  }
}`,
  },
  cursor: {
    label: 'Cursor',
    path: '.cursor/mcp.json',
    code: `{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"],
      "env": {
        "NAMINES_API_URL": "https://api.namines.dev",
        "NAMINES_API_KEY": "nmn_live_your_scoped_key"
      }
    }
  }
}`,
  },
  vscode: {
    label: 'VS Code',
    path: '.vscode/mcp.json',
    code: `{
  "servers": {
    "namines": {
      "type": "stdio",
      "command": "npx",
      "args": ["-y", "@namines/mcp"],
      "env": {
        "NAMINES_API_KEY": "nmn_live_your_scoped_key"
      }
    }
  }
}`,
  },
};

const IR_SNIPPET = `{
  "version": "2.3",
  "engine": "PostgreSQL",
  "enums": [
    { "name": "order_status", "values": ["pending", "paid", "shipped", "refunded"] }
  ],
  "tables": [
    {
      "name": "orders",
      "columns": [
        { "name": "id", "type": "uuid", "primaryKey": true, "defaultValue": "gen_random_uuid()" },
        { "name": "seq_id", "type": "bigint", "identity": "ALWAYS" },
        { "name": "customer_id", "type": "uuid", "nullable": false },
        { "name": "status", "type": "enum:order_status", "nullable": false, "defaultValue": "'pending'" },
        { "name": "subtotal_cents", "type": "integer", "nullable": false },
        { "name": "tax_cents", "type": "integer", "generated": "subtotal_cents * 0.2" },
        { "name": "tags", "type": "text", "isArray": true, "collation": "C" }
      ],
      "foreignKeys": [
        { "column": "customer_id", "referencesTable": "customers", "referencesColumn": "id", "onDelete": "RESTRICT" }
      ],
      "indexes": [
        { "name": "idx_orders_customer_status", "columns": ["customer_id", "status"], "unique": false }
      ],
      "checks": [
        { "name": "chk_orders_subtotal_nonneg", "expression": "subtotal_cents >= 0" }
      ]
    }
  ]
}`;

const GATEWAY_REQUEST_SNIPPET = `curl -X POST "https://api.namines.dev/api/gateway/proj_98a1f/query" \\
  -H "Authorization: Bearer nmn_live_scoped_token" \\
  -H "Content-Type: application/json" \\
  -d '{
    "table": "orders",
    "select": ["id", "status", "subtotal_cents"],
    "where": { "status": "paid" },
    "orderBy": [{ "column": "seq_id", "direction": "desc" }],
    "limit": 25
  }'`;

const GITHUB_WORKFLOW_SNIPPET = `name: Namines Schema Guard
on:
  pull_request:
    paths:
      - 'db/**'
      - 'prisma/**'
      - 'migrations/**'

jobs:
  schema-impact:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Analyze Migration Risk & Run Sandbox DDL
        uses: namines/action-schema-guard@v2
        with:
          api-key: \${{ secrets.NAMINES_API_KEY }}
          dialect: postgresql
          run-sandbox-tests: true
          fail-on-breaking: true`;

function DocCodeBlock({
  language,
  filename,
  code,
}: {
  language: string;
  filename?: string;
  code: string;
}) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    try {
      await navigator.clipboard.writeText(code);
      setCopied(true);
      setTimeout(() => setCopied(false), 1800);
    } catch {
      // Clipboard API unavailable in restricted contexts.
    }
  };

  return (
    <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-950 overflow-hidden">
      <div className="flex items-center justify-between gap-3 px-4 py-2.5 border-b border-surface-500/70 bg-surface-900/70">
        <div className="flex items-center gap-2.5 min-w-0">
          <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800 border border-surface-500/80 text-[11px] font-mono uppercase text-accent-text shrink-0">
            {language}
          </span>
          {filename && (
            <span className="text-[12px] font-mono text-content-muted truncate">
              {filename}
            </span>
          )}
        </div>
        <button
          type="button"
          onClick={handleCopy}
          aria-label="Copy code snippet"
          className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[var(--radius-control)] border border-surface-500/80 bg-surface-800 hover:bg-surface-700 text-[11px] font-mono text-content-secondary hover:text-content-primary transition-colors cursor-pointer shrink-0"
        >
          {copied ? (
            <>
              <Check className="w-3.5 h-3.5 text-accent-text" />
              <span>Copied</span>
            </>
          ) : (
            <>
              <Copy className="w-3.5 h-3.5" />
              <span>Copy</span>
            </>
          )}
        </button>
      </div>
      <pre className="p-4 text-[12px] sm:text-[13px] font-mono leading-relaxed text-content-primary overflow-x-auto">
        <code>{code}</code>
      </pre>
    </div>
  );
}

export default function DocsPage() {
  const [query, setQuery] = useState('');
  const [mcpTab, setMcpTab] = useState<'claude' | 'cursor' | 'vscode'>('claude');
  const [activeId, setActiveId] = useState('quickstart');

  useEffect(() => {
    const hash = window.location.hash.replace(/^#/, '');
    if (hash && DOC_SECTIONS.some((s) => s.id === hash)) {
      setActiveId(hash);
    }
    window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior });
    const raf = requestAnimationFrame(() => {
      window.scrollTo({ top: 0, behavior: 'instant' as ScrollBehavior });
    });
    return () => cancelAnimationFrame(raf);
  }, []);

  const filteredSections = useMemo(() => {
    const q = query.trim().toLowerCase();
    if (!q) return DOC_SECTIONS;
    return DOC_SECTIONS.filter(
      (s) =>
        s.title.toLowerCase().includes(q) ||
        s.summary.toLowerCase().includes(q) ||
        s.keywords.some((k) => k.includes(q))
    );
  }, [query]);

  const scrollToSection = (id: string) => {
    setActiveId(id);
    const el = document.getElementById(id);
    if (el) {
      el.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
  };

  return (
    <div className="min-h-screen bg-surface-900 text-content-primary flex flex-col">
      {/* Top Documentation Header */}
      <section className="border-b border-surface-500/60 bg-surface-900">
        <div className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-12 lg:py-16 grid grid-cols-1 lg:grid-cols-[1.25fr_0.75fr] gap-10 items-center">
          <div className="max-w-3xl space-y-4">
            <h1 className="text-3xl sm:text-4xl lg:text-[42px] font-bold tracking-tight leading-[1.15] text-content-primary">
              Deterministic schema engineering, documented end to end.
            </h1>
            <p className="text-base sm:text-lg text-content-secondary leading-relaxed">
              Integrate Namines into your IDE via Universal MCP, automate pull-request migration checks, inspect the canonical <code className="font-mono text-content-primary">ir.json</code> format, and deploy verified DDL across six SQL dialects.
            </p>
            <div className="flex flex-wrap items-center gap-3 pt-2">
              <Link
                href="/new"
                className="tap-44 inline-flex items-center gap-2 px-4 py-2.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-accent-on text-[14px] font-semibold transition-colors"
              >
                <span>Open Schema Architect</span>
                <ArrowRight className="w-4 h-4" />
              </Link>
              <Link
                href="/compile"
                className="tap-44 inline-flex items-center gap-2 px-4 py-2.5 rounded-[var(--radius-control)] border border-surface-500 bg-surface-800/60 hover:bg-surface-800 text-content-primary text-[14px] font-semibold transition-colors"
              >
                <span>Explore 19 Eject Targets</span>
              </Link>
            </div>
          </div>

          <div className="hidden lg:block">
            <IsometricIrPipeline />
          </div>
        </div>
      </section>

      {/* Main Docs Layout: Sticky Sidebar + Structured Article */}
      <main className="flex-1 w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-10 lg:py-14">
        <div className="grid grid-cols-1 lg:grid-cols-[260px_minmax(0,1fr)] gap-10 lg:gap-14 items-start">
          {/* Left Sticky Sidebar */}
          <aside className="lg:sticky lg:top-24 space-y-5">
            <div className="relative">
              <Search className="w-4 h-4 text-content-muted absolute left-3 top-1/2 -translate-y-1/2 pointer-events-none" />
              <input
                type="search"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                placeholder="Filter docs..."
                aria-label="Filter documentation sections"
                className="w-full pl-9 pr-3 py-2 rounded-[var(--radius-control)] bg-surface-800/70 border border-surface-500 text-[13px] text-content-primary placeholder:text-content-muted focus:outline-none focus:border-accent"
              />
            </div>

            <nav aria-label="Documentation sections" className="space-y-1">
              {filteredSections.map((section) => {
                const isActive = activeId === section.id;
                return (
                  <button
                    key={section.id}
                    type="button"
                    onClick={() => scrollToSection(section.id)}
                    className={`tap-44 w-full text-left px-3 py-2 rounded-[var(--radius-control)] flex items-center justify-between gap-2 text-[13px] transition-colors cursor-pointer ${
                      isActive
                        ? 'bg-surface-800 text-content-primary font-semibold border border-surface-500'
                        : 'text-content-secondary hover:text-content-primary hover:bg-surface-800/40 border border-transparent'
                    }`}
                  >
                    <span className="truncate">{section.title}</span>
                    {isActive && (
                      <span className="w-1.5 h-1.5 rounded-full bg-accent shrink-0" />
                    )}
                  </button>
                );
              })}
              {filteredSections.length === 0 && (
                <p className="px-3 py-2 text-[12px] text-content-muted">
                  No matching documentation section.
                </p>
              )}
            </nav>

            <div className="p-4 rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/80 space-y-2">
              <div className="text-[11px] font-mono uppercase tracking-wider text-content-muted">
                Need live help?
              </div>
              <p className="text-[12px] text-content-secondary leading-relaxed">
                Test any schema against ephemeral Docker engines in the Blueprints sandbox.
              </p>
              <Link
                href="/demo"
                className="inline-flex items-center gap-1.5 text-[12px] font-semibold text-accent-text hover:underline"
              >
                <span>Open Blueprints Sandbox</span>
                <ArrowRight className="w-3.5 h-3.5" />
              </Link>
            </div>
          </aside>

          {/* Right Content Column */}
          <div className="min-w-0 space-y-16">
            {/* 1. Quickstart */}
            <section id="quickstart" className="scroll-mt-24 space-y-6">
              <div className="space-y-2 border-b border-surface-500/60 pb-4">
                <div className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                  Getting Started
                </div>
                <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">
                  1. Quickstart Guide
                </h2>
                <p className="text-[15px] text-content-secondary leading-relaxed">
                  Namines separates creative schema modeling from deterministic compilation. Every change passes through a typed <code className="font-mono text-content-primary">ir.json</code> graph before touching SQL or ORM code.
                </p>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                {[
                  {
                    step: '01',
                    title: 'Author or Introspect',
                    body: 'Start from natural language, paste SQL/Prisma/EF Core, scan a GitHub repo, or introspect a live database without persisting credentials.',
                    href: '/new',
                    cta: 'Launch Architect',
                  },
                  {
                    step: '02',
                    title: 'Review & Sandbox Test',
                    body: 'Run SchemaImpactAnalyzer to classify Safe, Warning, and Breaking diffs, then execute DDL inside an ephemeral Docker container.',
                    href: '/review',
                    cta: 'Open Review Console',
                  },
                  {
                    step: '03',
                    title: 'Compile & Eject',
                    body: 'Export verified DDL across 6 SQL dialects and 19 downstream targets including Prisma, Drizzle, TypeScript SDK, and Next.js admin panels.',
                    href: '/compile',
                    cta: 'Open Compiler',
                  },
                ].map((card) => (
                  <div
                    key={card.step}
                    className="p-5 rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/80 flex flex-col justify-between gap-4"
                  >
                    <div className="space-y-2">
                      <span className="inline-block text-[11px] font-mono font-semibold text-accent-text">
                        STEP {card.step}
                      </span>
                      <h3 className="text-[16px] font-semibold text-content-primary">
                        {card.title}
                      </h3>
                      <p className="text-[13px] text-content-secondary leading-relaxed">
                        {card.body}
                      </p>
                    </div>
                    <Link
                      href={card.href}
                      className="inline-flex items-center gap-1.5 text-[13px] font-semibold text-accent-text hover:underline"
                    >
                      <span>{card.cta}</span>
                      <ArrowRight className="w-3.5 h-3.5" />
                    </Link>
                  </div>
                ))}
              </div>
            </section>

            {/* 2. CLI & Universal MCP */}
            <section id="cli-mcp" className="scroll-mt-24 space-y-6">
              <div className="space-y-2 border-b border-surface-500/60 pb-4">
                <div className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                  Integrations
                </div>
                <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">
                  2. CLI &amp; Universal MCP Server
                </h2>
                <p className="text-[15px] text-content-secondary leading-relaxed">
                  Connect your coding agent directly to Namines without global package installs. The stdio MCP server exposes deterministic schema compilation, impact analysis, and dialect verification tools.
                </p>
              </div>

              <div className="space-y-3">
                <div className="flex flex-wrap items-center gap-2">
                  {(['claude', 'cursor', 'vscode'] as const).map((key) => (
                    <button
                      key={key}
                      type="button"
                      onClick={() => setMcpTab(key)}
                      className={`px-3.5 py-1.5 rounded-[var(--radius-control)] text-[13px] font-medium border transition-colors cursor-pointer ${
                        mcpTab === key
                          ? 'bg-accent text-accent-on border-accent'
                          : 'bg-surface-800/60 text-content-secondary border-surface-500 hover:text-content-primary'
                      }`}
                    >
                      {MCP_CONFIGS[key].label}
                    </button>
                  ))}
                </div>
                <DocCodeBlock
                  language="json"
                  filename={MCP_CONFIGS[mcpTab].path}
                  code={MCP_CONFIGS[mcpTab].code}
                />
              </div>

              <div className="rounded-[var(--radius-card)] border border-surface-500/80 overflow-hidden">
                <div className="px-4 py-3 bg-surface-800/60 border-b border-surface-500/70 text-[12px] font-mono font-semibold text-content-primary">
                  Exposed MCP Tools
                </div>
                <div className="divide-y divide-surface-500/60">
                  {[
                    {
                      tool: 'namines_compile_ddl',
                      desc: 'Compiles canonical IR into target dialect SQL (PostgreSQL, MySQL, MSSQL, Oracle, MariaDB, SQLite).',
                    },
                    {
                      tool: 'namines_analyze_impact',
                      desc: 'Diffs base vs target IR and classifies destructive drops, lock risks, and affected code references.',
                    },
                    {
                      tool: 'namines_eject_target',
                      desc: 'Generates Prisma, Drizzle, EF Core, TypeScript SDK, Zod, Pydantic, or OpenAPI artifacts.',
                    },
                    {
                      tool: 'namines_run_sandbox',
                      desc: 'Executes generated DDL inside an isolated ephemeral container and returns engine diagnostics.',
                    },
                  ].map((row) => (
                    <div
                      key={row.tool}
                      className="p-4 grid grid-cols-1 sm:grid-cols-[220px_1fr] gap-2 sm:gap-4 items-start"
                    >
                      <code className="text-[12px] font-mono text-accent-text break-all">
                        {row.tool}
                      </code>
                      <p className="text-[13px] text-content-secondary leading-relaxed">
                        {row.desc}
                      </p>
                    </div>
                  ))}
                </div>
              </div>
            </section>

            {/* 3. Gateway REST API */}
            <section id="gateway" className="scroll-mt-24 space-y-6">
              <div className="space-y-2 border-b border-surface-500/60 pb-4">
                <div className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                  API Reference
                </div>
                <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">
                  3. Gateway REST API
                </h2>
                <p className="text-[15px] text-content-secondary leading-relaxed">
                  Query and mutate live project tables through scoped API keys (<code className="font-mono text-content-primary">read</code>, <code className="font-mono text-content-primary">write</code>, <code className="font-mono text-content-primary">rpc</code>, <code className="font-mono text-content-primary">admin</code>) with full audit logging and SSRF protection.
                </p>
              </div>

              <div className="rounded-[var(--radius-card)] border border-surface-500/80 divide-y divide-surface-500/60 overflow-hidden">
                {[
                  {
                    method: 'GET',
                    path: '/api/gateway/{projectId}/tables/{table}',
                    scope: 'read',
                    desc: 'Paginated list and single-row lookup with parameterized filters.',
                  },
                  {
                    method: 'POST',
                    path: '/api/gateway/{projectId}/query',
                    scope: 'read',
                    desc: 'Structured JSON query builder (select, where, orderBy, limit) compiled safely per engine.',
                  },
                  {
                    method: 'POST',
                    path: '/api/gateway/{projectId}/mutate',
                    scope: 'write',
                    desc: 'Schema-validated insert, update, delete, or batch import with audit trail.',
                  },
                  {
                    method: 'POST',
                    path: '/api/gateway/{projectId}/rpc/{fn}',
                    scope: 'rpc',
                    desc: 'Invoke whitelisted stored procedures or server functions.',
                  },
                ].map((ep) => (
                  <div
                    key={ep.path}
                    className="p-4 flex flex-col sm:flex-row sm:items-center justify-between gap-2 bg-surface-800/30"
                  >
                    <div className="space-y-1 min-w-0">
                      <div className="flex items-center gap-2.5 flex-wrap">
                        <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-accent/15 text-accent-text border border-accent/30 text-[11px] font-mono font-semibold">
                          {ep.method}
                        </span>
                        <code className="text-[13px] font-mono text-content-primary break-all">
                          {ep.path}
                        </code>
                      </div>
                      <p className="text-[13px] text-content-secondary">{ep.desc}</p>
                    </div>
                    <span className="px-2.5 py-1 rounded-full bg-surface-800 border border-surface-500 text-[11px] font-mono text-content-muted shrink-0 self-start sm:self-center">
                      scope: {ep.scope}
                    </span>
                  </div>
                ))}
              </div>

              <DocCodeBlock
                language="bash"
                filename="Example structured query"
                code={GATEWAY_REQUEST_SNIPPET}
              />
            </section>

            {/* 4. Canonical IR */}
            <section id="ir" className="scroll-mt-24 space-y-6">
              <div className="space-y-2 border-b border-surface-500/60 pb-4">
                <div className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                  Core Engine
                </div>
                <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">
                  4. Canonical IR (<code className="font-mono">ir.json</code>)
                </h2>
                <p className="text-[15px] text-content-secondary leading-relaxed">
                  Every Namines project is backed by a single canonical JSON intermediate representation supporting <code className="font-mono text-content-primary">identity</code>, <code className="font-mono text-content-primary">enum</code>, <code className="font-mono text-content-primary">generated</code> columns, <code className="font-mono text-content-primary">collation</code>, arrays, composite indexes, and check constraints.
                </p>
              </div>

              <DocCodeBlock
                language="json"
                filename="ir.json"
                code={IR_SNIPPET}
              />
            </section>

            {/* 5. SQL Dialect Matrix */}
            <section id="dialects" className="scroll-mt-24 space-y-6">
              <div className="space-y-2 border-b border-surface-500/60 pb-4">
                <div className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                  Core Engine
                </div>
                <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">
                  5. SQL Dialect Matrix
                </h2>
                <p className="text-[15px] text-content-secondary leading-relaxed">
                  When a target engine does not support a requested referential action or construct, Namines always falls back to the most restrictive safe option (<code className="font-mono text-content-primary">NO ACTION</code>) — never <code className="font-mono text-content-primary">CASCADE</code>.
                </p>
              </div>

              <div className="rounded-[var(--radius-card)] border border-surface-500/80 overflow-x-auto">
                <table className="w-full text-left border-collapse min-w-[640px]">
                  <thead>
                    <tr className="bg-surface-800/70 border-b border-surface-500/80 text-[11px] font-mono uppercase tracking-wider text-content-muted">
                      <th className="py-3 px-4">Engine</th>
                      <th className="py-3 px-4">Identity / Auto</th>
                      <th className="py-3 px-4">Enums</th>
                      <th className="py-3 px-4">JSON Type</th>
                      <th className="py-3 px-4">Unsupported FK Action</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-surface-500/60 text-[13px]">
                    {[
                      ['PostgreSQL', 'GENERATED ALWAYS AS IDENTITY', 'CREATE TYPE ... AS ENUM', 'JSONB', 'NO ACTION'],
                      ['MySQL 8.0+', 'AUTO_INCREMENT', 'Inline ENUM(...)', 'JSON', 'NO ACTION'],
                      ['SQL Server', 'IDENTITY(1,1)', 'VARCHAR + CHECK', 'NVARCHAR(MAX) + ISJSON', 'NO ACTION'],
                      ['Oracle', 'GENERATED AS IDENTITY', 'VARCHAR2 + CHECK', 'CLOB / JSON (21c+)', 'NO ACTION'],
                      ['MariaDB', 'AUTO_INCREMENT', 'Inline ENUM(...)', 'LONGTEXT + JSON_VALID', 'NO ACTION'],
                      ['SQLite', 'INTEGER PRIMARY KEY', 'TEXT + CHECK', 'TEXT', 'NO ACTION'],
                    ].map(([engine, identity, enums, json, fk]) => (
                      <tr key={engine} className="hover:bg-surface-800/30">
                        <td className="py-3 px-4 font-semibold text-content-primary">{engine}</td>
                        <td className="py-3 px-4 font-mono text-[12px] text-content-secondary">{identity}</td>
                        <td className="py-3 px-4 font-mono text-[12px] text-content-secondary">{enums}</td>
                        <td className="py-3 px-4 font-mono text-[12px] text-content-secondary">{json}</td>
                        <td className="py-3 px-4 font-mono text-[12px] text-accent-text">{fk}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>

            {/* 6. GitHub PR Bot */}
            <section id="github-bot" className="scroll-mt-24 space-y-6">
              <div className="space-y-2 border-b border-surface-500/60 pb-4">
                <div className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                  Automation
                </div>
                <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">
                  6. GitHub PR Bot &amp; CI/CD
                </h2>
                <p className="text-[15px] text-content-secondary leading-relaxed">
                  Automatically compute 3-way schema merges, risk scores, and affected repository files on every pull request. Safe migrations can be auto-approved per project policy with a full <code className="font-mono text-content-primary">ChangeRequestAuditLog</code>.
                </p>
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                {[
                  {
                    tier: 'Safe',
                    desc: 'Additive tables, nullable columns, and non-blocking indexes.',
                    icon: Layers,
                  },
                  {
                    tier: 'Warning',
                    desc: 'Type widening, new NOT NULL with default, or index rebuilds.',
                    icon: AlertTriangle,
                  },
                  {
                    tier: 'Breaking',
                    desc: 'Column/table drops, narrowing types, or cascade changes.',
                    icon: Lock,
                  },
                ].map((item) => {
                  const Icon = item.icon;
                  return (
                    <div
                      key={item.tier}
                      className="p-4 rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/80 space-y-1.5"
                    >
                      <div className="flex items-center gap-2 text-[14px] font-semibold text-content-primary">
                        <Icon className="w-4 h-4 text-accent-text" />
                        <span>{item.tier}</span>
                      </div>
                      <p className="text-[13px] text-content-secondary leading-relaxed">
                        {item.desc}
                      </p>
                    </div>
                  );
                })}
              </div>

              <DocCodeBlock
                language="yaml"
                filename=".github/workflows/namines-check.yml"
                code={GITHUB_WORKFLOW_SNIPPET}
              />
            </section>

            {/* 7. Sandbox & Security */}
            <section id="sandbox" className="scroll-mt-24 space-y-6">
              <div className="space-y-2 border-b border-surface-500/60 pb-4">
                <div className="text-[11px] font-mono uppercase tracking-wider text-accent-text">
                  Security
                </div>
                <h2 className="text-2xl sm:text-3xl font-bold tracking-tight">
                  7. Sandbox &amp; Security Guarantees
                </h2>
                <p className="text-[15px] text-content-secondary leading-relaxed">
                  Namines is engineered with defense-in-depth for both control-plane metadata and user database connections.
                </p>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                {[
                  {
                    title: 'Zero docker.sock Mounts',
                    body: 'Ephemeral sandbox provisioning uses isolated provisioning boundaries rather than mounting the host Docker socket inside application containers.',
                  },
                  {
                    title: 'SSRF-Hardened Introspection',
                    body: 'Private network ranges, loopback, and cloud metadata IPs are blocked during live database introspection and Gateway routing.',
                  },
                  {
                    title: 'Zero-Persistence Introspection',
                    body: 'Schema discovery extracts structural metadata in memory without retaining connection strings unless explicitly saved to encrypted Vault.',
                  },
                  {
                    title: 'Encrypted Vault & Audit Trails',
                    body: 'Saved credentials and backup snapshots use authenticated encryption, while every migration approval and Gateway write is logged.',
                  },
                ].map((item) => (
                    <div
                      key={item.title}
                      className="p-5 rounded-[var(--radius-card)] bg-surface-800/40 border border-surface-500/80 space-y-2"
                    >
                      <h3 className="text-[15px] font-semibold text-content-primary">
                        {item.title}
                      </h3>
                      <p className="text-[13px] text-content-secondary leading-relaxed">
                        {item.body}
                      </p>
                    </div>
                ))}
              </div>
            </section>
          </div>
        </div>
      </main>

      <Footer />
    </div>
  );
}
