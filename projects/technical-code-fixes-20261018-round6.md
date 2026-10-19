# 软件本体代码修订记录（第六轮：二极管/MOSFET 非线性教学求解、BJT 共集电极与 29 个电路模型文件）

> 日期：2026-10-18（Asia/Shanghai）
> 性质：按用户第六轮要求修改 WinUI 主软件与教学资源。用户要求：① 支持二极管非线性电路（整流/滤波/限幅/钳位等）的教学求解；② 支持 MOSFET 放大电路（共源/共漏/共栅）教学求解；③ 支持 BJT 共集电极（射极跟随器）；④ 多级放大只预留接口并明确拒绝、写入后续规划；⑤ 生成 29 个可直接打开求解的原生 `.circuit` 教学电路文件。第六轮补充（同日）：根据用户对 `document/3-6题.circuit`（PNP 分压偏置，题3-12 参数）的反馈，为分压式自偏置 BJT 策略新增"教材估算法对照"并加回归。
> 范围：`Engine/Nonlinear/`、`Engine/Strategies/`、`Engine/TopologyClassifier.cs`、`Engine/MultiStage/`、`Models/`、`ViewModels/`、`tools/CircuitModels.*`、`examples/circuit-models/`、`tests/*`、现行文档与索引。
> 结论：主项目构建 0 警告/0 错误；新增非线性策略回归 **491/491**、29 个教学电路文件校验 **259/259**、四个生成器 `--verify` 全部通过（7/7、6/6、10/10、6/6）；既有常规 C# 回归 8 套合计 **1145/1145**，独立数值审计 **1359/1359**，30 题本地基准 **30/30**、round-trip **31/31** 网表等价。多级级联求解**未实现**（只预留接口并明确拒绝），MOS 的 K/λ 已持久化但检查器无编辑框，JFET/耗尽型不承诺求解。未提交/推送 Git。

> 配套设计与协作契约：[非线性器件引擎与 29 个电路模型工程文件设计契约](nonlinear-engine-and-circuit-models-design-20261018.md)。本文是实施后的最终记录；契约中"必做/预留/不做"的分类与本轮完成情况见第 1 节。

## 1. 用户要求与处理

| 要求 | 处理 | 状态 |
|---|---|---|
| 二极管非线性教学求解 | 新增 `Engine/Nonlinear/DiodeModel.cs`（恒压降分段线性 + Shockley/Newton）与 `Engine/Strategies/DiodeCircuitStrategy.cs`（9 类子类型解析公式）；分类器对"无晶体管且有二极管"路由到 `DiodeCircuit` | 已实现（测试见第 3 节） |
| MOSFET 放大教学求解 | 新增 `Engine/Nonlinear/MosfetModel.cs`（Level-1 平方律）与 `Engine/Strategies/MosfetAmplifierStrategy.cs`（共源/共源+Rs/共源旁路/共漏/共栅，NMOS/PMOS） | 已实现（测试见第 3 节） |
| BJT 共集电极 | `TopologyClassifier` 新增 `CommonCollector` 判定；`BjtAmplifierStrategy` 增加共集 DC 与小信号分支（NPN/PNP） | 已实现（测试见第 3 节） |
| 多级放大只预留接口 | `Engine/MultiStage/MultiStageContracts.cs`（`IMultiStageAnalysis`/`MultiStagePlan`/`MultiStageStage`）+ `MultiStageAmplifierStrategy` 明确拒绝；`B12–B14` 文件可打开但求解拒绝 | 接口已预留、级联求解未实现（后续规划） |
| 29 个 `.circuit` 教学文件 | `tools/CircuitModels.{Core,Diode,BjtBias,BjtAmp,Mos}` 生成器 + `examples/circuit-models/` 29 个文件（D01–D07、B01–B16、M01–M06） | 已生成（校验见第 3 节） |
| MOS 的 K/λ | `Models/CircuitElement.cs`/`CircuitProjectData.cs` 新增 `MosfetK`/`MosfetLambda` 并持久化；检查器本轮不新增编辑框 | 数据模型/持久化已实现，UI 编辑受限（后续） |
| JFET/耗尽型 | 符号可画、工程可存；策略明确拒绝并点名器件 | 不承诺求解（已知边界） |
| **第六轮补充：BJT 分压偏置教材估算法对照** | 用户反馈 `document/3-6题.circuit`（PNP 分压偏置，题3-12 参数）软件戴维南精确解与教材估算法答案相差约 11.1%；`BjtAmplifierStrategy` 分压自偏置第 1 步新增教材估算法对照、Q 点面板双口径显示与结构化 Values，并新增回归 | 已实现（见 2.7/3.3） |

