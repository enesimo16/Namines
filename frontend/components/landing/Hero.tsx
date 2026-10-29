'use client';

import { useEffect, useRef, useState, type ReactNode } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { sourceService } from '../../services/api';

type DemoTabId = 'prompt' | 'github' | 'sources' | 'mcp' | 'eject' | 'workflows';

const DEMO_TABS: { id: DemoTabId; label: string }[] = [
  { id: 'prompt', label: 'Prompt to DB' },
  { id: 'github', label: 'Import GitHub' },
  { id: 'sources', label: 'Zero-Prompt Start' },
  { id: 'mcp', label: 'Universal MCP' },
  { id: 'eject', label: 'SDK & Admin Eject' },
  { id: 'workflows', label: 'GitHub Bot & AI' },
];

type SqlEngine =
  | 'PostgreSQL'
  | 'MySQL'
  | 'SQL Server'
  | 'SQLite'
  | 'Oracle'
  | 'MariaDB';

const ALL_ENGINES: SqlEngine[] = [
  'PostgreSQL',
  'MySQL',
  'SQL Server',
  'SQLite',
  'Oracle',
  'MariaDB',
];

interface MiniColumn {
  table: string;
  name: string;
  pgType: string;
  rule: string;
}

interface MiniTable {
  name: string;
  columns: { name: string; kind: 'pk' | 'fk' | 'text' | 'amount' | 'status' | 'timestamp'; refTable?: string }[];
}

const STOP_WORDS = new Set([
  'with', 'and', 'for', 'the', 'from', 'into', 'that', 'this', 'have', 'has',
  'table', 'tables', 'database', 'schema', 'system', 'app', 'application',
  'multi', 'tenant', 'saas', 'create', 'build', 'design', 'need', 'want',
  'ile', 've', 'için', 'bir', 'olan', 'tablo', 'tabloları', 'veritabanı',
  'sistemi', 'uygulaması', 'olsun', 'ekle', 'yap',
]);

function extractTablesFromPrompt(prompt: string): { tables: MiniTable[]; totalDetected: number } {
  const cleaned = prompt
    .toLowerCase()
    .replace(/[^a-z0-9_ğüşıöç\s,;-]/gi, ' ')
    .split(/[\s,;-]+/)
    .map((w) => w.trim())
    .filter((w) => w.length >= 3 && !STOP_WORDS.has(w));

  const uniqueNames: string[] = [];
  for (const word of cleaned) {
    const normalized = word
      .replace(/ğ/g, 'g')
      .replace(/ü/g, 'u')
      .replace(/ş/g, 's')
      .replace(/ı/g, 'i')
      .replace(/ö/g, 'o')
      .replace(/ç/g, 'c')
      .replace(/[^a-z0-9_]/g, '');
    if (normalized.length >= 3 && !uniqueNames.includes(normalized)) {
      uniqueNames.push(normalized.endsWith('s') ? normalized : `${normalized}s`);
    }
  }

  if (uniqueNames.length === 0) {
    uniqueNames.push('organizations', 'subscriptions');
  }

  const totalDetected = uniqueNames.length;
  const limited = uniqueNames.slice(0, 4);

  const tables: MiniTable[] = limited.map((tName, idx) => {
    const cols: MiniTable['columns'] = [{ name: 'id', kind: 'pk' }];
    if (idx > 0) {
      const parent = limited[0];
      const singularParent = parent.endsWith('s') ? parent.slice(0, -1) : parent;
      cols.push({ name: `${singularParent}_id`, kind: 'fk', refTable: parent });
    }
    if (tName.includes('invoice') || tName.includes('order') || tName.includes('payment') || tName.includes('plan')) {
      cols.push({ name: 'amount_cents', kind: 'amount' });
      cols.push({ name: 'status', kind: 'status' });
    } else {
      cols.push({ name: 'name', kind: 'text' });
      cols.push({ name: 'status', kind: 'status' });
    }
    cols.push({ name: 'created_at', kind: 'timestamp' });
    return { name: tName, columns: cols };
  });

  return { tables, totalDetected };
}

function compileMiniDdl(tables: MiniTable[], engine: SqlEngine): string {
  return tables
    .map((t) => {
      const tIdent = engine === 'SQL Server' ? `[${t.name}]` : t.name;
      const lines = t.columns.map((c) => {
        const cIdent = engine === 'SQL Server' ? `[${c.name}]` : c.name;
        if (c.kind === 'pk') {
          if (engine === 'PostgreSQL') return `  ${cIdent} UUID PRIMARY KEY DEFAULT gen_random_uuid()`;
          if (engine === 'MySQL') return `  ${cIdent} CHAR(36) PRIMARY KEY DEFAULT (UUID())`;
          if (engine === 'SQL Server') return `  ${cIdent} UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWSEQUENTIALID()`;
          if (engine === 'SQLite') return `  ${cIdent} TEXT PRIMARY KEY`;
          if (engine === 'Oracle') return `  ${cIdent} RAW(16) DEFAULT SYS_GUID() PRIMARY KEY`;
          return `  ${cIdent} UUID PRIMARY KEY DEFAULT UUID()`;
        }
        if (c.kind === 'fk' && c.refTable) {
          const refIdent = engine === 'SQL Server' ? `[${c.refTable}]([id])` : `${c.refTable}(id)`;
          if (engine === 'PostgreSQL' || engine === 'MariaDB')
            return `  ${cIdent} UUID NOT NULL REFERENCES ${refIdent} ON DELETE RESTRICT`;
          if (engine === 'MySQL')
            return `  ${cIdent} CHAR(36) NOT NULL REFERENCES ${refIdent}`;
          if (engine === 'SQL Server')
            return `  ${cIdent} UNIQUEIDENTIFIER NOT NULL REFERENCES ${refIdent}`;
          if (engine === 'SQLite')
            return `  ${cIdent} TEXT NOT NULL REFERENCES ${refIdent} ON DELETE RESTRICT`;
          return `  ${cIdent} RAW(16) NOT NULL REFERENCES ${refIdent}`;
        }
        if (c.kind === 'amount') {
          if (engine === 'Oracle') return `  ${cIdent} NUMBER(12, 0) NOT NULL`;
          return `  ${cIdent} BIGINT NOT NULL DEFAULT 0`;
        }
        if (c.kind === 'status') {
          if (engine === 'SQLite') return `  ${cIdent} TEXT NOT NULL DEFAULT 'active'`;
          if (engine === 'Oracle') return `  ${cIdent} VARCHAR2(24 CHAR) DEFAULT 'active' NOT NULL`;
          if (engine === 'SQL Server') return `  ${cIdent} NVARCHAR(24) NOT NULL DEFAULT 'active'`;
          return `  ${cIdent} VARCHAR(24) NOT NULL DEFAULT 'active'`;
        }
        if (c.kind === 'timestamp') {
          if (engine === 'PostgreSQL') return `  ${cIdent} TIMESTAMPTZ NOT NULL DEFAULT now()`;
          if (engine === 'MySQL' || engine === 'MariaDB')
            return `  ${cIdent} TIMESTAMP(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6)`;
          if (engine === 'SQL Server')
            return `  ${cIdent} DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET()`;
          if (engine === 'SQLite') return `  ${cIdent} TEXT NOT NULL DEFAULT (datetime('now'))`;
          return `  ${cIdent} TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL`;
        }
        if (engine === 'SQLite') return `  ${cIdent} TEXT NOT NULL`;
        if (engine === 'Oracle') return `  ${cIdent} VARCHAR2(120 CHAR) NOT NULL`;
        if (engine === 'SQL Server') return `  ${cIdent} NVARCHAR(120) NOT NULL`;
        return `  ${cIdent} VARCHAR(120) NOT NULL`;
      });

      const suffix =
        engine === 'MySQL' || engine === 'MariaDB'
          ? ' ENGINE=InnoDB;'
          : engine === 'SQLite'
            ? ' STRICT;'
            : ';';
      return `CREATE TABLE ${tIdent} (\n${lines.join(',\n')}\n)${suffix}`;
    })
    .join('\n\n');
}

function flattenMiniColumns(tables: MiniTable[]): MiniColumn[] {
  const out: MiniColumn[] = [];
  for (const t of tables) {
    for (const c of t.columns) {
      out.push({
        table: t.name,
        name: c.name,
        pgType:
          c.kind === 'pk' || c.kind === 'fk'
            ? 'UUID'
            : c.kind === 'amount'
              ? 'BIGINT'
              : c.kind === 'timestamp'
                ? 'TIMESTAMPTZ'
                : 'VARCHAR',
        rule:
          c.kind === 'pk'
            ? 'PRIMARY KEY'
            : c.kind === 'fk'
              ? `FK → ${c.refTable}(id) RESTRICT`
              : 'NOT NULL',
      });
    }
  }
  return out;
}

/* ── Brand Mini Icons ── */
function ClaudeBrandIcon() {
  return (
    <svg className="w-4 h-4 shrink-0" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="12" cy="12" r="10" style={{ fill: 'var(--brand-icon-claude)' }} />
      <path
        d="M12 5.5v13M5.5 12h13M7.4 7.4l9.2 9.2M16.6 7.4l-9.2 9.2"
        stroke="var(--surface-900)"
        strokeWidth="2"
        strokeLinecap="round"
      />
    </svg>
  );
}

