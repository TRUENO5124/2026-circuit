/**
 * Convert React Flow state (nodes + edges) back to a CircuitGraph.
 *
 * Uses Disjoint Set Union (DSU) to group connected component ports and
 * junction/ground nodes into clean electrical nets.
 */

import type { CircuitGraph, ComponentInstance, Connection } from "@circuit/schema";
import type { Node, Edge } from "@xyflow/react";
import type { ComponentNodeData, JunctionNodeData } from "../types/editor";
import { originalNodeId } from "./circuitToFlow";
import { PORT_LAYOUT } from "./portGeometry";

class DSU {
  private parent = new Map<string, string>();

  find(x: string): string {
    if (!this.parent.has(x)) {
      this.parent.set(x, x);
      return x;
    }
    const p = this.parent.get(x)!;
    if (p === x) return x;
    const root = this.find(p);
    this.parent.set(x, root);
    return root;
  }

  union(a: string, b: string): void {
    const rootA = this.find(a);
    const rootB = this.find(b);
    if (rootA !== rootB) {
      this.parent.set(rootA, rootB);
    }
  }
}

export function flowToCircuitGraph(
  circuitId: string,
  circuitVersion: string,
  circuitTitle: string | undefined,
  flowNodes: Node[],
  flowEdges: Edge[],
): CircuitGraph {
  const components: ComponentInstance[] = [];
  const nodes: CircuitGraph["nodes"] = [];
  const connections: Connection[] = [];

  const nodeMap = new Map<string, Node>(flowNodes.map((n) => [n.id, n]));
  const junctionNodes = new Map<string, JunctionNodeData>();
  const isGroundComp = new Set<string>();

  // 1. Collect components and junction nodes
  for (const fn of flowNodes) {
    if (fn.type === "junction") {
      const d = fn.data as JunctionNodeData;
      junctionNodes.set(fn.id, d);
      // Register existing junction node in graph nodes
      nodes.push({
        id: d.originalNodeId,
        label: d.label,
        kind: d.kind,
        position: { x: fn.position.x, y: fn.position.y },
      });
    } else {
      const d = fn.data as ComponentNodeData;
      if (!d || !d.definitionId) continue;
      const defaultPorts = PORT_LAYOUT[d.definitionId]?.map((p) => p.portId) ?? [];
      const ports = d.ports?.length ? d.ports : defaultPorts;
      if (d.definitionId === "reference.ground") {
        isGroundComp.add(fn.id);
      }
      components.push({
        id: fn.id,
        definitionId: d.definitionId,
        label: d.label,
        ports,
        parameters: d.parameters ?? {},
        modelVariant: d.modelVariant ?? "",
        position: { x: fn.position.x, y: fn.position.y },
        rotation: d.rotation ?? 0,
      });
    }
  }

  // 2. Disjoint Set Union over all edge endpoints
  const dsu = new DSU();
  const terminalMap = new Map<
    string,
    | { kind: "junction"; junctionId: string; data: JunctionNodeData }
    | { kind: "port"; componentId: string; portId: string; isGround: boolean }
  >();

  const getTerminalKey = (nodeId: string, handleId: string | null | undefined): string | null => {
    const fn = nodeMap.get(nodeId);
    if (!fn) return null;
    if (fn.type === "junction") {
      const jData = fn.data as JunctionNodeData;
      const key = fn.id;
      if (!terminalMap.has(key)) {
        terminalMap.set(key, { kind: "junction", junctionId: fn.id, data: jData });
      }
      return key;
    }
    // Component node
    if (!handleId) return null;
    const key = `${nodeId}:${handleId}`;
    if (!terminalMap.has(key)) {
      terminalMap.set(key, {
        kind: "port",
        componentId: nodeId,
        portId: handleId,
        isGround: isGroundComp.has(nodeId),
      });
    }
    return key;
  };

  for (const fe of flowEdges) {
    const srcKey = getTerminalKey(fe.source, fe.sourceHandle);
    const tgtKey = getTerminalKey(fe.target, fe.targetHandle);
    if (srcKey && tgtKey) {
      dsu.union(srcKey, tgtKey);
    }
  }

  // 3. Group terminals by connected component
  const groups = new Map<string, string[]>();
  for (const key of terminalMap.keys()) {
    const root = dsu.find(key);
    const list = groups.get(root) ?? [];
    list.push(key);
    groups.set(root, list);
  }

  // 4. Synthesize nets and connections
  let netCounter = 1;
  const existingNodeIds = new Set(nodes.map((n) => n.id));

  for (const [, terminalKeys] of groups) {
    const items = terminalKeys.map((k) => terminalMap.get(k)!);

    // Check if group touches ground or reference
    const hasGround = items.some(
      (item) =>
        (item.kind === "junction" && (item.data.kind === "reference" || item.data.originalNodeId === "0")) ||
        (item.kind === "port" && item.isGround),
    );

    // Check if group already contains a named junction node
    const explicitJunction = items.find((item) => item.kind === "junction");

    let netId: string;
    let netKind: "electrical" | "reference";

    if (hasGround) {
      netKind = "reference";
      netId = explicitJunction ? explicitJunction.data.originalNodeId : "0";
    } else if (explicitJunction) {
      netKind = explicitJunction.data.kind ?? "electrical";
      netId = explicitJunction.data.originalNodeId;
    } else {
      netKind = "electrical";
      netId = `net_${netCounter++}`;
    }

    // Ensure net exists in graph nodes
    if (!existingNodeIds.has(netId)) {
      nodes.push({
        id: netId,
        label: netId,
        kind: netKind,
      });
      existingNodeIds.add(netId);
    }

    // Add connection for each distinct component port in this net
    const seenPorts = new Set<string>();
    for (const item of items) {
      if (item.kind === "port") {
        const portKey = `${item.componentId}:${item.portId}`;
        if (!seenPorts.has(portKey)) {
          seenPorts.add(portKey);
          connections.push({
            id: `c_${item.componentId}_${item.portId}`,
            nodeId: netId,
            componentId: item.componentId,
            portId: item.portId,
          });
        }
      }
    }
  }

  return {
    id: circuitId,
    version: circuitVersion,
    title: circuitTitle,
    nodes,
    components,
    ports: [],
    connections,
    metadata: {},
    validationIssues: [],
  };
}
