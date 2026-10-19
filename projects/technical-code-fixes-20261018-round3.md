# 软件本体代码修订记录（第三轮：PNP、半导体材料与标准符号）

> 日期：2026-10-18（Asia/Shanghai）  
> 性质：按用户第三轮要求（PNP 三极管分析、硅/锗管压降参数、多级/混合非线性列入后续规划、按国际标准修改元件符号、明确 MOS 等参数含义）修改 WinUI 主软件。  
> 范围：BJT 教学策略/分类器、元件模型与工程 DTO、检查器与画布/符号卡片、测试工程、现行开发文档；未修改正式申报材料，未提交/推送 Git。  
> 结论：主项目构建 0 警告/0 错误（隔离 OutDir，未覆盖用户正在运行的旧构建）；PNP 四类偏置与硅/锗压降回归通过；多级/混合非线性按要求列入后续规划；符号与参数语义同步更新。  
> 验证结果：常规 C# 回归合计 1077/1077（M2 155、ChallengerRunner 131、M3 32、M3_4 9、EngineV2 303、报告 110、导入 168、符号 169）；独立审计 1359/1359；30 题基准 30/30 与 round-trip 31/31 网表等价。详见第 7 节。

## 1. 用户第三轮要求与处理

| 要求 | 处理 |
|---|---|
| 当前软件不支持 PNP 三极管分析，添加该功能 | `BjtAmplifierStrategy` 支持单只 NPN 或单只 PNP 的四类教学偏置（固定、分压、集电极反馈、共基极），PNP 采用镜像拓扑与 VEB/VEC 符号约定；分类器描述区分 PNP |
| 二极管、三极管参数要考虑硅管/锗管不同压降 | 元件新增 `SemiconductorMaterial`（硅/锗）与 `ForwardVoltageDrop`（0.7 V / 0.3 V）；检查器新增“半导体材料”下拉框；BJT 策略全部 VBE/VEB 使用该压降（NPN/PNP、四类偏置、截止/饱和判据）；二极管参数随材料切换 VF 并随工程保存 |
| 多级与混合非线性网络分析放到后续项目规划 | **本轮不实现**；失败信息改为明确拒绝；写进开发规格/项目架构后续规划，给出建议实施顺序（单管全类型 → 多级 → 二极管非线性 → 混合管 → 大信号/失真） |
| VCC 符号应与 Multisim 一致 | 符号统一库中 VCC 端口改为电源横杠 + 引线（去掉向上空心箭头），文字标签由画布/卡片单独绘制；方向随元素旋转 |
| NPN/PNP 符号相同无法区分；两个 JFET 符号不对；严格按国际标准修改元件符号 | 新增 `Models/SymbolGeometry.cs` 纯矢量符号库，画布与符号卡片共用；NPN/PNP 发射极实心箭头方向相反；N/P JFET 按 IEC 60617（箭头方向、漏/源垂直引出、去外圆）；四类 MOSFET 区分增强/耗尽与 N/P；GND/IEC 三横线、二极管、电源、受控源等同步修正 |
| 所有 MOS 管参数里的电压指代什么？若是阈值电压要说明 | 新增 `ParameterLabel/ParameterDescription/FormattedParameterDisplay`：增强型 MOS 显示“阈值电压 Vth”，耗尽型 MOS/JFET 显示“夹断电压 VGS(off)”，并给出解释；检查器标签、值下方说明、画布标签与符号卡片描述同步；BJT β、二极管 VF、受控源 μ/gm/rm 也标注 |

## 2. 引擎：PNP 与硅/锗压降

文件：`apps/desktop-winui/Engine/Strategies/BjtAmplifierStrategy.cs`、`Engine/TopologyClassifier.cs`。

- 只允许单只 BJT；多级级联、NPN+PNP 混合、含二极管等非线性器件明确拒绝，主 guard 文案改为“仅支持单个 NPN 或单个 PNP 放大电路；多级级联、NPN-PNP 混合网络以及含二极管等非线性器件的网络尚未支持，请拆分为单管放大电路后分别求解。”
- PNP 镜像拓扑：Re 属 E–VCC 侧、Rc 属 C–GND 侧、Rb1 B–VCC、Rb2 B–GND、集电极反馈 B–C、共基极输入在 E；四类子类型均输出 5 步推导；无外接负载按开路；耦合/旁路电容按连通性识别。
- 硅/锗压降：所有 VBE/VEB、截止/饱和判据、共基极与电解电容节点电位使用 `bjt.ForwardVoltageDrop`（硅 0.7 V、锗 0.3 V），无硬编码 0.7；NPN 默认硅时数值与旧实现一致。
- QPoint：PNP 的 `Vceq` 填 `|U_CE|` 正值，`WorkingRegion` 写明 `PNP: U_CE = -|U_CE| (V_EC>0)`；IBQ/ICQ 为正值，摘要注明 PNP 电流方向与材料。
- 失败原因细化：多管/混合/非线性、缺偏置、未覆盖电阻支路、截止（I_BQ≤0）、饱和（U_EC/V_CE≤0.2 V）、参数非有限分别给出原因；不补默认器件。
- 分类描述：PNP 加 “PNP ” 前缀，NPN 描述不变；分类优先级、MOS/JFET 拒绝逻辑、返回策略不变。

