'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import EmptyState from './EmptyState';
import {
  deskApi, DeskApiError, type DeskRow, type DeskSession,
  type GatewayFilter, type GatewaySortDirection,
} from '../lib/api';
import type { DeskView } from '../lib/nav';
import { type DeskTable, fieldKind, primaryKey, isEditable, displayColumn, formatCell } from '../lib/schema';
import RowForm, { type FkOptionsResult } from './RowForm';
import Canvas from './Canvas';
import Deployments from './Deployments';
import Logs from './Logs';
import Analytics from './Analytics';
import SqlConsole from './SqlConsole';
import ApiKeys from './ApiKeys';
import Vault from './Vault';
import Ground from './Ground';
import Members from './Members';
import BulkDeleteConfirm, { BULK_DELETE_THRESHOLD } from './BulkDeleteConfirm';
import { deploymentsApi } from '../lib/deployments';
import { useT } from '../lib/i18n';
import { ChevronsUpDown, Check } from 'lucide-react';

const PAGE_SIZE = 25;
/** Sabit referans — `loadRows`'un useCallback kimliği her render'da değişmesin. */
const NO_FILTERS: GatewayFilter[] = [];
const VERSION_POLL_MS = 30_000;

/**
 * Namines Desk — proje kapsamındaki İÇERİK.
 *
 * Shopify Polaris Düzeni + Cloudflare Dashboard Estetiği:
 * - Hızlı tablo değiştirici (Topbar Quick Switcher)
 * - Yüksek yoğunluklu veri ızgarası (High Density)
 * - Tabular monospaced veriler (Geist Mono ile PK, UUID, sayılar)
 * - Mükerrer bildirimlerin temizlenmesi
 */
