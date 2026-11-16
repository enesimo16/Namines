'use client';

import React, { useEffect, useMemo, useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import {
  Code2, Loader2, Plus, Sparkles, Terminal, Trash2, X, Zap, ListTree,
} from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';
import { useToastStore } from '../../store/useToastStore';
import { useAIGateway } from '../../hooks/useAIGateway';
import { schemaService } from '../../services/api';
import { apiErrorStatus, errorMessage } from '../../lib/errors';
import type {
  SchemaEnum,
  SchemaStoredProcedure,
  SchemaStoredProcedureParameter,
  SchemaTrigger,
} from '../../types/schema';

type DrawerTab = 'procedures' | 'triggers' | 'enums';

const inputClass =
  'bg-surface-700/60 border border-line rounded-[var(--radius-control)] text-content-primary placeholder:text-content-muted focus:outline-none focus:ring-1 focus:ring-line-strong transition-colors';

const ENGINES = ['PostgreSQL', 'MSSQL', 'MySQL', 'MariaDB', 'Oracle', 'SQLite'] as const;

export default function ProgrammabilityDrawer() {
  const {
    schema,
    dbType,
    naiModel,
    applyRevision,
    addTrigger,
    updateTrigger,
    deleteTrigger,
    addStoredProcedure,
    updateStoredProcedure,
    deleteStoredProcedure,
    addEnum,
    updateEnum,
    deleteEnum,
  } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const { checkAccess } = useAIGateway();

  const [isOpen, setIsOpen] = useState(false);
  const [activeTab, setActiveTab] = useState<DrawerTab>('procedures');
  const [tableFilter, setTableFilter] = useState<string>('ALL');
  const [aiPrompt, setAiPrompt] = useState('');
  const [isGenerating, setIsGenerating] = useState(false);
  const [newEnumName, setNewEnumName] = useState('');
  const [newEnumValues, setNewEnumValues] = useState('');

  useEffect(() => {
    const handler = (e: Event) => {
      const detail = (e as CustomEvent<{ tab?: DrawerTab; tableId?: string }>).detail;
      if (detail?.tab) setActiveTab(detail.tab);
      if (detail?.tableId) setTableFilter(detail.tableId);
      else setTableFilter('ALL');
      setIsOpen(true);
    };
    window.addEventListener('namines:open-programmability', handler);
    return () => window.removeEventListener('namines:open-programmability', handler);
  }, []);

  const procedures = schema?.storedProcedures ?? [];
  const triggers = schema?.triggers ?? [];
  const enums = schema?.enums ?? [];
  const tables = schema?.tables ?? [];

  const filteredTriggers = useMemo(() => {
    if (tableFilter === 'ALL') return triggers;
    return triggers.filter(t => t.tableId === tableFilter);
  }, [triggers, tableFilter]);

  const handleGenerateWithAi = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!schema || !aiPrompt.trim()) return;
    if (tables.length === 0) {
      showToast('Add at least one table before generating triggers or procedures.', 'warning');
      return;
    }
    if (!checkAccess('AI Routine & Trigger Synthesis')) return;

    try {
      setIsGenerating(true);
      const updated = await schemaService.reviseSchema(
        tables,
        schema.relations,
        aiPrompt.trim(),
        naiModel,
        {
          triggers,
          storedProcedures: procedures,
          enums,
          dbType,
        }
      );
      applyRevision(updated);
      setAiPrompt('');
      showToast('AI synthesized routines and triggers onto the schema.', 'success');
    } catch (error) {
      if (apiErrorStatus(error) === 429) {
        showToast('Daily AI limit reached! Please upgrade your plan.', 'warning');
      } else {
        showToast(errorMessage(error, 'Failed to generate routines with AI.'), 'error');
      }
    } finally {
      setIsGenerating(false);
    }
  };

  const handleCreateProcedure = () => {
    const nextNum = procedures.length + 1;
    const name = `sp_routine_${nextNum}`;
    const sp: Omit<SchemaStoredProcedure, 'stableUuid'> = {
      id: name,
      name,
      targetEngine: dbType,
      parameters: [{ name: 'p_id', type: 'UUID' }],
      body:
        dbType === 'MSSQL'
          ? `CREATE OR ALTER PROCEDURE [dbo].[${name}]\n  @p_id UNIQUEIDENTIFIER\nAS\nBEGIN\n  SET NOCOUNT ON;\n  SELECT 1;\nEND;`
          : `CREATE OR REPLACE PROCEDURE ${name}(p_id UUID)\nLANGUAGE plpgsql\nAS $$\nBEGIN\n  -- Stored procedure logic\nEND;\n$$;`,
    };
    addStoredProcedure(sp);
  };

  const handleCreateTrigger = () => {
    const targetTable =
      tables.find(t => t.id === tableFilter) ?? tables[0];
    if (!targetTable) {
      showToast('Add a table first before creating a trigger.', 'warning');
      return;
    }
    const cleanName = targetTable.name.toLowerCase().replace(/[^a-z0-9_]/g, '_');
    const trgId = `trg_${cleanName}_${triggers.length + 1}`;
    const trg: Omit<SchemaTrigger, 'stableUuid'> = {
      id: trgId,
      tableId: targetTable.id,
      timing: 'After',
      event: 'Insert',
      targetEngine: dbType,
      body: `CREATE OR REPLACE FUNCTION fn_${trgId}() RETURNS TRIGGER AS $$\nBEGIN\n  RETURN NEW;\nEND;\n$$ LANGUAGE plpgsql;`,
    };
    addTrigger(trg);
  };

  const handleCreateEnum = () => {
    const clean = newEnumName.trim();
    if (!clean) {
      showToast('Enum name is required.', 'warning');
      return;
    }
    const parsedVals = newEnumValues
      .split(',')
      .map(v => v.trim())
      .filter(Boolean);
    addEnum({
      id: `enum_${clean.toLowerCase().replace(/[^a-z0-9_]/g, '_')}`,
      name: clean,
      values: parsedVals.length > 0 ? parsedVals : ['pending', 'active'],
    });
    setNewEnumName('');
    setNewEnumValues('');
  };

  const handleParamChange = (
    sp: SchemaStoredProcedure,
    idx: number,
    patch: Partial<SchemaStoredProcedureParameter>
  ) => {
    const nextParams = sp.parameters.map((p, i) => (i === idx ? { ...p, ...patch } : p));
    updateStoredProcedure({ ...sp, parameters: nextParams });
  };

  return (
    <Dialog.Root open={isOpen} onOpenChange={setIsOpen}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-scrim/60 backdrop-blur-sm z-drawer-backdrop data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0" />

        <Dialog.Content
          className="fixed top-0 right-0 h-full w-[520px] max-w-[95vw] bg-surface-800 border-l border-line-strong shadow-2xl z-drawer flex flex-col"
          aria-describedby="programmability-drawer-desc"
        >
          <Dialog.Description id="programmability-drawer-desc" className="sr-only">
            Manage database stored procedures, triggers, and custom enums
          </Dialog.Description>

          {/* Header */}
          <div className="px-5 py-3.5 border-b border-line shrink-0 flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Code2 className="w-4 h-4 text-accent-text" />
              <div>
                <Dialog.Title className="text-sm font-bold text-content-primary">
                  Routines, Triggers & Enums
                </Dialog.Title>
                <p className="text-micro text-content-muted">
                  Compiled directly into {dbType} DDL · {procedures.length} procedures, {triggers.length} triggers
                </p>
              </div>
            </div>
            <button
              type="button"
              onClick={() => setIsOpen(false)}
              aria-label="Close programmability drawer"
              className="p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer"
            >
              <X className="w-4 h-4" />
            </button>
          </div>

          {/* Inline AI Prompt Bar */}
          <form
            onSubmit={handleGenerateWithAi}
            className="px-5 py-3 border-b border-line bg-surface-900/60 shrink-0 space-y-2"
          >
            <label
              htmlFor="prog-ai-input"
              className="text-micro font-semibold text-content-secondary flex items-center gap-1.5"
            >
              <Sparkles className="w-3.5 h-3.5 text-accent-text" />
              <span>AI Routine & Trigger Synthesis</span>
            </label>
            <div className="flex items-center gap-2">
              <input
                id="prog-ai-input"
                type="text"
                value={aiPrompt}
                onChange={e => setAiPrompt(e.target.value)}
                disabled={isGenerating}
                placeholder="e.g. Create sp_monthly_revenue and an after-insert trigger on orders..."
                className={`flex-1 min-w-0 px-3 py-1.5 text-xs ${inputClass}`}
              />
              <button
                type="submit"
                disabled={isGenerating || !aiPrompt.trim()}
                className="px-3 py-1.5 text-xs font-semibold rounded-[var(--radius-control)] bg-content-primary hover:bg-content-primary-hover text-surface-900 disabled:opacity-40 flex items-center gap-1.5 shrink-0 cursor-pointer transition-colors"
              >
                {isGenerating ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Sparkles className="w-3.5 h-3.5" />}
                <span>Generate</span>
              </button>
            </div>
          </form>

          {/* Tabs */}
          <div className="px-5 pt-2.5 border-b border-line shrink-0 flex items-center gap-1">
            <button
              type="button"
              onClick={() => setActiveTab('procedures')}
              className={`px-3 py-2 text-xs font-semibold border-b-2 transition-colors flex items-center gap-1.5 cursor-pointer ${
                activeTab === 'procedures'
                  ? 'border-accent text-content-primary'
                  : 'border-transparent text-content-muted hover:text-content-primary'
              }`}
            >
              <Terminal className="w-3.5 h-3.5" />
              <span>Procedures ({procedures.length})</span>
            </button>
            <button
              type="button"
              onClick={() => setActiveTab('triggers')}
              className={`px-3 py-2 text-xs font-semibold border-b-2 transition-colors flex items-center gap-1.5 cursor-pointer ${
                activeTab === 'triggers'
                  ? 'border-accent text-content-primary'
                  : 'border-transparent text-content-muted hover:text-content-primary'
              }`}
            >
              <Zap className="w-3.5 h-3.5" />
              <span>Triggers ({triggers.length})</span>
            </button>
            <button
              type="button"
              onClick={() => setActiveTab('enums')}
              className={`px-3 py-2 text-xs font-semibold border-b-2 transition-colors flex items-center gap-1.5 cursor-pointer ${
                activeTab === 'enums'
                  ? 'border-accent text-content-primary'
                  : 'border-transparent text-content-muted hover:text-content-primary'
              }`}
            >
              <ListTree className="w-3.5 h-3.5" />
              <span>Enums ({enums.length})</span>
            </button>
          </div>

          {/* Body */}
          <div className="flex-1 overflow-y-auto p-5 space-y-4">
            {activeTab === 'procedures' && (
              <>
                <div className="flex items-center justify-between">
                  <p className="text-micro text-content-muted">
                    Stored procedures matching active engine <span className="font-mono text-content-secondary">{dbType}</span> are emitted in final SQL builds.
                  </p>
                  <button
                    type="button"
                    onClick={handleCreateProcedure}
                    className={`px-2.5 py-1.5 text-xs font-semibold flex items-center gap-1 ${inputClass} hover:border-line-strong cursor-pointer shrink-0`}
                  >
                    <Plus className="w-3.5 h-3.5" />
                    <span>New Procedure</span>
                  </button>
                </div>

                {procedures.length === 0 ? (
                  <div className="p-6 rounded-[var(--radius-card)] border border-dashed border-line text-center space-y-2">
                    <p className="text-xs text-content-secondary font-medium">No stored procedures yet.</p>
                    <p className="text-micro text-content-muted">
                      Ask AI above (e.g. &ldquo;Create stored procedure sp_reconcile_invoices&rdquo;) or click New Procedure.
                    </p>
                  </div>
                ) : (
                  <div className="space-y-3">
                    {procedures.map(sp => (
                      <div key={sp.id} className="p-3 rounded-[var(--radius-card)] bg-surface-700/70 border border-line space-y-2.5">
                        <div className="flex items-center gap-2">
                          <input
                            aria-label={`Procedure name ${sp.name}`}
                            value={sp.name}
                            onChange={e => updateStoredProcedure({ ...sp, name: e.target.value })}
                            className={`flex-1 min-w-0 px-2.5 py-1 text-xs font-mono font-semibold ${inputClass}`}
                          />
                          <select
                            aria-label={`Target engine for ${sp.name}`}
                            value={sp.targetEngine}
                            onChange={e => updateStoredProcedure({ ...sp, targetEngine: e.target.value })}
                            className={`px-2 py-1 text-micro font-mono ${inputClass}`}
                          >
                            {ENGINES.map(eng => (
                              <option key={eng} value={eng}>{eng}</option>
                            ))}
                          </select>
                          <button
                            type="button"
                            onClick={() => deleteStoredProcedure(sp.id)}
                            aria-label={`Delete procedure ${sp.name}`}
                            className="p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-danger-text hover:bg-danger-subtle transition-colors cursor-pointer"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>

                        {/* Parameters */}
                        <div className="space-y-1.5">
                          <div className="flex items-center justify-between">
                            <span className="text-micro font-semibold text-content-muted uppercase tracking-wider">Parameters</span>
                            <button
                              type="button"
                              onClick={() =>
                                updateStoredProcedure({
                                  ...sp,
                                  parameters: [...sp.parameters, { name: `p_arg_${sp.parameters.length + 1}`, type: 'VARCHAR(100)' }],
                                })
                              }
                              className="text-micro text-accent-text hover:text-content-primary cursor-pointer flex items-center gap-0.5"
                            >
                              <Plus className="w-3 h-3" /> Add param
                            </button>
                          </div>
                          {sp.parameters.map((param, idx) => (
                            <div key={idx} className="flex items-center gap-1.5">
                              <input
                                value={param.name}
                                onChange={e => handleParamChange(sp, idx, { name: e.target.value })}
                                placeholder="p_name"
                                className={`flex-1 min-w-0 px-2 py-1 text-micro font-mono ${inputClass}`}
                              />
                              <input
                                value={param.type}
                                onChange={e => handleParamChange(sp, idx, { type: e.target.value })}
                                placeholder="UUID / INT"
                                className={`w-28 px-2 py-1 text-micro font-mono ${inputClass}`}
                              />
                              <button
                                type="button"
                                onClick={() =>
                                  updateStoredProcedure({
                                    ...sp,
                                    parameters: sp.parameters.filter((_, i) => i !== idx),
                                  })
                                }
                                aria-label="Remove parameter"
                                className="p-1 text-content-muted hover:text-danger-text cursor-pointer"
                              >
                                <X className="w-3 h-3" />
                              </button>
                            </div>
                          ))}
                        </div>

                        <textarea
                          aria-label={`SQL body for ${sp.name}`}
                          rows={5}
                          value={sp.body}
                          onChange={e => updateStoredProcedure({ ...sp, body: e.target.value })}
                          className={`w-full px-2.5 py-2 text-micro font-mono ${inputClass} resize-y`}
                        />
                      </div>
                    ))}
                  </div>
                )}
              </>
            )}

            {activeTab === 'triggers' && (
              <>
                <div className="flex items-center justify-between gap-2">
                  <select
                    aria-label="Filter triggers by table"
                    value={tableFilter}
                    onChange={e => setTableFilter(e.target.value)}
                    className={`px-2.5 py-1.5 text-xs ${inputClass} flex-1 min-w-0`}
                  >
                    <option value="ALL">All tables ({triggers.length})</option>
                    {tables.map(t => (
                      <option key={t.id} value={t.id}>{t.name}</option>
                    ))}
                  </select>
                  <button
                    type="button"
                    onClick={handleCreateTrigger}
                    className={`px-2.5 py-1.5 text-xs font-semibold flex items-center gap-1 ${inputClass} hover:border-line-strong cursor-pointer shrink-0`}
                  >
                    <Plus className="w-3.5 h-3.5" />
                    <span>New Trigger</span>
                  </button>
                </div>

                {filteredTriggers.length === 0 ? (
                  <div className="p-6 rounded-[var(--radius-card)] border border-dashed border-line text-center space-y-2">
                    <p className="text-xs text-content-secondary font-medium">No triggers defined for this view.</p>
                    <p className="text-micro text-content-muted">
                      Use AI above (e.g. &ldquo;Add AFTER INSERT trigger on orders to decrease stock&rdquo;) or click New Trigger.
                    </p>
                  </div>
                ) : (
                  <div className="space-y-3">
                    {filteredTriggers.map(trg => (
                      <div key={trg.id} className="p-3 rounded-[var(--radius-card)] bg-surface-700/70 border border-line space-y-2">
                        <div className="flex items-center gap-1.5">
                          <input
                            aria-label={`Trigger id ${trg.id}`}
                            value={trg.id}
                            onChange={e => updateTrigger({ ...trg, id: e.target.value })}
                            className={`flex-1 min-w-0 px-2 py-1 text-xs font-mono font-semibold ${inputClass}`}
                          />
                          <button
                            type="button"
                            onClick={() => deleteTrigger(trg.id)}
                            aria-label={`Delete trigger ${trg.id}`}
                            className="p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-danger-text hover:bg-danger-subtle transition-colors cursor-pointer"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>

                        <div className="grid grid-cols-3 gap-1.5">
                          <select
                            aria-label={`Table for trigger ${trg.id}`}
                            value={trg.tableId}
                            onChange={e => updateTrigger({ ...trg, tableId: e.target.value })}
                            className={`px-2 py-1 text-micro font-mono ${inputClass}`}
                          >
                            {tables.map(t => (
                              <option key={t.id} value={t.id}>{t.name}</option>
                            ))}
                          </select>
                          <select
                            aria-label={`Timing for trigger ${trg.id}`}
                            value={trg.timing}
                            onChange={e => updateTrigger({ ...trg, timing: e.target.value as 'Before' | 'After' })}
                            className={`px-2 py-1 text-micro font-mono ${inputClass}`}
                          >
                            <option value="Before">BEFORE</option>
                            <option value="After">AFTER</option>
                          </select>
                          <select
                            aria-label={`Event for trigger ${trg.id}`}
                            value={trg.event}
                            onChange={e => updateTrigger({ ...trg, event: e.target.value as 'Insert' | 'Update' | 'Delete' })}
                            className={`px-2 py-1 text-micro font-mono ${inputClass}`}
                          >
                            <option value="Insert">INSERT</option>
                            <option value="Update">UPDATE</option>
                            <option value="Delete">DELETE</option>
                          </select>
                        </div>

                        <div className="flex items-center justify-between text-micro text-content-muted">
                          <span>Target Engine</span>
                          <select
                            aria-label={`Engine for trigger ${trg.id}`}
                            value={trg.targetEngine}
                            onChange={e => updateTrigger({ ...trg, targetEngine: e.target.value })}
                            className={`px-2 py-0.5 text-micro font-mono ${inputClass}`}
                          >
                            {ENGINES.map(eng => (
                              <option key={eng} value={eng}>{eng}</option>
                            ))}
                          </select>
                        </div>

                        <textarea
                          aria-label={`SQL body for trigger ${trg.id}`}
                          rows={4}
                          value={trg.body}
                          onChange={e => updateTrigger({ ...trg, body: e.target.value })}
                          className={`w-full px-2.5 py-2 text-micro font-mono ${inputClass} resize-y`}
                        />
                      </div>
                    ))}
                  </div>
                )}
              </>
            )}

            {activeTab === 'enums' && (
              <>
                <div className="p-3 rounded-[var(--radius-card)] bg-surface-700/50 border border-line space-y-2">
                  <div className="text-xs font-semibold text-content-primary">Add Enum Type</div>
                  <div className="flex items-center gap-2">
                    <input
                      value={newEnumName}
                      onChange={e => setNewEnumName(e.target.value)}
                      placeholder="order_status"
                      className={`w-36 px-2.5 py-1.5 text-xs font-mono ${inputClass}`}
                    />
                    <input
                      value={newEnumValues}
                      onChange={e => setNewEnumValues(e.target.value)}
                      placeholder="pending, paid, shipped"
                      className={`flex-1 min-w-0 px-2.5 py-1.5 text-xs font-mono ${inputClass}`}
                    />
                    <button
                      type="button"
                      onClick={handleCreateEnum}
                      className={`px-2.5 py-1.5 text-xs font-semibold flex items-center gap-1 ${inputClass} hover:border-line-strong cursor-pointer`}
                    >
                      <Plus className="w-3.5 h-3.5" /> Add
                    </button>
                  </div>
                </div>

                {enums.length === 0 ? (
                  <p className="text-micro text-content-muted text-center py-4">No custom enums defined yet.</p>
                ) : (
                  <div className="space-y-2.5">
                    {enums.map(en => (
                      <div key={en.name} className="p-3 rounded-[var(--radius-card)] bg-surface-700/70 border border-line space-y-2">
                        <div className="flex items-center justify-between gap-2">
                          <span className="text-xs font-mono font-bold text-content-primary">{en.name}</span>
                          <button
                            type="button"
                            onClick={() => deleteEnum(en.name)}
                            aria-label={`Delete enum ${en.name}`}
                            className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-danger-text hover:bg-danger-subtle cursor-pointer"
                          >
                            <Trash2 className="w-3.5 h-3.5" />
                          </button>
                        </div>
                        <input
                          aria-label={`Values for enum ${en.name}`}
                          value={en.values.join(', ')}
                          onChange={e =>
                            updateEnum(en.name, {
                              ...en,
                              values: e.target.value.split(',').map(v => v.trim()).filter(Boolean),
                            })
                          }
                          className={`w-full px-2.5 py-1.5 text-micro font-mono ${inputClass}`}
                        />
                      </div>
                    ))}
                  </div>
                )}
              </>
            )}
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
