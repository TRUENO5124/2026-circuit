---
spec_title: "WinUI 原生电路教学辅助系统：当前开发规格"
spec_created_at: "2026-06-01"
spec_reviewed_at: "2026-10-19（耗尽型 MOS/JFET 与工作区校验修订、0.2.0-preview.1 发布后）"
spec_execution_status: "partial"
spec_execution_mode: "local-workspace"
---

# 当前开发规格与交付边界

本文替代早期 Web/HTML 主架构规格，用当前工作区实现校正任务状态。原始 Issue I1–I9、视觉方案及当时工具环境记录保存在[修订前归档](archive/2026-10-17-before-winui-doc-sync/spec-first-stage-local-circuit-analysis-20260601.md)，不再把过去“不是 Git 仓库”等描述当作当前环境。

> 用户澄清：视觉仍需开发，放在本体完善后。第一轮已修改本体源码并执行构建与回归；第二轮（2026-10-17）继续实现通用线性网络 MNA 2.0、原生报告导出、Web CircuitGraph 单向导入与 30 题本地基准；第三至五轮完成 PNP、材料压降、标准符号、默认朝向与 ALT+X 镜像。**第六轮（2026-10-18）实现二极管非线性教学求解、MOSFET 平方律教学求解、BJT 共集电极、多级放大预留接口与明确拒绝，并生成 29 个原生 `.circuit` 教学电路文件。第七轮（2026-10-18）按用户反馈把显示统一为教材估算法（每步两位小数舍入并回代、后端精确值 `Exact*` 不显示）、全部题型显示值 ≤2 位小数、BJT/MOS 推导按 DC/AC 等效电路分组，并用 Win2D 自绘公式框替换 Unicode 文本转写；第六轮补充的“教材估算法对照/双口径”已被第七轮单一口径取代（历史见第六轮修订记录）。第八轮（2026-10-18）支持 BJT 信号源内阻 Rs/电解输入耦合电容/Aus（B07 教学变体可解）、修复保存工程时改写 ProjectName 的缺陷、维护 M2 Suite 7 并新增保存回归。第九轮（2026-10-18）让 BJT 小信号参数 r_bb'/r_be(h_ie) 可编辑并持久化（0=自动；题目给定 r_be 直接采用；引擎全部 BJT 子类型读取实际参数；检查器与参数设置两处编辑框；当轮 Nonlinear 913、M2 160），按用户要求本轮不做 ui-capture 窗口验收。** **2026-10-19 补全耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 单管教学求解（共源/共源+Rs/共源旁路/共漏/共栅五类拓扑；增强/耗尽 MOS 用 Level-1 平方律、JFET 用 Shockley 方程 ID=IDSS·(1−VGS/VGS(off))²、K=IDSS/VGS(off)²；MOS K/λ 与 JFET IDSS 检查器可编辑并随工程持久化），并完成元器件工作区物理核验修订（BJT 无 Re 加载偏置方程、BE/BC 判据、MOS/JFET 分段模型+DC 负载线、二极管/FET Invalid）；发布阶段版本 0.2.0-preview.1，发布前全量控制台矩阵 20 组命令/4905 项断言全部通过（M2SolverChallenger 166/166、NonlinearCircuitChecks 1606/1606）。** 最新状态见[耗尽 MOS/JFET 实现记录](fet-depletion-jfet-strategies-20261019.md)、[工作区校验修订](operating-region-validation-20261019.md)与[第九轮本体修订记录](technical-code-fixes-20261018-round9.md)。

## 1. 当前范围

