# 软件本体代码修订记录（第八轮：BJT 信号源内阻 Rs/电解耦合电容/Aus 支持、B07 教学变体、保存保留工程名与 M2 测试维护）

> 日期：2026-10-18（Asia/Shanghai）
> 性质：按用户第三轮反馈（第八轮）修复 BJT 教学策略对"信号源内阻 + 电解输入耦合电容"拓扑的拒绝问题，并修复一个保存改名缺陷、维护既有测试。本轮只改代码/测试/文档，未提交或推送 Git。
> 用户反馈（原文要点）：用户把 `examples/circuit-models/B07-common-emitter-bypassed.circuit` 改为 vs → R6=50 Ω（信号源内阻）→ C3=47 μF 电解输入耦合电容（正极接基极）→ 基极的变体，并把参数改为 Rb1=33k、Rb2=10k、Rc=1k、Re=1k、RL=1k、Ce=47μF、C2=10μF、VCC=12V、β=100；软件旧版只识别 Rc/Re/Rb1/Rb2/RL，R6 落入"未覆盖电阻支路"而**明确失败**，无法求解该教材变体。
> 原因诊断：① BJT 策略的电阻识别集合没有"信号源内阻"角色，R6 被当成未覆盖支路；② 原输入耦合电容识别要求电容直接跨接输入网与交流源正端，R6 串入后该判定失效；③ 电解电容此前按引脚 `"1"/"2"` 取网络，电解电容的引脚是 `+/-`，导致电解输入耦合/旁路电容漏识别、极性核验步骤缺失；④ 保存工程时 `ProjectName` 被强制改写为文件名，教材示例被改名、生成器 `--verify` 工程名校验失败；⑤ 第七轮公式框重写后，`tests/M2SolverChallenger` Suite 7 仍在反射调用已删除的 `MathFormulaView.FormatLatexToUnicode`（此前只因 `bin/Debug` 为旧构建而暂时通过）。
> 范围：`Engine/Strategies/BjtAmplifierStrategy.cs`（Rs/Aus/电解电容/诊断文案/τ1）、`Models/AnalysisResult.cs`（`AcParametersInfo` 新增字段）、`Views/AnalysisStudioPage.xaml`（实时指标卡 Rs/Aus 两行 + AC 指标卡 8 行 `RowDefinition` 叠字修复）、`Services/ReportExportService.cs`（报告导出同步两行）、`ViewModels/AnalysisStudioViewModel.cs`（保存保留工程名）、`examples/circuit-models/B07-common-emitter-bypassed.circuit`、`examples/circuit-models/D06-double-limiter.circuit`、`tools/CircuitModels.BjtAmp/Definitions.cs`、`tests/NonlinearCircuitChecks/BjtSourceResistanceChecks.cs`（新增）、`tests/M2SolverChallenger`（Suite 7 与保存回归）、`tools/ui-verify/evidence/round8/`（窗口截图证据）、现行文档与索引。
> 结论：主项目构建 **0 警告/0 错误**；非线性引擎回归 **822/822**（+71 条 Rs/电解电容/负向断言）、`M2SolverChallenger` **156/156**（Suite 7 反射改为 `MathRendering` 新 API + 新增保存保留工程名断言）、29 个教学电路文件校验 **259/259**、公式排版 411/411、显示两位小数 55/55；其余回归全部通过（ChallengerRunner 131、M3 32、M3_4 9、EngineV2 306、报告 110、导入 172、30 题基准 30/30、独立审计 1359、符号 230）；4 个生成器 `--verify` 为 7/7、6/6、10/10、6/6。B07 教学变体已可正常求解（Q 点/AC 指标/Rs/Aus/fL 与 DC/AC 分组），保存不再改写工程名。**公式框、显示口径与 DC/AC 分组沿用第七轮，无回退。** 已用最终构建（2026-10-18 18:30:46，`bin\_verify\CircuitAssistant.exe`）在真实窗口（Win32 `PrintWindow`，150% DPI，1920×1118）对 B07 变体与普通模板做**静态截图级**窗口验收，并据该验收修复了实时指标 AC 卡 Rs/Aus 两行重叠的 UI 缺陷（修复见 2.8、验证见 3.4）；未做逐像素自动比对、深浅主题、其它 DPI 或自动化交互断言。