> B06 说明：PNP 分压偏置教学文件为满足 PNP 放大区（VEC 充足），Rb2 取 200 kΩ（而非 NPN 模板的 50 kΩ），该取值及原因已写入 `B06-pnp-voltage-divider-bias.circuit` 的 `Description`；这不是数值笔误。题3-12 场景的 Rb1/Rb2/Re/Rc 与材料参数另见 2.7。

## 2. 关键实现

### 2.1 二极管模型与教学策略

- `DiodeModel` 同时提供两类模型：
  - 分段线性恒压降模型：`VF = element.ForwardVoltageDrop`（硅 0.7 V / 锗 0.3 V），导通判据 `vD ≥ VF`，反向阻断；用于整流/限幅/钳位等大信号解析分析。
  - Shockley 模型：`iD = Is·(exp(vD/(n·Vt)) − 1)`，默认 `Is = 1e-12 A`、`n = 1`、`Vt` 取 `AnalysisParameters.ThermalVoltageUt`；指数自变量钳位（`vD ∈ [−0.99, 0.99] V`），`Current/Conductance/Region` 可单元测试，`Conductance` 与数值微分一致。
- `DiodeCircuitStrategy`（`TopologyType => DiodeCircuit`）支持子类型：`HalfWaveRectifier`、`HalfWaveRectifierFiltered`、`BridgeRectifier`、`BridgeRectifierFiltered`、`SeriesLimiter`、`DoubleLimiter`、`Clamper`、`PeakDetector`、`DiodeBias`。AC 源峰值取 `ValueSI`、频率取 `FrequencyHz`；恒压降模型输出扣除 VF；每个成功解 ≥ 4 个推导步骤，`QPoint` 统一标记"二极管非线性电路"、`AcParameters` 留 null，关键量写入 `SummaryText` 与步骤 `ResultValue`/`Values`。
- 拓扑不匹配时返回 `PedagogicalSolution.Failure`（消息含"二极管"与具体原因），不抛异常。
- 数值约定：与教材手算相对误差 ≤ 1%（测试断言口径）。

### 2.2 MOSFET 模型与教学策略

- `MosfetModel` 为 Level-1 平方律：`Vth = DisplayNumber`（PMOS 存负值）、`K = MosfetK`（默认 1 mA/V²）、`λ = MosfetLambda`（0 → `ro = ∞`）；区域判定 `Cutoff/Triode/Saturation`；NMOS/PMOS 共用镜像量公式（`vov`、`vdsMag`）；`gm = 2·K·Vov`、`gds = λ·K·Vov²`。
- `MosfetAmplifierStrategy` 子类型：`CommonSource`、`CommonSourceWithSourceResistance`、`CommonSourceBypassed`、`CommonDrain`、`CommonGate`；Q 点（VG/VGS/ID/VDS）、饱和区校验、gm/ro、Av/Ri/Ro；步骤 ≥ 5，`QPoint.Ibq="—"`、`Icq=ID`、`Vceq=VDS`、`WorkingRegion` 标饱和区；数字格式统一 `ScientificNotationFormatter`，`Values` 键稳定（`ID/VGS/VDS/gm/Av/Ri/Ro`）。
- 截止 / 不满足饱和区假设时明确失败，不输出伪增益；耗尽型与 JFET 明确拒绝并点名器件。
- 分类器对单只 MOSFET/JFET 路由到 `MosfetAmplifier`，子类型调用 `MosfetAmplifierStrategy.DetectSubtype`。

### 2.3 BJT 共集电极

