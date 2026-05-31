import React from 'react';
import { BaseEdge, EdgeLabelRenderer, EdgeProps, getBezierPath } from '@xyflow/react';

export default function RelationEdge({
  id,
  sourceX,
  sourceY,
  targetX,
  targetY,
  sourcePosition,
  targetPosition,
  style = {},
  markerEnd,
  data,
}: EdgeProps) {
  const [edgePath, labelX, labelY] = getBezierPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
  });

  const relationType = (data?.relationType as string) || '';

  // Determine short label for relation type
  let label = '1:N';
  if (relationType.toLowerCase() === 'onetoone') label = '1:1';
  else if (relationType.toLowerCase() === 'manytomany') label = 'N:M';

  return (
    <>
      <BaseEdge path={edgePath} markerEnd={markerEnd} style={style} />
      <EdgeLabelRenderer>
        <div
          style={{
            position: 'absolute',
            transform: `translate(-50%, -50%) translate(${labelX}px,${labelY}px)`,
            fontSize: 10,
            pointerEvents: 'all',
          }}
          className="nodrag nopan bg-zinc-800 text-indigo-300 px-2 py-1 rounded border border-zinc-700 shadow-md font-mono"
        >
          {label}
        </div>
      </EdgeLabelRenderer>
    </>
  );
}
