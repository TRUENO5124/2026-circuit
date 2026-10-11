/**
 * Generates self-contained HTML reports, LaTeX papers, Circuitikz snippets,
 * and JSON data artifacts with one-click download and copy support.
 */

import katex from "katex";
import katexCss from "katex/dist/katex.min.css?raw";
import type { CircuitGraph } from "@circuit/schema";
import type { AnalysisResult } from "../types/editor";

const PROBLEM_TYPE_LABELS: Record<string, string> = {
  bjt_dc_bias: "BJT 直流偏置分析",
  bjt_ac_small_signal: "BJT 交流小信号分析",
  mos_dc_bias: "MOSFET 直流偏置分析",
  mos_ac_small_signal: "MOSFET 交流小信号分析",
  diode_analysis: "二极管电路分析",
  unsupported: "通用电路拓扑分析",
};

/**
 * Trigger download of a self-contained HTML analysis report.
 */
export function exportAnalysisReport(
  result: AnalysisResult,
  circuitTitle: string,
): void {
  const html = buildReportHtml(result, circuitTitle);
  triggerDownload(
    `${sanitizeFilename(circuitTitle)}_分析报告.html`,
    html,
    "text/html;charset=utf-8",
  );
}

/**
 * Generate full academic LaTeX document string conforming to section 8.3 of presentation report.
 */
export function generateLatexDocument(
  result: AnalysisResult,
  circuitTitle: string,
  circuitGraph?: CircuitGraph,
): string {
  const typeLabel =
    PROBLEM_TYPE_LABELS[result.problemType] ?? result.problemType;
  const circuitikzSnippet = circuitGraph ? generateCircuitikzCode(circuitGraph) : "";

  return `% ==============================================================================
% 电路分析与推导学术报告
% 题目: ${circuitTitle} (${typeLabel})
% 生成时间: ${new Date().toLocaleString("zh-CN")}
% ==============================================================================
\\documentclass[12pt,a4paper]{article}
\\usepackage[UTF8]{ctex}
\\usepackage{amsmath,amssymb,amsfonts}
\\usepackage{siunitx}
\\usepackage{circuitikz}
\\usepackage{geometry}
\\usepackage{booktabs}
\\usepackage{microtype}
\\usepackage{xcolor}

\\geometry{left=2.5cm,right=2.5cm,top=2.5cm,bottom=2.5cm}

\\title{\\textbf{${circuitTitle}\\\\ \\large ${typeLabel}分析报告}}
\\author{电路学习辅助系统 (AnalogFlow Engine)}
\\date{\\today}

\\begin{document}
\\maketitle

\\begin{abstract}
本文档为针对电路模型 \\texttt{${circuitTitle}} 的结构化可解释分析报告。系统通过拓扑解析与符号推导，依次完成了直流静态工作点求解、微变等效参数分析及关键方程推导。
\\end{abstract}

\\section{电路拓扑与等效模型}
${
  circuitikzSnippet
    ? `\\begin{center}
\\begin{circuitikz}[american, scale=1.1, transform shape]
${circuitikzSnippet}
\\end{circuitikz}
\\captionof{figure}{电路原理图及元件连接拓扑}
\\end{center}`
    : "% 暂无电路图矢量代码"
}

\\section{分步骤求解与物理解释}
${result.steps
  .map(
    (step, i) => `
\\subsection{步骤 ${i + 1}：${step.description}}
\\paragraph{物理分析依据：} ${step.reasoning}

\\begin{equation}
${step.formula || "% (无独立公式)"}
\\end{equation}
`,
  )
  .join("\n")}

\\section{关键方程汇总}
${
  result.equations.length > 0
    ? result.equations
        .map(
          (eq) => `
\\begin{equation}
${eq.latex} \\quad \\text{(${eq.id})}
\\end{equation}
`,
        )
        .join("\n")
    : "本电路无额外独立方程。"
}

${
  result.operatingPoint
    ? `\\section{直流静态工作点 (Q-Point)}
\\begin{table}[htbp]
\\centering
\\caption{节点电压与支路电流计算结果}
\\begin{tabular}{ll}
\\toprule
参数项 & 求解表达式 / 数值 \\\\
\\midrule
${Object.entries(result.operatingPoint.nodeVoltages)
  .map(([k, v]) => `节点电压 $V_{${k}}$ & $${v}$ \\\\`)
  .join("\n")}
${Object.entries(result.operatingPoint.branchCurrents)
  .map(([k, v]) => `支路电流 $I_{${k}}$ & $${v}$ \\\\`)
  .join("\n")}
\\bottomrule
\\end{tabular}
\\end{table}

\\paragraph{半导体器件工作状态：}
${Object.entries(result.operatingPoint.regions)
  .map(([dev, reg]) => `\\textbf{${dev}}：${reg}`)
  .join("；")}
`
    : ""
}

${
  result.gainExpressions || result.impedanceExpressions
    ? `\\section{交流小信号动态性能指标}
\\begin{itemize}
${result.gainExpressions?.voltageGain ? `  \\item 电压增益 $A_v = ${result.gainExpressions.voltageGain}$` : ""}
${result.gainExpressions?.currentGain ? `  \\item 电流增益 $A_i = ${result.gainExpressions.currentGain}$` : ""}
${result.gainExpressions?.transconductance ? `  \\item 跨导 $G_m = ${result.gainExpressions.transconductance}$` : ""}
${result.impedanceExpressions?.inputResistance ? `  \\item 输入电阻 $R_{in} = ${result.impedanceExpressions.inputResistance}$` : ""}
${result.impedanceExpressions?.outputResistance ? `  \\item 输出电阻 $R_{out} = ${result.impedanceExpressions.outputResistance}$` : ""}
\\end{itemize}`
    : ""
}

\\section{知识点标注与教学拓展}
系统标记的核心知识点包括：${result.teachingTags.map((t) => `\\texttt{${t}}`).join("、")}。

\\end{document}
`;
}

