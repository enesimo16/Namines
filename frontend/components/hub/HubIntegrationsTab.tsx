'use client';

import React, { useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  Terminal,
  Shield,
  Server,
  Zap,
  ExternalLink,
  ArrowUpRight,
  CodeXml,
  Bot,
  Copy,
  Check,
  Plus,
  Loader2,
  Power,
  RotateCcw,
} from 'lucide-react';
import type { HubRepoDetail } from '../../types/hub';
import { useToastStore } from '../../store/useToastStore';
import { useActiveProjectStore } from '../../store/useActiveProjectStore';

import { ModuleSymbol, type ModuleKind } from '../layout/ModuleLogo';

interface HubIntegrationsTabProps {
  repo: HubRepoDetail;
}

interface IntegrationItem {
  id: ModuleKind;
  name: string;
  description: string;
  badge: string;
  actionText: string;
  actionHref: string;
}

export function HubIntegrationsTab({ repo }: HubIntegrationsTabProps) {
  const router = useRouter();
  const { showToast } = useToastStore();
  const [copiedKey, setCopiedKey] = useState<string | null>(null);

  const copyToClipboard = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopiedKey(label);
    showToast(`${label} copied to clipboard!`, 'success');
    setTimeout(() => setCopiedKey(null), 2000);
  };

  const handleNavigate = (href: string) => {
    useActiveProjectStore.getState().setScopedHubRepo({
      id: repo.id,
      owner: repo.owner?.username || 'workspace',
      slug: repo.slug || repo.id,
      name: repo.name || 'Project Workspace',
    });
    router.push(href);
  };

  const gatewayUrl =
    typeof window !== 'undefined'
      ? `${window.location.origin}/api/gateway/${repo.id}`
      : `https://namines.app/api/gateway/${repo.id}`;

  const mcpCommand = `npx @namines/mcp --project=${repo.id}`;

  const integrations: IntegrationItem[] = [
    {
      id: 'desk',
      name: 'Namines Desk',
      description:
        'Hosted query console and schema administration studio for executing queries, inspecting live data, and testing migrations.',
      badge: 'Query Console',
      actionText: 'Open Desk Console',
      actionHref: `/desk?project=${encodeURIComponent(repo.id)}`,
    },
    {
      id: 'vault',
      name: 'Namines Vault',
      description:
        'Continuous Point-In-Time Recovery (PITR) engine with AES-256 encryption, verify sandbox, and instant schema state rollbacks.',
      badge: 'Continuous PITR',
      actionText: 'Open Project Vault',
      actionHref: `/vault?project=${encodeURIComponent(repo.id)}`,
    },
    {
      id: 'ground',
      name: 'Namines Ground',
      description:
        'Isolated ephemeral database containers for testing migrations and DDL changes safely before production.',
      badge: 'Docker Sandbox',
      actionText: 'Open Project Ground',
      actionHref: `/ground?project=${encodeURIComponent(repo.id)}`,
    },
    {
      id: 'flow',
      name: 'Namines Flow',
      description:
        'Event-driven database lifecycle triggers, automated webhook dispatches, and scheduled background workers.',
      badge: 'Trigger Automation',
      actionText: 'Open Project Flow',
      actionHref: `/flow?project=${encodeURIComponent(repo.id)}`,
    },
  ];

  return (
    <div className="w-full space-y-5 font-sans">
      {/* Integrations Grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {integrations.map((item) => {
          return (
            <div
              key={item.id}
              className="rounded-[var(--radius-card)] bg-surface-850 border border-line hover:border-line-strong p-4 sm:p-5 flex flex-col justify-between gap-4 shadow-xs transition-colors"
            >
              <div className="space-y-2.5">
                <div className="flex items-center justify-between gap-2">
                  <div className="flex items-center gap-2.5 min-w-0">
                    <ModuleSymbol module={item.id} size={28} />
                    <h3 className="text-sm font-semibold text-content-primary truncate">
                      {item.name}
                    </h3>
                  </div>

                  <span className="text-[11px] font-mono text-accent-text px-2 py-0.5 rounded-[var(--radius-control)] bg-accent/15 border border-accent/25 shrink-0 flex items-center gap-1">
                    <span className="w-1.5 h-1.5 rounded-full bg-accent-text" />
                    <span>{item.badge}</span>
                  </span>
                </div>

                <p className="text-xs text-content-secondary leading-relaxed">
                  {item.description}
                </p>
              </div>

              <div className="pt-2 border-t border-line/60 flex items-center justify-between gap-2">
                <span className="text-[11px] font-mono text-content-muted">
                  Platform Integrated
                </span>

                <button
                  type="button"
                  onClick={() => handleNavigate(item.actionHref)}
                  className="inline-flex items-center gap-1.5 px-3 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-surface-750 hover:bg-surface-700 text-content-primary border border-line transition-all shadow-xs cursor-pointer"
                >
                  <span>{item.actionText}</span>
                  <ArrowUpRight className="w-3.5 h-3.5 text-content-muted" />
                </button>
              </div>
            </div>
          );
        })}
      </div>

      {/* Gateway API & MCP Developer Endpoints */}
      <div className="rounded-[var(--radius-card)] bg-surface-850 border border-line p-5 space-y-4 shadow-xs">
        <div className="flex items-center gap-2.5">
          <div className="w-7 h-7 rounded-[var(--radius-control)] border border-line bg-surface-750 flex items-center justify-center text-accent-text shrink-0">
            <CodeXml className="w-3.5 h-3.5" />
          </div>
          <div>
            <h3 className="text-sm font-semibold text-content-primary">
              REST Gateway &amp; MCP Integration
            </h3>
            <p className="text-xs text-content-muted">
              Direct access endpoints for cursor, Claude desktop, and client integrations.
            </p>
          </div>
        </div>

        <div className="space-y-3">
          {/* REST Gateway URL */}
          <div className="space-y-1">
            <label className="text-[11px] font-mono text-content-secondary uppercase tracking-wider block">
              REST Gateway Base URL
            </label>
            <div className="flex items-center gap-2">
              <input
                type="text"
                readOnly
                value={gatewayUrl}
                className="flex-1 bg-surface-900 border border-line rounded-[var(--radius-control)] px-3 py-1.5 text-xs font-mono text-content-primary select-all focus:outline-hidden"
              />
              <button
                type="button"
                onClick={() => copyToClipboard(gatewayUrl, 'Gateway URL')}
                className="px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-750 hover:bg-surface-700 border border-line text-xs font-medium text-content-primary flex items-center gap-1.5 transition-colors cursor-pointer shrink-0"
              >
                {copiedKey === 'Gateway URL' ? (
                  <Check className="w-3.5 h-3.5 text-accent-text" />
                ) : (
                  <Copy className="w-3.5 h-3.5 text-content-muted" />
                )}
                <span>{copiedKey === 'Gateway URL' ? 'Copied' : 'Copy'}</span>
              </button>
            </div>
          </div>

          {/* MCP Command */}
          <div className="space-y-1">
            <label className="text-[11px] font-mono text-content-secondary uppercase tracking-wider block">
              Model Context Protocol (MCP) CLI Command
            </label>
            <div className="flex items-center gap-2">
              <input
                type="text"
                readOnly
                value={mcpCommand}
                className="flex-1 bg-surface-900 border border-line rounded-[var(--radius-control)] px-3 py-1.5 text-xs font-mono text-content-primary select-all focus:outline-hidden"
              />
              <button
                type="button"
                onClick={() => copyToClipboard(mcpCommand, 'MCP Command')}
                className="px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-750 hover:bg-surface-700 border border-line text-xs font-medium text-content-primary flex items-center gap-1.5 transition-colors cursor-pointer shrink-0"
              >
                {copiedKey === 'MCP Command' ? (
                  <Check className="w-3.5 h-3.5 text-accent-text" />
                ) : (
                  <Copy className="w-3.5 h-3.5 text-content-muted" />
                )}
                <span>{copiedKey === 'MCP Command' ? 'Copied' : 'Copy'}</span>
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}
