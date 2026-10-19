# 第二轮实现后审查：能力增量、证据与遗留边界

> 日期：2026-10-17（Asia/Shanghai）  
> 性质：按第一轮审查所列"软件未能实现的能力"完成第二轮实现后的复审；不是代码修改前基线。  
> 后续：第三轮（2026-10-18）已按用户新要求增加单只 PNP 分析、硅/锗压降参数与按国际标准统一的元件符号，见[第三轮修订记录](technical-code-fixes-20261018-round3.md)；本文保留第二轮历史证据。
> 基线：当前工作区源码（含未提交改动与新增测试工程）；实现变更见[第二轮修订记录](technical-code-fixes-20261017-round2.md)。  
> 范围：WinUI 3 主软件本体（引擎/调度/报告/导入/UI 接线）、新增控制台回归工程与现行开发文档。  
> 不在范围：正式申报材料、Git 历史/发布、手机与视觉识别实现（仍为后续阶段）。

## 1. 结论

第二轮把第一轮记录中"最能被自动化证伪"的能力缺口推进到可运行、可复现证据的状态：

1. **通用线性网络 MNA 2.0**：R/C/L、独立 DC/AC 电压/电流源、四种受控源，DC 稳态与 AC 相量，任意端口等效电阻/戴维南等效，10 Hz–10 MHz 扫频与 −3 dB 截止估计；输出结构化 `LinearNetwork`（节点电位/端口等效/频响/扫频点/边界说明），并在专用教学策略拒绝时作为回退。
2. **原生分析报告导出**：Markdown、LaTeX + 自动 Circuitikz 原理图、Word `.docx`（手写最小 OOXML）、自包含 HTML；四种格式共享同一报告模型，覆盖指标、推导步骤、结构化线性网络与 SPICE。
3. **Web/Python CircuitGraph 单向导入**：3 个原型 benchmark 样例可导入并还原 groundTruth 拓扑；导入结果为未保存原生工程。
4. **原生 30 题教材基准**：一阶/二阶/BJT/通用 MNA 共 30 题，30/30 通过；每题网表强制拓扑自检，期望值为独立手算。
5. **求解调度**：协作式取消令牌贯通策略与 MNA 循环；求解结果缓存与报告一致；编辑/切换工程清空缓存。

验证证据：主项目构建 0 警告/0 错误；7 组常规 C# 回归共 **811 项断言全部通过**；独立数值审计 **1359/1359** 通过，并据审计修复 2 个低严重度问题；30 题基准 30/30，且可导出 CircuitGraph JSON round-trip 回原生工程（31/31 网表等价）。**仍不能据此宣称全部工业 EDA 验收完成**：PNP/FET/多级/非线性、瞬态、完整 ERC、应用内自动等效图、完整 TeX 排版、直接 PDF 生成仍未实现；真实窗口、TeX 编译、Word 实机、外部 SPICE 对照与高 DPI 未验收。

## 2. 审查范围与证据方式

### 2.1 审查的模块

- 引擎：`Engine/Linear/LinearNetworkAnalyzer.cs`、`Engine/Strategies/MnaLinearStrategy.cs`、`Engine/TopologyClassifier.cs`、`Engine/PedagogicalSolution.cs`、其余四个策略的取消令牌接口。
- 数据/结果契约：`Models/LinearAnalysisModels.cs`、`Models/AnalysisResult.cs`、`Models/CircuitProjectData.cs`。
- 服务：`Services/ReportExportService.cs`、`Services/WebCircuitGraphImporter.cs`、`Services/ProjectManagerService.cs`。
- 展示层：`ViewModels/AnalysisStudioViewModel.cs`、`Views/AnalysisStudioPage.xaml(.cs)`。
- 测试工程：`tests/EngineV2Checks`、`ReportExportChecks`、`WebImportChecks`、`NativeBenchmark30`、`EngineAuditChecks` 与四个既有套件的 csproj 链接变更。
- 文档：README/PROJECT/HANDOVER/CLAUDE/专业名词解释、三层 technical report、规格、测试计划、projects 索引、第一轮审查/修订记录与新增两份记录。

### 2.2 证据方式

