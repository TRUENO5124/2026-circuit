/**
 * Fullscreen academic analysis report view.
 *
 * Provides a clean, printable document view directly in the application.
 */

import React from "react";
import { useEditorStore } from "../../store/editorStore";
import KatexRenderer from "../common/KatexRenderer";
import {
  Printer,
  Download,
  ArrowLeft,
  FileText,
  CheckCircle2,
  AlertTriangle,
  Code2,
} from "lucide-react";
import { exportAnalysisReport, exportLatexReport } from "../../lib/exportReport";

export default function ReportView() {
  const {
    analysisResult,
    circuitTitle,
    setViewMode,
    setExportStudioOpen,
    exportCircuitGraph,
    addToast,
  } = useEditorStore();

  if (!analysisResult) {
    return (
      <div className="report-view-empty">
        <FileText size={48} className="text-muted" />
        <h3>暂无分析报告数据</h3>
        <p>请先在原理图编辑器中设计电路并点击「运行分析」。</p>
        <button className="btn btn-primary" onClick={() => setViewMode("editor")}>
          <ArrowLeft size={16} /> 返回编辑器
        </button>
      </div>
    );
  }

  const result = analysisResult;
  const circuitGraph = exportCircuitGraph();

  return (
    <div className="report-view-page">
      {/* Top Floating Control Bar */}
      <div className="report-floating-bar">
        <button className="btn btn-outline" onClick={() => setViewMode("editor")}>
          <ArrowLeft size={16} /> 返回原理图
        </button>

        <div className="report-bar-right">
          <button className="btn btn-outline" onClick={() => window.print()}>
            <Printer size={16} /> 打印 / 保存 PDF
          </button>
          <button
            className="btn btn-outline"
            onClick={() => {
              exportLatexReport(result, circuitTitle, circuitGraph);
              addToast({ type: "success", title: "已导出 LaTeX 论文源码" });
            }}
          >
            <Code2 size={16} /> 导出 LaTeX
          </button>
          <button
            className="btn btn-primary"
            onClick={() => {
              exportAnalysisReport(result, circuitTitle);
              addToast({ type: "success", title: "已导出 HTML 报告" });
            }}
          >
            <Download size={16} /> 导出 HTML
          </button>
        </div>
      </div>

      {/* Printable Report Document Paper */}
      <div className="report-paper-sheet">
        <header className="report-paper-header">
          <div className="report-paper-badge">AnalogFlow 电路分析报告</div>
          <h1 className="report-paper-title">{circuitTitle}</h1>
          <div className="report-paper-meta">
            <span>题目类型: <strong>{result.problemType}</strong></span>
            <span>·</span>
            <span>求解模式: {result.isSymbolic ? "符号推导" : "数值计算"}</span>
            <span>·</span>
            <span>日期: {new Date().toLocaleDateString("zh-CN")}</span>
          </div>

          <div className="report-paper-tags">
            {result.teachingTags.map((tag) => (
              <span key={tag} className="report-tag-chip">
                #{tag}
              </span>
            ))}
          </div>
        </header>

        <hr className="report-divider" />

        {/* Section: Solution Steps */}
        <section className="report-paper-section">
          <h2 className="report-section-heading">一、分步骤解题推导与物理依据</h2>
          <div className="report-steps-stack">
            {result.steps.map((step, idx) => (
              <div key={idx} className="report-step-item">
                <div className="report-step-title-line">
                  <span className="step-seq">{idx + 1}</span>
                  <strong>{step.description}</strong>
                </div>

                {step.reasoning && (
                  <p className="report-step-reasoning">
                    <strong>依据与原理：</strong>{step.reasoning}
                  </p>
                )}

                {step.formula && (
                  <div className="report-formula-box">
                    <KatexRenderer latex={step.formula} displayMode={true} />
                  </div>
                )}
              </div>
            ))}
          </div>
        </section>

        {/* Section: Operating Point */}
        {result.operatingPoint && (
          <section className="report-paper-section">
            <h2 className="report-section-heading">二、直流静态工作点 (Q-Point) 计算结果</h2>

            {Object.keys(result.operatingPoint.regions).length > 0 && (
              <div className="report-region-box">
                <strong>半导体器件工作区状态：</strong>
                {Object.entries(result.operatingPoint.regions).map(([dev, r]) => (
                  <span key={dev} className="report-region-pill">
                    {dev}: {r === "active" ? "正向放大区 (Active)" : r}
                  </span>
                ))}
              </div>
            )}

            <div className="report-tables-row">
              <div className="report-subtable">
                <h3>节点电压 (V)</h3>
                <table className="report-data-table">
                  <thead>
                    <tr>
                      <th>节点</th>
                      <th>表达式</th>
                    </tr>
                  </thead>
                  <tbody>
                    {Object.entries(result.operatingPoint.nodeVoltages).map(([k, v]) => (
                      <tr key={k}>
                        <td>{k}</td>
                        <td><KatexRenderer latex={v} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>

              <div className="report-subtable">
                <h3>支路电流 (I)</h3>
                <table className="report-data-table">
                  <thead>
                    <tr>
                      <th>支路</th>
                      <th>表达式</th>
                    </tr>
                  </thead>
                  <tbody>
                    {Object.entries(result.operatingPoint.branchCurrents).map(([k, v]) => (
                      <tr key={k}>
                        <td>{k}</td>
                        <td><KatexRenderer latex={v} /></td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          </section>
        )}

        {/* Section: AC Parameters */}
        {(result.gainExpressions || result.impedanceExpressions) && (
          <section className="report-paper-section">
            <h2 className="report-section-heading">三、交流微变等效与动态性能指标</h2>
            <div className="report-ac-list">
              {result.gainExpressions?.voltageGain && (
                <div className="report-ac-row">
                  <span>电压放大倍数 A_v:</span>
                  <KatexRenderer latex={`A_v = ${result.gainExpressions.voltageGain}`} />
                </div>
              )}
              {result.impedanceExpressions?.inputResistance && (
                <div className="report-ac-row">
                  <span>输入电阻 R_in:</span>
                  <KatexRenderer latex={`R_{in} = ${result.impedanceExpressions.inputResistance}`} />
                </div>
              )}
              {result.impedanceExpressions?.outputResistance && (
                <div className="report-ac-row">
                  <span>输出电阻 R_out:</span>
                  <KatexRenderer latex={`R_{out} = ${result.impedanceExpressions.outputResistance}`} />
                </div>
              )}
            </div>
          </section>
        )}

        {/* Section: Equations */}
        {result.equations.length > 0 && (
          <section className="report-paper-section">
            <h2 className="report-section-heading">四、关键系统方程汇总</h2>
            <div className="report-equations-list">
              {result.equations.map((eq) => (
                <div key={eq.id} className="report-eq-box">
                  <div className="report-eq-id">{eq.id}</div>
                  <KatexRenderer latex={eq.latex} displayMode={true} />
                </div>
              ))}
            </div>
          </section>
        )}

        <footer className="report-paper-footer">
          <p>由面向模拟电子电路教学的原型系统 (AnalogFlow) 自动生成</p>
        </footer>
      </div>
    </div>
  );
}
