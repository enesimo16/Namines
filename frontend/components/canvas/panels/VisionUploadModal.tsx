import React, { useState, useRef } from 'react';
import { X, Upload, Image, RefreshCw, AlertCircle, Sparkles, CheckCircle } from 'lucide-react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { reverseEngineerService } from '../../../services/api';

interface VisionUploadModalProps {
  isOpen: boolean;
  onClose: () => void;
}

export default function VisionUploadModal({ isOpen, onClose }: VisionUploadModalProps) {
  const { importFromVision } = useSchemaStore();
  
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

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

    // Apply the imported schema to the canvas Zustand store
    importFromVision(finalSchema);
    
    setSuccess(true);
    
    // Auto close modal shortly after success
    setTimeout(() => {
      handleClose();
    }, 1500);
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
    onClose();
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/75 backdrop-blur-sm animate-in fade-in duration-300">
      <div className="relative w-full max-w-lg rounded-3xl bg-[#0F172A]/95 backdrop-blur-md border border-indigo-500/20 shadow-[0_0_50px_rgba(99,102,241,0.25)] p-6 md:p-8 flex flex-col max-h-[85vh] overflow-hidden">
        
        {/* Header */}
        <div className="flex justify-between items-start mb-6 shrink-0 select-none">
          <div>
            <h2 className="text-xl font-bold bg-gradient-to-r from-indigo-200 via-indigo-400 to-indigo-100 bg-clip-text text-transparent flex items-center gap-2">
              <Sparkles className="w-5 h-5 text-indigo-400 animate-pulse animate-none" />
              <span>{showVerification ? 'Schema Verification' : 'Import from Whiteboard'}</span>
            </h2>
            <p className="text-zinc-400 text-xs mt-1">
              {showVerification 
                ? 'Review the table structures and foreign keys detected by the AI.' 
                : 'Upload a photo of the database schema you drew on a physical whiteboard, and let AI convert it to digital tables instantly.'}
            </p>
          </div>
          <button 
            onClick={handleClose}
            disabled={loading}
            className="p-2 text-zinc-400 hover:text-white bg-zinc-900/60 hover:bg-zinc-800 border border-zinc-800 rounded-xl transition-all disabled:opacity-50"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Error Alert */}
        {error && (
          <div className="bg-rose-950/20 border border-rose-500/20 rounded-2xl p-4 flex gap-3 items-start mb-5 shrink-0 animate-in fade-in slide-in-from-top-2">
            <AlertCircle className="w-4 h-4 text-rose-400 mt-0.5 shrink-0" />
            <div>
              <span className="text-rose-400 text-xs font-bold block mb-0.5 animate-none">Analysis Failed</span>
              <p className="text-zinc-300 text-[11px] leading-relaxed">{error}</p>
            </div>
          </div>
        )}

        {/* Success Alert */}
        {success && (
          <div className="bg-emerald-950/20 border border-emerald-500/20 rounded-2xl p-4 flex gap-3 items-start mb-5 shrink-0 animate-in fade-in">
            <CheckCircle className="w-4 h-4 text-emerald-400 mt-0.5 shrink-0" />
            <div>
              <span className="text-emerald-400 text-xs font-bold block mb-0.5 animate-none">Schema Imported Successfully!</span>
              <p className="text-zinc-300 text-[11px] leading-relaxed">Tables and relationships detected and placed. Updating canvas...</p>
            </div>
          </div>
        )}

        {/* Dynamic content scrollable area */}
        <div className="flex-1 overflow-y-auto pr-1 custom-scrollbar min-h-[220px] flex flex-col justify-start">
          
          {showVerification && parsedSchema ? (
            /* Verification Screen */
            <div className="space-y-4 py-2 animate-in fade-in slide-in-from-bottom-2">
              <div className="bg-indigo-500/5 border border-indigo-500/10 rounded-2xl p-4 flex gap-3 items-start">
                <AlertCircle className="w-4 h-4 text-indigo-400 mt-0.5 shrink-0" />
                <div>
                  <span className="text-indigo-300 text-xs font-bold block mb-0.5 animate-none">Parsing Successful!</span>
                  <p className="text-zinc-400 text-[10px] leading-relaxed">
                    Review the parsed table structures and relationships below. You can uncheck mismatched relationships to exclude them from the import.
                  </p>
                </div>
              </div>

              {/* Detected Tables Section */}
              <div className="space-y-2 select-none">
                <span className="text-xs font-bold text-zinc-400 uppercase tracking-wider block">Detected Tables ({parsedSchema.tables.length})</span>
                <div className="grid grid-cols-2 gap-2 max-h-[110px] overflow-y-auto pr-1 custom-scrollbar">
                  {parsedSchema.tables.map((t: any) => (
                    <div key={t.id} className="p-2.5 bg-zinc-950/40 border border-zinc-900 rounded-xl flex justify-between items-center text-xs">
                      <span className="text-zinc-200 font-bold truncate max-w-[120px]">{t.name}</span>
                      <span className="text-zinc-500 text-[10px]">{t.columns?.length || 0} Columns</span>
                    </div>
                  ))}
                </div>
              </div>

              {/* Detected Relationships Section */}
              <div className="space-y-2">
                <span className="text-xs font-bold text-zinc-400 uppercase tracking-wider block">Detected Relationships (Foreign Keys - {parsedSchema.relations?.length || 0})</span>
                <div className="space-y-2 max-h-[160px] overflow-y-auto pr-1 custom-scrollbar">
                  {(!parsedSchema.relations || parsedSchema.relations.length === 0) ? (
                    <p className="text-zinc-500 text-xs italic p-3 bg-zinc-950/20 border border-zinc-900 rounded-xl text-center select-none">No relationships detected.</p>
                  ) : (
                    parsedSchema.relations.map((rel: any, idx: number) => {
                      const sourceTable = parsedSchema.tables.find((t: any) => t.id === rel.sourceTableId || t.name === rel.sourceTableId);
                      const targetTable = parsedSchema.tables.find((t: any) => t.id === rel.targetTableId || t.name === rel.targetTableId);
                      const sourceName = sourceTable?.name || rel.sourceTableId;
                      const targetName = targetTable?.name || rel.targetTableId;
                      const key = rel.id || `rel-${idx}`;

                      return (
                        <div key={key} className="flex items-center justify-between p-2.5 bg-zinc-950/40 hover:bg-zinc-900/40 border border-zinc-900 rounded-xl transition-all">
                          <div className="flex items-center gap-2 text-[11px] select-none">
                            <span className="text-zinc-200 font-semibold truncate max-w-[100px]">{sourceName}</span>
                            <span className="text-indigo-400 px-1 py-0.5 bg-indigo-500/10 rounded border border-indigo-500/20 text-[9px] font-mono shrink-0">{rel.type || 'OneToMany'}</span>
                            <span className="text-zinc-500">➔</span>
                            <span className="text-zinc-200 font-semibold truncate max-w-[100px]">{targetName}</span>
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
                            className="w-4 h-4 rounded text-indigo-600 border-zinc-800 bg-zinc-950 focus:ring-indigo-500 cursor-pointer accent-indigo-500 shrink-0"
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
              className={`border-2 border-dashed rounded-3xl p-8 flex flex-col items-center justify-center gap-3 cursor-pointer transition-all duration-300 min-h-[200px] select-none ${isDragOver ? 'border-indigo-400 bg-indigo-500/5' : 'border-zinc-800 hover:border-zinc-700 bg-zinc-950/20'}`}
            >
              <input 
                type="file" 
                ref={fileInputRef}
                onChange={handleFileChange}
                accept="image/jpeg,image/png,image/webp"
                className="hidden"
              />
              <div className="w-12 h-12 bg-indigo-500/10 border border-indigo-500/20 text-indigo-400 flex items-center justify-center rounded-2xl shadow-inner">
                <Image className="w-5 h-5 animate-pulse animate-none" />
              </div>
              <div className="text-center">
                <span className="text-zinc-200 text-sm font-bold block">Drag and Drop Your Image File</span>
                <span className="text-zinc-400 text-xs mt-1 block">or click to select from your device (JPEG, PNG, WebP - Max 10MB)</span>
              </div>
            </div>
          ) : (
            /* Preview Area */
            <div className="space-y-4">
              <div className="relative rounded-2xl overflow-hidden border border-zinc-800 bg-zinc-950 max-h-[250px] flex items-center justify-center shadow-inner">
                <img 
                  src={previewUrl} 
                  alt="Preview" 
                  className="max-h-[250px] object-contain w-full"
                />
                
                {loading && (
                  /* Floating Loading Overlay */
                  <div className="absolute inset-0 bg-black/75 backdrop-blur-sm flex flex-col items-center justify-center gap-3 animate-in fade-in duration-300 select-none">
                    <RefreshCw className="w-8 h-8 text-indigo-400 animate-spin" />
                    <div className="text-center px-4">
                      <span className="text-zinc-100 text-xs font-bold block animate-pulse animate-none">Parsing Schema...</span>
                      <span className="text-zinc-400 text-[10px] mt-1 block">AI is analyzing the image, reading handwritings and lines.</span>
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
                    className="text-xs text-zinc-400 hover:text-zinc-200 bg-zinc-900 border border-zinc-800 px-4 py-2 rounded-xl transition-all"
                  >
                    Select Another Image
                  </button>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Footer */}
        <div className="border-t border-zinc-900 pt-6 mt-6 flex justify-end gap-3 shrink-0 select-none">
          <button 
            onClick={handleClose}
            disabled={loading}
            className="bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-zinc-300 text-xs font-bold px-5 py-2.5 rounded-xl transition-all disabled:opacity-50"
          >
            Close
          </button>
          
          {showVerification && parsedSchema && !success && (
            <button 
              onClick={handleConfirmImport}
              className="flex items-center gap-1.5 bg-gradient-to-r from-emerald-600 to-emerald-500 hover:from-emerald-500 hover:to-emerald-400 text-white text-xs font-bold px-6 py-2.5 border border-emerald-500/40 rounded-xl transition-all shadow-[0_0_15px_rgba(16,185,129,0.4)] animate-in fade-in"
            >
              <CheckCircle className="w-3.5 h-3.5 animate-none" />
              <span>Confirm & Import to Canvas</span>
            </button>
          )}

          {previewUrl && !showVerification && !loading && !success && (
            <button 
              onClick={handleAnalyze}
              className="flex items-center gap-1.5 bg-gradient-to-r from-indigo-600 to-indigo-500 hover:from-indigo-500 hover:to-indigo-400 text-white text-xs font-bold px-6 py-2.5 border border-indigo-500/40 rounded-xl transition-all shadow-[0_0_15px_rgba(99,102,241,0.4)]"
            >
              <Sparkles className="w-3.5 h-3.5 animate-none" />
              <span>Convert to Schema with AI</span>
            </button>
          )}
        </div>

      </div>
    </div>
  );
}
