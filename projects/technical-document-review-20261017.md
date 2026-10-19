# 当前工作区代码与开发文档一致性审查

> 日期：2026-10-17（Asia/Shanghai）  
> 性质：代码修改前的审查基线；本轮修订状态见[代码修订记录](technical-code-fixes-20261017.md)  
> 主目标：按 WinUI 软件修正开发说明  
> 基线：当前工作区源码，包含用户已有未提交和未跟踪 C# 文件  
> 变更范围：开发文档；没有修改应用/测试代码、Git 历史或发布状态  
> 后续：第二轮能力增量与复审见[第二轮修订记录](technical-code-fixes-20261017-round2.md)与[第二轮审查报告](technical-document-review-20261017-round2.md)；本文保留为历史证据，不再代表当前实现状态。

## 1. 结论

当前主软件是 WinUI 3 / XAML / C# / .NET 9，Win2D 绘制原理图，C# 进程内完成网表提取、分类与策略求解。早期“HTML/React 界面 → FastAPI → Python 符号求解”的架构仍存在为独立原型，不能作为主客户端架构。

本次不止替换技术名词：同步了三层报告正文、架构图、模型/接口、功能与支持范围、文件读写、启动脚本行为、测试命令、任务状态、术语和跨文档导航。旧方案完整归档，原始需求与视觉研究方案保留，视觉开发安排在后续阶段。

构建与现有测试通过；静态审查同时确认若干尚未解决的求解与工程状态问题，已写入文档边界。不能据此宣称全部工业 EDA 验收完成。

## 2. 审查范围与证据方式

### 2.1 软件模块

核对了原生项目配置、启动/异常入口、MainWindow 导航、工作台 XAML/事件、ViewModel 的筛选/编辑/工程/历史/求解链路、元件/引脚/导线与路径、工程 DTO/文件服务、模板、网表/分类/四策略、公式/数字格式化和 C# 测试项目。

还核对根 package scripts、Web 编辑器状态/API/手机/导出入口、Zod/元件/Benchmark 工具、Python 注册器/求解器、FastAPI 路由/队列/Worker 和批处理启动脚本，以避免把原型功能写为原生能力或把现有原型功能漏写为未实现。

证据分为源码静态确认和本轮实际构建/测试。未运行真实 WinUI 窗口、手机扫码、浏览器视觉回归、外部 SPICE 仿真或 TeX 导出编译。

### 2.2 文档逐份处理

| 文件/材料 | 原内容检查与处理 |
|---|---|
| `README.md` | 修正主引擎归属、依赖、功能数、公式能力、启动/检查与目录；增加开发入口 |
| `PROJECT.md` | 重绘 C# 数据流；校正接口、WireTap/纯母线契约、策略回退、布局数值和状态边界 |
| `HANDOVER.md` | 修正“清除所有 Web”“每次脚本自动构建”“持久化未开发”等；补实际文件命令和限制 |
| `CLAUDE.md` | 增补当前主客户端、检查入口和文档规则，保留已有技能路由 |
| `专业名词解释.md` | 修正前端/后端、数据事实来源、SPICE、SymPy、Benchmark、E2E；标明视觉术语为历史 |
| 三层 technical-report | 重写全部现行正文为 WinUI/C#，保留数据/校验、求解/保护、交互/公式/手机/导出各主题的现状与缺口 |
| `wzh-...design...md` | 更新产品流程、已定客户端与设计目标，不再把照片识别和前端选型当现行目标 |
| `wzh-...eng-review...md` | 对照原十三项决策、代码目录、T1–T10 与失败模式，替换失效架构/工具链结论 |
| `wzh-...eng-review-test-plan...md` | 替换虚构页面路由/视觉验收，列出 C# 与原型已测结果和待执行验收 |
| `spec-first-stage-...md` | 更新 I1–I9、M1–M4、完成定义/依赖和后续任务，移出“当前无 Git”等历史环境结论 |
| `technical-document-review-20260716.md` | 保留为历史入口，链接原文与新报告，避免旧计数和缺口继续误导 |
| `projects/README.md` | 新增现行、历史、第三方/申报材料的统一导航 |
| YOLO/PaddleOCR 报告 | 方案保留，后续阶段开发；当前无模型或精度交付 |
| 教材图片采集交接 | 作为后续视觉开发的数据准备说明，当前不启动采集 |
| `ORIGINAL_REQUEST.md` | 原始验收目标，保持原文；当前完成范围由本报告/规格说明 |
| `projects/archive` 与任务 JSONL | 历史证据，不篡改为“已完成”；新增八份修订前原文副本 |
| `files` 教材/申报/开题/PPT/PDF 及生成材料 | 属于教学和正式历史材料，不是本次现行开发说明，未重写 |
| 第三方符号说明/许可 | 核对与原生实现的关系，保留上游说明和许可原文 |
| `.agents/.omo` 内部执行记录 | 不作为开发手册，不改写运行历史 |

