---
spec_title: "第一阶段本地电路分析原型"
spec_created_at: "2026-06-01"
spec_revised_at: "2026-06-12"
spec_reviewed_at: "2026-07-16"
spec_source: "/spec"
spec_issue_url:
spec_issue_number:
spec_executed: false
spec_execution_status: "partial"
spec_execution_mode: "local-file-only"
dedupe_status: "skipped: gh is not installed"
quality_gate_status: "skipped: codex.exe returned Access is denied"
---

# Epic：第一阶段本地电路分析原型

## 2026-07-16 复核状态

| Issue | 状态 | 当前证据/边界 |
|---|---|---|
| I1 | 已完成 | Schema、18 类元件、3 个 Benchmark、校验和 JSON Schema 已实现 |
| I2/I3 | 基础版已完成 | 5 个策略、MNA、安全求解器和 16 项引擎测试；题型覆盖仍有限 |
| I4 | 基础版已完成 | FastAPI、SQLite、任务队列和 Worker 已实现 |
| I5 | 部分完成 | 编辑器、参数编辑和拓扑校验可用；撤销/重做未实现 |
| I6 | 后端完成、前端未闭环 | 会话/token 上传/SSE/捕获页存在；编辑器无 QR 入口 |
| I7 | 已取消 | 2026-07-23 用户取消当前阶段视觉模块接入；技术方案仅保留为未来研究参考 |
| I8 | 未完成 | 当前 3/30 个 Benchmark |
| I9 | 部分完成 | KaTeX、步骤展示和 HTML 报告可用；PDF/LaTeX/Circuitikz 未实现 |

完整证据见 `projects/technical-document-review-20260716.md`。

## 背景

项目已经通过 `/plan-eng-review`，第一阶段范围调整为 **本地优先的结构化电路分析闭环**：30 题 benchmark、类型化电路图、人工拓扑校正、基础模电分析、LaTeX/步骤输出，以及局域网手机上传。视觉识别接入已于 2026-07-23 取消。

已验证依据：

- 范围已在 `projects/wzh-no-git-worktree-eng-review-20260529-021023.md:12` 中收窄为本地优先 MVP。
- 类型化电路图是 `projects/wzh-no-git-worktree-eng-review-20260529-021023.md:27` 中的唯一事实来源。
- 2026-05-29 评审时目录没有前端、后端或测试框架；该描述是历史基线，不再代表当前状态。

## 子 Issue

| ID | 标题 | 优先级 | 范围 |
|---|---|---:|---|
| I1 | 初始化 TypeScript/Zod schema、元件库和 benchmark 样例 | P1 | 第一个可执行 issue |
| I2 | 实现第一批分析流程策略 | P1 | 依赖 I1，技术论证详见 `projects/analysis-engine-technical-report.md` |
| I3 | 加入求解器限制和警告传递 | P1 | 依赖 I2，三重保护机制详见技术论证报告 |
| I4 | 构建本地 API、任务队列和 worker 循环 | P1 | 依赖 I1 |
| I5 | 构建电路图编辑器外壳和拓扑校正 | P2 | 依赖 I1，技术论证详见 `projects/presentation-layer-technical-report.md` |
| I6 | 加入局域网手机上传流程 | P2 | 依赖 I4，QR+SSE 方案详见技术论证报告 |
| I7 | 加入 YOLOv8 + PaddleOCR 图像到候选拓扑流程 | 已取消 | 不纳入当前阶段交付；下方内容仅作历史方案参考 |
| I8 | 将 benchmark 从 3 个样例扩展到 30 题 | P1 | 依赖 I1/I2，电路图使用人工结构化录入 |
| I9 | 将结构化分析渲染为 LaTeX、解释和标注 | P2 | 依赖 I2，KaTeX 渲染 + 报告导出详见技术论证报告 |

## 依赖图

```text
I1 数据模型与元件库
├─> I2 分析流程策略 ──> I3 求解器限制
│                    └─> I9 LaTeX/解释/标注渲染
├─> I4 本地 API/任务队列 ──> I6 手机上传
├─> I5 编辑器外壳与人工拓扑校正
└─> I8 30 题 benchmark 扩展（人工结构化录入）

I7 视觉识别：已取消，不再参与当前依赖图
```

## 完成定义

