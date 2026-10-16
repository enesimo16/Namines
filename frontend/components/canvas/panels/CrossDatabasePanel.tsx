'use client';

import { useEffect, useState } from 'react';
import { Network, X, Plus, Trash2, Loader2, ArrowRight, ArrowLeft, Map } from 'lucide-react';
import CrossDatabaseMapView, { MapLink } from './CrossDatabaseMapView';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useToastStore } from '../../../store/useToastStore';
import { authService } from '../../../services/api';
import { DatabaseSchema } from '../../../types/schema';
import { useEscapeKey } from '../../../hooks/useEscapeKey';

interface Props {
  isOpen: boolean;
  onClose: () => void;
}

interface RelationRow {
  id: string;
  direction: 'outgoing' | 'incoming';
  localColumn: string;
  otherProjectId: string;
  otherProjectName: string;
  otherColumn: string;
  note: string | null;
  createdAt: string;
}

interface CloudProjectLite {
  id: string;
  name: string;
  schemaJson: string;
}

/**
 * second-phase/10-COKLU-DB.md — birden çok veritabanı (proje) arasındaki
 * mantıksal ilişkileri gösterir ve yönetir.
 *
 * <b>Bu ekranda kurulanlar GERÇEK bir yabancı anahtar değil</b> — veritabanı
 * bunu doğrulamaz, yalnızca Namines'in kaydıdır. Bu yüzden her satır kesikli
 * çerçeve ve açık bir "not enforced" etiketiyle gösteriliyor; aksi hâlde
 * kullanıcı veritabanının koruduğunu sanır (bkz. doc'un "olmayan bir güvenlik
 * hissi" uyarısı).
 *
 * <b>Kapsam bilerek dar:</b> "yan yana iki canlı canvas" (doc'un tam
 * tarifi) burada YOK — bu oturumda önceliklendirilen şey, ürünün asıl
 * iddiasını taşıyan kısım: ilişkiyi KAYDETMEK ve silme öncesi UYARMAK.
 */
export default function CrossDatabasePanel({ isOpen, onClose }: Props) {
  // Kapaliyken MONTE EDILMIYOR: onceki hali panel her acilista ONCEKI projenin
  // iliskilerini bir kare gosteriyordu (durum efektle degil, montajla
  // sifirlanmali).
  useEscapeKey(isOpen, onClose);
  if (!isOpen) return null;
  return <CrossDatabasePanelBody onClose={onClose} />;
}