主交付对象为 WinUI 3 / .NET 9 原生桌面软件：人工绘制或加载教学模板、修改元件和导线、进程内 C# 求解（单只 NPN/PNP 教学策略含共射/共基/共集、二极管非线性教学策略、增强/耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 单管放大策略 + 通用线性网络 MNA 2.0）、查看推导与结构化指标、保存工程、导出有限 SPICE 文本，以及原生导出四种格式的分析报告（Markdown/LaTeX+Circuitikz/Word/HTML）。可单向导入 Web/Python `CircuitGraph` JSON，并可直接打开 `examples/circuit-models/` 的 29 个原生教学电路；`tests/CircuitModelChecks -- --file <path>` 可对任意 `.circuit` 做单文件诊断。元件符号按 IEC/IEEE 315 与 Multisim VCC 参考统一为矢量符号库；二极管/BJT 支持硅管/锗管压降参数；MOS/JFET 参数在界面明确标注为阈值电压/夹断电压，MOS 的 K/λ 与 JFET 的 IDSS 可在检查器直接编辑并随工程文件保存/读回（旧工程缺省 0，JFET 求解前须补填有限正数）。**第七轮起教学结果显示为教材估算法**（分压偏置空载 `U_B` → `I_EQ→I_BQ→I_CQ→U_CEQ`，每个中间结果按显示单位最多两位小数舍入并代入下一步；公式不化简），后端戴维南精确值只保留在 `QPointInfo.Exact*`/步骤 `Values`，界面不显示、也不再并列双口径；全部题型（BJT/MOS/JFET + RC/RL/RLC/MNA/二极管/报告）显示值最多两位小数；推导按 DC/AC 等效电路分组展示（`SolutionPhase`/`StepGroups`，共射/共基 5 步、共集 6 步、MOSFET/JFET 7 步，电解电容极性核验归 DC 并重排 StepIndex）；公式框由 Win2D `CanvasControl` 自绘真排版（真角标/叠放分式/根号/矩阵/按宽度换行/自适应高度/基础字号 16，复制 LaTeX 保留）。**2026-10-19 工作区校验修订**：教学舍入链保留；BJT 用未舍入加载解及 BE/BC 偏置核验工作区，Re=0 改用含负载偏置回路，空载估算越界但加载解有效时切换含负载教学链；MOS/JFET 用未舍入分段模型及 DC 负载线核验；无效数值不映射为截止。见[修订记录](operating-region-validation-20261019.md)。**第八轮起 BJT 放大器支持信号源内阻 Rs**（"交流源与输入耦合电容之间恰一只电阻"的教材拓扑）与**电解输入耦合电容**（`+/-` 引脚识别、DC 相极性核验），识别到 Rs 时给出 `Aus=Av·Ri/(Rs+Ri)`、`τ1=(Rs+Ri)C1` 并在实时指标卡/报告显示"信号源内阻 Rs/源电压增益 Aus"两行；未覆盖电阻诊断给出元件 Id/网名与支持位置；保存工程保留 `ProjectName`（仅未命名工程回退文件名）。**第九轮起 BJT 小信号参数 r_bb'/r_be(h_ie) 可调**：元件/DTO 新增 `BjtRbb`/`BjtRbe`（Ω，0=自动：r_bb' 共射/共集 200 Ω、共基 50 Ω，r_be 按 Q 点计算；>0 题目给定直接采用），全部 BJT 子类型读取实际参数，rbe/h_ie 步骤文案/公式不再硬编码 200/50，步骤 `Values` 含 `rbb_ohm`/`rbe_ohm`/`rbe_source`；UI 在"元件参数检查器（选中 BJT）"与"参数设置 → 三极管模型与信号源特性"两处提供编辑框（宏参数与元件双向同步、0=自动、非法输入归零）；默认 0 时数值与第八轮一致；第九轮按用户要求不做窗口截图验收（仅控制台回归 + 构建）；2026-10-19 发布验证同样未做新的 ui-capture（控制台回归 + 构建 + 启动冒烟）。多级放大只预留 `IMultiStageAnalysis` 接口并明确拒绝，Cascade 求解未实现。具体接口见 [PROJECT.md](../PROJECT.md)。

React 编辑器、Zod、FastAPI、SQLite 与 Python/SymPy 引擎仍保留，属于独立原型；其功能与格式不自动成为 WinUI 主软件的一部分。视觉识别仍在项目规划内，安排在软件本体完善后的阶段开发，本阶段暂不接入或验收。

## 2. 历史 Issue 与当前实现的对应

