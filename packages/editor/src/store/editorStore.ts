/**
 * Zustand store for the circuit editor.
 *
 * Wraps React Flow's node/edge state and adds high-level actions
 * for component manipulation, validation, view mode switching,
 * toast notifications, and modals.
 */

import { create } from "zustand";
import {
  type Node,
  type Edge,
  type OnNodesChange,
  type OnEdgesChange,
  type OnConnect,
  type XYPosition,
  applyNodeChanges,
  applyEdgeChanges,
  addEdge,
} from "@xyflow/react";
import type { CircuitGraph, ParameterValue, ValidationIssue, BenchmarkCase } from "@circuit/schema";
import { componentDefinitions } from "@circuit/components/definitions";
import { validateCircuitGraph } from "@circuit/schema/validators";
import type {
  ComponentNodeData,
  JunctionNodeData,
  AnalysisResult,
  ViewMode,
  ToastMessage,
} from "../types/editor";
import { circuitGraphToFlow } from "../lib/circuitToFlow";
import { flowToCircuitGraph } from "../lib/flowToCircuit";
import { nextComponentId, nextEdgeId, resetCounters } from "../lib/idGenerator";

type HistoryEntry = { nodes: Node[]; edges: Edge[] };

function remember(state: Pick<EditorState, "nodes" | "edges" | "historyPast">) {
  const entry: HistoryEntry = { nodes: state.nodes, edges: state.edges };
  return [...state.historyPast.slice(-49), entry];
}

export interface EditorState {
  // ── View mode ──
  viewMode: ViewMode;
  setViewMode: (mode: ViewMode) => void;

  // ── React Flow raw state ──
  nodes: Node[];
  edges: Edge[];
  onNodesChange: OnNodesChange;
  onEdgesChange: OnEdgesChange;
  onConnect: OnConnect;

  // ── View & Canvas state ──
  selectedComponentId: string | null;
  parameterPanelOpen: boolean;
  snapToGrid: boolean;
  setSnapToGrid: (snap: boolean) => void;
  symbolStandard: "iec" | "ansi";
  setSymbolStandard: (standard: "iec" | "ansi") => void;

  // ── Modals state ──
  shortcutsOpen: boolean;
  setShortcutsOpen: (open: boolean) => void;
  benchmarkGalleryOpen: boolean;
  setBenchmarkGalleryOpen: (open: boolean) => void;
  mobileCaptureOpen: boolean;
  setMobileCaptureOpen: (open: boolean) => void;
  exportStudioOpen: boolean;
  setExportStudioOpen: (open: boolean) => void;
  symbolLibraryOpen: boolean;
  setSymbolLibraryOpen: (open: boolean) => void;

  // ── Toast notifications ──
  toasts: ToastMessage[];
  addToast: (toast: Omit<ToastMessage, "id">) => void;
  removeToast: (id: string) => void;

  // ── Validation ──
  validationIssues: ValidationIssue[];
  lastValidatedAt: number | null;

  // ── Circuit identity ──
  circuitId: string;
  circuitVersion: string;
  circuitTitle: string;
  setCircuitTitle: (title: string) => void;

  // ── Analysis ──
  analysisStatus: "idle" | "submitting" | "polling" | "completed" | "failed";
  analysisTaskId: string | null;
  analysisResult: AnalysisResult | null;
  analysisError: string | null;

  // ── Undo / redo ──
  historyPast: Array<{ nodes: Node[]; edges: Edge[] }>;
  historyFuture: Array<{ nodes: Node[]; edges: Edge[] }>;

  // ── Actions: mutations ──
  addComponent: (definitionId: string, position: XYPosition, modelVariant?: string) => void;
  addDefaultComponent: (definitionId: string) => void;
  removeSelected: () => void;
  updateParameter: (componentId: string, paramId: string, value: ParameterValue["value"]) => void;
  updateLabel: (componentId: string, label: string) => void;
  updateModelVariant: (componentId: string, variant: string) => void;
  selectComponent: (id: string | null) => void;
  moveSelected: (dx: number, dy: number) => void;
  duplicateSelected: () => void;
  rotateSelected: () => void;
  undo: () => void;
  redo: () => void;