- `TopologyClassifier`：单只 BJT 的子类型顺序为 `CommonBase` → **`CommonCollector`（新增）** → `FixedBias/VoltageDividerSelfBias/CollectorFeedbackBias`。共集判定：集电极直接接电源轨（PNP 镜像为地轨）、发射极经 Re 有直流通路且非直接接地、基极有输入耦合或偏置网络。
- `BjtAmplifierStrategy` 新增共集分支：DC（固定/分压偏置，NPN/PNP 镜像；截止/饱和失败条件与既有策略一致）；中频小信号 `Re' = Re∥RL`、`Av = (1+β)Re'/(rbe+(1+β)Re')`、`Ri = Rb∥[rbe+(1+β)Re']`、`Ro = Re∥(rbe+Rb)/(1+β)`；步骤 ≥ 5，QPoint/AcParameters 正常填写，摘要标注"射极跟随器（共集电极）"。
- 既有共射/共基公式行为未改变（回归见第 3 节）；分压式自偏置新增的教材估算法对照见 2.7（只增对照与 Values，不改变主 Q 值口径）。

### 2.4 多级放大：只预留接口

- `MultiStageContracts.cs`：`IMultiStageAnalysis.TryPlan`、`MultiStagePlan`（`Stages`/`TopologyNote`/`PlannedImplementationOrder`）、`MultiStageStage`（`DeviceId`/`DeviceType`/`SubtypeHint` 等）。
- `MultiStageAmplifierStrategy`：`TopologyType => MultiStageAmplifier`、`SubtypeName => "Cascade"`，`SolveAsync` 返回明确失败，消息包含"多级""后续开发规划""预留"并说明当前只支持单管；`TryPlan` 返回检测到的晶体管阶段列表。
- 分类器对晶体管总数 > 1（含 BJT+MOS 混合）一律路由到该策略，**不得**用第一只晶体管套用单管公式。
- 后续实现顺序（文档化于规格与 README）：单管全类型 → 多级级联 → 二极管非线性扩展 → 混合管 → 大信号/失真。

### 2.5 29 个教学电路文件与生成器

- `tools/CircuitModels.Core/` 共享声明式 API：`CircuitSpec/ElementSpec/NetSpec/ExpectedOutcome` + `SchematicBuilder`（构建 + 结构自检：`CircuitProjectData.Validate → ToModel → NetlistExtractor.Extract`，断言声明网表分区与提取分区一一对应、GND 唯一、无浮空引脚）+ `CircuitFileWriter`（与应用相同的 JSON 选项，Version=1.2.0）+ `Verification`（加载→提取→分类→求解，与预期比对）。
- 4 个生成器：`tools/CircuitModels.Diode`（D01–D07）、`tools/CircuitModels.BjtBias`（B01–B06）、`tools/CircuitModels.BjtAmp`（B07–B16）、`tools/CircuitModels.Mos`（M01–M06）。
- 产物：`examples/circuit-models/` 下 29 个原生 `.circuit` 文件；坐标为 10 px 网格、水平/垂直导线、T 形分支用 `WireTap`、VCC/GND 轨道布局；元件参数为可解教材值，`Description` 写知识点与教材来源；文件索引与边界见 `examples/circuit-models/README.md`。

### 2.6 数据模型与分类/展示接线

- `Models/CircuitElement.cs`、`Models/CircuitProjectData.cs`：新增 `MosfetK`/`MosfetLambda` 字段并持久化（版本仍为 1.2.0，旧版软件读取忽略新字段；旧文件缺省 K=1 mA/V²、λ=0）。
- `Engine/PedagogicalSolution.cs`：新增枚举值 `DiodeCircuit`、`MultiStageAmplifier`。
- `ViewModels/AnalysisStudioViewModel.cs`：`GetTopologyDisplayName` 增加 `DiodeCircuit`/`MultiStageAmplifier` 分支，避免落到默认文案。
- `MnaLinearStrategy` 的"拒绝二极管/MOS/BJT"行为保持不变（直接调用 MNA 的负向测试继续有效）；本轮只改分类层与新增教学策略，专用策略拒绝的纯线性网络仍可回退 MNA。

### 2.7 第六轮补充：BJT 分压偏置"教材估算法对照"

**用户反馈场景**：`document/3-6题.circuit` 是教材题3-12 的 PNP 分压偏置工程（VCC=12 V、RB1=15 kΩ、RB2=470 kΩ、RE=1.3 kΩ、RC=2 kΩ、PNP 锗管 VEB=0.3 V、β=100）。软件用戴维南等效精确求解，得到 IBQ=0.488 μA、ICQ=0.049 mA、V_ECQ=11.838 V；教材用"估算法"（把基极分压器视为不受基极电流加载，先算 U_B，再算 I_EQ→I_BQ→I_CQ）得到 IBQ≈0.53 μA、ICQ≈53 μA、U_CEQ≈11.82 V，二者相差约 **11.1%**。差异来源是分压器内阻 `R_th` 与 `(1+β)R_E` 的比值（本题 `R_th/((1+β)R_E)=0.111`）——教材估算忽略了基极电流对分压器的加载。

