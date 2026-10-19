# 软件本体代码修订记录（第七轮：教材估算法显示口径、全部结果显示两位小数、DC/AC 等效电路分组与 Win2D 真排版公式框）

> 日期：2026-10-18（Asia/Shanghai）
> 性质：按用户第七轮反馈修改 WinUI 主软件与教学资源；本轮只改代码/测试/文档，未提交或推送 Git。
> 用户反馈（原文要点）：① 不要"戴维南精确解 vs 教材估算法"双口径并列显示，BJT/MOSFET 的显示结果要按教材的计算/估算方式给出，数值要和学生手算一致（中间结果保留两位小数并代入下一步）；② 解题步骤要按"直流等效电路（DC）→ 交流等效电路（AC）"分组展示；③ 公式框排版差（Unicode 下标退化、横向滚动裁切），要求真正的上下角标/分式/根号/矩阵排版，并加大字号。
> 用户确认口径（2026-10-18）：**"不是代数化简，是数值保留两位小数"**——公式本身不得化简（例如 `(1+β)` 不合并、`I_CQ` 不近似为 `I_EQ`），只在显示数值时按显示单位最多保留两位小数（AwayFromZero），并把该舍入后的显示值代回后续公式，模拟教材手算。
> 范围：`Engine/Strategies/{BjtAmplifierStrategy,MosfetAmplifierStrategy}.cs`（整文件重写）、`Engine/{PedagogicalSolution,Strategies/*}` 显示层、`Engine/Linear/LinearNetworkAnalyzer.cs`、`Services/{TeachingDisplay,ScientificNotationFormatter,ReportExportService}.cs`、新增 `Math/`（5 个文件）、`Controls/MathFormulaView.xaml(.cs)`（重写）、`Models/AnalysisResult.cs`、`Views/AnalysisStudioPage.xaml`、`tests/{MathFormulaChecks,DisplayRoundingChecks,NonlinearCircuitChecks,NativeBenchmark30}`、现行文档与索引。
> 结论：主项目构建 **0 警告/0 错误**；新增公式排版回归 **411/411**、显示两位小数回归 **55/55**、非线性引擎回归扩至 **751/751**、29 个教学电路文件校验 **259/259**、30 题本地基准 **30/30（Accuracy=100.00%）**；既有 C# 回归全部通过（M2 155、ChallengerRunner 131、M3 32、M3_4 9、EngineV2 306、报告 110、导入 172、符号 230、独立数值审计 1359）；4 个生成器 `--verify` 为 7/7、6/6、10/10、6/6。多级级联求解仍未实现；MOS 的 K/λ 仍无检查器编辑框；公式框仍有明确限制（见第 5 节）。本轮另用 **Win32 `PrintWindow` 截图**做了真实窗口视觉验收（共射模板、题3-12 PNP、MOS 共源模板；证据见 3.5），确认公式真角标/分式、DC/AC 分组与两位小数显示；但这不是逐像素/自动化交互级验收，控制台回归仍不能替代完整窗口操作证据。

## 1. 用户要求与处理

| 用户反馈/要求 | 处理 | 状态 |
|---|---|---|
| 不要"戴维南精确 vs 教材估算法"双口径并列显示 | 显示层统一为教材估算法：BJT/MOSFET 全部偏置/拓扑的 Q 点、AC 指标、摘要与步骤数值都由教材手算链生成（每步按显示单位两位小数舍入、舍入值回代）；删除第 1 步"对照"、差异百分比、适用性判据与 Q 点双口径文案 | 已实现（`NonlinearCircuitChecks` 断言摘要不含"戴维南/精确解/11.1%"） |
| 显示数值要和学生手算一致 | 新增 `Services/TeachingDisplay.cs`（单位选择 + 两位小数 AwayFromZero + `RoundTo` 回代）；BJT/MOS 策略重写为"舍入后显示值代入下一步" | 已实现（题3-12 链见 2.1/3.2） |
| 用户确认："不是代数化简，是数值保留两位小数" | 公式保留全部项（`(1+β)`、`I_CQ=β·I_BQ` 等），只对显示数字舍入；教材链每步显示值即下一步输入 | 已实现 |
| "结果最多两位小数"适用于全部教学题型 | `ScientificNotationFormatter` 默认 precision 3→2、新增 `RoundEngineering`；FirstOrderRc/SecondOrderRlc/MnaLinear/Diode/ReportExportService/LinearNetworkAnalyzer 的显示值统一 ≤2 位 | 已实现（`DisplayRoundingChecks` 55/55） |
| BJT/MOS 步骤改"DC 等效电路 → AC 等效电路"分组 | 新增 `SolutionPhase`/`SolutionPhaseInfo`/`Step.Phase`，`ToAnalysisResult()` 生成 `StepGroups`；UI 增加组标题卡片与说明；DC 相在前（电解电容极性校核归 DC），AC 相在后 | 已实现（BJT 共射/共基 5 步、共集 6 步、MOS 7 步） |
| 公式框真排版（角标/分式/根号/矩阵/换行/字号） | 新增 `Math/MathNode.cs`、`MathLatexParser.cs`、`MathLayoutEngine.cs`、`IMathTextMeasurer.cs`、`EstimateMathTextMeasurer.cs`；`MathFormulaView` 改为 Win2D `CanvasControl` 自绘；基础字号 13.5→**16**；按宽度换行、内容自适应高度；复制 LaTeX 按钮保留 | 已实现（`MathFormulaChecks` 411/411） |
| 公式框限制必须如实说明 | array 列格式竖线不画、超宽原子整体等比缩小、Canvas 不支持文本选择、`\overline/\vec/\tilde` 等只保留内容不画装饰、非法 LaTeX 降级不抛异常 | 已知限制（见 5） |
| NativeBenchmark30 的 B08 在教材估算法下饱和 | B08（共基极第二组参数）fixture `Re` 由 1 kΩ 调整为 2 kΩ，使教材估算法下 Q 点仍处放大区；饱和拒绝路径改由 `NonlinearCircuitChecks` 覆盖 | 已实现，30/30 |
| 教材估算法可能把"软分压器"算进饱和区 | 若教材估算 Q 点 `U_CEQ≤0.2 V`（如原 B08 参数 `U_CEQ≈−2.05 V`），策略明确拒绝、不输出 QPoint/AcParameters/步骤，后端精确值也不显示 | 已实现（新增 5 项断言） |

