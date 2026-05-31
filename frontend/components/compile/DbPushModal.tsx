import React, { useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { X, Database, CheckCircle2, AlertCircle, Loader2 } from 'lucide-react';
import { useSchemaStore } from '../../store/useSchemaStore';

interface DbPushModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sqlScript: string;
}

const DB_PLACEHOLDERS: Record<string, string> = {
  MSSQL:      'Örn: Server=myServer;Database=myDb;User Id=myUser;Password=myPassword;',
  PostgreSQL: 'Örn: Host=localhost;Port=5432;Database=myDb;Username=myUser;Password=myPassword;',
  MySQL:      'Örn: Server=localhost;Port=3306;Database=myDb;Uid=myUser;Pwd=myPassword;',
  SQLite:     'Örn: Data Source=./mydb.sqlite;',
  Oracle:     'Örn: Data Source=localhost:1521/ORCL;User Id=myUser;Password=myPassword;',
  MariaDB:    'Örn: Server=localhost;Port=3306;Database=myDb;Uid=myUser;Pwd=myPassword;',
  Db2:        'Örn: Server=myAddress:50000;Database=myDataBase;UID=myUsername;PWD=myPassword;',
  Firebird:   'Örn: User=SYSDBA;Password=masterkey;Database=localhost:C:/Db/myDb.fdb;',
  Spanner:    'Örn: Project=my-project;Instance=my-instance;Database=my-db;',
  Redshift:   'Örn: Server=my-cluster.redshift.amazonaws.com;Database=myDb;User=myUser;Password=myPassword;Port=5439;',
};

