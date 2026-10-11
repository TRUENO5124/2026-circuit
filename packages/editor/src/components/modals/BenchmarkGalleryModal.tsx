import React, { useState } from "react";
import { useEditorStore } from "../../store/editorStore";
import { BUILTIN_BENCHMARKS } from "../../lib/benchmarks";
import { BookOpen, Sparkles, Check, ArrowRight, X, Cpu, Layers } from "lucide-react";
import { submitAnalysis, getTaskStatus } from "../../api/client";

export default function BenchmarkGalleryModal() {
  const {
    benchmarkGalleryOpen,
    setBenchmarkGalleryOpen,
    loadCircuitGraph,
    setCircuitTitle,
    setAnalysisStatus,
    setAnalysisResult,
    exportCircuitGraph,
    validate,
    setViewMode,
    addToast,
  } = useEditorStore();

  const [selectedId, setSelectedId] = useState<string>(BUILTIN_BENCHMARKS[0].id);

  if (!benchmarkGalleryOpen) return null;

  const currentBenchmark = BUILTIN_BENCHMARKS.find((b) => b.id === selectedId) ?? BUILTIN_BENCHMARKS[0];

  const handleLoadOnly = () => {
    loadCircuitGraph(currentBenchmark.circuitGraph);
    setCircuitTitle(currentBenchmark.title);
    setBenchmarkGalleryOpen(false);
    addToast({
      type: "success",
      title: "电路已载入画布",
      message: currentBenchmark.title,
    });
  };

  const handleLoadAndAnalyze = async () => {
    loadCircuitGraph(currentBenchmark.circuitGraph);
    setCircuitTitle(currentBenchmark.title);
    setBenchmarkGalleryOpen(false);
    setViewMode("analysis");

    addToast({
      type: "info",
      title: "正在提交分析任务...",
      message: currentBenchmark.title,
    });

    setAnalysisStatus("submitting");

    try {
      const graph = currentBenchmark.circuitGraph;
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
                title: "分析求解完成",
                message: `成功推导 ${data.result.steps?.length ?? 0} 个步骤`,
              });
            } else {
              setAnalysisStatus("failed");
              setAnalysisResult(null, "分析完成但没有结果");
            }
          } else if (data.status === "failed") {
            clearInterval(poll);
            setAnalysisStatus("failed");
            setAnalysisResult(null, data.error ?? "未知错误");
          }
        } catch {
          // keep polling
        }
      }, 1500);
    } catch (err) {
      setAnalysisStatus("failed");
      setAnalysisResult(null, String(err));
      addToast({
        type: "error",
        title: "分析请求失败",
        message: String(err),
      });
    }
  };

  return (
    <div className="modal-backdrop" onClick={() => setBenchmarkGalleryOpen(false)}>
      <div className="modal-dialog modal-dialog--large" onClick={(e) => e.stopPropagation()}>
        <div className="modal-header">
          <div className="modal-title-group">
            <div className="modal-icon-badge">
              <BookOpen size={20} className="text-cyan" />
            </div>
            <div>
              <h2 className="modal-title">经典电路题库与 Benchmark</h2>
              <p className="modal-subtitle">精选模电经典教材题目，支持一键载入拓扑与自动化可解释符号推导</p>
            </div>
          </div>
          <button className="modal-close-btn" onClick={() => setBenchmarkGalleryOpen(false)}>
            <X size={18} />
          </button>
        </div>

        <div className="modal-body modal-body--split">
          {/* Left: benchmark list */}
          <div className="benchmark-list">
            {BUILTIN_BENCHMARKS.map((bm) => {
              const isSelected = bm.id === selectedId;
              return (
                <div
                  key={bm.id}
                  className={`benchmark-card-item ${isSelected ? "benchmark-card-item--active" : ""}`}
                  onClick={() => setSelectedId(bm.id)}
                >
                  <div className="benchmark-card-top">
                    <span className="benchmark-category">{bm.category}</span>
                    <span className={`difficulty-badge difficulty-badge--${bm.difficulty}`}>
                      {bm.difficulty}
                    </span>
                  </div>
                  <div className="benchmark-card-title">{bm.title}</div>
                  <div className="benchmark-card-meta">
                    <span>
                      <Cpu size={12} /> {bm.circuitGraph.components.length} 个元件
                    </span>
                    <span>
                      <Layers size={12} /> {bm.circuitGraph.nodes.length} 个节点
                    </span>
                  </div>
                </div>
              );
            })}
          </div>

          {/* Right: benchmark details */}
          <div className="benchmark-detail-pane">
            <div className="benchmark-detail-header">
              <div className="benchmark-detail-category">{currentBenchmark.category}</div>
              <h3 className="benchmark-detail-title">{currentBenchmark.title}</h3>
            </div>

            <div className="detail-section">
              <h4 className="detail-section-title">📌 题目描述与要求</h4>
              <p className="detail-problem-text">{currentBenchmark.problemText}</p>
            </div>

            <div className="detail-section">
              <h4 className="detail-section-title">🎯 理论推导预期结果 (Ground Truth)</h4>
              <div className="detail-truth-box">
                {currentBenchmark.groundTruthSummary}
              </div>
            </div>

            <div className="detail-section">
              <h4 className="detail-section-title">🏷 教学知识点标签</h4>
              <div className="detail-tags-list">
                {currentBenchmark.teachingTags.map((tag) => (
                  <span key={tag} className="teaching-tag-pill">
                    #{tag}
                  </span>
                ))}
              </div>
            </div>

            <div className="detail-circuit-preview">
              <div className="detail-circuit-meta">
                <span>拓扑 ID: <code>{currentBenchmark.circuitGraph.id}</code></span>
                <span>包含 {currentBenchmark.circuitGraph.components.length} 个器件 · {currentBenchmark.circuitGraph.connections.length} 处连接</span>
              </div>
            </div>
          </div>
        </div>

        <div className="modal-footer">
          <div className="modal-footer-hint">
            <span>💡 提示：点击「载入并直接分析」将自动切换至解题工作台</span>
          </div>
          <div className="modal-footer-actions">
            <button className="btn btn-outline" onClick={handleLoadOnly}>
              仅载入电路图
            </button>
            <button className="btn btn-primary" onClick={handleLoadAndAnalyze}>
              <Sparkles size={16} /> 立即载入并分析
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