1. **源码静态核对**：逐文件读取新增/修改源码，核对接口、单位/方向约定、失败路径与边界注释。
2. **实际构建/回归**：在隔离输出目录构建主项目并运行全部 C# 控制台工程（命令见附录）。
3. **独立数值审计**：另行实现参照求解器与随机网络交叉验证（不在引擎自测代码路径内）。
4. **产物级验证**：报告四种格式落盘后用 Python `zipfile`/`ElementTree`/`python-docx` 检查 DOCX OOXML、LaTeX 结构/转义、HTML 自包含；导入器对 3 个 benchmark 的 groundTruth 拓扑逐项比对；30 题 fixture 再做 CircuitGraph round-trip 等价比较。
5. **独立数值审计**：独立实现参照求解器（不调用被测求解器取期望值）、48 个随机网络/KCL/戴维南/相量/扫频/边界/结构化一致性/取消，共 1359 项；审计发现的问题再回灌修复与回归。
6. **文档一致性**：本地链接检查、能力表述与代码对照、历史文档保留检查。

### 2.3 明确未验证

- 真实 WinUI 窗口中的菜单、选择器、指标卡滚动/主题、取消按钮响应；
- 外部 xelatex 编译 `.tex`、Word 实机打开 `.docx`、浏览器打印 HTML；
- 任意真实教材/工业电路的求解正确率与外部 SPICE 数值对照；
- 手机扫码/上传链路、视觉识别、高 DPI/字体/主题视觉回归。

## 3. 第一轮遗留能力逐项对照

| 第一轮记录的能力缺口 | 第二轮状态 | 证据/位置 | 仍缺 |
|---|---|---|---|
| 任意网络 Req、戴维南等效 | **已实现**（任意非地节点对） | `LinearNetworkAnalyzer.TryGetEquivalentResistance/TryGetThevenin`；`EngineV2Checks`/`EngineAuditChecks`/`NativeBenchmark30` M02/M03 | 应用内任意端口选择交互；+∞ 端口仅显示文本 |
| 完整动态/受控源 MNA | **部分实现**：C/L 的 DC 与 AC 相量、四种受控源 | `MnaLinearStrategy` 5 步 + `EngineV2Checks` 209 项 + 审计 1359 项 | 瞬态/时域、互感、运放、非线性；矩阵变量 ≤256 |
| PNP、多级 BJT、FET/MOS/JFET、二极管/非线性 | **仍未实现**（明确拒绝） | `MnaLinearStrategy` 拒绝列表；策略边界 | 真实器件模型与工作点迭代 |
| 统一结构化结果 | **已实现**（线性网络）+ 既有 Q 点/AC 指标 | `Models/LinearAnalysisModels.cs`、`AnalysisResult` 映射、指标卡 | 完整 DC/AC 等效图元、表达式树、知识点标签集合 |
| 原生报告导出、自动等效图 | **报告已实现**（四格式；LaTeX circuitikz 自动原理图） | `Services/ReportExportService.cs`；报告检查 110 项 + Python 外部校验 | 应用内自动等效电路图、直接 PDF、4 端受控源/BJT 的 circuitikz 绘制 |
| 完整数学排版 | 未实现（仍为 Unicode 文本） | `MathFormulaView` 边界 | KaTeX/MathML 级二维排版 |
| 30 题 Benchmark | **原生本地 30 题 30/30**，并可导出 CircuitGraph JSON round-trip 回原生工程（31/31 网表等价） | `tests/NativeBenchmark30`；每题拓扑自检；`--roundtrip` | Web/Python 原型 30 题与图片集仍是另一目标；不代表任意拓扑正确率 |
| 第三方/Web 文件兼容 | **单向导入**：Web/Python CircuitGraph → 原生 | `WebCircuitGraphImporter`；`WebImportChecks` 168 项 | 原生→Web 导出、KiCad/Altium `.sch`、双向契约测试 |
| 完整 ERC | 未扩展（沿用 `Validate`） | `ElectricalNetlist.Validate` | 管脚冲突/线穿引脚/多驱动等工业 ERC 规则 |
| 真正可终止的后台求解 | **部分实现**：协作式取消 + 超时取消令牌 | `SolveAsync(..., CancellationToken)`；MNA 循环检查 | 进程级强制终止不检查令牌的第三方代码 |
| 视口/分析结果持久化 | 未实现 | 工程 DTO 不保存视口/结果 | 可选的视口与结果快照 |
| 更广数值对照/外部 SPICE | 未实现 | — | ngspice/PySpice 数值交叉对照 |
| 真实窗口/高 DPI 验收 | 未做（本轮未操作窗口） | 第一轮有有限窗口证据 | 菜单/选择器/取消/主题/DPI 全流程 |
| 手机上传、视觉识别 | 未实现（后续阶段） | — | 按视觉路线图后续开发 |

