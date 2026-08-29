import React, { useState, useRef } from 'react';
import { X, Upload, Image, RefreshCw, AlertCircle, CheckCircle, AlertTriangle } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { reverseEngineerService } from '../../../services/api';
import { useAIGateway } from '../../../hooks/useAIGateway';

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
  const [pendingSchema, setPendingSchema] = useState<any | null>(null);

  // Verification Step States
  const [parsedSchema, setParsedSchema] = useState<any | null>(null);
  const [showVerification, setShowVerification] = useState(false);
  const [selectedRelations, setSelectedRelations] = useState<Record<string, boolean>>({});

  const fileInputRef = useRef<HTMLInputElement>(null);

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
      (schema.relations || []).forEach((rel: any, idx: number) => {
        const key = rel.id || `rel-${idx}`;
        initialRelations[key] = true;
      });
      setSelectedRelations(initialRelations);
      
      setShowVerification(true);

    } catch (err: any) {
      setError(err?.response?.data?.error || err.message || 'An error occurred on the AI server while analyzing the image.');
    } finally {
      setLoading(false);
    }
  };

  const executeImport = (finalSchema: any) => {
    // Apply the imported schema to the canvas Zustand store
    try {
      importFromVision(finalSchema);
    } catch (err: any) {
      // Ortak, kullanıcıya açıklayıcı hata (sessizce çökmesin).
      setError(err?.message || 'Import failed. The schema data might be missing or corrupt; please try again.');
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
    const filteredRelations = (parsedSchema.relations || []).filter((rel: any, idx: number) => {
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
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-300">
      <div className="relative w-full max-w-md rounded-2xl bg-surface-800 border border-content-primary/12 shadow-[0_20px_60px_rgba(0,0,0,0.6)] p-5 flex flex-col max-h-[85vh] overflow-hidden">

        {/* Header */}
        <div className="flex justify-between items-start mb-4 shrink-0 select-none">
          <div>
            <h2 className="text-sm font-bold text-content-primary">
              {showVerification ? 'Schema Verification' : 'Import from Whiteboard'}
            </h2>
            <p className="text-content-muted text-xs mt-1">
              {showVerification
                ? 'Review the table structures and foreign keys detected by the AI.'
                : 'Upload a photo of a whiteboard schema and let AI convert it to digital tables.'}
            </p>
          </div>
          <button
            onClick={handleClose}
            disabled={loading}
            className="p-1.5 text-content-subtle hover:text-content-primary hover:bg-white/[0.06] rounded-lg transition-all disabled:opacity-50"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Error Alert */}
        {error && (
          <div className="bg-danger-subtle border border-danger/25 rounded-xl p-3 flex gap-2.5 items-start mb-4 shrink-0">
            <AlertCircle className="w-4 h-4 text-danger-text mt-0.5 shrink-0" />
            <div>
              <span className="text-danger-text text-xs font-bold block mb-0.5">Analysis Failed</span>
              <p className="text-content-primary text-[11px] leading-relaxed">{error}</p>
            </div>
          </div>
        )}

        {/* Success Alert */}
        {success && (
          <div className="bg-success-subtle border border-success/25 rounded-xl p-3 flex gap-2.5 items-start mb-4 shrink-0">
            <CheckCircle className="w-4 h-4 text-success-text mt-0.5 shrink-0" />
            <div>
              <span className="text-success-text text-xs font-bold block mb-0.5">Schema Imported Successfully!</span>
              <p className="text-content-primary text-[11px] leading-relaxed">Tables and relationships detected and placed. Updating canvas...</p>
            </div>
          </div>
        )}

        {/* Dynamic content scrollable area */}
        <div className="flex-1 overflow-y-auto pr-1 min-h-[220px] flex flex-col justify-start">

          {showVerification && parsedSchema ? (
            /* Verification Screen */
            <div className="space-y-3 py-1">
              <div className="bg-surface-700 border border-content-primary/8 rounded-xl p-3 flex gap-2.5 items-start">
                <AlertCircle className="w-4 h-4 text-content-primary mt-0.5 shrink-0" />
                <div>
                  <span className="text-content-primary text-xs font-bold block mb-0.5">Parsing Successful!</span>
                  <p className="text-content-subtle text-[10px] leading-relaxed">
                    Review the parsed tables and relationships below. Uncheck mismatched relationships to exclude them.
                  </p>
                </div>
              </div>

              {/* Detected Tables Section */}
              <div className="space-y-1.5 select-none">
                <span className="text-[10px] font-bold text-content-subtle uppercase tracking-wider block">Detected Tables ({parsedSchema.tables.length})</span>
                <div className="grid grid-cols-2 gap-1.5 max-h-[110px] overflow-y-auto pr-1">
                  {parsedSchema.tables.map((t: any) => (
                    <div key={t.id} className="p-2 bg-surface-700 border border-content-primary/8 rounded-lg flex justify-between items-center text-xs">
                      <span className="text-content-primary font-semibold truncate max-w-[120px]">{t.name}</span>
                      <span className="text-content-subtle text-[10px]">{t.columns?.length || 0} Columns</span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Detected Relationships Section */}
              <div className="space-y-1.5">
                <span className="text-[10px] font-bold text-content-subtle uppercase tracking-wider block">Detected Relationships ({parsedSchema.relations?.length || 0})</span>
                <div className="space-y-1.5 max-h-[160px] overflow-y-auto pr-1">
                  {(!parsedSchema.relations || parsedSchema.relations.length === 0) ? (
                    <p className="text-content-subtle text-xs italic p-3 bg-surface-700 border border-content-primary/8 rounded-lg text-center select-none">No relationships detected.</p>
                  ) : (
                    parsedSchema.relations.map((rel: any, idx: number) => {
                      const sourceTable = parsedSchema.tables.find((t: any) => t.id === rel.sourceTableId || t.name === rel.sourceTableId);
                      const targetTable = parsedSchema.tables.find((t: any) => t.id === rel.targetTableId || t.name === rel.targetTableId);
                      const sourceName = sourceTable?.name || rel.sourceTableId;
                      const targetName = targetTable?.name || rel.targetTableId;
                      const key = rel.id || `rel-${idx}`;

                      return (
                        <div key={key} className="flex items-center justify-between p-2 bg-surface-700 hover:bg-surface-600 border border-content-primary/8 rounded-lg transition-all">
                          <div className="flex items-center gap-1.5 text-[11px] select-none">
                            <span className="text-content-primary font-semibold truncate max-w-[100px]">{sourceName}</span>
                            <span className="text-content-primary px-1 py-0.5 bg-white/[0.08] rounded border border-white/15 text-[9px] font-mono shrink-0">{rel.type || 'OneToMany'}</span>
                            <span className="text-content-subtle">→</span>
                            <span className="text-content-primary font-semibold truncate max-w-[100px]">{targetName}</span>
                          </div>
                          <input
                            type="checkbox"
                            checked={selectedRelations[key] !== false}
                            onChange={() => {
                              setSelectedRelations(prev => ({
                                ...prev,
                                [key]: !prev[key]
                              }));
                            }}
                            className="w-3.5 h-3.5 rounded accent-accent-hover cursor-pointer shrink-0"
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
              className={`border border-dashed rounded-xl p-6 flex flex-col items-center justify-center gap-2.5 cursor-pointer transition-all duration-300 min-h-[180px] select-none ${isDragOver ? 'border-focus-ring bg-white/[0.08]/40' : 'border-content-primary/12 hover:border-content-primary/20 bg-surface-700/40'}`}
            >
              <input
                type="file"
                ref={fileInputRef}
                onChange={handleFileChange}
                accept="image/jpeg,image/png,image/webp"
                className="hidden"
              />
              <div className="w-10 h-10 bg-surface-600 border border-content-primary/10 text-content-primary flex items-center justify-center rounded-xl">
                <Image className="w-4 h-4" />
              </div>
              <div className="text-center">
                <span className="text-content-primary text-sm font-semibold block">Drag and Drop Your Image File</span>
                <span className="text-content-subtle text-xs mt-1 block">or click to select (JPEG, PNG, WebP · max 10MB)</span>
              </div>
            </div>
          ) : (
            /* Preview Area */
            <div className="space-y-3">
              <div className="relative rounded-xl overflow-hidden border border-content-primary/10 bg-surface-700 max-h-[220px] flex items-center justify-center">
                <img
                  src={previewUrl}
                  alt="Preview"
                  className="max-h-[220px] object-contain w-full"
                />

                {loading && (
                  <div className="absolute inset-0 bg-scrim/75 backdrop-blur-sm flex flex-col items-center justify-center gap-3 select-none">
                    <RefreshCw className="w-6 h-6 text-content-primary animate-spin" />
                    <div className="text-center px-4">
                      <span className="text-content-primary text-xs font-bold block">Parsing Schema...</span>
                      <span className="text-content-muted text-[10px] mt-1 block">AI is analyzing the image, reading handwriting and lines.</span>
                    </div>
                  </div>
                )}
              </div>

              {!loading && !success && (
                <div className="flex gap-2 justify-center select-none">
                  <button
                    onClick={() => {
                      setSelectedFile(null);
                      setPreviewUrl(null);
                      setShowVerification(false);
                      setParsedSchema(null);
                    }}
                    className="text-xs text-content-muted hover:text-content-primary bg-surface-700 border border-content-primary/10 px-3 py-1.5 rounded-lg transition-all"
                  >
                    Select Another Image
                  </button>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-content-primary/10 pt-4 mt-4 flex justify-end gap-2 shrink-0 select-none">
          <button
            onClick={handleClose}
            disabled={loading}
            className="bg-transparent hover:bg-white/[0.04] border border-content-primary/10 text-content-muted hover:text-content-primary text-xs font-medium px-4 py-2 rounded-lg transition-all disabled:opacity-50"
          >
            Close
          </button>

          {showVerification && parsedSchema && !success && (
            <button
              onClick={handleConfirmImport}
              className="flex items-center gap-1.5 bg-success hover:bg-success-text text-surface-800 text-xs font-semibold px-4 py-2 rounded-lg transition-all"
            >
              <CheckCircle className="w-3.5 h-3.5" />
              <span>Confirm & Import to Canvas</span>
            </button>
          )}

          {previewUrl && !showVerification && !loading && !success && (
            <button
              onClick={handleAnalyze}
              className="flex items-center gap-1.5 bg-content-primary hover:bg-content-secondary text-surface-900 text-xs font-semibold px-4 py-2 rounded-lg transition-all"
            >
              <span>Convert to Schema with AI</span>
            </button>
          )}
        </div>

        {/* Overwrite warning dialog — desatüre uyarı/amber, veri kaybı riski (semantik) */}
        {showOverwriteWarning && (
          <div className="absolute inset-0 z-[110] bg-scrim/80 backdrop-blur-sm flex items-center justify-center p-5 animate-in fade-in duration-200">
            <div className="w-full max-w-sm bg-surface-800 border border-danger/30 rounded-2xl p-5 shadow-[0_20px_60px_rgba(0,0,0,0.6)] text-center space-y-4">
              <div className="w-10 h-10 bg-danger-subtle border border-danger/30 text-danger-text flex items-center justify-center rounded-xl mx-auto">
                <AlertTriangle className="w-5 h-5" />
              </div>
              <div className="space-y-1.5">
                <h4 className="text-content-primary text-sm font-bold">Destructive Import Warning</h4>
                <p className="text-content-muted text-xs leading-relaxed">
                  Importing this schema will overwrite your existing tables and layout. Continue?
                </p>
              </div>
              <div className="flex gap-2 justify-center pt-1 select-none">
                <button
                  onClick={() => {
                    setShowOverwriteWarning(false);
                    setPendingSchema(null);
                  }}
                  className="text-xs text-content-muted hover:text-content-primary bg-transparent hover:bg-white/[0.04] border border-content-primary/10 px-4 py-2 rounded-lg transition-all cursor-pointer"
                >
                  Cancel
                </button>
                <button
                  onClick={confirmOverwrite}
                  className="text-xs text-surface-800 font-semibold bg-danger hover:bg-danger-text px-4 py-2 rounded-lg transition-all cursor-pointer"
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
