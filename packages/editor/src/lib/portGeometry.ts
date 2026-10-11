import { Position, type Edge, type Node } from "@xyflow/react";
import type { ComponentNodeData } from "../types/editor";

/**
 * Port geometry for schematic component nodes.
 *
 * Every symbol is drawn on a square 100×100 viewBox (see
 * components/nodes/symbols).  A port entry stores the normalized terminal
 * point of the *unrotated* artwork plus the edge-routing side React Flow
 * should use at rotation 0.  Rotation is a clockwise turn around the symbol
 * box center — exactly what the CSS `rotate()` on the artwork does — so the
 * connection handles stay glued to the symbol's leads at every orientation.
 */

export interface PortLayoutEntry {
  portId: string;
  /** Normalized terminal point of the unrotated symbol; y grows downward. */
  x: number;
  y: number;
  /** React Flow routing side at rotation 0. */
  side: Position;
}

/** Side length of the square symbol box in px — keep in sync with index.css. */
export const SYMBOL_BOX = 100;

/**
 * Offset of the symbol box from the node's outer top-left corner — keep in
 * sync with index.css (1px border + 7px padding, horizontally centered).
 */
export const SYMBOL_OFFSET = { x: 28, y: 8 };

export const PORT_LAYOUT: Record<string, PortLayoutEntry[]> = {
  "reference.ground": [{ portId: "ref", x: 0.5, y: 0, side: Position.Top }],
  "passive.resistor": [{ portId: "p", x: 0, y: 0.5, side: Position.Left }, { portId: "n", x: 1, y: 0.5, side: Position.Right }],
  "passive.capacitor": [{ portId: "p", x: 0, y: 0.5, side: Position.Left }, { portId: "n", x: 1, y: 0.5, side: Position.Right }],
  "passive.inductor": [{ portId: "p", x: 0, y: 0.5, side: Position.Left }, { portId: "n", x: 1, y: 0.5, side: Position.Right }],
  "passive.transformer": [
    { portId: "p1", x: 0, y: 0.3, side: Position.Left },
    { portId: "n1", x: 0, y: 0.7, side: Position.Left },
    { portId: "p2", x: 1, y: 0.3, side: Position.Right },
    { portId: "n2", x: 1, y: 0.7, side: Position.Right },
  ],
  "source.v_dc": [{ portId: "p", x: 0.5, y: 0, side: Position.Top }, { portId: "n", x: 0.5, y: 1, side: Position.Bottom }],
  "source.i_dc": [{ portId: "p", x: 0.5, y: 0, side: Position.Top }, { portId: "n", x: 0.5, y: 1, side: Position.Bottom }],
  "source.v_ac_small_signal": [{ portId: "p", x: 0.5, y: 0, side: Position.Top }, { portId: "n", x: 0.5, y: 1, side: Position.Bottom }],
  "source.i_ac_small_signal": [{ portId: "p", x: 0.5, y: 0, side: Position.Top }, { portId: "n", x: 0.5, y: 1, side: Position.Bottom }],
  "diode.standard": [{ portId: "anode", x: 0, y: 0.5, side: Position.Left }, { portId: "cathode", x: 1, y: 0.5, side: Position.Right }],
  "bjt.npn": [
    { portId: "collector", x: 0.6, y: 0, side: Position.Top },
    { portId: "base", x: 0, y: 0.5, side: Position.Left },
    { portId: "emitter", x: 0.6, y: 1, side: Position.Bottom },
  ],
  "bjt.pnp": [
    { portId: "collector", x: 0.6, y: 0, side: Position.Top },
    { portId: "base", x: 0, y: 0.5, side: Position.Left },
    { portId: "emitter", x: 0.6, y: 1, side: Position.Bottom },
  ],
  "fet.mosfet": [
    { portId: "drain", x: 0.62, y: 0, side: Position.Top },
    { portId: "gate", x: 0, y: 0.5, side: Position.Left },
    { portId: "source", x: 0.62, y: 1, side: Position.Bottom },
    { portId: "bulk", x: 1, y: 0.5, side: Position.Right },
  ],
  "fet.jfet": [
    { portId: "drain", x: 0.62, y: 0, side: Position.Top },
    { portId: "gate", x: 0, y: 0.5, side: Position.Left },
    { portId: "source", x: 0.62, y: 1, side: Position.Bottom },
  ],
  "controlled.vcvs": [
    { portId: "p", x: 0.56, y: 0, side: Position.Top },
    { portId: "n", x: 0.56, y: 1, side: Position.Bottom },
    { portId: "cp", x: 0, y: 0.34, side: Position.Left },
    { portId: "cn", x: 0, y: 0.66, side: Position.Left },
  ],
  "controlled.vccs": [
    { portId: "p", x: 0.56, y: 0, side: Position.Top },
    { portId: "n", x: 0.56, y: 1, side: Position.Bottom },
    { portId: "cp", x: 0, y: 0.34, side: Position.Left },
    { portId: "cn", x: 0, y: 0.66, side: Position.Left },
  ],
  "controlled.ccvs": [
    { portId: "p", x: 0.56, y: 0, side: Position.Top },
    { portId: "n", x: 0.56, y: 1, side: Position.Bottom },
    { portId: "sp", x: 0, y: 0.34, side: Position.Left },
    { portId: "sn", x: 0, y: 0.66, side: Position.Left },
  ],
  "controlled.cccs": [
    { portId: "p", x: 0.56, y: 0, side: Position.Top },
    { portId: "n", x: 0.56, y: 1, side: Position.Bottom },
    { portId: "sp", x: 0, y: 0.34, side: Position.Left },
    { portId: "sn", x: 0, y: 0.66, side: Position.Left },
  ],
};