## 1. 用户反馈与处理

| 用户反馈/问题 | 原因诊断 | 处理 | 状态 |
|---|---|---|---|
| B07 变体（vs→R6 50 Ω→C3 47 μF 电解→基极）求解被"未覆盖电阻支路"拒绝 | 电阻识别集合无 Rs 角色；输入耦合电容识别只认"直接跨输入网与交流源正端"；电解电容按 `"1"/"2"` 取网导致漏识别 | `BjtAmplifierStrategy` 新增信号源内阻 Rs 识别（源 pos 网—输入耦合电容—基极，中间恰一只电阻；串联多只/复杂网络仍明确失败）；电解电容按 `+/-` 引脚取网络；共射四类偏置、共基、共集、NPN/PNP 全部支持 | 已实现（`BjtSourceResistanceChecks` 覆盖） |
| 需要教材式的源电压放大倍数 | 旧输出只有 Av/Ri/Ro，没有计入源内阻分压的指标 | 新增 `Aus = Av·Ri/(Rs+Ri)`（共基用 `Au·ri'/(Rs+ri')`）；`AcParametersInfo` 新增 `HasSourceResistance`/`SourceResistance`/`SourceVoltageGain`；实时指标卡与报告导出仅在识别到 Rs 时显示"信号源内阻 Rs""源电压增益 Aus"两行 | 已实现 |
| 源内阻影响下限截止频率 | 旧 `fL` 输入时间常数用 `Ri·C1`，未计 Rs | 改为 `τ1=(Rs+Ri)·C1`（无 Rs 时保持 `Ri·C1`）；B07 变体 `fl1=1/[2π(50+1.23k)·47μF]=2.65 Hz`，与原 `fl2`/`fl_e` 合成 `fL≈41.7 Hz` | 已实现 |
| 失败信息无法定位未覆盖电阻 | 旧文案只说"存在未覆盖电阻支路" | 改为可诊断文案：列出元件 Id 与两端网名，并列出支持位置（Rc、Re、Rb1/Rb2、集电极反馈 RB、RL、Rs=源与输入耦合电容之间），提示简化拓扑 | 已实现（负向断言检查 Id/网名/支持角色） |
| 保存后教材示例被改名 | `SaveProjectCoreAsync` 旧代码每次保存都 `data.ProjectName = 文件名` | 仅"未命名电路工程"（空或默认名）回退到文件名，其余保留工程标题；D06 示例被改名的 `ProjectName` 已恢复为"双向限幅电路（双管双偏置）" | 已实现（M2 10.8b 断言） |
| B07 教学文件与生成器不一致 | 文件被用户手工改动、生成器定义仍是旧 B07 | 保留用户手工布局，仅修正 `ProjectName`/`Description`（标题"分压式偏置共射放大电路（Ce 旁路）"，描述补 Rs/电解 C3/Aus/fL 知识点）；`tools/CircuitModels.BjtAmp/Definitions.cs` 仅 B07 定义改为该变体语义（新增本地 `PolarizedCapacitor` helper），未运行写盘模式，`--verify` 10/10 | 已实现 |
| M2 Suite 7 反射调用已删除 API | 第七轮公式框从 Unicode 转写改为 Win2D 真排版后，`MathFormulaView.FormatLatexToUnicode` 不存在；旧断言只因旧 `bin/Debug` 而暂时通过 | Suite 7 改为反射验证 `MathRendering.MathLatexParser/MathLayoutEngine/EstimateMathTextMeasurer`，并对 `\omega_0` 角标、r_be 分式、bmatrix 矩阵做真实布局断言；另新增 ViewModel 保存保留工程名回归；M2 总数 155→**156** | 已完成 |
| 实时指标 AC 卡新增 Rs/Aus 两行后窗口内叠字 | AC 指标卡 Grid 原只有 7 行定义，新增的 Rs/Aus 落在 `Grid.Row=6/7`，Row=7 与最后一行重叠（首拍截图 `13_b07_ac_metrics_region.png` 可见两行文字重叠） | `Views/AnalysisStudioPage.xaml` 的 AC 指标卡补足 **8 行** `RowDefinition`（Row=6 "信号源内阻 Rs"、Row=7 "源电压增益 Aus"，均绑定 `HasSourceResistance` 可见性）；修复后用新构建重拍 `11b/12b/13b/14b` 截图，两行显示正常、普通模板不显示 | 已修复（窗口截图验收） |