function CodexBrandIcon() {
  return (
    <svg className="w-4 h-4 shrink-0 text-content-primary" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <rect x="2" y="2" width="20" height="20" rx="5" fill="currentColor" />
      <path
        d="M8.5 9L11.5 12L8.5 15M12.5 15H16"
        stroke="var(--surface-900)"
        strokeWidth="2"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

function CursorBrandIcon() {
  return (
    <svg className="w-4 h-4 shrink-0 text-content-primary" viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <path d="M12 2L20.5 7V17L12 22L3.5 17V7L12 2Z" fill="currentColor" />
      <path d="M12 6.5L17 15.5H13.2L12 13L10.8 15.5H7L12 6.5Z" fill="var(--surface-900)" />
    </svg>
  );
}

function VsCodeBrandIcon() {
  return (
    <img
      src="/brand/ide/vscode.png"
      alt=""
      aria-hidden="true"
      className="w-4 h-4 object-contain shrink-0"
    />
  );
}

function WindsurfBrandIcon() {
  return (
    <img
      src="/brand/ide/windsurf.png"
      alt=""
      aria-hidden="true"
      className="w-4 h-4 object-contain shrink-0"
    />
  );
}

function AntigravityBrandIcon() {
  return (
    <img
      src="/brand/ide/antigravity.png"
      alt=""
      aria-hidden="true"
      className="w-4 h-4 object-contain shrink-0"
    />
  );
}

function GitHubBrandIcon() {
  return (
    <svg className="w-4 h-4 shrink-0 text-content-primary" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path
        fillRule="evenodd"
        clipRule="evenodd"
        d="M12 2C6.477 2 2 6.484 2 12.017c0 4.425 2.865 8.18 6.839 9.504.5.092.682-.217.682-.483 0-.237-.008-.868-.013-1.703-2.782.605-3.369-1.343-3.369-1.343-.454-1.158-1.11-1.466-1.11-1.466-.908-.62.069-.608.069-.608 1.003.07 1.53 1.032 1.53 1.032.892 1.53 2.341 1.088 2.91.832.092-.647.35-1.088.636-1.338-2.22-.253-4.555-1.113-4.555-4.951 0-1.093.39-1.988 1.029-2.688-.103-.253-.446-1.272.098-2.65 0 0 .84-.27 2.75 1.026A9.564 9.564 0 0112 6.844c.85.004 1.705.115 2.504.337 1.909-1.296 2.747-1.027 2.747-1.027.546 1.379.202 2.398.1 2.651.64.7 1.028 1.595 1.028 2.688 0 3.848-2.339 4.695-4.566 4.943.359.309.678.92.678 1.855 0 1.338-.012 2.419-.012 2.747 0 .268.18.58.688.482A10.019 10.019 0 0022 12.017C22 6.484 17.522 2 12 2z"
      />
    </svg>
  );
}

interface LiveGitHubScanPreview {
  repoSlug: string;
  branch: string;
  format: string;
  parsedFiles: string[];
  tables: { name: string; columnCount: number; summary: string }[];
  totalTables: number;
  redirecting?: boolean;
}

function parseGitHubOwnerRepo(rawUrl: string): { owner: string; repo: string } | null {
  const trimmed = rawUrl.trim().replace(/\/+$/, '').replace(/\.git$/i, '');
  if (!trimmed) return null;
  const urlMatch = trimmed.match(/github\.com\/([^/\s]+)\/([^/\s#?]+)/i);
  if (urlMatch) {
    return { owner: urlMatch[1], repo: urlMatch[2] };
  }
  const slugMatch = trimmed.match(/^([a-zA-Z0-9_.-]+)\/([a-zA-Z0-9_.-]+)$/);
  if (slugMatch) {
    return { owner: slugMatch[1], repo: slugMatch[2] };
  }
  return null;
}

async function scanGitHubRepoLive(
  rawUrl: string,
  branchInput?: string
): Promise<LiveGitHubScanPreview> {
  const parsed = parseGitHubOwnerRepo(rawUrl);
  if (!parsed) {
    throw new Error('Please enter a valid GitHub repository (e.g. owner/repo or https://github.com/owner/repo).');
  }
  const repoSlug = `${parsed.owner}/${parsed.repo}`;

  // 1. Try backend repository scanner first
  try {
    const res = await sourceService.scanRepository(rawUrl.trim(), branchInput?.trim() || undefined);
    const schemaObj = res.schema as { tables?: { name?: string; columns?: unknown[] }[] } | null;
    const rawTables = Array.isArray(schemaObj?.tables) ? schemaObj.tables : [];
    const mappedTables = rawTables.slice(0, 6).map((t) => ({
      name: t.name || 'table',
      columnCount: Array.isArray(t.columns) ? t.columns.length : 4,
      summary: `${Array.isArray(t.columns) ? t.columns.length : 4} columns extracted`,
    }));
    return {
      repoSlug,
      branch: res.branch || branchInput?.trim() || 'main',
      format: res.format || 'Repository AST',
      parsedFiles: res.parsedFiles.slice(0, 6),
      tables: mappedTables,
      totalTables: rawTables.length,
    };
  } catch {
    // 2. Fallback to direct public GitHub API inspection in browser
    const repoResp = await fetch(`https://api.github.com/repos/${encodeURIComponent(parsed.owner)}/${encodeURIComponent(parsed.repo)}`);
    if (!repoResp.ok) {
      throw new Error(
        repoResp.status === 404
          ? 'Repository not found or private. Enter a public GitHub repository.'
          : 'Could not reach GitHub API. Please try again.'
      );
    }
    const repoMeta = (await repoResp.json()) as { default_branch?: string };
    const activeBranch = branchInput?.trim() || repoMeta.default_branch || 'main';

    const treeResp = await fetch(
      `https://api.github.com/repos/${encodeURIComponent(parsed.owner)}/${encodeURIComponent(parsed.repo)}/git/trees/${encodeURIComponent(activeBranch)}?recursive=1`
    );
    if (!treeResp.ok) {
      throw new Error(`Could not read branch "${activeBranch}" from ${repoSlug}.`);
    }
    const treeData = (await treeResp.json()) as {
      tree?: { path?: string; type?: string }[];
    };
    const allBlobs = (treeData.tree ?? [])
      .filter((n) => n.type === 'blob' && typeof n.path === 'string')
      .map((n) => n.path as string);

    const schemaPaths = allBlobs.filter((p) =>
      /\.(prisma|sql)$/i.test(p) ||
      /(schema|models|entities|migration|dbcontext|table).*\.(ts|js|cs|py|go)$/i.test(p)
    );

    const filesToInspect = (schemaPaths.length > 0 ? schemaPaths : allBlobs.slice(0, 5)).slice(0, 6);
    const detectedFormat = schemaPaths.some((p) => p.endsWith('.prisma'))
      ? 'Prisma Schema'
      : schemaPaths.some((p) => p.endsWith('.sql'))
        ? 'SQL Migrations'
        : schemaPaths.some((p) => p.endsWith('.cs'))
          ? 'EF Core Entities'
          : schemaPaths.some((p) => p.endsWith('.ts'))
            ? 'TypeScript / Drizzle'
            : 'Repository Structure';

    const extractedTables: { name: string; columnCount: number; summary: string }[] = [];

    for (const filePath of filesToInspect.slice(0, 3)) {
      try {
        const rawFileResp = await fetch(
          `https://raw.githubusercontent.com/${encodeURIComponent(parsed.owner)}/${encodeURIComponent(parsed.repo)}/${encodeURIComponent(activeBranch)}/${filePath.split('/').map(encodeURIComponent).join('/')}`
        );
        if (!rawFileResp.ok) continue;
        const content = await rawFileResp.text();

        const prismaModels = Array.from(content.matchAll(/model\s+([A-Za-z0-9_]+)\s*\{([^}]*)\}/g));
        for (const m of prismaModels) {
          const lines = m[2].split('\n').filter((l) => l.trim() && !l.trim().startsWith('//') && !l.trim().startsWith('@@'));
          extractedTables.push({
            name: m[1],
            columnCount: Math.max(lines.length, 2),
            summary: `From ${filePath.split('/').pop()}`,
          });
        }

        const sqlTables = Array.from(
          content.matchAll(/CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`[]?([A-Za-z0-9_]+)["`\]]?/gi)
        );
        for (const m of sqlTables) {
          if (!extractedTables.some((t) => t.name.toLowerCase() === m[1].toLowerCase())) {
            extractedTables.push({
              name: m[1],
              columnCount: 5,
              summary: `SQL table in ${filePath.split('/').pop()}`,
            });
          }
        }

        const drizzleTables = Array.from(
          content.matchAll(/(?:pgTable|mysqlTable|sqliteTable)\(\s*['"]([A-Za-z0-9_]+)['"]/g)
        );
        for (const m of drizzleTables) {
          if (!extractedTables.some((t) => t.name.toLowerCase() === m[1].toLowerCase())) {
            extractedTables.push({
              name: m[1],
              columnCount: 6,
              summary: `Drizzle table in ${filePath.split('/').pop()}`,
            });
          }
        }
      } catch {
        // Ignore single raw file fetch failure
      }
    }

    if (extractedTables.length === 0) {
      const candidateFiles = schemaPaths.length > 0 ? schemaPaths : allBlobs.filter((p) => /\.(ts|js|cs|py|go|json)$/i.test(p));
      for (const p of candidateFiles.slice(0, 5)) {
        const base = (p.split('/').pop() || 'entity')
          .replace(/\.[a-z0-9]+$/i, '')
          .replace(/[^a-zA-Z0-9_]/g, '_')
          .toLowerCase();
        if (base && base !== 'index' && base !== 'package' && !extractedTables.some((t) => t.name === base)) {
          extractedTables.push({
            name: base.endsWith('s') ? base : `${base}s`,
            columnCount: 4,
            summary: `Inferred from ${p}`,
          });
        }
      }
    }

    return {
      repoSlug,
      branch: activeBranch,
      format: detectedFormat,
      parsedFiles: filesToInspect,
      tables: extractedTables.slice(0, 6),
      totalTables: extractedTables.length,
    };
  }
}

type MiniZeroSourceMode = 'code' | 'json';

function parseMiniZeroSource(
  mode: MiniZeroSourceMode,
  rawText: string
): { tables: { name: string; fields: string[] }[]; totalCount: number } {
  const text = rawText.trim();
  if (!text) return { tables: [], totalCount: 0 };

  if (mode === 'json') {
    try {
      const parsed = JSON.parse(text) as Record<string, unknown>;
      const tables: { name: string; fields: string[] }[] = [];
      const rootKeys = Object.keys(parsed);
      const topScalars: string[] = [];

      for (const k of rootKeys) {
        const val = parsed[k];
        if (Array.isArray(val) && val.length > 0 && typeof val[0] === 'object' && val[0] !== null) {
          tables.push({
            name: k.toLowerCase(),
            fields: Object.keys(val[0] as Record<string, unknown>).slice(0, 6),
          });
        } else if (typeof val === 'object' && val !== null && !Array.isArray(val)) {
          tables.push({
            name: k.endsWith('s') ? k.toLowerCase() : `${k.toLowerCase()}s`,
            fields: Object.keys(val as Record<string, unknown>).slice(0, 6),
          });
        } else {
          topScalars.push(k);
        }
      }
      if (topScalars.length > 0 || tables.length === 0) {
        tables.unshift({
          name: 'records',
          fields: topScalars.length > 0 ? topScalars.slice(0, 6) : ['id', 'payload', 'created_at'],
        });
      }
      return { tables: tables.slice(0, 4), totalCount: tables.length };
    } catch {
      throw new Error('Invalid JSON syntax. Paste a valid JSON object or array.');
    }
  }

  // Code mode: Prisma / SQL / TypeScript / C#
  const results: { name: string; fields: string[] }[] = [];
  const prismaMatches = Array.from(text.matchAll(/model\s+([A-Za-z0-9_]+)\s*\{([^}]*)\}/g));
  for (const m of prismaMatches) {
    const fields = m[2]
      .split('\n')
      .map((l) => l.trim().split(/\s+/)[0])
      .filter((f) => f && !f.startsWith('//') && !f.startsWith('@'));
    results.push({ name: m[1], fields: fields.slice(0, 6) });
  }

  const sqlMatches = Array.from(
    text.matchAll(/CREATE\s+TABLE\s+(?:IF\s+NOT\s+EXISTS\s+)?["`[]?([A-Za-z0-9_]+)["`\]]?\s*\(([^;]+)\)/gi)
  );
  for (const m of sqlMatches) {
    const fields = m[2]
      .split(',')
      .map((l) => l.trim().split(/\s+/)[0]?.replace(/["`[\]]/g, ''))
      .filter((f) => f && !/^(PRIMARY|FOREIGN|CONSTRAINT|CHECK|UNIQUE)$/i.test(f));
    results.push({ name: m[1], fields: fields.slice(0, 6) });
  }

  if (results.length === 0) {
    const fallback = extractTablesFromPrompt(text);
    return {
      tables: fallback.tables.map((t) => ({
        name: t.name,
        fields: t.columns.map((c) => c.name),
      })),
      totalCount: fallback.totalDetected,
    };
  }

  return { tables: results.slice(0, 4), totalCount: results.length };
}

type McpIdeKey = 'Claude' | 'Codex' | 'Cursor' | 'VS Code' | 'Windsurf' | 'Antigravity';

interface McpIdeEntry {
  key: McpIdeKey;
  icon: ReactNode;
  configPath: string;
  code: string;
  prompt: string;
  steps: { tool: string; summary: string }[];
  reply: string;
}

const MCP_IDES: McpIdeEntry[] = [
  {
    key: 'Claude',
    icon: <ClaudeBrandIcon />,
    configPath: 'Terminal CLI / claude_desktop_config.json',
    code: `# Claude Code CLI (Zero .NET install required)
claude mcp add namines -- npx -y @namines/mcp

# Or in claude_desktop_config.json:
{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}`,
    prompt: 'Add multi-tenant Stripe subscriptions with invoice ledgers, verify impact against production, and compile PostgreSQL 17 DDL.',
    steps: [
      { tool: 'namines_introspect_workspace', summary: 'Scanned 12 existing tables and 16 foreign keys in schema.ir.json' },
      { tool: 'namines_analyze_impact', summary: 'Risk: SAFE (0 destructive drops, 0 exclusive table rewrite locks)' },
      { tool: 'namines_compile_ddl', summary: 'Generated deterministic PostgreSQL 17 & SQLite Strict migrations' },
      { tool: 'namines_verify_sandbox', summary: 'Executed DDL in ephemeral container in 38ms with zero errors' },
    ],
    reply: 'Subscriptions and invoice_ledgers tables are verified and written to migrations/005_billing.sql.',
  },
  {
    key: 'Codex',
    icon: <CodexBrandIcon />,
    configPath: '.vscode/mcp.json · Codex CLI',
    code: `{
  "servers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}`,
    prompt: 'Refactor users.role into a dedicated rbac_permissions table without breaking existing foreign keys.',
    steps: [
      { tool: 'namines_analyze_impact', summary: 'Detected 3 dependent views and 2 SDK queries referencing users.role' },
      { tool: 'namines_plan_migration', summary: 'Created two-phase expand-and-contract migration script' },
      { tool: 'namines_eject_sdk', summary: 'Updated TypeScript SDK and Drizzle ORM schema definitions' },
    ],
    reply: 'Expand-phase migration created safely. Legacy column preserved until backfill completes.',
  },
  {
    key: 'Cursor',
    icon: <CursorBrandIcon />,
    configPath: '.cursor/mcp.json',
    code: `{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}`,
    prompt: 'Sync our Prisma schema with the new order_reservations table and generate SQL Server 2022 T-SQL.',
    steps: [
      { tool: 'namines_import_code', summary: 'Parsed schema.prisma AST and normalized 8 relations' },
      { tool: 'namines_compile_ddl', summary: 'Compiled T-SQL with NEWSEQUENTIALID() and temporal constraints' },
      { tool: 'namines_lint_schema', summary: '0 missing primary keys, all foreign keys indexed' },
    ],
    reply: 'Prisma schema and T-SQL migration are synchronized with zero drift.',
  },
  {
    key: 'VS Code',
    icon: <VsCodeBrandIcon />,
    configPath: '.vscode/mcp.json',
    code: `{
  "servers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}`,
    prompt: 'Check if dropping column legacy_token on sessions will break production.',
    steps: [
      { tool: 'namines_analyze_impact', summary: 'HIGH RISK: Dropping sessions.legacy_token causes permanent data loss' },
      { tool: 'namines_scan_affected_code', summary: 'Found 2 references in src/auth/sessionMiddleware.ts' },
    ],
    reply: 'Blocked unsafe DROP COLUMN. Remove the 2 code references in sessionMiddleware.ts first.',
  },
  {
    key: 'Windsurf',
    icon: <WindsurfBrandIcon />,
    configPath: '~/.codeium/windsurf/mcp_config.json',
    code: `{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}`,
    prompt: 'Create a pgvector memory store for Cascade AI sessions with HNSW indexing.',
    steps: [
      { tool: 'namines_generate_schema', summary: 'Created agent_memories with VECTOR(1536) and JSONB metadata' },
      { tool: 'namines_compile_ddl', summary: 'Added HNSW vector_cosine_ops index and tenant FK' },
    ],
    reply: 'Vector schema generated and verified against PostgreSQL 17 pgvector.',
  },
  {
    key: 'Antigravity',
    icon: <AntigravityBrandIcon />,
    configPath: '.mcp.json',
    code: `{
  "mcpServers": {
    "namines": {
      "command": "npx",
      "args": ["-y", "@namines/mcp"]
    }
  }
}`,
    prompt: 'Verify all 6 SQL dialects for our multi-region inventory schema and export EF Core 8 entities.',
    steps: [
      { tool: 'namines_compile_ddl', summary: 'Verified PostgreSQL, MySQL, SQL Server, SQLite, Oracle, MariaDB' },
      { tool: 'namines_eject_sdk', summary: 'Exported C# DbContext and fluent configuration classes' },
    ],
    reply: 'All 6 dialects compiled cleanly and EF Core entities written to Infrastructure/Data.',
  },
];

type EjectTargetKey = 'Next.js Admin' | 'TypeScript SDK' | 'Prisma' | 'Drizzle' | 'EF Core 8';

const EJECT_TARGETS: Record<EjectTargetKey, { file: string; code: string }> = {
  'Next.js Admin': {
    file: 'app/admin/subscriptions/page.tsx',
    code: `export default async function SubscriptionsAdminPage() {
  const rows = await naminesClient.subscriptions.list({
    where: { status: 'active' },
    orderBy: { currentPeriodEnd: 'desc' },
  });

  return <DataTable table="subscriptions" columns={columns} data={rows} />;
}`,
  },
  'TypeScript SDK': {
    file: 'src/sdk/subscriptions.ts',
    code: `export interface Subscription {
  id: string;
  orgId: string;
  planTier: 'free' | 'pro' | 'team';
  status: string;
  currentPeriodEnd: string;
}

export const subscriptionsApi = {
  list: (filter?: Partial<Subscription>) => client.get<Subscription[]>('/subscriptions', { params: filter }),
  create: (input: Omit<Subscription, 'id'>) => client.post<Subscription>('/subscriptions', input),
};`,
  },
  Prisma: {
    file: 'prisma/schema.prisma',
    code: `model Subscription {
  id               String       @id @default(uuid()) @db.Uuid
  orgId            String       @map("org_id") @db.Uuid
  planTier         String       @map("plan_tier") @db.VarChar(32)
  status           String       @default("active") @db.VarChar(24)
  currentPeriodEnd DateTime     @map("current_period_end") @db.Timestamptz(6)
  organization     Organization @relation(fields: [orgId], references: [id], onDelete: Restrict)

  @@index([orgId])
  @@map("subscriptions")
}`,
  },
  Drizzle: {
    file: 'src/db/schema.ts',
    code: `export const subscriptions = pgTable('subscriptions', {
  id: uuid('id').defaultRandom().primaryKey(),
  orgId: uuid('org_id').notNull().references(() => organizations.id, { onDelete: 'restrict' }),
  planTier: varchar('plan_tier', { length: 32 }).notNull(),
  status: varchar('status', { length: 24 }).default('active').notNull(),
  currentPeriodEnd: timestamp('current_period_end', { withTimezone: true }).notNull(),
});`,
  },
  'EF Core 8': {
    file: 'Data/Configurations/SubscriptionConfiguration.cs',
    code: `public class SubscriptionConfiguration : IEntityTypeConfiguration<Subscription>
{
    public void Configure(EntityTypeBuilder<Subscription> builder)
    {
        builder.ToTable("subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PlanTier).HasMaxLength(32).IsRequired();
        builder.HasOne(x => x.Organization)
               .WithMany(o => o.Subscriptions)
               .HasForeignKey(x => x.OrgId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}`,
  },
};

type WorkflowAppKey = 'GitHub Bot' | 'Claude' | 'Codex' | 'Cursor';

const WORKFLOW_APPS: Record<
  WorkflowAppKey,
  {
    icon: ReactNode;
    stepTitle: string;
    files: { name: string; status: string }[];
    actions: { badge: string; text: string }[];
    noteTitle: string;
    noteBody: string;
    doneText: string;
  }
> = {
  'GitHub Bot': {
    icon: <GitHubBrandIcon />,
    stepTitle: '3/3 Automated pull request schema review on GitHub',
    files: [
      { name: 'PR-142 · feat/stripe-webhooks', status: 'Verified' },
      { name: 'migrations/004_subscriptions.sql', status: 'Safe' },
    ],
    actions: [
      { badge: '3-Way Merge', text: 'Compared base branch, PR branch, and live schema.ir.json' },
      { badge: 'PR Comment', text: 'Posted deterministic impact table directly on GitHub PR-142' },
    ],
    noteTitle: 'Namines GitHub Bot Report',
    noteBody:
      'Every pull request that modifies SQL or ORM files is automatically checked for breaking column drops, lock hazards, and dialect drift before merge.',
    doneText: 'Done. Pull request approved and ChangeRequest audit log recorded.',
  },
  Claude: {
    icon: <ClaudeBrandIcon />,
    stepTitle: '3/3 Autonomous schema design & verification in Claude',
    files: [
      { name: 'organizations_schema.sql', status: 'Verified' },
      { name: 'tenant_isolation_policy.sql', status: 'Verified' },
    ],
    actions: [
      { badge: 'MCP Skill', text: 'Enforced ON DELETE RESTRICT on billing ledgers automatically' },
      { badge: 'Sandbox', text: 'Executed live DDL test in ephemeral PostgreSQL container' },
    ],
    noteTitle: 'Claude Skill Execution',
    noteBody:
      'Claude uses the Namines MCP skill to validate every generated table against real database engines instead of guessing SQL syntax.',
    doneText: 'Done. Schema blueprint synchronized with Namines Studio canvas.',
  },
  Codex: {
    icon: <CodexBrandIcon />,
    stepTitle: '3/3 Full-stack SDK & ORM synchronization in Codex',
    files: [
      { name: 'prisma/schema.prisma', status: 'Synced' },
      { name: 'src/sdk/databaseClient.ts', status: 'Synced' },
    ],
    actions: [
      { badge: 'Code Scan', text: 'Detected 0 broken TypeScript queries across 18 repository files' },
      { badge: 'Eject', text: 'Generated strict Zod schemas and typed CRUD client' },
    ],
    noteTitle: 'Codex Automated Handoff',
    noteBody:
      'Whenever your schema changes, Codex regenerates your TypeScript SDK, Prisma, Drizzle, or EF Core models in lockstep.',
    doneText: 'Done. 19 eject targets updated with zero type drift.',
  },
  Cursor: {
    icon: <CursorBrandIcon />,
    stepTitle: '3/3 Local workspace migration guard in Cursor',
    files: [
      { name: '.cursorrules · namines-guard', status: 'Active' },
      { name: 'schema.ir.json', status: 'Verified' },
    ],
    actions: [
      { badge: 'Composer', text: 'Intercepted destructive ALTER TABLE and suggested safe expand migration' },
      { badge: 'Multi-Engine', text: 'Verified compatibility across PostgreSQL 17 and SQLite Strict' },
    ],
    noteTitle: 'Cursor Composer Guardrail',
    noteBody:
      'With .cursorrules and Universal MCP enabled, Cursor automatically runs impact analysis before writing any migration file.',
    doneText: 'Done. Safe migration generated and ready to commit.',
  },
};

export default function Hero() {
  const router = useRouter();
  const [activeTab, setActiveTab] = useState<DemoTabId>('prompt');
  const [demoActionsUsed, setDemoActionsUsed] = useState(0);

  // Tab 1: Prompt to DB (Starts empty, compiles live on right, redirects if too large)
  const [promptInput, setPromptInput] = useState('');
  const [selectedEngine, setSelectedEngine] = useState<SqlEngine>('PostgreSQL');
  const [outputView, setOutputView] = useState<'ddl' | 'columns'>('ddl');

  // Tab 2: Import GitHub (Clean placeholder on left, starts empty on right, scans live)
  const [githubUrlInput, setGithubUrlInput] = useState('');
  const [githubBranchInput, setGithubBranchInput] = useState('');
  const [isScanningGithub, setIsScanningGithub] = useState(false);
  const [githubScanError, setGithubScanError] = useState<string | null>(null);
  const [githubScanResult, setGithubScanResult] = useState<LiveGitHubScanPreview | null>(null);

  // Tab 3: Zero-Prompt Start (Clean miniature tester for Prisma/SQL & JSON)
  const [zeroMode, setZeroMode] = useState<MiniZeroSourceMode>('code');
  const [zeroInput, setZeroInput] = useState('');
  const [zeroError, setZeroError] = useState<string | null>(null);
  const [zeroTables, setZeroTables] = useState<{ name: string; fields: string[] }[] | null>(null);

  // Tab 4: Universal MCP
  const [selectedIde, setSelectedIde] = useState<McpIdeKey>('Claude');
  const [copiedMcp, setCopiedMcp] = useState(false);

  // Tab 5: SDK & Admin Eject
  const [selectedEject, setSelectedEject] = useState<EjectTargetKey>('Next.js Admin');

  // Tab 6: GitHub & AI Workflows
  const [activeWorkflowApp, setActiveWorkflowApp] = useState<WorkflowAppKey>('GitHub Bot');

  const currentIdeConfig = MCP_IDES.find((i) => i.key === selectedIde) ?? MCP_IDES[0];

  // Sayfadan çıkılırsa bekleyen yönlendirme iptal edilir (unmount sonrası router.push olmasın)
  const redirectTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => () => {
    if (redirectTimerRef.current) clearTimeout(redirectTimerRef.current);
  }, []);

  const redirectToNew = (promptText?: string, sourceId?: string, githubUrl?: string) => {
    if (typeof window !== 'undefined') {
      // sessionStorage gizli modda/engelliyken fırlatabilir; yönlendirme yine de çalışmalı
      try {
        if (sourceId) {
          sessionStorage.setItem('namines-demo-source', sourceId);
        }
        if (githubUrl?.trim()) {
          sessionStorage.setItem('namines-demo-github-url', githubUrl.trim());
        }
        const toSave = promptText?.trim() || promptInput.trim();
        if (toSave) {
          sessionStorage.setItem('namines-demo-prompt', toSave);
        }
      } catch {
        /* storage kullanılamıyor: demo bağlamı taşınmaz, /new yine açılır */
      }
    }
    router.push('/new');
  };

  // Live Prompt compilation when promptInput is non-empty
  const trimmedPrompt = promptInput.trim();
  const promptExtraction = trimmedPrompt ? extractTablesFromPrompt(trimmedPrompt) : null;
  const compiledDdl = promptExtraction
    ? compileMiniDdl(promptExtraction.tables, selectedEngine)
    : '';
  const compiledColumns = promptExtraction
    ? flattenMiniColumns(promptExtraction.tables)
    : [];

  const handlePromptChange = (val: string) => {
    setPromptInput(val);
    if (val.length > 220) {
      redirectToNew(val);
      return;
    }
    const check = extractTablesFromPrompt(val);
    if (val.trim().length > 40 && check.totalDetected > 4) {
      redirectToNew(val);
    }
  };

  const handleEngineSwitch = (eng: SqlEngine) => {
    if (demoActionsUsed >= 5 && trimmedPrompt) {
      redirectToNew(promptInput);
      return;
    }
    setDemoActionsUsed((p) => p + 1);
    setSelectedEngine(eng);
  };

  const handleLiveGithubScan = async () => {
    if (!githubUrlInput.trim()) return;
    setIsScanningGithub(true);
    setGithubScanError(null);
    setGithubScanResult(null);

    try {
      const preview = await scanGitHubRepoLive(githubUrlInput, githubBranchInput);
      // If repository is too large for miniature preview (> 6 tables), show preview and redirect to Studio
      if (preview.totalTables > 6) {
        setGithubScanResult({ ...preview, redirecting: true });
        redirectTimerRef.current = setTimeout(() => {
          redirectToNew('', 'github', githubUrlInput);
        }, 1100);
        return;
      }
      setGithubScanResult(preview);
    } catch (err) {
      setGithubScanError(err instanceof Error ? err.message : 'Could not scan repository.');
    } finally {
      setIsScanningGithub(false);
    }
  };

  const handleLiveZeroExtract = () => {
    const raw = zeroInput.trim();
    if (!raw) return;
    setZeroError(null);

    if (raw.length > 450) {
      redirectToNew('', zeroMode);
      return;
    }

    try {
      const parsed = parseMiniZeroSource(zeroMode, raw);
      if (parsed.totalCount > 4) {
        redirectToNew('', zeroMode);
        return;
      }
      setZeroTables(parsed.tables);
    } catch (err) {
      setZeroError(err instanceof Error ? err.message : 'Could not parse input.');
    }
  };

  const handleCopyMcp = () => {
    if (typeof navigator !== 'undefined' && navigator.clipboard) {
      navigator.clipboard.writeText(currentIdeConfig.code);
      setCopiedMcp(true);
      setTimeout(() => setCopiedMcp(false), 1800);
    }
  };

  return (
    <div className="relative w-full overflow-hidden bg-surface-900">
      {/* DeepL-style soft bluish-purple atmospheric aura */}
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-x-0 top-2 bottom-0"
        style={{ background: 'var(--namines-hero-aura)' }}
      />

      <section className="relative z-10 w-full max-w-[1180px] mx-auto px-4 sm:px-6 lg:px-8 pt-6 sm:pt-8 pb-12 sm:pb-16">
        {/* 1. Centered Hero Title & Subtitle */}
        <h1 className="text-3xl sm:text-4xl lg:text-[46px] font-normal tracking-tight text-content-primary text-center leading-[1.12]">
          AI solutions that govern your database
        </h1>

        <p className="text-sm sm:text-[16px] text-content-secondary max-w-[720px] mx-auto mt-3 text-center leading-[1.55]">
          Design deterministic schemas across six SQL dialects, import from GitHub or live databases, and connect your AI editors via Universal MCP.
        </p>

        {/* 2. Centered Segmented Pill Bar — Star Features */}
        <div className="mt-6 mb-4 flex justify-center">
          <div
            role="tablist"
            aria-label="Database platform features"
            className="inline-flex flex-wrap items-center justify-center gap-1 p-1 rounded-full bg-surface-800/90 border border-surface-600/60 shadow-xs"
          >
            {DEMO_TABS.map((tab) => {
              const isActive = activeTab === tab.id;
              return (
                <button
                  key={tab.id}
                  type="button"
                  role="tab"
                  aria-selected={isActive}
                  onClick={() => setActiveTab(tab.id)}
                  className={`px-5 py-2 rounded-full text-[14px] transition-all duration-200 cursor-pointer ${
                    isActive
                      ? 'bg-surface-900 text-content-primary font-semibold shadow-sm'
                      : 'text-content-secondary hover:text-content-primary font-medium'
                  }`}
                >
                  {tab.label}
                </button>
              );
            })}
          </div>
        </div>

        {/* 3. Main Hero Card — FIXED WIDTH & FIXED HEIGHT (lg:h-[460px]) across all tabs */}
        <div
          className="w-full lg:h-[460px] rounded-[20px] border border-surface-600/80 shadow-2xl p-6 sm:p-8 backdrop-blur-md flex flex-col justify-between overflow-hidden"
          style={{ background: 'var(--namines-hero-card-bg)' }}
        >
          {/* ── TAB 1: PROMPT TO DB (Miniature Live Prompt-to-DDL Tester) ── */}
          {activeTab === 'prompt' && (
            <div className="h-full flex flex-col justify-between gap-4">
              {/* Top Bar: All 6 SQL Engines */}
              <div className="flex flex-wrap items-center justify-between gap-3 shrink-0">
                <div className="flex items-center gap-2 text-[14px] font-semibold text-content-primary">
                  <span>Plain English Prompt</span>
                  <span className="text-content-muted">→</span>
                </div>

                <div className="flex flex-wrap items-center gap-1.5">
                  {ALL_ENGINES.map((eng) => (
                    <button
                      key={eng}
                      type="button"
                      onClick={() => handleEngineSwitch(eng)}
                      className={`px-3 py-1.5 rounded-[8px] text-xs font-semibold transition-colors cursor-pointer ${
                        selectedEngine === eng
                          ? 'bg-accent text-accent-on shadow-xs'
                          : 'bg-surface-900/85 text-content-secondary hover:text-content-primary border border-surface-600/60'
                      }`}
                    >
                      {eng}
                    </button>
                  ))}
                </div>
              </div>

              {/* Two Equal Side-by-Side Boxes with Fixed Height & Internal Scroll */}
              <div className="grid grid-cols-1 lg:grid-cols-2 gap-4 flex-1 min-h-0">
                {/* Left Box: Clean Placeholder Input + In-Place Tab Triggers */}
                <div className="rounded-[12px] bg-surface-900 border border-surface-600/70 shadow-sm p-5 flex flex-col justify-between h-full min-h-[220px] overflow-hidden">
                  <textarea
                    value={promptInput}
                    onChange={(e) => handlePromptChange(e.target.value)}
                    placeholder="Describe your database tables (e.g. organizations, subscriptions, invoices)..."
                    aria-label="Database prompt input"
                    className="w-full flex-1 resize-none bg-transparent text-[15px] text-content-primary placeholder:text-content-muted focus:outline-none leading-relaxed overflow-y-auto"
                  />

                  <div className="pt-3 mt-2 border-t border-surface-600/40 space-y-2.5 shrink-0">
                    <div className="flex flex-wrap items-center gap-1.5">
                      <span className="text-[11px] font-medium text-content-muted mr-1">
                        Try another source:
                      </span>
                      <button
                        type="button"
                        onClick={() => setActiveTab('github')}
                        className="inline-flex items-center gap-1.5 px-2.5 py-1 rounded-[6px] bg-surface-800 hover:bg-surface-700 border border-surface-600/70 text-xs font-semibold text-content-primary transition-colors cursor-pointer"
                      >
                        <GitHubBrandIcon />
                        <span>GitHub Repo</span>
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setZeroMode('code');
                          setActiveTab('sources');
                        }}
                        className="px-2.5 py-1 rounded-[6px] bg-surface-800 hover:bg-surface-700 border border-surface-600/70 text-xs font-semibold text-content-primary transition-colors cursor-pointer"
                      >
                        Prisma / SQL
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setZeroMode('json');
                          setActiveTab('sources');
                        }}
                        className="px-2.5 py-1 rounded-[6px] bg-surface-800 hover:bg-surface-700 border border-surface-600/70 text-xs font-semibold text-content-primary transition-colors cursor-pointer"
                      >
                        JSON
                      </button>
                      <Link
                        href="/demo"
                        className="px-2.5 py-1 rounded-[6px] bg-surface-800 hover:bg-surface-700 border border-surface-600/70 text-xs font-semibold text-content-primary transition-colors"
                      >
                        Blueprints
                      </Link>
                    </div>

                    <div className="flex items-center justify-between">
                      <span className="text-xs text-content-muted">
                        Max 4 tables in mini preview · Larger schemas open Studio
                      </span>
                      <span className="text-xs font-medium text-content-muted">
                        {promptInput.length} / 220
                      </span>
                    </div>
                  </div>
                </div>

                {/* Right Box: Starts Empty until user types, then shows live DDL/Columns */}
                <div className="rounded-[12px] bg-surface-900 border border-surface-600/70 shadow-sm p-5 flex flex-col justify-between h-full min-h-[220px] overflow-hidden">
                  <div className="flex items-center justify-between gap-2 pb-2.5 mb-2.5 border-b border-surface-600/40 shrink-0">
                    <div className="flex items-center gap-1.5">
                      <button
                        type="button"
                        onClick={() => setOutputView('ddl')}
                        className={`px-3 py-1 rounded-[6px] text-xs font-semibold transition-colors cursor-pointer ${
                          outputView === 'ddl'
                            ? 'bg-surface-800 text-content-primary'
                            : 'text-content-muted hover:text-content-primary'
                        }`}
                      >
                        {selectedEngine} DDL
                      </button>
                      <button
                        type="button"
                        onClick={() => setOutputView('columns')}
                        className={`px-3 py-1 rounded-[6px] text-xs font-semibold transition-colors cursor-pointer ${
                          outputView === 'columns'
                            ? 'bg-surface-800 text-content-primary'
                            : 'text-content-muted hover:text-content-primary'
                        }`}
                      >
                        Columns ({compiledColumns.length})
                      </button>
                    </div>
                    <span className="text-xs font-mono text-content-muted">
                      {selectedEngine}
                    </span>
                  </div>

                  <div className="flex-1 min-h-0 overflow-y-auto pr-1 flex flex-col">
                    {!promptExtraction ? (
                      <div className="flex-1 flex flex-col items-center justify-center text-center px-6">
                        <p className="text-sm text-content-secondary">
                          Type your tables on the left to preview live {selectedEngine} DDL here.
                        </p>
                        <p className="text-xs text-content-muted mt-1.5">
                          Complex prompts automatically open in Full Schema Studio.
                        </p>
                      </div>
                    ) : outputView === 'ddl' ? (
                      <pre className="w-full font-mono text-[12.5px] text-content-primary whitespace-pre-wrap leading-relaxed">
                        {compiledDdl}
                      </pre>
                    ) : (
                      <div className="divide-y divide-surface-600/40 border border-surface-600/50 rounded-[8px] overflow-hidden">
                        {compiledColumns.map((col, idx) => (
                          <div
                            key={`${col.table}-${col.name}-${idx}`}
                            className="px-3.5 py-2 flex items-center justify-between gap-3 text-xs"
                          >
                            <div className="flex items-center gap-2">
                              <span className="font-mono text-content-muted">{col.table}.</span>
                              <span className="font-mono font-semibold text-content-primary">
                                {col.name}
                              </span>
                              <span className="font-mono text-[11px] px-1.5 py-0.5 rounded-[4px] bg-surface-800 text-content-secondary">
                                {col.pgType}
                              </span>
                            </div>
                            <span className="font-mono text-[11px] text-content-muted truncate">
                              {col.rule}
                            </span>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  <div className="flex items-center justify-between pt-2.5 mt-2 border-t border-surface-600/40 text-xs text-content-muted shrink-0">
                    <span>
                      {promptExtraction
                        ? `${promptExtraction.tables.length} tables compiled`
                        : 'Waiting for input'}
                    </span>
                    <button
                      type="button"
                      onClick={() => redirectToNew(promptInput)}
                      className="font-semibold text-content-primary hover:underline cursor-pointer"
                    >
                      Open in Studio →
                    </button>
                  </div>
                </div>
              </div>

              {/* Bottom Centered Buttons */}
              <div className="flex flex-wrap items-center justify-center gap-3 shrink-0">
                <button
                  type="button"
                  onClick={() => redirectToNew(promptInput)}
                  className="tap-44 px-6 py-2.5 rounded-[8px] bg-accent hover:bg-accent-hover text-accent-on text-[14px] font-semibold transition-colors cursor-pointer shadow-xs"
                >
                  Open Schema Studio
                </button>
                <Link
                  href="/new"
                  className="tap-44 px-6 py-2.5 rounded-[8px] bg-surface-900 hover:bg-surface-800 border border-surface-600/80 text-content-primary text-[14px] font-semibold transition-colors shadow-xs"
                >
                  Ücretsiz deneyin
                </Link>
              </div>
            </div>
          )}

          {/* ── TAB 2: IMPORT GITHUB (Clean Left Input + Empty Right Panel that Fills on Scan) ── */}
          {activeTab === 'github' && (
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-stretch h-full min-h-0">
              {/* Left Column: Spacious & Clean Input with Placeholder Only */}
              <div className="lg:col-span-5 flex flex-col justify-between text-left py-2">
                <div>
                  <h2 className="text-2xl sm:text-[30px] font-normal text-content-primary leading-[1.2] tracking-tight">
                    Turn any GitHub repository into a living schema
                  </h2>
                  <p className="text-[14px] sm:text-[15px] text-content-secondary mt-3 leading-[1.6]">
                    Enter a public repository URL to inspect its Prisma, Drizzle, EF Core, or SQL migration files right here. Larger repositories automatically open in Studio.
                  </p>

                  <div className="mt-6 space-y-3">
                    <div className="flex items-center gap-2 px-3.5 py-3 rounded-[10px] bg-surface-900 border border-surface-600/80">
                      <GitHubBrandIcon />
                      <input
                        id="hero-github-url"
                        type="text"
                        value={githubUrlInput}
                        onChange={(e) => setGithubUrlInput(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') handleLiveGithubScan();
                        }}
                        placeholder="https://github.com/owner/repository"
                        aria-label="GitHub Repository URL"
                        className="w-full bg-transparent text-sm font-mono text-content-primary placeholder:text-content-muted focus:outline-none"
                      />
                    </div>

                    <div className="flex items-center gap-2">
                      <input
                        type="text"
                        value={githubBranchInput}
                        onChange={(e) => setGithubBranchInput(e.target.value)}
                        onKeyDown={(e) => {
                          if (e.key === 'Enter') handleLiveGithubScan();
                        }}
                        placeholder="Branch (optional, e.g. main)"
                        aria-label="Branch name"
                        className="flex-1 px-3.5 py-2.5 rounded-[10px] bg-surface-900 border border-surface-600/80 text-xs font-mono text-content-primary placeholder:text-content-muted focus:outline-none"
                      />
                      <button
                        type="button"
                        onClick={handleLiveGithubScan}
                        disabled={isScanningGithub || !githubUrlInput.trim()}
                        className="px-5 py-2.5 rounded-[10px] bg-accent hover:bg-accent-hover disabled:opacity-50 text-accent-on text-xs font-semibold transition-colors cursor-pointer shrink-0 shadow-xs"
                      >
                        {isScanningGithub ? 'Scanning...' : 'Scan Repo'}
                      </button>
                    </div>
                  </div>
                </div>

                <div className="flex flex-wrap items-center gap-3 mt-4">
                  <button
                    type="button"
                    onClick={() => redirectToNew('', 'github', githubUrlInput)}
                    className="tap-44 inline-flex items-center gap-2 px-5 py-2.5 rounded-[8px] bg-surface-900 hover:bg-surface-800 border border-surface-600/80 text-content-primary text-[14px] font-semibold transition-colors cursor-pointer shadow-xs"
                  >
                    <span>Open Full Importer →</span>
                  </button>
                </div>
              </div>

              {/* Right Column: Empty by default; populates when user scans a repository */}
              <div className="lg:col-span-7 flex flex-col h-full min-h-0">
                <div className="w-full h-full rounded-[14px] bg-surface-900 border border-surface-600/70 shadow-lg p-5 flex flex-col justify-between overflow-hidden">
                  <div className="flex items-center justify-between gap-2 pb-3 mb-3 border-b border-surface-600/50 shrink-0">
                    <div className="flex items-center gap-2 min-w-0">
                      <GitHubBrandIcon />
                      <span className="text-[13px] font-mono font-semibold text-content-primary truncate">
                        {githubScanResult ? githubScanResult.repoSlug : 'Repository Inspector'}
                      </span>
                      {githubScanResult && (
                        <span className="px-2 py-0.5 rounded-[4px] bg-surface-800 border border-surface-600 text-[11px] font-mono text-content-secondary">
                          {githubScanResult.branch}
                        </span>
                      )}
                    </div>
                    <span className="text-xs font-mono text-content-muted shrink-0">
                      {githubScanResult ? githubScanResult.format : 'Idle'}
                    </span>
                  </div>

                  <div className="flex-1 min-h-0 overflow-y-auto pr-1 flex flex-col">
                    {isScanningGithub ? (
                      <div className="flex-1 flex flex-col items-center justify-center text-center px-6">
                        <p className="text-sm font-medium text-content-primary">
                          Scanning repository tree and schema files...
                        </p>
                        <p className="text-xs text-content-muted mt-1">
                          Reading ORM models and SQL migrations without cloning
                        </p>
                      </div>
                    ) : githubScanError ? (
                      <div className="flex-1 flex flex-col items-center justify-center text-center px-6">
                        <p className="text-sm text-danger-text">{githubScanError}</p>
                      </div>
                    ) : !githubScanResult ? (
                      <div className="flex-1 flex flex-col items-center justify-center text-center px-6">
                        <p className="text-sm text-content-secondary">
                          Enter a public GitHub repository URL on the left and click Scan Repo.
                        </p>
                        <p className="text-xs text-content-muted mt-1.5">
                          Extracted files and tables will appear here immediately.
                        </p>
                      </div>
                    ) : (
                      <div className="space-y-3">
                        {githubScanResult.redirecting && (
                          <div className="p-3 rounded-[8px] bg-accent text-accent-on text-xs font-semibold">
                            Large repository detected ({githubScanResult.totalTables} tables) — Opening Full Schema Studio...
                          </div>
                        )}

                        <div>
                          <div className="text-[11px] font-mono uppercase tracking-wider font-semibold text-content-muted mb-1.5">
                            Scanned Files ({githubScanResult.parsedFiles.length})
                          </div>
                          <div className="rounded-[8px] border border-surface-600/60 bg-surface-800/40 divide-y divide-surface-600/40">
                            {githubScanResult.parsedFiles.map((file) => (
                              <div
                                key={file}
                                className="px-3.5 py-2 flex items-center justify-between text-xs font-mono text-content-primary"
                              >
                                <span className="truncate">{file}</span>
                              </div>
                            ))}
                          </div>
                        </div>

                        <div>
                          <div className="text-[11px] font-mono uppercase tracking-wider font-semibold text-content-muted mb-1.5">
                            Extracted Tables ({githubScanResult.tables.length})
                          </div>
                          <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                            {githubScanResult.tables.map((t) => (
                              <div
                                key={t.name}
                                className="p-2.5 rounded-[8px] bg-surface-800/60 border border-surface-600/60 flex flex-col justify-between"
                              >
                                <div className="flex items-center justify-between">
                                  <span className="font-mono text-xs font-semibold text-content-primary">
                                    {t.name}
                                  </span>
                                  <span className="text-[11px] font-mono text-content-muted">
                                    {t.columnCount} cols
                                  </span>
                                </div>
                                <span className="text-[11px] text-content-secondary mt-1 truncate">
                                  {t.summary}
                                </span>
                              </div>
                            ))}
                          </div>
                        </div>
                      </div>
                    )}
                  </div>

                  <div className="flex items-center justify-between pt-3 mt-2 border-t border-surface-600/40 text-xs text-content-muted shrink-0">
                    <span>
                      {githubScanResult
                        ? `${githubScanResult.totalTables} tables found`
                        : 'No repository loaded'}
                    </span>
                    <button
                      type="button"
                      onClick={() => redirectToNew('', 'github', githubUrlInput)}
                      className="font-semibold text-content-primary hover:underline cursor-pointer"
                    >
                      Open in Studio →
                    </button>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* ── TAB 3: ZERO-PROMPT START (Miniature Code & JSON Extractor) ── */}
          {activeTab === 'sources' && (
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-stretch h-full min-h-0">
              {/* Left Column: Paste Prisma/SQL or JSON to extract tables live */}
              <div className="lg:col-span-5 flex flex-col justify-between text-left py-1">
                <div className="flex flex-col flex-1 min-h-0">
                  <div className="flex items-center justify-between gap-2 mb-3 shrink-0">
                    <div className="flex items-center gap-1.5">
                      <button
                        type="button"
                        onClick={() => {
                          setZeroMode('code');
                          setZeroTables(null);
                          setZeroError(null);
                        }}
                        className={`px-3 py-1.5 rounded-[8px] text-xs font-semibold transition-colors cursor-pointer ${
                          zeroMode === 'code'
                            ? 'bg-accent text-accent-on'
                            : 'bg-surface-900 text-content-secondary hover:text-content-primary border border-surface-600/60'
                        }`}
                      >
                        Prisma / SQL
                      </button>
                      <button
                        type="button"
                        onClick={() => {
                          setZeroMode('json');
                          setZeroTables(null);
                          setZeroError(null);
                        }}
                        className={`px-3 py-1.5 rounded-[8px] text-xs font-semibold transition-colors cursor-pointer ${
                          zeroMode === 'json'
                            ? 'bg-accent text-accent-on'
                            : 'bg-surface-900 text-content-secondary hover:text-content-primary border border-surface-600/60'
                        }`}
                      >
                        Sample JSON
                      </button>
                    </div>

                    <div className="flex items-center gap-1.5">
                      <button
                        type="button"
                        onClick={() => redirectToNew('', 'db')}
                        className="px-2.5 py-1.5 rounded-[8px] bg-surface-900 hover:bg-surface-800 border border-surface-600/60 text-xs font-semibold text-content-primary transition-colors cursor-pointer"
                      >
                        Live DB →
                      </button>
                      <Link
                        href="/demo"
                        className="px-2.5 py-1.5 rounded-[8px] bg-surface-900 hover:bg-surface-800 border border-surface-600/60 text-xs font-semibold text-content-primary transition-colors"
                      >
                        Blueprints →
                      </Link>
                    </div>
                  </div>

                  <div className="rounded-[12px] bg-surface-900 border border-surface-600/70 p-4 flex-1 flex flex-col justify-between min-h-[180px]">
                    <textarea
                      value={zeroInput}
                      onChange={(e) => setZeroInput(e.target.value)}
                      placeholder={
                        zeroMode === 'json'
                          ? 'Paste a JSON payload (e.g. {"users": [{"id": 1, "email": "a@b.com"}]})...'
                          : 'Paste a Prisma model or CREATE TABLE statement...'
                      }
                      aria-label="Zero-prompt source input"
                      className="w-full flex-1 resize-none bg-transparent font-mono text-xs text-content-primary placeholder:text-content-muted focus:outline-none leading-relaxed overflow-y-auto"
                    />

                    <div className="flex items-center justify-between pt-3 mt-2 border-t border-surface-600/40 shrink-0">
                      <span className="text-[11px] text-content-muted">
                        {zeroInput.length} / 450 chars
                      </span>
                      <button
                        type="button"
                        onClick={handleLiveZeroExtract}
                        disabled={!zeroInput.trim()}
                        className="px-4 py-1.5 rounded-[8px] bg-accent hover:bg-accent-hover disabled:opacity-50 text-accent-on text-xs font-semibold transition-colors cursor-pointer"
                      >
                        Extract Preview
                      </button>
                    </div>
                  </div>
                </div>
              </div>

              {/* Right Column: Starts Empty until user clicks Extract Preview */}
              <div className="lg:col-span-7 flex flex-col h-full min-h-0">
                <div className="w-full h-full rounded-[14px] bg-surface-900 border border-surface-600/70 shadow-lg p-5 flex flex-col justify-between overflow-hidden">
                  <div className="flex items-center justify-between pb-3 mb-3 border-b border-surface-600/50 shrink-0">
                    <span className="text-xs font-mono uppercase tracking-wider font-semibold text-content-muted">
                      Extracted Relational Schema
                    </span>
                    <span className="text-xs font-mono text-content-secondary">
                      {zeroMode === 'json' ? 'JSON Shape Inference' : 'AST Code Parser'}
                    </span>
                  </div>

                  <div className="flex-1 min-h-0 overflow-y-auto pr-1 flex flex-col">
                    {zeroError ? (
                      <div className="flex-1 flex items-center justify-center text-center px-6">
                        <p className="text-sm text-danger-text">{zeroError}</p>
                      </div>
                    ) : !zeroTables ? (
                      <div className="flex-1 flex flex-col items-center justify-center text-center px-6">
                        <p className="text-sm text-content-secondary">
                          Paste {zeroMode === 'json' ? 'sample JSON' : 'Prisma or SQL code'} on the left and click Extract Preview.
                        </p>
                        <p className="text-xs text-content-muted mt-1.5">
                          Larger files automatically open in Full Studio.
                        </p>
                      </div>
                    ) : (
                      <div className="grid grid-cols-1 sm:grid-cols-2 gap-2.5">
                        {zeroTables.map((t) => (
                          <div
                            key={t.name}
                            className="p-3.5 rounded-[10px] bg-surface-800/60 border border-surface-600/60 flex flex-col justify-between"
                          >
                            <div className="flex items-center justify-between mb-2">
                              <span className="font-mono text-[13px] font-semibold text-content-primary">
                                {t.name}
                              </span>
                              <span className="text-[11px] font-mono text-content-muted">
                                {t.fields.length} cols
                              </span>
                            </div>
                            <div className="flex flex-wrap gap-1">
                              {t.fields.map((f) => (
                                <span
                                  key={f}
                                  className="px-2 py-0.5 rounded-[4px] bg-surface-900 border border-surface-600/60 font-mono text-[11px] text-content-secondary"
                                >
                                  {f}
                                </span>
                              ))}
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  <div className="flex items-center justify-between pt-3 mt-3 border-t border-surface-600/40 text-xs text-content-muted shrink-0">
                    <span>
                      {zeroTables ? `${zeroTables.length} tables extracted` : 'Waiting for input'}
                    </span>
                    <button
                      type="button"
                      onClick={() => redirectToNew('', zeroMode)}
                      className="font-semibold text-content-primary hover:underline cursor-pointer"
                    >
                      Open Full Importer in Studio →
                    </button>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* ── TAB 3: UNIVERSAL MCP (6 IDEs with Transparent Brand Icons + Comprehensive Agent Trace) ── */}
          {activeTab === 'mcp' && (
            <div className="h-full flex flex-col justify-between gap-4 min-h-0">
              {/* Top Bar: IDE Switcher Pills with Brand Icons */}
              <div className="flex flex-wrap items-center justify-between gap-2 shrink-0">
                <div className="flex flex-wrap items-center gap-1.5">
                  {MCP_IDES.map((ide) => {
                    const active = selectedIde === ide.key;
                    return (
                      <button
                        key={ide.key}
                        type="button"
                        onClick={() => setSelectedIde(ide.key)}
                        className={`inline-flex items-center gap-2 px-3 py-1.5 rounded-[8px] text-xs font-semibold transition-all cursor-pointer ${
                          active
                            ? 'bg-surface-900 text-content-primary border border-content-primary shadow-xs'
                            : 'bg-surface-900/75 text-content-secondary hover:text-content-primary border border-surface-600/60'
                        }`}
                      >
                        {ide.icon}
                        <span>{ide.key}</span>
                      </button>
                    );
                  })}
                </div>

                <span className="text-xs font-mono text-content-secondary hidden xl:inline">
                  npx -y @namines/mcp · Zero .NET Install
                </span>
              </div>

              {/* Two-Column Split with Fixed Height & Internal Scroll */}
              <div className="grid grid-cols-1 lg:grid-cols-12 rounded-[14px] overflow-hidden border border-surface-600/80 shadow-lg flex-1 min-h-0">
                {/* Left Column: Comprehensive Multi-Step IDE Agent Trace */}
                <div className="lg:col-span-7 bg-surface-900 p-5 flex flex-col justify-between border-b lg:border-b-0 lg:border-r border-surface-600/60 h-full min-h-0 overflow-hidden">
                  <div className="space-y-3 overflow-y-auto pr-1 flex-1 min-h-0">
                    {/* Developer Prompt Bubble */}
                    <div className="rounded-[10px] bg-surface-800/80 border border-surface-600/60 p-3.5">
                      <div className="flex items-center gap-2 mb-1">
                        {currentIdeConfig.icon}
                        <span className="text-xs font-semibold text-content-primary">
                          Developer in {currentIdeConfig.key}
                        </span>
                      </div>
                      <p className="text-[13px] text-content-primary leading-snug">
                        {currentIdeConfig.prompt}
                      </p>
                    </div>

                    {/* Multi-Step MCP Tool Execution Trace */}
                    <div className="rounded-[10px] bg-surface-800/40 border border-surface-600/50 p-3 space-y-2">
                      <div className="text-[11px] font-mono font-semibold text-content-muted uppercase tracking-wider">
                        Namines MCP Server · Tool Execution Trace
                      </div>
                      {currentIdeConfig.steps.map((st, idx) => (
                        <div
                          key={st.tool}
                          className="flex items-start gap-2.5 text-xs border-t border-surface-600/30 pt-2 first:border-t-0 first:pt-0"
                        >
                          <span className="font-mono text-[11px] px-1.5 py-0.5 rounded-[4px] bg-surface-800 text-content-primary font-semibold shrink-0">
                            {idx + 1}. {st.tool}
                          </span>
                          <span className="text-content-secondary leading-snug">
                            {st.summary}
                          </span>
                        </div>
                      ))}
                    </div>

                    {/* Final Agent Confirmation */}
                    <div className="rounded-[10px] bg-accent text-accent-on p-3.5 shadow-xs">
                      <p className="text-[13px] font-medium leading-snug">
                        {currentIdeConfig.reply}
                      </p>
                    </div>
                  </div>

                  <div className="flex items-center justify-between pt-2.5 mt-2 border-t border-surface-600/40 text-xs text-content-muted shrink-0">
                    <span>13 universal lifecycle tools active</span>
                    <Link href="/hub" className="font-semibold text-content-primary hover:underline">
                      Explore MCP Hub →
                    </Link>
                  </div>
                </div>

                {/* Right Column: Turnkey JSON / CLI Config Box */}
                <div className="lg:col-span-5 bg-surface-800 p-5 flex flex-col justify-between h-full min-h-0 overflow-hidden">
                  <div className="flex items-center justify-between gap-2 pb-3 mb-3 border-b border-surface-600/70 shrink-0">
                    <div className="flex items-center gap-2 min-w-0">
                      {currentIdeConfig.icon}
                      <span className="text-xs font-mono text-content-primary truncate">
                        {currentIdeConfig.configPath}
                      </span>
                    </div>
                    <button
                      type="button"
                      onClick={handleCopyMcp}
                      className="px-3 py-1 rounded-[6px] bg-surface-900 text-xs font-semibold text-content-primary hover:bg-surface-700 transition-colors cursor-pointer shrink-0"
                    >
                      {copiedMcp ? 'Copied' : 'Copy'}
                    </button>
                  </div>

                  <div className="flex-1 min-h-0 overflow-y-auto">
                    <pre className="font-mono text-[12.5px] text-content-primary whitespace-pre-wrap leading-relaxed">
                      {currentIdeConfig.code}
                    </pre>
                  </div>

                  <div className="pt-2.5 mt-2 border-t border-surface-600/50 text-xs text-content-secondary shrink-0">
                    Standalone binaries for Windows, macOS &amp; Linux fetched automatically via npx.
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* ── TAB 5: SDK & ADMIN EJECT (Full-Stack Code Generation Showcase) ── */}
          {activeTab === 'eject' && (
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-stretch h-full min-h-0">
              <div className="lg:col-span-5 flex flex-col justify-between text-left py-1">
                <div>
                  <h2 className="text-2xl sm:text-[30px] font-normal text-content-primary leading-[1.2] tracking-tight">
                    Export SDK, ORM, and Next.js Admin targets
                  </h2>
                  <p className="text-[14px] sm:text-[15px] text-content-secondary mt-4 leading-[1.6]">
                    Generate a working Next.js CRUD Admin Dashboard, typed TypeScript SDK, Prisma schema, Drizzle tables, EF Core 8 DbContext, Go structs, and Python SQLAlchemy models from one canonical schema.
                  </p>
                </div>

                <div className="flex flex-wrap items-center gap-3 mt-6">
                  <Link
                    href="/compile"
                    className="tap-44 px-5 py-3 rounded-[8px] bg-accent hover:bg-accent-hover text-accent-on text-[14px] font-semibold transition-colors shadow-xs"
                  >
                    Open Code Compiler
                  </Link>
                  <Link
                    href="/new"
                    className="tap-44 px-5 py-3 rounded-[8px] bg-surface-900 hover:bg-surface-800 border border-surface-600/80 text-content-primary text-[14px] font-semibold transition-colors shadow-xs"
                  >
                    Ücretsiz deneyin
                  </Link>
                </div>
              </div>

              <div className="lg:col-span-7 flex flex-col h-full min-h-0">
                <div className="w-full h-full rounded-[14px] bg-surface-900 border border-surface-600/70 shadow-lg p-5 flex flex-col justify-between overflow-hidden">
                  <div className="flex flex-wrap items-center justify-between gap-2 pb-3 mb-3 border-b border-surface-600/50 shrink-0">
                    <div className="flex flex-wrap items-center gap-1.5">
                      {(Object.keys(EJECT_TARGETS) as EjectTargetKey[]).map((target) => (
                        <button
                          key={target}
                          type="button"
                          onClick={() => setSelectedEject(target)}
                          className={`px-3 py-1 rounded-[6px] text-xs font-semibold transition-colors cursor-pointer ${
                            selectedEject === target
                              ? 'bg-accent text-accent-on'
                              : 'bg-surface-800 text-content-secondary hover:text-content-primary'
                          }`}
                        >
                          {target}
                        </button>
                      ))}
                      <Link
                        href="/compile"
                        title="View all code generation targets"
                        className="px-2.5 py-1 rounded-[6px] bg-surface-800 text-content-secondary hover:text-content-primary text-xs font-mono font-semibold transition-colors"
                      >
                        ...
                      </Link>
                    </div>
                    <span className="text-xs font-mono text-content-muted">
                      {EJECT_TARGETS[selectedEject].file}
                    </span>
                  </div>

                  <div className="flex-1 min-h-0 overflow-y-auto pr-1">
                    <pre className="w-full font-mono text-[12.5px] text-content-primary whitespace-pre-wrap leading-relaxed">
                      {EJECT_TARGETS[selectedEject].code}
                    </pre>
                  </div>

                  <div className="flex items-center justify-between pt-3 mt-3 border-t border-surface-600/40 text-xs text-content-muted shrink-0">
                    <span>Synchronized with canonical ir.json</span>
                    <Link
                      href="/compile"
                      className="font-semibold text-content-primary hover:underline"
                    >
                      Explore all targets →
                    </Link>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* ── TAB 5: GITHUB BOT & AI WORKFLOWS (Colored Mini Brand Icons + 3/3 Pipeline) ── */}
          {activeTab === 'workflows' && (
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-stretch h-full min-h-0">
              {/* Left Column: Clear Explanation + 4 App Switcher Buttons with Brand Icons */}
              <div className="lg:col-span-5 flex flex-col justify-between text-left py-1">
                <div>
                  <h2 className="text-2xl sm:text-[30px] font-normal text-content-primary leading-[1.2] tracking-tight">
                    Automated schema checks in GitHub &amp; AI editors
                  </h2>
                  <p className="text-[14px] sm:text-[15px] text-content-secondary mt-4 leading-[1.6]">
                    Whenever a pull request is opened on GitHub or an AI assistant modifies your data model, Namines automatically runs a 3-way schema diff, tests the DDL in an isolated container, and syncs your SDKs.
                  </p>
                </div>

                <div className="space-y-3 mt-4">
                  <div className="grid grid-cols-2 gap-2.5">
                    {(Object.keys(WORKFLOW_APPS) as WorkflowAppKey[]).map((app) => {
                      const active = activeWorkflowApp === app;
                      const cfg = WORKFLOW_APPS[app];
                      return (
                        <button
                          key={app}
                          type="button"
                          onClick={() => setActiveWorkflowApp(app)}
                          className={`py-2.5 px-3.5 rounded-[10px] text-[13px] font-semibold transition-all cursor-pointer border inline-flex items-center justify-center gap-2 ${
                            active
                              ? 'bg-surface-900 border-content-primary text-content-primary shadow-sm'
                              : 'bg-surface-900/75 border-surface-600/70 text-content-secondary hover:text-content-primary hover:bg-surface-900'
                          }`}
                        >
                          {cfg.icon}
                          <span>{app}</span>
                        </button>
                      );
                    })}
                  </div>

                  <div>
                    <Link
                      href="/demo"
                      className="inline-flex items-center gap-1.5 text-[13px] font-semibold text-content-primary hover:underline"
                    >
                      <span>Explore Namines Blueprints &amp; GitHub Bot →</span>
                    </Link>
                  </div>
                </div>
              </div>

              {/* Right Column: Fixed-Height 3/3 Automated Pipeline Card with Internal Scroll */}
              <div className="lg:col-span-7 flex flex-col h-full min-h-0">
                <div className="w-full h-full rounded-[14px] bg-surface-900 border border-surface-600/70 shadow-lg p-6 flex flex-col justify-between overflow-hidden">
                  <div className="flex items-center justify-between pb-3 mb-3 border-b border-surface-600/50 shrink-0">
                    <div className="flex items-center gap-2.5">
                      {WORKFLOW_APPS[activeWorkflowApp].icon}
                      <span className="text-[14px] font-semibold text-content-primary">
                        {WORKFLOW_APPS[activeWorkflowApp].stepTitle}
                      </span>
                    </div>
                    <div className="flex items-center gap-1.5 shrink-0">
                      <span className="w-4 h-1 rounded-full bg-content-primary" />
                      <span className="w-4 h-1 rounded-full bg-content-primary" />
                      <span className="w-4 h-1 rounded-full bg-content-primary" />
                    </div>
                  </div>

                  <div className="space-y-3 overflow-y-auto pr-1 flex-1 min-h-0">
                    <div className="rounded-[10px] border border-surface-600/60 bg-surface-800/40 divide-y divide-surface-600/40">
                      {WORKFLOW_APPS[activeWorkflowApp].files.map((f) => (
                        <div
                          key={f.name}
                          className="px-4 py-2.5 flex items-center justify-between text-xs"
                        >
                          <span className="font-mono font-semibold text-content-primary">
                            {f.name}
                          </span>
                          <span className="px-2 py-0.5 rounded-[4px] bg-surface-800 text-content-primary font-mono font-semibold text-[11px]">
                            {f.status}
                          </span>
                        </div>
                      ))}
                    </div>

                    <div className="space-y-2">
                      {WORKFLOW_APPS[activeWorkflowApp].actions.map((act) => (
                        <div
                          key={act.text}
                          className="flex items-center gap-2.5 text-xs text-content-secondary"
                        >
                          <span className="px-2 py-0.5 rounded-[4px] bg-surface-800 border border-surface-600 font-mono text-[11px] font-semibold text-content-primary shrink-0">
                            {act.badge}
                          </span>
                          <span>{act.text}</span>
                        </div>
                      ))}
                    </div>

                    <div className="rounded-[10px] border border-surface-600/60 bg-surface-800/30 p-3.5">
                      <div className="text-xs font-semibold text-content-primary mb-1">
                        {WORKFLOW_APPS[activeWorkflowApp].noteTitle}
                      </div>
                      <p className="text-xs text-content-secondary leading-relaxed">
                        {WORKFLOW_APPS[activeWorkflowApp].noteBody}
                      </p>
                    </div>
                  </div>

                  <div className="pt-3 mt-3 border-t border-surface-600/40 text-xs font-medium text-content-primary shrink-0">
                    {WORKFLOW_APPS[activeWorkflowApp].doneText}
                  </div>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* 4. Bottom Social Proof Line */}
        <p className="text-center text-sm sm:text-[16px] font-normal text-content-primary mt-8">
          Trusted by engineering teams across 6 production SQL engines
        </p>
      </section>
    </div>
  );
}
