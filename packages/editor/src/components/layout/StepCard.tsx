/**
 * Single step card for the analysis panel.
 *
 * Displays one step in the solution process:
 *   - Numbered header (step description)
 *   - KaTeX-rendered formula block (centered, highlighted)
 *   - Reasoning prose text
 */

import type { AnalysisStep } from "../../types/editor";
import KatexRenderer from "../common/KatexRenderer";

export interface StepCardProps {
  step: AnalysisStep;
  index: number;
}

export default function StepCard({ step, index }: StepCardProps) {
  return (
    <div
      style={{
        marginBottom: 16,
        padding: "12px 14px",
        border: "1px solid #e0e0e0",
        borderRadius: 8,
        background: "#fff",
      }}
    >
      {/* Step header */}
      <div
        style={{
          fontSize: 14,
          fontWeight: 600,
          color: "#1a73e8",
          marginBottom: 10,
          display: "flex",
          alignItems: "center",
          gap: 6,
        }}
      >
        <span
          style={{
            display: "inline-flex",
            alignItems: "center",
            justifyContent: "center",
            width: 24,
            height: 24,
            borderRadius: "50%",
            background: "#1a73e8",
            color: "#fff",
            fontSize: 12,
            fontWeight: 700,
            flexShrink: 0,
          }}
        >
          {index + 1}
        </span>
        <span>{step.description}</span>
      </div>

      {/* Formula (rendered KaTeX block) */}
      {step.formula && (
        <div
          style={{
            padding: "12px 16px",
            background: "#f5f7fa",
            borderRadius: 6,
            marginBottom: 8,
            overflowX: "auto",
            textAlign: "center",
          }}
        >
          <KatexRenderer latex={step.formula} displayMode />
        </div>
      )}

      {/* Reasoning text */}
      {step.reasoning && (
        <div
          style={{
            fontSize: 12,
            color: "#666",
            lineHeight: 1.6,
            maxHeight: 120,
            overflowY: "auto",
          }}
        >
          <span style={{ marginRight: 4 }}>📝</span>
          {step.reasoning}
        </div>
      )}
    </div>
  );
}