## 3. 技术内容逐项比对

| 主题 | 旧文偏差 | 当前源码与文档修订 |
|---|---|---|
| 主 UI | React/HTML/React Flow | XAML + WinUI 3 + Win2D；原型单列 |
| 页面导航 | React Router、editor/results/jobs 页面 | MainWindow 直接 Frame 导航 AnalysisStudioPage |
| 语言/计算链 | TS/Python 为统一前后端 | C# 进程内求解；原型两套模型/链路并存 |
| 桌面包装 | Electron/Web 视图或待选 | 当前为原生非打包 WinUI 工程，没有该包装执行路径 |
| 包版本 | 固定/“最新” SDK 与 MVVM | Win2D 1.4.0，主项目其余四项 Version=* |
| 系统环境 | Python/Node 作为桌面必备 | 桌面依赖 .NET/Windows 构建与运行环境；Python/Node 维护原型 |
| 一键启动 | start-winui 总是构建最新代码 | 优先启动已存在 win-x64/Debug exe；修改后先构建 |
| start-all | 启动完整主软件 | 仅启动 API 3000 + Vite 5173 |
| 求解服务 | FastAPI 主解算/本地备用 | 主按钮 C# Extract/Classify/Solve；旧服务类仅残留 HTTP/静态共射预览 |
| API 契约 | 8000/api/v1/analyze 与当前 API 等价 | 残留客户端和实际 `/api/tasks/submit` 不兼容 |
| 编辑模型 | Zod CircuitGraph 全系统唯一 | 原生 CircuitElement/Pin/Wire、工程 DTO、ElectricalNetlist 分层 |
| 跨语言 | 自动生成 Pydantic 镜像/完整契约测试 | Python dataclass 手动解析；没有原生适配或完整双向契约测试 |
| 符号数 | 全系统 18/19 类 | 原生 25 条符号；Web 18 个定义；不等于全部可解算 |
| FET 引脚 | 原生直接沿用四端 MOS 数据 | 原生默认 G/D/S 三引脚，需额外转换 |
| 模板 | 无共基极/模板逻辑全在 ViewModel | 工厂含四种教学模板和空白；`.sch` 是显示名 |
| 导线数据 | 每根都绑定两个元件 | 允许自由母线、普通引脚、WireTap 父线引用 |
| 坐标公式 | `(world+translation)*scale` | `world*scale+translation` |
| 网格单位 | 40px 等于 80mil | 内部显示坐标，无物理 mil 换算 |
| 焊点 | 半径 3.8 | 半径 1.9、直径 3.8、引脚净空 5 |
| 旋转/撤销 | R/部分文档称未实现撤销 | Space/R；原生 50 快照，Web 也已有历史栈；不是全操作撤销 |
| 持久化 | 保存/打开还在待办 | 自有 JSON、文件选择器和 DTO 已有；完善状态/校验仍待办 |
| 文件兼容 | `.sch`/`.json` 自动互通 | 加载器只读自有 DTO，第三方/Web JSON 无适配 |
| 原生 RC/RL | 任意端口精确 Req | 多电阻直接相加、零初值、统一低通公式，范围有限 |
| 原生 RLC | 任意二阶网络精确求解 | 首个 R/L/C 标准串并联公式、近似截止频率 |
| 原生 BJT | 通用晶体管/无写死参数 | 首个 BJT、拓扑+ID 启发式/默认值/固定模型参数 |
| MOS/JFET | 已完整小信号求解 | 分类后回退基础 MNA，缺对应印记 |
| 原生 MNA | 所有线性与 SPICE 元件 | 仅电阻和独立电压源装配；奇异处理不可靠 |
| 校验/失败 | 非法电路全部阻止、开路可分类 | Validate 未接入口；分类器未产出 Disconnected/OpenCircuit |
| 原生求解保护 | 已有符号超时/复杂度/仿真回退 | Task.FromResult 同步计算，无通用取消/超时 |
| Python 策略 | 五个 | 当前六个，含 LinearAnalysisStrategy |
| Lcapy/PySpice | 已作为计算核心/数值校验 | 没有执行路径，SymPy 实际使用，原生不依赖 Python |
| 结果模型 | 完整 DC/AC 等效图与表达式树 | 原生步骤/Q 点/交流文本/日志；未生成完整等效图 |
| 数学引擎 | KaTeX/MathML 原生渲染 | 常见 LaTeX 转 Unicode TextBlock、复制原式 |
| 科学计数 | 全部数值自动规范化 | 仅 formatter 调用处；仍有 F2/F3，极小值归零 |
| 原生报告 | PDF/Word/LaTeX/Circuitikz 已支持 | 只有公式复制和有限 SPICE 文本；报告功能未实现 |
| Web 导出 | LaTeX/Circuitikz 全未实现 | 已有生成下载与 JSON/HTML；未编译验证导出内容 |
| 手机入口 | 无 QR 或已接原生端 | Web MobileCaptureModal 已有，原生未接入；真实手机链路待测 |
| 手机实时通知 | Express/WebSocket/Socket.IO | 实际 FastAPI/SSE，捕获页响应类型有问题 |
| 视觉 | 开发时序表述错误 | 后续阶段开发，当前先完善软件本体，无训练/权重/识别流水线 |
| Benchmark | 30 题及精度已完成 | 当前 3 个原型样例、imageRefs 为空，未提供原生转换与全量精度 |
| 验证 | npm check 覆盖桌面/全零警告/全 E2E | C# 与原型分开；有测试警告；本轮无真实窗口验收 |