> 分类不因相位/显示口径改变：`TopologyType`/`SubtypeName` 仍由 `TopologyClassifier` 决定（如 `BjtAmplifier/VoltageDividerSelfBias`），DC/AC 分组只是求解结果内部的展示结构。

## 2. 关键实现

### 2.1 显示层统一教材估算法与"最多两位小数"规范

- 新增 `apps/desktop-winui/Services/TeachingDisplay.cs`：
  - `QuantityKind`（Voltage/Current/Resistance/Capacitance/Inductance/Frequency/Time/Ratio）按教材习惯选显示单位：V/mV/μV、A/mA/μA、Ω/kΩ/MΩ、mF/μF/nF/pF、mH/μH/nH、Hz/kHz/MHz、s/ms/μs/ns；
  - `DisplayQuantity` 同时给出渲染文本（`Text`）与该文本对应的**舍入后 SI 值**（`Si`）；`Format/Text/RoundTo/Plain/RoundPlain/Latex/LatexNumber` 全部按 `MaxDecimals = 2`、`MidpointRounding.AwayFromZero` 舍入；负零显示为 `0`；
  - 显示是"数值保留两位小数"，**不是代数化简**：策略公式仍保留 `(1+β)` 等全部项，`RoundTo` 只把显示值回代。
- `Services/ScientificNotationFormatter.cs`：`ToLatexScientific`/`ToUnicodeScientific`/`FormatWithUnit` 默认 `precision = 2`（原 3），新增 `RoundEngineering(double)`；既有调用点保持可用，BJT/MOS 的显示数值统一走 `TeachingDisplay`。
- `BjtAmplifierStrategy.cs`、`MosfetAmplifierStrategy.cs` 整文件重写：
  - 所有显示数字（Q 点、摘要、AC 指标、LaTeX 代入式、步骤 `ResultValue`）统一经 `TeachingDisplay`；
  - 每步数值链按教材顺序把"显示值"代入下一步：分压偏置 `U_B`（空载分压）→ `I_EQ=(U_B−V_BE)/R_E` → `I_BQ=I_EQ/(1+β)` → `I_CQ=β·I_BQ` → `U_CEQ`；MOS 由 `V_G`（舍入）→ 平方律求 `I_D`（舍入）→ `V_GS/V_OV/V_DS` → `g_m` → `A_v/R_i/R_o/f_L`；
  - Q 点面板显示教材估算链值（如题3-12 的 `0.53 μA / 53 μA / 11.82 V`），不再显示戴维南精确值；摘要写明"直流等效电路/交流等效电路"来源，不含"戴维南/精确解/11.1%"双口径文案。
