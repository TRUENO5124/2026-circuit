/**
 * Reusable KaTeX renderer component.
 *
 * Renders LaTeX strings into HTML using KaTeX's renderToString.
 * Supports inline copy button on hover for displayMode equations.
 */

import { useMemo, useState } from "react";
import katex from "katex";
import { Copy, Check } from "lucide-react";
import { useEditorStore } from "../../store/editorStore";

export interface KatexRendererProps {
  /** LaTeX string to render */
  latex: string;
  /** Display mode (centered, larger) vs inline mode */
  displayMode?: boolean;
  /** Optional CSS class for the wrapper element */
  className?: string;
  /** Allow 1-click copy */
  copyable?: boolean;
}

export default function KatexRenderer({
  latex,
  displayMode = false,
  className,
  copyable = false,
}: KatexRendererProps) {
  const [copied, setCopied] = useState(false);
  const { addToast } = useEditorStore();

  const html = useMemo(() => {
    if (!latex) return "";
    try {
      return katex.renderToString(latex, {
        throwOnError: false,
        displayMode,
        strict: false,
        trust: false,
      });
    } catch {
      const escaped = latex
        .replace(/&/g, "&amp;")
        .replace(/</g, "&lt;")
        .replace(/>/g, "&gt;");
      return `<code style="color:#d93025;background:#fce8e6;padding:2px 6px;border-radius:4px;font-size:12px;">${escaped}</code>`;
    }
  }, [latex, displayMode]);

  const handleCopy = (e: React.MouseEvent) => {
    e.stopPropagation();
    navigator.clipboard.writeText(latex);
    setCopied(true);
    addToast({
      type: "success",
      title: "公式已复制",
      message: `LaTeX: ${latex.slice(0, 30)}${latex.length > 30 ? "..." : ""}`,
      duration: 2000,
    });
    setTimeout(() => setCopied(false), 1800);
  };

  if (!latex) return null;

  if (copyable || displayMode) {
    return (
      <span className={`katex-wrapper ${displayMode ? "katex-wrapper--display" : ""} ${className ?? ""}`}>
        <span dangerouslySetInnerHTML={{ __html: html }} />
        {copyable && (
          <button
            type="button"
            className="katex-copy-btn"
            onClick={handleCopy}
            title="复制 LaTeX 代码"
          >
            {copied ? <Check size={13} className="text-emerald" /> : <Copy size={13} />}
          </button>
        )}
      </span>
    );
  }

  return (
    <span
      className={className}
      dangerouslySetInnerHTML={{ __html: html }}
    />
  );
}
