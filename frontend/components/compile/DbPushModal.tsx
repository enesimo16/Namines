import React, { useState } from 'react';
import * as Dialog from '@radix-ui/react-dialog';
import { X, Database, CheckCircle2, AlertCircle, Loader2 } from 'lucide-react';
import { useSchemaStore, type DbType } from '../../store/useSchemaStore';
import { useAuthStore } from '../../store/useAuthStore';
import { executorService } from '../../services/api';

/**
 * Merkezi API istemcisi basarisiz HTTP durumlarinda firlatiyor; sunucunun
 * govdesi hatanin uzerinde tasiniyor. Bu iki yardimci onu tipli sekilde
 * okuyor -- `any` kullanmadan ve her cagri noktasinda tekrarlamadan.
 */
type ExecutorErrorBody = {
  message?: string;
  statementsExecuted?: number;
  partialApplyPossible?: boolean;
};

const serverBody = (err: unknown): ExecutorErrorBody | undefined =>
  (err as { response?: { data?: ExecutorErrorBody } })?.response?.data;

const serverMessage = (err: unknown): string | undefined => serverBody(err)?.message;

interface DbPushModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  sqlScript: string;
}

const DB_PLACEHOLDERS: Record<string, string> = {
  MSSQL:      'e.g., Server=myServer;Database=myDb;User Id=myUser;Password=myPassword;',
  PostgreSQL: 'e.g., Host=localhost;Port=5432;Database=myDb;Username=myUser;Password=myPassword;',
  MySQL:      'e.g., Server=localhost;Port=3306;Database=myDb;Uid=myUser;Pwd=myPassword;',
  SQLite:     'e.g., Data Source=./mydb.sqlite;',
  Oracle:     'e.g., Data Source=localhost:1521/ORCL;User Id=myUser;Password=myPassword;',
  MariaDB:    'e.g., Server=localhost;Port=3306;Database=myDb;Uid=myUser;Pwd=myPassword;',
};