各项在[架构](../PROJECT.md)、[数据层](data-layer-technical-report.md)、[引擎](analysis-engine-technical-report.md)和[展示层](presentation-layer-technical-report.md)给出字段、函数与实现位置。

## 4. 代码修改前的审查发现

以下不是新需求猜测，而是当前执行代码与旧文能力承诺的直接差异。这些是首次审查时的状态。用户随后授权修改代码；实现变更、后续授权的运行验证与剩余边界见[代码修订记录](technical-code-fixes-20261017.md)。

| 级别 | 发现与影响 | 代码证据 |
|---|---|---|
| 高 | 打开 A 后新建 B 没清 CurrentProjectFilePath；随后 SaveProjectAsync 可写回 A，存在覆盖旧工程风险 | `AnalysisStudioViewModel.CreateNewProject/SaveProjectAsync` |
| 高 | MNA 忽略电流源、受控源和多类器件；奇异矩阵跳过小主元、回代赋 0，仍可能成功 | `Engine/Strategies/MnaLinearStrategy.SolveAsync/SolveGaussian` |
| 高 | 求解入口未调用 Validate，开路枚举未由分类器生成；无效/不支持网络可能进入策略计算 | `SolveCircuitAsync`、`ElectricalNetlist.Validate`、`TopologyClassifier.Classify` |
| 中 | RC 多电阻求和并固定低通公式；HighPassFilter 分类和公式不一致，不能解一般端口/加载 | `FirstOrderRcStrategy.SolveAsync`、`DetectFirstOrderRcSubtype` |
| 中 | BJT 有缺失参数默认值、按 ID 找 Ce、固定高频参数与首个器件假设；PNP/多级范围不足 | `BjtAmplifierStrategy` |
| 中 | 工程 DTO 丢失 Description/Value/宏单位/原 CreatedAt；缺严格校验/版本迁移/原子写入 | `CircuitProjectData.From/ToModel`、`ProjectManagerService` |
| 中 | 新建/切换工程未统一清理历史、路径和结果，IsProjectModified/未保存提示不完整 | `AnalysisStudioViewModel` 工程与编辑命令 |
| 中 | AC 源 DisplayNumber/UnitSymbol 同时承载幅值和频率语义；参数宏可能回写同一元件 | `BuildAnalysisParameters`、`OnFreqNumberChanged`、`CircuitTemplateFactory` |
| 中 | 求解结束无条件设置“求解成功”，没有统一按 solution.IsSuccess 生成状态；同步策略无通用超时 | `SolveCircuitAsync`、四个 `SolveAsync` |
| 中 | 原型捕获页返回字符串，未声明 HTMLResponse；接口测试只检查文本，不能证明浏览器正确呈现 | `services/api/main.py:capture_page`、`test_api.py` |
| 低 | Unicode 公式覆盖有限，替换顺序和文本矩阵/长公式仍需验证；不是完整排版 | `MathFormulaView.FormatLatexToUnicode` |
| 低 | 类别列表漏“半导体管”而二极管属于该类；仍能从“全部”或搜索添加 | `InitializeSymbols/SymbolCategories` |