- 全部教学题型的显示值 ≤2 位：RC/RL、RLC、通用 MNA 2.0（`MnaLinearStrategy`+`LinearNetworkAnalyzer` 显示文本）、二极管非线性、BJT/MOS、报告导出（`ReportExportService`）统一到同一规范；`DisplayRoundingChecks` 用文本扫描（最多两位小数）覆盖上述题型与教学模板/教学电路文件。
- 题3-12（PNP 分压偏置，`document/3-6题.circuit`：VCC=12 V、RB1=15 kΩ、RB2=470 kΩ、RE=1.3 kΩ、RC=2 kΩ、锗管 V_EB=0.3 V、β=100）显示链：

  ```text
  U_B   = 12×470/485 = 11.6289… → 11.63 V
  I_EQ  = (12 − 11.63 − 0.3)/1.3 kΩ = 53.846… μA → 53.85 μA
  I_BQ  = 53.85 μA/(1+100) = 0.5331… μA → 0.53 μA
  I_CQ  = 100×0.53 μA = 53 μA
  U_ECQ = 12 − 53 μA×2 kΩ − 53.85 μA×1.3 kΩ = 11.82399… V → 11.82 V
  ```

  后端保留的戴维南精确值（不参与显示）：`IBQ=0.487767 μA`、`ICQ=0.0487767 mA`、`U_ECQ=11.8384 V`、`Vth=11.6289 V`、`Rth=14536.1 Ω`，实际基极电位约 11.636 V。
- 分压式共射教学场景（VCC=12 V、Rb1=51 kΩ、Rb2=15 kΩ、Re=1 kΩ、Rc=3 kΩ、β=100）教材估算链：`IBQ=20.1 μA`、`ICQ=2.01 mA`、`U_CEQ=3.94 V`、`rbe=1.49 kΩ`；Re 不旁路且 RL=10 kΩ（B08 教材参数场景）时 `Av=−2.25`。工作台"共射"模板带 Ce=47 μF 旁路（RL=10 kΩ），同一 Q 点下 `Av≈−155.03`；两者 Q 点相同、交流指标不同，不能混用。
- 后端精确值只进入 `QPointInfo.ExactIbq/ExactIcq/ExactVceq` 与第 1 步 `Values` 的 `IBQ_exact_uA`/`ICQ_exact_mA`/`UCE_exact_V`/`IEQ_exact_uA`（分压偏置另有 `Vth_V`/`Rth_ohm`/`UB_actual_V`），供测试/诊断/报告内部使用，**UI 不绑定**。

### 2.2 DC/AC 等效电路相位与 StepGroups

- `Engine/PedagogicalSolution.cs` 新增：
  - `SolutionPhase`：`None=0`（RC/RL/RLC/MNA/二极管等非 DC/AC 分组策略）、`DcEquivalent=1`、`AcEquivalent=2`；
  - `SolutionPhaseInfo.GetTitle/GetSubtitle`：DC 组标题"直流等效电路（DC 分析）"，说明"耦合/旁路电容视为开路、交流信号源置零，只保留直流电源与偏置网络，确定静态工作点 Q。"；AC 组标题"交流等效电路（AC 分析）"，说明"直流电源置零（VCC 交流接地）、耦合/旁路电容视为短路，晶体管用微变等效模型替代，计算中频小信号指标。"；
  - `PedagogicalSolutionStep.Phase`（默认 `None`）。
- `ToAnalysisResult()` 按步骤顺序把相邻同相位步骤归组为 `AnalysisStepGroup`（`Phase/Title/Subtitle/HasHeader/Steps`）；`None` 相位归为单一无标题组（`HasHeader=false`），保持 RC/RLC/MNA/二极管等原有平铺外观；BJT/MOS 得到 **2 个分组**（DC 段在前、AC 段在后）。分组是 UI 派生结构，不写入工程文件。
- 步骤契约：BJT 共射/共基 **5 步**（DC 1 + AC 4）、共集 **6 步**（DC 1 + AC 5）、MOS **7 步**（DC 3 + AC 4）；若电路含电解电容，每个电容在 DC 相追加一条"极性安全/元件特性"核验步骤（本次测试夹具 5 步 + 3 条 = 8 步），编号由 `RenumberSteps` 统一重排且 `StepIndex` 从 1 连续。
- DC 相第一步写明"画直流等效电路：耦合/旁路电容开路、交流信号源置零、保留 VCC/偏置网络"；AC 相首步写明"画交流等效电路：直流电源置零（VCC 交流接地）、耦合/旁路电容短路，晶体管用 h 参数（r_be、βi_b）微变模型替代"（MOS 为 gm·v_gs 模型）；AC 共集首步同样使用 h 参数说明。
- 分类不因相位改变：`TopologyType`/`SubtypeName` 由分类器与策略原有逻辑决定，`SolutionPhase` 只影响展示分组。

### 2.3 Win2D 真排版公式框（弃用 Unicode 下标转换与横向裁切）