const NEXT_SIDE: Record<string, Position> = {
  [Position.Top]: Position.Right,
  [Position.Right]: Position.Bottom,
  [Position.Bottom]: Position.Left,
  [Position.Left]: Position.Top,
};

/**
 * Rotate a normalized anchor clockwise around the symbol box center.
 * Matches CSS `rotate(θdeg)` in screen coordinates (y grows downward).
 */
export function rotateAnchor(x: number, y: number, rotation = 0): { x: number; y: number } {
  const rad = (rotation * Math.PI) / 180;
  const cos = Math.cos(rad);
  const sin = Math.sin(rad);
  const dx = x - 0.5;
  const dy = y - 0.5;
  return { x: 0.5 + dx * cos - dy * sin, y: 0.5 + dx * sin + dy * cos };
}

/** Rotate a routing side clockwise: left → top → right → bottom → left. */
export function rotateSide(side: Position, rotation = 0): Position {
  const turns = Math.round((((rotation % 360) + 360) % 360) / 90) % 4;
  let out = side;
  for (let index = 0; index < turns; index += 1) {
    out = NEXT_SIDE[out];
  }
  return out;
}

/** A port entry after applying the node's rotation. */
export interface PlacedPort extends PortLayoutEntry {
  side: Position;
}

/** All ports of a definition placed for the given rotation. */
export function placedPorts(definitionId: string, rotation = 0): PlacedPort[] {
  return (PORT_LAYOUT[definitionId] ?? []).map((entry) => {
    const anchor = rotateAnchor(entry.x, entry.y, rotation);
    return { portId: entry.portId, x: anchor.x, y: anchor.y, side: rotateSide(entry.side, rotation) };
  });
}

/** Absolute canvas point of a component's port, or null if unknown. */
export function getPortPoint(node: Node, portId: string) {
  const data = node.data as unknown as Partial<ComponentNodeData>;
  const entry = PORT_LAYOUT[data.definitionId ?? ""]?.find((item) => item.portId === portId);
  if (!entry) return null;
  const anchor = rotateAnchor(entry.x, entry.y, data.rotation ?? 0);
  return {
    x: node.position.x + SYMBOL_OFFSET.x + anchor.x * SYMBOL_BOX,
    y: node.position.y + SYMBOL_OFFSET.y + anchor.y * SYMBOL_BOX,
  };
}

export interface InlineJunctionGeometry {
  position: { x: number; y: number };
  handles: Array<{ id: string; position: Position }>;
}

export function inlineJunctionGeometry(
  sourceNode: Node,
  targetNode: Node,
  sourcePortId: string,
  targetPortId: string,
  sourceHandleId: string,
  targetHandleId: string,
): InlineJunctionGeometry {
  const sourcePoint = getPortPoint(sourceNode, sourcePortId) ?? { x: sourceNode.position.x, y: sourceNode.position.y };
  const targetPoint = getPortPoint(targetNode, targetPortId) ?? { x: targetNode.position.x, y: targetNode.position.y };
  const midpoint = { x: (sourcePoint.x + targetPoint.x) / 2, y: (sourcePoint.y + targetPoint.y) / 2 };
  const dx = targetPoint.x - sourcePoint.x;
  const dy = targetPoint.y - sourcePoint.y;
  const horizontal = Math.abs(dx) >= Math.abs(dy);
  const sourcePosition = horizontal
    ? sourcePoint.x <= midpoint.x ? Position.Left : Position.Right
    : sourcePoint.y <= midpoint.y ? Position.Top : Position.Bottom;
  const targetPosition = horizontal
    ? targetPoint.x >= midpoint.x ? Position.Right : Position.Left
    : targetPoint.y >= midpoint.y ? Position.Bottom : Position.Top;
  return {
    position: { x: midpoint.x - 4, y: midpoint.y - 4 },
    handles: [
      { id: sourceHandleId, position: sourcePosition },
      { id: targetHandleId, position: targetPosition },
    ],
  };
}

export function syncInlineJunctions(nodes: Node[], edges: Edge[]) {
  const byId = new Map(nodes.map((node) => [node.id, node]));
  return nodes.map((node) => {
    const data = node.data as { inline?: boolean; handlePositions?: Array<{ id: string; position: Position }> };
    if (node.type !== "junction" || !data.inline) return node;
    const incident = edges.filter((edge) => edge.source === node.id && edge.target);
    if (incident.length < 2) return node;
    const first = incident[0];
    const second = incident[1];
    const firstTarget = byId.get(first.target);
    const secondTarget = byId.get(second.target);
    if (!firstTarget || !secondTarget) return node;
    const geometry = inlineJunctionGeometry(
      firstTarget,
      secondTarget,
      first.targetHandle ?? "",
      second.targetHandle ?? "",
      first.sourceHandle ?? `${node.id}:source`,
      second.sourceHandle ?? `${node.id}:target`,
    );
    return { ...node, position: geometry.position, data: { ...node.data, handlePositions: geometry.handles } };
  });
}