旧的 80px 位移误并网风险已按当前代码和 M3 两组测试重新核对：本轮对应模型回归通过，不再沿用“仍未修复”的旧结论。范围仍限测试场景，真实画布交互未复测。

## 5. 代码修改前的历史验证结果

以下结果来自前一轮文档审查时，尚未修改软件代码。用户随后授权程序与测试，修改后结果另见[本体修订记录](technical-code-fixes-20261017.md)；不得以此表作为修改后通过证据。原检查命令退出码均为 0：

| 命令 | 结果 |
|---|---|
| `dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore --verbosity minimal` | 成功，0 警告，0 错误 |
| `dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj --verbosity quiet` | 108/108 |
| `dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj --verbosity quiet` | 131/131；CS1998/CS8602 警告 |
| `dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj --verbosity quiet` | 32/32；CS1998 警告 |
| `dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj --verbosity quiet` | 9/9；包括 132/132 位移组合保持节点 |
| `npm run check` | 全链路通过，细分见下 |

C# 合计 280 项断言；不等于 280 道教材题，更不等于真实窗口 E2E。位移组采用测试辅助路径，不能证明所有画布操作分支都一致。

原型检查：16 个 TS 测试、7 个 Python MNA/安全求解测试、10 个 Python 管线测试、16 个 API/Worker 测试、Web 生产构建、3 个 Benchmark 校验、3 份 Schema 导出均通过。

Web 构建 2123 模块，主 JS 874.55 kB（gzip 260.69 kB），仍有超过 500 kB 警告；API 测试有 Starlette/httpx 弃用警告。历史 704 kB/15 项 TS/16 项引擎等数值已移入历史报告，不作本轮结果。

## 6. 未验证与后续验收

- 未运行真实窗口中的文件选择器、拖拽/框选/快捷键、分支、撤销、工程切换和关闭提示。
- 未做 Windows 最低版本、不同 DPI/字体/主题、公式溢出及整体视觉检查。
- 未做手机扫码浏览器、Web 导出文件排版/TeX 编译或外部 SPICE 数值对照。
- 未验证任意拓扑的分析正确率；3 个 Benchmark 不能证明 30 题目标或历史百分比阈值。

具体待验收操作列在[测试计划](wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md)，待开发项列在[规格](spec-first-stage-local-circuit-analysis-20260601.md)。文档记录缺口不代表本次已修复代码。

## 7. 文档与工作区完整性

现行文档统一从[开发索引](README.md)进入。此次替换前的八份论证/设计/规格/复核均按原内容复制到[归档](archive/2026-10-17-before-winui-doc-sync/README.md)，保留研究参考与历史证据。

完成后已检查 15 份现行 Markdown 的本地链接和代码围栏，均通过；八份归档与修改前原文比对一致。文档范围 `git diff --check -- '*.md'` 通过。任务开始时的 19 个已有代码改动/新增文件内容哈希全部保持不变。全工作区空白检查还报告既有 `CircuitTemplateFactory.cs` 文件末尾空行，本次未修改该代码。正式申报资料、原始验收目标与上游许可不作现状改写。