## 4. 新增/修改能力与代码位置

| 能力 | 关键文件（相对仓库根） | 规模/说明 |
|---|---|---|
| 复数 MNA 2.0 分析器 | `apps/desktop-winui/Engine/Linear/LinearNetworkAnalyzer.cs` | 约 1.6k 行；DC/AC、受控源、Req/戴维南、扫频、失败原因、CancellationToken |
| MNA 教学策略重写 | `apps/desktop-winui/Engine/Strategies/MnaLinearStrategy.cs` | 约 0.9k 行；5 步、`LinearNetwork` 填充、`AcParameters`、旧日志格式保留 |
| 结构化结果契约 | `apps/desktop-winui/Models/LinearAnalysisModels.cs`、`AnalysisResult.cs` | 节点电位/端口等效/频响/扫频点；`ToAnalysisResult` 映射 |
| 求解调度 | `ViewModels/AnalysisStudioViewModel.cs` | 令牌传入、专用策略→MNA 回退、最近解缓存、取消/超时清理；新增报告命令；打开时探测 Web 图 |
| 原生报告导出 | `apps/desktop-winui/Services/ReportExportService.cs` | 约 1.6k 行；四格式共享报告模型；LaTeX circuitikz 自动原理图；手写 DOCX；HTML 自包含 |
| Web CircuitGraph 导入 | `apps/desktop-winui/Services/WebCircuitGraphImporter.cs` | 约 1k 行；两种形状、definitionId/端口映射、单位换算、确定性格点布局、错误聚合 |
| 文件选择器 | `Services/ProjectManagerService.cs` | 新增 `PickExportReportFileAsync`；打开流程探测 Web 图 |
| 报告菜单/指标卡 | `Views/AnalysisStudioPage.xaml` | 工程菜单“导出分步分析报告”；实时指标新增节点电位/戴维南表/频响扫频卡 |
| 引擎回归 | `tests/EngineV2Checks` | 209 项断言（含审计修复回归 33 项） |
| 报告回归 | `tests/ReportExportChecks` | 110 项断言 + 外部 python 校验 |
| 导入回归 | `tests/WebImportChecks` | 168 项断言 |
| 30 题基准 | `tests/NativeBenchmark30` | 30 题、每题拓扑自检、`--topology` 审计模式、`--roundtrip` 等价校验 |
| 独立审计 | `tests/EngineAuditChecks` | 1359 项；独立参照求解器随机网络/KCL/相量/扫频/边界/取消 |

所有新增测试工程均为 `dotnet run` 控制台项目，不是 `dotnet test` 项目；主项目与测试工程未引入新的 NuGet 依赖（测试工程仅复用既有 CommunityToolkit.Mvvm）。

## 5. 验证结果

### 5.1 构建与回归

| 检查 | 结果 |
|---|---|
| `dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=...bin\_verify` | 0 警告、0 错误 |
| M2SolverChallenger | 152/152 |
| ChallengerRunner | 131/131 |
| M3AdversarialStress | 32/32 |
| M3_4EmpiricalStress | 9/9 |
| EngineV2Checks | 209/209（含审计修复后新增 33 项回归） |
| ReportExportChecks | 110/110 |
| WebImportChecks | 168/168 |
| **常规 C# 合计** | **811/811** |
| EngineAuditChecks（独立审计） | 1359/1359 |
| NativeBenchmark30 | 30/30 + `--roundtrip` 30/30（31/31 网表等价） |

既有 324 项断言在本轮未减少、未弱化；4 个既有测试工程只补源码链接（`Engine/Linear/LinearNetworkAnalyzer.cs`；M2 另补报告/导入服务）。审计发现的问题修复后，`EngineV2Checks` 由 176 增至 209，`EngineAuditChecks` 由 1318 增至 1359。