独立手算基准（EngineV2Checks 内，容差 0.1%，全部通过）：

| PNP 子类型 | 参数（VCC=12 V，β=100 除注明） | Si 实测/手算 | Ge 实测/手算 |
|---|---|---|---|
| 固定偏置 | Rb=470 kΩ(B–GND)、Rc=2 kΩ(C–GND)、E→VCC | IBQ 24.043 µA、ICQ 2.404 mA、VEC 7.191 V、Av −156.08、Ri 1.278 kΩ、Ro 2 kΩ | IBQ 24.894 µA、ICQ 2.489 mA、VEC 7.021 V、Av −160.71 |
| 分压偏置 | Rb1 15k(B–VCC)、Rb2 51k(B–GND)、Re 1k(E–VCC)、Rc 3k、RL 10k、耦合/旁路电容 | 18.006 µA、1.801 mA、4.780 V、Av −140.37、Ri 1.440 kΩ、fL≈31.3 Hz | 21.558 µA、2.156 mA、3.355 V、Av −164.13 |
| 集电极反馈 | Rb 200k(B–C)、Rc 2k、E→VCC | 28.110 µA、2.811 mA、6.322 V、Av −176.02、Ri 563.7 Ω、Ro 1.980 kΩ | — |
| 共基极 | Rb1 15k、Rb2 30k、Re 2k(E–VCC)、Rc 3k、RL 3k、β=50 | 29.464 µA、1.473 mA、VEC 4.575 V、Au +80.44、ri′ 18.1 Ω | 33.036 µA、1.652 mA、3.675 V、Au +89.60 |

NPN 回归：同一分压电路硅→锗时 IBQ 18.006→21.558 µA、VCE 4.780→3.355 V（0.3 V 压降生效）。拒绝用例：2×NPN 多级、NPN+PNP 混合、PNP 截止/饱和、PNP+二极管、PNP 缺基极偏置均明确失败。

## 3. 数据模型与工程格式

- `Models/CircuitElement.cs`：新增 `SemiconductorMaterial { Silicon, Germanium }`、`Material`、`MaterialIndex`、`MaterialOptions`、`ForwardVoltageDrop`、`SupportsSemiconductorMaterial`、`ParameterLabel`、`ParameterDescription`、`HasParameterDescription`、`FormattedParameterDisplay`；`Clone()` 复制材料；二极管切换材料时同步数值（0.7/0.3 V）。
- `Models/CircuitSymbolItem.cs`：新增 `DefaultMaterial`，新放置元件继承符号默认材料。
- `Models/CircuitProjectData.cs`：元件 DTO 新增 `Material`，工程版本 1.2.0（读取兼容 1.0.0/1.1.0；旧文件缺省硅管），校验未知材料枚举。
- 检查器（`Views/AnalysisStudioPage.xaml`）：参数标签按类型显示（R/C/L/β/Vth/VGS(off)/VF/μ/gm/rm…），值下方显示物理含义说明，新增“半导体材料”下拉框（仅二极管/BJT 可见）。
- 画布标签（`Controls/CircuitCanvas.xaml.cs`）：半导体元件值显示 `FormattedParameterDisplay`（β/Vth/VGS(off)/VF）。

## 4. 元件符号（国际标准矢量库）

文件：`Models/SymbolGeometry.cs`（新增，纯 C#/System.Numerics）、`Controls/CircuitSymbolPreview.xaml.cs`、`Controls/CircuitCanvas.xaml.cs`。

- 符号库输出线段/实心多边形/圆三类图元，坐标与 `CircuitElement.InitializeDefaultPins()` 的引脚一致；画布（Win2D）与左侧卡片（XAML）都从同一几何渲染，不再各自手绘。
- NPN/PNP：外圆+基极竖条+集电极/发射极斜线，发射极箭头为实心三角形，NPN 向外、PNP 向内（据此可区分）。
- JFET N/P：按 IEC 60617 竖直沟道+独立栅极线+实心箭头（N 指向沟道、P 背离），漏/源引线从沟道上下端垂直引出，去除不必要的外圆。
- MOSFET 四类：栅极板与沟道分离，增强型沟道 3 段、耗尽型 1 段实线，衬底/源箭头 N 指向沟道、P 背离，去除外圆。
- VCC：Multisim 风格电源横杠 + 引线，无向上箭头；GND：IEC 三横线；二极管：三角+阴极竖线；独立源：圆+极性/方向；受控源：菱形+控制端标记（四根引线与 IN1/IN2/OUT1/OUT2 引脚对齐）；R/C/L/变压器保持 IEC/ANSI 风格并统一线宽。
- 符号库为纯 C#，新增 `tests/SymbolChecks` 从几何层验证标准不变量（NPN/PNP 箭头方向、JFET/MOS 箭头、增强/耗尽差异、VCC 横杠、二极管极性、引脚对齐、无 NaN 等）。

