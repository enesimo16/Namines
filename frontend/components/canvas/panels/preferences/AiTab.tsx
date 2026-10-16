import React from 'react';
import { ArrowLeft, Save, Shield, SlidersHorizontal } from 'lucide-react';
import type { AiAdvancedSettings } from '../../../../store/useAIPolicyStore';
import { CustomSelect, cardClass, primaryBtnClass } from './ui';
import {
  autoIndexOptions, dbaSeverityOptions, docLevelOptions, fkActionOptions, maxTokensOptions,
  namingOptions, policyFields, policyOptions, promptStyleOptions, scaffoldOptions,
  seedDomainOptions, sqlPrettyOptions, tempOptions,
} from './options';
import type { AiSettings } from './useAiSettings';

const ADVANCED_FIELDS: { field: keyof AiAdvancedSettings; label: string; desc: string; options: { value: string; label: string }[] }[] = [
  { field: 'seedDomain', label: 'Seeding Mock Domain', desc: 'Vertical semantic context for mock table values.', options: seedDomainOptions },
  { field: 'docLevel', label: 'Documentation Technical Detail', desc: 'Complexity depth of generated READMEs & documentation.', options: docLevelOptions },
  { field: 'scaffoldVersion', label: 'Target Scaffolder Framework', desc: 'C# classes compiler compilation target framework.', options: scaffoldOptions },
  { field: 'dbaSeverity', label: 'DBA Diagnostic Severity', desc: 'Severity threshold filter for diagnostics reports.', options: dbaSeverityOptions },
  { field: 'temperature', label: 'AI Temperature (Creativity)', desc: 'Controls randomness vs structural correctness.', options: tempOptions },
  { field: 'promptStyle', label: 'Code Presentation Style', desc: 'Inline comments, naming density, and formatting.', options: promptStyleOptions },
  { field: 'namingConvention', label: 'SQL Schema Naming Standard', desc: 'Case formats applied to tables and columns.', options: namingOptions },
  { field: 'fkAction', label: 'Foreign Key Action Rule', desc: 'Referential integrity behavior on parent row delete.', options: fkActionOptions },
  { field: 'maxTokens', label: 'Context Output Token Limit', desc: 'Limits maximum text tokens in compiled files.', options: maxTokensOptions },
  { field: 'autoIndex', label: 'Auto Foreign Key Indexes', desc: 'Automatically suggest index DDL on relationships.', options: autoIndexOptions },
];

export default function AiTab({ ai }: { ai: AiSettings }) {
  return (
    <div className="space-y-6 animate-in fade-in duration-200">
      {!ai.showAdvancedScreen ? <RoutingScreen ai={ai} /> : <AdvancedScreen ai={ai} />}
    </div>
  );
}

