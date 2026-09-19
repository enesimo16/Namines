import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath } from '@xyflow/react';
import { useFlowFiringStore } from '../../../store/useFlowFiringStore';

/**
 * Bir tabloyu ona bağlı Namines Flow kuralına bağlayan kenar.
 *
 * Neden `RelationEdge`'den ayrı bir tip: ikisi de kesikli çizgiyle
 * çizildiğinde kullanıcı bunu bir veritabanı ilişkisi sanıyordu. Buradaki
 * bağ bir FK değil — bu yüzden çizginin ÜZERİNDE açıkça "Flow" etiketi
 * duruyor, rengi uyarı paletinden geliyor ve kural tetiklendiğinde kısa süre
 * vurgulanıyor.
 */
export default function AutomationEdge({
  id,
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

  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  return (
    <>
      <BaseEdge
        path={edgePath}
        style={{
          stroke: 'var(--color-warning-text)',
          strokeDasharray: '4 4',
          strokeWidth: isFiring ? 2.5 : 1.5,
          opacity: isFiring ? 1 : 0.7,
          transition: 'stroke-width 150ms, opacity 150ms',
        }}
      />
      <EdgeLabelRenderer>
        <div
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
          }}
          className={`nodrag nopan pointer-events-none rounded-[var(--radius-control)] border border-warning/40 bg-surface-700 px-1.5 py-0.5 text-micro font-semibold uppercase tracking-wide text-warning-text transition-transform duration-150 ${
            isFiring ? 'scale-125 border-warning' : ''
          }`}
          data-testid={`automation-edge-label-${id}`}
        >
          Flow
        </div>
      </EdgeLabelRenderer>
    </>
  );
}