### 5.2 30 题本地基准

`tests/NativeBenchmark30`：一阶 6、二阶 6、BJT 8、MNA 10；相对容差默认 1%，扫频截止 2%。结果 `Total=30 Passed=30 Failed=0 Accuracy=100.00%`，exit 0。负向验证：重复绑定引脚被拓扑自检拒绝（exit 1）；故意改错一题期望值得 `[FAIL]` + expected/actual/err 明细（Passed=29，exit 1），恢复后 30/30。该基准是**原生 C# 引擎的本地教材题集**，不是 Web/Python 原型 30 题目标，也不证明任意拓扑正确率。

`--roundtrip` 模式另把 30 题导出为 Web/Python `CircuitGraph` JSON，经生产导入器回到原生工程后重新求解并逐字段比对：RoundTripTotal=30/30、Netlists=31/31、NegativeChecks=2/2，31 对网表 1517 个字段在 1e-9 相对容差内等价。说明"基准 fixture → CircuitGraph JSON → 原生导入 → 求解"链路在教材场景可用（但自动布局对任意拓扑的形式化保证仍没有）。生成的可读 JSON 在 `tests/NativeBenchmark30/generated/circuitgraph/`（31 个，可用 `--roundtrip` 重新生成），可直接用应用“打开工程 / 导入 Web CircuitGraph”打开。

### 5.3 报告与导入产物验证

- 报告：`ReportExportChecks` 110 项通过；另用环境变量落盘样例，由 Python 验证 DOCX 5 个 XML entry well-formed、python-docx 可正常打开（53 段落/7 表格/Heading 样式）、document.xml 转义与中文正确；LaTeX 环境配对/花括号平衡/特殊字符转义；HTML 自包含无外链（64 项外部检查全过，日志在系统临时目录，未纳入仓库）。
- 导入：`WebImportChecks` 168 项通过；3 个 benchmark 的元件 id 集合、connection 数、节点分区（groundTruth）、分支/晶体管数、`Validate()` 均一致；BJT 样例可导入/可分类但 seed 参数非物理，用同拓扑（仅 RB=470 kΩ）派生变体验证导入数据可驱动求解（Q 点 24.04 μA/2.404 mA/7.19 V、5 步、放大区），diode/MOS 导入后按引擎边界明确失败。

### 5.4 独立数值审计

`tests/EngineAuditChecks` 由独立审计方实现：自带一套从零编写的复数 MNA 参考求解器（独立装置模型 + 缩放选主元高斯消元 + 反向误差残差门），期望值从不调用被测求解器；随机种子固定可复现。最终 **1359/1359 通过**（exit 0，重建 0 警告/0 错误）。

覆盖：独立求解器自检 19；DC 解析 + 逐节点 KCL 128；48 个随机网络交叉验证 436（DC 电位/KCL/4 端口 Req/Req 对称性/戴维南带载公式，R∈1e-3..1e7 Ω）；任意端口 Req/戴维南/串并联 52；AC 相量 207（含 12 个随机 AC 网络×3 频点）；61 点扫频与截止 87；边界/奇异/拒绝 135；取消与规模 17；结构化契约 235；NetlistExtractor 端到端 43。灵敏度自检：在引擎副本中翻转 VCCS 跨导符号后同一 harness 立即大量失败（副本已删除，仓库源码未改），证明断言非空转。

审计发现并已修复的 2 个低严重度问题：

1. 扫频窗口外的截止频率曾误报（fc≈0.1 Hz 被 10 Hz–10 MHz 窗口扫描时返回 ≈14.13 Hz，且提示文案与 −40 dB 实际矛盾）。修复：窗口外探针 + 通带参考；交叉点不可测时截止/带宽为 null、Display"未检测到（超出扫描范围）"、BoundaryNote 说明，不再输出伪值；有窗口内正常检出对照组。
2. DC 激励端口曾依赖元件 Id 顺序（混合源时可能取电流源并报告"无电压增益定义"）。修复：DC 与 AC 一致、电压源优先、同类按 Id 确定性排序；纯电流源网络保留原语义。

