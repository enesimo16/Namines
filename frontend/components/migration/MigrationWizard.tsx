import React, { useState, useRef, useEffect } from 'react';
import type { DatabaseSchema } from '../../types/schema';
import {
  X,
  Upload,
  Check,
  ChevronRight,
  RefreshCw,
  AlertCircle,
  CheckCircle,
  Database,
  ArrowLeft,
  Terminal,
  AlertTriangle
} from 'lucide-react';
import { useSchemaStore, DbType } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { confirmDialog } from '../../store/useConfirmStore';
import { useReactFlow } from '@xyflow/react';
import { flowToSchema } from '../../lib/flowToSchema';
import { migrationService } from '../../services/api';
import { SchemaDiffResult, MigrationResult } from '../../types/migration';
import DiffViewer from './DiffViewer';
import MigrationCodeView from './MigrationCodeView';
import { useAIGateway } from '../../hooks/useAIGateway';
import { errorMessage } from '../../lib/errors';
import { useEscapeKey } from '../../hooks/useEscapeKey';

interface MigrationWizardProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function MigrationWizard({ isOpen, onClose }: MigrationWizardProps) {
  const { schema, loadFromSchema, dbType, setDbType, projectName } = useSchemaStore();
  const { getNodes, getEdges } = useReactFlow();
  const { checkAccess } = useAIGateway();

  const { projects, activeProjectId, setMigrationBaseline } = useProjectHistoryStore();
  const activeProject = projects.find(p => p.id === activeProjectId);
  const activeBranchName = activeProject?.currentBranch || 'main';
  const activeBranch = activeProject?.branches?.find(b => b.name === activeBranchName);
  const migrationBaseline = activeBranch?.migrationBaseline || activeProject?.migrationBaseline || null;

  const [step, setStep] = useState<1 | 2 | 3>(1);
  const [dbContextCode, setDbContextCode] = useState('');
  const [selectedDbType, setSelectedDbType] = useState<DbType>(dbType || 'MSSQL');
  const [isDragOver, setIsDragOver] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Overwrite warning states
  const [showOverwriteWarning, setShowOverwriteWarning] = useState(false);
  const [pendingParsedSchema, setPendingParsedSchema] = useState<DatabaseSchema | null>(null);

  // Results state
  const [diffResult, setDiffResult] = useState<SchemaDiffResult | null>(null);
  const [migrationResult, setMigrationResult] = useState<MigrationResult | null>(null);
  
  const fileInputRef = useRef<HTMLInputElement>(null);

  const confirmOverwrite = () => {
    if (pendingParsedSchema) {
      loadFromSchema(pendingParsedSchema);
      setMigrationBaseline(pendingParsedSchema);
      setStep(2);
      setShowOverwriteWarning(false);
      setPendingParsedSchema(null);
    }
  };

  // Migration her workspace'e (projeye) özgü olmalı: proje değişince component-local
  // durumu (dbContext kodu, diff, sonuç, adım) sıfırla ki başka projeye sızmasın.
  useEffect(() => {
    setDbContextCode('');
    setDiffResult(null);
    setMigrationResult(null);
    setStep(1);
    setError(null);
  }, [activeProjectId]);

  // Check if we already have a loaded old migration schema in store
  useEffect(() => {
    if (isOpen) {
      if (migrationBaseline) {
        setStep(2);
      } else {
        setStep(1);
      }
      setError(null);
    }
  }, [isOpen, migrationBaseline]);

  useEscapeKey(isOpen, onClose);

  if (!isOpen) return null;

