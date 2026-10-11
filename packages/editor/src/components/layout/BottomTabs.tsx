/**
 * Bottom dockable drawer for quick validation and analysis status in editor view.
 */

import React, { useState, useEffect, useRef } from "react";
import { useEditorStore } from "../../store/editorStore";
import ValidationPanel from "./ValidationPanel";
import AnalysisPanel from "./AnalysisPanel";
import { ChevronUp, ChevronDown, CheckCircle2, AlertTriangle, Zap, ExternalLink } from "lucide-react";

type BottomTab = "validation" | "analysis";

export default function BottomTabs() {
  const {
    analysisStatus,
    analysisResult,
    validationIssues,
    lastValidatedAt,
    setViewMode,
  } = useEditorStore();

  const hasAnalysis =
    analysisResult != null ||
    analysisStatus === "submitting" ||
    analysisStatus === "polling" ||
    analysisStatus === "failed";
  const hasValidation = validationIssues.length > 0 || lastValidatedAt != null;

  const [activeTab, setActiveTab] = useState<BottomTab>("validation");
  const [collapsed, setCollapsed] = useState(true);

  useEffect(() => {
    if (analysisStatus === "completed" && analysisResult) {
      setActiveTab("analysis");
      setCollapsed(false);
    }
  }, [analysisStatus, analysisResult]);

  if (!hasAnalysis && !hasValidation) {
    return null;
  }

  const errCount = validationIssues.filter((i) => i.severity === "error").length;
  const warnCount = validationIssues.filter((i) => i.severity === "warning").length;

  return (
    <div className={`bottom-dock ${collapsed ? "bottom-dock--collapsed" : "bottom-dock--expanded"}`}>
      {/* Dock Bar */}
      <div className="bottom-dock-bar">
        <div className="dock-tabs-list">
          {hasValidation && (
            <button
              className={`dock-tab-btn ${activeTab === "validation" && !collapsed ? "dock-tab-btn--active" : ""}`}
              onClick={() => {
                setActiveTab("validation");
                setCollapsed(false);
              }}
            >
              {errCount > 0 ? (
                <span className="dock-badge dock-badge--error">{errCount} 错误</span>
              ) : (
                <span className="dock-badge dock-badge--ok">✓ 校验通过</span>
              )}
              <span>规则检查</span>
            </button>
          )}

          {hasAnalysis && (
            <button
              className={`dock-tab-btn ${activeTab === "analysis" && !collapsed ? "dock-tab-btn--active" : ""}`}
              onClick={() => {
                setActiveTab("analysis");
                setCollapsed(false);
              }}
            >
              <Zap size={14} className="text-cyan" />
              <span>求解推导</span>
              {analysisStatus === "completed" && (
                <span className="dock-badge dock-badge--ok">就绪</span>
              )}
            </button>
          )}
        </div>

        <div className="dock-bar-actions">
          {activeTab === "analysis" && !collapsed && (
            <button
              className="dock-tool-btn"
              onClick={() => setViewMode("analysis")}
              title="切换至全屏分析推导工作台"
            >
              <ExternalLink size={13} /> 全屏推导视图
            </button>
          )}

          <button
            className="dock-tool-btn"
            onClick={() => setCollapsed(!collapsed)}
            title={collapsed ? "展开面板" : "折叠面板"}
          >
            {collapsed ? <ChevronUp size={16} /> : <ChevronDown size={16} />}
          </button>
        </div>
      </div>

      {/* Dock Body */}
      {!collapsed && (
        <div className="bottom-dock-body">
          {activeTab === "validation" && <ValidationPanel />}
          {activeTab === "analysis" && <AnalysisPanel />}
        </div>
      )}
    </div>
  );
}