两处修复未改变 `Models/LinearAnalysisModels.cs` 字段/类型，只收紧越界语义与端口选择规则；`EngineV2Checks` 新增 33 项回归（176→209），审计新增 41 项断言（1318→1359）。

残余审计风险：几何提取只审计 3 种手工布局（未 fuzz 随机布线/T 型 tap/GND 压线）；取消只验证"已取消 token"；>1e7/1e12 极端参数与稠密 256 变量受控源网络条件数未系统验证；UI 层元件/导线上限属 ViewModel，未在控制台审计；输出端口启发式教学语义未验证；随机网络跳过了参考判定为奇异的候选（奇异由定向拒绝用例覆盖）。

## 6. 新发现问题与严重性

| 级别 | 发现 | 影响/证据 | 处置 |
|---|---|---|---|
| 中 | 原型 BJT benchmark seed 参数非物理（RB=100 kΩ/RC=2 kΩ/VCC=12 V/β=100 → VCEQ≈−10.6 V） | 导入成功但原生 BJT 策略按教学安全边界拒绝；Python 原型只做符号推导不校验 Q 点 | 文档与测试明确记录；不修改原型数据；可解变体证明导入链路有效 |
| 中 | circuitikz 不绘制 4 端受控源/BJT/MOS/变压器/VccPort | `NetlistExtractor` 只为二端器件生成 Branch，报告会列入“原理图未覆盖器件” | 报告已注明；绘制键已实现，待提取器补二端映射或增加四端绘制 |
| 中 | 频响输出端口为启发式（排除激励端后取幅值最大非地节点） | 复杂多输出网络可能选错“主输出”，`FrequencyResponse` 可能为 null 并写 BoundaryNote | 文档边界；任意端口等效表可补偿；后续提供端口选择 UI |
| 中 | `.tex` 未真实编译、`.docx` 未 Word 实机打开、PDF 未直接生成 | 报告产物只在源码/结构层面验证 | 明确写入文档与测试计划；后续安装 TeX/Word 环境验收 |
| 中 | 真实窗口未重新验收（菜单、选择器、指标卡、取消） | 本轮只做隔离构建与控制台回归 | 测试计划保留窗口验收项；不建议据此宣称 UI 完成 |
| 低 | AC 源幅值不随扫频变化（相量取峰值幅值），截止估计依赖 −3 dB 判据与 61 点插值 | 教师需理解这是小信号线性频响，不是 SPICE `.ac` 全功能 | 文档/步骤 5 说明；30 题与审计用解析网络校准 |
| 低 | 任意端口 `Req` 可为 +∞，需依赖显示字段而非 double 直接绑定 | UI 已用 `ResistanceDisplay`；报告也读显示/数值并处理 | 已处理并写入契约说明 |
| 低 | 导入自动布局只保证拓扑/确定性，不保证美观；四端 MOSFET bulk 折叠 source | 导入后画布可能需要手工整理；bulk 非 source 时被近似短接 | 文档边界；导入后提示另存/检查 |
| 低 | 自动布局 + 8px 引脚/5px 线段吸附在理论上可能对并联同坐标元件或 GND 落线产生虚假连接 | round-trip 31/31（含并联 C/L、并联 RLC、BJT VCC 轨）未触发，但只是经验验证 | 已用负向注入验证检测能力；后续可加几何约束或导入后 ERC 复核 |
| 低 | 协作式取消无法终止不检查令牌的同步代码 | 超时/取消后仍可能有一个后台计算结束 | 已知边界；MNA 循环已检查令牌 |
| 低 | 浏览器/Node/Python 原型本轮未复跑 | `npm run check` 结果不能引用为第二轮证据 | 文档明确“未复跑”；如需以原型证据交付应重新执行 |
| 低（审计发现，已修复） | 扫频窗口外的截止频率曾误报（fc≈0.1 Hz → 假值 ≈14.13 Hz，提示文案矛盾） | 仅 UI/报告文字误导；不影响 DC/AC 数值解 | 已修复为窗口外探针 + 通带参考；不可测时截止/带宽 null、Display“未检测到（超出扫描范围）”；EngineV2Checks 与审计均加回归 |
| 低（审计发现，已修复） | DC 激励端口曾按元件 Id 顺序选择（混合源时可能取电流源），与 AC 行为不一致 | 数值电位/Voc/Req 正确，仅增益/输入电阻标注依赖命名 | 已修复为 DC/AC 一致、电压源优先、同类按 Id 确定性排序；两套回归覆盖 |

