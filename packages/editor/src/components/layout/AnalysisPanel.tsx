/**
 * Dedicated educational analysis and solving studio.
 *
 * Provides:
 *   1. Step-by-step interpretable solution walkthrough with physical reasoning
 *   2. DC Operating Point (Q-Point) dashboard & transistor operating region check
 *   3. AC small-signal parameters (Av, Rin, Rout, gm, r_pi)
 *   4. Equivalent circuit analysis & key equations
 */

import React, { useState } from "react";
import { useEditorStore } from "../../store/editorStore";
import type { AnalysisResult, AnalysisStep } from "../../types/editor";
import KatexRenderer from "../common/KatexRenderer";
import {
  Sparkles,
  BookOpen,
  Zap,
  TrendingUp,
  Activity,
  CheckCircle,
  AlertCircle,
  ChevronDown,
  ChevronRight,
  Copy,
  Download,
  Share2,
  RefreshCw,
  ExternalLink,
} from "lucide-react";

type AnalysisSubTab = "steps" | "op" | "ac" | "equivalent" | "equations";

const PROBLEM_TYPE_LABELS: Record<string, string> = {
  bjt_dc_bias: "BJT 直流偏置分析",
  bjt_ac_small_signal: "BJT 交流小信号分析",
  mos_dc_bias: "MOSFET 直流偏置分析",
  mos_ac_small_signal: "MOSFET 交流小信号分析",
  diode_analysis: "二极管电路分析",
  linear_frequency_analysis: "一阶 RC/RL 滤波与频率特性分析",
  linear_dc_analysis: "线性直流网络分析",
  unsupported: "通用电路拓扑分析",
};

