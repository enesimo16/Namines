'use client';

import React, { useRef, useState } from 'react';
import { X, Braces, Loader2, AlertTriangle } from 'lucide-react';
import { useFocusTrap } from '../../hooks/useFocusTrap';
import { errorMessage } from '../../lib/errors';
import type { InferredEntity, ShapeInferenceResult } from '../../types/source';
import type { DatabaseSchema, SchemaColumn, SchemaRelation, SchemaTable } from '../../types/schema';

interface ObservedResponse {
  endpoint: string;
  body: string;
}

interface JsonShapeModalProps {
  onInfer: (responses: ObservedResponse[]) => Promise<ShapeInferenceResult>;
  onApply: (schema: DatabaseSchema) => void;
  onClose: () => void;
}

const id = () =>
  typeof crypto !== 'undefined' && crypto.randomUUID
    ? crypto.randomUUID()
    : Math.random().toString(36).slice(2);

/**
 * second-phase/06-VERI-KAYNAKLARI.md kademe 3 — gözlemlenen JSON yanıtlarının
 * ŞEKLİNDEN veri modeli çıkarımı.
 *
 * <b>Çıkarım motoru sunucuda zaten vardı; ekranı yoktu.</b> Yani özellik
 * kullanıcıya hiç ulaşmıyordu.
 *
 * Doc'un iki kuralı bu ekranda somutlaşıyor:
 * <b>(1) Sonuç bir TAHMİN</b> ve öyle yazıyor — üretilen şey "sitenin
 * veritabanı" değil, API'sinden çıkarılan bir model.
 * <b>(2) Otomatik onay YOK</b> — hiçbir varlık kullanıcı kabul etmeden
 * şemaya girmiyor, reddedilen varlığın ilişkisi de girmiyor (tek ucu olan bir
 * yabancı anahtar kırık şema demek).
 *
 * <b>Değerler gönderilmiyor mu?</b> Gönderilen gövdeyi kullanıcı kendisi
 * yapıştırıyor; sunucu ondan yalnızca alan adı ve tip çıkarıyor, hiçbir değeri
 * saklamıyor. Bu yüzden ekran "örnek yanıt" diyor, "verini yükle" demiyor.
 */