- 新增 `apps/desktop-winui/Math/`（纯 C#，不依赖 WinUI/Win2D，控制台可测）：
  - `MathNode.cs`：语法树（Row/Run/Fraction/Sqrt/Script/Space/LineBreak/Matrix/Accent；`MathMatrixKind` 支持 matrix/array/aligned 无定界、bmatrix 方括号、pmatrix 圆括号、cases 左花括号、vmatrix 竖线；`MathAccentKind` 支持 hat/dot/ddot）；
  - `MathLatexParser.cs`：面向教学引擎输出子集的递归下降解析器，支持 `\frac`、`\sqrt`（含 `\sqrt[n]`）、`_{}`/`^{}`、撇号、`\text/\mathrm/\mathbf/\mathit`、希腊字母、关系/二元运算符、`\left/\right` 与 `\big|` 定界符、间距命令、`\\` 换行、花括号分组、matrix/cases/array/aligned 环境（列对齐与 `\hline`）；解析器**永不抛异常**，非法输入降级为可识别文本，不会让单条公式导致 UI 崩溃；
  - `MathLayoutEngine.cs`：真上下角标（0.72× 字号 + 基线偏移）、上下叠放分式（含分数线）、根号（radical + 上横线）、矩阵/cases/array 网格与 `\hline` 横线、贪心自动换行（在运算符/标点处断行）、单一不可拆分原子超宽时整体等比缩小（不横向裁切）；输出定位后的文本片元与线条/定界符几何；
  - `IMathTextMeasurer.cs` + `EstimateMathTextMeasurer.cs`：文本测量抽象与确定性估算实现，供无 Win2D 的控制台回归使用。
- `Controls/MathFormulaView.xaml(.cs)` 重写：`CanvasControl` 自绘（DirectWrite 文本），基础字号 `BaseFontSize = 16f`（原 13.5px 文本）；按控件可用宽度重新排版、`Height` 随内容自适应（内容增高时控件变高，不出现横向滚动裁切）；数学字体候选 `Cambria Math`/`Latin Modern Math`/`STIX Two Math`/`Cambria`/`Segoe UI`，CJK 回退候选 `Microsoft YaHei UI`/`Microsoft YaHei`/`SimSun`/`Segoe UI`；颜色跟随主题强调前景；右上角"LaTeX"复制按钮保留，可将原始 LaTeX 放入剪贴板。
- `Views/AnalysisStudioPage.xaml`：步骤卡片模板提取到 `Page.Resources`（`AnalysisStepCardTemplate`/`AnalysisStepGroupTemplate`），公式/代入框加大；标签文案改为"符号推导公式 / 定理方程:"与"数值代入与结果（最多保留两位小数）:"；步骤卡片头改为**两行式**（第一行"步骤号 + 标题"，第二行方法名小字、`MaxLines=2` 超出省略），避免长方法名单行放第三列时挤压标题列；推导页按 `ViewModel.Result.StepGroups` 渲染分组卡片（组标题+说明+组内步骤），无相位结果只有一组且不显示组标题。

### 2.4 测试工程新增与更新

- 新增 `tests/MathFormulaChecks`（`net9.0` 控制台，链接 `Math/*.cs` 源码、不引用 WinUI；`EstimateMathTextMeasurer` 保证无界面环境可运行）：**411 项**，覆盖解析（frac/上下标/sqrt/分组/希腊字母/矩阵环境/未知命令降级）、布局（0.72× 角标、分式叠放与居中、根号嵌套、自动换行不裁切、超宽原子等比缩小、矩阵 cases/array、12/14/16/18px 字号结构保持），以及教学引擎真实公式（r_be/V_th/I_BQ/RLC 等）在 640/320 宽度下的排版断言。
- 新增 `tests/DisplayRoundingChecks`（`net9.0-windows` 控制台，链接 `TeachingDisplay`/`ScientificNotationFormatter`/`Engine/**` 等生产源码）：**55 项**，遍历 RC/RL/RLC/MNA（含受控源、戴维南、频响）/二极管（D01/D05 教学文件）/BJT（B02/B10 教学文件与教学模板）/MOS（M01 教学文件与教学模板）的 Q 点、AC 参数、摘要、步骤文本，断言所有显示数字最多两位小数。
- `tests/NonlinearCircuitChecks`（**751/751**，原 491）：
  - 新增 `BjtDcAcPhaseChecks`（BJT 四类偏置 NPN/PNP、共基、共集分压/固定偏置、2 个 MOS 子类型的 DC/AC 相位与教材估算链、逐项显示两位小数、电解电容极性步骤归 DC、后端精确值；新增"教材估算法饱和拒绝"5 项断言：原 B08 参数 VCC=15/Rb1=120k/Rb2=60k/Rc=3k/Re=1k/β=80 下 `U_B=5 V`、`I_EQ=4.3 mA`、`I_BQ=53.09 μA`、`I_CQ=4.25 mA`、`U_CEQ≈−2.05 V`，仍分类 `BjtAmplifier/VoltageDividerSelfBias`，但 `!IsSuccess`、失败文案含"饱和区"、不输出 QPoint/AcParameters/步骤）；
  - 新增 `TestQuantity`（把 "0.53 μA"/"11.82 V"/"10.31 kΩ" 等显示文本解析回 SI 便于数值比较）；
  - 重写 `BjtVdEstimateChecks`（题3-12 教材估算链、Q 点显示值、摘要不含双口径、精确值仅后端、5 步与 2 个 StepGroups、逐项 ≤2 位小数）；更新 `BjtCcChecks`（共集 6 步、相位、教材估算链）。