| 历史项 | 当前状态 | 原生主软件的实际位置与缺口 |
|---|---|---|
| I1 数据模型/元件库 | 两套模型并存，新增单向导入 | C# 编辑模型、工程 DTO、电气网表、线性网络结构化结果已实现；Web 有 18 类 Zod 定义，原生有 25 个符号条目；`WebCircuitGraphImporter` 支持 Web→原生单向导入，无原生→Web 导出 |
| I2/I3 策略/保护 | 原生单管全类型 + 二极管/MOSFET/JFET 教学策略 + MNA 2.0；第七轮补教材估算法显示与 DC/AC 分组；第八轮补 BJT Rs/电解电容/Aus；第九轮补 BJT r_bb'/r_be(h_ie) 可调；2026-10-19 补耗尽型 MOS/JFET 单管求解与工作区物理核验 | 专用 C# 策略：RC/RL、RLC、BJT（单只 NPN/PNP，共射/共基/共集，硅/锗压降；显示为教材估算法+两位小数；后端 `Exact*` 仅保留；推导 DC/AC 分组；**支持信号源内阻 Rs/电解输入耦合电容/Aus**；**r_bb'/r_be(h_ie) 可调，0=自动/题目给定直接采用**）、二极管非线性（9 类子类型，显示 ≤2 位小数）、增强/耗尽型 MOSFET 与 N/P 沟道 JFET（共源/共源+Rs/共源旁路/共漏/共栅五类拓扑；增强/耗尽 MOS 用 Level-1 平方律、JFET 用 Shockley 方程；K/λ/IDSS 可编辑并持久化；教材估算法 7 步、DC/AC 分组；暂不支持 Rs）、多级预留拒绝策略；另有 `MnaLinearStrategy`（复数 MNA 2.0：受控源/动态元件/戴维南/扫频）；快照后台求解、校验、15 秒等待上限、协作式取消与 MNA 回退已接入；Python 独立注册六个策略并有 SafeSolver |
| I4 API/Worker | 独立原型已实现 | 不位于 WinUI 求解依赖链；不可作为原生任务调度已完成的证据 |
| I5 编辑器 | 原生基础交互已实现 | Win2D、布线、分支、框选、撤销快照；属性与工程状态历史覆盖仍不完整 |
| I6 手机上传 | 原型已有 QR/SSE 代码 | WinUI 未接入；捕获页 HTML 响应和编码已通过接口回归，真实手机链路待验证 |
| I7 视觉识别 | 后续阶段开发 | 不创建/训练/部署视觉模块，不纳入当前验收 |
| I8 30 题 Benchmark | **原生 30 题基准已完成** | `tests/NativeBenchmark30`：一阶/二阶/BJT/MNA 共 30 题独立手算，30/30 通过；`--roundtrip` 可导出为 CircuitGraph JSON 并经生产导入器回原生工程（31/31 网表等价）。Web/Python 原型的 30 题与图片集仍是另一目标，3 个原型样例已可原生导入 |
| I9 公式/报告 | 原生报告已实现，公式改为 Win2D 真排版 | 原生 Markdown/LaTeX+Circuitikz/Word/HTML 报告、任意端口等效表与频响扫频；`MathFormulaView` 第七轮改为 Win2D `CanvasControl` 自绘（真上下角标/叠放分式/根号/bmatrix·pmatrix·cases·array/按宽度换行/自适应高度/基础字号 16/复制 LaTeX），限制为 array 竖线不画、超宽原子等比缩小、Canvas 文本不可选择、`\overline/\vec/\tilde` 不画装饰；仍不是 KaTeX/MathML 完整排版引擎；PDF 需浏览器打印或外部 xelatex |

## 3. 原生实现清单