export default function AnalysisPanel() {
  const {
    analysisStatus,
    analysisResult,
    analysisError,
    circuitTitle,
    setExportStudioOpen,
    setViewMode,
  } = useEditorStore();

  const [activeSubTab, setActiveSubTab] = useState<AnalysisSubTab>("steps");
  const [expandedSteps, setExpandedSteps] = useState<Record<number, boolean>>({});

  const toggleStep = (idx: number) => {
    setExpandedSteps((prev) => ({ ...prev, [idx]: !prev[idx] }));
  };

  // ── 1. Idle State ────────────────────────────────────────────────
  if (analysisStatus === "idle") {
    return (
      <div className="analysis-empty-container">
        <div className="analysis-empty-card">
          <div className="analysis-empty-icon">
            <Zap size={32} className="text-cyan" />
          </div>
          <h3>尚未运行电路分析求解</h3>
          <p>
            在工具栏中点击「▶ 运行分析」或在「经典题库」中选择一道经典模电题目，系统将为您生成详尽的分步骤推导过程与 LaTeX 报告。
          </p>
        </div>
      </div>
    );
  }

  // ── 2. Running State ─────────────────────────────────────────────
  if (analysisStatus === "submitting" || analysisStatus === "polling") {
    return (
      <div className="analysis-loading-container">
        <div className="analysis-loading-card">
          <RefreshCw className="animate-spin text-cyan" size={40} />
          <h3>
            {analysisStatus === "submitting" ? "正在提交电路拓扑任务..." : "正在调用符号计算引擎求解..."}
          </h3>
          <p>正在分析节点电压方程、构建 MNA 导纳矩阵并推导关键公式...</p>
        </div>
      </div>
    );
  }

  // ── 3. Failed State ──────────────────────────────────────────────
  if (analysisStatus === "failed" || (analysisResult && analysisResult.error)) {
    const errorMsg = analysisError ?? analysisResult?.error ?? "电路求解异常";
    return (
      <div className="analysis-failed-container">
        <div className="analysis-failed-card">
          <AlertCircle size={36} className="text-danger" />
          <h3>求解过程未能成功完成</h3>
          <div className="error-message-box">{errorMsg}</div>
          <p>建议检查电路拓扑是否包含悬空端口，或从「经典题库」加载标准拓扑进行验证。</p>
        </div>
      </div>
    );
  }

  // ── 4. Completed State ───────────────────────────────────────────
  if (!analysisResult) return null;

  const result = analysisResult;
  const typeLabel = PROBLEM_TYPE_LABELS[result.problemType] ?? result.problemType;

  const hasOp = !!result.operatingPoint;
  const hasAc =
    !!(
      result.gainExpressions?.voltageGain ||
      result.gainExpressions?.currentGain ||
      result.gainExpressions?.transconductance
    ) ||
    !!(
      result.impedanceExpressions?.inputResistance ||
      result.impedanceExpressions?.outputResistance
    );

  return (
    <div className="analysis-studio-wrapper">
      {/* Top Header Card */}
      <div className="analysis-studio-header">
        <div className="studio-header-main">
          <div className="studio-badge-row">
            <span className="studio-type-pill">{typeLabel}</span>
            <span className="studio-mode-pill">
              {result.isSymbolic ? "✓ 符号解析推导" : "数值求解"}
            </span>
            {result.teachingTags.map((tag) => (
              <span key={tag} className="studio-tag-pill">
                #{tag}
              </span>
            ))}
          </div>
          <h2 className="studio-circuit-title">{circuitTitle}</h2>
        </div>

        <div className="studio-header-actions">
          <button className="btn btn-outline" onClick={() => setExportStudioOpen(true)}>
            <Download size={14} /> 导出报告
          </button>
          <button className="btn btn-primary" onClick={() => setViewMode("report")}>
            <ExternalLink size={14} /> 全屏阅读报告
          </button>
        </div>
      </div>

      {/* Sub Navigation Tabs */}
      <div className="analysis-subtabs-bar">
        <button
          className={`analysis-subtab-btn ${activeSubTab === "steps" ? "analysis-subtab-btn--active" : ""}`}
          onClick={() => setActiveSubTab("steps")}
        >
          <BookOpen size={15} /> 分步骤详细推导 ({result.steps.length})
        </button>

        {hasOp && (
          <button
            className={`analysis-subtab-btn ${activeSubTab === "op" ? "analysis-subtab-btn--active" : ""}`}
            onClick={() => setActiveSubTab("op")}
          >
            <Activity size={15} /> 直流工作点 (Q-Point)
          </button>
        )}

        {hasAc && (
          <button
            className={`analysis-subtab-btn ${activeSubTab === "ac" ? "analysis-subtab-btn--active" : ""}`}
            onClick={() => setActiveSubTab("ac")}
          >
            <TrendingUp size={15} />{" "}
            {result.problemType === "linear_frequency_analysis"
              ? "频率特性与传输函数 (Au)"
              : "交流动态指标 (Av, Rin, Rout)"}
          </button>
        )}

        <button
          className={`analysis-subtab-btn ${activeSubTab === "equivalent" ? "analysis-subtab-btn--active" : ""}`}
          onClick={() => setActiveSubTab("equivalent")}
        >
          <Zap size={15} /> 等效电路模型分析
        </button>

        {result.equations.length > 0 && (
          <button
            className={`analysis-subtab-btn ${activeSubTab === "equations" ? "analysis-subtab-btn--active" : ""}`}
            onClick={() => setActiveSubTab("equations")}
          >
            📐 关键方程汇总 ({result.equations.length})
          </button>
        )}
      </div>

      {/* Main Tab Content */}
      <div className="analysis-studio-content">
        {/* SubTab 1: Steps */}
        {activeSubTab === "steps" && (
          <div className="steps-container">
            {result.steps.map((step, idx) => {
              const isExpanded = expandedSteps[idx] !== false; // expanded by default

              return (
                <div key={idx} className="step-card">
                  <div className="step-card-header" onClick={() => toggleStep(idx)}>
                    <div className="step-badge-title">
                      <span className="step-index-circle">{idx + 1}</span>
                      <h4 className="step-description">{step.description}</h4>
                    </div>
                    <button className="step-toggle-btn">
                      {isExpanded ? <ChevronDown size={18} /> : <ChevronRight size={18} />}
                    </button>
                  </div>

                  {isExpanded && (
                    <div className="step-card-body">
                      {/* Physical reasoning box */}
                      {step.reasoning && (
                        <div className="step-reasoning-callout">
                          <span className="reasoning-label">💡 物理推导依据：</span>
                          <span className="reasoning-text">{step.reasoning}</span>
                        </div>
                      )}

                      {/* Formula */}
                      {step.formula && (
                        <div className="step-formula-box">
                          <KatexRenderer latex={step.formula} displayMode={true} copyable={true} />
                        </div>
                      )}
                    </div>
                  )}
                </div>
              );
            })}
          </div>
        )}

        {/* SubTab 2: Operating Point */}
        {activeSubTab === "op" && result.operatingPoint && (
          <div className="operating-point-pane">
            {/* Transistor Operating Region Banner */}
            {Object.keys(result.operatingPoint.regions).length > 0 && (
              <div className="region-dashboard-card">
                <div className="region-card-header">
                  <CheckCircle size={20} className="text-emerald" />
                  <h4>半导体器件工作区状态判定</h4>
                </div>
                <div className="regions-grid">
                  {Object.entries(result.operatingPoint.regions).map(([dev, region]) => (
                    <div key={dev} className="region-item-chip">
                      <span className="region-dev-name">{dev}</span>
                      <span className="region-state-val">
                        {region === "active" ? "🟢 正向放大区 (Active)" : region}
                      </span>
                    </div>
                  ))}
                </div>
                <p className="region-hint">
                  经 KVL/KCL 联立方程求解，发射结正偏 (Vbe ≈ 0.7V)，集电结反偏 (Vce &gt; 0.3V)，放大工作区条件充分成立。
                </p>
              </div>
            )}

            {/* Tables for Node Voltages and Branch Currents */}
            <div className="op-tables-grid">
              <div className="op-table-card">
                <h4>⚡ 各节点静态直流电位 (V)</h4>
                <table className="op-table">
                  <thead>
                    <tr>
                      <th>节点名称</th>
                      <th>表达式 / 计算值</th>
                    </tr>
                  </thead>
                  <tbody>
                    {Object.entries(result.operatingPoint.nodeVoltages).map(([nodeId, val]) => (
                      <tr key={nodeId}>
                        <td>
                          <code>{nodeId}</code>
                        </td>
                        <td>
                          <KatexRenderer latex={val} copyable={true} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="op-table-card">
                <h4>⚡ 各支路直流偏置电流 (I)</h4>
                <table className="op-table">
                  <thead>
                    <tr>
                      <th>支路名称</th>
                      <th>表达式 / 计算值</th>
                    </tr>
                  </thead>
                  <tbody>
                    {Object.entries(result.operatingPoint.branchCurrents).map(([branchId, val]) => (
                      <tr key={branchId}>
                        <td>
                          <code>{branchId}</code>
                        </td>
                        <td>
                          <KatexRenderer latex={val} copyable={true} />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        )}

        {/* SubTab 3: AC Parameters */}
        {activeSubTab === "ac" && (
          <div className="ac-parameters-pane">
            <div className="ac-cards-grid">
              {result.gainExpressions?.voltageGain && (
                <div className="ac-metric-card">
                  <span className="ac-metric-title">
                    {result.problemType === "linear_frequency_analysis"
                      ? "电压传输函数 A_u(jω)"
                      : "中频电压放大倍数 A_v"}
                  </span>
                  <div className="ac-metric-formula">
                    <KatexRenderer
                      latex={
                        result.gainExpressions.voltageGain.startsWith("A_") ||
                        result.gainExpressions.voltageGain.startsWith("H(")
                          ? result.gainExpressions.voltageGain
                          : `A_v = ${result.gainExpressions.voltageGain}`
                      }
                      displayMode={true}
                      copyable={true}
                    />
                  </div>
                  <span className="ac-metric-note">
                    {result.problemType === "linear_frequency_analysis"
                      ? "输出相量与输入正弦激励相量之比的复频域函数（标准作图形式）"
                      : "输出电压与输入交流微变信号的幅值比"}
                  </span>
                </div>
              )}

              {result.impedanceExpressions?.inputResistance && (
                <div className="ac-metric-card">
                  <span className="ac-metric-title">输入电阻 R_in</span>
                  <div className="ac-metric-formula">
                    <KatexRenderer
                      latex={`R_{in} = ${result.impedanceExpressions.inputResistance}`}
                      displayMode={true}
                      copyable={true}
                    />
                  </div>
                  <span className="ac-metric-note">信号源视入放大电路的等效交流电阻</span>
                </div>
              )}

              {result.impedanceExpressions?.outputResistance && (
                <div className="ac-metric-card">
                  <span className="ac-metric-title">输出电阻 R_out</span>
                  <div className="ac-metric-formula">
                    <KatexRenderer
                      latex={`R_{out} = ${result.impedanceExpressions.outputResistance}`}
                      displayMode={true}
                      copyable={true}
                    />
                  </div>
                  <span className="ac-metric-note">负载端视入放大电路的戴维南等效内阻</span>
                </div>
              )}

              {result.gainExpressions?.transconductance && (
                <div className="ac-metric-card">
                  <span className="ac-metric-title">动态跨导 G_m</span>
                  <div className="ac-metric-formula">
                    <KatexRenderer
                      latex={`G_m = ${result.gainExpressions.transconductance}`}
                      displayMode={true}
                      copyable={true}
                    />
                  </div>
                  <span className="ac-metric-note">管子栅极/基极电压对漏极/集电极电流的控制能力</span>
                </div>
              )}
            </div>
          </div>
        )}

        {/* SubTab 4: Equivalent Model Analysis */}
        {activeSubTab === "equivalent" && (
          <div className="equivalent-analysis-pane">
            <div className="equivalent-concept-card">
              <h4>🔄 直流等效电路与交流小信号等效模型原则</h4>
              <div className="principles-grid">
                <div className="principle-box">
                  <h5>直流等效电路原则 (DC Equivalent)</h5>
                  <ul>
                    <li>电容器视作开路 (阻抗无穷大，隔离直流偏置)</li>
                    <li>电感器视作短路 (直流内阻趋近于零)</li>
                    <li>交流小信号独立源置零 (电压源短路，电流源开路)</li>
                    <li>保留所有直流偏置电源与三极管/MOS管大信号静态特性</li>
                  </ul>
                </div>
                <div className="principle-box">
                  <h5>交流微变等效原则 (AC Small-Signal)</h5>
                  <ul>
                    <li>耦合电容与旁路电容容抗极小，视作交流短路</li>
                    <li>{"恒压直流电源 V_CC / V_DD 交流内阻为零，对交流地短路"}</li>
                    <li>{"三极管用 r_π 与受控电流源 β·i_b 微变模型置换"}</li>
                    <li>{"场效应管用输入开路与受控电流源 g_m·v_gs 微变模型置换"}</li>
                  </ul>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* SubTab 5: Key Equations */}
        {activeSubTab === "equations" && (
          <div className="equations-grid-pane">
            {result.equations.map((eq) => (
              <div key={eq.id} className="equation-card-item">
                <div className="equation-card-id">{eq.id}</div>
                <div className="equation-card-body">
                  <KatexRenderer latex={eq.latex} displayMode={true} copyable={true} />
                </div>
                {eq.variables.length > 0 && (
                  <div className="equation-card-vars">
                    <span>所含参量：</span>
                    {eq.variables.map((v) => (
                      <span key={v} className="var-chip">
                        {v}
                      </span>
                    ))}
                  </div>
                )}
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
