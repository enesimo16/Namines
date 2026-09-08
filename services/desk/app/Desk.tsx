'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
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
import Members from './Members';
import BulkDeleteConfirm, { BULK_DELETE_THRESHOLD } from './BulkDeleteConfirm';
import { deploymentsApi } from '../lib/deployments';

const PAGE_SIZE = 25;
/** Sabit referans — `loadRows`'un useCallback kimliği her render'da değişmesin. */
const NO_FILTERS: GatewayFilter[] = [];
/** D5 §4.1 — yoklama aralığı. WebSocket değil: SignalR hub'ı şu an canvas
 * işbirliği için yapılandırılmış, Desk'i ona bağlamak ayrı bir iş. */
const VERSION_POLL_MS = 30_000;

/**
 * Namines Desk — proje kapsamındaki İÇERİK.
 *
 * <b>v2.1'de kabuk buradan çıktı:</b> sol gezinme, üst şerit ve görünüm
 * seçimi artık `AppShell` + `page.tsx`'in işi. Bu bileşen yalnızca seçili
 * görünümün içeriğini üretiyor — böylece kabuk, proje seçilmemişken de
 * (karşılama panosu) aynı biçimde çalışabiliyor.
 *
 * Görünüm ve seçili tablo DIŞARIDAN KONTROL EDİLİYOR (`view`, `activeTable`):
 * sol paneldeki tablo listesi kabukta yaşıyor, iki ayrı yerde ayrı bir "hangi
 * tablo seçili" durumu tutmak ikisinin ayrışmasına açık kapı bırakırdı.
 *
 * Hiçbir adımda bağlantı dizesi istemciye gelmiyor; sunucu onu oturum
 * sahibinin bu projeye erişimini doğruladıktan sonra çözüyor
 * (01-KIMLIK-VE-OTURUM.md §3).
 */