1. 本地应用可以用类型化图表示教材电路题。
2. 元件定义驱动端口、参数、教学标签和后续分析映射。
3. Benchmark 用例使用与编辑器和分析模块相同的图格式。
4. 第一阶段范围保持不变：不做云端、不做账号系统、不做完整 EDA、不承诺任意电路。

---

# Issue I1：初始化 TypeScript/Zod Schema、元件库和 Benchmark 样例

## 背景

这个 issue 创建整个项目的基础。后续分析、编辑器、视觉和 benchmark 工作都依赖同一套共享数据形状。没有这一层，后续模块会各自发明不兼容的图格式。技术方案论证详见 `projects/data-layer-technical-report.md`。

## 当前状态

I1 已完成：根目录已有 package manifest；`packages/schema`、`packages/components`、`benchmarks/examples`、校验脚本和自动化测试均已存在。当前工作区仍不是 Git 仓库。

## 拟议变更

初始化项目骨架并实现第一层共享数据层：

```text
package.json
tsconfig.json
packages/schema/
packages/components/
benchmarks/examples/
scripts/validate-benchmarks.ts
```

使用 **TypeScript + Zod 作为事实来源**，并导出 JSON Schema 供 Python 和后续服务使用。

## 数据模型

核心 schema 必须定义：

- `CircuitGraph`：`id`、`version`、`nodes`、`components`、`ports`、`connections`、`metadata`、`validationIssues`
- `ComponentInstance`：`id`、`definitionId`、`label`、`ports`、`parameters`、`modelVariant`、`position?`
- `ComponentDefinition`：`id`、`family`、`ports`、`parameters`、`allowedModelVariants`、`analysisTags`、`teachingTags`
- `BenchmarkCase`：`id`、`title`、`source`、`problemText`、`imageRefs`、`circuitGraph`、`groundTruth`、`difficultyTags`

## 首批元件库

包含这些元件族：

- 无源元件：电阻、电容、电感、变压器
- 独立源：直流电压源、直流电流源、交流小信号电压源、交流小信号电流源
- 受控源：压控电压源、压控电流源、流控电压源、流控电流源
- 二极管：二极管
- BJT：NPN、PNP
- FET 家族：增强型 MOSFET、耗尽型 MOSFET、JFET

注意：JFET 不应建模为 MOS 子类型；它属于更宽泛的 FET 家族。

## Benchmark 样例

创建 3 个完整样例：

1. 二极管限幅或钳位电路
2. BJT 共射静态工作点
3. MOS 共源基础放大电路

本 issue 暂不创建占位图片路径，也不验证教材图片。`imageRefs` 字段必须先保留真实教材图片引用能力，但具体图片引用、裁剪区域和识别验证要等用户提供教材照片后再补齐。

## 验收标准

1. `npm test` 验证所有 schema 单元测试。
2. `npm run validate:benchmarks` 验证全部 3 个 benchmark 样例。
3. 非法图会针对缺失端口、悬空节点、重复 id 和非法参数给出可操作错误。
4. 元件定义包含端口、参数单位、必填/可选标记、模型变体、分析标签和教学标签。
5. 从 Zod 导出 `CircuitGraph`、`ComponentDefinition` 和 `BenchmarkCase` 的 JSON Schema artifact。
6. 本 issue 不实现云端、账号、UI 编辑器、图像识别或分析求解器。

## 测试计划

| 层级 | 测什么 | 数量 |
|---|---|---:|
| 单元测试 | Zod schema 接受合法的图、元件和 benchmark 对象 | +6 |
| 单元测试 | Zod schema 拒绝重复 id、缺失端口、悬空连接和非法值 | +8 |
| 集成测试 | 从磁盘验证所有 benchmark 样例 | +1 |
| 契约测试 | 导出 JSON Schema artifacts 并验证文件存在 | +1 |

## 回滚计划

如果这个基础设计被证明不合适，在下游模块依赖它之前回滚 package 骨架和 schema 文件。本 issue 不引入持久用户数据或外部服务状态。

## 工作量估算

人工：约 2-3 天。  
AI agent：约 6-10 小时。

拆分：1 小时项目骨架，3 小时 schema 设计，2 小时元件定义，2 小时 benchmark 样例，2 小时测试和校验脚本。

## 不在范围内

- 真实教材图片识别
- 完整 30 题 benchmark
- 分析引擎
- Web 编辑器
- 手机上传
- SPICE/Lcapy/SymPy 集成