## 7. 文档一致性检查

- 已更新 11 份现行文档：`README.md`、`PROJECT.md`、`HANDOVER.md`、`CLAUDE.md`、`专业名词解释.md`、`projects/presentation-layer-technical-report.md`、`projects/data-layer-technical-report.md`、`projects/analysis-engine-technical-report.md`、`projects/spec-first-stage-local-circuit-analysis-20260601.md`、`projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md`、`projects/README.md`。
- 新增：本报告、[第二轮修订记录](technical-code-fixes-20261017-round2.md)。
- 保留为历史：`projects/technical-document-review-20261017.md`、`projects/technical-code-fixes-20261017.md`、`projects/technical-document-review-20260716.md`、`projects/archive/**`、原始需求、申报/开题/PPT/PDF、第三方许可；两份第一轮文档已加第二轮链接与“历史证据”标注。
- 一致性要点：能力描述由“未提供报告/不支持受控源”更新为“四格式报告已提供/MNA 2.0 支持受控源与动态元件”，同时保留“未实现/未验收”清单；两个 30 题口径（原生本地 vs Web/Python 原型）分开陈述；测试数字注明日期与命令；未把原型能力写为原生能力。
- 链接与格式：16 份现行文档的本地链接与代码围栏检查通过（0 问题）；`git diff --check` 仅报告既有 `CircuitTemplateFactory.cs` 文件末尾空行（第一轮已记录、本轮未修改该文件）。
- 工作区完整性：本轮批量脚本事故导致的 5 个 C# 文件乱码已按[第二轮修订记录 §7.3](technical-code-fixes-20261017-round2.md) 所述方式恢复（字符串缺失 0、行数一致、构建与 324 项回归通过），恢复脚本与证据目录已清理；未覆盖用户其它文件，未结束用户正在运行的程序，未提交/推送 Git。

## 8. 未验证与后续验收

1. 真实窗口：报告菜单与四种选择器、Web 导入提示、指标卡滚动/主题、取消求解响应、高 DPI/字体/主题。
2. 外部工具链：xelatex 编译 `.tex`、Word 打开 `.docx`、浏览器打印 HTML→PDF；外部 SPICE（ngspice/PySpice）数值对照。
3. 求解正确率：任意真实教材/工业拓扑、极端参数、病态矩阵、PNP/FET/非线性；矩阵变量 256 以上的工程化策略。
4. 数据与格式：原生→Web 导出、第三方 `.sch`、工程版本迁移、视口/结果持久化。
5. 手机与视觉：扫码/上传全流程与视觉识别（视觉仍在后续阶段）。
6. 原型：`npm run check` 未复跑；本轮未做浏览器/API E2E。

## 9. 建议的下一轮优先级

1. 真实窗口端到端验收（报告导出、Web 导入、取消、指标卡、保存/关闭）并修复 UI 层问题；
2. 任意端口选择 UI + 等效电路图渲染（把结构化结果变成可见图）；
3. PNP/多级 BJT 与 FET 小信号模型（先做可验证的教学子集，再做非线性）；
4. 外部 SPICE 数值对照与更广基准（含真实教材参数）；
5. 完整数学排版与 PDF 生成（若允许引入外部依赖/工具链）。

## 附录：复现命令与证据位置

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj --verbosity quiet
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj --verbosity quiet
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj --verbosity quiet
dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj --verbosity quiet
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
dotnet run --project tests/WebImportChecks/WebImportChecks.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj -- --roundtrip
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
```

证据目录：各测试工程源码与输出；报告样例可用 `REPORT_EXPORT_ARTIFACTS_DIR` 环境变量落盘（本轮样例在系统临时目录，未提交仓库）。本轮构建使用隔离 `OutDir`，未覆盖用户正在运行的程序，也未结束用户进程。

审查实施：Lead（集成/文档/复审）与 engine/reports/interop/benchmark/auditor 分工实施；最终构建与回归由 Lead 在本工作区复跑。
