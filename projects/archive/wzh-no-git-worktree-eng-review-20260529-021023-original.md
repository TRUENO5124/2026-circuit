# 工程评审：面向电子电路学习的图像识别与可解释分析辅助系统

由 /plan-eng-review 在 2026-05-29 生成  
分支：no-git-worktree  
基于设计文档：projects/wzh-no-git-worktree-design-20260527-172528.md  
状态：APPROVED_WITH_SCOPE_REDUCTION

## Step 0：范围挑战

原始设计同时包含图像识别、拓扑编辑、分析引擎、LaTeX 输出、benchmark、可视化标注和远期扩展，复杂度超过第一阶段可控范围。

已确认收窄为 **最小端到端闭环**：

- 30 道 benchmark 框架
- 电路拓扑中间表示
- 手动/半自动拓扑校正
- 模电分析引擎
- LaTeX / 步骤输出
- 轻量本地图像处理原型
- 手机局域网采集端
- 后续可替换为更强识别模块

不追求第一阶段完整云端服务、账号系统、完整 Multisim 式 IDE、任意电路自动分析或模拟 IC 设计。

## 已确认工程决策

1. **typed circuit graph 是唯一事实来源，SPICE/netlist 作为导出和校验层。**
   前端画布、图像识别、分析引擎、LaTeX 输出和 benchmark 都围绕 typed circuit graph 读写。

2. **图像识别采用 stage-gated pipeline。**
   图像模块输出候选识别结果和候选拓扑，不直接生成最终电路；用户确认后才进入分析。

3. **编辑器采用轻量 Web schematic editor + 层级化元件库。**
   吸收 CircuitMath 的轻量交互和 Multisim 的元件库组织方式，但不做完整 EDA IDE。

4. **分析引擎采用课程规则层 + SymPy / Lcapy / SPICE 工具层。**
   LLM 不作为唯一计算来源。课程规则负责等效、电路模型和题型逻辑；工具层负责符号求解、线性电路分析和数值校验。

5. **benchmark 采用多层 ground truth。**
   每道题记录图片、题目文本、元件、节点、参数、typed graph、等效电路、关键方程、目标答案、可接受等价表达式和知识点标签。

6. **输出系统区分 AnalysisResult 和 PresentationResult。**
   公式、标注、LaTeX 和解释都从结构化 AnalysisResult 渲染，不让展示层重新生成答案。

7. **采用 TypeScript 前端 + Python 分析服务 + 共享 JSON Schema。**
   前端负责交互，Python 负责 SymPy / Lcapy / SPICE 和 benchmark，二者通过严格 schema 通信。

8. **采用数据驱动元件库。**
   主要服务于模电分析、数电分析和电路分析扩展；模拟 IC 设计只作为远期探索方向，不作为当前或中期分析主线。

9. **分析引擎采用插件式 AnalysisPipeline + Strategy 注册机制。**
   第一阶段服务模电基础题，后续扩展电路分析和数电分析。

10. **采用分层测试矩阵 + 30 题 benchmark 自动评测。**
    测试定位到 schema、benchmark、analysis、presentation、UI/E2E 各层。

11. **设定分层量化通过线。**
    识别、分析和输出都有明确验收指标。

12. **本地优先架构 + 局域网手机采集端 + 本地异步 job。**
    手机扫码上传图片到电脑本地服务；全部处理在本地完成，不依赖云端或互联网。

13. **符号计算加入超时、复杂度限制和 fallback。**
    避免某道题把本地软件卡死。

## 推荐系统架构