> 回归保护：**无 Rs 的电路输出与上一轮完全一致**（`HasSourceResistance=false`、`SourceResistance`/`SourceVoltageGain` 保持"—"、Values 不含 `Rs_ohm`/`Aus`、Av/Ri/fL 数值不变）；第七轮的教材估算法显示、两位小数、DC/AC 分组、后端 `Exact*` 不显示等行为均未回退。

## 2. 关键实现

### 2.1 信号源内阻 Rs 识别与 Aus

- 在 `BjtAmplifierStrategy` 的输入网络识别处（原"输入耦合电容 C1"逻辑之后）新增：
  - 输入网 `inputNet`（共射/共集为基极网 `bNet`，共基为发射极网 `eNet`）与交流源正端网 `acSourcePosNet` 之间若直接跨接电容，仍按原逻辑识别为 C1、`Rs=0`；
  - 否则对每个跨输入网的电容，检查其"远端网"与 `acSourcePosNet` 之间**恰有一只电阻**；命中则该电阻为信号源内阻 `Rs`、该电容为输入耦合电容 C1（如 vs—R6—C3—基极）；串联多只电阻或更复杂网络不猜测，仍由未覆盖电阻诊断给出元件与网名；
  - 识别到的 Rs 加入 `identifiedResistors`，因此不再触发未覆盖电阻拒绝；
  - 适用于共射四类偏置（固定/分压/集电极反馈/共基）、共集、NPN/PNP；共基的输入网为发射极网，同样支持。
- 源电压放大倍数（教材口径，用显示舍入后的 Av/Ri 计算）：

  ```text
  共射/共集：Aus = Av·Ri/(Rs+Ri)
  共基：     Aus = Au·ri'/(Rs+ri')     // Au 同相，ri' 为共基输入电阻
  ```

  - 步骤 3/4 的 Av（或 Au）公式、代入式与结果文案在识别到 Rs 时附带 `A_us` 项；摘要追加"信号源内阻 Rs = …，源电压放大倍数 Aus = …"；
  - 第 1 步 `Values` 新增稳定键 `Rs_ohm`、`Aus`；无 Rs 时不写入这两个键；
  - `AcParametersInfo` 新增 `HasSourceResistance`（bool）、`SourceResistance`、`SourceVoltageGain`（显示文本）；无 Rs 时 `HasSourceResistance=false`、两个显示值为"—"；
  - 实时指标卡（`Views/AnalysisStudioPage.xaml`）新增"信号源内阻 Rs:"与"源电压增益 Aus:"两行（仅 `HasSourceResistance` 时可见）；报告导出（`ReportExportService`）在 AC 参数表中同步追加"信号源内阻 Rs"与"源电压增益 Aus（含 Rs）"两行。

### 2.2 电解输入耦合电容识别与极性核验

- 电容网络取法新增 `CapNets(cap)`：电解电容（`ElementType.CapacitorPolarized`）用 `+`/`-` 引脚取网，普通电容用 `"1"/"2"`；输入/输出耦合电容、发射极旁路电容、电解电容检测均改用该函数，避免电解电容漏识别。
- 电解输入耦合电容可被识别为 C1；DC 相在 Q 点之后追加一条电解电容极性核验步骤（正向为"元件特性"、反向为"极性安全"Warning 并写摘要告警），`StepIndex` 由 `RenumberSteps` 连续重排。B07 变体因此为 **DC 2 步（Q 点 + C3 极性核验）+ AC 4 步 = 6 步**，`StepGroups` 为 DC(2)/AC(4) 两组。
- 反接电解电容仍可求解，但给出 Warning 步骤与"极性反接"摘要告警（不静默忽略）。

### 2.3 下限截止频率区分 Rs