/**
 * Trigger download of a LaTeX .tex file.
 */
export function exportLatexReport(
  result: AnalysisResult,
  circuitTitle: string,
  circuitGraph?: CircuitGraph,
): void {
  const tex = generateLatexDocument(result, circuitTitle, circuitGraph);
  triggerDownload(
    `${sanitizeFilename(circuitTitle)}_学术报告.tex`,
    tex,
    "text/x-tex;charset=utf-8",
  );
}

/**
 * Generate Circuitikz code for standard LaTeX drawing conforming to section 8.4.
 */
export function generateCircuitikzCode(circuit: CircuitGraph): string {
  const lines: string[] = [];

  // Map components
  circuit.components.forEach((comp, idx) => {
    const x = ((idx % 3) * 3).toFixed(1);
    const y = (-Math.floor(idx / 3) * 2.5).toFixed(1);
    const x2 = (parseFloat(x) + 2).toFixed(1);

    switch (comp.definitionId) {
      case "passive.resistor":
        lines.push(`  \\draw (${x},${y}) to[R, l=$${comp.label}$] (${x2},${y});`);
        break;
      case "passive.capacitor":
        lines.push(`  \\draw (${x},${y}) to[C, l=$${comp.label}$] (${x2},${y});`);
        break;
      case "passive.inductor":
        lines.push(`  \\draw (${x},${y}) to[L, l=$${comp.label}$] (${x2},${y});`);
        break;
      case "source.v_dc":
        lines.push(`  \\draw (${x},${y}) to[V, l=$${comp.label}$] (${x},${(parseFloat(y) + 2).toFixed(1)});`);
        break;
      case "source.v_ac_small_signal":
        lines.push(`  \\draw (${x},${y}) to[sV, l=$${comp.label}$] (${x},${(parseFloat(y) + 2).toFixed(1)});`);
        break;
      case "source.i_dc":
        lines.push(`  \\draw (${x},${y}) to[I, l=$${comp.label}$] (${x},${(parseFloat(y) + 2).toFixed(1)});`);
        break;
      case "diode.standard":
        lines.push(`  \\draw (${x},${y}) to[D, l=$${comp.label}$] (${x2},${y});`);
        break;
      case "bjt.npn":
        lines.push(`  \\draw (${x},${y}) node[npn] (${comp.id}) {${comp.label}};`);
        break;
      case "bjt.pnp":
        lines.push(`  \\draw (${x},${y}) node[pnp] (${comp.id}) {${comp.label}};`);
        break;
      case "fet.mosfet":
      case "fet.nmos_enhancement":
        lines.push(`  \\draw (${x},${y}) node[nmos] (${comp.id}) {${comp.label}};`);
        break;
      case "reference.ground":
        lines.push(`  \\draw (${x},${y}) node[ground] {};`);
        break;
      default:
        lines.push(`  % ${comp.label} (${comp.definitionId})`);
        break;
    }
  });

  return lines.join("\n");
}

/**
 * Trigger download of a Circuitikz snippet.
 */
