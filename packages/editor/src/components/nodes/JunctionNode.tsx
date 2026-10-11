/**
 * Junction node — represents an electrical connection point.
 *
 * Renders as a small filled circle. Reference (ground) nodes render
 * the ground symbol. Junction nodes are auto-created by the wiring
 * handler or loaded from CircuitGraph JSON.
 */

import { Handle, Position, type NodeProps } from "@xyflow/react";
import { createElement } from "react";
import type { JunctionNodeData } from "../../types/editor";
import { SYMBOLS } from "./symbols/index";

export default function JunctionNode({ data, selected }: NodeProps) {
  const d = data as unknown as JunctionNodeData;
  const isGround = d.kind === "reference";
  const isInline = d.inline === true;

  if (isGround) {
    return (
      <div
        className={`junction-node ground ${selected ? "selected" : ""}`}
        style={{
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          position: "relative",
          cursor: "pointer",
        }}
      >
        <Handle
          type="target"
          id={d.originalNodeId}
          position={Position.Top}
          style={{ background: "#555", width: 10, height: 10, border: "2px solid #fff", left: "50%", top: 0, transform: "translate(-50%, -50%)" }}
        />
        <Handle
          type="source"
          id={d.originalNodeId}
          position={Position.Top}
          style={{ background: "#555", width: 10, height: 10, border: "2px solid #fff", left: "50%", top: 0, transform: "translate(-50%, -50%)" }}
        />
        <div className="junction-ground-symbol">
          {createElement(SYMBOLS["reference.ground"])}
        </div>
        {d.label && (
          <div style={{ fontSize: 10, color: "#555", marginTop: 1 }}>{d.label}</div>
        )}
      </div>
    );
  }

  return (
    <div
      className={`junction-node ${selected ? "selected" : ""}`}
      style={{
        position: "relative",
        cursor: "pointer",
      }}
    >
      {(d.handlePositions ?? [{ id: d.originalNodeId, position: Position.Top }]).map((handle) => (
        <span key={handle.id}>
          <Handle
            type="target"
            id={handle.id}
            position={handle.position}
            style={{ background: isInline ? "transparent" : "#555", width: 10, height: 10, border: isInline ? "0" : "2px solid #fff", left: "50%", top: "50%", transform: "translate(-50%, -50%)" }}
          />
          <Handle
            type="source"
            id={handle.id}
            position={handle.position}
            style={{ background: isInline ? "transparent" : "#555", width: 10, height: 10, border: isInline ? "0" : "2px solid #fff", left: "50%", top: "50%", transform: "translate(-50%, -50%)" }}
          />
        </span>
      ))}
      <div
        style={{
          width: isInline ? 0 : 8,
          height: isInline ? 0 : 8,
          borderRadius: "50%",
          background: isInline ? "transparent" : selected ? "#1a73e8" : "#444",
          border: isInline ? "0" : selected ? "2px solid #1a73e8" : "2px solid transparent",
        }}
      />
      {d.label && (
        <div style={{ fontSize: 9, color: "#555", position: "absolute", top: 12, whiteSpace: "nowrap" }}>
          {d.label}
        </div>
      )}
    </div>
  );
}