- `tests/NativeBenchmark30`（**30/30，Accuracy=100.00%**）：B08 共基极第二组参数 `Re` 由 1 kΩ 调整为 2 kΩ。原因：教材原参数在"空载分压忽略基极电流加载"的教材估算法下 `U_CEQ≈−2.05 V` 已进饱和区，属负向行为；该拒绝路径改由 `NonlinearCircuitChecks` 覆盖，基准题只承担"30 题成功求解+数值对拍"。调整后教材链：`U_B=5 V`、`I_EQ=2.15 mA`、`I_BQ=26.54 μA`、`I_CQ=2.12 mA`、`U_CEQ=4.34 V`、`hie=1.03 kΩ`、`hib=12.72 Ω`、`Au=233.01`、`Ri'=12.64 Ω`、`Ro'=3 kΩ`（与实测一致，容差 ≤1%）。

### 2.5 维护经验：Windows PowerShell 5.1 的 ANSI 编码陷阱

本机 `pwsh` 实为 Windows PowerShell 5.1；其 `Get-Content`/`Set-Content` 默认按 ANSI 编码读写，直接管道回写 UTF-8 中文源码会破坏文件（本轮 `BjtAmplifierStrategy.cs` 一度出现 **359 个 U+FFFD 替换字符**，已用文件写入工具按原文重写恢复并重新构建/回归通过）。维护约定：

- 修改含中文的源码/文档只用文件读写工具（read/edit/write），或用显式 UTF-8 IO（读 `Get-Content -Encoding UTF8`；写回避免 PS 5.1 的 `-Encoding UTF8` 附加 BOM，可用 .NET `UTF8Encoding($false)`）；
- 改动后用构建 + 回归验证，并抽查中文/符号文字是否出现 U+FFFD 替换字符；
- 不要把 `pwsh` 的默认编码假设为 UTF-8；该经验已写入 `HANDOVER.md` 的维护注意点。

## 3. 测试与验证

> 以下为 2026-10-18 本工作区最终回归实测（Lead 统一全量复核；数字以最终回归为准）。均为 `dotnet run` 控制台检查工程；另附 3.5 的 Win32 截图窗口验收（静态截图级，非自动化交互测试）。

### 3.1 构建与新增/更新测试

| 检查 | 命令 | 结果 |
|---|---|---|
| 主项目构建 | `dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify` | **成功，0 警告/0 错误** |
| 公式排版回归（新增） | `dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj` | **Total=411 Passed=411 Failed=0** |
| 显示两位小数回归（新增） | `dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj` | **Total=55 Passed=55 Failed=0** |
| 非线性引擎回归（含新增相位/估算检查） | `dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj` | **Total=751 Passed=751 Failed=0** |
| 29 文件校验 | `dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj` | **Total=259 Passed=259 Failed=0** |
| 30 题本地基准 | `dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj` | **Total=30 Passed=30 Failed=0 Accuracy=100.00%** |
| 二极管生成器 | `dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify` | **7/7** |
| BJT 偏置生成器 | `dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify` | **6/6** |
| BJT 放大生成器 | `dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify` | **10/10** |
| MOS 生成器 | `dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify` | **6/6** |

### 3.2 既有控制台回归

| 检查 | 结果 |
|---|---|
| M2SolverChallenger（BJT 显示断言改为教材估算法 20.1 μA/2.01 mA/3.94 V） | **155/155** |
| ChallengerRunner | **131/131** |
| M3AdversarialStress | **32/32** |
| M3_4EmpiricalStress | **9/9** |
| EngineV2Checks（PNP/硅锗段断言改为教材估算法数值） | **306/306** |
| ReportExportChecks | **110/110** |
| WebImportChecks | **172/172** |
| EngineAuditChecks（独立数值审计，未改） | **1359/1359** |
| SymbolChecks（未改） | **230/230** |

### 3.3 关键数值核对

