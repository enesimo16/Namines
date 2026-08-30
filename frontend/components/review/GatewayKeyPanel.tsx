'use client';

import { useCallback, useEffect, useState } from 'react';
import { KeyRound, Plus, Trash2, Loader2, Copy, Check, ShieldAlert, Table2 } from 'lucide-react';
import { gatewayKeyService } from '../../services/api';
import { useToastStore } from '../../store/useToastStore';
import { useSchemaStore } from '../../store/useSchemaStore';
import { GatewayKey, GatewayKeyCreated, GatewayTablePermission } from '../../types/gatewayKey';

interface Props {
  projectId: string;
}

/**
 * 08 §4.3 — Gateway API anahtarları ve tablo izinleri.
 *
 * Panelin iki yarısı ayrı sebeplerle burada:
 *
 * - Anahtar üretildiğinde ham değer BİR KEZ görünür ve sunucu onu saklamaz. Bu
 *   yüzden kopyalama kutusu geçici bir bildirim değil, kullanıcı kapatana kadar
 *   duran bir blok — kaybolan anahtar geri getirilemez, yenisi üretilir.
 * - Tablo listesi varsayılan olarak BOŞ görünür, çünkü izin satırı olmayan tablo
 *   erişilemez demektir (08 §1). Kullanıcının "neden hiçbir şey çalışmıyor"
 *   sorusuna cevap, listenin kendisi olmalı.
 */
