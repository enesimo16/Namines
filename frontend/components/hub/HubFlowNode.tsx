'use client';

import React, { memo } from 'react';
import { Handle, Position, NodeProps } from '@xyflow/react';
import {
  Globe,
  Database,
  Terminal,
  Shield,
  Zap,
  Activity,
  Layers,
  Cpu,
  Workflow,
  Server,
  Key,
} from 'lucide-react';

export interface HubFlowNodeData {
  title: string;
  subtitle?: string;
  category: 'gateway' | 'engine' | 'flow' | 'vault' | 'desk' | 'ground' | 'stream' | 'auth';
  badge?: string;
  metrics?: { label: string; value: string }[];
  description?: string;
  isFocused?: boolean;
}

const CATEGORY_CONFIG: Record<
  HubFlowNodeData['category'],
  { label: string; icon: React.ComponentType<{ className?: string }> }
> = {
  gateway: { label: 'API Gateway', icon: Globe },
  engine: { label: 'Storage Engine', icon: Database },
  flow: { label: 'Automation Triggers', icon: Zap },
  vault: { label: 'Continuous Vault', icon: Shield },
  desk: { label: 'Desk Console', icon: Terminal },
  ground: { label: 'Ground Sandbox', icon: Server },
  stream: { label: 'CDC & Event Stream', icon: Activity },
  auth: { label: 'Identity & RBAC', icon: Key },
};

function HubFlowNodeComponent({ data }: NodeProps) {
  const nodeData = data as unknown as HubFlowNodeData;
  const config = CATEGORY_CONFIG[nodeData.category] || { label: 'Component', icon: Cpu };
  const Icon = config.icon;
  const isFocused = nodeData.isFocused;

  return (
    <div
      className={`relative w-[280px] rounded-[var(--radius-card)] bg-surface-850 border transition-all duration-150 shadow-md select-none p-3.5 ${
        isFocused
          ? 'border-accent ring-2 ring-accent/30 shadow-lg'
          : 'border-line hover:border-line-strong'
      }`}
    >
      <Handle
        type="target"
        position={Position.Left}
        id="target"
        className="!w-2.5 !h-2.5 !bg-accent-text !border !border-line-strong"
      />
      <Handle
        type="source"
        position={Position.Right}
        id="source"
        className="!w-2.5 !h-2.5 !bg-accent-text !border !border-line-strong"
      />

      {/* Top Meta Bar */}
      <div className="flex items-center justify-between gap-2 pb-2 mb-2 border-b border-line text-[11px] font-mono">
        <span className="text-content-muted flex items-center gap-1">
          <Workflow className="w-3 h-3 text-accent-text" />
          <span>{config.label}</span>
        </span>
        {nodeData.badge && (
          <span className="px-2 py-0.5 rounded-[var(--radius-control)] bg-surface-800 text-accent-text border border-line font-medium text-[10px]">
            {nodeData.badge}
          </span>
        )}
      </div>

      {/* Title & Icon */}
      <div className="flex items-start gap-2.5 mb-2">
        <div className="w-7 h-7 rounded-[var(--radius-control)] bg-surface-750 border border-line flex items-center justify-center text-accent-text shrink-0 mt-0.5">
          <Icon className="w-3.5 h-3.5" />
        </div>
        <div className="min-w-0">
          <h4 className="font-mono text-xs font-semibold text-content-primary truncate">
            {nodeData.title}
          </h4>
          {nodeData.subtitle && (
            <p className="text-[11px] text-content-secondary truncate">{nodeData.subtitle}</p>
          )}
        </div>
      </div>

      {/* Description */}
      {nodeData.description && (
        <p className="text-[11px] text-content-secondary leading-relaxed mb-3 line-clamp-2">
          {nodeData.description}
        </p>
      )}

      {/* Metrics Grid */}
      {nodeData.metrics && nodeData.metrics.length > 0 && (
        <div className="pt-2 border-t border-line/60 grid grid-cols-2 gap-2 text-[10px] font-mono">
          {nodeData.metrics.map((m, idx) => (
            <div key={idx} className="bg-surface-800/80 px-2 py-1 rounded-[var(--radius-control)] border border-line/50">
              <span className="text-content-muted block">{m.label}</span>
              <span className="text-content-primary font-semibold">{m.value}</span>
            </div>
          ))}
        </div>
      )}
    </div>
  );
}

export const HubFlowNode = memo(HubFlowNodeComponent);
