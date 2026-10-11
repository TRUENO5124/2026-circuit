/**
 * Top command bar and global workflow navigation.
 */

import React, { useEffect, useRef, useState } from "react";
import { useEditorStore } from "../../store/editorStore";
import { submitAnalysis, getTaskStatus } from "../../api/client";
import {
  Activity,
  FolderOpen,
  Download,
  RotateCw,
  Undo2,
  Redo2,
  Copy,
  Trash2,
  Play,
  CheckCircle2,
  AlertTriangle,
  BookOpen,
  Smartphone,
  FileText,
  HelpCircle,
  LayoutGrid,
  Zap,
  Edit2,
  Check,
  Sparkles,
} from "lucide-react";

export default function Toolbar() {
  const fileRef = useRef<HTMLInputElement>(null);
  const [editingTitle, setEditingTitle] = useState(false);
  const [titleDraft, setTitleDraft] = useState("");

  const {
    viewMode,
    setViewMode,
    circuitTitle,
    setCircuitTitle,
    loadCircuitGraph,
    loadBenchmark,
    exportCircuitGraph,
    validate,
    clearCanvas,
    analysisStatus,
    setAnalysisStatus,
    setAnalysisResult,
    undo,
    redo,
    duplicateSelected,
    rotateSelected,
    removeSelected,
    validationIssues,
    setShortcutsOpen,
    setBenchmarkGalleryOpen,
    setMobileCaptureOpen,
    setExportStudioOpen,
    symbolStandard,
    setSymbolStandard,
    setSymbolLibraryOpen,
    addToast,
  } = useEditorStore();

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    const reader = new FileReader();
    reader.onload = () => {
      try {
        const raw = JSON.parse(reader.result as string);
        if (raw.circuitGraph && raw.groundTruth) {
          loadBenchmark(raw);
          addToast({
            type: "success",
            title: "已载入 Benchmark 题目",
            message: raw.title,
          });
        } else if (raw.nodes && raw.components && raw.connections) {
          loadCircuitGraph(raw);
          addToast({
            type: "success",
            title: "已加载电路拓扑",
            message: raw.title ?? raw.id,
          });
        } else {
          addToast({
            type: "error",
            title: "无法识别的 JSON 格式",
            message: "文件不符合 CircuitGraph 或 BenchmarkCase 规范",
          });
        }
      } catch {
        addToast({
          type: "error",
          title: "JSON 解析错误",
          message: "请检查文件格式是否有效",
        });
      }
    };
    reader.readAsText(file);
    e.target.value = "";
  };

  const handleValidate = () => {
    const issues = validate();
    const errCount = issues.filter((i) => i.severity === "error").length;
    const warnCount = issues.filter((i) => i.severity === "warning").length;

    if (issues.length === 0) {
      addToast({
        type: "success",
        title: "电路规则检查通过",
        message: "未发现悬空引脚或非法参数，可直接运行分析求解",
      });
    } else {
      addToast({
        type: errCount > 0 ? "error" : "warning",
        title: `检查发现 ${errCount} 错误，${warnCount} 警告`,
        message: "请在右侧属性面板查看详细修复建议",
      });
    }
  };

  const handleAnalyze = async () => {
    const graph = exportCircuitGraph();
    const errors = validate().filter((i) => i.severity === "error");

    if (errors.length > 0) {
      const proceed = confirm(
        `电路拓扑检查发现 ${errors.length} 处错误（如悬空引脚或缺少基准参考点）。仍然尝试求解吗？`,
      );
      if (!proceed) return;
    }

    setAnalysisStatus("submitting");
    setViewMode("analysis");
    addToast({
      type: "info",
      title: "分析任务提交中",
      message: "正在调用 Python 符号分析引擎...",
    });

    try {
      const { id: taskId } = await submitAnalysis(graph);
      setAnalysisStatus("polling");

      const poll = setInterval(async () => {
        try {
          const data = await getTaskStatus(taskId);
          if (data.status === "completed") {
            clearInterval(poll);
            if (data.result) {
              setAnalysisStatus("completed");
              setAnalysisResult(data.result, null);
              addToast({
                type: "success",
                title: "分析求解完成！",
                message: `成功推导 ${data.result.steps?.length ?? 0} 个解题步骤`,
              });
            } else {
              setAnalysisStatus("failed");
              setAnalysisResult(null, "分析完成但未生成有效解题报告");
            }
          } else if (data.status === "failed") {
            clearInterval(poll);
            setAnalysisStatus("failed");
            setAnalysisResult(null, data.error ?? "求解引擎执行异常");
            addToast({
              type: "error",
              title: "分析失败",
              message: data.error ?? "未知错误",
            });
          }
        } catch {
          // keep polling
        }
      }, 1500);
    } catch (error) {
      setAnalysisStatus("failed");
      setAnalysisResult(null, String(error));
      addToast({
        type: "error",
        title: "分析提交失败",
        message: String(error),
      });
    }
  };

  const isAnalyzing = analysisStatus === "submitting" || analysisStatus === "polling";
  const errCount = validationIssues.filter((i) => i.severity === "error").length;

  return (
    <header className="app-toolbar">
      {/* Brand & Title */}
      <div className="toolbar-brand-section">
        <div className="brand-logo-container">
          <div className="brand-pulse-badge">
            <Activity size={18} className="text-white" />
          </div>
          <div className="brand-texts">
            <span className="brand-title">AnalogFlow</span>
            <span className="brand-subtitle">电路分析学习工作台</span>
          </div>
        </div>

        <div className="toolbar-vertical-divider" />

        <div className="circuit-title-widget">
          {editingTitle ? (
            <div className="circuit-title-edit-box">
              <input
                type="text"
                value={titleDraft}
                onChange={(e) => setTitleDraft(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === "Enter") {
                    setCircuitTitle(titleDraft || "未命名电路");
                    setEditingTitle(false);
                  } else if (e.key === "Escape") {
                    setEditingTitle(false);
                  }
                }}
                autoFocus
                className="circuit-title-input"
              />
              <button
                className="btn-icon-subtle"
                onClick={() => {
                  setCircuitTitle(titleDraft || "未命名电路");
                  setEditingTitle(false);
                }}
              >
                <Check size={14} />
              </button>
            </div>
          ) : (
            <div
              className="circuit-title-display"
              onClick={() => {
                setTitleDraft(circuitTitle);
                setEditingTitle(true);
              }}
              title="点击重命名电路"
            >
              <span className="circuit-title-text">{circuitTitle}</span>
              <Edit2 size={12} className="circuit-title-icon" />
            </div>
          )}
        </div>
      </div>

      {/* Mode View Switcher */}
      <div className="toolbar-center-modes">
        <div className="mode-segmented-control">
          <button
            className={`mode-segment-btn ${viewMode === "editor" ? "mode-segment-btn--active" : ""}`}
            onClick={() => setViewMode("editor")}
          >
            <LayoutGrid size={15} /> 原理图设计
          </button>
          <button
            className={`mode-segment-btn ${viewMode === "analysis" ? "mode-segment-btn--active" : ""}`}
            onClick={() => setViewMode("analysis")}
          >
            <Zap size={15} /> 智能求解推导
            {analysisStatus === "completed" && <span className="mode-dot-success" />}
            {isAnalyzing && <span className="mode-dot-running" />}
          </button>
          <button
            className={`mode-segment-btn ${viewMode === "report" ? "mode-segment-btn--active" : ""}`}
            onClick={() => setViewMode("report")}
          >
            <FileText size={15} /> 完整解题报告
          </button>
        </div>
      </div>

      {/* Action Buttons */}
      <div className="toolbar-actions-section">
        {/* Symbol Standard & Library */}
        <div className="toolbar-btn-group">
          <div className="mode-segmented-control" title="切换原理图符号规范 (IEC 国标 / ANSI 美标)">
            <button
              className={`mode-segment-btn ${symbolStandard === "iec" ? "mode-segment-btn--active" : ""}`}
              onClick={() => {
                setSymbolStandard("iec");
                addToast({
                  type: "info",
                  title: "符号规范已切换",
                  message: "已采用 IEC 60617 / GB/T 4728 (国标方块电阻、弧形电感)",
                  duration: 2200,
                });
              }}
            >
              IEC (国标)
            </button>
            <button
              className={`mode-segment-btn ${symbolStandard === "ansi" ? "mode-segment-btn--active" : ""}`}
              onClick={() => {
                setSymbolStandard("ansi");
                addToast({
                  type: "info",
                  title: "符号规范已切换",
                  message: "已采用 ANSI Y32.2 / IEEE 315 (美标锯齿电阻、圈形电感)",
                  duration: 2200,
                });
              }}
            >
              ANSI (美标)
            </button>
          </div>
          <button
            className="action-btn"
            onClick={() => setSymbolLibraryOpen(true)}
            title="浏览、对比与导入高清矢量电气符号库 (CC0 1.0)"
          >
            <Sparkles size={14} className="text-cyan" /> 矢量符号库
          </button>
        </div>

        <div className="toolbar-vertical-divider" />

        {/* Gallery & Capture */}
        <div className="toolbar-btn-group">
          <button
            className="action-btn"
            onClick={() => setBenchmarkGalleryOpen(true)}
            title="浏览经典教材题目与 Benchmark"
          >
            <BookOpen size={15} /> 经典题库
          </button>
          <button
            className="action-btn"
            onClick={() => setMobileCaptureOpen(true)}
            title="手机局域网扫码拍照上传"
          >
            <Smartphone size={15} /> 手机直传
          </button>
          <button
            className="action-btn"
            onClick={() => setExportStudioOpen(true)}
            title="导出 HTML、LaTeX、Circuitikz 与 JSON"
          >
            <Download size={15} /> 导出中心
          </button>
        </div>

        <div className="toolbar-vertical-divider" />

        {/* Edit History */}
        <div className="toolbar-btn-group">
          <button className="icon-action-btn" onClick={undo} title="撤销 (Ctrl+Z)">
            <Undo2 size={16} />
          </button>
          <button className="icon-action-btn" onClick={redo} title="重做 (Ctrl+Y)">
            <Redo2 size={16} />
          </button>
          <button className="icon-action-btn" onClick={duplicateSelected} title="复制元件 (Ctrl+D)">
            <Copy size={16} />
          </button>
          <button className="icon-action-btn" onClick={rotateSelected} title="顺时针旋转 (R)">
            <RotateCw size={16} />
          </button>
          <button
            className="icon-action-btn icon-action-btn--danger"
            onClick={removeSelected}
            title="删除 (Delete)"
          >
            <Trash2 size={16} />
          </button>
        </div>

        <div className="toolbar-vertical-divider" />

        {/* Validation Status */}
        <button
          className={`validation-status-pill ${errCount > 0 ? "validation-status-pill--error" : "validation-status-pill--ok"}`}
          onClick={handleValidate}
          title="点击执行电路拓扑与电气规则校验"
        >
          {errCount > 0 ? (
            <>
              <AlertTriangle size={14} /> {errCount} 处需修正
            </>
          ) : (
            <>
              <CheckCircle2 size={14} /> 校验通过
            </>
          )}
        </button>

        {/* Run Analysis Button */}
        <button
          className={`run-analysis-btn ${isAnalyzing ? "run-analysis-btn--running" : ""}`}
          onClick={handleAnalyze}
          disabled={isAnalyzing}
          title="运行电路可解释分析推导"
        >
          {isAnalyzing ? (
            <>
              <span className="btn-spinner" /> 求解中...
            </>
          ) : (
            <>
              <Play size={16} fill="currentColor" /> 运行分析
            </>
          )}
        </button>

        <div className="toolbar-vertical-divider" />

        {/* Help & Clear */}
        <div className="toolbar-btn-group">
          <button
            className="icon-action-btn"
            onClick={() => fileRef.current?.click()}
            title="打开本地 JSON 文件 (Ctrl+O)"
          >
            <FolderOpen size={16} />
          </button>
          <button
            className="icon-action-btn"
            onClick={() => setShortcutsOpen(true)}
            title="快捷键指南 (F1 / ?)"
          >
            <HelpCircle size={16} />
          </button>
        </div>
      </div>

      <input ref={fileRef} type="file" accept=".json" hidden onChange={handleFileChange} />
    </header>
  );
}