export default function GatewayKeyPanel({ projectId }: Props) {
  const showToast = useToastStore(s => s.showToast);
  const schema = useSchemaStore(s => s.schema);

  const [keys, setKeys] = useState<GatewayKey[] | null>(null);
  const [permissions, setPermissions] = useState<GatewayTablePermission[]>([]);
  const [created, setCreated] = useState<GatewayKeyCreated | null>(null);
  const [copied, setCopied] = useState(false);

  const [name, setName] = useState('');
  const [canWrite, setCanWrite] = useState(false);
  const [showRestrictions, setShowRestrictions] = useState(false);
  const [allowedOrigins, setAllowedOrigins] = useState('');
  const [allowedIps, setAllowedIps] = useState('');
  const [rateLimit, setRateLimit] = useState('');
  const [isCreating, setIsCreating] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [busyTable, setBusyTable] = useState<string | null>(null);

  const load = useCallback(() => {
    gatewayKeyService.list(projectId).then(setKeys).catch(() => setKeys([]));
    gatewayKeyService.listTables(projectId).then(setPermissions).catch(() => setPermissions([]));
  }, [projectId]);

  useEffect(load, [load]);

  const permissionFor = (table: string): GatewayTablePermission =>
    permissions.find(p => p.tableName === table) ?? { tableName: table, canRead: false, canWrite: false };

  const handleCreate = async () => {
    if (!name.trim()) return;
    setIsCreating(true);
    try {
      const parsedLimit = rateLimit.trim() === '' ? null : Number(rateLimit);
      if (parsedLimit !== null && (!Number.isFinite(parsedLimit) || parsedLimit <= 0)) {
        showToast('Rate limit must be a number greater than zero.', 'error');
        return;
      }

      const result = await gatewayKeyService.create(projectId, name.trim(), canWrite, null, {
        allowedOrigins: allowedOrigins.trim() || null,
        allowedIps: allowedIps.trim() || null,
        rateLimitPerMinute: parsedLimit,
      });
      setCreated(result);
      setCopied(false);
      setName('');
      setCanWrite(false);
      setAllowedOrigins('');
      setAllowedIps('');
      setRateLimit('');
      setShowRestrictions(false);
      load();
    } catch {
      showToast('Key could not be created.', 'error');
    } finally {
      setIsCreating(false);
    }
  };

  const handleRevoke = async (key: GatewayKey) => {
    setBusyId(key.id);
    try {
      await gatewayKeyService.revoke(projectId, key.id);
      showToast(`"${key.name}" revoked.`, 'success');
      load();
    } catch {
      showToast('Key could not be revoked.', 'error');
    } finally {
      setBusyId(null);
    }
  };

  const handleToggle = async (table: string, next: { canRead: boolean; canWrite: boolean }) => {
    setBusyTable(table);
    try {
      // Yazma okumayı ima eder: yazabilen ama okuyamayan bir istemci yazdığını
      // doğrulayamaz. Sunucu da aynı kuralı uyguluyor; UI onu yansıtıyor.
      const canRead = next.canWrite ? true : next.canRead;
      await gatewayKeyService.setTable(projectId, table, canRead, next.canWrite);
      const others = permissions.filter(p => p.tableName !== table);
      setPermissions(
        canRead || next.canWrite
          ? [...others, { tableName: table, canRead, canWrite: next.canWrite }]
          : others,
      );
    } catch {
      showToast('Permission could not be saved.', 'error');
    } finally {
      setBusyTable(null);
    }
  };

  const copyKey = async () => {
    if (!created) return;
    await navigator.clipboard.writeText(created.key);
    setCopied(true);
  };

  const tables = schema?.tables ?? [];
  const openCount = permissions.filter(p => p.canRead || p.canWrite).length;

  return (
    <section className="bg-surface-700 border border-content-primary/15 rounded-[var(--radius-card)] overflow-hidden">
      <header className="flex items-center gap-2 px-4 py-3 border-b border-content-primary/10">
        <KeyRound className="w-4 h-4 text-content-secondary" />
        <h2 className="text-xs font-semibold text-content-primary">Gateway API keys</h2>
        <span className="ml-auto text-[10px] text-content-subtle">
          {openCount === 0 ? 'no table exposed' : `${openCount} table${openCount === 1 ? '' : 's'} exposed`}
        </span>
      </header>

      {/* Üretilen anahtar — kullanıcı kapatana kadar durur. */}
      {created && (
        <div className="px-4 py-3 border-b border-content-primary/10 bg-surface-800">
          <p className="text-[11px] font-semibold text-content-primary mb-1">
            {created.name} — copy it now
          </p>
          <p className="text-[10px] text-content-muted mb-2">{created.warning}</p>
          <div className="flex items-center gap-2">
            <code className="flex-1 min-w-0 truncate bg-surface-900 border border-content-primary/15 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] font-mono text-content-secondary">
              {created.key}
            </code>
            <button
              onClick={copyKey}
              className="shrink-0 flex items-center gap-1.5 px-2.5 py-1.5 rounded-[var(--radius-control)] bg-surface-600 hover:bg-surface-500 text-[11px] text-content-primary transition-colors"
            >
              {copied ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
              {copied ? 'Copied' : 'Copy'}
            </button>
            <button
              onClick={() => setCreated(null)}
              className="shrink-0 px-2.5 py-1.5 rounded-[var(--radius-control)] text-[11px] text-content-muted hover:text-content-primary transition-colors"
            >
              Done
            </button>
          </div>
        </div>
      )}

      {/* Yeni anahtar */}
      <div className="px-4 py-3 border-b border-content-primary/10 flex flex-wrap items-center gap-2">
        <input
          value={name}
          onChange={e => setName(e.target.value)}
          placeholder="Key name (e.g. production backend)"
          className="flex-1 min-w-[180px] bg-surface-800 border border-content-primary/15 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] text-content-primary placeholder:text-content-muted outline-none focus:border-accent/50"
        />
        <label className="flex items-center gap-1.5 text-[11px] text-content-secondary cursor-pointer select-none">
          <input
            type="checkbox"
            checked={canWrite}
            onChange={e => setCanWrite(e.target.checked)}
            className="accent-accent"
          />
          Allow writes
        </label>
        <button
          onClick={() => setShowRestrictions(v => !v)}
          className="text-[11px] text-content-muted hover:text-content-primary transition-colors"
        >
          {showRestrictions ? 'Hide limits' : 'Limits…'}
        </button>
        <button
          onClick={handleCreate}
          disabled={isCreating || !name.trim()}
          className="flex items-center gap-1.5 px-3 py-1.5 rounded-[var(--radius-control)] bg-accent hover:bg-accent-hover text-[11px] font-medium text-content-primary transition-colors disabled:opacity-50"
        >
          {isCreating ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Plus className="w-3.5 h-3.5" />}
          Create
        </button>

        {/*
          Kısıtlar varsayılan olarak KAPALI: boş bırakılan bir alan "kısıt yok"
          demek, ve zorunlu görünen boş alanlar kullanıcıyı anlamadığı bir şeyi
          doldurmaya iter. İsteyen açar.
        */}
        {showRestrictions && (
          <div className="w-full grid gap-2 pt-2 sm:grid-cols-3">
            <label className="flex flex-col gap-1">
              <span className="text-[10px] text-content-muted">Allowed origins</span>
              <input
                value={allowedOrigins}
                onChange={e => setAllowedOrigins(e.target.value)}
                placeholder="https://app.example.com"
                className="bg-surface-800 border border-content-primary/15 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] text-content-primary placeholder:text-content-muted outline-none focus:border-accent/50"
              />
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-[10px] text-content-muted">Allowed IPs / CIDR</span>
              <input
                value={allowedIps}
                onChange={e => setAllowedIps(e.target.value)}
                placeholder="1.2.3.4, 10.0.0.0/8"
                className="bg-surface-800 border border-content-primary/15 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] text-content-primary placeholder:text-content-muted outline-none focus:border-accent/50"
              />
            </label>
            <label className="flex flex-col gap-1">
              <span className="text-[10px] text-content-muted">Requests / minute</span>
              <input
                value={rateLimit}
                onChange={e => setRateLimit(e.target.value)}
                inputMode="numeric"
                placeholder="600"
                className="bg-surface-800 border border-content-primary/15 rounded-[var(--radius-control)] px-2.5 py-1.5 text-[11px] text-content-primary placeholder:text-content-muted outline-none focus:border-accent/50"
              />
            </label>
            <p className="sm:col-span-3 text-[10px] text-content-muted leading-relaxed">
              Leave a field empty for no restriction. IP rules are only enforced when the server can
              determine the caller&apos;s address reliably; if it cannot, requests with an IP list are
              refused rather than silently let through.
            </p>
          </div>
        )}
      </div>

      {/* Anahtar listesi */}
      <div className="divide-y divide-content-primary/10">
        {keys === null ? (
          <div className="px-4 py-6 flex justify-center">
            <Loader2 className="w-4 h-4 animate-spin text-content-muted" />
          </div>
        ) : keys.length === 0 ? (
          <p className="px-4 py-5 text-[11px] text-content-muted text-center">
            No API keys yet. Create one to let an application read this project through the Gateway.
          </p>
        ) : (
          keys.map(key => (
            <div key={key.id} className="px-4 py-2.5 flex items-center gap-3">
              <div className="min-w-0 flex-1">
                <p className="text-[11px] font-medium text-content-primary truncate">
                  {key.name}
                  {key.revokedAt && <span className="ml-2 text-[10px] text-danger-text">revoked</span>}
                </p>
                <p className="text-[10px] font-mono text-content-subtle truncate">
                  {key.prefix}… · {key.canWrite ? 'read + write' : 'read only'}
                  {key.rateLimitPerMinute ? ` · ${key.rateLimitPerMinute}/min` : ''}
                  {key.allowedIps ? ' · ip-restricted' : ''}
                  {key.allowedOrigins ? ' · origin-restricted' : ''}
                  {key.lastUsedAt ? ` · last used ${new Date(key.lastUsedAt).toLocaleDateString()}` : ' · never used'}
                </p>
              </div>
              {!key.revokedAt && (
                <button
                  onClick={() => handleRevoke(key)}
                  disabled={busyId === key.id}
                  title="Revoke"
                  className="shrink-0 p-1.5 rounded-[var(--radius-control)] text-content-muted hover:text-danger-text hover:bg-surface-600 transition-colors disabled:opacity-50"
                >
                  {busyId === key.id ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Trash2 className="w-3.5 h-3.5" />}
                </button>
              )}
            </div>
          ))
        )}
      </div>

      {/* Tablo izinleri */}
      <div className="border-t border-content-primary/10">
        <div className="px-4 py-2.5 flex items-center gap-2">
          <Table2 className="w-3.5 h-3.5 text-content-secondary" />
          <h3 className="text-[11px] font-semibold text-content-primary">Exposed tables</h3>
        </div>

        <div className="px-4 pb-3">
          <div className="flex items-start gap-2 mb-2.5">
            <ShieldAlert className="w-3.5 h-3.5 mt-0.5 shrink-0 text-content-muted" />
            <p className="text-[10px] text-content-muted leading-relaxed">
              Nothing is exposed by default. A table an API key can reach is a table anyone
              holding that key can read — grant only what the application needs.
            </p>
          </div>

          {tables.length === 0 ? (
            <p className="text-[11px] text-content-muted">
              This project has no tables yet.
            </p>
          ) : (
            <div className="space-y-1">
              {tables.map(table => {
                const permission = permissionFor(table.name);
                const busy = busyTable === table.name;
                return (
                  <div
                    key={table.id || table.name}
                    className="flex items-center gap-3 bg-surface-800 border border-content-primary/10 rounded-[var(--radius-control)] px-2.5 py-1.5"
                  >
                    <span className="flex-1 min-w-0 truncate text-[11px] font-mono text-content-secondary">
                      {table.name}
                    </span>
                    {busy && <Loader2 className="w-3 h-3 animate-spin text-content-muted" />}
                    <label className="flex items-center gap-1.5 text-[10px] text-content-secondary cursor-pointer select-none">
                      <input
                        type="checkbox"
                        checked={permission.canRead}
                        disabled={busy}
                        onChange={e => handleToggle(table.name, { canRead: e.target.checked, canWrite: permission.canWrite && e.target.checked })}
                        className="accent-accent"
                      />
                      read
                    </label>
                    <label className="flex items-center gap-1.5 text-[10px] text-content-secondary cursor-pointer select-none">
                      <input
                        type="checkbox"
                        checked={permission.canWrite}
                        disabled={busy}
                        onChange={e => handleToggle(table.name, { canRead: permission.canRead, canWrite: e.target.checked })}
                        className="accent-accent"
                      />
                      write
                    </label>
                  </div>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </section>
  );
}
