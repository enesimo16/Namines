import { BaseEdge, EdgeLabelRenderer, EdgeProps, getSmoothStepPath } from '@xyflow/react';
import { useFlowFiringStore } from '../../../store/useFlowFiringStore';

/**
 * Bir tabloyu ona bağlı Namines Flow kuralına bağlayan kenar.
 *
 * <b>Neden `RelationEdge`'den yalnızca renkle ayrılmıyor:</b> şema ilişkileri
 * `animated: true` ile çiziliyor (bkz. lib/schemaToFlow.ts) ve React Flow bunu
 * YÜRÜYEN KESİKLİ çizgi olarak render ediyor. Yani "kesikli çizgi" zaten
 * ilişkilerin dili — üzerine ikinci bir kesikli çizgi koymak ayırt edici
 * olmuyor, kullanıcı bunu bir foreign key sanıyordu.
 *
 * Ayrım bu yüzden RENKTE değil ŞEKİLDE:
 *   - İlişki  : yumuşak bezier eğrisi, yürüyen kesikler, mavi-gri.
 *   - Flow    : dik açılı (smooth-step) hat, YUVARLAK NOKTALAR, uyarı rengi.
 * İki silüet uzaktan bakıldığında bile karışmıyor.
 */
export default function AutomationEdge({
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  data,
}: EdgeProps) {
  const ruleId = (data?.ruleId as string) ?? '';
  const isFiring = useFlowFiringStore(s => ruleId !== '' && ruleId in s.firings);

  const [edgePath, labelX, labelY] = getSmoothStepPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
    // Geniş yarıçap: köşeler dik açı gibi "kırılmıyor", geniş bir yay
    // çiziyor. Yine de yatay/dikey düz hatlar korunuyor — ilişki kenarının
    // baştan sona eğri olan bezier'inden ayıran şey o düz koşular.
    borderRadius: 40,
  });

  return (
    <>
      <BaseEdge
        path={edgePath}
        style={{
          stroke: 'var(--color-warning-text)',
          // `0 6` + yuvarlak uç = nokta dizisi. Kesik ÇİZGİ değil, bu yüzden
          // ilişki kenarlarıyla aynı desene düşmüyor.
          strokeDasharray: '0 7',
          strokeLinecap: 'round',
          strokeWidth: isFiring ? 4 : 2.5,
          opacity: isFiring ? 1 : 0.75,
          transition: 'stroke-width 150ms, opacity 150ms',
        }}
      />

      {/* Tablo ucundaki halka: hattın NEREDEN çıktığını tek bakışta gösteriyor —
          bağlılığın anlaşılmadığı asıl nokta buydu. */}
      <circle
        cx={sourceX}
        cy={sourceY}
        r={isFiring ? 5 : 3.5}
        fill="var(--color-surface-700)"
        stroke="var(--color-warning-text)"
        strokeWidth={2}
        style={{ transition: 'r 150ms' }}
      />

      <EdgeLabelRenderer>
        <div
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
          }}
          className={`nodrag nopan pointer-events-none flex items-center gap-1 rounded-full border border-warning/50 bg-surface-700 px-1.5 py-0.5 text-micro font-semibold uppercase tracking-wide text-warning-text transition-transform duration-150 ${
            isFiring ? 'scale-125 border-warning' : ''
          }`}
        >
          <span aria-hidden="true">⚡</span>
          <span>Flow</span>
        </div>
      </EdgeLabelRenderer>
    </>
  );
}