- 输入耦合电容时间常数：`τ1=(Rs+Ri)·C1`（无 Rs 时 `τ1=Ri·C1`），`fl1=1/(2πτ1)`；其余 `fl2=1/[2π(Rc+RL)C2]`、`fl_e=1/(2πRe'·Ce)` 不变，`fL=√(fl1²+fl2²+fl_e²)` 教学估算。
- B07 变体：`fl1=2.65 Hz`、`fl2=7.96 Hz`、`fl_e=40.85 Hz → fL≈41.7 Hz`；`fH≈1.76 MHz` 仍为第七轮的 Cπ/Cμ 模型估算（非扫描结果）。

### 2.4 未覆盖电阻可诊断文案

- 拒绝文案改为：`存在教学模型未覆盖的电阻支路：R9（netA–netB）、…。当前 BJT 教学模型只识别以下电阻位置：Rc（集电极–电源轨）、Re（发射极–参考轨）、Rb1/Rb2（基极–VCC / 基极–GND 分压偏置）、集电极反馈偏置的 RB（基极–集电极）、RL（输出耦合电容之后的负载）、Rs（交流信号源与输入耦合电容之间的信号源内阻，如 vs–Rs–耦合电容–基极）。请检查该电阻的连接位置，或把电路简化为上述教学拓扑后重试。`
- 负向回归断言：失败文案含"未覆盖"、元件 Id（如 R9）与两端网名、并列出支持位置（Rc/Re/Rb1/Rb2/RL/Rs）。

### 2.5 保存保留工程名（真实缺陷修复）

- `ViewModels/AnalysisStudioViewModel.cs` 的 `SaveProjectCoreAsync` 旧行为：每次保存都把 `data.ProjectName = 文件名`，导致教材示例（B07/D06 等）被改名、`CircuitModels.* --verify` 的工程名校验失败。
- 新行为：保存前仅当 `data.ProjectName` 为空或等于默认"未命名电路工程"时回退到文件名；其余保留工程标题。保存成功后 `ProjectName` 同步为 `data.ProjectName`。
- 工作区中 D06 因旧缺陷被改名的 `ProjectName` 已恢复为"双向限幅电路（双管双偏置）"；B07 文件保留"分压式偏置共射放大电路（Ce 旁路）"。
- 新增回归：`tests/M2SolverChallenger` 10.8b "保存保留工程标题（旧缺陷：每次保存把 ProjectName 改写为文件名）"。

### 2.6 B07 教学变体与生成器同步

- `examples/circuit-models/B07-common-emitter-bypassed.circuit` 保留用户手工布局（13 个元件），仅修正 `ProjectName` 与 `Description`：
  - 标题："分压式偏置共射放大电路（Ce 旁路）"；
  - 描述补充信号源内阻 Rs（R6=50 Ω）、47 μF 电解输入耦合电容 C3（正极接基极）、`Aus=Av·Ri/(Rs+Ri)`、`τ1=(Rs+Ri)C1` 知识点；
  - 参数：VCC=12 V、Rb1=33 kΩ、Rb2=10 kΩ、Rc=1 kΩ、Re=1 kΩ、RL=1 kΩ、Ce=47 μF、C2=10 μF、β=100、vs=10 mV/1 kHz，C3 为 `passive.capacitor_polarized`。
- `tools/CircuitModels.BjtAmp/Definitions.cs` 仅 B07 定义改为该变体语义（本地 `PolarizedCapacitor` helper 生成 `+/–` 引脚电解电容），**未运行写盘模式**（保留用户手工布局）；`--verify` 只读校验通过 10/10。
- D06 的 `ProjectName` 恢复为"双向限幅电路（双管双偏置）"后，`CircuitModels.Diode --verify` 恢复 7/7（修复前曾因工程名被改名而 6/7）。

### 2.7 M2 测试维护（Suite 7 + 保存回归）

- `tests/M2SolverChallenger` Suite 7 原反射调用 `MathFormulaView.FormatLatexToUnicode`（第七轮已删除该 API）；现改为反射验证 `CircuitAssistant.MathRendering.MathLatexParser.Parse`、`MathLayoutEngine.Layout`、`EstimateMathTextMeasurer.Instance`，并对 `\omega_0`（真角标 ω+下标 0）、r_be 分式（上下叠放）、bmatrix（定界符 + 4 元素）做真实布局断言（无原始 LaTeX 泄漏）；断言 7.1.1–7.1.5。
- 新增 10.8b 保存保留工程名断言；M2 总数 155→**156**（`Passed Tests: 156, Failed Tests: 0, VERDICT: APPROVE`）。

