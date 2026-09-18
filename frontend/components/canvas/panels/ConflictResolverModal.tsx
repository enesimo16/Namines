import React, { useRef } from 'react';
import type { SchemaColumn, SchemaRelation, SchemaTable } from '../../../types/schema';
import { GitMerge, X, CheckCircle2, ChevronRight, AlertTriangle } from 'lucide-react';
import { useBranchStore } from '../../../store/useBranchStore';
import { useProjectHistoryStore } from '../../../store/useProjectHistoryStore';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { DatabaseSchema } from '../../../types/schema';
import { useFocusTrap } from '../../../hooks/useFocusTrap';

/**
 * Cakisan bir degeri gosterir.
 *
 * **Neden ayri bilesen:** onceki kod `value.type ? ... : value.columns` yazip
 * hem tabloyu hem kolonu ayni dalda okuyordu; `any` oldugu icin derleyici
 * susuyordu. Tablo nesnesinde `type` alani YOK, yani tablo dalinda her zaman
 * `columns` sayisi gosteriliyordu -- calisiyordu ama kazara. `'type' in value`
 * kontrolu ayrimi ACIK hale getiriyor ve yanlis dal artik DERLENMEZ.
 */
/**
 * Arayüzün zengin gösterimi olmayan bir çakışmanın değeri.
 *
 * **Neden ayrı bir bileşen:** sunucu yeni bir çakışma kategorisi eklediğinde
 * bu modal onu tanımaz. Tanımadığını listeden DÜŞÜRMEK, kullanıcının çakışmayı
 * çözdüğünü sanarak bozuk bir şema üretmesi demek olurdu; bunun yerine elde ne
 * varsa okunabilir biçimde gösteriliyor ve gerekçe satırı üstte duruyor.
 */
function UnknownValue({ value }: { value: unknown }) {
  if (value === null || value === undefined) {
    return <span className="text-danger-text italic font-sans">Not in this branch</span>;
  }
  if (typeof value === 'string' || typeof value === 'number' || typeof value === 'boolean') {
    return <span>{String(value)}</span>;
  }

  const named = value as { name?: unknown; type?: unknown };
  if (typeof named.name === 'string') {
    return (
      <span className="truncate">
        {named.name}
        {typeof named.type === 'string' && <span className="text-content-muted"> · {named.type}</span>}
      </span>
    );
  }

  return <span className="text-content-muted italic font-sans">Changed</span>;
}

function ConflictValue({ value }: { value: SchemaTable | SchemaColumn | string | null }) {
  if (!value) {
    return <span className="text-danger-text italic font-sans">Not in this branch</span>;
  }
  if (typeof value === 'string') {
    return <span>{value}</span>;
  }
  const column = 'type' in value ? value : null;
  return (
    <div className="flex flex-col gap-1 w-full">
      <span className="font-semibold text-content-primary">{value.name}</span>
      <span className="text-[10px] text-content-subtle">
        {column
          ? `${column.type} ${column.isPK ? '[PK]' : ''} ${column.isFK ? '[FK]' : ''}`.trim()
          : `${(value as SchemaTable).columns?.length || 0} Columns`}
      </span>
    </div>
  );
}