export function exportCircuitikzSnippet(circuit: CircuitGraph): void {
  const code = `% Circuitikz snippet for ${circuit.title ?? circuit.id}
\\begin{circuitikz}[american]
${generateCircuitikzCode(circuit)}
\\end{circuitikz}
`;
  triggerDownload(
    `${sanitizeFilename(circuit.title ?? circuit.id)}_circuitikz.tex`,
    code,
    "text/x-tex;charset=utf-8",
  );
}

/**
 * Trigger download of circuit JSON.
 */
export function exportCircuitJson(circuit: CircuitGraph): void {
  const jsonStr = JSON.stringify(circuit, null, 2);
  triggerDownload(
    `${sanitizeFilename(circuit.title ?? circuit.id)}.json`,
    jsonStr,
    "application/json;charset=utf-8",
  );
}

// ── HTML Document Builder ─────────────────────────────────────────────

export function buildReportHtml(
  result: AnalysisResult,
  circuitTitle: string,
): string {
  const typeLabel =
    PROBLEM_TYPE_LABELS[result.problemType] ?? result.problemType;
  const now = new Date().toLocaleString("zh-CN");

  const sections: string[] = [];

  // Steps section
  if (result.steps.length > 0) {
    sections.push("<h2>📝 分步骤推导过程</h2>");
    result.steps.forEach((step, i) => {
      sections.push(renderStep(i + 1, step));
    });
  }

  // Equations section
  if (result.equations.length > 0) {
    sections.push("<h2>📐 关键方程汇总</h2>");
    sections.push('<div class="equations">');
    result.equations.forEach((eq) => {
      sections.push(renderEquationCard(eq));
    });
    sections.push("</div>");
  }

  // Operating point section
  if (
    result.operatingPoint &&
    (Object.keys(result.operatingPoint.nodeVoltages).length > 0 ||
      Object.keys(result.operatingPoint.branchCurrents).length > 0 ||
      Object.keys(result.operatingPoint.regions).length > 0)
  ) {
    sections.push("<h2>⚡ 直流静态工作点 (Q-Point)</h2>");
    if (Object.keys(result.operatingPoint.nodeVoltages).length > 0) {
      sections.push(
        renderKvTable("节点电压", result.operatingPoint.nodeVoltages),
      );
    }
    if (Object.keys(result.operatingPoint.branchCurrents).length > 0) {
      sections.push(
        renderKvTable("支路电流", result.operatingPoint.branchCurrents),
      );
    }
    if (Object.keys(result.operatingPoint.regions).length > 0) {
      sections.push("<h3>半导体器件工作区</h3>");
      sections.push(renderRegionsTable(result.operatingPoint.regions));
    }
  }

  // AC parameters section
  if (
    result.gainExpressions &&
    (result.gainExpressions.voltageGain ||
      result.gainExpressions.currentGain ||
      result.gainExpressions.transconductance)
  ) {
    sections.push("<h2>📈 交流增益性能</h2>");
    if (result.gainExpressions.voltageGain) {
      sections.push(
        renderLabeledFormula("电压放大倍数 A_v", result.gainExpressions.voltageGain),
      );
    }
    if (result.gainExpressions.currentGain) {
      sections.push(
        renderLabeledFormula("电流放大倍数 A_i", result.gainExpressions.currentGain),
      );
    }
    if (result.gainExpressions.transconductance) {
      sections.push(
        renderLabeledFormula("跨导 G_m", result.gainExpressions.transconductance),
      );
    }
  }

  if (
    result.impedanceExpressions &&
    (result.impedanceExpressions.inputResistance ||
      result.impedanceExpressions.outputResistance)
  ) {
    sections.push("<h2>📉 输入输出阻抗</h2>");
    if (result.impedanceExpressions.inputResistance) {
      sections.push(
        renderLabeledFormula("输入电阻 R_in", result.impedanceExpressions.inputResistance),
      );
    }
    if (result.impedanceExpressions.outputResistance) {
      sections.push(
        renderLabeledFormula("输出电阻 R_out", result.impedanceExpressions.outputResistance),
      );
    }
  }

  // Warnings
  if (result.warnings.length > 0) {
    sections.push('<div class="warnings">');
    sections.push("<h3>⚠ 求解与推导提示</h3>");
    sections.push("<ul>");
    result.warnings.forEach((w) => {
      sections.push(`<li>${escapeHtml(w)}</li>`);
    });
    sections.push("</ul>");
    sections.push("</div>");
  }

  return `<!DOCTYPE html>
<html lang="zh-CN">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>${escapeHtml(circuitTitle)} — 电路可解释分析报告</title>
<style>
${katexCss}
/* ── Modern Report Styles ── */
* { box-sizing: border-box; margin: 0; padding: 0; }
body {
  font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", "PingFang SC", "Microsoft YaHei", sans-serif;
  max-width: 860px;
  margin: 0 auto;
  padding: 48px 32px;
  color: #1e293b;
  background: #f8fafc;
  line-height: 1.65;
  font-size: 14.5px;
}
.report-paper {
  background: #ffffff;
  padding: 48px;
  border-radius: 16px;
  box-shadow: 0 4px 24px rgba(0,0,0,0.06);
  border: 1px solid #e2e8f0;
}
h1 { font-size: 1.85em; font-weight: 700; margin-bottom: 8px; color: #0f172a; letter-spacing: -0.02em; }
h2 { font-size: 1.35em; font-weight: 600; margin: 36px 0 16px; padding-bottom: 8px; border-bottom: 2px solid #0ea5e9; color: #0f172a; }
h3 { font-size: 1.1em; font-weight: 600; margin: 20px 0 10px; color: #334155; }
.meta { color: #64748b; font-size: 0.9em; margin-bottom: 28px; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.badge {
  display: inline-flex; align-items: center; padding: 4px 12px; border-radius: 9999px;
  font-size: 0.8em; font-weight: 600;
}
.badge-type { background: #e0f2fe; color: #0284c7; }
.badge-mode { background: #dcfce7; color: #16a34a; }
.badge-tag { background: #f1f5f9; color: #475569; }
.step {
  margin: 18px 0; padding: 18px 22px;
  border: 1px solid #e2e8f0; border-radius: 12px; background: #ffffff;
  box-shadow: 0 1px 4px rgba(0,0,0,0.02);
}
.step-header { font-size: 1.05em; font-weight: 600; color: #0284c7; margin-bottom: 12px; display: flex; align-items: center; }
.step-num {
  display: inline-flex; align-items: center; justify-content: center;
  width: 26px; height: 26px; border-radius: 50%;
  background: #0ea5e9; color: #fff; font-size: 0.8em; margin-right: 10px; font-weight: 700;
}
.step-formula {
  padding: 16px 20px; background: #f8fafc; border-radius: 8px; border: 1px solid #edf2f7;
  margin-bottom: 12px; overflow-x: auto; text-align: center;
}
.step-reasoning { font-size: 0.9em; color: #475569; line-height: 1.6; }
.equations { display: grid; grid-template-columns: repeat(auto-fill, minmax(320px, 1fr)); gap: 14px; margin: 16px 0; }
.eq-card { padding: 16px 18px; border: 1px solid #e2e8f0; border-radius: 10px; background: #f8fafc; }
.eq-id { font-size: 0.78em; color: #94a3b8; font-family: monospace; margin-bottom: 8px; }
.eq-vars { display: flex; gap: 6px; margin-top: 10px; flex-wrap: wrap; }
.eq-var { font-size: 0.72em; padding: 2px 8px; border-radius: 6px; background: #e0f2fe; color: #0369a1; font-family: monospace; font-weight: 600; }
table { width: 100%; border-collapse: collapse; margin: 12px 0 24px; font-size: 0.95em; }
th { text-align: left; padding: 10px 14px; border-bottom: 2px solid #cbd5e1; font-size: 0.85em; color: #64748b; background: #f8fafc; font-weight: 600; }
td { padding: 10px 14px; border-bottom: 1px solid #f1f5f9; }
td:first-child { font-family: monospace; font-size: 0.9em; color: #334155; }
.formula-block { padding: 16px 20px; border: 1px solid #e2e8f0; border-radius: 10px; background: #ffffff; margin: 12px 0 18px; text-align: center; }
.formula-block .label { font-size: 0.85em; color: #64748b; margin-bottom: 8px; font-weight: 600; }
.warnings { margin-top: 24px; padding: 16px 20px; background: #fefce8; border: 1px solid #fef08a; border-radius: 10px; }
.warnings h3 { color: #854d0e; margin-top: 0; }
.warnings ul { margin: 8px 0 0 20px; color: #854d0e; font-size: 0.9em; }
.footer { margin-top: 48px; padding-top: 18px; border-top: 1px solid #e2e8f0; text-align: center; font-size: 0.8em; color: #94a3b8; }
.print-btn-bar { display: flex; justify-content: flex-end; margin-bottom: 20px; gap: 12px; }
.btn {
  padding: 8px 16px; border-radius: 8px; font-size: 13px; font-weight: 600; cursor: pointer;
  border: 1px solid #cbd5e1; background: #fff; color: #334155; transition: all 0.2s;
}
.btn:hover { background: #f1f5f9; border-color: #94a3b8; }
.btn-primary { background: #0ea5e9; color: #fff; border-color: #0ea5e9; }
.btn-primary:hover { background: #0284c7; }
@media print {
  body { background: #fff; padding: 0; }
  .report-paper { box-shadow: none; border: 0; padding: 0; }
  .print-btn-bar { display: none; }
  .step, .eq-card { break-inside: avoid; }
  table { break-inside: avoid; }
}
</style>
</head>
<body>
<div class="print-btn-bar">
  <button class="btn btn-primary" onclick="window.print()">🖨 打印或另存为 PDF</button>
</div>
<div class="report-paper">
  <h1>${escapeHtml(circuitTitle)}</h1>
  <div class="meta">
    <span class="badge badge-type">${escapeHtml(typeLabel)}</span>
    <span class="badge badge-mode">${result.isSymbolic ? "符号推导" : "数值求解"}</span>
    ${result.teachingTags.map((t) => `<span class="badge badge-tag">${escapeHtml(t)}</span>`).join(" ")}
    <span>· 生成于 ${now}</span>
  </div>
  ${sections.join("\n")}
  <div class="footer">
    由电路分析助手 (AnalogFlow) 自动生成 · 遵循国家标准 GB/T 4728 电路规范
  </div>
</div>
</body>
</html>`;
}

