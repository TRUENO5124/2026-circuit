/**
 * Right inspector and parameter panel.
 *
 * Displays detailed parameter controls, model variant options,
 * and port connectivity when a component is selected.
 * When nothing is selected, displays circuit overview and validation status.
 */

import React, { useState, useEffect, useMemo } from "react";
import { useEditorStore } from "../../store/editorStore";
import { componentDefinitions } from "@circuit/components/definitions";
import type { ComponentNodeData } from "../../types/editor";
import { SYMBOLS } from "../nodes/symbols/index";
import { getUnitOptions, decomposeBaseValue, UnitPrefixOption } from "../../lib/units";
import {
  Sliders,
  Settings,
  RotateCw,
  Copy,
  Trash2,
  CheckCircle2,
  AlertTriangle,
  Info,
  X,
  Share2,
  Cpu,
  Layers,
  HelpCircle,
} from "lucide-react";

function NumericParamWithUnit({
  paramDef,
  baseValue,
  options,
  onChange,
}: {
  paramDef: any;
  baseValue: number;
  options: UnitPrefixOption[];
  onChange: (val: number) => void;
}) {
  const decomposed = useMemo(() => {
    return decomposeBaseValue(baseValue, paramDef.unit);
  }, [baseValue, paramDef.unit]);

  const [localDisplay, setLocalDisplay] = useState<string>(
    decomposed ? String(decomposed.displayValue) : String(baseValue),
  );
  const [selectedMultiplier, setSelectedMultiplier] = useState<number>(
    decomposed ? decomposed.selectedOption.multiplier : options[0].multiplier,
  );

  useEffect(() => {
    if (decomposed) {
      setLocalDisplay(String(decomposed.displayValue));
      setSelectedMultiplier(decomposed.selectedOption.multiplier);
    }
  }, [baseValue, paramDef.unit]);

  const handleInputChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const rawStr = e.target.value;
    setLocalDisplay(rawStr);
    const num = parseFloat(rawStr);
    if (!isNaN(num)) {
      onChange(num * selectedMultiplier);
    }
  };

  const handleUnitChange = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const newMult = parseFloat(e.target.value);
    setSelectedMultiplier(newMult);
    const num = parseFloat(localDisplay);
    if (!isNaN(num)) {
      onChange(num * newMult);
    }
  };

  const currentOption = options.find((o) => o.multiplier === selectedMultiplier) ?? options[0];

  return (
    <div className="param-field-item">
      <div className="param-field-header">
        <span className="param-name">{paramDef.label}</span>
        <span className="param-unit-badge">{currentOption.symbol}</span>
      </div>
      <div style={{ display: "flex", gap: "6px", alignItems: "center" }}>
        <input
          type="number"
          step="any"
          value={localDisplay}
          onChange={handleInputChange}
          className="inspector-input"
          style={{ flex: 1 }}
          placeholder={`输入 ${paramDef.label}`}
        />
        <select
          value={selectedMultiplier}
          onChange={handleUnitChange}
          className="inspector-select"
          style={{
            width: "92px",
            fontWeight: 600,
            color: "#0284c7",
            padding: "6px 6px",
            backgroundColor: "#f8fafc",
            border: "1px solid #cbd5e1",
            borderRadius: "6px",
            fontSize: "12px",
          }}
          title="选择国际工程单位 (如 Ω, kΩ, MΩ)"
        >
          {options.map((opt) => (
            <option key={opt.symbol} value={opt.multiplier}>
              {opt.symbol}
            </option>
          ))}
        </select>
      </div>
    </div>
  );
}

function ParameterFieldItem({
  paramDef,
  currentVal,
  onChange,
}: {
  paramDef: any;
  currentVal: any;
  onChange: (val: any) => void;
}) {
  const options = getUnitOptions(paramDef.unit);

  if (paramDef.valueType === "number" && options && options.length > 0) {
    const numVal = typeof currentVal === "number" ? currentVal : parseFloat(currentVal) || 0;
    return (
      <NumericParamWithUnit
        paramDef={paramDef}
        baseValue={numVal}
        options={options}
        onChange={onChange}
      />
    );
  }

  return (
    <div className="param-field-item">
      <div className="param-field-header">
        <span className="param-name">{paramDef.label}</span>
        <span className="param-unit-badge">{paramDef.unit ?? "-"}</span>
      </div>
      <input
        type={paramDef.valueType === "number" ? "number" : "text"}
        step="any"
        value={typeof currentVal === "boolean" ? String(currentVal) : currentVal}
        onChange={(e) => {
          const val =
            paramDef.valueType === "number"
              ? parseFloat(e.target.value) || 0
              : e.target.value;
          onChange(val);
        }}
        className="inspector-input"
        placeholder={`输入 ${paramDef.label}`}
      />
    </div>
  );
}

