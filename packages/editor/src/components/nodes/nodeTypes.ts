/**
 * Register all custom node types for React Flow.
 *
 * Each component definitionId maps to the unified CircuitNode component.
 * This is data-driven: adding a new definition in packages/components
 * automatically makes it renderable (defaults to showing the definitionId text).
 */

import type { NodeTypes } from "@xyflow/react";
import { componentDefinitions } from "@circuit/components/definitions";
import CircuitNode from "./CircuitNode";
import JunctionNode from "./JunctionNode";

const nodeTypes: NodeTypes = Object.fromEntries(
  componentDefinitions.map((def) => [def.id, CircuitNode]),
);

nodeTypes["junction"] = JunctionNode;

export default nodeTypes;