  // ── Actions: I/O ──
  loadCircuitGraph: (graph: CircuitGraph) => void;
  exportCircuitGraph: () => CircuitGraph;
  loadBenchmark: (benchmark: BenchmarkCase) => void;
  clearCanvas: () => void;

  // ── Actions: validation ──
  validate: () => ValidationIssue[];

  // ── Actions: analysis ──
  setAnalysisStatus: (status: EditorState["analysisStatus"]) => void;
  setAnalysisResult: (result: AnalysisResult | null, error: string | null) => void;
}

export const useEditorStore = create<EditorState>((set, get) => ({
  viewMode: "editor",
  setViewMode: (mode) => set({ viewMode: mode }),

  nodes: [],
  edges: [],
  selectedComponentId: null,
  parameterPanelOpen: false,
  snapToGrid: true,
  setSnapToGrid: (snap) => set({ snapToGrid: snap }),
  symbolStandard: "iec",
  setSymbolStandard: (standard) => set({ symbolStandard: standard }),

  shortcutsOpen: false,
  setShortcutsOpen: (open) => set({ shortcutsOpen: open }),
  benchmarkGalleryOpen: false,
  setBenchmarkGalleryOpen: (open) => set({ benchmarkGalleryOpen: open }),
  mobileCaptureOpen: false,
  setMobileCaptureOpen: (open) => set({ mobileCaptureOpen: open }),
  exportStudioOpen: false,
  setExportStudioOpen: (open) => set({ exportStudioOpen: open }),
  symbolLibraryOpen: false,
  setSymbolLibraryOpen: (open) => set({ symbolLibraryOpen: open }),

  toasts: [],
  addToast: (toast) => {
    const id = `toast_${Date.now()}_${Math.random().toString(36).substring(2, 6)}`;
    const newToast: ToastMessage = { ...toast, id };
    set((s) => ({ toasts: [...s.toasts, newToast] }));
    const duration = toast.duration ?? 3500;
    if (duration > 0) {
      setTimeout(() => {
        get().removeToast(id);
      }, duration);
    }
  },
  removeToast: (id) => {
    set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) }));
  },

  validationIssues: [],
  lastValidatedAt: null,
  circuitId: "graph.editor_001",
  circuitVersion: "0.1.0",
  circuitTitle: "BJT 共射分压偏置放大电路",
  setCircuitTitle: (title) => set({ circuitTitle: title }),

  analysisStatus: "idle",
  analysisTaskId: null,
  analysisResult: null,
  analysisError: null,
  historyPast: [],
  historyFuture: [],

  // ── React Flow handlers ──

  onNodesChange: (changes) => {
    set((s) => {
      const next = applyNodeChanges(changes, s.nodes);
      const selected = changes.filter((c) => c.type === "select");
      const lastSelected = selected.length > 0 ? selected[selected.length - 1] : null;
      const selId =
        lastSelected && lastSelected.selected ? lastSelected.id : null;
      const committedMove = changes.some(
        (change) => change.type === "position" && change.dragging === false,
      );
      return {
        nodes: next,
        selectedComponentId: selId ?? s.selectedComponentId,
        parameterPanelOpen: selId ? true : s.parameterPanelOpen,
        ...(committedMove
          ? { historyPast: remember(s), historyFuture: [] }
          : {}),
      };
    });
  },

  onEdgesChange: (changes) => {
    set((s) => ({
      edges: applyEdgeChanges(changes, s.edges),
      ...(changes.some((change) => change.type === "remove")
        ? { historyPast: remember(s), historyFuture: [] }
        : {}),
    }));
  },

  onConnect: (connection) => {
    if (!connection.source || !connection.target) return;
    if (connection.source === connection.target && connection.sourceHandle === connection.targetHandle) return;

    const { edges } = get();

    const exists = edges.some(
      (e) =>
        (e.source === connection.source &&
          e.sourceHandle === connection.sourceHandle &&
          e.target === connection.target &&
          e.targetHandle === connection.targetHandle) ||
        (e.source === connection.target &&
          e.sourceHandle === connection.targetHandle &&
          e.target === connection.source &&
          e.targetHandle === connection.sourceHandle),
    );
    if (exists) return;

    const newEdge: Edge = {
      id: nextEdgeId(),
      source: connection.source,
      sourceHandle: connection.sourceHandle ?? null,
      target: connection.target,
      targetHandle: connection.targetHandle ?? null,
      type: "wire",
      style: { stroke: "#334155", strokeWidth: 2 },
    };

    set((s) => ({
      edges: addEdge(newEdge, s.edges),
      historyPast: remember(s),
      historyFuture: [],
    }));
  },

  // ── Actions: mutations ──

  addComponent: (definitionId, position, requestedVariant) => {
    const def = componentDefinitions.find((d) => d.id === definitionId);
    if (!def) return;

    const compId = nextComponentId(definitionId);

    const parameters: Record<string, ParameterValue> = {};
    for (const pd of def.parameters) {
      if (pd.defaultValue !== undefined) {
        parameters[pd.id] = pd.defaultValue;
      } else {
        const defaultVal =
          pd.id === "resistance" ? 1000 :
          pd.id === "capacitance" ? 1e-6 :
          pd.id === "inductance" ? 1e-3 :
          pd.id === "voltage" || pd.id === "amplitude" ? 5 :
          pd.id === "current" ? 0.001 :
          pd.id === "frequency" ? 1000 : 0;
        parameters[pd.id] = {
          value: defaultVal,
          unit: pd.unit,
          source: "default" as const,
        };
      }
    }

    const modelVariant = requestedVariant && def.allowedModelVariants.includes(requestedVariant)
      ? requestedVariant
      : def.defaultModelVariant;
    const data: ComponentNodeData = {
      definitionId,
      label: compId,
      ports: def.ports.map((p) => p.id),
      parameters,
      modelVariant,
      rotation: 0,
    };

    const node: Node = {
      id: compId,
      type: definitionId,
      position,
      data,
    };

    set((s) => ({
      nodes: [...s.nodes, node],
      selectedComponentId: compId,
      parameterPanelOpen: true,
      historyPast: remember(s),
      historyFuture: [],
    }));
  },

  addDefaultComponent: (definitionId) => {
    const { nodes, addComponent } = get();
    const count = nodes.length;
    const posX = 160 + (count % 4) * 160;
    const posY = 120 + Math.floor(count / 4) * 140;
    addComponent(definitionId, { x: posX, y: posY });
    get().addToast({
      type: "info",
      title: "已放置元件",
      message: `放置了 ${definitionId}`,
      duration: 2000,
    });
  },

  removeSelected: () => {
    const { nodes, edges, selectedComponentId } = get();

    const selectedEdgeIds = new Set(
      edges.filter((edge) => edge.selected).map((edge) => edge.id),
    );
    if (selectedEdgeIds.size > 0) {
      const remainingEdges = edges.filter((edge) => !selectedEdgeIds.has(edge.id));
      const edgeCount = new Map<string, number>();
      for (const edge of remainingEdges) {
        edgeCount.set(edge.source, (edgeCount.get(edge.source) ?? 0) + 1);
        edgeCount.set(edge.target, (edgeCount.get(edge.target) ?? 0) + 1);
      }
      const orphanJunctions = new Set<string>();
      for (const node of nodes) {
        if (node.type === "junction" && (edgeCount.get(node.id) ?? 0) < 2) {
          orphanJunctions.add(node.id);
        }
      }
      set({
        nodes: nodes.filter((node) => !orphanJunctions.has(node.id)),
        edges: remainingEdges.filter(
          (edge) => !orphanJunctions.has(edge.source) && !orphanJunctions.has(edge.target),
        ),
        selectedComponentId: null,
        parameterPanelOpen: false,
        historyPast: remember({ ...get(), nodes, edges }),
        historyFuture: [],
      });
      return;
    }

    if (!selectedComponentId) {
      const selectedIds = new Set(
        nodes.filter((n) => n.selected).map((n) => n.id),
      );
      if (selectedIds.size === 0) return;

      const remainingEdges = edges.filter(
        (e) => !selectedIds.has(e.source) && !selectedIds.has(e.target),
      );

      const edgeCount = new Map<string, number>();
      for (const e of remainingEdges) {
        edgeCount.set(e.source, (edgeCount.get(e.source) ?? 0) + 1);
        edgeCount.set(e.target, (edgeCount.get(e.target) ?? 0) + 1);
      }
      const orphanJunctions = new Set<string>();
      for (const n of nodes) {
        if (n.type === "junction" && !selectedIds.has(n.id)) {
          if ((edgeCount.get(n.id) ?? 0) < 2) {
            orphanJunctions.add(n.id);
          }
        }
      }

      set({
        nodes: nodes.filter(
          (n) => !selectedIds.has(n.id) && !orphanJunctions.has(n.id),
        ),
        edges: remainingEdges.filter(
          (e) => !orphanJunctions.has(e.source) && !orphanJunctions.has(e.target),
        ),
        selectedComponentId: null,
        parameterPanelOpen: false,
        historyPast: remember({ ...get(), nodes, edges }),
        historyFuture: [],
      });
      return;
    }

    const targetId = selectedComponentId;
    set({
      nodes: nodes.filter((n) => n.id !== targetId),
      edges: edges.filter(
        (e) => e.source !== targetId && e.target !== targetId,
      ),
      selectedComponentId: null,
      parameterPanelOpen: false,
      historyPast: remember({ ...get(), nodes, edges }),
      historyFuture: [],
    });
  },

  updateParameter: (componentId, paramId, value) => {
    set((s) => {
      const node = s.nodes.find((n) => n.id === componentId);
      if (!node || node.type === "junction") return s;

      const data = { ...node.data } as ComponentNodeData;
      const def = componentDefinitions.find((d) => d.id === data.definitionId);
      const pd = def?.parameters.find((p) => p.id === paramId);
      const existing = data.parameters[paramId];
      data.parameters = {
        ...data.parameters,
        [paramId]: {
          value,
          unit: existing?.unit ?? pd?.unit,
          source: "user" as const,
        },
      };

      return {
        nodes: s.nodes.map((n) =>
          n.id === componentId
            ? { ...n, data: { ...n.data, ...data } }
            : n,
        ),
        historyPast: remember(s),
        historyFuture: [],
      };
    });
  },

  updateLabel: (componentId, label) => {
    set((s) => ({
      nodes: s.nodes.map((n) =>
        n.id === componentId
          ? { ...n, data: { ...n.data, label } }
          : n,
      ),
      historyPast: remember(s),
      historyFuture: [],
    }));
  },

  updateModelVariant: (componentId, variant) => {
    set((s) => ({
      nodes: s.nodes.map((n) =>
        n.id === componentId
          ? { ...n, data: { ...n.data, modelVariant: variant } }
          : n,
      ),
      historyPast: remember(s),
      historyFuture: [],
    }));
  },

  selectComponent: (id) => {
    set({ selectedComponentId: id, parameterPanelOpen: id !== null });
  },

  moveSelected: (dx, dy) => {
    set((s) => {
      const selectedIds = new Set(
        s.nodes.filter((node) => node.selected || node.id === s.selectedComponentId).map((node) => node.id),
      );
      if (selectedIds.size === 0) return s;
      return {
        nodes: s.nodes.map((node) =>
          selectedIds.has(node.id)
            ? { ...node, position: { x: node.position.x + dx, y: node.position.y + dy } }
            : node,
        ),
        historyPast: remember(s),
        historyFuture: [],
      };
    });
  },

  duplicateSelected: () => {
    set((s) => {
      const source = s.nodes.find(
        (node) => node.id === s.selectedComponentId || node.selected,
      );
      if (!source || source.type === "junction") return s;
      const sourceData = source.data as unknown as ComponentNodeData;
      const copyId = nextComponentId(sourceData.definitionId);
      const copy: Node = {
        ...source,
        id: copyId,
        selected: true,
        position: { x: source.position.x + 30, y: source.position.y + 30 },
        data: { ...source.data, label: copyId },
      };
      return {
        nodes: [...s.nodes.map((node) => ({ ...node, selected: false })), copy],
        selectedComponentId: copyId,
        parameterPanelOpen: true,
        historyPast: remember(s),
        historyFuture: [],
      };
    });
  },

  rotateSelected: () => {
    set((s) => {
      const selectedIds = new Set(
        s.nodes.filter((node) => node.selected || node.id === s.selectedComponentId).map((node) => node.id),
      );
      if (selectedIds.size === 0) return s;
      const rotatedNodes = s.nodes.map((node) => {
        if (!selectedIds.has(node.id) || node.type === "junction") return node;
        const data = node.data as unknown as ComponentNodeData;
        return { ...node, data: { ...node.data, rotation: ((data.rotation ?? 0) + 90) % 360 } };
      });
      return {
        nodes: rotatedNodes,
        historyPast: remember(s),
        historyFuture: [],
      };
    });
  },

  undo: () => {
    set((s) => {
      const previous = s.historyPast.at(-1);
      if (!previous) return s;
      return {
        nodes: previous.nodes,
        edges: previous.edges,
        historyPast: s.historyPast.slice(0, -1),
        historyFuture: [...s.historyFuture, { nodes: s.nodes, edges: s.edges }],
        selectedComponentId: null,
        parameterPanelOpen: false,
      };
    });
  },

  redo: () => {
    set((s) => {
      const next = s.historyFuture.at(-1);
      if (!next) return s;
      return {
        nodes: next.nodes,
        edges: next.edges,
        historyFuture: s.historyFuture.slice(0, -1),
        historyPast: [...s.historyPast, { nodes: s.nodes, edges: s.edges }],
        selectedComponentId: null,
        parameterPanelOpen: false,
      };
    });
  },

  // ── Actions: I/O ──

  loadCircuitGraph: (graph) => {
    resetCounters();
    const { nodes, edges } = circuitGraphToFlow(graph);
    set({
      nodes,
      edges,
      circuitId: graph.id,
      circuitVersion: graph.version,
      circuitTitle: graph.title ?? "Untitled Circuit",
      selectedComponentId: null,
      parameterPanelOpen: false,
      validationIssues: [],
      lastValidatedAt: null,
      analysisResult: null,
      analysisStatus: "idle",
      analysisError: null,
      historyPast: [],
      historyFuture: [],
    });
  },

  exportCircuitGraph: (): CircuitGraph => {
    const { nodes, edges, circuitId, circuitVersion, circuitTitle } = get();
    return flowToCircuitGraph(circuitId, circuitVersion, circuitTitle, nodes, edges);
  },

  loadBenchmark: (benchmark) => {
    get().loadCircuitGraph(benchmark.circuitGraph);
    set({ circuitTitle: benchmark.title });
    get().addToast({
      type: "success",
      title: "已加载题目",
      message: benchmark.title,
    });
  },

  clearCanvas: () => {
    resetCounters();
    set({
      nodes: [],
      edges: [],
      selectedComponentId: null,
      parameterPanelOpen: false,
      validationIssues: [],
      lastValidatedAt: null,
      analysisResult: null,
      analysisStatus: "idle",
      analysisError: null,
      circuitId: "graph.editor_001",
      circuitTitle: "未命名电路",
      historyPast: [],
      historyFuture: [],
    });
    get().addToast({
      type: "info",
      title: "画布已清空",
      duration: 2000,
    });
  },

  // ── Actions: validation ──

  validate: () => {
    const state = get();
    try {
      const graph = flowToCircuitGraph(
        state.circuitId,
        state.circuitVersion,
        state.circuitTitle,
        state.nodes,
        state.edges,
      );
      const issues = validateCircuitGraph(graph, componentDefinitions as any);
      set({ validationIssues: issues, lastValidatedAt: Date.now() });
      return issues;
    } catch (e) {
      const issue: ValidationIssue = {
        severity: "error",
        code: "export_error",
        message: `电路导出检查失败: ${String(e)}`,
        path: [],
      };
      set({ validationIssues: [issue], lastValidatedAt: Date.now() });
      return [issue];
    }
  },

  // ── Actions: analysis ──

  setAnalysisStatus: (status) => {
    set({ analysisStatus: status });
  },

  setAnalysisResult: (result, error) => {
    set({ analysisResult: result, analysisError: error });
  },
}));