export default function ParameterPanel() {
  const {
    nodes,
    edges,
    selectedComponentId,
    selectComponent,
    updateParameter,
    updateLabel,
    updateModelVariant,
    rotateSelected,
    duplicateSelected,
    removeSelected,
    validationIssues,
    validate,
  } = useEditorStore();

  const selectedNode = nodes.find((n) => n.id === selectedComponentId);
  const isComponent = selectedNode && selectedNode.type !== "junction";
  const compData = isComponent ? (selectedNode.data as unknown as ComponentNodeData) : null;
  const def = compData
    ? componentDefinitions.find((d) => d.id === compData.definitionId)
    : null;

  // Determine connected ports
  const connectedPorts = React.useMemo(() => {
    if (!selectedNode) return new Set<string>();
    const ports = new Set<string>();
    for (const edge of edges) {
      if (edge.source === selectedNode.id && edge.sourceHandle) {
        ports.add(edge.sourceHandle);
      }
      if (edge.target === selectedNode.id && edge.targetHandle) {
        ports.add(edge.targetHandle);
      }
    }
    return ports;
  }, [selectedNode, edges]);

  // Overall circuit statistics
  const componentCount = nodes.filter((n) => n.type !== "junction").length;
  const junctionCount = nodes.filter((n) => n.type === "junction").length;
  const wireCount = edges.length;
  const hasGround = nodes.some(
    (n) =>
      (n.data as any)?.definitionId === "reference.ground" ||
      (n.data as any)?.kind === "reference",
  );

  return (
    <aside className="app-inspector-sidebar">
      {/* Inspector Header */}
      <div className="inspector-header">
        <div className="inspector-title-group">
          <Sliders size={17} className="text-cyan" />
          <h2 className="inspector-heading">
            {isComponent ? "器件参数与配置" : "电路拓扑概览"}
          </h2>
        </div>
        {selectedComponentId && (
          <button
            className="inspector-close-btn"
            onClick={() => selectComponent(null)}
            title="关闭检查器"
          >
            <X size={15} />
          </button>
        )}
      </div>

      <div className="inspector-scroll-body">
        {isComponent && compData && def ? (
          /* Component Inspector View */
          <div className="component-inspector-pane">
            {/* Header Card */}
            <div className="component-summary-card">
              <div className="component-summary-symbol">
                {SYMBOLS[compData.definitionId] ? (
                  React.createElement(SYMBOLS[compData.definitionId])
                ) : (
                  <Cpu size={24} />
                )}
              </div>
              <div className="component-summary-info">
                <span className="component-family-tag">{def.family}</span>
                <h3 className="component-label-title">{compData.label}</h3>
                <span className="component-def-id">{compData.definitionId}</span>
              </div>
            </div>

            {/* Quick Actions */}
            <div className="inspector-action-row">
              <button className="inspector-btn" onClick={rotateSelected} title="顺时针旋转 (R)">
                <RotateCw size={14} /> 旋转 90°
              </button>
              <button className="inspector-btn" onClick={duplicateSelected} title="克隆元件 (Ctrl+D)">
                <Copy size={14} /> 复制
              </button>
              <button
                className="inspector-btn inspector-btn--danger"
                onClick={removeSelected}
                title="删除元件 (Delete)"
              >
                <Trash2 size={14} /> 删除
              </button>
            </div>

            {/* Component Label Editing */}
            <div className="inspector-section">
              <label className="inspector-field-label">
                <span>器件标号 (Label)</span>
                <input
                  type="text"
                  value={compData.label}
                  onChange={(e) => updateLabel(compData.label, e.target.value)}
                  className="inspector-input"
                  placeholder="例如: R1, Q1, VCC"
                />
              </label>
            </div>

            {/* Model Variant Selection */}
            {def.allowedModelVariants.length > 1 && (
              <div className="inspector-section">
                <label className="inspector-field-label">
                  <span>电气模型变体 (Model Variant)</span>
                  <select
                    value={compData.modelVariant}
                    onChange={(e) => updateModelVariant(compData.label, e.target.value)}
                    className="inspector-select"
                  >
                    {def.allowedModelVariants.map((variant) => (
                      <option key={variant} value={variant}>
                        {variant}
                      </option>
                    ))}
                  </select>
                </label>
              </div>
            )}

            {/* Parameters Form */}
            {def.parameters.length > 0 && (
              <div className="inspector-section">
                <h4 className="inspector-section-heading">物理参数设定</h4>
                <div className="inspector-params-list">
                  {def.parameters.map((paramDef) => {
                    const currentVal = compData.parameters[paramDef.id]?.value ?? "";
                    return (
                      <ParameterFieldItem
                        key={paramDef.id}
                        paramDef={paramDef}
                        currentVal={currentVal}
                        onChange={(val) => updateParameter(compData.label, paramDef.id, val)}
                      />
                    );
                  })}
                </div>
              </div>
            )}

            {/* Port Connectivity */}
            <div className="inspector-section">
              <h4 className="inspector-section-heading">引脚连接状态</h4>
              <div className="ports-status-list">
                {def.ports.map((port) => {
                  const isConnected = connectedPorts.has(port.id);
                  return (
                    <div key={port.id} className="port-status-row">
                      <span className="port-name">{port.label} ({port.id})</span>
                      <span
                        className={`port-badge ${isConnected ? "port-badge--connected" : "port-badge--dangling"}`}
                      >
                        {isConnected ? "已连接" : "悬空"}
                      </span>
                    </div>
                  );
                })}
              </div>
            </div>
          </div>
        ) : (
          /* Empty / Circuit Overview View */
          <div className="circuit-overview-pane">
            {/* Quick Stats Grid */}
            <div className="stats-metric-grid">
              <div className="metric-box">
                <span className="metric-val">{componentCount}</span>
                <span className="metric-lbl">元件总数</span>
              </div>
              <div className="metric-box">
                <span className="metric-val">{junctionCount}</span>
                <span className="metric-lbl">连接节点</span>
              </div>
              <div className="metric-box">
                <span className="metric-val">{wireCount}</span>
                <span className="metric-lbl">布线支路</span>
              </div>
            </div>

            {/* Reference Ground Check */}
            <div
              className={`ground-status-card ${hasGround ? "ground-status-card--ok" : "ground-status-card--warn"}`}
            >
              {hasGround ? (
                <>
                  <CheckCircle2 size={16} className="text-emerald" />
                  <div>
                    <strong>已包含参考地 (GND)</strong>
                    <p>系统已确立零电位参考节点，符合分析条件</p>
                  </div>
                </>
              ) : (
                <>
                  <AlertTriangle size={16} className="text-amber" />
                  <div>
                    <strong>缺少参考地 (GND)</strong>
                    <p>请从元件库添加「参考地」以确定节点电压基准</p>
                  </div>
                </>
              )}
            </div>

            {/* Pre-flight Validation Issues List */}
            <div className="inspector-section">
              <div className="section-title-with-action">
                <h4 className="inspector-section-heading">电气规则检查</h4>
                <button className="btn-link" onClick={() => validate()}>
                  刷新检查
                </button>
              </div>

              {validationIssues.length === 0 ? (
                <div className="validation-empty-card">
                  <CheckCircle2 size={18} className="text-emerald" />
                  <span>电路拓扑完备，无校验错误</span>
                </div>
              ) : (
                <div className="validation-issues-stack">
                  {validationIssues.map((issue, idx) => (
                    <div
                      key={idx}
                      className={`validation-issue-item validation-issue-item--${issue.severity}`}
                    >
                      <AlertTriangle size={14} className="issue-icon" />
                      <div className="issue-body">
                        <span className="issue-msg">{issue.message}</span>
                        {issue.code && <span className="issue-code">{issue.code}</span>}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>

            {/* Tips Card */}
            <div className="inspector-tips-card">
              <div className="tips-card-header">
                <HelpCircle size={14} className="text-cyan" />
                <span>交互小贴士</span>
              </div>
              <ul className="tips-list">
                <li>单击画布元件可直接打开此面板修改数值</li>
                <li>从引脚端点拖出导线到另一个引脚即可自动连线</li>
                <li>点击工具栏「经典题库」可一键调取模电经典题目</li>
                <li>点击「手机直传」支持手机拍照扫码直接导入</li>
              </ul>
            </div>
          </div>
        )}
      </div>
    </aside>
  );
}