- **题3-12 显示链**（PNP 分压偏置，`document/3-6题.circuit`）：`U_B=11.63 V → I_EQ=53.85 μA → I_BQ=0.53 μA → I_CQ=53 μA → U_ECQ=11.82 V`；Q 点面板 `0.53 μA / 53 μA / 11.82 V`，摘要与第 1 步 `Values` 一致；摘要不含"戴维南/教材估算对照/精确解/11.1%"，工作区注明"放大区"与 PNP 极性约定。
- **题3-12 后端精确值**（`QPointInfo.Exact*` 与第 1 步 `Values`，不显示）：`IBQ=0.487767 μA`、`ICQ=0.0487767 mA`、`U_ECQ=11.8384 V`、`Vth=11.6289 V`、`Rth=14536.1 Ω`、实际基极电位 ≈11.636 V。
- **步骤与分组**：题3-12 共 5 步（DC 1 + AC 4），`StepGroups` 两组，组标题分别含"直流等效电路"/"交流等效电路"且 `HasHeader=true`；每步 `Phase` 均非 `None`，DC 步连续在前、AC 步连续在后。
- **共射教材估算场景**：`IBQ=20.1 μA`、`ICQ=2.01 mA`、`U_CEQ=3.94 V`、`rbe=1.49 kΩ`；Re 不旁路（B08 教材参数、RL=10 kΩ）时 `Av=−2.25`；工作台共射模板（Ce 旁路）同 Q 点下 `Av≈−155.03`。
- **NativeBenchmark30 B08**（共基极，VCC=15 V、Rb1=120 kΩ、Rb2=60 kΩ、Rc=3 kΩ、Re=2 kΩ、β=80）：教材链 `U_B=5 V → I_EQ=2.15 mA → I_BQ=26.54 μA → I_CQ=2.12 mA → U_CEQ=4.34 V`，`hie=1.03 kΩ`、`hib=12.72 Ω`、`Au=233.01`、`Ri'=12.64 Ω`、`Ro'=3 kΩ`；实测 `IBQ=2.654e-5 A`、`ICQ=2.12e-3 A`、`VCEQ=4.34 V`、`Au=233.01`、`Ri=12.64 Ω`、`Ro=3000 Ω`，误差 0%（容差 ≤1%）。
- **教材估算法饱和拒绝**：原 B08 参数（VCC=15、Rb1=120k、Rb2=60k、Rc=3k、Re=1k、β=80）下 `U_B=5 V`、`I_EQ=4.3 mA`、`I_BQ=53.09 μA`、`I_CQ=4.25 mA`、`U_CEQ≈−2.05 V`；策略仍分类 `BjtAmplifier/VoltageDividerSelfBias` 但明确失败（文案含"饱和区"），不输出 QPoint/AcParameters/步骤（5 项断言）。

### 3.4 测试口径说明

- BJT/MOS 教学策略的数值断言与教材手算公式对拍（容差 ≤1%，教材舍入链按显示值逐步回代）；不代表外部 SPICE 数值一致性。
- `MathFormulaChecks` 用确定性估算测量器验证解析/布局/换行/缩放几何，不渲染真实 DirectWrite 字形；真实 DirectWrite 字形已由 3.5 的窗口截图抽查（代表公式/模板路径），但未做像素级自动比对。
- `DisplayRoundingChecks` 用正则扫描显示文本（排除科学计数法、LaTeX 下标/命令），不检查图表/画布像素。
- 本轮未重新运行 `npm run check`（独立 Web/Python 原型），该命令也不检查 WinUI；本轮未复跑 `NativeBenchmark30 -- --roundtrip`（历史结果 31/31 网表等价）。

### 3.5 真实窗口截图验收（Win32 PrintWindow，2026-10-18）

> 工具与证据：`tools/ui-verify/Capture-Ui.ps1` + `Run-CaUiFlow.ps1`（Win32 `PrintWindow(flags=2)` 截图、`SetWindowPos` 固定 1920×1118 物理像素 @150% DPI、`SendInput` 点击/滚轮、`System.Windows.Forms.SendKeys` 输入、UIA 辅助定位）；before 证据在 `tools/ui-verify/evidence/before/`（旧构建 04:59），after 在 `tools/ui-verify/evidence/after-fixed/`（新构建 17:23）。`tools/ui-verify/evidence/after/` 保留首次截图，其中暴露并已修复“步骤卡片头被长方法名挤压”的布局问题。

已人工核对的项：