### 2.8 实时指标 AC 卡 Rs/Aus 两行布局修复（窗口验收发现）

- 症状：真实窗口首次打开 B07 变体并求解时，实时指标 AC 卡新增的"信号源内阻 Rs""源电压增益 Aus"两行出现**叠字**（截图 `13_b07_ac_metrics_region.png` 为 bug 现场证据）。
- 原因：AC 指标卡 Grid 只定义了 **7 行** `RowDefinition`（Row 0–5 对应六项原指标 Av/Ri/Ro/fL/fH/BW，Row 6 为最后一行）；新增的 Rs/Aus 两行使用 `Grid.Row=6/7`，Row=7 没有对应行定义，被归入最后一行，导致 Aus 文本覆盖在 Row=6 的 Rs 行上。
- 修复：`Views/AnalysisStudioPage.xaml` 的 AC 指标卡补足 **8 行** `RowDefinition`（Row 0–5 原指标、Row 6 "信号源内阻 Rs:"、Row 7 "源电压增益 Aus:"）；两行仍由 `AcParameters.HasSourceResistance` 控制 `Visibility`，无 Rs 时不占显示空间。**该修复是 XAML 布局改动，不改变引擎/结果契约。**
- 验证：用 2026-10-18 18:30:46 重新构建的 `bin\_verify\CircuitAssistant.exe` 重拍窗口截图 `11b_b07_metrics_fixed.png`、`12b_b07_qpoint_fixed.png`、`13b_b07_ac_metrics_fixed.png`（B07 变体两行正常）与 `14b_normal_metrics_fixed.png`（普通共射模板不显示两行）——均正常，无叠字。

## 3. 测试与验证

> 以下为 2026-10-18 本工作区最终回归实测（`.tmp_final8b`，Lead 最终全量复核；数字以最终回归为准）。`dotnet run` 控制台检查工程本身不是真实 WinUI 窗口端到端验收；窗口验收见 3.4 的静态截图证据。

### 3.1 构建与测试数字

| 检查 | 命令 | 结果 |
|---|---|---|
| 主项目构建 | `dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify` | **成功，0 警告/0 错误**（最终窗口验收所用 `bin\_verify\CircuitAssistant.exe` 构建于 2026-10-18 18:30:46） |
| 非线性引擎回归（含新增 Rs/电解电容检查） | `dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj` | **Total=822 Passed=822 Failed=0**（+71 条） |
| M2 回归（Suite 7 + 保存工程名） | `dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj` | **Total Tests=156, Passed=156, Failed=0, VERDICT: APPROVE** |
| 29 文件校验 | `dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj` | **Total=259 Passed=259 Failed=0** |
| 公式排版回归 | `dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj` | **Total=411 Passed=411 Failed=0** |
| 显示两位小数回归 | `dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj` | **Total=55 Passed=55 Failed=0** |
| 30 题本地基准 | `dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj` | **Total=30 Passed=30 Failed=0 Accuracy=100.00%** |
| 二极管生成器 | `dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify` | **7/7**（D06 工程名恢复后从 6/7 回到 7/7） |
| BJT 偏置生成器 | `dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify` | **6/6** |
| BJT 放大生成器 | `dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify` | **10/10**（B07 新变体定义只读校验） |
| MOS 生成器 | `dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify` | **6/6** |

### 3.2 既有控制台回归

| 检查 | 结果 |
|---|---|
| ChallengerRunner | **131/131** |
| M3AdversarialStress | **32/32** |
| M3_4EmpiricalStress | **9/9** |
| EngineV2Checks | **306/306** |
| ReportExportChecks | **110/110**（未新增 Rs/Aus 报告行断言；报告行由代码路径与人工验收覆盖） |
| WebImportChecks | **172/172** |
| EngineAuditChecks（独立数值审计，未改） | **1359/1359** |
| SymbolChecks（未改） | **230/230** |

### 3.3 B07 变体关键数值核对

教材估算法两位小数显示链（`BjtAmplifierStrategy`，NPN 硅管 VBE=0.7 V）：