| 模块 | 源码已实现 | 未覆盖的交付要求 |
|---|---|---|
| M1 模板 | 空白与共射、RC 暂态、阻容耦合、共基极工厂 | 任意模板标准化、第三方 `.sch` 兼容与全屏视觉验收 |
| M2 提取/求解 | 并查集网表、分类、RC/RL、RLC、BJT（NPN/PNP，共射/共基/共集，硅/锗压降；教材估算法显示+两位小数；DC/AC 分组；**信号源内阻 Rs/电解输入耦合电容/Aus**；**r_bb'/r_be(h_ie) 可调，0=自动/题目给定直接采用，步骤 Values 含 `rbb_ohm`/`rbe_ohm`/`rbe_source`**）、二极管非线性（整流/滤波/限幅/钳位/检波/直流偏置，显示 ≤2 位小数）、增强/耗尽型 MOSFET 与 N/P 沟道 JFET 放大（共源/共源+Rs/共源旁路/共漏/共栅五类拓扑；增强/耗尽 MOS 平方律、JFET Shockley；K/λ/IDSS 可编辑并持久化；7 步教材估算法；暂不支持 Rs）、通用线性 MNA 2.0（受控源/动态/戴维南/相量/扫频）、多级明确拒绝与预留接口、教学步骤、校验与显式失败、专用策略→MNA 回退 | 任意拓扑正确率、多级/混合非线性级联求解、完整 ERC、外部 SPICE 数值对照 |
| M3 画布 | WireTap、母线、线段调整、元件/导线跟随和选择；符号统一由 `SymbolGeometry` 按国际标准绘制（NPN/PNP、JFET、MOSFET、VCC、GND 等） | 避障布线、所有交互路径一致、符号像素级视觉验收、深层分支/删除/焦点端到端验收 |
| M4 集成验证 | 原生构建与全量控制台回归/审计（**发布前实测 2026-10-19，0.2.0-preview.1**：M2 **166**、ChallengerRunner 131、M3 32、M3_4 9、EngineV2 306、报告 110、导入 172、符号 230、非线性 **1606**、29 文件 259、公式排版 411、显示两位小数 55，另独立审计 1359、30 题基准 30/30；生成器 `--verify` 29/29；全量矩阵 20 组命令/4905 项断言全部通过、Release x64 自包含 528 个运行文件、构建 0 警告/0 错误、启动冒烟通过、`npm test` 16/16）；`CircuitModelChecks -- --file` 单文件诊断可用；**B07 变体 + 普通模板已做真实窗口静态截图验收（含 Rs/Aus 两行与 AC 卡叠字修复，见第八轮记录 3.4）**；**第九轮与 2026-10-19 发布验证均未做新的 ui-capture 窗口验收** | 逐像素自动比对、深浅主题、其它 DPI、自动化交互/文件选择器、完整保存/关闭流程验收 |
| 工程管理 | JSON 打开/保存/另存为、路径重置、修改标志、未保存提示、引用校验、原子替换、SPICE 导出、四种格式报告导出、Web CircuitGraph 单向导入；**保存保留 ProjectName（仅未命名工程回退文件名，第八轮修复）**；**BJT `BjtRbb`/`BjtRbe` 与 FET `MosfetK`/`MosfetLambda`/`JfetIdss` 随工程保存/读回（第九轮/2026-10-19，格式仍 1.2.0）** | 原生→Web 导出、完整版本迁移、第三方格式与全部窗口边界验收 |

“实现”表示有实际执行代码；“测试通过”指测试计划与历轮修订记录（当前 2026-10-19 修订，见[耗尽 MOS/JFET 实现记录](fet-depletion-jfet-strategies-20261019.md)、[工作区校验修订](operating-region-validation-20261019.md)与[第九轮修订记录](technical-code-fixes-20261018-round9.md)）中列出的控制台断言。没有将所有原始工业 EDA 标准和要求勾选为完成。

## 4. 当前端到端路径

```text
启动 WinUI → 新建/载入模板（或导入 Web CircuitGraph）
 → 放置/编辑/连线
 → 快照后台提取 → 网表/参数校验 → 拓扑分类 → 专用策略 / MNA 2.0 回退 / 明确拒绝
 → 分步公式（Win2D 真排版）、教材估算法两位小数指标、节点电位、任意端口等效、交流频响扫频
 → 推导按 DC/AC 等效电路分组（BJT/MOS）
 → 保存自有 JSON / 导出有限 SPICE 文本 / 导出 Markdown·LaTeX·Word·HTML 分析报告
```

文件路径是独立能力：打开自有 JSON → ToModel → 更新集合/模板宏 → 编辑；打开 Web/Python `CircuitGraph` JSON → 导入适配器 → 原生 DTO → 自动布局 → 未保存工程 → 求解/另存。原生→Web 导出、任意第三方 `.sch` 与项目 DTO 的双向互通仍未实现。

## 5. 历轮修订与后续任务