```text
----------------------+       LAN upload       +----------------------+
| Mobile Capture Page |  -------------------->  | Local API Server     |
| - scan QR code      |                         | - sessions           |
| - take/upload photo |                         | - upload endpoint    |
+----------------------+                         | - job endpoints      |
                                                   +----------+-----------+
                                                              |
                                                              v
                                                   +----------------------+
                                                   | Local Job Queue      |
                                                   | - SQLite job table   |
                                                   | - local file store   |
                                                   +----------+-----------+
                                                              |
                     +----------------------------------------+------------------+
                     |                                                           |
                     v                                                           v
          +----------------------+                                  +----------------------+
          | Vision Pipeline      |                                  | Analysis Pipeline    |
          | - PDF/native extract |                                  | - validate graph     |
          | - classical CV       |                                  | - select strategy    |
          | - graph proposal     |                                  | - build equivalent   |
          |                      |                                  |   circuit diagrams   |
          | - validation issues  |                                  | - solve equations    |
          +----------+-----------+                                  +----------+-----------+
                     |                                                           |
                     v                                                           v
          +----------------------+                                  +----------------------+
          | Web Schematic Editor | <---------- typed graph -------- | AnalysisResult       |
          | - correction UI      |                                  | - equations          |
          | - component library  |                                  | - solved targets     |
          | - annotations        |                                  | - warnings           |
          +----------+-----------+                                  +----------+-----------+
                     |                                                           |
                     +-------------------- PresentationResult <------------------+
                                          - LaTeX
                                          - steps
                                          - knowledge points
```

## What Already Exists

- [立项申请书](../面向电子电路学习的图像识别与可解释分析辅助系统-立项申请书.docx) 已经形成项目申报版描述。
- [office-hours 设计文档](wzh-no-git-worktree-design-20260527-172528.md) 已经明确项目目标、约束、备选方案、推荐路线和成功标准。
- 当前目录不是 git 仓库，没有已有代码、测试框架、前端工程或后端工程。

## NOT In Scope

- 云端部署、账号系统、多用户权限、云同步：第一阶段全部本地运行。
- 复杂分布式任务系统：第一阶段使用本地 job table + local worker。
- 完整 Multisim 式 EDA IDE：第一阶段只做轻量 schematic editor + 层级元件库。
- 任意电路全自动分析：第一阶段限定 30 道模电基础题 benchmark。
- 手绘电路图识别：第一阶段优先标准印刷图 / 手机拍摄教材图。
- 模拟 IC 设计：远期探索方向，不进入第一阶段或中期分析主线。
- LLM 直接计算电路答案：LLM 可用于解释或辅助文本理解，但不能作为唯一计算来源。

## 分层通过线

识别层：

- 元件类型识别准确率 >= 90%
- 元件参数识别准确率 >= 85%
- 节点连接识别准确率 >= 85%
- 低置信度 / 可疑点召回率 >= 90%

分析层：

- confirmed graph 上目标求解量正确率 >= 90%
- 等效电路生成正确率 >= 85%
- 关键方程正确率 >= 85%

输出层：

- LaTeX 渲染成功率 >= 95%
- 步骤说明覆盖关键知识点 >= 85%
- 用户修正后端到端成功率 >= 90%

## Failure Modes

| Codepath | 现实失败方式 | 测试要求 | 错误处理要求 | 用户可见性 |
|---|---|---|---|---|
| 手机上传 | 手机与电脑不在同一局域网，上传失败 | E2E 手动/自动上传失败测试 | 显示连接失败、二维码重试说明 | 明确错误 |
| 图片导入 | 图片过大、格式不支持、方向错误 | 文件类型、大小、EXIF 方向测试 | 拒绝或自动修正并提示 | 明确错误 |
| CV 候选识别 | 断线、阴影、低对比度导致拓扑不完整 | benchmark 分层识别测试 | 输出 validation issues，不直接分析 | 明确可疑点 |
| 拓扑确认 | 用户遗漏未连接端口 | schema + UI 校验测试 | 阻止分析并高亮问题 | 明确错误 |
| typed graph schema | 前后端字段不一致 | JSON Schema contract tests | 请求拒绝并返回字段错误 | 明确错误 |
| 分析策略选择 | 题型分类错误 | strategy selection tests | 提供可手动选择策略 fallback | 明确警告 |
| 符号求解 | SymPy/Lcapy 表达式爆炸或超时 | timeout / complexity tests | partial result + warning + fallback | 明确警告 |
| SPICE 校验 | 本地 ngspice 缺失或运行失败 | adapter failure tests | 跳过校验并说明依赖缺失 | 明确警告 |
| LaTeX 输出 | 公式不可渲染或变量不一致 | LaTeX render tests | 标注失败公式并保留结构化结果 | 明确错误 |
| benchmark 批跑 | 单题失败中断全套评测 | batch isolation tests | 记录单题失败，继续后续题 | 明确报告 |