export default function DbPushModal({ open, onOpenChange, sqlScript }: DbPushModalProps) {
  const { dbType, setDbType } = useSchemaStore();
  const [connectionString, setConnectionString] = useState('');
  const [isTesting, setIsTesting] = useState(false);
  const [testSuccess, setTestSuccess] = useState<boolean | null>(null);
  const [isDeploying, setIsDeploying] = useState(false);
  const [deployMessage, setDeployMessage] = useState<{ text: string; isError: boolean } | null>(null);

  const resetState = () => {
    setTestSuccess(null);
    setDeployMessage(null);
  };

  const handleTestConnection = async () => {
    if (!connectionString.trim()) return;
    setIsTesting(true);
    resetState();
    try {
      const response = await fetch('http://localhost:5000/api/executor/test-connection', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ connectionString, dbType }),
      });
      const data = await response.json();
      if (response.ok && data.success) {
        setTestSuccess(true);
        setDeployMessage({ text: 'Bağlantı başarılı!', isError: false });
      } else {
        setTestSuccess(false);
        setDeployMessage({ text: data.message || 'Bağlantı başarısız.', isError: true });
      }
    } catch {
      setTestSuccess(false);
      setDeployMessage({ text: 'Sunucuya ulaşılamadı veya bir hata oluştu.', isError: true });
    } finally {
      setIsTesting(false);
    }
  };

  const handleDeploy = async () => {
    if (!connectionString.trim() || !sqlScript.trim()) return;
    setIsDeploying(true);
    setDeployMessage(null);
    try {
      const response = await fetch('http://localhost:5000/api/executor/execute', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ connectionString, dbType, script: sqlScript }),
      });
      const data = await response.json();
      if (response.ok && data.success) {
        setDeployMessage({ text: data.message || 'Başarıyla aktarıldı!', isError: false });
      } else {
        setDeployMessage({ text: data.message || 'Aktarım başarısız.', isError: true });
      }
    } catch {
      setDeployMessage({ text: 'Aktarım sırasında bir hata oluştu.', isError: true });
    } finally {
      setIsDeploying(false);
    }
  };

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-black/70 backdrop-blur-sm z-50 animate-in fade-in" />
        <Dialog.Content className="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 w-full max-w-[480px] bg-[#111318] border border-zinc-800 rounded-2xl shadow-2xl z-50 flex flex-col animate-in fade-in zoom-in-95 overflow-hidden">

          {/* Header */}
          <div className="flex items-center justify-between px-6 pt-6 pb-4">
            <Dialog.Title className="text-[18px] font-bold text-white flex items-center gap-2.5">
              <Database className="w-5 h-5 text-indigo-400" strokeWidth={2} />
              Canlı Veritabanına Aktar
            </Dialog.Title>
            <Dialog.Close asChild>
              <button className="w-8 h-8 flex items-center justify-center text-zinc-500 hover:text-zinc-300 hover:bg-zinc-800 rounded-lg transition-colors">
                <X className="w-4 h-4" />
              </button>
            </Dialog.Close>
          </div>

          <div className="px-6 pb-6 flex flex-col gap-5">
            {/* DB Type */}
            <div className="flex flex-col gap-2">
              <label className="text-sm font-medium text-zinc-400 flex items-center gap-2">
                <Database className="w-4 h-4 text-zinc-500" />
                Hedef Veritabanı Türü
              </label>
              <div className="relative">
                <select
                  value={dbType}
                  onChange={(e) => { setDbType(e.target.value as any); resetState(); }}
                  className="w-full appearance-none bg-[#1a1d24] border border-zinc-700/60 text-white text-[15px] rounded-xl px-4 py-3 pr-10 focus:outline-none focus:border-indigo-500/60 transition-colors cursor-pointer"
                >
                  <option value="MSSQL">SQL Server</option>
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
                {/* Chevron */}
                <div className="pointer-events-none absolute inset-y-0 right-3 flex items-center">
                  <svg className="w-4 h-4 text-zinc-500" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
                  </svg>
                </div>
              </div>
            </div>

            {/* Connection String */}
            <div className="flex flex-col gap-2">
              <label className="text-sm font-medium text-zinc-300">
                Connection String
              </label>
              <textarea
                value={connectionString}
                onChange={(e) => { setConnectionString(e.target.value); resetState(); }}
                placeholder={DB_PLACEHOLDERS[dbType] || DB_PLACEHOLDERS['MSSQL']}
                rows={3}
                className="w-full bg-[#1a1d24] border border-zinc-700/60 text-zinc-200 text-sm rounded-xl px-4 py-3 resize-none focus:outline-none focus:border-indigo-500/60 transition-colors font-mono placeholder:text-zinc-600 leading-relaxed"
              />
              <p className="text-xs text-amber-500/90 flex items-center gap-1.5">
                <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                Bilgileriniz kaydedilmez veya loglanmaz. Oturum bittiğinde silinir.
              </p>
            </div>

            {/* Feedback */}
            {deployMessage && (
              <div className={`px-4 py-3 rounded-xl text-sm flex items-start gap-2 ${
                deployMessage.isError
                  ? 'bg-red-500/10 text-red-400 border border-red-500/20'
                  : 'bg-emerald-500/10 text-emerald-400 border border-emerald-500/20'
              }`}>
                {deployMessage.isError
                  ? <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
                  : <CheckCircle2 className="w-4 h-4 shrink-0 mt-0.5" />}
                <span className="leading-relaxed">{deployMessage.text}</span>
              </div>
            )}

            {/* Actions */}
            <div className="flex gap-3 pt-1">
              <button
                onClick={handleTestConnection}
                disabled={isTesting || !connectionString.trim()}
                className="flex-1 py-2.5 bg-zinc-800 hover:bg-zinc-700 disabled:opacity-40 text-zinc-200 text-sm font-medium rounded-xl transition-colors border border-zinc-700 flex items-center justify-center gap-2"
              >
                {isTesting
                  ? <Loader2 className="w-4 h-4 animate-spin" />
                  : testSuccess === true
                    ? <CheckCircle2 className="w-4 h-4 text-emerald-400" />
                    : null}
                Bağlantıyı Test Et
              </button>
              <button
                onClick={handleDeploy}
                disabled={!testSuccess || isDeploying || !connectionString.trim() || !sqlScript.trim()}
                className="flex-1 py-2.5 bg-indigo-600 hover:bg-indigo-500 disabled:opacity-40 disabled:bg-zinc-800 text-white text-sm font-medium rounded-xl transition-colors flex items-center justify-center gap-2"
              >
                {isDeploying ? <Loader2 className="w-4 h-4 animate-spin" /> : null}
                Script'i Çalıştır
              </button>
            </div>
          </div>

        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