// ── Helpers ────────────────────────────────────────────────────────────

function renderStep(n: number, step: {
  description: string;
  formula: string;
  reasoning: string;
}): string {
  const formulaHtml = step.formula
    ? `<div class="step-formula">${safeKatex(step.formula, true)}</div>`
    : "";
  const reasoningHtml = step.reasoning
    ? `<div class="step-reasoning">💡 <strong>推导依据：</strong>${escapeHtml(step.reasoning)}</div>`
    : "";

  return `<div class="step">
  <div class="step-header"><span class="step-num">${n}</span>${escapeHtml(step.description)}</div>
  ${formulaHtml}
  ${reasoningHtml}
</div>`;
}

function renderEquationCard(eq: {
  id: string;
  latex: string;
  variables: string[];
}): string {
  const varsHtml = eq.variables.length > 0
    ? `<div class="eq-vars">${eq.variables.map((v) => `<span class="eq-var">${escapeHtml(v)}</span>`).join("")}</div>`
    : "";
  return `<div class="eq-card">
  <div class="eq-id">${escapeHtml(eq.id)}</div>
  <div>${safeKatex(eq.latex, true)}</div>
  ${varsHtml}
</div>`;
}

function renderKvTable(title: string, data: Record<string, string>): string {
  const rows = Object.entries(data)
    .map(
      ([k, v]) =>
        `<tr><td>${escapeHtml(k)}</td><td>${safeKatex(v, false)}</td></tr>`,
    )
    .join("");
  return `<h3>${escapeHtml(title)}</h3>
<table><thead><tr><th>变量名</th><th>表达式 / 评估值</th></tr></thead><tbody>${rows}</tbody></table>`;
}

function renderRegionsTable(regions: Record<string, string>): string {
  const rows = Object.entries(regions)
    .map(([d, r]) => `<tr><td><code>${escapeHtml(d)}</code></td><td><strong>${escapeHtml(r)}</strong></td></tr>`)
    .join("");
  return `<table><thead><tr><th>器件</th><th>工作状态</th></tr></thead><tbody>${rows}</tbody></table>`;
}

function renderLabeledFormula(label: string, latex: string): string {
  return `<div class="formula-block">
  <div class="label">${escapeHtml(label)}</div>
  ${safeKatex(latex, true)}
</div>`;
}

function safeKatex(latex: string, displayMode: boolean): string {
  if (!latex) return "";
  try {
    return katex.renderToString(latex, {
      throwOnError: false,
      displayMode,
      strict: false,
      trust: false,
    });
  } catch {
    return `<code style="color:#d93025;">${escapeHtml(latex)}</code>`;
  }
}

function escapeHtml(s: string): string {
  return s
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function sanitizeFilename(s: string): string {
  return s.replace(/[<>:"/\\|?*]/g, "_").replace(/\s+/g, "_").slice(0, 80);
}

function triggerDownload(filename: string, content: string, mimeType: string): void {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