关键要求：任何失败都不能静默吞掉；必须进入 `warnings[]` / `errors[]`，并在 UI 中展示。

## Worktree Parallelization Strategy

虽然当前不是 git 仓库，但实现时可以按模块并行。

| Step | Modules touched | Depends on |
|---|---|---|
| Schema & component definitions | `packages/schema`, `packages/components` | — |
| Web editor shell | `apps/web` | Schema |
| Local API + job queue | `services/api`, `services/jobs` | Schema |
| Analysis service | `services/analysis` | Schema, component definitions |
| Vision proposal service | `services/vision` | Schema |
| Benchmark harness | `benchmarks`, `services/eval` | Schema, analysis service |
| Presentation output | `services/presentation`, `apps/web` | AnalysisResult schema |
| Mobile capture page | `apps/web`, `services/api` | Local API |

Parallel lanes:

- Lane A：Schema & component definitions -> Analysis service -> Presentation output
- Lane B：Web editor shell -> Mobile capture page
- Lane C：Local API + job queue -> Vision proposal service
- Lane D：Benchmark harness，等待 Schema 和 Analysis service 后启动

Execution order:

1. 先完成 Schema & component definitions。
2. 并行启动 Web editor、Local API/job queue、Analysis service。
3. 接入 Vision proposal 和 Mobile capture。
4. 最后补齐 benchmark harness 和 presentation polish。

Conflict flags:

- `apps/web` 会同时被 editor、result viewer、mobile capture 触碰，建议先定义页面路由和共享组件边界。
- `packages/schema` 是所有模块共同依赖，必须先稳定初版，避免并行分支频繁冲突。

## Implementation Tasks

Synthesized from this review's findings. Each task derives from a specific finding above. Run with Claude Code or Codex; checkbox as you ship.

- [ ] **T1 (P1, human: ~2d / CC: ~4h)** — schema — Define typed circuit graph as the system source of truth
  - Surfaced by: Architecture Review — Decision 1
  - Files: `packages/schema`
  - Verify: JSON Schema contract tests for valid/invalid CircuitGraph, ComponentDefinition, AnalysisResult, ProblemCase

- [ ] **T2 (P1, human: ~3d / CC: ~6h)** — component library — Build data-driven component definitions
  - Surfaced by: Code Quality Review — Decision 8
  - Files: `packages/components`, `packages/schema`
  - Verify: menu generation, port validation, SPICE/Lcapy mapping tests for first-stage components

- [ ] **T3 (P1, human: ~5d / CC: ~1d)** — analysis — Implement plugin-style AnalysisPipeline and first-stage strategies, including equivalent circuit diagrams
  - Surfaced by: Code Quality Review — Decision 9
  - Files: `services/analysis`
  - Verify: unit tests for diode_basic, bjt_bias, bjt_small_signal, mos_small_signal on confirmed graphs; visual/schema checks for DC equivalent and AC small-signal equivalent circuit diagrams

- [ ] **T4 (P1, human: ~3d / CC: ~6h)** — solver safety — Add timeout, complexity limits, fallback, and warning propagation
  - Surfaced by: Performance Review — Decision 13
  - Files: `services/analysis`, `services/jobs`
  - Verify: tests that force timeout and expression explosion return partial result instead of hanging