**改进（`BjtAmplifierStrategy` 分压式自偏置，NPN/PNP 均适用）**：

- 第 1 步新增"教材估算法对照"：给出 `U_B`、`I_EQ`、`I_BQ`、`I_CQ`、`U_CEQ/U_ECQ`、与精确解的差异百分比及原因；步骤方法名标注"（附教材估算法对照）"。仍保持 **5 个教学步骤不变**。
- Q 点面板双口径显示：当分压器"足够硬"（`R_th/((1+β)R_E) ≤ 0.2`）时，Q 点主值仍为戴维南精确解，并同时显示"（教材估算≈…）"（教材两位小数舍入值）；`0.2 < 比值 ≤ 0.5` 时只在步骤/摘要给出对照；`> 0.5` 时给出"估算法不适用/误差偏大"提示，不把误差大的估算值写入 Q 点主值。
- 第 1 步新增结构化 `Values`（键名稳定）：`IBQ_exact_uA`、`ICQ_exact_mA`、`UCE_exact_V`、`Vth_V`、`Rth_ohm`、`UB_actual_V`、`UB_textbook_V`、`IBQ_textbook_uA`、`ICQ_textbook_mA`、`UCE_textbook_V`、`IBQ_textbookRounded_uA`、`ICQ_textbookRounded_uA`、`UCE_textbookRounded_V`、`EstimateDiffPercent`、`Rth_over_betaRe`。
- rbe 步骤对 `I_EQ < 1 mA` 自动改用 μA 显示，避免小电流被两位小数舍入成 0 而失真。
- 教材答案习惯把 `U_B` 保留两位小数后再算：题3-12 为 `U_B≈11.63 V → IBQ≈0.533 μA、ICQ≈53.313 μA、U_ECQ≈11.823 V`，策略同时给出未舍入与按教材两位小数舍入的两组值。
- 回归：新增 `tests/NonlinearCircuitChecks/BjtVdEstimateChecks.cs`（22 项：题3-12 精确/估算/舍入值、摘要/Q点/步骤/Values、5 步不变、分压器不够硬时不污染 Q 点主值）；`NonlinearCircuitChecks` 总数由 469 变为 **491/491**。

## 3. 测试与验证

> 以下数字为 2026-10-18 本工作区实测（Lead 统一回归 + `verify-docs` 更新断言后复跑）。均为 `dotnet run` 控制台检查工程，不是真实 WinUI 窗口端到端验收。

### 3.1 构建与新增测试

| 检查 | 命令 | 结果 |
|---|---|---|
| 主项目构建 | `dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify` | **成功，0 警告/0 错误** |
| 非线性引擎检查 | `dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj` | **Total=491 Passed=491 Failed=0**（DiodeChecks/MosChecks/BjtCcChecks/BjtVdEstimateChecks） |
| 29 文件校验 | `dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj` | **Total=259 Passed=259 Failed=0**（29 个 `.circuit` 全部通过文件数/反序列化/Validate/分类/子类型/求解/步骤≥4） |
| 单文件诊断 | `dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "document/3-6题.circuit"` | **成功**：输出工程/网表校验/分类/求解/摘要/Q点/全部步骤与 `Values`（用于对照任意 `.circuit`） |
| 二极管生成器 | `dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify` | **7/7**（D05/D06 限幅已修复） |
| BJT 偏置生成器 | `dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify` | **6/6** |
| BJT 放大生成器 | `dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify` | **10/10** |
| MOS 生成器 | `dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify` | **6/6** |

### 3.2 既有控制台回归

| 检查 | 结果 |
|---|---|
| M2SolverChallenger | Passed=155 |
| ChallengerRunner | Total=131 Passed=131 |
| M3AdversarialStress | Total=32 Passed=32 |
| M3_4EmpiricalStress | 9/9 |
| EngineV2Checks（第六轮更新 3 处断言） | **Total=306 Passed=306** |
| ReportExportChecks | 110/110 |
| WebImportChecks（第六轮更新 5 处断言/文案） | **Total=172 Passed=172** |
| NativeBenchmark30 | 30/30（100%） |
| EngineAuditChecks（独立数值审计） | 1359/1359 |
| SymbolChecks | 230/230 |
| **既有 8 套常规回归合计** | **1145/1145**（M2 155 + ChallengerRunner 131 + M3 32 + M3_4 9 + EngineV2 306 + 报告 110 + 导入 172 + 符号 230） |

