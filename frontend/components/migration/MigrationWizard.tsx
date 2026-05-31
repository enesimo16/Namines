import React, { useState, useRef, useEffect } from 'react';
import { 
  X, Upload, Check, ChevronRight, RefreshCw, 
  AlertCircle, FileCode, CheckCircle, Database, 
  ArrowLeft, Terminal, AlertTriangle 
} from 'lucide-react';
import { useSchemaStore, DbType } from '../../store/useSchemaStore';
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

  const [step, setStep] = useState<1 | 2 | 3>(1);
  const [dbContextCode, setDbContextCode] = useState('');
  const [selectedDbType, setSelectedDbType] = useState<DbType>(dbType || 'MSSQL');
  const [isDragOver, setIsDragOver] = useState(false);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // Results state
  const [diffResult, setDiffResult] = useState<SchemaDiffResult | null>(null);
  const [migrationResult, setMigrationResult] = useState<MigrationResult | null>(null);
  
  const fileInputRef = useRef<HTMLInputElement>(null);

  // Check if we already have a loaded old migration schema in local storage
  useEffect(() => {
    if (isOpen) {
      const savedSchema = localStorage.getItem('namines-old-migration-schema');
      if (savedSchema) {
        setStep(2);
      } else {
        setStep(1);
      }
      setError(null);
    }
  }, [isOpen]);

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
      setError('Lütfen bir DbContext kodu yapıştırın veya dosya yükleyin.');
      return;
    }

    setLoading(true);
    setError(null);
    try {
      setDbType(selectedDbType);
      const parsedSchema = await migrationService.parseDbContext(dbContextCode, selectedDbType);
      
      if (!parsedSchema || !parsedSchema.tables || parsedSchema.tables.length === 0) {
        throw new Error('AI kodu parse edemedi veya tablolar boş. Lütfen geçerli bir DbContext kurgusu yükleyin.');
      }

      // Load it into canvas workspace
      loadFromSchema(parsedSchema);

      // Persist it as our baseline 'oldSchema'
      localStorage.setItem('namines-old-migration-schema', JSON.stringify(parsedSchema));
      
      setStep(2);
    } catch (err: any) {
      setError(err?.response?.data?.error || err.message || 'DbContext parse edilirken AI sunucusunda bir sorun oluştu.');
    } finally {
      setLoading(false);
    }
  };

  const handleUseCurrentAsBaseline = () => {
    try {
      const currentSchema = flowToSchema(schema || { schemaId: '', name: projectName, tables: [], relations: [] }, getNodes(), getEdges());
      if (!currentSchema || !currentSchema.tables || currentSchema.tables.length === 0) {
        setError('Canvas üzerinde kayıtlı herhangi bir tablo bulunamadı. Lütfen önce diyagram tasarlayın veya şema yükleyin.');
        return;
      }
      
      // Clear C# code text
      setDbContextCode('');
      
      // Save canvas state as baseline
      localStorage.setItem('namines-old-migration-schema', JSON.stringify(currentSchema));
      
      setStep(2);
      setError(null);
    } catch (err: any) {
      setError(err.message || 'Mevcut şema baseline olarak kaydedilirken hata oluştu.');
    }
  };

  // Step 2: Compare & Generate Migration
  const handleGenerate = async () => {
    const savedSchemaString = localStorage.getItem('namines-old-migration-schema');
    if (!savedSchemaString) {
      setError('Eski şema bulunamadı. Lütfen ilk adıma geri dönüp DbContext yükleyin.');
      setStep(1);
      return;
    }

    setLoading(true);
    setError(null);

    try {
      const oldSchema = JSON.parse(savedSchemaString);
      // Construct current state from flow canvas
      const currentSchema = flowToSchema(schema || { schemaId: '', name: 'Current', tables: [], relations: [] }, getNodes(), getEdges());
      
      if (!currentSchema) {
        throw new Error('Mevcut şema canvas yapısından çıkartılamadı.');
      }

      // Call Calculate Diff
      const diff = await migrationService.calculateDiff(oldSchema, currentSchema);
      setDiffResult(diff);

      // Call Generate Migration
      const migration = await migrationService.generateMigration(oldSchema, currentSchema, selectedDbType);
      setMigrationResult(migration);

      setStep(3);
    } catch (err: any) {
      setError(err?.response?.data?.error || err.message || 'Migration oluşturulurken hata meydana geldi.');
    } finally {
      setLoading(false);
    }
  };

  const handleReset = () => {
    if (confirm('Migration geçmişi sıfırlanacak. Emin misiniz?')) {
      localStorage.removeItem('namines-old-migration-schema');
      setDbContextCode('');
      setDiffResult(null);
      setMigrationResult(null);
      setStep(1);
      setError(null);
    }
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/75 backdrop-blur-sm animate-in fade-in duration-300">
      <div className="relative w-full max-w-3xl rounded-3xl bg-[#0F172A]/90 backdrop-blur-md border border-indigo-500/20 shadow-[0_0_50px_rgba(99,102,241,0.2)] p-6 md:p-8 flex flex-col max-h-[85vh] overflow-hidden">
        
        {/* Header */}
        <div className="flex justify-between items-start mb-6">
          <div>
            <h2 className="text-xl md:text-2xl font-bold bg-gradient-to-r from-indigo-200 via-indigo-400 to-indigo-100 bg-clip-text text-transparent flex items-center gap-2">
              <span>Migration Engine</span>
            </h2>
            <p className="text-zinc-400 text-xs mt-1">
              Mevcut DbContext kodunuzu yükleyin, şemanızı Canvas üzerinde güncelleyin ve EF Core Migration kodlarını anında üretin.
            </p>
          </div>
          <button 
            onClick={onClose}
            className="p-2 text-zinc-400 hover:text-white bg-zinc-900/60 hover:bg-zinc-800 border border-zinc-800 rounded-xl transition-all"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Stepper */}
        <div className="flex items-center gap-4 mb-6 bg-zinc-950/40 border border-zinc-900 p-3 rounded-2xl">
          <div className="flex items-center gap-2 flex-1 justify-center">
            <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold ${step >= 1 ? 'bg-indigo-600 text-white shadow-md' : 'bg-zinc-900 text-zinc-500 border border-zinc-800'}`}>
              {step > 1 ? <Check className="w-4 h-4" /> : '1'}
            </div>
            <span className={`text-xs font-semibold ${step >= 1 ? 'text-indigo-300' : 'text-zinc-500'}`}>DbContext Yükle</span>
          </div>
          <ChevronRight className="w-4 h-4 text-zinc-700" />
          <div className="flex items-center gap-2 flex-1 justify-center">
            <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold ${step >= 2 ? 'bg-indigo-600 text-white shadow-md' : 'bg-zinc-900 text-zinc-500 border border-zinc-800'}`}>
              {step > 2 ? <Check className="w-4 h-4" /> : '2'}
            </div>
            <span className={`text-xs font-semibold ${step >= 2 ? 'text-indigo-300' : 'text-zinc-500'}`}>Canvas'ta Düzenle</span>
          </div>
          <ChevronRight className="w-4 h-4 text-zinc-700" />
          <div className="flex items-center gap-2 flex-1 justify-center">
            <div className={`w-7 h-7 rounded-full flex items-center justify-center text-xs font-bold ${step >= 3 ? 'bg-indigo-600 text-white shadow-md' : 'bg-zinc-900 text-zinc-500 border border-zinc-800'}`}>
              3
            </div>
            <span className={`text-xs font-semibold ${step >= 3 ? 'text-indigo-300' : 'text-zinc-500'}`}>Migration Üret</span>
          </div>
        </div>

        {/* Errors */}
        {error && (
          <div className="bg-rose-950/20 border border-rose-500/20 rounded-2xl p-4 flex gap-3 items-start mb-6 animate-in fade-in slide-in-from-top-2">
            <AlertCircle className="w-5 h-5 text-rose-400 mt-0.5 shrink-0" />
            <div>
              <span className="text-rose-400 text-xs font-bold block mb-0.5">İşlem Başarısız Oldu</span>
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
                    <span className="text-zinc-200 text-xs font-bold block">Veritabanı Tipi</span>
                    <span className="text-zinc-400 text-[11px]">Şema geçişi bu veritabanı kurgusuna göre yapılacaktır.</span>
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
                    <span className="text-zinc-200 text-xs font-bold block">Mevcut Canvas Diyagramını Temel Al</span>
                    <span className="text-zinc-400 text-[11px] leading-relaxed block mt-0.5">
                      Elinizde hazır bir C# DbContext veya veritabanı kodu yoksa, canvas üzerindeki güncel çiziminizi "başlangıç noktası" olarak kaydedip, yapacağınız yeni değişikliklerin geçiş (migration) kodunu üretebilirsiniz.
                    </span>
                  </div>
                </div>
                <button
                  type="button"
                  onClick={handleUseCurrentAsBaseline}
                  className="bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-bold px-5 py-2.5 rounded-xl transition-all shadow-md shrink-0 flex items-center gap-1.5"
                >
                  <Check className="w-3.5 h-3.5" />
                  <span>Başlangıç Noktası Yap</span>
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
                  <span className="text-zinc-200 text-sm font-bold block">DbContext.cs Dosyanızı Sürükleyin</span>
                  <span className="text-zinc-400 text-xs mt-1 block">veya tıklayarak bilgisayarınızdan seçin (C# .cs dosyası)</span>
                </div>
              </div>

              {/* Text Area for raw pasting */}
              <div className="space-y-2">
                <span className="text-zinc-400 text-xs font-semibold block">Veya DbContext kodunu doğrudan buraya yapıştırın:</span>
                <div className="relative rounded-2xl overflow-hidden border border-zinc-800 bg-zinc-950/50">
                  <textarea
                    value={dbContextCode}
                    onChange={(e) => setDbContextCode(e.target.value)}
                    placeholder="public class AppDbContext : DbContext { ..."
                    className="w-full h-44 bg-transparent text-xs font-mono text-zinc-300 p-4 focus:outline-none resize-none custom-scrollbar"
                  />
                  <div className="absolute bottom-3 right-3 flex items-center gap-1.5 bg-[#0F172A] border border-zinc-800 px-3 py-1 rounded-lg">
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
              <div className="text-center max-w-md">
                <h3 className="text-lg font-bold text-zinc-100 mb-2">
                  {dbContextCode.trim() ? 'DbContext Başarıyla Algılandı!' : 'Başlangıç Şeması Başarıyla Kaydedildi!'}
                </h3>
                <p className="text-zinc-400 text-xs leading-relaxed">
                  {dbContextCode.trim() 
                    ? 'C# DbContext kodunuz Groq AI tarafından çözümlendi ve veritabanı şemanız arka plandaki Canvas üzerine aktarıldı.' 
                    : 'Mevcut canvas diyagramınız referans baseline şeması olarak güvenle belleğe alındı.'}
                </p>
              </div>

              {/* Guidance Box */}
              <div className="bg-[#1E293B]/40 border border-indigo-500/15 rounded-3xl p-5 w-full space-y-3.5">
                <div className="flex items-center gap-2 text-indigo-300 text-xs font-bold">
                  <AlertTriangle className="w-4 h-4" />
                  <span>Şimdi Ne Yapmalıyım?</span>
                </div>
                <ol className="list-decimal pl-5 space-y-2.5 text-xs text-zinc-300 leading-relaxed font-medium">
                  <li>Bu modalı kapatıp arkadaki Canvas alanına geçiş yapın.</li>
                  <li>Canvas üzerinde istediğiniz yapısal değişiklikleri gerçekleştirin (örneğin tablo silin, kolon ekleyin, kolon tiplerini düzenleyin).</li>
                  <li>Tüm güncellemeleri tamamladıktan sonra üstteki **Migration** butonuna tekrar tıklayın.</li>
                  <li>Buradaki **"Şema Değişikliklerini Karşılaştır"** butonuna basarak farkları (diff) görün ve C# migration kodunuzu anında alın!</li>
                </ol>
              </div>
            </div>
          )}

          {/* STEP 3: DIFF & MIGRATION GENERATED */}
          {step === 3 && (
            <div className="space-y-6">
              {diffResult && (
                <div className="space-y-2.5">
                  <span className="text-zinc-400 text-xs font-bold block">1. Tespit Edilen Şema Farkları (Diff)</span>
                  <DiffViewer diff={diffResult} />
                </div>
              )}
              
              {migrationResult && (
                <div className="space-y-2.5 pt-4 border-t border-zinc-900">
                  <span className="text-zinc-400 text-xs font-bold block">2. Üretilen EF Core Migration Kodu</span>
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
                className="text-xs text-rose-400 hover:text-rose-300 font-bold flex items-center gap-1 bg-rose-500/5 hover:bg-rose-500/10 border border-rose-500/20 px-3 py-2 rounded-xl transition-all"
              >
                <RefreshCw className="w-3.5 h-3.5" />
                <span>Geçmişi Sıfırla</span>
              </button>
            )}
            {step === 3 && (
              <button 
                onClick={() => setStep(2)}
                className="text-xs text-zinc-400 hover:text-zinc-200 font-bold flex items-center gap-1.5 bg-zinc-900/60 hover:bg-zinc-800 border border-zinc-800 px-3.5 py-2.5 rounded-xl transition-all"
              >
                <ArrowLeft className="w-3.5 h-3.5" />
                <span>Geri Dön</span>
              </button>
            )}
          </div>

          <div className="flex gap-3">
            {step === 1 && (
              <button 
                onClick={handleParse}
                disabled={loading || !dbContextCode.trim()}
                className="flex items-center gap-2 bg-indigo-600 hover:bg-indigo-500 disabled:bg-zinc-800/80 disabled:border-zinc-800 disabled:text-zinc-500 text-white text-xs font-bold px-6 py-2.5 border border-indigo-500/40 rounded-xl transition-all shadow-md disabled:cursor-not-allowed"
              >
                {loading ? (
                  <>
                    <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                    <span>Şema Çözümleniyor...</span>
                  </>
                ) : (
                  <>
                    <span>Şemayı Yükle ve Başla</span>
                    <ChevronRight className="w-4 h-4" />
                  </>
                )}
              </button>
            )}

            {step === 2 && (
              <>
                <button 
                  onClick={onClose}
                  className="bg-zinc-900 hover:bg-zinc-800 border border-zinc-800 text-zinc-300 text-xs font-bold px-5 py-2.5 rounded-xl transition-all"
                >
                  Diyagramı Düzenle (Kapat)
                </button>
                <button 
                  onClick={handleGenerate}
                  disabled={loading}
                  className="flex items-center gap-2 bg-indigo-600 hover:bg-indigo-500 disabled:bg-zinc-800/80 disabled:border-zinc-800 disabled:text-zinc-500 text-white text-xs font-bold px-6 py-2.5 border border-indigo-500/40 rounded-xl transition-all shadow-md"
                >
                  {loading ? (
                    <>
                      <RefreshCw className="w-3.5 h-3.5 animate-spin" />
                      <span>Farklar Karşılaştırılıyor...</span>
                    </>
                  ) : (
                    <>
                      <span>Farkları Karşılaştır ve Migration Üret</span>
                      <ChevronRight className="w-4 h-4" />
                    </>
                  )}
                </button>
              </>
            )}

            {step === 3 && (
              <button 
                onClick={onClose}
                className="bg-indigo-600 hover:bg-indigo-500 text-white text-xs font-bold px-6 py-2.5 border border-indigo-500/40 rounded-xl transition-all shadow-md"
              >
                Tamamla ve Kapat
              </button>
            )}
          </div>
        </div>

      </div>
    </div>
  );
}
