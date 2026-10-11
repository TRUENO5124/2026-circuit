/**
 * Convert a CircuitGraph (from @circuit/schema) into React Flow nodes and edges.
 *
 * Mapping:
 *   ComponentInstance → React Flow Node (type = definitionId)
 *   Node (electrical) → React Flow Node (type = "junction")
 *   Connection         → React Flow Edge (junction → component port)
 */

import type { CircuitGraph, Connection } from "@circuit/schema";
import type { Node, Edge } from "@xyflow/react";
import type { ComponentNodeData, JunctionNodeData } from "../types/editor";

/** Prefix for junction node IDs in React Flow to avoid collision with component IDs */
const JUNCTION_PREFIX = "jn_";

export interface FlowResult {
  nodes: Node[];
  edges: Edge[];
}

/**
 * Build a junction node ID from the circuit graph node ID.
 */
export function junctionNodeId(circuitNodeId: string): string {
  return `${JUNCTION_PREFIX}${circuitNodeId}`;
}

/**
 * Extract the original circuit node ID from a junction's React Flow node ID.
 */
export function originalNodeId(junctionId: string): string {
  if (junctionId.startsWith(JUNCTION_PREFIX)) {
    return junctionId.slice(JUNCTION_PREFIX.length);
  }
  return junctionId;
}

/**
 * Convert a CircuitGraph to React Flow nodes + edges.
 */
export function circuitGraphToFlow(graph: CircuitGraph): FlowResult {
  const nodes: Node[] = [];
  const edges: Edge[] = [];

  // 1) Component instances → React Flow nodes
  for (const comp of graph.components) {
    const pos = comp.position ?? { x: 0, y: 0 };
    const data: ComponentNodeData = {
      definitionId: comp.definitionId,
      label: comp.label,
      ports: comp.ports,
      parameters: comp.parameters,
      modelVariant: comp.modelVariant,
      rotation: comp.rotation ?? 0,
    };
    nodes.push({
      id: comp.id,
      type: comp.definitionId, // one registered type per definitionId
      position: { x: pos.x, y: pos.y },
      data,
    });
  }

  // 2) Circuit nodes → junction React Flow nodes
  for (const node of graph.nodes) {
    const pos = node.position ?? { x: 0, y: 0 };
    const data: JunctionNodeData = {
      originalNodeId: node.id,
      label: node.label,
      kind: node.kind,
    };
    nodes.push({
      id: junctionNodeId(node.id),
      type: "junction",
      position: { x: pos.x, y: pos.y },
      data,
    });
  }

  // 3) Connections → React Flow edges (junction → component)
  for (const conn of graph.connections) {
    edges.push(makeEdge(conn));
  }

  return { nodes, edges };
}

/**
 * Create a single React Flow edge from a Connection.
 * Direction: junction node → component port.
 */
function makeEdge(conn: Connection): Edge {
  return {
    id: conn.id,
    source: junctionNodeId(conn.nodeId),
    sourceHandle: conn.nodeId,
    target: conn.componentId,
    targetHandle: conn.portId,
    type: "smoothstep",
    style: { stroke: "#333", strokeWidth: 2 },
  };
}
