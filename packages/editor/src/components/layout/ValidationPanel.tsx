/**
 * Validation issues panel displaying electrical checks and suggestions.
 */

import React from "react";
import type { ValidationIssue } from "@circuit/schema";
import { useEditorStore } from "../../store/editorStore";
import { AlertTriangle, AlertCircle, Info, CheckCircle2 } from "lucide-react";

export default function ValidationPanel() {
  const { validationIssues, lastValidatedAt, selectComponent } = useEditorStore();

  if (validationIssues.length === 0 && !lastValidatedAt) {
    return null;
  }

  const errors = validationIssues.filter((i) => i.severity === "error");
  const warnings = validationIssues.filter((i) => i.severity === "warning");

  return (
    <div className="validation-panel-container">
      {validationIssues.length === 0 ? (
        <div className="validation-panel-empty">
          <CheckCircle2 size={18} className="text-emerald" />
          <span>电路拓扑符合电气规范，无未连接引脚或参数异常。</span>
        </div>
      ) : (
        <div className="validation-panel-list">
          {validationIssues.map((issue, idx) => {
            const isError = issue.severity === "error";

            return (
              <div
                key={idx}
                className={`validation-row-item ${isError ? "validation-row-item--error" : "validation-row-item--warn"}`}
                onClick={() => {
                  // If path mentions a component id, select it
                  if (issue.path && issue.path.length > 0) {
                    const compId = issue.path[issue.path.length - 1];
                    if (typeof compId === "string") {
                      selectComponent(compId);
                    }
                  }
                }}
              >
                <div className="validation-row-icon">
                  {isError ? (
                    <AlertCircle size={15} className="text-danger" />
                  ) : (
                    <AlertTriangle size={15} className="text-amber" />
                  )}
                </div>
                <div className="validation-row-text">
                  <span className="validation-row-msg">{issue.message}</span>
                  {issue.code && (
                    <code className="validation-row-code">{issue.code}</code>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}
    </div>
  );
}