| 量 | 显示值 | 说明 |
|---|---|---|
| U_B | **2.79 V** | `12×10/(33+10)=2.7907 V` 舍入 |
| I_EQ | **2.09 mA** | `(2.79−0.7)/1 kΩ` |
| I_BQ | **20.69 μA** | `2.09 mA/(1+100)` |
| I_CQ | **2.07 mA** | `100×20.69 μA` |
| U_CEQ | **7.84 V** | `12−2.07 mA×1 kΩ−2.09 mA×1 kΩ` |
| rbe | **1.46 kΩ** | `200+101×26 mV/2.09 mA` |
| Av | **−34.25** | `−β(Rc∥RL)/rbe`（Rc=RL=1 kΩ） |
| Ri | **1.23 kΩ** | `Rb1∥Rb2∥rbe` |
| Ro | **1 kΩ** | `≈Rc` |
| Rs | **50 Ω** | 信号源内阻 R6 |
| Aus | **−32.91** | `Av·Ri/(Rs+Ri)`（显示值回代） |
| fL | **41.7 Hz** | `√(2.65²+7.96²+40.85²)`，含 `τ1=(50+1.23k)·47 μF` |
| fH | **≈1.76 MHz** | Cπ/Cμ 模型估算（非扫描结果） |

- 步骤：**6 步 = 5 教学步 + 1 个 C3 电解极性核验步**；DC 2 步（Q 点 + 极性核验）+ AC 4 步，`StepIndex` 连续、`StepGroups` 两组；C3 极性正常（正极在基极侧 2.64 V）。
- 后端戴维南精确值仍保留不显示：`IBQ≈19.24 μA`、`ICQ≈1.92 mA`、`U_CE≈8.13 V`（`QPointInfo.Exact*`）。
- 无 Rs 对照：`HasSourceResistance=false`、无 Aus 文案与 `Rs_ohm`/`Aus` 键，Av/Ri 与 B07 变体相同，`fL=41.71 Hz`（`τ1=Ri·C1`），步骤 5 步（DC 1）。

### 3.4 真实窗口截图验收（已完成，静态截图级）

> 工具/环境：Win32 `PrintWindow`（`flags=2`）截图、`SetWindowPos` 固定 1920×1118 物理像素 @150% DPI、`SendInput` 点击/滚轮、UIA 辅助定位；使用最终构建 `apps\desktop-winui\bin\_verify\CircuitAssistant.exe`（**2026-10-18 18:30:46**，0 警告/0 错误）；打开文件 `C:\dsh_b07\b07.circuit`（= `examples/circuit-models/B07-common-emitter-bypassed.circuit` 最新版）；证据目录 `tools/ui-verify/evidence/round8/`。

逐项结论：

- **求解成功**：状态栏显示"求解完成 · 晶体管共射极放大电路 · 6 个步骤"，不再出现"存在教学模型未覆盖的电阻支路"（证据 `01_b07_derivation_top_full.png`、`02_b07_derivation_top_panel.png`）。
- **推导页 DC/AC 分组**：DC 2 步（第 1 步 Q 点；第 2 步"电解电容 C3 直流偏置与功能特性核验"，显示 `U_C3 = 2.64 V − 0 V = 2.64 V ≥ 0 V`，极性正常；证据 `03_b07_step2_electrolytic_full.png`、`04_b07_step2_electrolytic_region.png`）+ AC 4 步（证据 `05_b07_ac_group_full.png`）。
- **Av/Aus 公式与内容**：第 4 步公式含 `A_v=-β(R_C∥R_L)/r_be，A_us=A_v·R_i/(R_s+R_i)`，内容写明"信号源内阻 R_s=50 Ω 与放大器输入电阻 Ri 构成分压"（证据 `07_b07_av_step_full.png`、`08_b07_av_formula_region.png`、`09_b07_av_result_full.png`）。
- **实时指标 Q 点**：IBQ=20.69 μA、ICQ=2.07 mA、VCEQ=7.84 V（证据 `12b_b07_qpoint_fixed.png`）。
- **实时指标 AC 卡**：Av=−34.25、Ri=1.23 kΩ、Ro=1 kΩ、fL=41.7 Hz、fH=1.76 MHz（模型估算）、BW=1.76 MHz，并显示新增"信号源内阻 Rs: 50 Ω""源电压增益 Aus: -32.91"两行（证据 `11b_b07_metrics_fixed.png`、`13b_b07_ac_metrics_fixed.png`）。
- **无 Rs 对照**：普通共射模板的实时指标卡**不显示** Rs/Aus 两行（证据 `14b_normal_metrics_fixed.png`）。
- **UI 缺陷修复验证**：首拍 `13_b07_ac_metrics_region.png` 记录 RowDefinition 缺失导致的两行叠字；补足 AC 卡 8 行后重拍 `11b/12b/13b/14b` 正常（详见 2.8）。
- 流程/几何/时间戳：`round8-summary.json`、`round8b-summary.json`、`flow-log.jsonl`（另含 `sweep_*`/`step4probe_*`/`repeat_*` 辅助截图）。