function CrossDatabasePanelBody({ onClose }: { onClose: () => void }) {
  const schema = useSchemaStore(s => s.schema);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const showToast = useToastStore(s => s.showToast);

  const [relations, setRelations] = useState<RelationRow[]>([]);
  // Panel veri cekerek aciliyor; ilk karede "iliski yok" gosterilmemeli.
  const [isLoading, setIsLoading] = useState(true);
  const [isAdding, setIsAdding] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const [otherProjects, setOtherProjects] = useState<CloudProjectLite[]>([]);
  // Harita görünümü: hangi karşı projeyle açıldığı. Null = kapalı.
  const [mapForProjectId, setMapForProjectId] = useState<string | null>(null);
  const [mapSchema, setMapSchema] = useState<DatabaseSchema | null>(null);
  const projectName = useSchemaStore(s => s.projectName);
  const [otherProjectId, setOtherProjectId] = useState('');
  const [localTableId, setLocalTableId] = useState('');
  const [localColumnId, setLocalColumnId] = useState('');
  const [remoteTableId, setRemoteTableId] = useState('');
  const [remoteColumnId, setRemoteColumnId] = useState('');
  const [note, setNote] = useState('');

  /** Iliskileri ceker; yukleme bayragini cagirana birakir. */
  const fetchRelations = async () => {
    if (!activeProjectId) return;
    try {
      const data = await authService.crossDatabase.listRelations(activeProjectId);
      setRelations(data);
    } catch {
      showToast('Failed to load cross-database relations.', 'error');
    }
  };

  /** Yazma isleminden sonra yenile (spinner gostererek). */
  const loadRelations = async () => {
    setIsLoading(true);
    try {
      await fetchRelations();
    } finally {
      setIsLoading(false);
    }
  };

  // Aktif proje degisirse yeniden cekiliyor. `setIsLoading(true)` efekt icinde
  // SENKRON cagrilmiyor; ilk deger `true`, sonraki gecislerde kisa bir an eski
  // liste gorunur -- proje degisimi zaten tuvalin tamamini yeniliyor.
  useEffect(() => {
    let cancelled = false;
    fetchRelations().finally(() => {
      if (!cancelled) setIsLoading(false);
    });
    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeProjectId]);

  const startAdding = async () => {
    setIsAdding(true);
    try {
      const all = await authService.getCloudProjects();
      setOtherProjects(all.filter(p => p.id !== activeProjectId));
    } catch {
      showToast('Failed to load your other projects.', 'error');
      setIsAdding(false);
    }
  };

  const remoteSchema: DatabaseSchema | null = (() => {
    const p = otherProjects.find(p => p.id === otherProjectId);
    if (!p) return null;
    try { return JSON.parse(p.schemaJson); } catch { return null; }
  })();

  const localTable = schema?.tables.find(t => t.id === localTableId);
  const remoteTable = remoteSchema?.tables.find(t => t.id === remoteTableId);

  const resetAddForm = () => {
    setIsAdding(false);
    setOtherProjectId('');
    setLocalTableId('');
    setLocalColumnId('');
    setRemoteTableId('');
    setRemoteColumnId('');
    setNote('');
  };

  const handleCreate = async () => {
    if (!activeProjectId || !otherProjectId || !localTableId || !localColumnId || !remoteTableId || !remoteColumnId) return;
    setIsSubmitting(true);
    try {
      await authService.crossDatabase.createRelation({
        sourceProjectId: activeProjectId,
        sourceTableId: localTableId,
        sourceColumnId: localColumnId,
        targetProjectId: otherProjectId,
        targetTableId: remoteTableId,
        targetColumnId: remoteColumnId,
        note: note.trim() || undefined,
      });
      showToast('Cross-database relation saved.', 'success');
      resetAddForm();
      loadRelations();
    } catch {
      showToast('Failed to save the relation.', 'error');
    } finally {
      setIsSubmitting(false);
    }
  };

  /**
   * Haritayı açar. Karşı projenin şeması ihtiyaç ANINDA çekiliyor — paneli her
   * açışta tüm projelerin şemasını indirmek, yalnızca liste görmek isteyen
   * kullanıcıya gereksiz bir maliyet olurdu.
   */
  const openMap = async (otherProjectId: string) => {
    setMapForProjectId(otherProjectId);
    setMapSchema(null);
    try {
      const all = await authService.getCloudProjects();
      const target = all.find(p => p.id === otherProjectId);
      setMapSchema(target ? JSON.parse(target.schemaJson) : null);
    } catch {
      showToast('Could not load the other database.', 'error');
      setMapForProjectId(null);
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await authService.crossDatabase.deleteRelation(id);
      setRelations(prev => prev.filter(r => r.id !== id));
    } catch {
      showToast('Failed to delete the relation.', 'error');
    }
  };

  // Kapali durum kontrolu artik dis kabukta (bilesen kapaliyken hic monte
  // edilmiyor), burada tekrarlanmasi gerekmiyor.
  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200 select-none">
      <div className="bg-surface-900 border border-line-strong rounded-[var(--radius-modal)] w-[90vw] max-w-xl max-h-[85vh] flex flex-col shadow-2xl overflow-hidden">
        <div className="border-b border-line bg-surface-800/40 px-5 py-3.5 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-7 w-7 bg-surface-700 border border-line-strong rounded-[var(--radius-control)] flex items-center justify-center text-content-primary">
              <Network className="w-3.5 h-3.5" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">Cross-Database Relations</h2>
              <p className="text-micro text-content-muted">Logical links between distinct databases (not enforced)</p>
            </div>
          </div>
          <button onClick={onClose} className="p-1 rounded-[var(--radius-control)] text-content-muted hover:text-content-primary hover:bg-surface-700 transition-colors cursor-pointer" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          {!activeProjectId ? (
            <div className="text-center py-8">
              <p className="text-xs text-content-muted">Save this project first before linking it to another database.</p>
            </div>
          ) : isLoading ? (
            <div className="flex items-center justify-center gap-2 py-8 text-xs text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin text-content-primary" /> Loading relations…
            </div>
          ) : (
            <div className="flex flex-col gap-2.5">
              {relations.length === 0 && !isAdding && (
                <div className="text-center py-6 border border-dashed border-line rounded-[var(--radius-card)] bg-surface-800/30">
                  <p className="text-xs text-content-muted">No cross-database virtual relations defined yet.</p>
                </div>
              )}

              {relations.map(r => (
                <div key={r.id} className="border border-line bg-surface-800/70 rounded-[var(--radius-card)] p-3.5 flex items-center justify-between gap-3 shadow-xs">
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5 text-xs font-mono font-bold text-content-primary">
                      <span className="truncate">{r.direction === 'outgoing' ? r.localColumn : r.otherColumn}</span>
                      {r.direction === 'outgoing' ? <ArrowRight className="w-3 h-3 text-content-muted shrink-0" /> : <ArrowLeft className="w-3 h-3 text-content-muted shrink-0" />}
                      <span className="truncate">{r.direction === 'outgoing' ? r.otherColumn : r.localColumn}</span>
                    </div>
                    <div className="flex items-center gap-2 mt-1.5">
                      <span className="text-micro font-bold uppercase tracking-wider text-content-muted bg-surface-700/80 border border-line px-2 py-0.5 rounded-full">
                        Virtual Link
                      </span>
                      <span className="text-micro text-content-muted truncate">→ {r.otherProjectName}</span>
                    </div>
                    {r.note && <p className="text-micro text-content-subtle mt-1 truncate">{r.note}</p>}
                  </div>
                  <div className="flex items-center gap-1 shrink-0">
                    <button
                      onClick={() => openMap(r.otherProjectId)}
                      className="p-1.5 text-content-muted hover:text-content-primary hover:bg-surface-700 rounded-[var(--radius-control)] transition-colors cursor-pointer"
                      aria-label="See both databases side by side"
                      title="See both databases side by side"
                    >
                      <Map className="w-3.5 h-3.5" />
                    </button>
                    <button
                      onClick={() => handleDelete(r.id)}
                      className="p-1.5 text-content-muted hover:text-danger-text hover:bg-danger-subtle rounded-[var(--radius-control)] transition-colors cursor-pointer"
                      aria-label="Delete relation"
                    >
                      <Trash2 className="w-3.5 h-3.5" />
                    </button>
                  </div>
                </div>
              ))}

              {isAdding ? (
                <div className="border border-line-strong bg-surface-800/90 rounded-[var(--radius-card)] p-4 flex flex-col gap-3 mt-1 shadow-sm">
                  <label className="flex flex-col gap-1 text-micro font-bold uppercase tracking-wider text-content-muted">
                    Target Project
                    <select
                      value={otherProjectId}
                      onChange={e => { setOtherProjectId(e.target.value); setRemoteTableId(''); setRemoteColumnId(''); }}
                      className="bg-surface-700 border border-line focus:border-line-strong rounded-[var(--radius-control)] px-2.5 py-1.5 text-xs text-content-primary outline-none cursor-pointer"
                    >
                      <option value="">Select a project…</option>
                      {otherProjects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                  </label>

                  <div className="grid grid-cols-2 gap-3">
                    <label className="flex flex-col gap-1 text-micro font-bold uppercase tracking-wider text-content-muted">
                      Local Column
                      <select value={localTableId} onChange={e => { setLocalTableId(e.target.value); setLocalColumnId(''); }} className="bg-surface-700 border border-line rounded-[var(--radius-control)] px-2.5 py-1.5 text-xs text-content-primary outline-none cursor-pointer">
                        <option value="">Select table…</option>
                        {schema?.tables.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                      </select>
                      <select value={localColumnId} onChange={e => setLocalColumnId(e.target.value)} disabled={!localTable} className="bg-surface-700 border border-line rounded-[var(--radius-control)] px-2.5 py-1.5 text-xs text-content-primary disabled:opacity-40 outline-none cursor-pointer mt-1">
                        <option value="">Select column…</option>
                        {localTable?.columns.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                      </select>
                    </label>

                    <label className="flex flex-col gap-1 text-micro font-bold uppercase tracking-wider text-content-muted">
                      Foreign Column
                      <select value={remoteTableId} onChange={e => { setRemoteTableId(e.target.value); setRemoteColumnId(''); }} disabled={!remoteSchema} className="bg-surface-700 border border-line rounded-[var(--radius-control)] px-2.5 py-1.5 text-xs text-content-primary disabled:opacity-40 outline-none cursor-pointer">
                        <option value="">Select table…</option>
                        {remoteSchema?.tables.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                      </select>
                      <select value={remoteColumnId} onChange={e => setRemoteColumnId(e.target.value)} disabled={!remoteTable} className="bg-surface-700 border border-line rounded-[var(--radius-control)] px-2.5 py-1.5 text-xs text-content-primary disabled:opacity-40 outline-none cursor-pointer mt-1">
                        <option value="">Select column…</option>
                        {remoteTable?.columns.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                      </select>
                    </label>
                  </div>

                  <label className="flex flex-col gap-1 text-micro font-bold uppercase tracking-wider text-content-muted">
                    Note (Optional)
                    <input
                      value={note}
                      onChange={e => setNote(e.target.value)}
                      placeholder="e.g. shared tenant ID, global user reference"
                      className="bg-surface-700 border border-line focus:border-line-strong rounded-[var(--radius-control)] px-2.5 py-1.5 text-xs text-content-primary outline-none"
                    />
                  </label>

                  <div className="flex items-center justify-end gap-2 pt-2 border-t border-line">
                    <button onClick={resetAddForm} className="px-3 py-1.5 rounded-[var(--radius-control)] bg-surface-700 hover:bg-surface-600 text-content-secondary hover:text-content-primary text-xs font-semibold border border-line transition-all cursor-pointer">
                      Cancel
                    </button>
                    <button
                      onClick={handleCreate}
                      disabled={isSubmitting || !otherProjectId || !localColumnId || !remoteColumnId}
                      className="bg-content-primary hover:bg-content-secondary text-surface-900 px-3.5 py-1.5 rounded-[var(--radius-control)] text-xs font-bold transition-all disabled:opacity-50 cursor-pointer shadow-sm"
                    >
                      {isSubmitting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : 'Save Relation'}
                    </button>
                  </div>
                </div>
              ) : (
                <button
                  onClick={startAdding}
                  className="flex items-center justify-center gap-1.5 mt-1 px-3 py-2.5 rounded-[var(--radius-control)] border border-dashed border-line-strong bg-surface-800/40 hover:bg-surface-800 hover:border-content-primary text-content-muted hover:text-content-primary text-xs font-semibold transition-all cursor-pointer"
                >
                  <Plus className="w-3.5 h-3.5" /> Link to another database
                </button>
              )}
            </div>
          )}
        </div>
      </div>

      {/* Yan yana harita — second-phase/10-COKLU-DB.md */}
      {mapForProjectId && schema && (
        <CrossDatabaseMapView
          isOpen
          onClose={() => { setMapForProjectId(null); setMapSchema(null); }}
          localName={projectName}
          localSchema={schema}
          otherName={relations.find(r => r.otherProjectId === mapForProjectId)?.otherProjectName ?? 'other database'}
          otherSchema={mapSchema}
          links={relations
            .filter(r => r.otherProjectId === mapForProjectId)
            .map<MapLink>(r => ({
              id: r.id,
              localColumn: r.localColumn,
              otherColumn: r.otherColumn,
              note: r.note,
            }))}
        />
      )}
    </div>
  );
}