- **公式框**：旧构建公式显示 `V_th`/`R_b1` 一类下划线且长公式横向裁切；新构建为真上下角标（0.72×）、上下叠放分式（如 `R_i = 11.59 kΩ ∥ 1.49 kΩ = 1.32 kΩ`）、基础字号 16、按宽度换行且无横向裁切。共射模板第 1 步、第 4 步与底部“解题推导抽屉”中的公式均正常渲染，CJK/单位/希腊字母与 `∥` 无豆腐块。
- **DC/AC 分组**：解题推导页出现“直流等效电路（DC 分析）”“交流等效电路（AC 分析）”两个组标题与说明；共射模板第 1 步归 DC、第 2–5 步归 AC；MOS 模板第 1 步归 DC；步骤卡片头为两行式（步骤号+标题一行、方法名小字第二行），不再因长方法名挤压标题列。
- **数值显示**：实时指标 Q 点 `IBQ=20.1 μA、ICQ=2.01 mA、VCEQ=3.94 V`，交流 `Av=-155.03、Ri=1.32 kΩ、Ro=3 kΩ、fL=31.92 Hz、fH=456.52 kHz（模型估算）、BW=456.49 kHz`；均为教材估算链的 ≤2 位小数显示。
- **题3-12**（`document/3-6题.circuit`，PNP 分压偏置）：在窗口中打开并求解后，Q 点显示 `IBQ=0.53 μA、ICQ=53 μA、U_ECQ=11.82 V`；第 1 步为教材估算法公式（`U_B=V_CC·R_b2/(R_b1+R_b2)`、`I_EQ=(V_CC−U_B−V_EB)/R_E`、`I_BQ=I_EQ/(1+β)`、`I_CQ=β·I_BQ`、`U_ECQ=…`），显示文本无“戴维南精确/教材估算对照/11.1%”旧双口径文案（证据 `after-fixed/08_pnp_loaded.png` … `13_pnp_qpoint_region.png`）。
- **未覆盖**：逐像素自动比对、深浅色主题切换、150% 以外 DPI 与窗口尺寸组合、自动化交互断言；`PrintWindow` 是静态截图，不等同 WinUI UI 测试。

## 4. 文档更新

本轮同步更新（保留第六轮及更早修订记录原文，不改写历史轮次）：

- 新增本文件：第七轮最终记录（用户要求与处理、关键实现、测试数字、文档更新、边界、复现命令）。
- `README.md`：当前能力摘要（教材估算法显示+两位小数、DC/AC 分组、Win2D 公式排版）、新增测试工程、回归数字与命令、公式框限制说明。
- `PROJECT.md`：架构图与组件职责新增 `Math/*` 渲染、`TeachingDisplay`、`SolutionPhase`/`StepGroups`；结果契约补 `Exact*` 与分组；支持矩阵补"公式排版/显示两位小数/DC-AC 分组"行；后续规划更新。
- `HANDOVER.md`：维护入口（`Math/`、`TeachingDisplay`、`StepGroups`/`Phase`）、日常操作、求解限制（教材估算显示 vs 后端精确、软分压器饱和、两位小数规范、公式框限制）、验证命令与数字、PowerShell 5.1 编码注意事项。
- `CLAUDE.md`：当前架构段落（Win2D 公式自绘、DC/AC 分组、教材估算法显示、后端 `Exact*`）与原生测试工程清单（新增 MathFormulaChecks/DisplayRoundingChecks）。
- `projects/analysis-engine-technical-report.md`：6.1 BJT（教材估算法手算链+后端精确保留，删除双口径）、6.2 共集、6.4 MOS（DC/AC 相位与舍入链）、6.6 改写为"教材估算法数值口径"；第 10 节测试数字。
- `projects/presentation-layer-technical-report.md`：公式框改 Win2D 自绘（能力与限制）、推导 DC/AC 分组卡片 UI、两位小数显示、`Exact*` 不显示、标签文案；验证数字。
- `projects/data-layer-technical-report.md`：`QPointInfo.ExactIbq/ExactIcq/ExactVceq`（后端诊断、不绑定 UI）、`AnalysisStepGroup`/`StepGroups`（UI 派生、不改工程序列化）、无工程文件版本变化。
- `projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md`：第七轮命令、结果表、能力状态（公式排版、全部题型两位小数、DC/AC 分组、BJT/MOS 教材估算法显示、后端精确保留）。
- `专业名词解释.md`：新增/修订术语（教材估算法显示口径、两位小数显示规范、直流/交流等效电路、微变等效模型、LaTeX 排版/Win2D、StepGroups/求解阶段、后端精确值 Exact*）。
- `projects/README.md`：文档索引新增第七轮记录。
- `examples/circuit-models/README.md`：注明 Q 点为教材估算法两位小数显示、精确值仅后端保留，29 文件校验仍 259/259。
- `projects/spec-first-stage-local-circuit-analysis-20260601.md`（经 Lead 扩展写作用域）：frontmatter 复核日期、范围、I2/I3、I9、M2/M4、DC/AC 端到端路径、任务勾选与验收数字更新为第七轮现状；第六轮"教材估算法对照"条目保留并标注已被第七轮单一口径取代。
- 窗口截图验收与工具：新增 `tools/ui-verify/`（`Capture-Ui.ps1`、`Run-CaUiFlow.ps1`、`_debug/*` 开发脚本，`evidence/before/`、`evidence/after-fixed/` 截图证据与 `evidence/notes.md` 逐项对照）；同步修订 `README.md`、`HANDOVER.md`、`projects/presentation-layer-technical-report.md`、`projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md` 中"窗口未验收"的旧表述为"已做静态截图级验收，未做自动化像素/交互验收"。