export function JsonShapeModal({ onInfer, onApply, onClose }: JsonShapeModalProps) {
  const [endpoint, setEndpoint] = useState('');
  const [body, setBody] = useState('');
  const [responses, setResponses] = useState<ObservedResponse[]>([]);
  const [result, setResult] = useState<ShapeInferenceResult | null>(null);
  const [accepted, setAccepted] = useState<Record<string, boolean>>({});
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const modalRef = useRef<HTMLDivElement>(null);
  useFocusTrap(true, modalRef);

  const addResponse = () => {
    const text = body.trim();
    if (!text) return;

    // Geçersiz JSON'u sunucuya göndermek, kullanıcıya sunucudan dönen
    // anlaşılmaz bir hata göstermek olurdu — burada anlaşılır biçimde
    // söylenebiliyor.
    try {
      JSON.parse(text);
    } catch {
      setError('That is not valid JSON.');
      return;
    }

    setError(null);
    setResponses(prev => [...prev, { endpoint: endpoint.trim() || `response-${prev.length + 1}`, body: text }]);
    setBody('');
  };

  const infer = async () => {
    if (responses.length === 0) return;

    setBusy(true);
    setError(null);
    setResult(null);
    try {
      const inferred = await onInfer(responses);
      setResult(inferred);
      // Varsayılan işaretli, ama şemaya girmesi için kullanıcının "Add"
      // düğmesine basması gerekiyor — onay ORADA veriliyor.
      setAccepted(Object.fromEntries(inferred.entities.map(e => [e.name, true])));
    } catch (e) {
      setError(errorMessage(e, 'Could not infer a data model from those responses.'));
    } finally {
      setBusy(false);
    }
  };

  /** Kabul edilen varlıkları bir şemaya çevirir. */
  const apply = () => {
    if (!result) return;

    const entities = result.entities.filter(e => accepted[e.name]);
    const columnIds = new Map<string, Map<string, string>>();

    const tables: SchemaTable[] = entities.map(entity => {
      const columns: SchemaColumn[] = entity.fields.map((field, index) => {
        const columnId = id();
        return {
          id: columnId,
          name: field.name,
          type: field.type === 'UNKNOWN' ? 'VARCHAR' : field.type,
          // Çıkarımda birincil anahtar bilgisi YOK. `id` alanını anahtar
          // saymak bir varsayım, ama kullanıcının canvas'ta düzelteceği
          // görünür bir varsayım — anahtarsız tablo hiçbir motorda çalışmaz.
          isPK: field.name.toLowerCase() === 'id' && index >= 0,
          isFK: false,
          isNullable: field.isUncertain,
        } as SchemaColumn;
      });

      columnIds.set(entity.name, new Map(entity.fields.map((f, i) => [f.name, columns[i].id])));

      return { id: id(), name: entity.name, columns } as SchemaTable;
    });

    const byName = new Map(tables.map(t => [t.name, t]));

    // İlişki yalnızca İKİ UCU DA kabul edildiyse taşınır: tek ucu olan bir
    // yabancı anahtar, hiçbir motorda çalışmayan bir şema üretir.
    const relations: SchemaRelation[] = [];
    for (const relation of result.relations) {
      const from = byName.get(relation.fromEntity);
      const to = byName.get(relation.toEntity);
      if (!from || !to) continue;

      const sourceColumnId = columnIds.get(relation.fromEntity)?.get(relation.fromField);
      const targetColumnId = columnIds.get(relation.toEntity)?.get('id');
      if (!sourceColumnId || !targetColumnId) continue;

      relations.push({
        id: id(), type: '1:N',
        sourceTableId: from.id, sourceColumnId,
        targetTableId: to.id, targetColumnId,
      } as SchemaRelation);
    }

    onApply({ schemaId: id(), name: 'Inferred model', tables, relations } as DatabaseSchema);
  };

  const acceptedCount = result ? result.entities.filter(e => accepted[e.name]).length : 0;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center p-4 bg-surface-900/80 backdrop-blur-sm">
      <div
        ref={modalRef}
        role="dialog"
        aria-modal="true"
        aria-label="Infer a data model from JSON responses"
        className="w-full max-w-2xl max-h-[85vh] overflow-y-auto glass-panel rounded-[var(--radius-modal)] p-5 flex flex-col gap-4"
      >
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-2">
            <Braces className="w-4 h-4 text-content-secondary" />
            <h2 className="text-body-lg text-content-primary">Sample JSON response</h2>
          </div>
          <button type="button" onClick={onClose} aria-label="Close"
            className="p-1 text-content-muted hover:text-content-primary transition-colors">
            <X className="w-4 h-4" />
          </button>
        </div>

        <p className="text-micro text-content-muted">
          Paste what an API returns. Only field names and types are read — no values are stored.
          The result is a <strong>guess</strong> at the data model behind the API, not its database.
        </p>

        <div className="flex flex-col gap-2">
          <input
            aria-label="Endpoint (optional)"
            type="text"
            value={endpoint}
            onChange={(e) => setEndpoint(e.target.value)}
            placeholder="/api/users — optional, improves confidence"
            className="glass-input rounded-[var(--radius-control)] px-3 py-2 text-sm text-content-primary placeholder:text-content-muted focus:outline-none"
          />
          <textarea
            aria-label="JSON response"
            value={body}
            onChange={(e) => setBody(e.target.value)}
            placeholder={'{ "id": 1, "email": "a@b.c" }'}
            className="glass-input rounded-[var(--radius-card)] px-3 py-2 h-28 resize-none text-sm font-mono text-content-primary placeholder:text-content-muted focus:outline-none"
          />
        </div>

        <div className="flex items-center gap-2 flex-wrap">
          <button type="button" onClick={addResponse}
            className="px-3 py-2 rounded-[var(--radius-control)] glass-button text-sm text-content-primary">
            Add response
          </button>
          <button type="button" onClick={infer} disabled={busy}
            className="flex items-center gap-2 px-3 py-2 rounded-[var(--radius-control)] glass-button text-sm text-content-primary disabled:opacity-50">
            {busy && <Loader2 className="w-3.5 h-3.5 animate-spin" />}
            Infer from {responses.length} response{responses.length === 1 ? '' : 's'}
          </button>
          {responses.length > 0 && (
            <span className="text-micro text-content-muted">
              {responses.map(r => r.endpoint).join(' · ')}
            </span>
          )}
        </div>

        {error && (
          <p className="text-sm text-danger-text border border-line-strong rounded-[var(--radius-control)] p-3">
            {error}
          </p>
        )}

        {result && (
          <div className="flex flex-col gap-3">
            <p className="flex items-start gap-2 text-micro text-content-secondary">
              <AlertTriangle className="w-3.5 h-3.5 shrink-0 mt-px" />
              This is a guess from the shape of the responses. Accept only what looks right.
            </p>

            {result.entities.map((entity: InferredEntity) => (
              <div key={entity.name} className="flex flex-col gap-1 border border-line-strong rounded-[var(--radius-card)] p-3">
                <label className="flex items-center gap-2">
                  <input
                    type="checkbox"
                    aria-label={`Accept ${entity.name}`}
                    checked={accepted[entity.name] ?? false}
                    onChange={(e) => setAccepted(prev => ({ ...prev, [entity.name]: e.target.checked }))}
                  />
                  <span className="text-sm text-content-primary">{entity.name}</span>
                  <span className="text-micro text-content-muted">
                    {entity.confidence} confidence · {entity.sampleCount} samples · {entity.endpointCount} endpoints
                  </span>
                </label>

                <div className="flex flex-wrap gap-x-3 gap-y-1 pl-6">
                  {entity.fields.map(field => (
                    <span key={field.name} className="text-micro text-content-muted">
                      {field.name}
                      <span className="text-content-secondary"> {field.type}</span>
                      {field.isUncertain && <span className="text-danger-text"> uncertain</span>}
                    </span>
                  ))}
                </div>
              </div>
            ))}

            <button
              type="button"
              onClick={apply}
              disabled={acceptedCount === 0}
              className="self-start px-3 py-2 rounded-[var(--radius-control)] glass-button text-sm text-accent-text border-accent disabled:opacity-50"
            >
              Add {acceptedCount} to the canvas
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