export default function DbPushModal({ open, onOpenChange, sqlScript }: DbPushModalProps) {
  const { dbType, setDbType } = useSchemaStore();
  const { isAuthenticated } = useAuthStore();
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
    if (!isAuthenticated) {
      resetState();
      setTestSuccess(false);
      setDeployMessage({ text: 'You need to sign in to use this feature.', isError: true });
      return;
    }
    setIsTesting(true);
    resetState();
    try {
      const data = await executorService.testConnection(connectionString, dbType);
      setTestSuccess(true);
      setDeployMessage({ text: data.message || 'Connection successful!', isError: false });
    } catch (err) {
      // Sunucunun SINIFLANDIRILMIS mesajı gövdede geliyor (ör. "Authentication
      // failed. Check the username and password") ve kullanıcıya gösterilmeli —
      // "bir hata oluştu" demek onu çözümsüz bırakırdı.
      setTestSuccess(false);
      setDeployMessage({
        text: serverMessage(err) ?? 'Could not reach server or an error occurred.',
        isError: true,
      });
    } finally {
      setIsTesting(false);
    }
  };

  const handleDeploy = async () => {
    if (!connectionString.trim() || !sqlScript.trim()) return;
    if (!isAuthenticated) {
      setDeployMessage({ text: 'You need to sign in to use this feature.', isError: true });
      return;
    }
    setIsDeploying(true);
    setDeployMessage(null);
    try {
      const data = await executorService.execute(connectionString, dbType, sqlScript);
      setDeployMessage({ text: data.message || 'Successfully applied!', isError: false });
    } catch (err) {
      // Kısmi uygulama uyarısı GİZLENMEZ. Sunucu betiği tek transaction'da
      // çalıştırıyor, ama MySQL/MariaDB/Oracle DDL'i örtük commit'liyor — yani
      // "geri alındı" demek o motorlarda yalan olurdu. Kullanıcı veritabanının
      // yarım kalmış olabileceğini bilmeden devam ederse, ikinci denemesi
      // "zaten var" hatalarına çarpar ve nedenini anlayamaz.
      const body = serverBody(err);
      const partial = body?.partialApplyPossible
        ? ` Warning: this engine commits DDL outside the transaction, so the first ${body.statementsExecuted ?? 0} statement(s) may already be applied. Check the database before retrying.`
        : '';

      setDeployMessage({
        text: (body?.message ?? 'An error occurred during deployment.') + partial,
        isError: true,
      });
    } finally {
      setIsDeploying(false);
    }
  };

  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay className="fixed inset-0 bg-surface-900/80 backdrop-blur-sm z-50 animate-in fade-in" />
        <Dialog.Content className="fixed left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 w-full max-w-[460px] bg-surface-700 border border-surface-500 rounded-[var(--radius-modal)] z-50 flex flex-col animate-in fade-in zoom-in-95 overflow-hidden">

          {/* Header */}
          <div className="flex items-center justify-between px-6 pt-5 pb-3">
            <Dialog.Title className="text-base font-bold text-content-primary flex items-center gap-2.5">
              <Database className="w-4.5 h-4.5 text-content-muted" strokeWidth={2} />
              Deploy to Live Database
            </Dialog.Title>
            <Dialog.Close asChild>
              <button className="w-7 h-7 flex items-center justify-center text-content-subtle hover:text-content-secondary hover:bg-white/[0.08] rounded-[var(--radius-control)] transition-colors cursor-pointer">
                <X className="w-4 h-4" />
              </button>
            </Dialog.Close>
          </div>

          <div className="px-6 pb-6 flex flex-col gap-4">
            {/* DB Type */}
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-medium text-content-subtle flex items-center gap-1.5">
                <Database className="w-3.5 h-3.5 text-content-subtle" />
                Target Database Type
              </label>
              <div className="relative">
                <select
                  aria-label="Target Database Type"
                  value={dbType}
                  onChange={(e) => { setDbType(e.target.value as DbType); resetState(); }}
                  className="w-full appearance-none bg-surface-600 border border-surface-500 text-content-primary text-sm rounded-[var(--radius-control)] px-3.5 py-2.5 pr-9 focus:outline-none focus:border-focus-ring transition-colors cursor-pointer"
                >
                  <option value="MSSQL">SQL Server</option>
                  <option value="PostgreSQL">PostgreSQL</option>
                  <option value="MySQL">MySQL</option>
                  <option value="SQLite">SQLite</option>
                  <option value="Oracle">Oracle</option>
                  <option value="MariaDB">MariaDB</option>
                </select>
                <div className="pointer-events-none absolute inset-y-0 right-3 flex items-center">
                  <svg className="w-3.5 h-3.5 text-content-subtle" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
                    <path strokeLinecap="round" strokeLinejoin="round" d="M19 9l-7 7-7-7" />
                  </svg>
                </div>
              </div>
            </div>

            {/* Connection String */}
            <div className="flex flex-col gap-1.5">
              <label className="text-xs font-medium text-content-subtle">
                Connection String
              </label>
              <textarea
                aria-label="Connection String"
                value={connectionString}
                onChange={(e) => { setConnectionString(e.target.value); resetState(); }}
                placeholder={DB_PLACEHOLDERS[dbType] || DB_PLACEHOLDERS['MSSQL']}
                rows={3}
                className="w-full bg-surface-600 border border-surface-500 text-content-secondary text-sm rounded-[var(--radius-control)] px-3.5 py-2.5 resize-none focus:outline-none focus:border-focus-ring transition-colors font-mono placeholder:text-content-subtle leading-relaxed"
              />
              <p className="text-[11px] text-content-subtle flex items-center gap-1.5 font-medium">
                <AlertCircle className="w-3.5 h-3.5 shrink-0" />
                Your credentials are not saved or logged. They are deleted when the session ends.
              </p>
            </div>

            {/* Feedback */}
            {deployMessage && (
              <div className={`px-3.5 py-2.5 rounded-[var(--radius-control)] text-xs flex items-start gap-2 ${
                deployMessage.isError
                  ? 'bg-danger-text/10 text-danger-text'
                  : 'bg-success-text/10 text-success-text'
              }`}>
                {deployMessage.isError
                  ? <AlertCircle className="w-3.5 h-3.5 shrink-0 mt-0.5" />
                  : <CheckCircle2 className="w-3.5 h-3.5 shrink-0 mt-0.5" />}
                <span className="leading-relaxed font-medium">{deployMessage.text}</span>
              </div>
            )}

            {/* Actions */}
            <div className="flex gap-2.5 pt-1">
              <button
                onClick={handleTestConnection}
                disabled={isTesting || !connectionString.trim()}
                className="flex-1 py-2.5 bg-white/[0.06] hover:bg-white/[0.1] disabled:opacity-40 text-content-secondary text-xs font-semibold rounded-[var(--radius-control)] transition-colors flex items-center justify-center gap-2 cursor-pointer"
              >
                {isTesting
                  ? <Loader2 className="w-3.5 h-3.5 animate-spin" />
                  : testSuccess === true
                    ? <CheckCircle2 className="w-3.5 h-3.5 text-success-text" />
                    : null}
                Test Connection
              </button>
              <button
                onClick={handleDeploy}
                disabled={!testSuccess || isDeploying || !connectionString.trim() || !sqlScript.trim()}
                className="flex-1 py-2.5 bg-content-primary hover:bg-content-primary-hover disabled:opacity-40 disabled:bg-white/[0.06] text-surface-900 disabled:text-content-subtle text-xs font-semibold rounded-[var(--radius-control)] transition-colors flex items-center justify-center gap-2 cursor-pointer"
              >
                {isDeploying ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : null}
                Run Script
              </button>
            </div>
          </div>

        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