**未覆盖**：逐像素自动比对、深浅色主题切换、150% 以外 DPI 与其它窗口尺寸、自动化交互断言；`PrintWindow` 是静态截图，不等同 WinUI UI 测试。

### 3.5 测试口径说明

- BJT 数值断言对教材手算公式回代链（容差 ≤1%）；不代表外部 SPICE 数值一致性。
- `fH`/`fL` 仍为教材近似/模型估算，不是真实频率扫描。
- 窗口验收为 3.4 的静态截图级（B07 变体 + 普通模板对照），未做逐像素自动比对/主题/其它 DPI/自动化交互；报告 DOCX/TeX 实机产物仍未覆盖；`ReportExportChecks` 未新增 Rs/Aus 行断言。

## 4. 文档更新

本轮同步更新（保留第七轮及更早修订记录原文，不改写历史轮次）：

- 新增本文件：第八轮最终记录（用户反馈与原因诊断、关键实现、B07 数值链、测试与窗口截图验收、文档更新、边界、复现命令）。
- `README.md`：能力摘要补"BJT 信号源内阻 Rs + 电解输入耦合电容 + Aus、B07 教学变体、保存保留工程名"；测试数字改为 822/156 等；边界（MOS 放大器暂不支持 Rs；Rs 只识别"源与输入耦合电容之间恰一只电阻"）；补窗口截图验收结论与 UI 两行叠字修复。
- `PROJECT.md`：引擎/UI 组件职责、`AcParametersInfo` 新字段与显示行、BJT 支持矩阵补 Rs/电解电容/Aus、已知边界、保存 ProjectName 保留语义。
- `CLAUDE.md`：当前架构段落补 Rs/Aus/电解电容识别与保存保留工程名；测试清单补数字（NonlinearCircuitChecks 822、M2 156）。
- `HANDOVER.md`：维护入口、求解限制（Rs 识别条件、Aus、τ1/fL、电解电容、诊断文案、保存命名）、验证数字与窗口证据位置更新。
- `projects/analysis-engine-technical-report.md`：6.1 BJT 补信号源内阻 Rs/Aus/电解电容/τ1/诊断文案；6.2 共集与共基同样支持；6.4 MOSFET 明确暂不支持源内阻；第 10 节数字（822/156 等）。
- `projects/presentation-layer-technical-report.md`：实时指标卡新增 Rs/Aus 两行（仅识别到 Rs 时）、AC 卡 8 行 Grid 与叠字 bug 修复、报告导出同步两行、B07 变体分组；窗口截图验收与证据。
- `projects/data-layer-technical-report.md`：`AcParametersInfo.HasSourceResistance/SourceResistance/SourceVoltageGain`（显示/报告用、不序列化）；保存 `ProjectName` 保留语义（修复）；工程格式仍 1.2.0。
- `projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md`：第八轮命令/结果表/能力状态（Rs 支持、B07 变体、保存命名修复、窗口截图验收与未覆盖项）。
- `专业名词解释.md`：新增/修订"信号源内阻 Rs""源电压放大倍数 Aus""电解电容极性核验""未覆盖支路诊断"，更新电压增益、电容与工程保存相关条目。
- `projects/README.md`：文档索引新增第八轮记录（摘要含窗口截图验收）。
- `examples/circuit-models/README.md`：B07 变体说明（Rs=50 Ω、电解 C3、Aus、fL；手工布局保留、生成器定义已同步；保存改名修复后工程名不再被改写；D06 工程名恢复）。
- `projects/spec-first-stage-local-circuit-analysis-20260601.md`：frontmatter/范围/I 项/M 项/任务勾选/验收数字补第八轮，含窗口截图验收与 UI 修复。