function RoutingScreen({ ai }: { ai: AiSettings }) {
  const { localPolicy, setLocalPolicy } = ai;

  return (
    <div className="space-y-6">
      <form onSubmit={ai.savePolicy} className="space-y-6">
        <div className={`${cardClass} p-5 space-y-4`}>
          <div className="flex items-center justify-between border-b border-line pb-3 gap-4">
            <div className="flex items-center gap-2">
              <span className="w-2 h-2 rounded-full bg-accent-text shrink-0" />
              <h4 className="text-xs font-semibold text-content-primary uppercase tracking-wider">AI Services Engine Routing</h4>
            </div>
            <button
              type="button"
              onClick={() => ai.setShowAdvancedScreen(true)}
              className="flex items-center gap-1.5 text-xs font-medium text-content-secondary hover:text-content-primary bg-surface-700 hover:bg-surface-600 border border-line-strong px-2.5 py-1.5 rounded-[var(--radius-control)] shrink-0 transition-colors cursor-pointer"
            >
              <SlidersHorizontal className="w-3.5 h-3.5" />
              <span>Advanced Settings</span>
            </button>
          </div>

          {/* Cost Multiplier Legend */}
          <div className="p-3 bg-surface-900/60 border border-line rounded-[var(--radius-control)] text-xs space-y-2">
            <p className="text-[11px] font-medium text-content-muted uppercase tracking-wider">AI Cost Multiplier Legend</p>
            <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
              {[
                ['NAI v1 Flash', '0.5x'], ['NAI v1 Standard', '1x'], ['NAI v1 Pro', '2x'],
              ].map(([label, val]) => (
                <div key={label} className="p-2 bg-surface-800 border border-line rounded-[var(--radius-control)] flex items-center justify-between">
                  <span className="text-xs font-medium text-content-primary">{label}</span>
                  <span className="text-xs font-mono text-content-muted">{val}</span>
                </div>
              ))}
            </div>
          </div>

          <div className="divide-y divide-line">
            {policyFields.map((field) => (
              <div key={field.key} className="grid grid-cols-12 gap-4 items-center py-3.5">
                <div className="col-span-12 md:col-span-7 space-y-0.5">
                  <h5 className="text-xs font-semibold text-content-primary">{field.label}</h5>
                  <p className="text-[11px] text-content-muted leading-relaxed">
                    {field.key === 'dbaAnalysis'
                      ? 'Performance and security analyzer (falls back to local rules engine on Default/Namines).'
                      : field.key === 'schemaRevision'
                        ? 'AI-powered regional and global schema revision prompts (falls back to local rules on Default/Namines).'
                        : field.desc
                    }
                  </p>
                </div>
                <div className="col-span-12 md:col-span-5 flex justify-end">
                  <CustomSelect
                    value={localPolicy[field.key]}
                    onChange={(val) => setLocalPolicy({ ...localPolicy, [field.key]: val })}
                    options={policyOptions}
                    className="w-full max-w-[240px]"
                  />
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="flex justify-end">
          <button
            type="submit"
            disabled={ai.isLoading}
            className={`px-5 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer flex items-center justify-center gap-1.5 active:scale-95 ${primaryBtnClass}`}
          >
            <span>{ai.isLoading ? 'Saving...' : 'Save AI Routing'}</span>
          </button>
        </div>
      </form>
    </div>
  );
}

function AdvancedScreen({ ai }: { ai: AiSettings }) {
  const { advanced, setAdvancedField } = ai;

  return (
    <div className="space-y-6 animate-in fade-in duration-200">
      <div className="flex items-center justify-between border-b border-line pb-4 gap-4">
        <button
          type="button"
          onClick={() => ai.setShowAdvancedScreen(false)}
          className="flex items-center gap-2 px-3 py-1.5 text-xs font-medium text-content-secondary hover:text-content-primary bg-surface-700 hover:bg-surface-600 border border-line-strong rounded-[var(--radius-control)] transition-all cursor-pointer select-none active:scale-95"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>Back to AI Routing</span>
        </button>
        <h4 className="text-xs font-semibold text-content-primary uppercase tracking-wider">
          Advanced AI Tuning & Parameters
        </h4>
      </div>

      <div className="flex gap-2.5 p-3.5 bg-surface-800 border border-line rounded-[var(--radius-control)]">
        <Shield className="w-4 h-4 shrink-0 text-content-muted mt-0.5" />
        <div className="text-[11px] leading-relaxed text-content-secondary">
          <span className="font-semibold text-content-primary">Note:</span> Altering default prompt settings, delete action policies, or temperature variables can result in unexpected compilation failures or syntax differences.
        </div>
      </div>

      <form onSubmit={ai.saveAdvanced} className="space-y-6">
        <div className={`${cardClass} p-5`}>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
            {ADVANCED_FIELDS.map((f) => (
              <div key={f.label} className="space-y-1.5">
                <div className="flex flex-col">
                  <span className="text-xs font-medium text-content-primary">{f.label}</span>
                  <span className="text-[11px] text-content-muted mt-0.5">{f.desc}</span>
                </div>
                <CustomSelect value={advanced[f.field]} onChange={(val) => setAdvancedField(f.field, val)} options={f.options} className="w-full" />
              </div>
            ))}

            <div className="space-y-1.5 md:col-span-2">
              <div className="flex flex-col">
                <span className="text-xs font-medium text-content-primary">SQL Formatting (Pretty Print)</span>
                <span className="text-[11px] text-content-muted mt-0.5">Ensures code output block is parsed and formatted.</span>
              </div>
              <CustomSelect value={advanced.sqlPrettyPrint} onChange={(val) => setAdvancedField('sqlPrettyPrint', val)} options={sqlPrettyOptions} className="w-full" />
            </div>
          </div>
        </div>

        <div className="flex justify-end">
          <button
            type="submit"
            className={`px-5 py-2 text-xs rounded-[var(--radius-control)] transition-all cursor-pointer flex items-center justify-center gap-1.5 active:scale-95 ${primaryBtnClass}`}
          >
            <Save className="w-3.5 h-3.5" />
            <span>Save Advanced Settings</span>
          </button>
        </div>
      </form>
    </div>
  );
}
