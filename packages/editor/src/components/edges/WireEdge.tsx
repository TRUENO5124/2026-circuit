/** Custom orthogonal wire edge for circuit connections. */

import { BaseEdge, getSmoothStepPath, type EdgeProps } from "@xyflow/react";

export default function WireEdge(props: EdgeProps) {
  const { id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, selected, markerEnd } = props;
  const [edgePath] = getSmoothStepPath({
    sourceX,
    sourceY,
    sourcePosition,
    targetX,
    targetY,
    targetPosition,
    borderRadius: 8,
  });

  return (
    <>
      {/* Invisible wider stroke for easier clicking */}
      <path
        d={edgePath}
        fill="none"
        stroke="transparent"
        strokeWidth={14}
        className="wire-edge-hitbox"
      />
      <BaseEdge
        id={id}
        path={edgePath}
        style={{
          stroke: selected ? "#0ea5e9" : "#334155",
          strokeWidth: selected ? 2.8 : 2,
          filter: selected ? "drop-shadow(0 0 3px rgba(14, 165, 233, 0.45))" : undefined,
          transition: "stroke 0.15s, stroke-width 0.15s",
        }}
        markerEnd={markerEnd}
      />
    </>
  );
}
