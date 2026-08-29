'use client';

import { useEffect, useState } from 'react';
import { Network, X, Plus, Trash2, Loader2, ArrowRight, ArrowLeft, Map } from 'lucide-react';
import CrossDatabaseMapView, { MapLink } from './CrossDatabaseMapView';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useToastStore } from '../../../store/useToastStore';
import { authService } from '../../../services/api';
import { DatabaseSchema } from '../../../types/schema';

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
  const schema = useSchemaStore(s => s.schema);
  const activeProjectId = useProjectHistoryStore(s => s.activeProjectId);
  const showToast = useToastStore(s => s.showToast);

  const [relations, setRelations] = useState<RelationRow[]>([]);
  const [isLoading, setIsLoading] = useState(false);
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

  const loadRelations = async () => {
    if (!activeProjectId) return;
    setIsLoading(true);
    try {
      const data = await authService.crossDatabase.listRelations(activeProjectId);
      setRelations(data);
    } catch {
      showToast('Failed to load cross-database relations.', 'error');
    } finally {
      setIsLoading(false);
    }
  };

  useEffect(() => {
    if (isOpen) loadRelations();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isOpen, activeProjectId]);

  const startAdding = async () => {
    setIsAdding(true);
    try {
      const all = await authService.getCloudProjects();
      setOtherProjects(all.filter((p: any) => p.id !== activeProjectId));
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
      const target = all.find((p: any) => p.id === otherProjectId);
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

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-200">
      <div className="bg-surface-800 border border-content-primary/12 rounded-2xl w-[90vw] max-w-xl max-h-[85vh] flex flex-col shadow-[0_20px_60px_rgba(0,0,0,0.6)] overflow-hidden">
        <div className="border-b border-content-primary/10 px-5 py-3.5 flex items-center justify-between shrink-0">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 bg-surface-600 border border-content-primary/10 rounded-lg flex items-center justify-center">
              <Network className="w-4 h-4 text-content-primary" />
            </div>
            <div>
              <h2 className="text-sm font-bold text-content-primary">Cross-Database Relations</h2>
              <p className="text-[11px] text-content-muted">Logical links to other projects — not enforced by any database.</p>
            </div>
          </div>
          <button onClick={onClose} className="p-1.5 hover:bg-white/[0.06] rounded-lg text-content-subtle hover:text-content-primary transition-colors" aria-label="Close">
            <X className="w-4 h-4" />
          </button>
        </div>

        <div className="flex-1 overflow-y-auto p-5">
          {!activeProjectId ? (
            <p className="text-xs text-content-muted">Save this project first (generate a schema) before linking it to another database.</p>
          ) : isLoading ? (
            <div className="flex items-center justify-center gap-2 py-8 text-sm text-content-muted">
              <Loader2 className="w-4 h-4 animate-spin" /> Loading…
            </div>
          ) : (
            <div className="flex flex-col gap-2.5">
              {relations.length === 0 && !isAdding && (
                <p className="text-xs text-content-muted">No cross-database relations yet.</p>
              )}

              {relations.map(r => (
                <div key={r.id} className="border border-dashed border-content-primary/25 rounded-xl p-3 flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <div className="flex items-center gap-1.5 text-xs font-mono text-content-primary">
                      <span className="truncate">{r.direction === 'outgoing' ? r.localColumn : r.otherColumn}</span>
                      {r.direction === 'outgoing' ? <ArrowRight className="w-3 h-3 text-content-subtle shrink-0" /> : <ArrowLeft className="w-3 h-3 text-content-subtle shrink-0" />}
                      <span className="truncate">{r.direction === 'outgoing' ? r.otherColumn : r.localColumn}</span>
                    </div>
                    <div className="flex items-center gap-1.5 mt-1">
                      <span className="text-[9px] font-bold uppercase tracking-wide text-content-subtle bg-surface-700 border border-content-primary/10 px-1.5 py-0.5 rounded">
                        Not enforced
                      </span>
                      <span className="text-[10px] text-content-muted truncate">→ {r.otherProjectName}</span>
                    </div>
                    {r.note && <p className="text-[10px] text-content-subtle mt-1 truncate">{r.note}</p>}
                  </div>
                  <div className="flex items-center gap-0.5 shrink-0">
                    <button
                      onClick={() => openMap(r.otherProjectId)}
                      className="p-1.5 text-content-subtle hover:text-content-primary hover:bg-white/[0.06] rounded-lg transition-colors"
                      aria-label="See both databases side by side"
                      title="See both databases side by side"
                    >
                      <Map className="w-3.5 h-3.5" />
                    </button>
                    <button onClick={() => handleDelete(r.id)} className="p-1.5 text-content-subtle hover:text-danger-text hover:bg-danger-subtle rounded-lg transition-colors" aria-label="Delete relation">
                      <Trash2 className="w-3.5 h-3.5" />
                    </button>
                  </div>
                </div>
              ))}

              {isAdding ? (
                <div className="border border-content-primary/15 rounded-xl p-3.5 flex flex-col gap-2.5 mt-1">
                  <label className="flex flex-col gap-1 text-[11px] text-content-muted">
                    Other project
                    <select
                      value={otherProjectId}
                      onChange={e => { setOtherProjectId(e.target.value); setRemoteTableId(''); setRemoteColumnId(''); }}
                      className="bg-surface-700 border border-content-primary/10 rounded-lg px-2 py-1.5 text-xs text-content-primary"
                    >
                      <option value="">Select a project…</option>
                      {otherProjects.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}
                    </select>
                  </label>

                  <div className="grid grid-cols-2 gap-2.5">
                    <label className="flex flex-col gap-1 text-[11px] text-content-muted">
                      This project's table.column
                      <select value={localTableId} onChange={e => { setLocalTableId(e.target.value); setLocalColumnId(''); }} className="bg-surface-700 border border-content-primary/10 rounded-lg px-2 py-1.5 text-xs text-content-primary">
                        <option value="">Table…</option>
                        {schema?.tables.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                      </select>
                      <select value={localColumnId} onChange={e => setLocalColumnId(e.target.value)} disabled={!localTable} className="bg-surface-700 border border-content-primary/10 rounded-lg px-2 py-1.5 text-xs text-content-primary disabled:opacity-40">
                        <option value="">Column…</option>
                        {localTable?.columns.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                      </select>
                    </label>

                    <label className="flex flex-col gap-1 text-[11px] text-content-muted">
                      Their table.column
                      <select value={remoteTableId} onChange={e => { setRemoteTableId(e.target.value); setRemoteColumnId(''); }} disabled={!remoteSchema} className="bg-surface-700 border border-content-primary/10 rounded-lg px-2 py-1.5 text-xs text-content-primary disabled:opacity-40">
                        <option value="">Table…</option>
                        {remoteSchema?.tables.map(t => <option key={t.id} value={t.id}>{t.name}</option>)}
                      </select>
                      <select value={remoteColumnId} onChange={e => setRemoteColumnId(e.target.value)} disabled={!remoteTable} className="bg-surface-700 border border-content-primary/10 rounded-lg px-2 py-1.5 text-xs text-content-primary disabled:opacity-40">
                        <option value="">Column…</option>
                        {remoteTable?.columns.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}
                      </select>
                    </label>
                  </div>

                  <label className="flex flex-col gap-1 text-[11px] text-content-muted">
                    Note (optional)
                    <input value={note} onChange={e => setNote(e.target.value)} placeholder="e.g. same user identity" className="bg-surface-700 border border-content-primary/10 rounded-lg px-2 py-1.5 text-xs text-content-primary" />
                  </label>

                  <div className="flex items-center justify-end gap-2 mt-1">
                    <button onClick={resetAddForm} className="px-3 py-1.5 rounded-lg text-content-muted hover:text-content-primary text-xs font-semibold transition-colors">Cancel</button>
                    <button
                      onClick={handleCreate}
                      disabled={isSubmitting || !otherProjectId || !localColumnId || !remoteColumnId}
                      className="bg-content-primary hover:bg-content-secondary text-surface-900 px-3 py-1.5 rounded-lg text-xs font-semibold transition-all disabled:opacity-50"
                    >
                      {isSubmitting ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : 'Save relation'}
                    </button>
                  </div>
                </div>
              ) : (
                <button
                  onClick={startAdding}
                  className="flex items-center justify-center gap-1.5 mt-1 px-3 py-2 rounded-lg border border-dashed border-content-primary/20 text-content-muted hover:text-content-primary hover:border-content-primary/40 text-xs font-semibold transition-colors"
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