## 执行备注

- 本次 `/spec` 按本地文件归档处理：当前目录不是 git 仓库，且 `gh` 未安装，因此没有创建 GitHub issue。
- 质量门的密钥/敏感串检查已通过。
- `codex.exe` 质量评分在沙箱内和提权后均返回 `Access is denied`，因此按 `/spec` 规则跳过质量门。

---

# Issue I7：加入 YOLOv8 + PaddleOCR 图像到候选拓扑流程（已取消）

> 状态：2026-07-23 取消接入，不纳入当前阶段实施、交付或验收。以下内容仅保留为历史技术方案参考。

## 背景

本 issue 原计划实现视觉管线，将教科书电路图照片转换为带置信度的候选拓扑 `CircuitGraph` JSON。技术方案已于 2026-06-12 完成论证，详见 `projects/yolo-paddleocr-circuit-recognition-technical-report.md`，但当前阶段不再实施。

原工程评审（2026-05-29）中 Decision 2 描述为"传统图像处理 + 轻量目标检测 + OCR 辅助"，后曾细化为 **YOLOv8s + PaddleOCR PP-OCRv4 + OpenCV** 三层架构。该历史方案已取消，不再产生 `services/vision` 模块，也不影响其他 Issue 的实现。

## 当前状态

- I1 已完成：`packages/schema` 定义了 `CircuitGraph`、`ComponentInstance` 等核心类型。
- I7 已取消，不创建 `services/vision`，不接入 API Worker 或编辑器。
- 仓库无训练数据集、领域模型、视觉推理代码或项目精度报告。

## 技术方案概要

### 三层架构

```text
输入图像 (JPG/PNG)
    │
    ▼
┌─────────────────────┐
│ OpenCV 预处理层      │  灰度化 → 自适应二值化 → 形态学去噪 → 透视校正
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ YOLOv8 目标检测层    │  CSPDarknet(C2f) + PAN-FPN + Decoupled Anchor-Free Head
│ (~6 MB ONNX)        │  输出: bbox + 类别(15-19类) + 置信度
│ CPU: ~100-150ms/帧   │  mAP@0.5 预期 ≥95%
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ PaddleOCR 参数识别层  │  DB++ 文本检测 + SVTR_HGNet 文本识别
│ (~15 MB ONNX)        │  输出: 元件标注文本 (如 "R1=10kΩ")
│ CPU: ~150-300ms/帧   │  准确率预期 ≥90%
└──────────┬──────────┘
           ▼
┌─────────────────────┐
│ OpenCV 拓扑提取层     │  FLD 线段检测 + 节点聚类 + CircuitGraph 构建
└──────────┬──────────┘
           ▼
  CircuitGraph JSON (对齐 packages/schema)
```

### 关键技术选型理由

| 决策 | 选型 | 理由 |
|------|------|------|
| 元件检测 | YOLOv8s | PCB 检测文献 mAP 93%–97%；COCO 预训练微调仅需 200–500 张；CPU 推理 ~100–150ms |
| 参数 OCR | PaddleOCR PP-OCRv4 | 中英文混排 SOTA；模型仅 ~15MB；可定制电路领域字典微调 |
| 统一推理 | ONNX Runtime | 避免同时安装 PyTorch + PaddlePaddle；总模型 ~40MB |
| 拓扑提取 | OpenCV FLD | 快速线段检测 + 共线段合并；配合节点聚类提取连接关系 |

## 拟议目录结构