- [x] 通用线性网络 MNA 2.0：R/C/L、独立源、四种受控源、DC/AC 相量、任意端口 Req/戴维南、10 Hz–10 MHz 扫频与截止估计；结构化 `LinearNetwork` 结果。
- [x] 专用策略拒绝时回退 MNA；取消令牌传入全部策略与 MNA 循环。
- [x] 原生报告导出：Markdown、LaTeX+Circuitikz、Word `.docx`、自包含 HTML；报告信息/指标/原理图/步骤/结构化网络/SPICE。
- [x] Web/Python `CircuitGraph` 单向导入适配器 + 3 个原型 benchmark 样例导入回归。
- [x] 原生 30 题教材基准（30/30）与独立数值审计（历轮结果见[第七轮修订记录](technical-code-fixes-20261018-round7.md)）。
- [x] 单只 PNP 三极管四类教学偏置分析与硅/锗管 VBE/VEB 压降；二极管/BJT 材料参数与工程持久化；元件符号按 IEC/IEEE 315/Multisim VCC 统一；MOS/JFET 参数含义在界面标注。
- [x] 第六轮：二极管非线性教学求解（恒压降分段线性 + Shockley/Newton；9 类子类型）；MOSFET 平方律教学求解（Level-1，NMOS/PMOS 共源/共漏/共栅，K/λ 持久化；2026-10-19 已扩展为增强/耗尽 MOS 与 N/P 沟道 JFET 五类拓扑）；BJT 共集电极（射极跟随器）；多级放大 `MultiStageAmplifier/Cascade` 分类 + `IMultiStageAnalysis` 预留接口 + 明确拒绝；29 个原生 `.circuit` 教学电路文件（D01–D07、B01–B16、M01–M06）。
- [x] 第六轮补充（第七轮已改为单一口径，保留为历史）：BJT 分压偏置"教材估算法对照"（第 1 步/摘要/Q 点双口径/结构化 Values，`R_th/((1+β)R_E)` 适用性判据，题3-12 场景回归）；`CircuitModelChecks -- --file <path>` 单文件诊断。
- [x] **第七轮（2026-10-18）**：BJT/MOS 显示统一为教材估算法（每个中间结果按显示单位两位小数舍入并回代、公式不化简；题3-12 显示链 `U_B=11.63 V → I_EQ=53.85 μA → I_BQ=0.53 μA → I_CQ=53 μA → U_ECQ=11.82 V`），后端戴维南精确值仅保留在 `QPointInfo.Exact*`/步骤 `Values`（不显示、不再双口径）；全部题型显示值 ≤2 位小数；推导按 DC/AC 等效电路分组（`SolutionPhase`/`StepGroups`，共射/共基 5 步、共集 6 步、MOS 7 步，电解电容极性归 DC 并重排 StepIndex）；公式框改为 Win2D `CanvasControl` 自绘真排版（`Math/*`，基础字号 16、按宽度换行、内容自适应高度、复制 LaTeX 保留）；新增 `tests/MathFormulaChecks`（411/411）与 `tests/DisplayRoundingChecks`（55/55），`NonlinearCircuitChecks` 扩至 751/751，NativeBenchmark30 30/30（B08 参数按记录调整）。
- [x] **第八轮（2026-10-18）**：BJT 策略支持信号源内阻 Rs（"交流源与输入耦合电容之间恰一只电阻"的拓扑，B07 变体 R6=50 Ω+电解 C3）与电解输入耦合电容（`+/-` 引脚识别、DC 相极性核验），新增 `Aus=Av·Ri/(Rs+Ri)`（共基 `Au·ri'/(Rs+ri')`）、`τ1=(Rs+Ri)C1` 与可诊断的未覆盖电阻文案；实时指标卡/报告在识别到 Rs 时显示"信号源内阻 Rs/源电压增益 Aus"两行（AC 指标卡 8 行 Grid；窗口验收首拍发现 7 行导致两行叠字，修复后重拍正常）；修复保存工程时把 `ProjectName` 改写为文件名的缺陷（D06 工程名恢复）；B07 教学文件变体与生成器定义同步（保留手工布局，`--verify` 10/10）；M2 Suite 7 改为反射 Win2D 公式管线并新增保存保留工程名断言；`NonlinearCircuitChecks` 822/822（+71）、`M2SolverChallenger` 156/156。
- [x] **第八轮窗口截图验收**：用最终构建（2026-10-18 18:30:46，`bin\_verify\CircuitAssistant.exe`）在真实窗口（Win32 `PrintWindow`，150% DPI，1920×1118）打开 B07 变体求解成功（“6 个步骤”，无“未覆盖电阻”错误），DC 2 步（Q 点 + C3 极性 `U_C3=2.64 V`）+ AC 4 步、第 4 步含 `A_us=A_v·R_i/(R_s+R_i)`、实时指标 Rs/Aus 两行正常、普通模板不显示两行；证据 `tools/ui-verify/evidence/round8/`（bug 现场 `13_b07_ac_metrics_region.png` 与修复后 `11b/12b/13b/14b`）。静态截图级，未做逐像素比对/主题/其它 DPI/自动化交互。
- [x] **第九轮（2026-10-18）**：BJT 小信号参数 r_bb'/r_be(h_ie) 可编辑并持久化——元件 `BjtRbb`（Ω，0=自动：共射/共集 200 Ω、共基 50 Ω）/`BjtRbe`（Ω，0=按 Q 点计算；>0 题目给定直接采用）与 DTO 字段（负数/非有限值拒绝，旧工程缺省 0，格式仍 1.2.0）；引擎全部 BJT 子类型读取实际参数，rbe/h_ie 步骤文案/公式/代入使用实际 rbb，第 2 步 Values 新增 `rbb_ohm`/`rbe_ohm`/`rbe_source`（computed|given）；UI 在元件参数检查器（选中 BJT）与参数设置 Group 3 两处新增编辑框（0=自动提示、宏参数双向同步、无效输入归零）；`BjtSmallSignalParameterChecks` 91 项 → `NonlinearCircuitChecks` 913/913（当轮）、`M2SolverChallenger` 160/160（当轮，+4：宏同步/默认自动/修改失效/保存读回）；默认 0 时数值与第八轮完全一致。**本轮按用户要求不做 ui-capture 窗口验收（仅控制台回归 + 构建）。**
- [x] **2026-10-19（发布 0.2.0-preview.1）**：耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 单管教学求解（共源/共源+Rs/共源旁路/共漏/共栅；JFET 用 Shockley 方程 ID=IDSS·(1−VGS/VGS(off))²、K=IDSS/VGS(off)²，增强/耗尽 MOS 用 Level-1 平方律；MOS K/λ 与 JFET IDSS 检查器可编辑并随工程持久化）；元器件工作区物理核验修订（BJT 无 Re 加载偏置方程、BE/BC 判据、MOS/JFET 分段模型+DC 负载线、二极管/FET Invalid）；发布前全量矩阵 20 组命令/4905 项断言全绿、构建 0 警告/0 错误、Release x64 自包含 528 个运行文件、启动冒烟通过。
- [ ] **多级放大级联求解（仅预留接口，按用户要求列入后续项目规划，本阶段不实现）**；NPN+PNP 混合与 BJT+MOS 混合非线性网络求解；**MOSFET/JFET 放大器信号源内阻 Rs/Aus（第八轮仅 BJT）与小信号参数 r_bb'/r_be 可调（第九轮仅 BJT）**；大信号/失真分析。
- [ ] 完整 ERC、任意拓扑正确率与外部 SPICE 数值对照、完整工程版本迁移。
- [ ] KaTeX/MathML 级排版（基础二维排版已由 Win2D 实现：角标/分式/根号/矩阵/自动换行；`\overline/\vec/\tilde` 装饰、Canvas 文本选择、array 竖线等仍受限）、应用内自动等效电路图、PDF 直接生成。
- [ ] 当前/历史真实窗口交互验收的剩余项：逐像素自动比对、深浅主题切换、150% 以外 DPI 与其它窗口尺寸、自动化交互断言、保存/关闭流程窗口操作；视觉数据与原生模型协议、识别管线、候选确认与分层评测。