> 范围说明：`projects/wzh-no-git-worktree-eng-review-20260529-021023.md`、`projects/technical-document-review-*` 等历史文档保持历史原样（其中"教材估算法对照/Unicode 公式"等描述不作为现行口径）；现行口径以本文及上述现行文档为准。

## 5. 已知边界与后续规划

1. **显示口径**：UI 显示教材估算法（含显示两位小数后回代），后端保留戴维南精确值；两者数值可能相差明显（题3-12 约 11%），这是教材估算/舍入约定，不是计算错误。若后续需要"精确值视图"，需另设诊断入口，不能混入教学显示。
2. **教材估算法的局限**：软分压器（`R_th` 相对 `(1+β)R_E` 不够小）时教材估算法可能进入饱和/截止区；此时策略按放大区假设**明确拒绝**，不把失败工作点包装成成功结果，也不显示后端精确值。
3. **高频 f_H 仍为模型估算**：单管 BJT 的 `f_H` 由 Cπ/Cμ 固定模型参数与密勒倍增估算，未做真实频率扫描或完整器件寄生参数拟合；共集第 6 步为下限截止频率估算，共基不单独推导截止频率。
4. **MOS 的 K/λ**：已持久化到工程文件（`MosfetK`/`MosfetLambda`），检查器仍无编辑框；默认 K=1 mA/V²、λ=0。
5. **多级放大未实现**：≥2 晶体管仍分类 `MultiStageAmplifier/Cascade` 并明确拒绝，只预留 `IMultiStageAnalysis`；B12–B14 可打开/编辑/画图但不求解。
6. **公式框限制**：array 列格式中的竖线不画（列对齐保留，`\hline` 横线会画）；单一不可拆分原子超宽时整体等比缩小；Canvas 自绘文本**不支持选择/复制局部文本**（只能复制整条原始 LaTeX）；`\overline/\vec/\tilde/\underline` 等命令只保留内容、不画装饰（`\hat/\dot/\ddot` 有装饰）；未知 LaTeX 命令降级显示、不崩溃；不嵌入 KaTeX/MathML，也不是完整 TeX 排版引擎。
7. **公式视觉仅截图级验收**：控制台只验证解析/布局几何；2026-10-18 已用 Win32 `PrintWindow` 在 150% DPI、窗口 1920×1118 下对共射模板、题3-12 PNP、MOS 共源模板的公式/分组/两位小数做静态截图核对（3.5），但逐像素自动比对、深浅色主题、其它 DPI/窗口尺寸组合与自动化交互仍未验收。
8. **显示两位小数是显示约定**：不改变后端计算精度；报告内部数值字段与显示文本可能不同（JSON/CSV 式数值字段不以显示文本反解析）。
9. **无 SPICE 对拍**：二极管/MOS/BJT 数值断言对教材手算公式（≤1%），不是与 ngspice 等外部仿真器的一致性验证。
10. **MNA 边界未变**：`MnaLinearStrategy`/`LinearNetworkAnalyzer` 对二极管/MOS/JFET/BJT/变压器/VccPort 的直接拒绝行为保持；无 AD/DA、运放、互感。
11. **B08 基准参数调整**：NativeBenchmark30 只承担"成功求解+数值对拍"，教材原参数下的饱和拒绝由 `NonlinearCircuitChecks` 覆盖；调整理由已写入 `Problems.cs` 注释与本文，不应被误读为"为了过测试改题"。
12. **分类不因显示/相位改变**：DC/AC 分组只是展示结构，不新增拓扑类型；`SolutionPhase` 仅三值，其他策略为 `None`。
13. **工程文件无版本变化**：仍为 `1.2.0`；`Exact*`/`StepGroups` 不序列化；旧工程可直接打开。
14. **未提交 Git**：本轮改动保留在工作区，未做提交/推送；用户正在运行的客户端可能是旧构建，需重新构建并重启才能看到新公式框/分组/两位小数显示。
15. **后续规划**：多级级联求解 → 混合 BJT/MOS 非线性网络 → JFET/耗尽型求解 → 大信号/失真分析 → 公式框扩展（更多装饰/可选文本选择方案）→ 完整 ERC 与外部 SPICE 数值对照；视觉识别仍安排在软件本体完善之后。

## 6. 附录：复现命令

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj
dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "document/3-6题.circuit"
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
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
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
dotnet run --project tests/SymbolChecks/SymbolChecks.csproj
```