### 3.3 第六轮补充回归：教材估算法对照

- `BjtVdEstimateChecks`（22 项）覆盖：题3-12 分类为 `BjtAmplifier/VoltageDividerSelfBias`、精确 Q 点（0.488 μA / 0.049 mA / 11.838 V）、教材估算（U_B=11.629 V、IBQ≈0.542 μA、ICQ≈54.177 μA、U_ECQ≈11.821 V）、教材两位小数舍入（0.533 μA / 53.313 μA / 11.823 V）、摘要/Q点/第 1 步 Values/方法名、5 步不变、`R_th/((1+β)R_E)=0.111` 与差异 +11.1%。
- 宽分压器负向用例（`R_th/((1+β)R_E)=0.66`）：Q 点主值不混入教材估算，仅步骤/摘要提示"估算法误差偏大/不适用"。
- `tests/CircuitModelChecks -- --file <path>` 单文件诊断：加载→校验→网表→分类→求解→打印全部步骤/Values；实测 `document/3-6题.circuit` 输出与上述数值一致，可直接检查任意 `.circuit`（不依赖 29 文件清单/预期表）。

测试口径说明：

- 二极管/MOSFET/BJT 教学策略的数值断言与教材手算公式 1% 对拍；不代表外部 SPICE 数值一致性。教材估算法对照是"把软件过程与教材步骤并列展示"，主 Q 点仍为戴维南精确解。
- 多级文件只校验"分类为 `MultiStageAmplifier/Cascade` 且求解明确失败、消息含多级/后续规划/预留/只支持单管"；`IMultiStageAnalysis.TryPlan` 的阶段列表在 `BjtCcChecks` 中断言。
- `MnaLinearStrategy`/`LinearNetworkAnalyzer` 对二极管/变压器/BJT/MOS/JFET 的直接拒绝断言**保持不变**（EngineV2Checks 5.1/5.3、EngineAuditChecks 直接 `TryCreate` 用例）。
- 端到端旧断言更新（`verify-docs`）：`EngineV2Checks` 3 处（二极管→`DiodeCircuit/DiodeBias` 明确拒绝并联直流回路；两个 BJT→`MultiStageAmplifier/Cascade`；NPN+PNP 混合→`MultiStageAmplifier/Cascade`），`WebImportChecks` 5 处（diode/mos 样例的分类型、策略类型、失败原因与非 Unsupported 文案）；`M2SolverChallenger` 9.7 两条同步为新策略语义（断言数不变，仍 155）。

## 4. 文档更新

本轮同步更新（保留旧历史记录，不改写旧轮次记录）：

- `README.md`：第六轮能力摘要、29 个教学电路入口、两类新增测试工程与生成器命令、教材估算法对照、最终回归数字与边界。
- `PROJECT.md`：架构图与组件职责新增非线性模型/多级预留/生成器，分类优先级改"多级 → 单管 BJT（含共集）→ 单管 MOS → 二极管 → RC/RL → RLC → MNA"，支持矩阵新增二极管与 MOSFET 行、BJT 行补共集，后续规划更新。
- `HANDOVER.md`：维护入口表新增非线性/多级/生成器/29 文件，日常操作与求解限制（二极管/MOS/共集/多级/K/λ/教材估算法对照）更新，验证数字更新。
- `CLAUDE.md`：当前架构段落与原生测试工程清单更新（新增 NonlinearCircuitChecks、CircuitModelChecks 与生成器 `--verify`）。
- `projects/README.md`：文档索引新增第六轮记录与教学电路 README，分析引擎条目更新。
- `projects/spec-first-stage-local-circuit-analysis-20260601.md`：范围、I2/I3、M2/M4、任务勾选与验收数字更新；多级级联仍列为后续规划。
- `projects/analysis-engine-technical-report.md`：分类表更新；新增 6.2 共集、6.3 二极管、6.4 MOSFET、6.5 多级预留、6.6 BJT 教材估算法对照；MNA 边界与第 10 节测试数字更新。
- `projects/data-layer-technical-report.md`：`MosfetK`/`MosfetLambda` 字段与持久化、29 个教学电路文件、单文件诊断与导入/校验测试数字更新。
- `专业名词解释.md`：新增恒压降/Shockley 模型、MOS 平方律与 K/λ、多级预留接口、教学电路模型文件等术语，更新二极管已进入求解器、共集已实现、MNA 边界与参数语义。
- `projects/presentation-layer-technical-report.md`：Q 点双口径显示（戴维南精确/教材估算）与验证数字更新。
- `projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md`：第六轮命令、结果表、能力状态与单文件诊断命令更新。
- 新增 `examples/circuit-models/README.md`：29 文件索引、打开方式、可解/仅画/多级拒绝说明、BJT/MOS 参数、教材估算法对照、`--file` 诊断、生成与校验命令、已知边界。
- 新增本文件：本轮最终记录（含第六轮补充）。

