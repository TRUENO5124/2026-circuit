/** Unified schematic circuit component node for React Flow. */

import { Handle, useUpdateNodeInternals, type NodeProps } from "@xyflow/react";
import { useMemo, createElement, useEffect } from "react";
import type { ComponentNodeData } from "../../types/editor";
import { placedPorts } from "../../lib/portGeometry";
import { symbolFor } from "./symbols/index";
import { useEditorStore } from "../../store/editorStore";

import { formatCanvasParam } from "../../lib/units";

function primaryParam(data: ComponentNodeData): string | null {
  const entry = Object.entries(data.parameters)[0];
  if (!entry) return null;
  const [, pv] = entry;
  return formatCanvasParam(pv.value, pv.unit, data.definitionId);
}

export default function CircuitNode({ id, data, selected }: NodeProps) {
  const d = data as unknown as ComponentNodeData;
  const symbolStandard = useEditorStore((s) => s.symbolStandard);
  const SymbolComponent = symbolFor(d.definitionId, d.modelVariant, symbolStandard);
  const rotation = d.rotation ?? 0;
  const updateNodeInternals = useUpdateNodeInternals();

  const layout = useMemo(() => placedPorts(d.definitionId, rotation), [d.definitionId, rotation]);
  const portIds = useMemo(() => (d.ports?.length ? d.ports : layout.map((item) => item.portId)), [d.ports, layout]);
  const handles = layout.filter((item) => portIds.includes(item.portId));
  const prim = primaryParam(d);

  useEffect(() => {
    updateNodeInternals(id);
  }, [id, rotation, updateNodeInternals]);

  return (
    <div className={`circuit-node ${selected ? "selected" : ""}`} title={`${d.label} (${d.definitionId})`}>
      <div className="circuit-node__symbol">
        {handles.map((handle) => (
          <div key={handle.portId}>
            <Handle
              type="target"
              id={handle.portId}
              position={handle.side}
              className="circuit-node__handle circuit-node__handle--target"
              style={{
                left: `${handle.x * 100}%`,
                top: `${handle.y * 100}%`,
                bottom: "auto",
                right: "auto",
              }}
              title={`引脚: ${handle.portId}`}
            />
            <Handle
              type="source"
              id={handle.portId}
              position={handle.side}
              className="circuit-node__handle circuit-node__handle--source"
              style={{
                left: `${handle.x * 100}%`,
                top: `${handle.y * 100}%`,
                bottom: "auto",
                right: "auto",
              }}
              title={`引脚: ${handle.portId}`}
            />
          </div>
        ))}
        <div className="circuit-node__art" style={{ transform: `rotate(${rotation}deg)` }}>
          {SymbolComponent ? createElement(SymbolComponent) : <span>{d.definitionId}</span>}
        </div>
      </div>

      <div className="circuit-node__badge-container">
        <div className="circuit-node__label">{d.label}</div>
        {prim && <div className="circuit-node__value">{prim}</div>}
      </div>

      {rotation !== 0 && <span className="circuit-node__rotation">{rotation}°</span>}
    </div>
  );
}