export default function Desk({
  session, designSchemaJson, nodePositionsJson, isOwner, allowDeskSql, onDeskSqlToggled,
  onChangeProject, view, activeTable, onSelectTable, onTablesLoaded,
}: {
  session: DeskSession;
  designSchemaJson: string | null;
  nodePositionsJson: string | null;
  /** Namines Desk v2 §E4.2 — yalnızca Owner SQL sekmesini kullanabilir. */
  isOwner: boolean;
  allowDeskSql: boolean;
  onDeskSqlToggled: () => void;
  onChangeProject: () => void;
  /** Kabuğun seçtiği görünüm. */
  view: DeskView;
  /** Kabuğun seçtiği tablo (Data görünümü için). */
  activeTable: string | null;
  onSelectTable: (name: string) => void;
  /** Şema okunduğunda kabuğun tablo listesini doldurabilmesi için. */
  onTablesLoaded: (tables: DeskTable[] | null) => void;
}) {
  // D5 §4.1 — "yeni sürüm bildirimi". En son görülen ChangeRequest id'si;
  // yoklama sonucu farklı bir id gelirse şerit gösterilir. `ChangeRequest`
  // zaten her yeni `SchemaVersion`'a eşlik ediyor (CreateQuick'in kendi
  // yorumu), yani bu, ayrı bir uç eklemeden aynı sinyali veriyor.
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

  // Namines Desk v2 §E4.1 — toplu silme. Seçim pk değerleriyle tutuluyor
  // (satır referansıyla değil) — sayfa değişse bile hangi kayıtların seçili
  // olduğu net kalır.
  const [selectedPks, setSelectedPks] = useState<Set<string>>(new Set());
  const [confirmingBulkDelete, setConfirmingBulkDelete] = useState(false);

  // D4 §2.2 — sıralama + filtre. Gateway zaten destekliyor (Filters/OrderByColumn/
  // SortDirection); bu tamamen bir arayüz durumu, backend değişikliği gerekmiyor.
  //
  // <b>Ölçütler HANGİ TABLOYA ait olduklarıyla birlikte tutuluyor.</b> Önceden
  // ayrı state'lerdi ve tablo değişince bir EFEKT onları sıfırlıyordu; efekt
  // sıfırlamayı bir sonraki render'a bıraktığı için ilk istek ESKİ tablonun
  // sıralama/filtresiyle gidiyordu. Yeni tabloda o kolon yoksa Gateway 400
  // döndürüyor, hata şeridi yanıp sönüyor ve her tablo geçişi iki istek
  // harcıyordu. Ölçütü tabloya bağlayıp TÜRETMEK bu yarışı tamamen kaldırıyor:
  // ilk render'da zaten doğru değerler okunuyor.
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
  // Filtre paneli VARSAYILAN KAPALI: her kolon icin bir kutu ciziliyor ve
  // 15 kolonlu bir tabloda bu, verinin kendisini ekrandan itip cikaran bir
  // giris duvarina donusuyordu. Acik filtre varsa panel kendiliginden acilir.
  const [filtersOpen, setFiltersOpen] = useState(false);

  const table = tables?.find(t => t.name === active) ?? null;

  // Sıralama/filtre sıfırlaması ARTIK BURADA DEĞİL — `criteria` tabloya bağlı
  // olduğu için türetiliyor (yukarı bkz.). Burada yalnızca seçim temizleniyor;
  // seçim hiçbir isteği beslemediği için efekt gecikmesi zararsız.
  useEffect(() => {
    setSelectedPks(new Set());
  }, [active]);

  const reloadSchema = useCallback(async () => {
    try {
      const res = await deskApi.schema(session);
      setTables(res.tables);
      // Varsayılan tabloyu BURADA seçmiyoruz: `onSelectTable` aynı zamanda
      // Data görünümüne geçiriyor, bu da projeyi açar açmaz Şema yerine ham
      // satırlara düşmek demek olurdu. Varsayılanı kabuk belirliyor
      // (`page.tsx` → handleTablesLoaded), görünümü değiştirmeden.
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Şema okunamadı.');
    }
  }, [session]);

  // Şema bir kez: tablo listesi ve form tanımlarının ikisi de buradan geliyor.
  useEffect(() => { reloadSchema(); }, [reloadSchema]);

  // Tablo listesi kabuğa (sol panel) bildiriliyor — orada ayrı bir şema
  // isteği atmak, aynı veriyi iki kez çekmek olurdu.
  useEffect(() => { onTablesLoaded(tables); }, [tables, onTablesLoaded]);

  // D5 §4.1 — yeni sürüm bildirimi. Desk açıkken ana uygulamada bir "Request
  // Review" yapılırsa üstte bir şerit çıkar; DDL ÇALIŞTIRILMAZ, yalnızca
  // Canvas/şemanın yeniden okunması tetiklenir (§4.2). §4.3'teki "doğrudan
  // veritabanına uygula" bilinçli olarak burada YOK — Vault'tan sonraya bırakıldı.
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
        // Yoklama sessizce atlanır — bu bir kullanıcı eylemi değil, arka plan
        // sinyali; başarısızlığı bir hata bildirimiyle göstermek gürültü olurdu.
      }
    }

    poll();
    const id = setInterval(poll, VERSION_POLL_MS);
    return () => { cancelled = true; clearInterval(id); };
  }, [session]);

  const loadRows = useCallback(async (tableName: string, p: number) => {
    setLoading(true);
    setError(null);
    try {
      const res = await deskApi.list(session, tableName, p, PAGE_SIZE, {
        orderByColumn: sortColumn, sortDirection: sortDir, filters: appliedFilters,
      });
      setRows(res.rows);
      setTotal(res.totalCount);
    } catch (err) {
      setRows([]);
      setError(err instanceof Error ? err.message : 'Satırlar okunamadı.');
    } finally {
      setLoading(false);
    }
  }, [session, sortColumn, sortDir, appliedFilters]);

  useEffect(() => {
    if (active) { setPage(1); loadRows(active, 1); }
    // sortColumn/sortDir/appliedFilters degistiginde de yeniden yuklenir —
    // loadRows zaten bunlara bagimli (useCallback deps), burada tekrar etmiyoruz.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [active, loadRows]);

  /** Ölçütü DAİMA aktif tabloya bağlayarak yazar — kısmi/karışık bir duruma düşmesin. */
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
      setError(err instanceof Error ? err.message : 'Dışa aktarılamadı.');
    } finally {
      setExporting(false);
    }
  }

  // D4 §2.1 — FK açılır listesi. 200'den fazla satır varsa (ölçek sınırı) ham
  // değer girişine düşülür; RowForm bu ayrımı 'too_many' ile kendisi gösterir.
  //
  // Hedef tablo başına önbelleklenir: aynı forma iki FK kolonu aynı hedefe
  // bakıyorsa (iki arama bekleme yerine bir), ya da kullanıcı arka arkaya
  // "Yeni satır" açıyorsa gereksiz tekrar istek atılmaz. `tables` değişince
  // (şema yeniden okunduğunda) YENİ bir Map üretilir — önbellek böylece
  // otomatik geçersiz kalır, elle temizleme gerekmez.
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
    // 'error' önbelleklenmez: geçici bir ağ hatası, form her açıldığında
    // kalıcı olarak ham girişe düşürmesin — bir sonraki açılışta yeniden denenir.
    promise.then(r => { if (r.kind === 'error') fkOptionsCache.delete(targetTableName); });
    return promise;
  }, [session, tables, fkOptionsCache]);

  async function handleDelete(row: Record<string, unknown>) {
    if (!table) return;
    const pk = primaryKey(table);
    if (!pk) return;
    // Silme geri alınamaz; onay bilinçli olarak satırı adlandırıyor ki
    // kullanıcı hangi kaydı sildiğini görsün.
    const shown = String(row[displayColumn(table)] ?? row[pk]);
    if (!confirm(`"${shown}" kalıcı olarak silinecek. Emin misiniz?`)) return;
    try {
      await deskApi.remove(session, table.name, pk, String(row[pk]));
      await loadRows(table.name, page);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Silinemedi.');
    }
  }

  function toggleRowSelected(pkValue: string) {
    setSelectedPks(prev => {
      const next = new Set(prev);
      if (next.has(pkValue)) next.delete(pkValue); else next.add(pkValue);
      return next;
    });
  }

  /**
   * Namines Desk v2 §E4.1 — toplu silme. 10'dan AZ seçimde tarayıcının kendi
   * `confirm()`'ü (tekil silmeyle tutarlı); 10 ve üzeri
   * <see cref="BulkDeleteConfirm" />'ün "SİL" yazma zorunluluğuna düşer
   * (34-SENDEN-BEKLENENLER.md madde 12).
   */
  function requestBulkDelete() {
    if (selectedPks.size === 0) return;
    if (selectedPks.size < BULK_DELETE_THRESHOLD) {
      if (!confirm(`${selectedPks.size} satır kalıcı olarak silinecek. Emin misiniz?`)) return;
      // Hata BURADA yakalanıyor: bu yol `BulkDeleteConfirm`'den geçmiyor, yani
      // modalın kendi try/catch'i devrede değil. Yakalanmadığında silme
      // başarısız oluyor, hiçbir uyarı çıkmıyor ve satırlar seçili kaldığı için
      // kullanıcı "sildim ama liste yenilenmedi" sanıyordu.
      void executeBulkDelete().catch(err => {
        setError(err instanceof Error ? err.message : 'Silinemedi.');
      });
      return;
    }
    setConfirmingBulkDelete(true);
  }

  /**
   * Hatayı BİLEREK yukarı fırlatır: 10+ seçimde çağıran `BulkDeleteConfirm`,
   * mesajı kendi modalinde göstermek için bunu bekliyor. 10'un altındaki yol
   * ise `requestBulkDelete` içinde yakalıyor.
   */
  async function executeBulkDelete() {
    if (!table) return;
    const pk = primaryKey(table);
    if (!pk) return;
    await deskApi.bulkRemove(session, table.name, pk, Array.from(selectedPks));
    setSelectedPks(new Set());
    setConfirmingBulkDelete(false);
    await loadRows(table.name, page);
  }

  if (error && !tables) {
    return (
      <div className="page">
        <div className="page-head">
          <div>
            <h1 className="page-title">Şema okunamadı</h1>
            <p className="page-desc">
              Bu projenin canlı veritabanına bağlanılamadı. Bağlantı bilgisi değişmiş olabilir.
            </p>
          </div>
        </div>
        <div className="notice notice-error">{error}</div>
        <button className="btn" onClick={onChangeProject}>Başka bir proje seç</button>
      </div>
    );
  }

  if (!tables) return <div className="page"><div className="empty empty-plain">Şema okunuyor…</div></div>;

  const pk = table ? primaryKey(table) : null;
  const editable = table ? isEditable(table) : false;
  const lastPage = total !== null ? Math.max(1, Math.ceil(total / PAGE_SIZE)) : page;

  return (
    <>
      <div className="desk-content">
        {newVersionBanner && (
          <div className="notice" style={{ margin: 0, borderRadius: 0, display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
            <span>Şema güncellendi — Canvas ve tablolar güncel olmayabilir.</span>
            <button className="btn btn-sm btn-primary" onClick={() => { setNewVersionBanner(false); reloadSchema(); }}>
              Yenile
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
        ) : view === 'members' ? (
          <Members session={session} />
        ) : (
        <>
        <div className="topbar">
          <div>
            <h1>{table?.name ?? 'Tablo seçin'}</h1>
            {table && (
              <div className="meta">
                {total !== null ? `${total} kayıt` : `${rows.length} kayıt`}
                {' · '}{table.columns.length} kolon
                {!editable && ' · salt-okunur'}
              </div>
            )}
          </div>
          {table && (
            <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap' }}>
              <button
                className="btn btn-sm"
                aria-current={filtersOpen}
                onClick={() => setFiltersOpen(o => !o)}
                title="Sütun filtreleri"
              >
                Filtreler{appliedFilters.length > 0 ? ` (${appliedFilters.length})` : ''}
              </button>
              <button className="btn btn-sm" disabled={exporting} onClick={() => handleExport('csv')}>
                {exporting ? 'Aktarılıyor…' : 'CSV'}
              </button>
              <button className="btn btn-sm" disabled={exporting} onClick={() => handleExport('json')}>
                {exporting ? 'Aktarılıyor…' : 'JSON'}
              </button>
              {editable && selectedPks.size > 0 && (
                <button className="btn btn-sm btn-danger" onClick={requestBulkDelete}>
                  Seçilenleri sil ({selectedPks.size})
                </button>
              )}
              {editable && (
                <button className="btn btn-primary" onClick={() => setEditing(null)}>Yeni satır</button>
              )}
            </div>
          )}
        </div>

        <div className="page">
          {error && <div className="notice notice-error">{error}</div>}
          {table && !editable && (
            <div className="notice">
              Bu tablo salt-okunur.{' '}
              {!table.canWrite
                ? 'API anahtarının bu tabloya yazma izni yok.'
                : 'Tek kolonlu birincil anahtarı olmadığı için güvenle güncellenemiyor — bileşik anahtarlı bir satırı yanlış eşleştirmek başka bir kaydı değiştirebilirdi.'}
            </div>
          )}

          {table && (filtersOpen || appliedFilters.length > 0) && (
            <div className="grid-wrap" style={{ padding: 10, marginBottom: 12, display: 'flex', flexWrap: 'wrap', gap: 8, alignItems: 'flex-end' }}>
              {table.columns.map(c => {
                const kind = fieldKind(c);
                const d = filterDraft[c.name] ?? {};
                if (kind === 'boolean') {
                  return (
                    <div key={c.name} className="field" style={{ margin: 0 }}>
                      <label>{c.name}</label>
                      <select value={d.eq ?? ''} onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { eq: e.target.value || undefined } }))}>
                        <option value="">Tümü</option>
                        <option value="true">Evet</option>
                        <option value="false">Hayır</option>
                      </select>
                    </div>
                  );
                }
                if (kind === 'number' || kind === 'date' || kind === 'datetime') {
                  const inputType = kind === 'number' ? 'number' : kind === 'datetime' ? 'datetime-local' : 'date';
                  return (
                    <div key={c.name} className="field" style={{ margin: 0, display: 'flex', gap: 4 }}>
                      <div>
                        <label>{c.name} ≥</label>
                        <input type={inputType} value={d.min ?? ''} style={{ width: 120 }}
                               onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { ...prev[c.name], min: e.target.value || undefined } }))} />
                      </div>
                      <div>
                        <label>{c.name} ≤</label>
                        <input type={inputType} value={d.max ?? ''} style={{ width: 120 }}
                               onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { ...prev[c.name], max: e.target.value || undefined } }))} />
                      </div>
                    </div>
                  );
                }
                return (
                  <div key={c.name} className="field" style={{ margin: 0 }}>
                    <label>{c.name} içerir</label>
                    <input type="text" value={d.eq ?? ''} style={{ width: 140 }}
                           onChange={e => setFilterDraft(prev => ({ ...prev, [c.name]: { eq: e.target.value || undefined } }))} />
                  </div>
                );
              })}
              <button className="btn btn-sm btn-primary" onClick={applyFilters}>Filtrele</button>
              {appliedFilters.length > 0 && (
                <button className="btn btn-sm" onClick={() => setCriteria({ table: active, sortColumn, sortDir, filters: NO_FILTERS, draft: {} })}>Temizle</button>
              )}
            </div>
          )}

          {loading ? (
            <div className="empty">Yükleniyor…</div>
          ) : !table ? (
            <div className="empty">Soldan bir tablo seçin.</div>
          ) : rows.length === 0 ? (
            <div className="empty">Bu tabloda kayıt yok.</div>
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
                        <th key={c.name} style={{ cursor: 'pointer', userSelect: 'none' }}
                            onClick={() => toggleSort(c.name)} title="Sıralamak için tıklayın">
                          {c.name}
                          {sortColumn === c.name && <span className="col-badge">{sortDir === 'Asc' ? '▲' : '▼'}</span>}
                          {c.isPK && <span className="col-badge">PK</span>}
                          {c.references && <span className="col-badge">→{c.references.table}</span>}
                        </th>
                      ))}
                      {editable && <th className="col-actions" style={{ textAlign: 'right' }}>İşlem</th>}
                    </tr>
                  </thead>
                  <tbody>
                    {rows.map((r, i) => (
                      <tr key={pk ? String(r.values[pk]) : i}>
                        {editable && pk && (
                          <td className="col-check">
                            <input
                              type="checkbox"
                              checked={selectedPks.has(String(r.values[pk]))}
                              onChange={() => toggleRowSelected(String(r.values[pk]))}
                            />
                          </td>
                        )}
                        {table.columns.map(c => (
                          <td key={c.name} className={c.isPK ? 'pk-cell' : undefined}>
                            {formatCell(r.values[c.name])}
                          </td>
                        ))}
                        {editable && (
                          <td className="col-actions">
                            <div className="row-actions" style={{ justifyContent: 'flex-end' }}>
                              <button className="btn btn-sm" onClick={() => setEditing(r.values)}>Düzenle</button>
                              <button className="btn btn-sm btn-danger" onClick={() => handleDelete(r.values)}>Sil</button>
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
                        onClick={() => { const p = page - 1; setPage(p); loadRows(table.name, p); }}>Önceki</button>
                <span>{page} / {lastPage}</span>
                <button className="btn btn-sm" disabled={page >= lastPage}
                        onClick={() => { const p = page + 1; setPage(p); loadRows(table.name, p); }}>Sonraki</button>
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
