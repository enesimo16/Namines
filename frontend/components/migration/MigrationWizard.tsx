import React, { useState, useRef, useEffect } from 'react';
import { 
  X, Upload, Check, ChevronRight, RefreshCw, 
  AlertCircle, FileCode, CheckCircle, Database, 
  ArrowLeft, Terminal, AlertTriangle 
} from 'lucide-react';
import { useSchemaStore, DbType } from '../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../store/useProjectHistoryStore';
import { useReactFlow } from '@xyflow/react';
import { flowToSchema } from '../../lib/flowToSchema';
import { migrationService } from '../../services/api';
import { SchemaDiffResult, MigrationResult } from '../../types/migration';
import DiffViewer from './DiffViewer';
import MigrationCodeView from './MigrationCodeView';

interface MigrationWizardProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function MigrationWizard({ isOpen, onClose }: MigrationWizardProps) {
  const { schema, loadFromSchema, dbType, setDbType, projectName } = useSchemaStore();
  const { getNodes, getEdges } = useReactFlow();

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
  const [pendingParsedSchema, setPendingParsedSchema] = useState<any | null>(null);

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
    } catch (err: any) {
      setError(err?.response?.data?.error || err.message || 'A problem occurred on the AI server while parsing DbContext.');
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
    } catch (err: any) {
      setError(err.message || 'An error occurred while saving the current schema as a baseline.');
    }
  };

  // Step 2: Compare & Generate Migration
  const handleGenerate = async () => {
    if (!migrationBaseline) {
      setError('Old schema not found. Please go back to the first step and load a DbContext.');
      setStep(1);
      return;
    }

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
      const diff = await migrationService.calculateDiff(oldSchema, currentSchema);
      setDiffResult(diff);

      // Call Generate Migration
      const migration = await migrationService.generateMigration(oldSchema, currentSchema, selectedDbType);
      setMigrationResult(migration);

      setStep(3);
    } catch (err: any) {
      setError(err?.response?.data?.error || err.message || 'An error occurred while creating the migration.');
    } finally {
      setLoading(false);
    }
  };

  const handleReset = () => {
    if (confirm('Migration history will be reset. Are you sure?')) {
      setMigrationBaseline(null);
      setDbContextCode('');
      setDiffResult(null);
      setMigrationResult(null);
      setStep(1);
      setError(null);
    }
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/75 backdrop-blur-sm animate-in fade-in duration-300 animate-none">
      <div className="relative w-full max-w-3xl rounded-3xl bg-[#0F172A]/90 backdrop-blur-md border border-indigo-500/20 shadow-[0_0_50px_rgba(99,102,241,0.2)] p-6 md:p-8 flex flex-col max-h-[85vh] overflow-hidden">
        
        {/* Header */}
        <div className="flex justify-between items-start mb-6 select-none">
          <div>
            <h2 className="text-xl md:text-2xl font-bold bg-gradient-to-r from-indigo-200 via-indigo-400 to-indigo-100 bg-clip-text text-transparent flex items-center gap-2">
              <span>Migration Engine</span>
            </h2>
            <p className="text-zinc-400 text-xs mt-1">
              Upload your existing DbContext C# code, modify your schema on the Canvas, and generate C# EF Core Migration code instantly.
            </p>
          </div>
          <button 
            onClick={onClose}
            className="p-2 text-zinc-400 hover:text-white bg-zinc-900/60 hover:bg-zinc-800 border border-zinc-800 rounded-xl transition-all cursor-pointer"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Stepper */}
        <div className="flex items-center gap-4 mb-6 bg-zinc-950/40 border border-zinc-900 p-3 rounded-2xl select-none">
          <div className="flex items-center gap-2 flex-1 justify-center">
            <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold ${step >= 1 ? 'bg-indigo-600 text-white shadow-md' : 'bg-zinc-900 text-zinc-500 border border-zinc-800'}`}>
              {step > 1 ? <Check className="w-4 h-4" /> : '1'}
            </div>
            <span className={`text-xs font-semibold ${step >= 1 ? 'text-indigo-300' : 'text-zinc-500'}`}>Upload DbContext</span>
          </div>
          <ChevronRight className="w-4 h-4 text-zinc-700" />
          <div className="flex items-center gap-2 flex-1 justify-center">
            <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold ${step >= 2 ? 'bg-indigo-600 text-white shadow-md' : 'bg-zinc-900 text-zinc-500 border border-zinc-800'}`}>
              {step > 2 ? <Check className="w-4 h-4" /> : '2'}
            </div>
            <span className={`text-xs font-semibold ${step >= 2 ? 'text-indigo-300' : 'text-zinc-500'}`}>Edit on Canvas</span>
          </div>
          <ChevronRight className="w-4 h-4 text-zinc-700" />
          <div className="flex items-center gap-2 flex-1 justify-center">
            <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold ${step >= 3 ? 'bg-indigo-600 text-white shadow-md' : 'bg-zinc-900 text-zinc-500 border border-zinc-800'}`}>
              3
            </div>
            <span className={`text-xs font-semibold ${step >= 3 ? 'text-indigo-300' : 'text-zinc-500'}`}>Generate Migration</span>
          </div>
        </div>

        {/* Errors */}
        {error && (
          <div className="bg-rose-950/20 border border-rose-500/20 rounded-2xl p-4 flex gap-3 items-start mb-6 animate-in fade-in slide-in-from-top-2">
            <AlertCircle className="w-5 h-5 text-rose-400 mt-0.5 shrink-0" />
            <div>
              <span className="text-rose-400 text-xs font-bold block mb-0.5">Operation Failed</span>
              <p className="text-zinc-300 text-xs leading-relaxed">{error}</p>
            </div>
          </div>
        )}

        {/* Dynamic Step Content */}
        <div className="flex-1 overflow-y-auto pr-1 custom-scrollbar min-h-[300px]">
          
          {/* STEP 1: UPLOAD/PASTE */}
          {step === 1 && (
            <div className="space-y-6">
              
              {/* Controls: DB Type */}
              <div className="flex items-center justify-between bg-zinc-900/40 p-4 rounded-2xl border border-zinc-800/80">
                <div className="flex items-center gap-2.5">
                  <Database className="w-5 h-5 text-indigo-400" />
                  <div>
                    <span className="text-zinc-200 text-xs font-bold block">Database Provider</span>
                    <span className="text-zinc-400 text-[11px]">The database schema migration will be computed specifically for this configuration.</span>
                  </div>
                </div>
                <select 
                  value={selectedDbType}
                  onChange={(e) => setSelectedDbType(e.target.value as DbType)}
                  className="bg-zinc-950 border border-zinc-800 rounded-xl px-4 py-2 text-xs font-semibold text-zinc-300 focus:outline-none focus:border-indigo-500"
                >
                  <option value="MSSQL">Microsoft SQL Server</option>
                  <option value="PostgreSQL">PostgreSQL</option>
                  <option value="MySQL">MySQL</option>
                  <option value="SQLite">SQLite</option>
                  <option value="Oracle">Oracle</option>
                  <option value="MariaDB">MariaDB</option>
                  <option value="Db2">IBM Db2</option>
                  <option value="Firebird">Firebird</option>
                  <option value="Spanner">Google Spanner</option>
                  <option value="Redshift">Amazon Redshift</option>
                </select>
              </div>

              {/* Option 2: Use Current Diagram as Baseline */}
              <div className="bg-gradient-to-r from-emerald-500/10 via-emerald-600/5 to-transparent border border-emerald-500/20 rounded-3xl p-5 flex flex-col md:flex-row items-center justify-between gap-4">
                <div className="flex items-start gap-3">
                  <div className="w-10 h-10 bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 flex items-center justify-center rounded-xl shadow-inner mt-0.5 shrink-0">
                    <CheckCircle className="w-5.5 h-5.5" />
                  </div>
                  <div>
                    <span className="text-zinc-200 text-xs font-bold block">Set Current Canvas as Baseline</span>
                    <span className="text-zinc-400 text-[11px] leading-relaxed block mt-0.5">
                      If you do not have an existing DbContext or database script, you can declare your current whiteboard design as the reference baseline. Any subsequent changes you make on the canvas will be compared to this baseline to generate migration scripts.
                    </span>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={handleUseCurrentAsBaseline}
                  className="bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold px-5 py-2.5 rounded-xl transition-all shadow-md shrink-0 flex items-center gap-1.5 cursor-pointer"
                >
                  <Check className="w-3.5 h-3.5" />
                  <span>Set as Reference Baseline</span>
                </button>
              </div>

              {/* Drag n Drop Zone */}
              <div 
                onDragOver={handleDragOver}
                onDragLeave={handleDragLeave}
                onDrop={handleDrop}
                onClick={() => fileInputRef.current?.click()}
                className={`border-2 border-dashed rounded-3xl p-8 flex flex-col items-center justify-center gap-3 cursor-pointer transition-all duration-300 ${isDragOver ? 'border-indigo-400 bg-indigo-500/5' : 'border-zinc-800 hover:border-zinc-700 bg-zinc-950/20'}`}
              >
                <input 
                  type="file" 
                  ref={fileInputRef}
                  onChange={handleFileChange}
                  accept=".cs"
                  className="hidden"
                />
                <div className="w-12 h-12 bg-indigo-500/10 border border-indigo-500/20 text-indigo-400 flex items-center justify-center rounded-2xl shadow-inner">
                  <Upload className="w-6 h-6 animate-pulse" />
                </div>
                <div className="text-center">
                  <span className="text-zinc-200 text-sm font-bold block">Drag and drop your DbContext.cs file here</span>
                  <span className="text-zinc-400 text-xs mt-1 block">or click to browse from your computer (.cs source file)</span>
                </div>
              </div>

              {/* Text Area for raw pasting */}
              <div className="space-y-2">
                <span className="text-zinc-400 text-xs font-semibold block">Or paste your DbContext C# source code directly:</span>
                <div className="relative rounded-2xl overflow-hidden border border-zinc-800 bg-zinc-950/50">
                  <textarea
                    value={dbContextCode}
                    onChange={(e) => setDbContextCode(e.target.value)}
                    placeholder="public class AppDbContext : DbContext { ..."
                    className="w-full h-44 bg-transparent text-xs font-mono text-zinc-300 p-4 focus:outline-none resize-none custom-scrollbar"
                  />
                  <div className="absolute bottom-3 right-3 flex items-center gap-1.5 bg-[#0F172A] border border-zinc-800 px-3 py-1 rounded-lg select-none">
                    <Terminal className="w-3.5 h-3.5 text-indigo-400" />
                    <span className="text-zinc-500 text-[10px] font-semibold uppercase">C# Source</span>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* STEP 2: CANVAS EDIT GUIDANCE */}
          {step === 2 && (
            <div className="space-y-6 flex flex-col items-center justify-center py-6">
              <div className="w-16 h-16 bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 flex items-center justify-center rounded-3xl shadow-[0_0_20px_rgba(16,185,129,0.2)] animate-bounce">
                <CheckCircle className="w-8 h-8" />
              </div>
              <div className="text-center max-w-md select-none">
                <h3 className="text-lg font-bold text-zinc-100 mb-2">
                  {dbContextCode.trim() ? 'DbContext Successfully Parsed!' : 'Baseline Schema Successfully Saved!'}
                </h3>
                <p className="text-zinc-400 text-xs leading-relaxed font-semibold">
                  {dbContextCode.trim() 
                    ? 'Your C# DbContext has been parsed successfully and your schema tables are loaded onto the canvas.' 
                    : 'Your current canvas layout has been securely saved as the reference baseline schema.'}
                </p>
              </div>

              {/* Guidance Box */}
              <div className="bg-[#1E293B]/40 border border-indigo-500/15 rounded-3xl p-5 w-full space-y-3.5">
                <div className="flex items-center gap-2 text-indigo-300 text-xs font-bold">
                  <AlertTriangle className="w-4 h-4" />
                  <span>What Should I Do Next?</span>
                </div>
                <ol className="list-decimal pl-5 space-y-2.5 text-xs text-zinc-300 leading-relaxed font-medium">
                  <li>Close this window and head to the interactive database Canvas.</li>
                  <li>Modify the schema as desired on the canvas (add/delete tables, edit columns, configure relationships, etc.).</li>
                  <li>Once your updates are complete, click the **"Migration"** button on the top toolbar again.</li>
                  <li>Click **"Compare & Generate Migration"** below to calculate changes and receive your C# EF Core code instantly!</li>
                </ol>
              </div>
            </div>
          )}

          {/* STEP 3: DIFF & MIGRATION GENERATED */}
          {step === 3 && (
            <div className="space-y-6">
              {diffResult && (
                <div className="space-y-2.5">
                  <span className="text-zinc-400 text-xs font-bold block">1. Detected Schema Changes (Diff)</span>
                  <DiffViewer diff={diffResult} />
                </div>
              )}
              
              {migrationResult && (
                <div className="space-y-2.5 pt-4 border-t border-zinc-900">
                  <span className="text-zinc-400 text-xs font-bold block">2. Generated EF Core Migration Code</span>
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
        <div className="border-t border-zinc-900 pt-6 mt-6 flex justify-between gap-3 items-center">
          <div>
            {step === 2 && (
              <button 
                onClick={handleReset}
                className="text-xs text-rose-400 hover:text-rose-300 font-bold flex items-center gap-1 bg-rose-500/5 hover:bg-rose-500/10 border border-rose-500/20 px-3 py-2 rounded-xl transition-all cursor-pointer"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                <span>Reset Baseline</span>
              </button>
            )}
            {step === 3 && (
              <button 
                onClick={() => setStep(2)}
                className="text-xs text-zinc-400 hover:text-zinc-200 font-bold flex items-center gap-1.5 bg-zinc-900/60 hover:bg-zinc-800 border border-zinc-800 px-3.5 py-2.5 rounded-xl transition-all cursor-pointer"
              >
                <ArrowLeft className="w-3.5 h-3.5" />
                <span>Go Back</span>
              </button>
            )}
          </div>

          <div className="flex gap-3">
            {step === 1 && (
              <button 
                onClick={handleParse}
                disabled={loading || !dbContextCode.trim()}
                className="flex items-center gap-2 bg-indigo-600 hover:bg-indigo-500 disabled:bg-zinc-800/80 disabled:border-zinc-800 disabled:text-zinc-500 text-white text-xs font-bold px-6 py-2.5 border border-indigo-500/40 rounded-xl transition-all shadow-md disabled:cursor-not-allowed cursor-pointer"
              >
                {loading ? (
                  <>
                    <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                    <span>Analyzing DbContext...</span>
                  </>
                ) : (
                  <>
                    <span>Load Schema & Begin</span>
                    <ChevronRight className="w-4 h-4" />
                  </>
                )}
              </button>
            )}

            {step === 2 && (
              <>
                <button 
                  onClick={onClose}
                  className="bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-zinc-300 text-xs font-bold px-5 py-2.5 rounded-xl transition-all cursor-pointer"
                >
                  Edit Schema (Close)
                </button>
                <button 
                  onClick={handleGenerate}
                  disabled={loading}
                  className="flex items-center gap-2 bg-indigo-600 hover:bg-indigo-500 disabled:bg-zinc-800/80 disabled:border-zinc-800 disabled:text-zinc-500 text-white text-xs font-bold px-6 py-2.5 border border-indigo-500/40 rounded-xl transition-all shadow-md cursor-pointer"
                >
                  {loading ? (
                    <>
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      <span>Calculating Diff...</span>
                    </>
                  ) : (
                    <>
                      <span>Compare & Generate Migration</span>
                      <ChevronRight className="w-4 h-4" />
                    </>
                  )}
                </button>
              </>
            )}

            {step === 3 && (
              <button 
                onClick={onClose}
                className="bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold px-6 py-2.5 border border-indigo-500/40 rounded-xl transition-all shadow-md cursor-pointer"
              >
                Complete & Close
              </button>
            )}
          </div>
        </div>

        {/* Overwrite warning dialog */}
        {showOverwriteWarning && (
          <div className="absolute inset-0 z-[110] bg-black/80 backdrop-blur-md flex items-center justify-center p-6 animate-in fade-in duration-300">
            <div className="w-full max-w-md bg-gradient-to-b from-[#1F1635]/95 via-[#0F172A]/98 to-[#0F172A]/98 border border-amber-500/40 rounded-3xl p-8 shadow-[0_0_50px_rgba(245,158,11,0.25)] text-center space-y-5 animate-in zoom-in-95 duration-200">
              <div className="w-14 h-14 bg-gradient-to-tr from-amber-500/20 to-yellow-500/10 border border-amber-500/30 text-amber-400 flex items-center justify-center rounded-2xl mx-auto shadow-[0_0_15px_rgba(245,158,11,0.2)]">
                <AlertTriangle className="w-7 h-7" />
              </div>
              <div className="space-y-2">
                <h4 className="text-zinc-100 text-base font-extrabold tracking-wide uppercase">Destructive Import Warning</h4>
                <p className="text-zinc-300 text-xs leading-relaxed font-semibold">
                  Loading this schema will overwrite your existing whiteboard tables and canvas layout. Are you sure you want to proceed?
                </p>
              </div>
              <div className="flex gap-3 justify-center pt-2 select-none">
                <button
                  onClick={() => {
                    setShowOverwriteWarning(false);
                    setPendingParsedSchema(null);
                  }}
                  className="text-xs text-zinc-300 hover:text-white bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 px-5 py-2.5 rounded-xl transition-all cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  onClick={confirmOverwrite}
                  className="text-xs text-black font-bold bg-gradient-to-r from-amber-500 to-yellow-500 hover:from-amber-400 hover:to-yellow-400 hover:shadow-[0_0_20px_rgba(245,158,11,0.4)] border border-amber-400/40 px-6 py-2.5 rounded-xl transition-all cursor-pointer"
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