## 5. 已知边界与后续规划

1. 二极管大信号波形分析采用**分段线性恒压降模型 + 解析公式**，不是 SPICE 瞬态仿真；不覆盖非教材拓扑。
2. MOSFET 为**平方律 Level-1 模型**，忽略体效应/亚阈值/迁移率退化；不覆盖非饱和区工作点（明确失败）。
3. 耗尽型 MOS 与 JFET 不承诺求解（明确拒绝）；符号与参数语义仍可编辑/保存。
4. MOS 的 K/λ 已持久化到工程文件，但检查器本轮不提供编辑框；默认 K=1 mA/V²、λ=0。
5. 多级放大仍未实现，仅预留接口并明确拒绝；B12–B14 文件可打开但不会给出级联结果。
6. 波形分析的数值指标是教材近似公式（如滤波电容纹波），容差按 1% 对拍手算公式，不承诺与真实 SPICE 一致。
7. 自动布局只保证拓扑正确与可读，不做避障/美观最优。
8. 未在真实 WinUI 窗口做像素级/交互级验收（打开 29 个文件、点击求解、报告导出的窗口流程仍属人工验收项）；`CircuitModelChecks`/`--file` 只验证文件级加载→分类→求解，不替代窗口交互。
9. 后续规划顺序：单管全类型 → 多级级联 → 二极管非线性扩展 → 混合 BJT/MOS 非线性网络 → 大信号/失真分析；视觉识别仍在软件本体完善后的阶段。
10. `EngineV2Checks` 的二极管端到端夹具是"1 V 源 + R1 + D1"**并联**直流回路，本轮按新行为断言"分类 `DiodeCircuit/DiodeBias` + 明确拒绝并联分支"；它不验证二极管串联回路成功路径（成功路径由 `DiodeChecks` 与 `CircuitModelChecks` 的 D 组覆盖）。
11. `WebImportChecks` 的 MOS 样例来自 Web/Python 原型 benchmark，m1 的 `DisplayNumber` 还是导入器给出的 `0.002`（跨导 0.002 S），并非真实阈值电压；本轮断言只覆盖"分类 `MosfetAmplifier` + 缺栅极偏置网络的明确失败"，不把该样例当作可解 MOS 放大器；可解路径由 M01–M06 覆盖。导入器的 MOS 参数映射（threshold_voltage→Vth、transconductance→K）属于后续改进项，本轮未改 `WebCircuitGraphImporter`。
12. 二极管/多级/共集/教材估算法对照的测试断言使用失败消息/摘要关键词（如"二极管""并联""多级""后续开发规划""预留""只支持单管""教材估算法对照""11.1%"）；后续若改策略文案，需要同步更新 `EngineV2Checks`、`WebImportChecks`、`CircuitModelChecks`、`NonlinearCircuitChecks` 与本轮文档。
13. 教材估算法对照只是教学对照：主 Q 点始终为戴维南精确解；教材答案若采用估算法/舍入值，差异是教材简化而非软件错误。`R_th/((1+β)R_E) > 0.5` 时估算法不适用，软件会提示并以精确解为准。

## 6. 附录：复现命令

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "document/3-6题.circuit"
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify
dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj
dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
dotnet run --project tests/WebImportChecks/WebImportChecks.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
dotnet run --project tests/SymbolChecks/SymbolChecks.csproj
```
