# 当前测试计划与验证记录

> 原计划日期：2026-05-29；**发布前复验：2026-10-19（0.2.0-preview.1：全量 20 组命令 / 4905 项断言全部通过，Release x64 自包含 528 个运行文件、构建 0 警告/0 错误、发布目录启动冒烟通过；未做新的 ui-capture 窗口验收）**；第九轮复验：2026-10-18（BJT 小信号参数 r_bb'/r_be(h_ie) 可调：数据层/引擎/UI/测试；本轮按用户要求不做 ui-capture 窗口验收）；第八轮复验：2026-10-18（BJT 信号源内阻 Rs/电解耦合电容/Aus、B07 教学变体、保存保留工程名、M2 Suite 7 维护）；第七轮复验：2026-10-18（教材估算法显示/全部题型两位小数/DC-AC 分组/Win2D 公式排版）；第六轮复验：2026-10-18（二极管/MOSFET/共集/多级预留接口与 29 个教学电路）。主对象是 WinUI 3。用户随后授权程序与测试；[旧计划](archive/2026-10-17-before-winui-doc-sync/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md)保留历史目标。

## 1. 测试对象与证据边界

- 原生入口：App → MainWindow → AnalysisStudioPage。
- 原生状态、文件与控件：AnalysisStudioViewModel、工程 DTO/文件服务、模板、CircuitCanvas、MathFormulaView（Win2D 自绘）、报告导出命令、Web 导入入口、检查器参数标签/材料选择。
- 原生引擎：提取、校验、分类、教学策略（RC/RL、RLC、单只 NPN/PNP BJT 共射/共基/共集、二极管非线性 9 类子类型、增强/耗尽型 MOSFET 与 N/P 沟道 JFET 共源/共源+Rs/共源旁路/共漏/共栅）、教材估算法显示与 DC/AC 相位分组、工作区物理核验（BE/BC 判据、MOS/JFET 分段负载线、FET Invalid）、多级 `MultiStageAmplifier` 明确拒绝与 `IMultiStageAnalysis` 预留接口、通用线性网络 MNA 2.0（受控源/动态/戴维南/相量/扫频）、不支持策略、专用策略→MNA 回退。
- 原生模型：`Engine/Nonlinear/DiodeModel.cs`（恒压降 + Shockley/Newton）、`Engine/Nonlinear/MosfetModel.cs`（Level-1 平方律 + JFET Shockley 方程，Vth/VGS(off)/K/λ/IDSS）、`Engine/MultiStage/MultiStageContracts.cs`、`Engine/PedagogicalSolution.cs`（`SolutionPhase`/`Step.Phase`）、`Models/AnalysisResult.cs`（`QPointInfo.Exact*`、`AnalysisStepGroup`/`StepGroups`、`AcParametersInfo.HasSourceResistance/SourceResistance/SourceVoltageGain`）、`Services/TeachingDisplay.cs`（教材单位/两位小数/回代值）；MOS 的 K/λ 与 JFET 的 IDSS 可在检查器编辑并随工程持久化。
- 原生输入拓扑（第八轮）：BJT 策略识别信号源内阻 Rs（交流源与输入耦合电容之间恰一只电阻）、电解输入耦合电容（`+/-` 引脚）与源电压增益 Aus；未覆盖电阻诊断文案；`ViewModels/AnalysisStudioViewModel.cs` 保存保留工程名（仅未命名工程回退文件名）。
- 原生 BJT 小信号参数（第九轮）：`Models/CircuitElement.cs` 的 `BjtRbb`（Ω，0=自动）/`BjtRbe`（Ω，0=按 Q 点计算）与 `SupportsBjtSmallSignalParameters`；`Models/CircuitProjectData.cs` 的 DTO 持久化/校验；`BjtAmplifierStrategy` 全部子类型读取实际参数（`rbb_ohm`/`rbe_ohm`/`rbe_source`）；ViewModel `RbbNumber`/`RbeNumber` 宏同步与两处编辑框。**本轮无窗口验收。**
- 原生符号与参数：`Models/SymbolGeometry.cs` 标准矢量符号（画布与卡片共用）、元件参数语义（β/Vth/VGS(off)/VF/K/λ/IDSS）与材料持久化；MOS K/λ 与 JFET IDSS 检查器编辑框（2026-10-19）。
- 原生公式排版：`Math/*`（纯 C# 解析/布局/测量抽象）+ `Controls/MathFormulaView`（Win2D `CanvasControl`，真角标/分式/根号/矩阵/自动换行、基础字号 16、复制 LaTeX）。
- 原生报告与导入：`ReportExportService` 四格式渲染（含 DOCX OOXML 结构）、`WebCircuitGraphImporter` 与 3 个原型 benchmark 样例。
- 原生教学电路与诊断：`examples/circuit-models/` 的 29 个 `.circuit` 文件、4 个 `tools/CircuitModels.*` 生成器、`tests/CircuitModelChecks -- --file <path>` 单文件诊断。
- 独立原型：Web 编辑器、Zod/元件定义、Python/SymPy、FastAPI/Worker。