export default function Desk({
  session, designSchemaJson, nodePositionsJson, isOwner, allowDeskSql, onDeskSqlToggled,
  onChangeProject, view, activeTable, onSelectTable, onTablesLoaded,
}: {
  session: DeskSession;
  designSchemaJson: string | null;
  nodePositionsJson: string | null;
  isOwner: boolean;
  allowDeskSql: boolean;
  onDeskSqlToggled: () => void;
  onChangeProject: () => void;
  view: DeskView;
  activeTable: string | null;
  onSelectTable: (name: string) => void;
  onTablesLoaded: (tables: DeskTable[] | null) => void;
}) {
  const t = useT();
  const [latestVersionSeen, setLatestVersionSeen] = useState<string | null>(null);
  const [newVersionBanner, setNewVersionBanner] = useState(false);
  const [tables, setTables] = useState<DeskTable[] | null>(null);
  const active = activeTable;
  const [rows, setRows] = useState<DeskRow[]>([]);
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState<number | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Record<string, unknown> | null | undefined>(undefined);

  const [selectedPks, setSelectedPks] = useState<Set<string>>(new Set());
  const [confirmingBulkDelete, setConfirmingBulkDelete] = useState(false);

  // Topbar Hızlı Tablo Seçici Popover
  const [tableSwitcherOpen, setTableSwitcherOpen] = useState(false);
  const [tableSearch, setTableSearch] = useState('');
  const tableSwitcherRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!tableSwitcherOpen) return;
    function onClick(e: MouseEvent) {
      if (tableSwitcherRef.current && !tableSwitcherRef.current.contains(e.target as Node)) {
        setTableSwitcherOpen(false);
      }
    }
    document.addEventListener('mousedown', onClick);
    return () => document.removeEventListener('mousedown', onClick);
  }, [tableSwitcherOpen]);

  const [criteria, setCriteria] = useState<{
    table: string | null;
    sortColumn: string | null;
    sortDir: GatewaySortDirection;
    filters: GatewayFilter[];
    draft: Record<string, { min?: string; max?: string; eq?: string }>;
  }>({ table: null, sortColumn: null, sortDir: 'Asc', filters: NO_FILTERS, draft: {} });

  const criteriaApply = criteria.table === active;
  const sortColumn = criteriaApply ? criteria.sortColumn : null;
  const sortDir = criteriaApply ? criteria.sortDir : 'Asc';
  const appliedFilters = criteriaApply ? criteria.filters : NO_FILTERS;
  const filterDraft = criteriaApply ? criteria.draft : {};

  const setFilterDraft = useCallback((
    update: (prev: Record<string, { min?: string; max?: string; eq?: string }>) => Record<string, { min?: string; max?: string; eq?: string }>,
  ) => {
    setCriteria(prev => {
      const base = prev.table === active ? prev : { ...prev, table: active, sortColumn: null, sortDir: 'Asc' as GatewaySortDirection, filters: NO_FILTERS, draft: {} };
      return { ...base, draft: update(base.draft) };
    });
  }, [active]);
  const [exporting, setExporting] = useState(false);
  const [filtersOpen, setFiltersOpen] = useState(false);

  const table = tables?.find(t => t.name === active) ?? null;

  useEffect(() => {
    setSelectedPks(new Set());
  }, [active, sortColumn, sortDir, appliedFilters]);

  const reloadSchema = useCallback(async () => {
    try {
      const res = await deskApi.schema(session);
      setTables(res.tables);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('data.schemaLoadFailed'));
    }
  }, [session, t]);

  useEffect(() => { reloadSchema(); }, [reloadSchema]);
  useEffect(() => { onTablesLoaded(tables); }, [tables, onTablesLoaded]);

  useEffect(() => {
    let cancelled = false;

    async function poll() {
      try {
        const list = await deploymentsApi.list(session, session.projectId);
        if (cancelled || list.length === 0) return;
        const latestId = list[0].id;
        setLatestVersionSeen(prev => {
          if (prev !== null && prev !== latestId) setNewVersionBanner(true);
          return latestId;
        });
      } catch {
        // Yoklama sessizce atlanır
      }
    }

    poll();
    const id = setInterval(poll, VERSION_POLL_MS);
    return () => { cancelled = true; clearInterval(id); };
  }, [session]);

  const latestRequestRef = useRef<symbol | null>(null);

  const loadRows = useCallback(async (tableName: string, p: number) => {
    const requestToken = Symbol();
    latestRequestRef.current = requestToken;
    setLoading(true);
    setError(null);
    try {
      const res = await deskApi.list(session, tableName, p, PAGE_SIZE, {
        orderByColumn: sortColumn, sortDirection: sortDir, filters: appliedFilters,
      });
      if (latestRequestRef.current !== requestToken) return;
      setRows(res.rows);
      setTotal(res.totalCount);
    } catch (err) {
      if (latestRequestRef.current !== requestToken) return;
      setRows([]);
      setError(err instanceof Error ? err.message : t('data.rowsLoadFailed'));
    } finally {
      if (latestRequestRef.current === requestToken) setLoading(false);
    }
  }, [session, sortColumn, sortDir, appliedFilters, t]);

  useEffect(() => {
    return () => { latestRequestRef.current = null; };
  }, []);

  useEffect(() => {
    if (active) { setPage(1); loadRows(active, 1); }
  }, [active, loadRows]);

  function updateCriteria(patch: Partial<{ sortColumn: string | null; sortDir: GatewaySortDirection; filters: GatewayFilter[] }>) {
    setCriteria(prev => {
      const base = prev.table === active
        ? prev
        : { table: active, sortColumn: null, sortDir: 'Asc' as GatewaySortDirection, filters: NO_FILTERS, draft: {} };
      return { ...base, table: active, ...patch };
    });
  }

  function toggleSort(columnName: string) {
    if (sortColumn !== columnName) { updateCriteria({ sortColumn: columnName, sortDir: 'Asc' }); return; }
    if (sortDir === 'Asc') { updateCriteria({ sortDir: 'Desc' }); return; }
    updateCriteria({ sortColumn: null, sortDir: 'Asc' });
  }

  function applyFilters() {
    if (!table) return;
    const built: GatewayFilter[] = [];
    for (const c of table.columns) {
      const d = filterDraft[c.name];
      if (!d) continue;
      const kind = fieldKind(c);
      if (kind === 'boolean') {
        if (d.eq === 'true' || d.eq === 'false') built.push({ column: c.name, operator: 'Eq', values: [d.eq] });
      } else if (kind === 'number' || kind === 'date' || kind === 'datetime') {
        if (d.min) built.push({ column: c.name, operator: 'Gte', values: [d.min] });
        if (d.max) built.push({ column: c.name, operator: 'Lte', values: [d.max] });
      } else if (d.eq) {
        built.push({ column: c.name, operator: 'Like', values: [`%${d.eq}%`] });
      }
    }
    updateCriteria({ filters: built });
  }

  async function handleExport(format: 'csv' | 'json') {
    if (!table) return;
    setExporting(true);
    try {
      const { blob, fileName } = await deskApi.export(session, table.name, format, {
        orderByColumn: sortColumn, sortDirection: sortDir, filters: appliedFilters,
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url; a.download = fileName; a.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('data.exportFailed'));
    } finally {
      setExporting(false);
    }
  }

  const fkOptionsCache = useMemo(() => new Map<string, Promise<FkOptionsResult>>(), [tables]);

  const fetchFkOptions = useCallback((targetTableName: string): Promise<FkOptionsResult> => {
    const cached = fkOptionsCache.get(targetTableName);
    if (cached) return cached;

    const promise = (async (): Promise<FkOptionsResult> => {
      const targetTable = tables?.find(t => t.name === targetTableName);
      if (!targetTable) return { kind: 'error' };
      const pk = primaryKey(targetTable);
      if (!pk) return { kind: 'error' };
      const disp = displayColumn(targetTable);
      try {
        const res = await deskApi.list(session, targetTableName, 1, 201, { select: Array.from(new Set([pk, disp])) });
        if (res.rows.length > 200 || (res.totalCount ?? 0) > 200) return { kind: 'too_many' };
        return {
          kind: 'options',
          options: res.rows.map(r => ({
            value: String(r.values[pk]),
            label: String(r.values[disp] ?? r.values[pk]),
          })),
        };
      } catch {
        return { kind: 'error' };
      }
    })();

    fkOptionsCache.set(targetTableName, promise);
    promise.then(r => { if (r.kind === 'error') fkOptionsCache.delete(targetTableName); });
    return promise;
  }, [session, tables, fkOptionsCache]);

  async function handleDelete(row: Record<string, unknown>) {
    if (!table) return;
    const pk = primaryKey(table);
    if (!pk) return;
    const shown = String(row[displayColumn(table)] ?? row[pk]);
    if (!confirm(t('data.confirmDelete', { name: shown }))) return;
    try {
      await deskApi.remove(session, table.name, pk, String(row[pk]));
      await loadRows(table.name, page);
    } catch (err) {
      setError(err instanceof Error ? err.message : t('common.deleteFailed'));
    }
  }

  function toggleRowSelected(pkValue: string) {
    setSelectedPks(prev => {
      const next = new Set(prev);
      if (next.has(pkValue)) next.delete(pkValue); else next.add(pkValue);
      return next;
    });
  }

  function requestBulkDelete() {
    if (selectedPks.size === 0) return;
    if (selectedPks.size < BULK_DELETE_THRESHOLD) {
      if (!confirm(t(selectedPks.size === 1 ? 'data.confirmBulkOne' : 'data.confirmBulkOther', { count: selectedPks.size }))) return;
      void executeBulkDelete().catch(err => {
        setError(err instanceof Error ? err.message : t('common.deleteFailed'));
      });
      return;
    }
    setConfirmingBulkDelete(true);
  }

  async function executeBulkDelete() {
    if (!table) return;
    const pk = primaryKey(table);
    if (!pk) return;
    await deskApi.bulkRemove(session, table.name, pk, Array.from(selectedPks));
    setSelectedPks(new Set());
    setConfirmingBulkDelete(false);
    await loadRows(table.name, page);
  }

  if (view === 'ground') return <Ground session={session} isOwner={isOwner} />;
  if (!tables) {
    if (view === 'deployments') return <div className="desk-content"><Deployments session={session} /></div>;
    if (view === 'logs') return <div className="desk-content"><Logs session={session} /></div>;
    if (view === 'apikeys') return <div className="desk-content"><ApiKeys session={session} isOwner={isOwner} /></div>;
    if (view === 'vault') return <div className="desk-content"><Vault session={session} isOwner={isOwner} /></div>;
    if (view === 'members') return <div className="desk-content"><Members session={session} /></div>;
    if (view === 'sql') return <div className="desk-content"><SqlConsole session={session} isOwner={isOwner} allowDeskSql={allowDeskSql} onToggled={onDeskSqlToggled} /></div>;
  }

  if (error && !tables) {
    return (
      <div className="page">
        <div className="page-head">
          <div>
            <h1 className="page-title">{t('data.schemaErrorTitle')}</h1>
            <p className="page-desc">{t('data.schemaErrorDesc')}</p>
          </div>
        </div>
        <div className="notice notice-error">{error}</div>
        <button className="btn" onClick={onChangeProject}>{t('data.chooseAnother')}</button>
      </div>
    );
  }

  if (!tables) return <div className="page"><div className="empty empty-plain">{t('data.loadingSchema')}</div></div>;

  const pk = table ? primaryKey(table) : null;
  const editable = table ? isEditable(table) : false;
  const lastPage = total !== null ? Math.max(1, Math.ceil(total / PAGE_SIZE)) : page;

  return (
    <>
      <div className="desk-content">
        {newVersionBanner && (
          <div className="notice" style={{ margin: 0, borderRadius: 0, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span>{t('data.schemaUpdated')}</span>
            <button className="btn btn-sm btn-primary" onClick={() => { setNewVersionBanner(false); reloadSchema(); }}>
              {t('data.reload')}
            </button>
          </div>
        )}
        {view === 'canvas' ? (
          <Canvas
            tables={tables}
            schemaJson={designSchemaJson}
            nodePositionsJson={nodePositionsJson}
            onOpenTable={name => onSelectTable(name)}
          />
        ) : view === 'deployments' ? (
          <Deployments session={session} />
        ) : view === 'logs' ? (
          <Logs session={session} />
        ) : view === 'analytics' ? (
          <Analytics session={session} tables={tables} />
        ) : view === 'sql' ? (
          <SqlConsole session={session} isOwner={isOwner} allowDeskSql={allowDeskSql} onToggled={onDeskSqlToggled} />
        ) : view === 'apikeys' ? (
          <ApiKeys session={session} isOwner={isOwner} />
        ) : view === 'vault' ? (
          <Vault session={session} isOwner={isOwner} />
        ) : view === 'members' ? (
          <Members session={session} />
        ) : (
        <>
        <div className="topbar">
          <div>
            <div className="table-quick-select-wrap" ref={tableSwitcherRef}>
              <button
                type="button"
                className="table-quick-select-btn"
                onClick={() => setTableSwitcherOpen(o => !o)}
                aria-haspopup="listbox"
                aria-expanded={tableSwitcherOpen}
                title={t('data.selectTable')}
              >
                <span>{table?.name ?? t('data.selectTable')}</span>
                <ChevronsUpDown size={14} style={{ color: 'var(--content-muted)' }} />
              </button>

              {tableSwitcherOpen && tables && (
                <div className="table-quick-popover" role="listbox">
                  <div className="table-quick-search">
                    <input
                      type="search"
                      value={tableSearch}
                      autoFocus
                      placeholder={t('shell.search')}
                      onChange={e => setTableSearch(e.target.value)}
                    />
                  </div>
                  <div className="table-quick-list">
                    {tables
                      .filter(t => !tableSearch || t.name.toLowerCase().includes(tableSearch.toLowerCase()))
                      .map(t => (
                        <button
                          key={t.name}
                          type="button"
                          className="table-quick-item"
                          role="option"
                          aria-selected={t.name === active}
                          onClick={() => {
                            setTableSwitcherOpen(false);
                            setTableSearch('');
                            onSelectTable(t.name);
                          }}
                        >
                          <span>{t.name}</span>
                          {t.name === active && <Check size={13} style={{ color: 'var(--accent-text)' }} />}
                        </button>
                      ))}
                  </div>
                </div>
              )}
            </div>

            {table && (
              <div className="meta">
                <span className="mono-cell">
                  {t((total ?? rows.length) === 1 ? 'common.rowsOne' : 'common.rowsOther', { count: total ?? rows.length })}
                </span>
                {' · '}
                <span className="mono-cell">
                  {t('common.columns', { count: table.columns.length })}
                </span>
                {!editable && (
                  <span
                    className="badge badge-off"
                    style={{ marginLeft: 8 }}
                    title={!table.canWrite ? t('data.noWritePermission') : t('data.noSinglePk')}
                  >
                    {t('data.readOnly')}
                  </span>
                )}
              </div>
            )}
          </div>

          {table && (
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              <button
                className="btn btn-sm"
                aria-current={filtersOpen}
                onClick={() => setFiltersOpen(o => !o)}
                title={t('data.filtersTitle')}
              >
                {appliedFilters.length > 0 ? t('data.filtersCount', { count: appliedFilters.length }) : t('data.filters')}
              </button>
              <button className="btn btn-sm" disabled={exporting} onClick={() => handleExport('csv')}>
                {exporting ? t('data.exporting') : 'CSV'}
              </button>
              <button className="btn btn-sm" disabled={exporting} onClick={() => handleExport('json')}>
                {exporting ? t('data.exporting') : 'JSON'}
              </button>
              {editable && selectedPks.size > 0 && (
                <button className="btn btn-sm btn-danger" onClick={requestBulkDelete}>
                  {t('data.deleteSelected', { count: selectedPks.size })}
                </button>
              )}
              {editable && (
                <button className="btn btn-primary" onClick={() => setEditing(null)}>{t('data.newRow')}</button>
              )}
            </div>
          )}
        </div>

        <div className="page">
          {error && <div className="notice notice-error">{error}</div>}

          {table && (filtersOpen || appliedFilters.length > 0) && (
            <div className="grid-wrap" style={{ padding: 10, marginBottom: 12, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
              {table.columns.map(c => {
                const kind = fieldKind(c);
                const d = filterDraft[c.name] ?? {};
                if (kind === 'boolean') {
                  return (
                    <div key={c.name} className="field" style={{ margin: 0 }}>
                      <label>{c.name}</label>
                      <select aria-label={c.name} value={d.eq ?? ''} onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { eq: e.target.value || undefined } }))}>
                        <option value="">{t('data.all')}</option>
                        <option value="true">{t('data.yes')}</option>
                        <option value="false">{t('data.no')}</option>
                      </select>
                    </div>
                  );
                }
                if (kind === 'number' || kind === 'date' || kind === 'datetime') {
                  return (
                    <div key={c.name} className="field" style={{ margin: 0 }}>
                      <label>{c.name}</label>
                      <div className="row" style={{ gap: 4 }}>
                        <input
                          type={kind === 'number' ? 'number' : kind === 'date' ? 'date' : 'datetime-local'}
                          placeholder="Min"
                          aria-label={`${c.name} min`}
                          style={{ width: 110 }}
                          value={d.min ?? ''}
                          onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { ...prev[c.name], min: e.target.value || undefined } }))}
                        />
                        <input
                          type={kind === 'number' ? 'number' : kind === 'date' ? 'date' : 'datetime-local'}
                          placeholder="Max"
                          aria-label={`${c.name} max`}
                          style={{ width: 110 }}
                          value={d.max ?? ''}
                          onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { ...prev[c.name], max: e.target.value || undefined } }))}
                        />
                      </div>
                    </div>
                  );
                }
                return (
                  <div key={c.name} className="field" style={{ margin: 0 }}>
                    <label>{c.name}</label>
                    <input
                      type="text"
                      placeholder={t('data.contains')}
                      aria-label={c.name}
                      style={{ width: 140 }}
                      value={d.eq ?? ''}
                      onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { eq: e.target.value || undefined } }))}
                    />
                  </div>
                );
              })}
              <button className="btn btn-sm btn-primary" onClick={applyFilters}>{t('data.filter')}</button>
              {appliedFilters.length > 0 && (
                <button className="btn btn-sm" onClick={() => setCriteria({ table: active, sortColumn, sortDir, filters: NO_FILTERS, draft: {} })}>{t('data.clear')}</button>
              )}
            </div>
          )}

          {loading ? (
            <div className="empty">{t('common.loading')}</div>
          ) : !table ? (
            <EmptyState
              title={t('data.selectTable')}
              description={t('data.selectTableDesc')}
            />
          ) : rows.length === 0 ? (
            <EmptyState
              title={t('data.emptyTitle')}
              description={t('data.emptyDesc')}
            />
          ) : (
            <>
              <div className="grid-wrap">
                <table className="data-table">
                  <thead>
                    <tr>
                      {editable && pk && (
                        <th className="col-check" style={{ width: 28 }}>
                          <input
                            type="checkbox"
                            aria-label={t('data.selectAll')}
                            checked={rows.length > 0 && rows.every(r => selectedPks.has(String(r.values[pk])))}
                            onChange={e => {
                              setSelectedPks(prev => {
                                const next = new Set(prev);
                                for (const r of rows) {
                                  const v = String(r.values[pk]);
                                  if (e.target.checked) next.add(v); else next.delete(v);
                                }
                                return next;
                              });
                            }}
                          />
                        </th>
                      )}
                      {table.columns.map(c => (
                        <th key={c.name} lang="en" style={{ cursor: 'pointer', userSelect: 'none' }}
                            onClick={() => toggleSort(c.name)} title={t('data.clickToSort')}>
                          {c.name}
                          {sortColumn === c.name && <span className="col-badge" style={{ color: 'var(--accent-text)' }}>{sortDir === 'Asc' ? '↑' : '↓'}</span>}
                          {c.isPK && <span className="col-badge">PK</span>}
                          {c.references && <span className="col-badge">→{c.references.table}</span>}
                        </th>
                      ))}
                      {editable && <th className="col-actions" style={{ textAlign: 'right' }}>{t('common.action')}</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((r, i) => (
                      <tr key={pk ? String(r.values[pk]) : i}>
                        {editable && pk && (
                          <td className="col-check">
                            <input
                              type="checkbox"
                              aria-label={t('data.selectRow', { key: String(r.values[pk]) })}
                              checked={selectedPks.has(String(r.values[pk]))}
                              onChange={() => toggleRowSelected(String(r.values[pk]))}
                            />
                          </td>
                        )}
                        {table.columns.map(c => {
                          const kind = fieldKind(c);
                          const isMono = c.isPK || kind === 'number' || kind === 'date' || kind === 'datetime';
                          return (
                            <td key={c.name} className={c.isPK ? 'pk-cell' : isMono ? 'mono-cell' : undefined}>
                              {formatCell(r.values[c.name])}
                            </td>
                          );
                        })}
                        {editable && (
                          <td className="col-actions">
                            <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                              <button className="btn btn-sm" onClick={() => setEditing(r.values)}>{t('data.edit')}</button>
                              <button className="btn btn-sm btn-danger" onClick={() => handleDelete(r.values)}>{t('common.delete')}</button>
                            </div>
                          </td>
                        )}
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="pager">
                <button className="btn btn-sm" disabled={page <= 1}
                        onClick={() => { const p = page - 1; setPage(p); loadRows(table.name, p); }}>{t('common.previous')}</button>
                <span>{page} / {lastPage}</span>
                <button className="btn btn-sm" disabled={page >= lastPage}
                        onClick={() => { const p = page + 1; setPage(p); loadRows(table.name, p); }}>{t('common.next')}</button>
              </div>
            </>
          )}
        </div>
        </>
        )}
      </div>

      {table && editing !== undefined && (
        <RowForm
          table={table}
          initial={editing}
          fetchFkOptions={fetchFkOptions}
          onCancel={() => setEditing(undefined)}
          onSubmit={async values => {
            if (editing === null) {
              await deskApi.create(session, table.name, values);
            } else if (pk) {
              await deskApi.update(session, table.name, pk, String(editing[pk]), values);
            }
            setEditing(undefined);
            await loadRows(table.name, page);
          }}
        />
      )}

      {confirmingBulkDelete && (
        <BulkDeleteConfirm
          count={selectedPks.size}
          onCancel={() => setConfirmingBulkDelete(false)}
          onConfirm={executeBulkDelete}
        />
      )}
    </>
  );
}

export { DeskApiError };