> 范围说明：`projects/wzh-no-git-worktree-eng-review-20260529-021023.md`、`projects/technical-document-review-*` 与 round1–7 修订记录保持历史原样（其中"未覆盖电阻"旧口径与 751/155 等旧数字属历史）；现行口径以本文及上述现行文档为准。

## 5. 已知边界与后续规划

1. **Rs 识别条件**：只识别"交流源正端—输入耦合电容—基极/发射极"之间**恰一只电阻**的教学拓扑；串联多只电阻或更复杂源网络仍明确失败（给出诊断文案），不做任意网络等效。
2. **电解电容**：按 `+/-` 引脚取网；反接仍可求解但给出 Warning 极性步骤与"极性反接"摘要告警；极性判断基于直流偏置方向（正极电位不得低于负极）。
3. **Aus 口径**：`Aus=Av·Ri/(Rs+Ri)`（共基 `Au·ri'/(Rs+ri')`）使用显示舍入后的 Av/Ri 计算，属教材手算约定；不显示后端未舍入的 Aus。
4. **fL/fH**：`τ1=(Rs+Ri)C1` 与 fL 合成、fH 仍为 Cπ/Cμ 模型估算，不是真实频率扫描或器件寄生参数拟合。
5. **MOSFET 放大器暂不支持源内阻 Rs**（本轮范围仅 BJT；MOS 的 Rs/Aus 列入后续）。
6. **多级放大未实现**：≥2 晶体管仍分类 `MultiStageAmplifier/Cascade` 并明确拒绝，只预留 `IMultiStageAnalysis`。
7. **MOS K/λ 仍无检查器编辑框**；JFET/耗尽型不承诺求解。
8. **公式框限制不变**：array 列格式竖线不画、超宽原子等比缩小、Canvas 文本不可选择、`\overline/\vec/\tilde` 不画装饰。
9. **显示两位小数是显示约定**，不改变后端计算精度；后端 `Exact*` 仍保留不显示。
10. **保存命名**：仅"未命名电路工程"回退文件名；打开旧工程/教材示例保留其 `ProjectName`；文件名 (`ProjectFileName`) 仍随保存位置更新。
11. **报告导出 Rs/Aus 行**：代码已实现，但 `ReportExportChecks` 未新增针对性断言，属人工验收项。
12. **窗口验收为静态截图级**：已用最终构建在真实窗口（Win32 `PrintWindow`，150% DPI）对 B07 变体与普通模板做静态截图验收（3.4，证据 `tools/ui-verify/evidence/round8/`）；**未做**逐像素自动比对、深浅色主题切换、150% 以外 DPI/其它窗口尺寸、自动化交互断言；`PrintWindow` 是静态截图，不等同 WinUI UI 测试。
13. **UI 修复范围**：AC 指标卡 Rs/Aus 两行布局修复只改 `AnalysisStudioPage.xaml` 行定义（7→8 行），不改变引擎/结果契约；两行仅在 `HasSourceResistance=true` 时可见。
14. **本轮未复跑** `npm run check`（独立 Web/Python 原型）与 `NativeBenchmark30 -- --roundtrip`（历史 31/31）。
15. **无 SPICE 对拍**：数值断言对教材手算公式，不是与 ngspice 等外部仿真器的一致性验证。
16. **后续规划**：MOSFET 源内阻/Aus → 多级级联求解 → 混合 BJT/MOS 非线性网络 → JFET/耗尽型求解 → 大信号/失真分析 → 公式框扩展 → 完整 ERC 与外部 SPICE 数值对照；视觉识别仍在软件本体完善之后。

## 6. 附录：复现命令

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "examples/circuit-models/B07-common-emitter-bypassed.circuit"
dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj
dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj
dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
dotnet run --project tests/WebImportChecks/WebImportChecks.csproj
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
dotnet run --project tests/SymbolChecks/SymbolChecks.csproj
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify
```