export default function ConflictResolverModal() {
  const modalRef = useRef<HTMLDivElement>(null);
  const { 
    isConflictModalOpen, 
    mergeSourceBranch, 
    mergeTargetBranch, 
    conflicts, 
    autoMerged,
    serverMerged,
    updateConflictChoice, 
    resetMergeSession,
    setIsDiffMode
  } = useBranchStore();

  const { projects, activeProjectId, mergeBranch } = useProjectHistoryStore();
  const { schema, loadFromSchema } = useSchemaStore();

  const showToast = useToastStore(state => state.showToast);

  // trapping focus when conflict resolver is open
  useFocusTrap(isConflictModalOpen, modalRef);

  if (!isConflictModalOpen || !schema || !activeProjectId) return null;

  const activeProject = projects.find(p => p.id === activeProjectId);
  if (!activeProject) return null;

  // Bloke eden çakışma varsa birleştirme UYGULANAMAZ: bir taraf silmiş,
  // diğeri değiştirmişse iki seçenekten biri her hâlükârda iş kaybediyor.
  // Belirsizlikte en kısıtlayıcı davranışa düşme kuralı (ReferentialActionSql)
  // burada da geçerli.
  const hasBlockingConflict = conflicts.some(c => c.blocking);

  // Auto-resolve helper
  const handleAutoResolve = (choice: 'source' | 'target') => {
    conflicts.forEach(c => {
      updateConflictChoice(c.id, choice);
    });
  };

  // Compile resolutions and perform Git Merge
  const handleCompleteMerge = () => {
    // TABAN: sunucunun OTOMATİK kararları uygulanmış şeması, varsa.
    //
    // **Aktif dalın şemasından başlamak, otomatik birleşenleri yok sayıyordu.**
    // Üç yollu akışta farkların çoğu hiç çakışma olarak gösterilmiyor; taban
    // aktif dal olduğunda "1 merged automatically" denen kolon sonuçta HİÇ
    // bulunmuyordu. Kod incelemesinde bulundu.
    //
    // Sunucu şeması çakışmalarda AKTİF tarafı tutuyor, yani aşağıdaki
    // "kullanıcı 'target' seçtiyse uygula" mantığı değişmeden geçerli.
    const mergedSchema: DatabaseSchema = JSON.parse(JSON.stringify(serverMerged ?? schema));
    
    // Fetch source branch (the compared branch we are pulling changes from)
    const sourceBranch = activeProject.branches?.find(b => b.name === mergeSourceBranch);
    if (!sourceBranch) return;

    // Apply choices
    conflicts.forEach(item => {
      // Source value is what exists in Active Branch. Target value is what exists in Incoming Branch.
      // If user chooses 'source', we keep what we have (no change needed in mergedSchema since it's a clone of active).
      // If user chooses 'target', we apply the incoming branch's structure!
      if (item.selectedChoice === 'target') {
        if (item.type === 'table_added') {
          // 'target' is null (didn't exist in compare branch), so we delete the table
          mergedSchema.tables = mergedSchema.tables.filter(t => t.id !== item.id);
        } else if (item.type === 'table_deleted') {
          // 'target' is the table (existed in compare branch but missing here), so we restore it
          if (item.targetValue) {
            mergedSchema.tables.push(item.targetValue);
          }
        } else if (item.type === 'table_name') {
          // Update table name to target value (compare branch's table name)
          const targetTable = mergedSchema.tables.find(t => t.name === item.tableName);
          if (targetTable) {
            targetTable.name = item.targetValue;
          }
        } else if (item.type === 'column_added') {
          // Target is null (didn't exist in compare), so we remove this column
          // Kimlik TAŞINIYORSA o kullanılır; `id`'yi `-` ile parçalamak
          // GUID'lerde GUID'in ilk bölümünü tablo kimliği sanıyor ve yanlış
          // tabloyu buluyor (sunucudan gelen çakışmalar GUID taşıyor).
          const parts = item.id.split('-'); // tableId-colId-added
          const tableId = item.tableId ?? parts[0];
          const colId = item.columnId ?? parts[1];
          const targetTable = mergedSchema.tables.find(t => t.id === tableId);
          if (targetTable) {
            targetTable.columns = targetTable.columns.filter(c => c.id !== colId);
          }
        } else if (item.type === 'column_deleted') {
          // Target is the column (existed in compare, missing here), so we restore it
          const parts = item.id.split('-'); // tableId-colId-deleted
          const tableId = item.tableId ?? parts[0];
          if (item.targetValue) {
            const targetTable = mergedSchema.tables.find(t => t.id === tableId);
            if (targetTable) {
              targetTable.columns.push(item.targetValue);
            }
          }
        } else if (item.type === 'column_modified') {
          // Target is the modified column structure, so we overwrite it
          const parts = item.id.split('-'); // tableId-colId-modified
          const tableId = item.tableId ?? parts[0];
          const colId = item.columnId ?? parts[1];
          const targetTable = mergedSchema.tables.find(t => t.id === tableId);
          // `item.targetValue`u sabite aliyoruz: TypeScript, degistirilebilir bir
          // ozelligin daralmasini kapanis (closure) icinde KORUMUYOR -- `map`
          // geri cagrisinin icinde tip yeniden `SchemaColumn | null` oluyor.
          const replacement = item.targetValue;
          if (targetTable && replacement) {
            targetTable.columns = targetTable.columns.map(c =>
              c.id === colId ? replacement : c
            );
          }
        } else if (item.type === 'unknown' && !item.columnName) {
          // Tablo seviyesi çakışma (ör. iki dalın aynı adla eklediği tablo).
          // Sessizce hiçbir şey yapmamak, kullanıcının seçimini yok saymaktı.
          const incomingTable = item.targetValue as SchemaTable | null;
          if (incomingTable && typeof incomingTable === 'object' && 'columns' in incomingTable) {
            mergedSchema.tables = mergedSchema.tables.filter(
              t => t.id !== item.tableId && t.name !== item.tableName);
            mergedSchema.tables.push(incomingTable);
          }
        } else if (item.type === 'unknown' && item.tableId && item.columnName) {
          // Ad çakışması gibi, arayüzün zengin gösterimi olmayan kolon
          // çakışmaları. "Incoming" seçildiğinde HİÇBİR ŞEY YAPMAMAK en kötü
          // sonuç olurdu: kullanıcı seçimini yapar, ekran kapanır ve şema
          // seçtiğinin tersini taşır.
          const targetTable = mergedSchema.tables.find(t => t.id === item.tableId);
          const incoming = item.targetValue as SchemaColumn | null;
          if (targetTable && incoming && typeof incoming === 'object' && 'name' in incoming) {
            targetTable.columns = targetTable.columns.filter(
              c => c.id !== item.columnId && c.name !== item.columnName);
            targetTable.columns.push(incoming);
          }
        }
      }
    });

    // Merge relations from both branches and ensure referential integrity
    const allRelations = [
      ...(schema.relations || []),
      ...(sourceBranch.schema.relations || [])
    ];

    const uniqueRelationsMap = new Map<string, SchemaRelation>();
    allRelations.forEach(rel => {
      const key = `${rel.sourceTableId}-${rel.sourceColumnId}-${rel.targetTableId}-${rel.targetColumnId}`;
      uniqueRelationsMap.set(key, rel);
    });

    const mergedRelations = Array.from(uniqueRelationsMap.values()).filter(rel => {
      const sourceTable = mergedSchema.tables.find(t => t.id === rel.sourceTableId);
      const targetTable = mergedSchema.tables.find(t => t.id === rel.targetTableId);
      if (!sourceTable || !targetTable) return false;

      const sourceCol = sourceTable.columns.find(c => c.id === rel.sourceColumnId);
      const targetCol = targetTable.columns.find(c => c.id === rel.targetColumnId);
      return !!(sourceCol && targetCol);
    });

    mergedSchema.relations = mergedRelations;

    // Save final merged structure in the target branch (which is our active branch!)
    if (mergeTargetBranch) {
      mergeBranch(
        activeProject.id, 
        mergeSourceBranch || '', 
        mergeTargetBranch, 
        mergedSchema, 
        activeProject.nodePositions
      );
      
      // Load back into schema store
      loadFromSchema(mergedSchema, activeProject.nodePositions);
    }

    // Close merge session and disable diff mode
    setIsDiffMode(false);
    resetMergeSession();
    showToast("Branches merged successfully!", "success");
  };

  const getConflictBadge = (type: string) => {
    switch(type) {
      case 'table_added': return <span className="bg-success-subtle text-success-text border border-success/25 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-semibold">New Table</span>;
      case 'table_deleted': return <span className="bg-danger-subtle text-danger-text border border-danger/25 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-semibold">Deleted Table</span>;
      case 'table_name': return <span className="bg-surface-600 text-content-secondary border border-content-primary/15 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px] font-semibold">Name Change</span>;
      case 'column_added': return <span className="bg-success-subtle text-success-text border border-success/20 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px]">New Column</span>;
      case 'column_deleted': return <span className="bg-danger-subtle text-danger-text border border-danger/20 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px]">Deleted Column</span>;
      case 'column_modified': return <span className="bg-surface-600 text-content-secondary border border-content-primary/15 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px]">Column Modification</span>;
      // Arayüzün zengin gösterimi olmayan türler de ETİKETLENİR: rozetsiz bir
      // satır, kullanıcının neye karar verdiğini söylemeyen tek satır olurdu.
      case 'unknown': return <span className="bg-surface-600 text-content-secondary border border-content-primary/15 px-2 py-0.5 rounded-[var(--radius-control)] text-[10px]">Needs a decision</span>;
      default: return null;
    }
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-scrim/70 backdrop-blur-sm animate-in fade-in duration-300">
      <div ref={modalRef} role="dialog" aria-modal="true" aria-labelledby="merge-modal-title" className="bg-surface-800 border border-content-primary/12 rounded-[var(--radius-modal)] w-[90vw] max-w-4xl h-[85vh] flex flex-col shadow-[0_20px_60px_color-mix(in srgb, var(--color-scrim) 60%, transparent)] overflow-hidden">

        {/* Header */}
        <div className="bg-surface-800 border-b border-content-primary/10 px-5 py-3.5 flex items-center justify-between">
          <div className="flex items-center gap-2.5">
            <div className="h-8 w-8 bg-surface-600 border border-content-primary/10 rounded-[var(--radius-control)] flex items-center justify-center">
              <GitMerge className="w-4 h-4 text-content-primary" />
            </div>
            <div>
              <h2 id="merge-modal-title" className="text-sm font-bold text-content-primary">
                Merge Branches
              </h2>
              <p className="text-[11px] text-content-muted">
                Merging <span className="font-semibold text-content-primary">{mergeSourceBranch}</span> into <span className="font-semibold text-content-primary">{mergeTargetBranch}</span>
              </p>
            </div>
          </div>

          <button
            onClick={resetMergeSession}
            className="p-1.5 hover:bg-white/[0.06] rounded-[var(--radius-control)] text-content-subtle hover:text-content-primary transition-colors"
            aria-label="Close"
          >
            <X className="w-4 h-4" />
          </button>
        </div>

        {/* Info banner */}
        <div className="bg-surface-700 border-b border-content-primary/10 px-5 py-2.5 flex items-center justify-between text-xs text-content-primary">
          <div className="flex items-center gap-2">
            <AlertTriangle className="w-3.5 h-3.5 text-content-muted" />
            <span>
              <strong>{conflicts.length}</strong> structural changes detected. Choose a version for each row.
              {/* Ortak ata sayesinde sorulmayanlar da SAYILIYOR: kullanıcının
                  gördüğü kısa listenin neden kısa olduğunu bilmesi gerekiyor. */}
              {autoMerged.length > 0 && (
                <span className="text-content-muted"> · {autoMerged.length} merged automatically</span>
              )}
            </span>
          </div>
          <div className="flex gap-2">
            <button
              onClick={() => handleAutoResolve('source')}
              className="bg-surface-600 hover:bg-white/[0.08] text-content-primary px-2.5 py-1.5 rounded-[var(--radius-control)] border border-content-primary/10 font-semibold transition-all text-[11px]"
            >
              Keep All Active
            </button>
            <button
              onClick={() => handleAutoResolve('target')}
              className="bg-content-primary hover:bg-content-secondary text-surface-900 px-2.5 py-1.5 rounded-[var(--radius-control)] font-semibold transition-all text-[11px]"
            >
              Accept All Incoming
            </button>
          </div>
        </div>

        {/* Conflict list container */}
        <div className="flex-1 overflow-y-auto p-5 space-y-3">
          {conflicts.map((item) => {
            const isSourceSelected = item.selectedChoice === 'source';
            const isTargetSelected = item.selectedChoice === 'target';

            return (
              <div
                key={item.id}
                className="bg-surface-700 border border-content-primary/8 rounded-[var(--radius-card)] p-3.5"
              >
                {/* Meta details */}
                <div className="flex items-center gap-2 mb-2.5">
                  {getConflictBadge(item.type)}
                  <ChevronRight className="w-3 h-3 text-content-subtle" />
                  <span className="text-xs font-bold text-content-primary tracking-wide">
                    {item.tableName}
                    {item.columnName && <span className="text-content-subtle font-normal"> → {item.columnName}</span>}
                  </span>
                </div>

                {/* Sunucunun gerekçesi olduğu gibi gösteriliyor: "neden
                    çakışma" sorusunun cevabını yalnızca o metin taşıyor. */}
                {item.explanation && (
                  <p className={`text-micro mb-2.5 ${item.blocking ? 'text-danger-text' : 'text-content-muted'}`}>
                    {item.explanation}
                  </p>
                )}

                {/* Side-by-Side comparison cards */}
                <div className="grid grid-cols-2 gap-3">
                  {/* Left Choice: Keep Source (Active Branch) */}
                  <div
                    onClick={() => updateConflictChoice(item.id, 'source')}
                    className={`border rounded-[var(--radius-control)] p-2.5 cursor-pointer transition-all flex flex-col justify-between ${
                      isSourceSelected
                        ? 'border-focus-ring bg-white/[0.06]'
                        : 'border-content-primary/8 bg-surface-800 opacity-70 hover:opacity-100 hover:border-content-primary/15'
                    }`}
                  >
                    <div className="flex justify-between items-center mb-1.5">
                      <span className="text-micro font-bold text-content-muted uppercase tracking-widest">Active (Current)</span>
                      {isSourceSelected && <CheckCircle2 className="w-3.5 h-3.5 text-content-primary" />}
                    </div>

                    <div className="text-xs font-mono text-content-primary bg-scrim/20 p-2 rounded-[var(--radius-control)] min-h-[40px] flex items-center">
                      {item.type === 'unknown'
                        ? <UnknownValue value={item.sourceValue} />
                        : <ConflictValue value={item.sourceValue} />}
                    </div>
                  </div>

                  {/* Right Choice: Keep Target (Incoming Branch) */}
                  <div
                    onClick={() => updateConflictChoice(item.id, 'target')}
                    className={`border rounded-[var(--radius-control)] p-2.5 cursor-pointer transition-all flex flex-col justify-between ${
                      isTargetSelected
                        ? 'border-success bg-success-subtle/60'
                        : 'border-content-primary/8 bg-surface-800 opacity-70 hover:opacity-100 hover:border-content-primary/15'
                    }`}
                  >
                    <div className="flex justify-between items-center mb-1.5">
                      <span className="text-micro font-bold text-success-text uppercase tracking-widest">Incoming</span>
                      {isTargetSelected && <CheckCircle2 className="w-3.5 h-3.5 text-success-text" />}
                    </div>

                    <div className="text-xs font-mono text-content-primary bg-scrim/20 p-2 rounded-[var(--radius-control)] min-h-[40px] flex items-center">
                      {item.type === 'unknown'
                        ? <UnknownValue value={item.targetValue} />
                        : <ConflictValue value={item.targetValue} />}
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>

        {/* Footer actions */}
        <div className="bg-surface-800 border-t border-content-primary/10 px-5 py-3.5 flex items-center justify-between">
          <button
            onClick={resetMergeSession}
            className="px-4 py-2 rounded-[var(--radius-control)] border border-content-primary/10 text-content-muted hover:text-content-primary hover:bg-white/[0.04] text-xs font-semibold transition-colors"
          >
            Cancel
          </button>

          {/* Bloke eden çakışma (bir taraf sildi, diğeri değiştirdi) elle
              seçimle çözülemez: iki seçenekten biri her hâlükârda iş
              kaybediyor. Düğmeyi açık bırakıp "son yazan kazanır"a izin
              vermek, şemada veri kaybı demektir. */}
          <button
            onClick={handleCompleteMerge}
            disabled={hasBlockingConflict}
            title={hasBlockingConflict ? 'A blocking conflict must be resolved in the branches first.' : undefined}
            className="bg-content-primary hover:bg-content-secondary text-surface-900 px-4 py-2 rounded-[var(--radius-control)] text-xs font-semibold flex items-center gap-2 transition-all disabled:opacity-50 disabled:cursor-not-allowed"
          >
            <GitMerge className="w-3.5 h-3.5" />
            <span>Apply &amp; Complete Merge</span>
          </button>
        </div>

      </div>
    </div>
  );
}