  // Step 1: Parse DbContext.cs
  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      readFileContent(file);
    }
  };

  const readFileContent = (file: File) => {
    const reader = new FileReader();
    reader.onload = (e) => {
      const text = e.target?.result as string;
      setDbContextCode(text);
    };
    reader.readAsText(file);
  };

  const handleDragOver = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(true);
  };

  const handleDragLeave = () => {
    setIsDragOver(false);
  };

  const handleDrop = (e: React.DragEvent) => {
    e.preventDefault();
    setIsDragOver(false);
    const file = e.dataTransfer.files?.[0];
    if (file) {
      readFileContent(file);
    }
  };

  const handleParse = async () => {
    if (!dbContextCode.trim()) {
      setError('Please paste a DbContext code or upload a file.');
      return;
    }
    if (!checkAccess("DbContext Parser")) return;

    setLoading(true);
    setError(null);
    try {
      setDbType(selectedDbType);
      const parsedSchema = await migrationService.parseDbContext(dbContextCode, selectedDbType);
      
      if (!parsedSchema || !parsedSchema.tables || parsedSchema.tables.length === 0) {
        throw new Error('AI could not parse the code or the tables are empty. Please upload a valid DbContext configuration.');
      }

      // Check if the canvas already contains tables before loading the schema
      if (schema && schema.tables && schema.tables.length > 0) {
        setPendingParsedSchema(parsedSchema);
        setShowOverwriteWarning(true);
      } else {
        // Load it into canvas workspace
        loadFromSchema(parsedSchema);
        // Persist it as our baseline
        setMigrationBaseline(parsedSchema);
        setStep(2);
      }
    } catch (err) {
      setError(errorMessage(err, 'A problem occurred on the AI server while parsing DbContext.'));
    } finally {
      setLoading(false);
    }
  };

  const handleUseCurrentAsBaseline = () => {
    try {
      const currentSchema = flowToSchema(schema || { schemaId: '', name: projectName, tables: [], relations: [] }, getNodes(), getEdges());
      if (!currentSchema || !currentSchema.tables || currentSchema.tables.length === 0) {
        setError('No tables found on the canvas. Please design a diagram or load a schema first.');
        return;
      }
      
      // Clear C# code text
      setDbContextCode('');
      
      // Save canvas state as baseline in the IndexedDB store
      setMigrationBaseline(currentSchema);
      
      setStep(2);
      setError(null);
    } catch (err) {
      setError(errorMessage(err, 'An error occurred while saving the current schema as a baseline.'));
    }
  };

  // Step 2: Compare & Generate Migration
  const handleGenerate = async () => {
    if (!migrationBaseline) {
      setError('Old schema not found. Please go back to the first step and load a DbContext.');
      setStep(1);
      return;
    }
    if (!checkAccess("Migration Generator")) return;

    setLoading(true);
    setError(null);

    try {
      const oldSchema = migrationBaseline;
      // Construct current state from flow canvas
      const currentSchema = flowToSchema(schema || { schemaId: '', name: 'Current', tables: [], relations: [] }, getNodes(), getEdges());
      
      if (!currentSchema) {
        throw new Error('Could not extract current schema from canvas structure.');
      }

      // Call Calculate Diff
      const diff = await migrationService.calculateDiff(oldSchema, currentSchema, selectedDbType);
      setDiffResult(diff);

      // Call Generate Migration
      const migration = await migrationService.generateMigration(oldSchema, currentSchema, selectedDbType);
      setMigrationResult(migration);

      setStep(3);
    } catch (err) {
      setError(errorMessage(err, 'An error occurred while creating the migration.'));
    } finally {
      setLoading(false);
    }
  };

  const handleReset = async () => {
    const ok = await confirmDialog({
      title: 'Reset migration',
      message: 'Migration history will be reset. Are you sure?',
      confirmLabel: 'Reset',
      danger: true,
    });
    if (ok) {
      setMigrationBaseline(null);
      setDbContextCode('');
      setDiffResult(null);
      setMigrationResult(null);
      setStep(1);
      setError(null);
    }
  };

  return (
    <div className="fixed inset-0 z-modal flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-300">
      <div className="relative w-full max-w-2xl rounded-[var(--radius-modal)] bg-surface-800 border border-line-strong shadow-2xl p-5 md:p-6 flex flex-col max-h-[85vh] overflow-hidden">

        {/* Header */}
        <div className="flex justify-between items-start mb-5 pb-3 border-b border-line select-none">
          <div>
            <h2 className="text-sm font-bold text-content-primary">Migration Engine</h2>
            <p className="text-content-muted text-xs mt-1">
              Upload your DbContext, edit the schema on the canvas, and generate EF Core migration code.
            </p>
          </div>
          <button
            onClick={onClose}
            className="p-1.5 text-content-muted hover:text-content-primary hover:bg-surface-700 rounded-[var(--radius-control)] transition-all cursor-pointer"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Stepper */}
        <div className="flex items-center gap-3 mb-5 bg-surface-700/80 border border-line-strong p-2.5 rounded-[var(--radius-card)] select-none shadow-xs">
          <div className="flex items-center gap-2 flex-1 justify-center min-w-0">
            <div className={`w-6 h-6 rounded-full flex items-center justify-center text-[11px] font-bold shrink-0 shadow-xs ${step >= 1 ? 'bg-namines-teal-vibrant text-surface-900 font-bold' : 'bg-surface-600 text-content-muted'}`}>
              {step > 1 ? <Check className="w-3.5 h-3.5" /> : '1'}
            </div>
            <span className={`text-micro font-bold truncate ${step >= 1 ? 'text-content-primary' : 'text-content-muted'}`}>Upload DbContext</span>
          </div>
          <ChevronRight className="w-3.5 h-3.5 text-content-muted shrink-0" />
          <div className="flex items-center gap-2 flex-1 justify-center min-w-0">
            <div className={`w-6 h-6 rounded-full flex items-center justify-center text-[11px] font-bold shrink-0 shadow-xs ${step >= 2 ? 'bg-namines-teal-vibrant text-surface-900 font-bold' : 'bg-surface-600 text-content-muted'}`}>
              {step > 2 ? <Check className="w-3.5 h-3.5" /> : '2'}
            </div>
            <span className={`text-micro font-bold truncate ${step >= 2 ? 'text-content-primary' : 'text-content-muted'}`}>Edit on Canvas</span>
          </div>
          <ChevronRight className="w-3.5 h-3.5 text-content-muted shrink-0" />
          <div className="flex items-center gap-2 flex-1 justify-center min-w-0">
            <div className={`w-6 h-6 rounded-full flex items-center justify-center text-[11px] font-bold shrink-0 shadow-xs ${step >= 3 ? 'bg-namines-teal-vibrant text-surface-900 font-bold' : 'bg-surface-600 text-content-muted'}`}>
              3
            </div>
            <span className={`text-micro font-bold truncate ${step >= 3 ? 'text-content-primary' : 'text-content-muted'}`}>Generate Migration</span>
          </div>
        </div>

        {/* Errors */}
        {error && (
          <div className="bg-danger-subtle border border-danger/30 rounded-[var(--radius-card)] p-3 flex gap-2.5 items-start mb-5 shadow-xs">
            <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
            <div>
              <span className="text-danger-text text-xs font-bold block mb-0.5">Operation Failed</span>
              <p className="text-content-secondary text-xs leading-relaxed">{error}</p>
            </div>
          </div>
        )}

        {/* Dynamic Step Content */}
        <div className="flex-1 overflow-y-auto pr-1 min-h-[300px]">

          {/* STEP 1: UPLOAD/PASTE */}
          {step === 1 && (
            <div className="space-y-4">

              {/* Controls: DB Type */}
              <div className="flex items-center justify-between bg-surface-700/80 border border-line-strong p-3.5 rounded-[var(--radius-card)] shadow-xs">
                <div className="flex items-center gap-2.5">
                  <Database className="w-4 h-4 text-namines-teal-vibrant" />
                  <div>
                    <span className="text-content-primary text-xs font-bold block">Database Provider</span>
                    <span className="text-content-muted text-micro">Migration will be computed for this configuration.</span>
                  </div>
                </div>
                <select
                  aria-label="Database Provider"
                  value={selectedDbType}
                  onChange={(e) => setSelectedDbType(e.target.value as DbType)}
                  className="bg-surface-800 border border-line-strong rounded-[var(--radius-control)] px-3 py-1.5 text-xs font-semibold text-content-primary focus:outline-none focus:border-focus-ring shadow-xs cursor-pointer"
                >
                  <option value="MSSQL">Microsoft SQL Server</option>
                  <option value="PostgreSQL">PostgreSQL</option>
                  <option value="MySQL">MySQL</option>
                  <option value="SQLite">SQLite</option>
                  <option value="Oracle">Oracle</option>
                  <option value="MariaDB">MariaDB</option>
                </select>
              </div>

              {/* Option 2: Use Current Diagram as Baseline */}
              <div className="bg-surface-700/80 border border-line-strong rounded-[var(--radius-card)] p-4 shadow-xs flex flex-col md:flex-row items-center justify-between gap-3">
                <div className="flex items-start gap-3">
                  <CheckCircle className="w-4 h-4 text-namines-teal-vibrant mt-0.5 shrink-0" />
                  <div>
                    <span className="text-content-primary text-xs font-bold block">Set Current Canvas as Baseline</span>
                    <span className="text-content-muted text-micro leading-relaxed block mt-0.5">
                      No existing DbContext? Declare the current whiteboard design as the reference baseline — future canvas edits are compared to it.
                    </span>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={handleUseCurrentAsBaseline}
                  className="bg-surface-700 hover:bg-surface-600 text-content-primary border border-line-strong text-xs font-bold px-4 py-2 rounded-[var(--radius-control)] transition-all shrink-0 flex items-center gap-1.5 cursor-pointer shadow-xs"
                >
                  <Check className="w-3.5 h-3.5 text-namines-teal-vibrant" />
                  <span>Set as Baseline</span>
                </button>
              </div>

              {/* Drag n Drop Zone */}
              <div
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                onClick={() => fileInputRef.current?.click()}
                className={`border border-dashed rounded-[var(--radius-card)] p-6 flex flex-col items-center justify-center gap-2.5 cursor-pointer transition-all duration-300 ${isDragOver ? 'border-focus-ring bg-accent-subtle/30' : 'border-line-strong hover:border-line-strong hover:bg-surface-700/60 bg-surface-700/40 shadow-xs'}`}
              >
                <input
                  type="file"
                  ref={fileInputRef}
                  onChange={handleFileChange}
                  accept=".cs"
                  className="hidden"
                />
                <div className="w-10 h-10 bg-surface-700 border border-line-strong text-namines-teal-vibrant flex items-center justify-center rounded-[var(--radius-card)] shadow-xs">
                  <Upload className="w-4 h-4" />
                </div>
                <div className="text-center">
                  <span className="text-content-primary text-xs font-bold block">Drag and drop your DbContext.cs file here</span>
                  <span className="text-content-muted text-micro mt-0.5 block">or click to browse (.cs source file)</span>
                </div>
              </div>

              {/* Text Area for raw pasting */}
              <div className="space-y-1.5">
                <span className="text-content-muted text-micro font-bold uppercase tracking-wider block">Or paste your DbContext C# source code directly:</span>
                <div className="relative rounded-[var(--radius-card)] overflow-hidden border border-line-strong bg-surface-700/80 shadow-xs">
                  <textarea
                    aria-label="DbContext C# source code"
                    value={dbContextCode}
                    onChange={(e) => setDbContextCode(e.target.value)}
                    placeholder="public class AppDbContext : DbContext { ..."
                    className="w-full h-40 bg-transparent text-xs font-mono text-content-primary p-3.5 focus:outline-none resize-none leading-relaxed"
                  />
                  <div className="absolute bottom-2.5 right-2.5 flex items-center gap-1.5 bg-surface-800 border border-line px-2.5 py-1 rounded-[var(--radius-control)] select-none shadow-xs">
                    <Terminal className="w-3 h-3 text-content-muted" />
                    <span className="text-content-muted text-[10px] font-bold uppercase">C# Source</span>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* STEP 2: CANVAS EDIT GUIDANCE */}
          {step === 2 && (
            <div className="space-y-5 flex flex-col items-center justify-center py-4">
              <div className="w-12 h-12 bg-surface-700 border border-line-strong text-namines-teal-vibrant flex items-center justify-center rounded-[var(--radius-card)] shadow-xs">
                <CheckCircle className="w-5 h-5" />
              </div>
              <div className="text-center max-w-md select-none">
                <h3 className="text-sm font-bold text-content-primary mb-1.5">
                  {dbContextCode.trim() ? 'DbContext Successfully Parsed!' : 'Baseline Schema Successfully Saved!'}
                </h3>
                <p className="text-content-muted text-xs leading-relaxed">
                  {dbContextCode.trim()
                    ? 'Your C# DbContext has been parsed and your schema tables are loaded onto the canvas.'
                    : 'Your current canvas layout has been saved as the reference baseline schema.'}
                </p>
              </div>

              {/* Guidance Box */}
              <div className="bg-surface-700/80 border border-line-strong rounded-[var(--radius-card)] p-4 w-full space-y-2.5 shadow-xs">
                <div className="flex items-center gap-2 text-content-primary text-xs font-bold">
                  <AlertTriangle className="w-3.5 h-3.5 text-namines-teal-vibrant" />
                  <span>What Should I Do Next?</span>
                </div>
                <ol className="list-decimal pl-5 space-y-2 text-xs text-content-muted leading-relaxed">
                  <li>Close this window and head to the interactive canvas.</li>
                  <li>Modify the schema as desired (add/delete tables, edit columns, configure relationships).</li>
                  <li>Once your updates are complete, click <strong className="text-content-primary">&quot;Migration&quot;</strong> in the bottom toolkit again.</li>
                  <li>Click <strong className="text-content-primary">&quot;Compare & Generate Migration&quot;</strong> below for your EF Core code.</li>
                </ol>
              </div>
            </div>
          )}

          {/* STEP 3: DIFF & MIGRATION GENERATED */}
          {step === 3 && (
            <div className="space-y-5">
              {diffResult && (
                <div className="space-y-2">
                  <span className="text-content-subtle text-xs font-bold block">1. Detected Schema Changes (Diff)</span>
                  <DiffViewer diff={diffResult} />
                </div>
              )}

              {migrationResult && (
                <div className="space-y-2 pt-3 border-t border-content-primary/8">
                  <span className="text-content-subtle text-xs font-bold block">2. Generated EF Core Migration Code</span>
                  <MigrationCodeView
                    migration={migrationResult}
                    hasBreakingChanges={diffResult?.hasBreakingChanges || false}
                  />
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer Buttons */}
        <div className="border-t border-line pt-4 mt-4 flex justify-between gap-3 items-center">
          <div>
            {step === 2 && (
              <button
                onClick={handleReset}
                className="text-xs text-danger-text hover:text-danger-text font-semibold flex items-center gap-1.5 bg-danger-subtle hover:bg-danger/25 border border-danger/30 px-3 py-2 rounded-[var(--radius-control)] transition-all shadow-xs cursor-pointer"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                <span>Reset Baseline</span>
              </button>
            )}
            {step === 3 && (
              <button
                onClick={() => setStep(2)}
                className="text-xs text-content-muted hover:text-content-primary font-semibold flex items-center gap-1.5 bg-surface-700 hover:bg-surface-600 border border-line-strong px-3 py-2 rounded-[var(--radius-control)] transition-all shadow-xs cursor-pointer"
              >
                <ArrowLeft className="w-3.5 h-3.5" />
                <span>Go Back</span>
              </button>
            )}
          </div>

          <div className="flex gap-2">
            {step === 1 && (
              <button
                onClick={handleParse}
                disabled={loading || !dbContextCode.trim()}
                className="flex items-center gap-2 bg-namines-teal-vibrant hover:brightness-105 active:scale-95 disabled:bg-surface-600 disabled:text-content-muted text-surface-900 text-xs font-bold px-5 py-2 rounded-[var(--radius-control)] transition-all disabled:cursor-not-allowed cursor-pointer shadow-xs"
              >
                {loading ? (
                  <>
                    <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                    <span>Analyzing DbContext...</span>
                  </>
                ) : (
                  <>
                    <span>Load Schema & Begin</span>
                    <ChevronRight className="w-3.5 h-3.5" />
                  </>
                )}
              </button>
            )}

            {step === 2 && (
              <>
                <button
                  onClick={onClose}
                  className="bg-surface-700 hover:bg-surface-600 text-content-primary border border-line-strong text-xs font-semibold px-4 py-2 rounded-[var(--radius-control)] transition-all shadow-xs cursor-pointer"
                >
                  Edit Schema (Close)
                </button>
                <button
                  onClick={handleGenerate}
                  disabled={loading}
                  className="flex items-center gap-2 bg-namines-teal-vibrant hover:brightness-105 active:scale-95 disabled:bg-surface-600 disabled:text-content-muted text-surface-900 text-xs font-bold px-5 py-2 rounded-[var(--radius-control)] transition-all cursor-pointer shadow-xs"
                >
                  {loading ? (
                    <>
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      <span>Calculating Diff...</span>
                    </>
                  ) : (
                    <>
                      <span>Compare & Generate Migration</span>
                      <ChevronRight className="w-3.5 h-3.5" />
                    </>
                  )}
                </button>
              </>
            )}

            {step === 3 && (
              <button
                onClick={onClose}
                className="bg-namines-teal-vibrant hover:brightness-105 active:scale-95 text-surface-900 text-xs font-bold px-5 py-2 rounded-[var(--radius-control)] transition-all cursor-pointer shadow-xs"
              >
                Complete & Close
              </button>
            )}
          </div>
        </div>

        {/* Overwrite warning dialog */}
        {showOverwriteWarning && (
          <div className="absolute inset-0 z-modal bg-scrim/80 backdrop-blur-sm flex items-center justify-center p-5 animate-in fade-in duration-200">
            <div className="w-full max-w-sm bg-surface-800 border border-danger/30 rounded-[var(--radius-modal)] p-5 shadow-2xl text-center space-y-4">
              <div className="w-10 h-10 bg-danger-subtle border border-danger/30 text-danger-text flex items-center justify-center rounded-[var(--radius-card)] mx-auto shadow-xs">
                <AlertTriangle className="w-5 h-5" />
              </div>
              <div className="space-y-1.5">
                <h4 className="text-content-primary text-sm font-bold">Destructive Import Warning</h4>
                <p className="text-content-muted text-xs leading-relaxed">
                  Loading this schema will overwrite your existing whiteboard tables and canvas layout. Continue?
                </p>
              </div>
              <div className="flex gap-2 justify-center pt-1 select-none">
                <button
                  onClick={() => {
                    setShowOverwriteWarning(false);
                    setPendingParsedSchema(null);
                  }}
                  className="text-xs text-content-secondary hover:text-content-primary bg-surface-700 hover:bg-surface-600 border border-line-strong px-4 py-2 rounded-[var(--radius-control)] transition-all cursor-pointer shadow-xs"
                >
                  Cancel
                </button>
                <button
                  onClick={confirmOverwrite}
                  className="text-xs text-danger-text font-bold bg-danger-subtle hover:bg-danger/25 border border-danger/30 px-4 py-2 rounded-[var(--radius-control)] transition-all cursor-pointer shadow-xs"
                >
                  Confirm & Overwrite
                </button>
              </div>
            </div>
          </div>
        )}

      </div>
    </div>
  );
}