```text
services/vision/
├── models/                    # ONNX 模型文件
│   ├── yolov8s_circuit.onnx  # 微调后的 YOLOv8s (~6MB)
│   ├── ppocrv4_det.onnx      # PaddleOCR 文本检测 (~3.5MB)
│   └── ppocrv4_rec.onnx      # PaddleOCR 文本识别 (~10.5MB)
├── dict/
│   └── circuit_dict.txt       # 电路参数专用字典 (Ω/β/μ 等)
├── src/
│   ├── preprocess.py          # OpenCV 预处理
│   ├── detector.py            # YOLOv8 推理 + 后处理
│   ├── ocr.py                 # PaddleOCR 推理 + 字典微调
│   ├── topology.py            # FLD 线段检测 + 节点聚类
│   ├── param_parser.py        # 参数文本解析 + 规则纠正
│   ├── pipeline.py            # 端到端管线串联
│   └── types.py               # 检测结果类型定义 (与 schema 对齐)
├── training/
│   ├── data.yaml              # YOLO 训练配置
│   ├── train_yolo.py          # YOLOv8 微调脚本
│   ├── train_ocr.py           # PaddleOCR 微调脚本
│   └── export_onnx.py         # ONNX 导出脚本
├── data/
│   ├── train/                 # 训练集 (images + labels)
│   ├── val/                   # 验证集
│   └── circuit_dict.txt       # 电路参数字典
├── tests/
│   ├── test_preprocess.py     # 预处理单元测试
│   ├── test_detector.py       # 检测器单元测试
│   ├── test_ocr.py            # OCR 单元测试
│   ├── test_topology.py       # 拓扑提取单元测试
│   └── test_pipeline.py       # 端到端管线集成测试
└── requirements.txt           # Python 依赖
```

## 子任务分解

| 子任务 | 描述 | 工时 | 前置依赖 |
|--------|------|------|----------|
| T8a | 冻结 16 个视觉类到 18 个元件类型的映射，在 50–100 张标注图上完成 YOLOv8n/s 微调 POC | 2–4 天 | 无 |
| T8b | 收集 200–500 张教科书电路图，分别完成 YOLO 元件标注与 PaddleOCR 文字标注 | 1–2 周 | 无 |
| T8c | YOLOv8s 微调训练 + ONNX 导出 | 3–5 天 | T8a, T8b |
| T8d | PaddleOCR v6 基线评测、字符字典核对与识别模型微调 | 3–5 天 | T8a |
| T8e | OpenCV 预处理管线 + FLD 线段检测 + 节点聚类 | 1 周 | 无 |
| T8f | 三模块串联 + 参数后处理 + 规则纠正 | 1 周 | T8c, T8d, T8e |
| T8g | 端到端管线联调 + 10 张基准题测试 | 3–5 天 | T8f |

## 验收标准

1. YOLOv8 元件检测 mAP@0.5 ≥95%（在验证集上）。
2. PaddleOCR 元件参数识别准确率 ≥90%（在验证集上）。
3. 节点连接识别准确率 ≥85%。
4. 低置信度 / 可疑点召回率 ≥90%。
5. 端到端推理速度：单图全管线在 CPU 上 ≤2 秒。
6. 输出的候选拓扑 JSON 符合 `packages/schema` 的 `CircuitGraph` 类型定义。
7. 10 张基准题中至少 8 张能正确识别出主要元件和拓扑结构。

## 测试计划

| 层级 | 测什么 | 数量 |
|------|--------|------|
| 单元测试 | OpenCV 预处理输出格式正确 | +3 |
| 单元测试 | YOLOv8 检测输出 bbox + 类别 + 置信度 | +4 |
| 单元测试 | PaddleOCR 识别输出文本 + 置信度 | +3 |
| 单元测试 | 参数解析器正确提取元件 ID、值、单位 | +5 |
| 单元测试 | 拓扑提取输出有效的 CircuitGraph | +3 |
| 集成测试 | 端到端管线对 10 张基准题的识别精度 | +1 |
| 性能测试 | 单图推理速度 ≤2 秒 (CPU) | +1 |

## 风险与缓解

| 风险 | 缓解策略 |
|------|----------|
| 训练数据不足 | 合成数据生成 (KiCad → YOLO 标注)；Mosaic/MixUp 增强 |
| 数学符号 OCR 错误 | 电路领域字典微调；后处理规则纠正 (R→Ω, C→F) |
| 导线检测不准 | FLD + 共线段合并；用户交互确认兜底 |
| Windows GPU 不兼容 | 使用 CPU 推理；ONNX Runtime 替代原生框架 |

## 工作量估算

总工时：7–10 周。其中数据集构建 1–2 周，模型训练 3–4 周，管线集成 3–4 周。

## 不在范围内

- 手写电路图识别（远期目标）
- 实时视频流识别
- 云端推理
- 任意风格电路图（仅支持教科书印刷体）

## 执行备注

- 本 issue 需要 Python 环境，与 I1 的 TypeScript 环境独立。
- 模型文件不纳入 git 版本控制，通过 `.gitignore` 排除 `services/vision/models/`。
- 技术论证详见 `projects/yolo-paddleocr-circuit-recognition-technical-report.md`。