主软件没有 Web 页面路由，不依赖 FastAPI 完成求解。视觉在本体完善后开发，不纳入本阶段实现和精度验收。

## 2. 原生构建与自动回归（第九轮 + 2026-10-19 发布前复验）

根目录命令：

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=bin\_verify
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
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "document/3-6题.circuit"
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "examples/circuit-models/B07-common-emitter-bypassed.circuit"
dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj
dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify
```

| 检查 | 最新结果（2026-10-19 发布前实测；括号注明第九轮当轮） | 覆盖 |
|---|---|---|
| WinUI build | **0 警告、0 错误**（含默认 Debug 重新构建；Release x64 自包含发布 528 个运行文件、发布目录启动冒烟通过） | 完整主项目编译（默认 Debug + 隔离 `_verify` OutDir + Release x64 自包含 publish；新编辑框需重启客户端可见） |
| M2SolverChallenger | **166/166**【第九轮当轮 160/160】 | 原有求解/格式/文件/ViewModel 回归；第九轮新增 4 项：10.7b 默认自动、10.7c 宏参数同步到 BJT 元件、10.7d 修改使旧结果失效、10.8c rbb/rbe 随工程保存并读回（另保留第八轮 Suite 7 反射 `MathRendering` 与 10.8b 保存保留工程名）；2026-10-19 发布前复验含后续新增回归 |
| ChallengerRunner | 131/131 | 模板、网表、分类、WireTap、求解 |
| M3AdversarialStress | 32/32 | 位移/母线/父子线不变量及 80px 场景 |
| M3_4EmpiricalStress | 9/9 | 母线/80px 及 132 个位移组合 |
| EngineV2Checks | 306/306 | MNA 2.0 全部 + PNP/硅锗段断言为教材估算法数值（项数不变） |
| ReportExportChecks | 110/110 | Markdown/LaTeX/DOCX/HTML 内容、转义、OOXML 结构、失败/空数据降级（未新增 Rs/Aus 行断言） |
| WebImportChecks | 172/172 | 3 个 benchmark 导入、拓扑还原、单位/端口映射、BJT 可解变体、diode/MOS 教学策略断言 |
| SymbolChecks | 230/230 | 符号几何、引脚/引线、NPN/PNP、JFET/MOS、VCC/GND、二极管、镜像/节点跟随 |
| NonlinearCircuitChecks | **1606/1606**【第九轮当轮 913/913】 | 二极管 9 类、MOSFET M01–M06、BJT 共集/共基、多级预留、教材估算法显示链与 DC/AC 相位、软分压器饱和拒绝、两位小数、后端 Exact*；**历史分解：第八轮 71 项 `BjtSourceResistanceChecks`（822）+ 第九轮 91 项 `BjtSmallSignalParameterChecks`（913）**；**2026-10-19 新增耗尽型 MOS 与 N/P JFET 五类拓扑（Shockley/平方律）、IDSS/K/λ 参数、栅结正偏拒绝与工作区负载线核验，扩至 1606/1606**：默认自动/自定义 rbb=300/100/400 Ω、题目给定 rbe=1500/1200/1500 Ω，CE/CB/CC、NPN/PNP、`rbb_ohm`/`rbe_ohm`/`rbe_source`、步骤文案/公式/代入无硬编码 200/50、非法值失败与显示口径 |
| CircuitModelChecks | 259/259 | 29 个教学电路文件：反序列化/Validate/分类/子类型/求解/步骤≥4（默认 BjtRbb/BjtRbe=0，数值不回归）；`--file` 单文件诊断在 `document/3-6题.circuit` 与 B07 变体上通过 |
| MathFormulaChecks | **411/411** | LaTeX 解析、真角标（0.72×）、叠放分式、根号、matrix/cases/array、自动换行不裁切、超宽原子等比缩小、12/14/16/18px 排版 |
| DisplayRoundingChecks | **55/55** | RC/RL/RLC/MNA（含受控源/戴维南/频响）/二极管/BJT/MOS 的 Q 点、AC 参数、摘要、步骤文本全部 ≤2 位小数（含题目给定 r_be 显示） |
| 生成器 `--verify` | 7/7、6/6、10/10、6/6 | Diode/BjtBias/BjtAmp/Mos 声明式生成 + 分类/求解预期；B07 定义同步为 Rs/电解 C3 变体且默认 rbb=0（只读校验）；D06 工程名保持"双向限幅电路（双管双偏置）" |
| NativeBenchmark30 | **30/30（Accuracy=100.00%）** | 一阶/二阶/BJT/通用线性 MNA 各题手算期望；B08 共基极 `Re` 为 2 kΩ（第七轮调整，原因见第七轮记录）；第九轮未复跑 `--roundtrip`（历史 31/31 网表等价） |
| EngineAuditChecks | 1359/1359 | 独立参照求解器随机网络交叉验证、KCL 残差、戴维南/相量/扫频/边界拒绝/结构化一致性/取消 |

C# 常规回归合计 **1156 项自动化断言**（166+131+32+9+306+110+172+230；第九轮 M2 156→160，发布前 160→166），另有非线性引擎 **1606** 项、29 个教学电路文件校验 **259** 项、公式排版 **411** 项、显示两位小数 **55** 项、生成器 `--verify` 合计 **29/29**、独立审计 1359 项、30 题基准 30/30；全量矩阵 **20 组命令 / 4905 项断言全部通过**，Release x64 自包含 **528 个运行文件**、构建 0 警告/0 错误、启动冒烟通过、`npm test` 16/16。BJT 小信号参数 r_bb'/r_be(h_ie) 可调（默认/自定义/给定、非法值）、信号源内阻 Rs/电解电容/Aus、B07 教学变体、保存保留工程名、教材估算法显示、DC/AC 分组、公式排版、耗尽型 MOS/JFET 五类拓扑与 29 个文件均有针对性回归；真实窗口已做静态截图级视觉验收（第七轮记录 3.5：共射模板、题3-12 PNP、MOS 共源模板；**第八轮记录 3.4：B07 变体 + 普通模板对照，含 Rs/Aus 两行与 AC 卡叠字 bug 修复**，证据 `tools/ui-verify/evidence/round8/`）；**第九轮与 2026-10-19 发布验证均未做新的 ui-capture 窗口验收**，rbb/rbe、MOS K/λ 与 JFET IDSS 编辑框窗口外观/交互未验收；自动化交互、像素级比对、深浅主题、其它 DPI、符号像素与 Word/TeX 产物仍未验收。

测试目录仍有 CS8602/CS1998 警告（如存在）。它们是控制台项目，必须使用 `dotnet run`；`dotnet test` 空运行不能视为通过。`MathFormulaChecks` 用确定性估算测量器验证解析/布局几何，不验证真实 DirectWrite 字形与窗口换行（第七轮已做静态截图抽查，见第七轮记录 3.5）；`DisplayRoundingChecks` 只扫描文本，不检查画布像素；`BjtSourceResistanceChecks`/`BjtSmallSignalParameterChecks` 用测试网表构造 Rs/电解 C3 与 rbb/rbe 参数，不覆盖真实文件选择器/窗口编辑路径；模型位移不等于真实指针拖拽；报告 DOCX 未在 Word 实机打开，`tex` 未真实编译。

## 3. 独立原型回归

```powershell
npm run check
python services/api/tests/test_api.py
```

第一轮曾通过：16 个 TS 测试、17 个 Python 引擎测试、16 个 API/Worker 测试、Web 生产构建、3 个 Benchmark 校验、3 份 JSON Schema 导出；**第二轮未重新运行 `npm run check`**，因此不能引用为第二轮通过证据。第二轮新增的是原生对 3 个原型 benchmark 样例的导入回归（WebImportChecks），不是浏览器/API E2E。

## 4. 实际窗口检查

第一轮曾通过 computer-use 启动窗口检查空白状态、共射模板求解、关闭工程未保存对话框和保存选择器弹出；未完成路径输入与保存。第二轮**未重新操作真实窗口**（用户程序仍在运行，该轮只做隔离构建与自动化回归），因此以下仍是未验收项。第七轮已用 Win32 `PrintWindow` + SendInput 对部分项做了**静态截图级**验收（见[第七轮修订记录](technical-code-fixes-20261018-round7.md) 3.5）：共射模板推导页/公式框/DC-AC 分组/Q 点、题3-12 PNP 打开并求解、MOS 共源模板。**第八轮已完成 B07 教学变体（Rs=50 Ω + 电解 C3）的静态截图级窗口验收**（见[第八轮修订记录](technical-code-fixes-20261018-round8.md) 3.4）：用 2026-10-18 18:30:46 最终构建（`bin\_verify\CircuitAssistant.exe`，0 警告/0 错误）在 150% DPI/1920×1118 下打开 B07 变体并求解——无"未覆盖电阻"错误、状态栏"6 个步骤"、DC 2 步（Q 点 + C3 极性核验 `U_C3=2.64 V`）+ AC 4 步、第 4 步含 `A_us=A_v·R_i/(R_s+R_i)`、实时指标 Q 点 20.69 μA/2.07 mA/7.84 V 与 Av/Ri/Ro/fL/fH/BW、Rs/Aus 两行正常；普通模板不显示该两行。**该验收首拍发现 AC 指标卡 RowDefinition 少一行导致 Rs/Aus 两行叠字，修复为 8 行后重拍 `11b/12b/13b/14b` 正常**；证据目录 `tools/ui-verify/evidence/round8/`（bug 现场 `13_b07_ac_metrics_region.png`）。**第九轮按用户明确要求不做 ui-capture 窗口测试**：只完成控制台回归 + 构建（含默认 Debug 重新构建，0 警告/0 错误）；r_bb'/r_be 两处编辑框的真实窗口外观/焦点/键鼠交互未验收，第七/八轮截图不覆盖本轮新增编辑器。**2026-10-19 发布验证同样为控制台回归 + 构建 + 启动冒烟，未做新的 ui-capture；MOS K/λ 与 JFET IDSS 编辑框的窗口外观/交互也未验收。** 下列其余项与自动化交互仍未验收：

- 报告菜单四种格式的实际选择器与写盘；
- “打开工程 / 导入 Web CircuitGraph”在窗口中的提示与错误对话框；
- 打开 `examples/circuit-models/` 的 29 个教学电路并逐个点击求解（尤其 B10 共集、D 组整流/限幅/钳位、M01–M06 MOS、B12–B14 多级明确拒绝；B07 变体已单独截图验收）；
- 打开 `document/3-6题.circuit` 检查 BJT 分压偏置 Q 点是否显示教材估算链 `0.53 μA / 53 μA / 11.82 V`（不再有"戴维南精确/教材估算"双口径文案）、第 1 步是否分入"直流等效电路（DC 分析）"组、摘要是否含 `U_B=11.63 V → I_EQ=53.85 μA → I_BQ=0.53 μA → I_CQ=53 μA → U_ECQ=11.82 V`；
- B07 教学变体窗口项（已静态截图验收，见上）；报告导出含 Rs/Aus 两行尚未窗口验收；
- **BJT 小信号参数编辑（第九轮）**：选中 BJT 后"元件参数检查器"的 rbb′/给定 rbe 两个编辑框、参数设置 Group 3 的宏参数编辑框在真实窗口中的显示、焦点、非法输入归零与求解后数值变化；本轮未做任何窗口操作；
- 保存教材示例后确认 `ProjectName` 未被改写为文件名（DTO 中标题保持"分压式偏置共射放大电路（Ce 旁路）"/"双向限幅电路（双管双偏置）"；控制台 M2 10.8b 已断言，窗口未操作）；
- 公式框在真实窗口中的排版：真上下角标/分式/根号、按宽度换行且无横向裁切、内容高度自适应、复制 LaTeX 按钮、深浅主题与高 DPI；
- 解题推导页的 DC/AC 分组卡片与两行式步骤卡片头（步骤号+标题一行、方法名小字第二行）在窄栏宽度下的显示；
- 新增指标卡在真实窗口中的滚动/溢出/主题样式；
- 取消求解按钮对长 MNA 扫描的实际响应。

## 5. 后续窗口与数据验收

| 场景 | 操作与通过条件 |
|---|---|
| 文件与路径 | 保存/重开、另存为；打开 A 后新建 B，再保存不能覆盖 A；取消选择器不改路径；Web 导入后路径为空且置脏；**保存教材示例后 ProjectName 不被改写为文件名**（仅未命名工程回退文件名；已由 M2 10.8b 控制台断言覆盖） |
| 切换与关闭 | 打开/新建/关闭/窗口关闭分别检查保存、不保存、取消；保存失败应保留工程 |
| 保存期间编辑 | 保存快照期间继续编辑仍保持修改标志；切换期间不得丢失新编辑 |
| 格式与字段 | 宏非默认单位、创建时间、元件字段、旧 1.0.0、WireTap/路径、失败写入和被占用文件 |
| 报告导出 | 4 种格式选择器、中文路径/文件名、打开产物检查（Markdown/HTML/Word；LaTeX 需外部 xelatex） |
| 符号与参数 | 画布/卡片中 NPN/PNP、JFET、四类 MOS、VCC、GND 等符号显示；检查器参数标签与说明；二极管/BJT 材料下拉框切换后数值/标签/保存加载正确；阈值电压/夹断电压不被误读为电源电压；**BJT 小信号参数 rbb′/给定 rbe 在检查器与参数设置两处编辑后保存重开仍一致、非法输入归零**（第九轮；窗口未验收）；**MOS K/λ 与 JFET IDSS 编辑后保存重开一致、JFET 缺 IDSS 求解明确失败**（2026-10-19；窗口未验收） |
| Web 导入 | 3 个 benchmark 样例导入后的画布布局、求解提示、另存为原生工程；非 CircuitGraph JSON 仍按原生工程/错误处理 |
| 放置/检索 | 25 条符号、二极管类别、显示/引脚一致 |
| 选择与旋转 | 单选、框选、Ctrl/Shift、多选移动；Space/R 不重复，输入框不误触画布快捷键 |
| 布线与拖动 | 引脚间/线到线、嵌套分支、线段调整、重复移出移回；交叉不误接 |
| 撤销 | 每种编辑、50 次历史边界、切换工程历史隔离；宏同步不丢单位 |
| 显示 | 40%/100%/300%、不同 DPI、深浅主题、公式排版（角标/分式/换行/高度）与复制分别检查；Q 点/AC 指标两位小数显示；识别到 Rs 时实时指标卡与报告出现"信号源内阻 Rs/源电压增益 Aus"两行（无 Rs 时不显示）；BJT rbb′/rbe 编辑框显示"0=自动"提示 |
| SPICE | 逐器件节点/方向/单位；补模型卡和真实仿真器对照后才可宣称可直接仿真 |

## 6. 后续求解与范围验收

| 场景 | 当前状态与后续目标 |
|---|---|
| 开路、短路、缺地 | Validate 已接入；补全 ERC；自动参考地给出警告，分类器仍无独立开路类型 |
| 奇异/矛盾源 | MNA 2.0 返回明确失败；继续扩展病态矩阵与数值尺度范围与独立审计 |
| 受控源与动态元件 | DC/AC 相量已支持四种受控源、C/L；非线性/互感/运放仍拒绝 |
| RC/RL | 标准串联回路与高通已修正；复杂网络可回退 MNA 稳态/相量，但不再是瞬态答案 |
| RLC | 标准串/并联；复杂二阶网络回退 MNA，需在界面文字中继续明确"回退"语义 |
| BJT | 单只 NPN 或 PNP 四类偏置、共基极与共集电极已支持（硅/锗 VBE 可选，共集 Av/Ri/Ro 已测）；**显示为教材估算法**（每步两位小数舍入并回代，题3-12 链已回归；后端 `Exact*` 仅保留不显示）；软分压器若被估算进饱和区明确拒绝；步骤 DC/AC 分组（共射/共基 5 步、共集 6 步）；**支持信号源内阻 Rs（源与输入耦合电容之间恰一只电阻）与电解输入耦合电容，识别到 Rs 时给出 `Aus=Av·Ri/(Rs+Ri)`、`τ1=(Rs+Ri)C1` 并显示 Rs/Aus 两行**（B07 变体回归：`U_B=2.79 V → I_EQ=2.09 mA → I_BQ=20.69 μA → I_CQ=2.07 mA → U_CEQ=7.84 V`，`Av=−34.25、Aus=−32.91、fL≈41.7 Hz`）；**r_bb'/r_be(h_ie) 可调（第九轮）**：`BjtRbb`/`BjtRbe` 0=自动，题目给定 r_be 直接采用（不二次舍入），rbe/h_ie 步骤文案/公式用实际 rbb，`Values` 含 `rbb_ohm`/`rbe_ohm`/`rbe_source`（`BjtSmallSignalParameterChecks` 91 项；当轮 913/913，发布前实测 1606/1606）；含二极管等非线性器件的单 BJT 网络明确拒绝 |
| BJT 小信号参数 | `BjtRbb`（Ω，0=自动：共射/共集 200 Ω、共基 50 Ω）与 `BjtRbe`（Ω，0=按 Q 点计算；>0 题目给定）随工程保存/读回（格式仍 1.2.0，旧工程缺省 0）；检查器与参数设置两处编辑框，宏参数双向同步；非法 UI 输入归零，后端非法值明确失败；**第九轮与 2026-10-19 发布验证均无 ui-capture 窗口验收**（控制台回归 + 构建，发布另含启动冒烟） |
| 信号源内阻 Rs/电解电容 | Rs 只识别"交流源与输入耦合电容之间恰一只电阻"的拓扑，串联多只/复杂源网络明确失败并给出元件 Id/网名与支持位置；电解电容按 `+/-` 引脚识别，极性核验归 DC 相，反接仍求解但给 Warning+摘要告警；无 Rs 时输出与第七轮一致；**MOSFET/JFET 放大器暂不支持 Rs/Aus（后续规划）** |
| 教学显示规范 | 全部题型（BJT/MOS/JFET + RC/RL/RLC/MNA/二极管）与报告显示值最多两位小数（`DisplayRoundingChecks` 55/55）；BJT/MOS/JFET 显示为教材手算链；步骤 `SolutionPhase`/`StepGroups` 分 DC/AC 两组，无相位题型平铺；后端精确值 `QPointInfo.Exact*` 不绑定 UI |
| 公式排版 | `Math/*` 解析+布局 + `MathFormulaView` Win2D 自绘：真角标/叠放分式/根号/矩阵/自动换行/自适应高度/基础字号 16/复制 LaTeX（`MathFormulaChecks` 411/411）；限制：array 竖线不画、超宽原子等比缩小、Canvas 文本不可选择、`\overline/\vec/\tilde` 不画装饰；真实窗口已静态抽查（第七轮记录 3.5），像素级比对未验收 |
| 二极管电路 | 半波/桥式整流、电容滤波、串联/双向限幅、钳位、峰值检波、二极管直流偏置已支持（教材解析公式，非 SPICE 瞬态）；直流偏置只支持单一串联回路，并联/分支明确拒绝；显示值 ≤2 位小数 |
| MOSFET/JFET | 单只增强/耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 共源/共源+Rs/共源旁路/共漏/共栅已支持（增强/耗尽 MOS 用 Level-1 平方律、JFET 用 Shockley 方程 ID=IDSS·(1−VGS/VGS(off))²；K/λ/IDSS 检查器可编辑并持久化）；教材估算法 7 步（DC 3 + AC 4）与两位小数显示；JFET 栅结正偏、IDSS 缺失/非正数明确失败；可变电阻（三极管）区只报告分区并停止 AC 推导 |
| 多级放大 | ≥2 晶体管分类为 `MultiStageAmplifier/Cascade`，`MultiStageAmplifierStrategy` 明确拒绝并说明后续规划；`IMultiStageAnalysis` 已预留，级联求解**未实现** |
| 教学电路文件与诊断 | `examples/circuit-models/` 29 个原生 `.circuit` 已由 `CircuitModelChecks` 与生成器 `--verify` 验证（B07 文件为用户手工布局的 Rs/电解 C3 变体，`--file` 可诊断）；`-- --file <path>` 可诊断任意 `.circuit`（实测 `document/3-6题.circuit`、B07 变体）；真实窗口中已抽查打开/求解 `document/3-6题.circuit`、共射模板、MOS 模板（第七轮记录 3.5）与 **B07 变体 + 普通模板对照（第八轮记录 3.4，证据 `round8/`）**，全量逐个仍属人工验收项 |
| 符号标准 | SymbolChecks 230 项几何/镜像/节点跟随检查通过；真实窗口已做静态截图抽查（第七轮记录 3.5），像素级视觉验收仍需人工确认（需重新构建并重启客户端） |
| 等效与频响 | 任意端口 Req/戴维南与 61 点扫频已有结构化结果；输出端口为启发式，真实任意端口选择 UI 未提供 |
| 报告 | 4 格式已生成；PDF 非直接导出，`tex` 未编译，DOCX 未 Word 实机打开 |
| 长计算 | 后台、15 秒上限、协作式取消已接入；强制终止真实长任务仍未实现 |

30 题本地 Benchmark 已达 30/30（1% 容差），但这只证明题目集与手算期望一致；任意电路正确率、完整渲染成功率和真实窗口 E2E 仍没有全量证据。第七轮新增的公式排版（411 项布局几何）、全部题型两位小数（55 项文本扫描）、DC/AC 分组与教材估算法显示链，以及 **第八轮信号源内阻 Rs/电解电容/Aus/B07 变体（当轮非线性引擎 822 项，+71 条）与保存保留工程名（当轮 M2 156 项）**、**第九轮 BJT 小信号参数 r_bb'/r_be(h_ie) 可调（当轮非线性引擎 913 项，+91 条）与宏同步/保存读回（当轮 M2 160 项，+4 条）**、**2026-10-19 耗尽型 MOS/JFET 五类拓扑与工作区校验（发布前非线性引擎 1606 项）** 同样是控制台证据，不能替代窗口验收；第七轮补了 Win32 静态截图抽查（3.5），**第八轮补了 B07 变体 + 普通模板对照的静态截图验收（3.4，含 Rs/Aus 两行与 AC 卡叠字 bug 修复；证据 `tools/ui-verify/evidence/round8/`）**；**第九轮与 2026-10-19 发布验证均未做新的 ui-capture 窗口验收**，rbb/rbe、MOS K/λ 与 JFET IDSS 编辑框窗口外观/交互未覆盖。窗口验收仍为静态截图级，自动化交互、逐像素比对、深浅主题与其它 DPI 仍未覆盖。后续按数值容差、适用条件、预期拒绝与窗口操作分别记录，不能以未崩溃代替正确。
