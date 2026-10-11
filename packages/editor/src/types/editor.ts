import type { ParameterValue, CircuitGraph } from "@circuit/schema";
import type { Position } from "@xyflow/react";

/**
 * View modes of the application.
 */
export type ViewMode = "editor" | "analysis" | "report";

/**
 * Toast notification message.
 */
export interface ToastMessage {
  id: string;
  type: "info" | "success" | "warning" | "error";
  title: string;
  message?: string;
  duration?: number;
}

/**
 * Data attached to each component React Flow node.
 * Must satisfy Record<string, unknown> for React Flow's Node generic.
 */
export interface ComponentNodeData {
  [key: string]: unknown;
  definitionId: string;
  label: string;
  ports: string[];
  parameters: Record<string, ParameterValue>;
  modelVariant: string;
  /** Visual rotation used by the schematic editor's rotate shortcut (0, 90, 180, 270). */
  rotation?: number;
}

/**
 * Data attached to each junction React Flow node.
 */
export interface JunctionNodeData {
  [key: string]: unknown;
  originalNodeId: string;
  label?: string;
  kind: "electrical" | "reference";
  /** Optional directional handles used by an auto-created two-terminal net. */
  handlePositions?: Array<{ id: string; position: Position }>;
  /** A two-terminal bridge is electrically a junction but should render as a plain wire. */
  inline?: boolean;
}

/** Union of all possible node data types */
export type EditorNodeData = ComponentNodeData | JunctionNodeData;

/** Port handle position: which side of the component node */
export type HandleSide = "top" | "bottom" | "left" | "right";

/** Maps a port ID to its handle position */
export interface PortLayout {
  portId: string;
  side: HandleSide;
  offset: number;
  label: string;
}

/** Describes how ports are laid out for a component definition */
export interface ComponentLayout {
  definitionId: string;
  ports: PortLayout[];
  width: number;
  height: number;
}

/** Analysis task status from the API */
export interface AnalysisTask {
  taskId: string;
  status: "pending" | "running" | "completed" | "failed";
  result?: unknown;
  error?: string;
}

// ── Analysis result types ─────────────────────────────────────────

/** A single step in the analysis solution process */
export interface AnalysisStep {
  description: string; // Chinese description of this step
  formula: string; // LaTeX string for the formula
  reasoning: string; // Chinese reasoning explanation
}

/** A named equation from the analysis */
export interface AnalysisEquation {
  id: string; // e.g. "eq.vbe_assumption"
  latex: string; // LaTeX string
  variables: string[]; // variable names appearing in the equation
}

/** DC operating point */
export interface AnalysisOperatingPoint {
  nodeVoltages: Record<string, string>; // node_id → LaTeX expression
  branchCurrents: Record<string, string>; // branch_id → LaTeX expression
  regions: Record<string, string>; // device_id → region label (e.g. "active")
}

/** Small-signal gain expressions */
export interface AnalysisGainExpressions {
  voltageGain?: string;
  currentGain?: string;
  transconductance?: string;
}

/** Small-signal impedance expressions */
export interface AnalysisImpedanceExpressions {
  inputResistance?: string;
  outputResistance?: string;
}

/** Full structured analysis result from the Python engine */
export interface AnalysisResult {
  problemType: string; // e.g. "bjt_dc_bias"
  steps: AnalysisStep[];
  equations: AnalysisEquation[];
  operatingPoint?: AnalysisOperatingPoint; // DC-only
  gainExpressions?: AnalysisGainExpressions; // AC-only
  impedanceExpressions?: AnalysisImpedanceExpressions; // AC-only
  teachingTags: string[];
  warnings: string[];
  isSymbolic: boolean;
  error?: string; // present when analysis fails at the engine level
}

/** Built-in benchmark item metadata */
export interface BenchmarkItem {
  id: string;
  title: string;
  category: string;
  difficulty: "基础" | "典型" | "提高";
  problemText: string;
  teachingTags: string[];
  circuitGraph: CircuitGraph;
  equationsCount: number;
  groundTruthSummary: string;
}
