/**
 * Interactive circuit schematic canvas with React Flow.
 */

import React, { useCallback, useEffect, useRef, type DragEvent } from "react";
import {
  ReactFlow,
  Background,
  MiniMap,
  BackgroundVariant,
  type ReactFlowInstance,
  SelectionMode,
  ConnectionMode,
  ConnectionLineType,
  type EdgeTypes,
  type Edge,
} from "@xyflow/react";
import "@xyflow/react/dist/style.css";
import { useEditorStore } from "../../store/editorStore";
import nodeTypes from "../nodes/nodeTypes";
import WireEdge from "../edges/WireEdge";
import {
  ZoomIn,
  ZoomOut,
  Maximize2,
  Grid,
  RotateCw,
  Copy,
  Trash2,
  HelpCircle,
} from "lucide-react";

const edgeTypes: EdgeTypes = { wire: WireEdge, default: WireEdge };

export default function CircuitCanvas() {
  const rfInstance = useRef<ReactFlowInstance | null>(null);

  const {
    nodes,
    edges,
    onNodesChange,
    onEdgesChange,
    onConnect,
    addComponent,
    removeSelected,
    selectComponent,
    selectedComponentId,
    moveSelected,
    duplicateSelected,
    rotateSelected,
    undo,
    redo,
    snapToGrid,
    setSnapToGrid,
    setShortcutsOpen,
  } = useEditorStore();

  const onDragOver = useCallback((e: DragEvent) => {
    e.preventDefault();
    e.dataTransfer.dropEffect = "move";
  }, []);

  const onDrop = useCallback(
    (e: DragEvent) => {
      e.preventDefault();
      const definitionId = e.dataTransfer.getData("application/definitionId");
      const modelVariant = e.dataTransfer.getData("application/modelVariant") || undefined;
      if (!definitionId || !rfInstance.current) return;
      const position = rfInstance.current.screenToFlowPosition({
        x: e.clientX,
        y: e.clientY,
      });
      addComponent(definitionId, position, modelVariant);
    },
    [addComponent],
  );

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (useEditorStore.getState().shortcutsOpen) return;
      const target = event.target as HTMLElement | null;
      if (
        target?.tagName === "INPUT" ||
        target?.tagName === "TEXTAREA" ||
        target?.tagName === "SELECT" ||
        target?.isContentEditable
      )
        return;

      const key = event.key.toLowerCase();
      const primary = event.ctrlKey || event.metaKey;

      if (primary && key === "z") {
        event.preventDefault();
        if (event.shiftKey) redo();
        else undo();
        return;
      }
      if (primary && key === "y") {
        event.preventDefault();
        redo();
        return;
      }
      if (primary && key === "d") {
        event.preventDefault();
        duplicateSelected();
        return;
      }
      if (key === "delete" || key === "backspace") {
        event.preventDefault();
        removeSelected();
        return;
      }
      if (key === "r") {
        event.preventDefault();
        rotateSelected();
        return;
      }
      if (key === "escape") {
        selectComponent(null);
        return;
      }
      const step = event.shiftKey ? 10 : 1;
      if (
        event.key === "ArrowUp" ||
        event.key === "ArrowDown" ||
        event.key === "ArrowLeft" ||
        event.key === "ArrowRight"
      ) {
        event.preventDefault();
        moveSelected(
          event.key === "ArrowLeft" ? -step : event.key === "ArrowRight" ? step : 0,
          event.key === "ArrowUp" ? -step : event.key === "ArrowDown" ? step : 0,
        );
      }
    };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [
    duplicateSelected,
    moveSelected,
    redo,
    removeSelected,
    rotateSelected,
    selectComponent,
    undo,
  ]);

  const onEdgeClick = useCallback(
    (_event: React.MouseEvent, edge: Edge) => {
      selectComponent(null);
      onEdgesChange(
        edges.map((cand) => ({
          type: "select" as const,
          id: cand.id,
          selected: cand.id === edge.id,
        })),
      );
    },
    [edges, onEdgesChange, selectComponent],
  );

  const onNodeClick = useCallback(
    (_event: React.MouseEvent, node: { id: string }) => {
      selectComponent(node.id);
      onEdgesChange(
        edges.map((cand) => ({
          type: "select" as const,
          id: cand.id,
          selected: false,
        })),
      );
    },
    [edges, onEdgesChange, selectComponent],
  );

  const onPaneClick = useCallback(() => {
    selectComponent(null);
    onEdgesChange(
      edges.map((cand) => ({
        type: "select" as const,
        id: cand.id,
        selected: false,
      })),
    );
  }, [edges, onEdgesChange, selectComponent]);

  const handleZoomIn = () => rfInstance.current?.zoomIn({ duration: 250 });
  const handleZoomOut = () => rfInstance.current?.zoomOut({ duration: 250 });
  const handleFitView = () => rfInstance.current?.fitView({ duration: 300, padding: 0.2 });

  const hasSelection = selectedComponentId !== null || edges.some((e) => e.selected);

  const isValidConnection = useCallback(
    (connection: { source: string; target: string; sourceHandle?: string | null; targetHandle?: string | null }) => {
      if (connection.source === connection.target && connection.sourceHandle === connection.targetHandle) {
        return false;
      }
      return true;
    },
    [],
  );

  return (
    <main className="app-canvas-container" aria-label="电路原理图交互画布">
      {/* Floating Canvas Top Controls / HUD */}
      <div className="canvas-floating-hud">
        <div className="canvas-hud-pill">
          <button className="hud-btn" onClick={handleZoomIn} title="放大画布 (+)">
            <ZoomIn size={15} />
          </button>
          <button className="hud-btn" onClick={handleZoomOut} title="缩小画布 (-)">
            <ZoomOut size={15} />
          </button>
          <button className="hud-btn" onClick={handleFitView} title="适应屏幕视图 (Fit View)">
            <Maximize2 size={15} />
          </button>
          <div className="hud-divider" />
          <button
            className={`hud-btn ${snapToGrid ? "hud-btn--active" : ""}`}
            onClick={() => setSnapToGrid(!snapToGrid)}
            title="对齐到网格 (Snap to Grid)"
          >
            <Grid size={15} />
          </button>
        </div>

        {/* Selected Component Quick Action Bar */}
        {hasSelection && (
          <div className="canvas-selection-pill">
            <span className="selection-label">已选定对象</span>
            <button className="selection-action-btn" onClick={rotateSelected} title="旋转 (R)">
              <RotateCw size={14} /> 旋转
            </button>
            <button className="selection-action-btn" onClick={duplicateSelected} title="复制 (Ctrl+D)">
              <Copy size={14} /> 复制
            </button>
            <button
              className="selection-action-btn selection-action-btn--danger"
              onClick={removeSelected}
              title="删除 (Delete)"
            >
              <Trash2 size={14} /> 删除
            </button>
          </div>
        )}
      </div>

      <ReactFlow
        nodes={nodes}
        edges={edges}
        onNodesChange={onNodesChange}
        onEdgesChange={onEdgesChange}
        onConnect={onConnect}
        isValidConnection={isValidConnection}
        onInit={(instance) => {
          rfInstance.current = instance;
        }}
        onDrop={onDrop}
        onDragOver={onDragOver}
        onNodeClick={onNodeClick}
        onEdgeClick={onEdgeClick}
        onPaneClick={onPaneClick}
        nodeTypes={nodeTypes}
        edgeTypes={edgeTypes}
        connectionMode={ConnectionMode.Loose}
        connectionLineType={ConnectionLineType.SmoothStep}
        connectionLineStyle={{ stroke: "#0ea5e9", strokeWidth: 2 }}
        defaultEdgeOptions={{ type: "wire" }}
        fitView
        deleteKeyCode={[]}
        selectionMode={SelectionMode.Partial}
        snapToGrid={snapToGrid}
        snapGrid={[10, 10]}
      >
        <Background variant={BackgroundVariant.Dots} color="#cbd5e1" gap={20} size={1.2} />
        <MiniMap
          nodeStrokeWidth={2}
          pannable
          zoomable
          className="canvas-minimap-card"
          nodeColor={(n) => (n.id === selectedComponentId ? "#0ea5e9" : "#94a3b8")}
        />
      </ReactFlow>

      {/* Floating Canvas Bottom Hint */}
      <div className="canvas-bottom-hint">
        <span>从引脚端点拖出导线进行连接</span>
        <span>·</span>
        <span>按 <kbd>R</kbd> 旋转</span>
        <span>·</span>
        <span>按 <kbd>Del</kbd> 删除</span>
        <span>·</span>
        <button className="hint-link" onClick={() => setShortcutsOpen(true)}>
          更多快捷键
        </button>
      </div>
    </main>
  );
}