## 5. 多级与混合非线性网络：后续规划（本轮不实现）

按用户要求，本轮不实现多级/混合非线性分析，当前明确拒绝并写入开发规格与项目架构的后续规划。建议顺序：

1. 单管全类型（本轮完成：NPN/PNP + 硅/锗）；
2. 多级级联（阻容耦合/直接耦合，逐级 Q 点 + 级联小信号）；
3. 二极管非线性（Newton–Raphson 迭代、整流/钳位/二极管偏置）；
4. NPN+PNP 混合管拓扑识别（互补对称/推挽/镜像电流源）；
5. 截止/饱和区大信号迭代、失真分析与更完整高频模型。

以上 2–5 项目前均返回 `IsSuccess=false` 并给出明确原因，不补默认器件、不使用教材固定答案。

## 6. 测试覆盖

- `tests/EngineV2Checks`：新增“PNP 与硅/锗压降”段，覆盖 PNP 四类偏置、Si/Ge 数值差异、NPN 回归、拒绝用例（多级/混合/截止/饱和/二极管/缺偏置）、分类描述；209 → 303 项断言。
- `tests/M2SolverChallenger`：工程材料字段保存/读取、1.2.0 版本、未知材料拒绝等断言（155/155）。
- `tests/SymbolChecks`（新增）：符号标准几何断言（数量见第 7 节）。
- 既有 M2/ChallengerRunner/M3/M3_4/EngineV2 断言未删减或弱化。

## 7. 验证结果

| 检查 | 结果 |
|---|---|
| WinUI 主项目构建（隔离 OutDir） | 成功，0 警告/0 错误 |
| EngineV2Checks | 303/303（新增 PNP/硅锗段 94 项） |
| M2SolverChallenger | 155/155（含工程材料/版本回归） |
| ChallengerRunner | 131/131 |
| M3AdversarialStress | 32/32 |
| M3_4EmpiricalStress | 9/9 |
| ReportExportChecks | 110/110 |
| WebImportChecks | 168/168（导入版本断言更新为 1.2.0） |
| SymbolChecks（新增） | 169/169 |
| 常规 C# 合计 | **1077/1077**（155+131+32+9+303+110+168+169） |
| NativeBenchmark30 | 30/30（100%）+ `--roundtrip` 30/30（31/31 网表等价） |
| EngineAuditChecks（第二轮审计，回归不回退） | 1359/1359 |
| 符号目视核对 | 26 类型总览与 18 个关键符号放大图（临时目录），确认 NPN/PNP 箭头相反、JFET 漏源垂直引出、四类 MOSFET 区分、VCC 横杠、GND 三横线 |

说明：常规套件中 M2 由 152 增至 155、EngineV2 由 209 增至 303、新增 SymbolChecks 169 项；第二轮审计 1359 项与 30 题基准本轮未改动代码路径但随源码一起复跑通过。真实窗口交互、符号像素级视觉验收仍未进行（用户进程仍运行旧构建，需重新构建并重启才能看到新符号）。

## 8. 文档同步

已同步：`README.md`、`PROJECT.md`、`HANDOVER.md`、`CLAUDE.md`、`专业名词解释.md`、三层 technical report、开发规格、测试计划、`projects/README.md`；新增本记录。第二轮修订记录/审查报告保留原文。

## 9. 剩余边界

- 多级级联、NPN+PNP 混合、二极管等非线性、MOS/JFET 实际求解、大信号/失真分析：未实现，列入后续规划。
- PNP 仅支持镜像画法（Re 在 E–VCC 侧、Rc 在 C–GND 侧）；输出负载仍按“输出耦合电容后接 GND”识别，PNP 负载接 VCC 的镜像画法暂不支持；固定/反馈偏置要求发射极直接接交流地轨；饱和压降统一近似 0.2 V；材料只影响 |VBE|，不影响 β/rbb′/结电容/温度；高频仍为估算。
- 符号为 IEC/IEEE 315 教学示意，未做真实窗口像素级视觉验收；变压器等少数符号仍为简化示意。与 Multisim 参考图的已知差异：BJT 集电极/发射极引线采用 IEEE 315 单斜线（未加 Multisim 式垂直折弯）；JFET/MOSFET 按 IEC 标准去掉外圆（Multisim 风格常带包络圆，如需要可在几何库对应 builder 增加一个圆）；交流电流源为“圆+箭头+正弦”的折中画法；JFET 栅极箭头画在栅极引线上（部分 IEC 版式画在栅极条上，语义一致）。
- `SymbolLibraryPage.xaml` 是未接入主窗口导航的静态示例页（12 张卡片，未含 VCC 端口/受控源），主工作台符号列表与画布均已使用新几何；如后续启用该页需同步其卡片。
- 用户当前运行的客户端是旧构建（进程未结束）；新符号与参数需重新构建并重启客户端后才能看到。真实窗口交互、保存/关闭全流程、高 DPI/主题视觉、手机与视觉识别仍未验收（视觉按路线图后续阶段开发）。
