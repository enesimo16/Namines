import React, { useState, useRef } from 'react';
import { X, Image as ImageIcon, RefreshCw, AlertCircle, CheckCircle, AlertTriangle } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { reverseEngineerService } from '../../../services/api';
import { useAIGateway } from '../../../hooks/useAIGateway';
import { errorMessage } from '../../../lib/errors';
import type { DatabaseSchema, SchemaRelation, SchemaTable } from '../../../types/schema';
import { useEscapeKey } from '../../../hooks/useEscapeKey';

interface VisionUploadModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function VisionUploadModal({ isOpen, onClose }: VisionUploadModalProps) {
  const { schema, importFromVision } = useSchemaStore();
  const { checkAccess } = useAIGateway();
  
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  // Overwrite warning states
  const [showOverwriteWarning, setShowOverwriteWarning] = useState(false);
  const [pendingSchema, setPendingSchema] = useState<DatabaseSchema | null>(null);

  // Verification Step States
  const [parsedSchema, setParsedSchema] = useState<DatabaseSchema | null>(null);
  const [showVerification, setShowVerification] = useState(false);
  const [selectedRelations, setSelectedRelations] = useState<Record<string, boolean>>({});

  const fileInputRef = useRef<HTMLInputElement>(null);

  useEscapeKey(isOpen, onClose);