- [ ] **T5 (P1, human: ~4d / CC: ~1d)** — local runtime — Build local API, SQLite job queue, and worker loop
  - Surfaced by: Performance Review — Decision 12
  - Files: `services/api`, `services/jobs`
  - Verify: job lifecycle tests for queued, running, needs_correction, completed, failed, cancelled

- [ ] **T6 (P2, human: ~4d / CC: ~1d)** — editor — Build lightweight schematic editor with correction workflow
  - Surfaced by: Architecture Review — Decision 3
  - Files: `apps/web`
  - Verify: E2E test for load graph, edit component parameter, fix node connection, submit confirmed graph

- [ ] **T7 (P2, human: ~3d / CC: ~6h)** — mobile capture — Add LAN QR/session upload flow
  - Surfaced by: Performance Review — Decision 12
  - Files: `apps/web`, `services/api`
  - Verify: E2E/manual test for phone upload via one-time session token

- [ ] **T8 (P2, human: ~5d / CC: ~1d)** — vision — Implement local stage-gated image proposal pipeline
  - Surfaced by: Architecture Review — Decision 2
  - Files: `services/vision`
  - Verify: benchmark tests for candidate detection, validation issues, graph proposal output

- [ ] **T9 (P1, human: ~1w / CC: ~2d)** — benchmark — Create 30-case multilayer benchmark and evaluation harness
  - Surfaced by: Architecture Review — Decision 5; Test Review — Decisions 10/11
  - Files: `benchmarks`, `services/eval`
  - Verify: automated report emits recognition, topology, analysis, equation, LaTeX, and end-to-end metrics

- [ ] **T10 (P2, human: ~3d / CC: ~6h)** — presentation — Render AnalysisResult into LaTeX, explanation, annotations, and warnings
  - Surfaced by: Architecture Review — Decision 6
  - Files: `services/presentation`, `apps/web`
  - Verify: LaTeX render tests and traceability tests from displayed variables back to AnalysisResult

## Test Plan

### Affected Pages / Routes

- `/` — desktop local app shell, project/session landing.
- `/capture/:sessionId` — mobile capture page for local LAN upload.
- `/editor/:problemId` — schematic editor and correction workflow.
- `/jobs/:jobId` — job status and result polling.
- `/results/:problemId` — analysis result, LaTeX, annotations, warnings.
- `/benchmark` — local benchmark run and report viewer.

### Key Interactions to Verify

- User scans QR code and uploads an image from phone.
- Desktop receives uploaded image and creates a vision job.
- System shows graph proposal with validation issues.
- User corrects node connections, component type, and parameters.
- User confirms graph and triggers analysis.
- System returns AnalysisResult and PresentationResult.
- User exports LaTeX.
- User runs 30-case benchmark and views metrics.

### Edge Cases

- Phone cannot reach desktop local server.
- QR session expired or token reused.
- Unsupported image format or very large image.
- Vision proposal has missing component port.
- User submits graph with floating node.
- Analysis strategy cannot classify problem.
- SymPy/Lcapy timeout.
- SPICE/ngspice unavailable.
- LaTeX render failure.
- One benchmark case fails while batch continues.

### Critical Paths

- Capture -> graph proposal -> correction -> confirmed graph -> analysis -> equivalent circuit diagrams -> LaTeX export.
- Confirmed graph -> strategy selection -> DC/AC equivalent circuit diagrams -> equations -> solved target.
- Benchmark case -> expected ground truth -> per-layer metric report.

## Review Summary

- Step 0: Scope Challenge — scope reduced to minimum end-to-end local prototype.
- Architecture Review: 6 decisions confirmed.
- Code Quality Review: 3 decisions confirmed.
- Test Review: 2 decisions confirmed.
- Performance Review: 2 decisions confirmed.
- NOT in scope: written.
- What already exists: written.
- Failure modes: 10 failure modes listed; silent failures disallowed.
- Worktree parallelization: 4 lanes proposed.
- Outside voice: skipped; not needed for current planning pass.