## 6. 验收标准

基础自动化验证使用[当前测试计划](wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md)。**发布前实测（2026-10-19，0.2.0-preview.1）：主项目 Debug/Release 构建 0 警告/0 错误，Release x64 自包含发布 528 个运行文件**；C# 常规回归 1156 项（M2 **166**、ChallengerRunner 131、M3 32、M3_4 9、EngineV2 306、报告 110、导入 172、符号 230），非线性引擎回归 **1606/1606**、29 个教学电路文件校验 **259/259**、公式排版 **411/411**、显示两位小数 **55/55**、独立数值审计 1359/1359、30 题基准 30/30；四个生成器 `--verify` 合计 29/29（7/7、6/6、10/10、6/6）；全量控制台矩阵 **20 组命令 / 4905 项断言全部通过**；`npm test`（独立 Web/Python 原型）16/16；发布目录启动冒烟通过。第九轮未复跑 `npm run check` 与 `NativeBenchmark30 -- --roundtrip`（历史 31/31）。**第九轮按用户要求不做 ui-capture 窗口验收；2026-10-19 发布验证同样未做新的 ui-capture**（仅控制台回归 + 构建 + 启动冒烟）；第七/八轮的静态截图验收（共射/题3-12/PNP/MOS、B07 变体 + 普通模板，证据 `tools/ui-verify/evidence/round8/`）不覆盖新增的 r_bb'/r_be、MOS K/λ 与 JFET IDSS 编辑框，也未做逐像素自动比对、深浅主题、其它 DPI 或自动化交互断言。这不代表上述待办完成，也没有得到任意电路正确率、TeX 渲染成功率或完整窗口 E2E 覆盖。

2026-10-19 按授权只同步现行文档、未改源码/测试，并完成 0.2.0-preview.1 发布（Windows x64 自包含、528 个运行文件、启动冒烟通过）；第九轮曾按授权修改应用源码、测试与现行开发文档并保留历史记录。未修改正式申报材料，也没有推送 Git。