  if (!isOpen) return null;

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) {
      processFile(file);
    }
  };

  const processFile = (file: File) => {
    const validTypes = ['image/jpeg', 'image/png', 'image/webp'];
    if (!validTypes.includes(file.type)) {
      setError('Invalid file format. Please upload JPEG, PNG, or WebP.');
      return;
    }

    if (file.size > 10 * 1024 * 1024) {
      setError('File size cannot exceed 10MB.');
      return;
    }

    setSelectedFile(file);
    setError(null);
    setSuccess(false);
    setShowVerification(false);
    setParsedSchema(null);

    // Create a local preview URL
    const reader = new FileReader();
    reader.onloadend = () => {
      setPreviewUrl(reader.result as string);
    };
    reader.readAsDataURL(file);
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
      processFile(file);
    }
  };

  const handleAnalyze = async () => {
    if (!selectedFile) return;
    if (!checkAccess("Vision Reverse Engineering")) return;

    setLoading(true);
    setError(null);

    try {
      const schema = await reverseEngineerService.analyzeVisionImage(selectedFile);

      if (!schema || !schema.tables || schema.tables.length === 0) {
        throw new Error('Could not parse a valid database schema from the image.');
      }

      setParsedSchema(schema);
      
      // Select all relationships by default
      const initialRelations: Record<string, boolean> = {};
      (schema.relations || []).forEach((rel: SchemaRelation, idx: number) => {
        const key = rel.id || `rel-${idx}`;
        initialRelations[key] = true;
      });
      setSelectedRelations(initialRelations);
      
      setShowVerification(true);

    } catch (err) {
      setError(errorMessage(err, 'An error occurred on the AI server while analyzing the image.'));
    } finally {
      setLoading(false);
    }
  };

  const executeImport = (finalSchema: DatabaseSchema) => {
    // Apply the imported schema to the canvas Zustand store
    try {
      importFromVision(finalSchema);
    } catch (err) {
      // Ortak, kullanıcıya açıklayıcı hata (sessizce çökmesin).
      setError(errorMessage(err, 'Import failed. The schema data might be missing or corrupt; please try again.'));
      return;
    }

    setSuccess(true);

    // Auto close modal shortly after success
    setTimeout(() => {
      handleClose();
    }, 1500);
  };

  const confirmOverwrite = () => {
    if (pendingSchema) {
      executeImport(pendingSchema);
      setShowOverwriteWarning(false);
      setPendingSchema(null);
    }
  };

  const handleConfirmImport = () => {
    if (!parsedSchema) return;

    // Filter relations based on user checkbox selection
    const filteredRelations = (parsedSchema.relations || []).filter((rel: SchemaRelation, idx: number) => {
      const key = rel.id || `rel-${idx}`;
      return selectedRelations[key] !== false;
    });

    const finalSchema = {
      ...parsedSchema,
      relations: filteredRelations
    };

    if (schema && schema.tables && schema.tables.length > 0) {
      setPendingSchema(finalSchema);
      setShowOverwriteWarning(true);
    } else {
      executeImport(finalSchema);
    }
  };

  const handleClose = () => {
    setSelectedFile(null);
    setPreviewUrl(null);
    setParsedSchema(null);
    setShowVerification(false);
    setSelectedRelations({});
    setError(null);
    setSuccess(false);
    setLoading(false);
    setShowOverwriteWarning(false);
    setPendingSchema(null);
    onClose();
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200 select-none">
      <div className="relative w-full max-w-md rounded-[var(--radius-modal)] bg-surface-900 border border-line-strong shadow-2xl flex flex-col max-h-[85vh] overflow-hidden">

        {/* Header */}
        <div className="flex items-center justify-between px-5 py-3.5 border-b border-line bg-surface-800/40 shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-7 w-7 bg-surface-700 border border-line-strong rounded-[var(--radius-control)] flex items-center justify-center text-content-primary">
              <ImageIcon className="w-3.5 h-3.5" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">
                {showVerification ? 'Schema Verification' : 'Import from Whiteboard'}
              </h2>
              <p className="text-micro text-content-muted">
                {showVerification
                  ? 'Verify detected tables and relationships'
                  : 'Convert whiteboard or ERD diagram photos into tables'}
              </p>
            </div>
          </div>
          <button
            onClick={handleClose}
            disabled={loading}
            className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors disabled:opacity-50 cursor-pointer"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Error Alert */}
        {error && (
          <div className="m-4 bg-danger-subtle border border-danger/30 rounded-[var(--radius-card)] p-3 flex gap-2.5 items-start shrink-0">
            <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
            <div>
              <span className="text-danger-text text-xs font-bold block mb-0.5">Analysis Failed</span>
              <p className="text-danger-text text-micro leading-relaxed">{error}</p>
            </div>
          </div>
        )}

        {/* Success Alert */}
        {success && (
          <div className="m-4 bg-success-subtle border border-success/30 rounded-[var(--radius-card)] p-3 flex gap-2.5 items-start shrink-0">
            <CheckCircle className="w-4 h-4 text-success-text mt-0.5 shrink-0" />
            <div>
              <span className="text-success-text text-xs font-bold block mb-0.5">Schema Imported Successfully</span>
              <p className="text-success-text text-micro leading-relaxed">Tables and relationships placed on canvas.</p>
            </div>
          </div>
        )}

        {/* Dynamic content scrollable area */}
        <div className="flex-1 overflow-y-auto p-5 min-h-[200px] flex flex-col justify-start">

          {showVerification && parsedSchema ? (
            /* Verification Screen */
            <div className="space-y-3">
              <div className="bg-surface-800/80 border border-line rounded-[var(--radius-card)] p-3 flex gap-2.5 items-start">
                <CheckCircle className="w-4 h-4 text-success-text mt-0.5 shrink-0" />
                <div>
                  <span className="text-content-primary text-xs font-bold block mb-0.5">Parsing Succeeded</span>
                  <p className="text-content-muted text-micro leading-relaxed">
                    Review parsed tables and relationships. Uncheck any relations you wish to exclude.
                  </p>
                </div>
              </div>

              {/* Detected Tables Section */}
              <div className="space-y-1.5 select-none">
                <span className="text-micro font-bold text-content-muted uppercase tracking-wider block">
                  Detected Tables ({parsedSchema.tables?.length || 0})
                </span>
                <div className="grid grid-cols-2 gap-1.5 max-h-[110px] overflow-y-auto pr-1">
                  {(parsedSchema.tables || []).map((t: SchemaTable) => (
                    <div key={t.id} className="p-2 bg-surface-800 border border-line rounded-[var(--radius-control)] flex justify-between items-center text-xs">
                      <span className="text-content-primary font-bold font-mono truncate max-w-[120px]">{t.name}</span>
                      <span className="text-content-muted text-micro">{t.columns?.length || 0} cols</span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Detected Relationships Section */}
              <div className="space-y-1.5">
                <span className="text-micro font-bold text-content-muted uppercase tracking-wider block">
                  Detected Relationships ({parsedSchema.relations?.length || 0})
                </span>
                <div className="space-y-1.5 max-h-[160px] overflow-y-auto pr-1">
                  {(!parsedSchema.relations || parsedSchema.relations.length === 0) ? (
                    <p className="text-content-muted text-xs italic p-3 bg-surface-800 border border-line rounded-[var(--radius-control)] text-center select-none">
                      No relationships detected.
                    </p>
                  ) : (
                    parsedSchema.relations.map((rel: SchemaRelation, idx: number) => {
                      const sourceTable = parsedSchema.tables?.find((t: SchemaTable) => t.id === rel.sourceTableId || t.name === rel.sourceTableId);
                      const targetTable = parsedSchema.tables?.find((t: SchemaTable) => t.id === rel.targetTableId || t.name === rel.targetTableId);
                      const sourceName = sourceTable?.name || rel.sourceTableId;
                      const targetName = targetTable?.name || rel.targetTableId;
                      const key = rel.id || `rel-${idx}`;

                      return (
                        <div key={key} className="flex items-center justify-between p-2 bg-surface-800/80 hover:bg-surface-700/80 border border-line rounded-[var(--radius-control)] transition-all">
                          <div className="flex items-center gap-1.5 text-micro select-none font-mono">
                            <span className="text-content-primary font-bold truncate max-w-[100px]">{sourceName}</span>
                            <span className="text-content-muted px-1.5 py-0.5 bg-surface-700 rounded-[var(--radius-control)] border border-line text-[10px] font-mono shrink-0">{rel.type || '1:N'}</span>
                            <span className="text-content-muted">→</span>
                            <span className="text-content-primary font-bold truncate max-w-[100px]">{targetName}</span>
                          </div>
                          <input
                            type="checkbox"
                            aria-label={`Include relation ${sourceName} → ${targetName}`}
                            checked={selectedRelations[key] !== false}
                            onChange={() => {
                              setSelectedRelations(prev => ({
                                ...prev,
                                [key]: !prev[key]
                              }));
                            }}
                            className="w-4 h-4 rounded-[var(--radius-control)] border-line bg-surface-700 text-accent cursor-pointer shrink-0"
                          />
                        </div>
                      );
                    })
                  )}
                </div>
              </div>
            </div>
          ) : !previewUrl ? (
            /* Dropzone area */
            <div
              onDragOver={handleDragOver}
              onDragLeave={handleDragLeave}
              onDrop={handleDrop}
              onClick={() => fileInputRef.current?.click()}
              className={`border-2 border-dashed rounded-[var(--radius-card)] p-8 flex flex-col items-center justify-center gap-3 cursor-pointer transition-all duration-200 min-h-[190px] group ${
                isDragOver ? 'border-focus-ring bg-surface-700/60' : 'border-line-strong hover:border-content-primary bg-surface-800/40 hover:bg-surface-800/70'
              }`}
            >
              <input
                type="file"
                ref={fileInputRef}
                onChange={handleFileChange}
                accept="image/jpeg,image/png,image/webp"
                className="hidden"
              />
              <div className="w-10 h-10 bg-surface-700 border border-line text-content-muted group-hover:text-content-primary flex items-center justify-center rounded-full group-hover:scale-105 transition-all">
                <ImageIcon className="w-5 h-5" />
              </div>
              <div className="text-center">
                <span className="text-content-primary text-xs font-bold block">Drag & Drop Whiteboard or Diagram Image</span>
                <span className="text-content-muted text-micro mt-0.5 block">or click to browse (JPEG, PNG, WebP · max 10MB)</span>
              </div>
            </div>
          ) : (
            /* Preview Area */
            <div className="space-y-3">
              <div className="relative rounded-[var(--radius-card)] overflow-hidden border border-line bg-surface-800 max-h-[220px] flex items-center justify-center">
                <img
                  src={previewUrl}
                  alt="Preview"
                  className="max-h-[220px] object-contain w-full"
                />

                {loading && (
                  <div className="absolute inset-0 bg-scrim/80 backdrop-blur-sm flex flex-col items-center justify-center gap-2.5">
                    <RefreshCw className="w-6 h-6 text-content-primary animate-spin" />
                    <div className="text-center px-4">
                      <span className="text-content-primary text-xs font-bold block">Analyzing Diagram with AI…</span>
                      <span className="text-content-muted text-micro mt-0.5 block">Reading handwriting, tables, and relationships.</span>
                    </div>
                  </div>
                )}
              </div>

              {!loading && !success && (
                <div className="flex justify-center">
                  <button
                    onClick={() => {
                      setSelectedFile(null);
                      setPreviewUrl(null);
                      setShowVerification(false);
                      setParsedSchema(null);
                    }}
                    className="text-micro font-semibold text-content-muted hover:text-content-primary bg-surface-800 hover:bg-surface-700 border border-line px-3 py-1.5 rounded-[var(--radius-control)] transition-all cursor-pointer"
                  >
                    Select Another Image
                  </button>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-line px-5 py-3 flex items-center justify-end gap-2 shrink-0 bg-surface-800/40">
          <button
            onClick={handleClose}
            disabled={loading}
            className="px-3.5 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-secondary hover:text-content-primary text-xs font-semibold border border-line transition-all cursor-pointer disabled:opacity-50"
          >
            Close
          </button>

          {showVerification && parsedSchema && !success && (
            <button
              onClick={handleConfirmImport}
              className="flex items-center gap-1.5 bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-bold px-4 py-1.5 rounded-[var(--radius-control)] transition-all shadow-sm cursor-pointer"
            >
              <CheckCircle className="w-3.5 h-3.5" />
              <span>Confirm & Place on Canvas</span>
            </button>
          )}

          {previewUrl && !showVerification && !loading && !success && (
            <button
              onClick={handleAnalyze}
              className="flex items-center gap-1.5 bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-bold px-4 py-1.5 rounded-[var(--radius-control)] transition-all shadow-sm cursor-pointer"
            >
              <span>Convert to Schema with AI</span>
            </button>
          )}
        </div>

        {/* Overwrite warning dialog */}
        {showOverwriteWarning && (
          <div className="absolute inset-0 z-[110] bg-scrim/80 backdrop-blur-sm flex items-center justify-center p-5 animate-in fade-in duration-200">
            <div className="w-full max-w-sm bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] p-5 shadow-2xl text-center space-y-4">
              <div className="w-10 h-10 bg-danger-subtle border border-danger/30 text-danger-text flex items-center justify-center rounded-full mx-auto">
                <AlertTriangle className="w-5 h-5" />
              </div>
              <div className="space-y-1">
                <h4 className="text-content-primary text-sm font-bold">Overwrite Canvas Schema?</h4>
                <p className="text-content-muted text-xs leading-relaxed">
                  Importing this diagram will replace existing tables on the canvas. Proceed?
                </p>
              </div>
              <div className="flex gap-2 justify-center pt-1">
                <button
                  onClick={() => {
                    setShowOverwriteWarning(false);
                    setPendingSchema(null);
                  }}
                  className="px-3.5 py-1.5 rounded-[var(--radius-control)] bg-surface-800 hover:bg-surface-700 border border-line text-xs font-semibold text-content-primary cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  onClick={confirmOverwrite}
                  className="px-4 py-1.5 rounded-[var(--radius-control)] bg-danger-subtle hover:bg-danger/25 text-danger-text border border-danger/30 text-xs font-bold cursor-pointer shadow-sm transition-colors"
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
