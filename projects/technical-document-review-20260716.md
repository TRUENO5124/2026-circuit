# 技术文档复核与调试报告

> 首次复核：2026-07-16；范围更新与复测：2026-07-23  
> 复核范围：数据层、分析引擎、展示层、视觉识别方案、第一阶段规格、启动与测试入口  
> 事实来源：当前工作区源码与本轮自动化检查；API/HTTP 证据注明历史验证日期

## 1. 结论

项目已经形成“编辑器 -> 本地 API -> Worker -> 分析引擎 -> 结构化结果”的基础闭环，但原技术文档混合了已实现能力、设计方案和远期目标。复核后，当前状态归纳如下：

| 模块 | 状态 | 证据 |
|---|---|---|
| 数据层 | 已实现第一阶段 | 18 类元件定义、3 个 Benchmark、Zod 校验、3 份 JSON Schema；15 项测试通过 |
| 分析引擎 | 已实现基础题型 | 5 个策略、SymPy MNA、安全求解器；16 项测试通过 |
| 本地 API | 已实现基础闭环 | FastAPI、SQLite、任务 Worker、会话/上传/SSE；16 项测试和真实 HTTP 任务于 2026-07-16 通过，本轮未复跑 |
| 编辑器/展示 | 部分实现 | React Flow、参数编辑、校验、分析轮询、KaTeX、HTML 报告；生产构建通过 |
| 视觉识别 | 当前阶段取消接入 | 2026-07-23 用户取消视觉模块接入；`services/vision`、训练数据、领域模型和推理管线均不存在 |
| 30 题 Benchmark | 未完成 | 当前仅 3 个样例 |

## 2. 本次发现并修复的问题

### 2.1 总检查漏掉编辑器和 Python 模块

原 `npm run check` 排除了 `packages/editor`，也不运行 Python 测试，因此在总检查全绿时编辑器仍有 6 个 TypeScript 构建错误，引擎 pytest 也无法收集测试。

修复：

- 将编辑器生产构建、引擎测试和 API 测试纳入根 `check`。
- 为 SVG 路径 helper 补齐并转发 `fillOpacity`。
- 加入 Vite 客户端类型声明，使 `?raw` CSS 导入可被 TypeScript 识别。
- 将测试脚本的 UTF-8 输出配置移到 `__main__`，避免导入阶段替换并关闭 pytest 的捕获流。

### 2.2 编辑器与 API 的任务字段不一致

FastAPI 返回 `id/finished_at`，编辑器却读取 `task_id/completed_at`。用户点击分析后会得到 `undefined`，随后永久轮询 `/api/tasks/undefined`。

修复：

- 前端任务类型与 API 模型统一为 `id/finished_at`。
- 工具栏复用类型化 API 客户端，不再维护第二套裸 `fetch` 契约。
- 对“completed 但没有 result”的损坏响应显式显示失败。
- API 测试锁定提交响应必须包含非空 `id`。

## 3. 文档与实现偏差

### 3.1 数据层报告

已实现：TypeScript/Zod 事实来源、节点-元件-连接模型、元件定义、图校验、Benchmark、JSON Schema 导出。

偏差：Python 端当前使用 dataclass 和手动 JSON 解析，不是报告示例中的 Pydantic 对称模型或自动生成模型；尚无 TypeScript/Python 双向契约测试；教材图片引用仍为空。

### 3.2 分析引擎报告

已实现：插件式策略、BJT DC/AC、MOS DC/AC、二极管分析、MNA、SymPy 安全求解、结构化步骤/公式/知识点。

偏差：Lcapy 和 PySpice 仅在依赖说明中列为可选项，没有进入执行路径；“覆盖所有元件”是 MNA 方法能力论证，不代表当前策略覆盖；数值仿真交叉验证尚未实现。

### 3.3 展示层报告

已实现：React + TypeScript + React Flow、18 类元件符号、拖拽/连线/参数编辑、图校验、任务轮询、KaTeX、HTML 报告。

偏差：实际后端是 FastAPI，不是文中架构图的 Express；会话上传使用 SSE，不是 WebSocket；手机上传后端与捕获页存在，但编辑器没有桌面端 QR 入口；撤销/重做、等效电路渲染、PDF、LaTeX 和 Circuitikz 导出尚未实现。

### 3.4 视觉识别报告

该文档是方案论证，不是实现报告。YOLO/PaddleOCR 的精度数字来自外部研究或目标值，不能作为本项目验收结果。2026-07-23 用户取消当前阶段的视觉模块接入；仓库没有 `services/vision`、训练/验证数据、领域权重、推理/拓扑管线或项目精度测量。

## 4. 验证证据

### 4.1 2026-07-23 本轮复测

- TypeScript 类型检查通过。
- 15/15 TypeScript Schema/元件测试通过。
- 16/16 分析引擎测试通过（7 项 MNA/安全求解器 + 9 项分析管线）。
- 编辑器生产构建通过，220 个模块完成转换；主包约 704 kB（gzip 约 216 kB）的警告仍存在。
- 3/3 Benchmark 校验通过。
- 3/3 JSON Schema 成功导出。
- API/Worker 测试未在本轮复跑：当前 `D:\anaconda3\python.exe` 未安装 FastAPI。下方 API 与 HTTP 结果仅为 2026-07-16 的历史验证证据。

### 4.2 2026-07-16 历史验证

2026-07-16 执行 `npm run check`，结果：

- 15/15 TypeScript Schema/元件测试通过
- 16/16 引擎测试通过
- 16/16 API/Worker 测试通过
- 编辑器生产构建通过，220 个模块完成转换
- 3/3 Benchmark 校验通过
- 3/3 JSON Schema 成功导出

真实 HTTP 流程通过：提交 `bjt-common-emitter-bias.json`，Worker 返回 `completed`，结果类型为 `bjt_dc_bias`，包含 8 个步骤和 3 个方程。

## 5. 剩余风险

1. 浏览器视觉回归未完成。项目服务可访问，但本机 gstack browse daemon 无法启动，且工作区未安装 Playwright/Puppeteer；不能据此声称交互布局已在桌面和移动端验证。
2. 编辑器主 JavaScript 包约 704 kB（gzip 约 216 kB），Vite 给出大于 500 kB 的警告；功能正确，但后续应拆分 React Flow/KaTeX 等依赖。
3. API 任务接口没有用户认证，只适用于本地可信网络；手机上传使用会话 token，但不能替代整套 API 的访问控制。
4. 当前 3 个 Benchmark 只能证明基础题型链路，不能证明原规格中的 30 题覆盖率；视觉识别指标已随模块取消而退出验收范围。

## 6. 下一阶段建议

1. 先补浏览器端端到端测试，锁定“加载 Benchmark -> 校验 -> 分析 -> 展示/导出”主流程。
2. 将 API OpenAPI 契约生成 TypeScript 类型，消除手写字段再次漂移的风险。
3. 扩展至 30 题前，按题型建立覆盖矩阵和预期误差容限。
4. 不再把视觉模块列入当前阶段交付或验收；相关技术报告仅作为未来研究参考。
