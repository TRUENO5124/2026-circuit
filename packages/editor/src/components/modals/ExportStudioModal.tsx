import React, { useState } from "react";
import { useEditorStore } from "../../store/editorStore";
import {
  exportAnalysisReport,
  exportLatexReport,
  exportCircuitikzSnippet,
  exportCircuitJson,
  generateLatexDocument,
  generateCircuitikzCode,
} from "../../lib/exportReport";
import { Download, Copy, Check, FileText, Code2, Cpu, FileJson, X } from "lucide-react";

type ExportTab = "html" | "latex" | "circuitikz" | "json";

export default function ExportStudioModal() {
  const {
    exportStudioOpen,
    setExportStudioOpen,
    analysisResult,
    circuitTitle,
    exportCircuitGraph,
    addToast,
  } = useEditorStore();

  const [activeTab, setActiveTab] = useState<ExportTab>("html");
  const [copied, setCopied] = useState(false);

  if (!exportStudioOpen) return null;

  const circuitGraph = exportCircuitGraph();

  const handleCopy = (text: string, label: string) => {
    navigator.clipboard.writeText(text);
    setCopied(true);
    addToast({
      type: "success",
      title: `已复制 ${label}`,
      duration: 2000,
    });
    setTimeout(() => setCopied(false), 2000);
  };

  const latexCode = analysisResult
    ? generateLatexDocument(analysisResult, circuitTitle, circuitGraph)
    : "% 请先在工作台运行电路分析以生成推导步骤与方程\n\\documentclass{article}\n\\begin{document}\n% No analysis result yet\n\\end{document}";

  const circuitikzCode = generateCircuitikzCode(circuitGraph);

  const jsonCode = JSON.stringify(circuitGraph, null, 2);

  return (
    <div className="modal-backdrop" onClick={() => setExportStudioOpen(false)}>
      <div className="modal-dialog modal-dialog--large" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="modal-title-group">
            <div className="modal-icon-badge">
              <Download size={20} className="text-cyan" />
            </div>
            <div>
              <h2 className="modal-title">导出与学术报告中心</h2>
              <p className="modal-subtitle">支持导出交互式 HTML、LaTeX 学术报告、Circuitikz 矢量电路源码与电路 JSON</p>
            </div>
          </div>
          <button className="modal-close-btn" onClick={() => setExportStudioOpen(false)}>
            <X size={18} />
          </button>
        </div>

        <div className="export-tabs-bar">
          <button
            className={`export-tab-btn ${activeTab === "html" ? "export-tab-btn--active" : ""}`}
            onClick={() => setActiveTab("html")}
          >
            <FileText size={16} /> 网页分析报告 (.html)
          </button>
          <button
            className={`export-tab-btn ${activeTab === "latex" ? "export-tab-btn--active" : ""}`}
            onClick={() => setActiveTab("latex")}
          >
            <Code2 size={16} /> LaTeX 论文源码 (.tex)
          </button>
          <button
            className={`export-tab-btn ${activeTab === "circuitikz" ? "export-tab-btn--active" : ""}`}
            onClick={() => setActiveTab("circuitikz")}
          >
            <Cpu size={16} /> Circuitikz 矢量电路图
          </button>
          <button
            className={`export-tab-btn ${activeTab === "json" ? "export-tab-btn--active" : ""}`}
            onClick={() => setActiveTab("json")}
          >
            <FileJson size={16} /> CircuitGraph (.json)
          </button>
        </div>

        <div className="modal-body">
          {activeTab === "html" && (
            <div className="export-pane-box">
              <div className="export-pane-desc">
                <h4>📄 交互式独立 HTML 报告</h4>
                <p>
                  生成包含完整数学公式渲染 (KaTeX CSS 内置)、解题步骤、直流工作点及阻抗分析的独立 HTML 文件。无需网络连接，支持一键浏览器打印为高质量 PDF。
                </p>
                {!analysisResult && (
                  <div className="export-warn-notice">
                    ⚠ 当前尚未运行电路分析，导出文件仅包含空结果结构。建议先点击「运行分析」。
                  </div>
                )}
              </div>
              <div className="export-pane-actions">
                <button
                  className="btn btn-primary"
                  onClick={() => {
                    if (analysisResult) {
                      exportAnalysisReport(analysisResult, circuitTitle);
                      addToast({ type: "success", title: "HTML 报告已开始下载" });
                    } else {
                      addToast({ type: "warning", title: "请先运行电路分析" });
                    }
                  }}
                >
                  <Download size={16} /> 下载完整 HTML 报告
                </button>
              </div>
            </div>
          )}

          {activeTab === "latex" && (
            <div className="export-code-container">
              <div className="export-code-toolbar">
                <span className="code-lang-label">LaTeX (CTeX + amsmath + circuitikz)</span>
                <div className="code-toolbar-actions">
                  <button
                    className="btn btn-sm btn-outline"
                    onClick={() => handleCopy(latexCode, "LaTeX 源码")}
                  >
                    {copied ? <Check size={14} className="text-emerald" /> : <Copy size={14} />} 复制代码
                  </button>
                  <button
                    className="btn btn-sm btn-primary"
                    onClick={() => {
                      if (analysisResult) {
                        exportLatexReport(analysisResult, circuitTitle, circuitGraph);
                        addToast({ type: "success", title: "LaTeX 源码已下载" });
                      } else {
                        addToast({ type: "warning", title: "请先运行电路分析" });
                      }
                    }}
                  >
                    <Download size={14} /> 下载 .tex 文件
                  </button>
                </div>
              </div>
              <pre className="code-preview-block">
                <code>{latexCode}</code>
              </pre>
            </div>
          )}

          {activeTab === "circuitikz" && (
            <div className="export-code-container">
              <div className="export-code-toolbar">
                <span className="code-lang-label">Circuitikz TikZ 代码</span>
                <div className="code-toolbar-actions">
                  <button
                    className="btn btn-sm btn-outline"
                    onClick={() => handleCopy(circuitikzCode, "Circuitikz 代码")}
                  >
                    {copied ? <Check size={14} className="text-emerald" /> : <Copy size={14} />} 复制代码
                  </button>
                  <button
                    className="btn btn-sm btn-primary"
                    onClick={() => {
                      exportCircuitikzSnippet(circuitGraph);
                      addToast({ type: "success", title: "Circuitikz 代码已下载" });
                    }}
                  >
                    <Download size={14} /> 下载 .tex 片段
                  </button>
                </div>
              </div>
              <pre className="code-preview-block">
                <code>{circuitikzCode}</code>
              </pre>
            </div>
          )}

          {activeTab === "json" && (
            <div className="export-code-container">
              <div className="export-code-toolbar">
                <span className="code-lang-label">CircuitGraph JSON (符合 Zod 规范)</span>
                <div className="code-toolbar-actions">
                  <button
                    className="btn btn-sm btn-outline"
                    onClick={() => handleCopy(jsonCode, "JSON 数据")}
                  >
                    {copied ? <Check size={14} className="text-emerald" /> : <Copy size={14} />} 复制代码
                  </button>
                  <button
                    className="btn btn-sm btn-primary"
                    onClick={() => {
                      exportCircuitJson(circuitGraph);
                      addToast({ type: "success", title: "JSON 文件已下载" });
                    }}
                  >
                    <Download size={14} /> 下载 .json 文件
                  </button>
                </div>
              </div>
              <pre className="code-preview-block">
                <code>{jsonCode}</code>
              </pre>
            </div>
          )}
        </div>

        <div className="modal-footer">
          <div className="modal-footer-hint">
            <span>支持与 Overleaf、TeX Live、VS Code 等学术写作工具无缝集成</span>
          </div>
          <button className="btn btn-outline" onClick={() => setExportStudioOpen(false)}>
            关闭
          </button>
        </div>
      </div>
    </div>
  );
}
