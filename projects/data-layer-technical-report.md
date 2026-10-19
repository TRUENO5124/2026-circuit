# 数据层实现报告：WinUI 编辑模型、工程文件与电气网表

> 更新：2026-10-18（第九轮能力增量：BJT 元件 `BjtRbb`/`BjtRbe` 字段与 DTO 持久化/校验、旧工程缺省 0=自动、M2 rbb-rbe 保存读回断言；第八轮：`AcParametersInfo` Rs/Aus 显示字段、保存保留 `ProjectName` 语义修复；工程文件格式仍为 1.2.0 未变；第七轮：`QPointInfo.Exact*` 后端精确字段、`AnalysisStepGroup`/`StepGroups` UI 派生分组、教材估算显示 Values；第六轮：MOS K/λ 字段、29 个教学电路文件、单文件诊断）；以当前工作区 C# 源码为准，含未提交的 `CircuitProjectData.cs`、`ProjectManagerService.cs`、`WebCircuitGraphImporter.cs`。早期 TypeScript/Zod 论证已保存在[历史副本](archive/2026-10-17-before-winui-doc-sync/data-layer-technical-report.md)。

## 1. 当前数据分层

当前主软件不以 Zod `CircuitGraph` 作为统一事实来源，而是在同一 .NET 进程中维护用途不同的模型：

```text
编辑态：CircuitElement / CircuitPin / CircuitWire
       ├─ CircuitProjectData.From → JSON 文件 → ToModel → 编辑态
       ├─ Web 原型 CircuitGraph JSON → WebCircuitGraphImporter → CircuitProjectData（单向导入）
       └─ NetlistExtractor → ElectricalNetlist → 分类和求解 → PedagogicalSolution / LinearNetworkInfo
                                        └→ ReportExportService → Markdown / LaTeX / DOCX / HTML
```

| 层 | 事实来源 | 消费者 | 序列化/校验 |
|---|---|---|---|
| 编辑模型 | `Models/CircuitElement.cs` | ViewModel、Win2D 画布、模板工厂 | ObservableObject，属性通知；没有统一参数约束 Schema |
| 工程 DTO | `Models/CircuitProjectData.cs` | 工程保存/打开 | `System.Text.Json`；版本/引用完整性校验 |
| 原型导入适配器 | `Services/WebCircuitGraphImporter.cs` | 打开 Web/Python CircuitGraph JSON | System.Text.Json + 映射 + `CircuitProjectData.Validate()` |
| 电气网络 | `Engine/ElectricalNetlist.cs` | 分类器、策略、SPICE 文本导出、报告导出 | 由网表提取器生成，`Validate()` 返回诊断 |
| 结构化分析结果 | `Models/LinearAnalysisModels.cs` | 展示层、报告导出 | 纯数据对象，由 `MnaLinearStrategy` 填充并映射到 `AnalysisResult` |
| 报告文档 | `Services/ReportExportService.cs` | 分析报告导出 | 纯内存四格式渲染，不写文件 |
| 独立原型契约 | `packages/schema/src/schemas.ts` | Web/Python、Benchmark | Zod 与导出的 JSON Schema；不参与原生文件加载 |

以上 C# 路径均相对于 `apps/desktop-winui/`。原生与 Web 模型目前只有 **Web → 原生** 的单向导入适配器，没有原生 → Web 导出或双向 Schema 镜像；导入只保证 3 个原型 benchmark 样例的拓扑还原与格式转换，不保证所有第三方 CircuitGraph 变体。

## 2. 编辑模型

### 2.1 CircuitElement

| 字段 | 语义 |
|---|---|
| `Id/Name/DefinitionId/Category/Description` | 实例标识、标签、符号定义引用及展示描述 |
| `Type` | `ElementType` 枚举，决定原生绘制、默认引脚及求解分类 |
| `DisplayNumber/UnitSymbol` | 用户编辑的数值和显示单位；含义随类型变化（见 `ParameterLabel`），求解时转换 SI |
| `Material` | 半导体材料（SILICON/GE）：硅管压降≈0.7 V、锗管≈0.3 V；二极管映射为 VF，BJT 映射为 \|VBE\| |
| `ForwardVoltageDrop` | 由 `Material` 派生的压降（只读，硅 0.7 V / 锗 0.3 V） |
| `MosfetK` / `MosfetLambda` | MOSFET 平方律参数：K（A/V²，默认 1 mA/V²）与沟道长度调制 λ（1/V，默认 0，0 表示忽略）；随工程 DTO 持久化，检查器当前不提供编辑框 |
| `BjtRbb` | BJT 基区体电阻 r_bb'（Ω，默认 0=自动：共射/共集 200 Ω、共基 50 Ω）；随工程 DTO 持久化，检查器与参数设置两处可编辑；`NaN`/负数自动归零 |
| `BjtRbe` | BJT 题目给定 r_be=h_ie（Ω，默认 0=按 Q 点计算；>0 直接采用给定值参与 Av/Ri/Ro/fL）；随工程 DTO 持久化；`NaN`/负数自动归零 |
| `SupportsBjtSmallSignalParameters` / `BjtRbbDisplay` / `BjtRbeDisplay` | 仅 NPN/PNP 为 true（UI 显隐与宏同步）；显示"自动（共射/共集 200 Ω，共基 50 Ω）"/"按 Q 点计算（r_be=r_bb'+(1+β)U_T/I_EQ）"或给定值 |
| `ParameterLabel/ParameterDescription/FormattedParameterDisplay` | 参数含义与显示标签：β、Vth、VGS(off)、VF、μ/gm/rm 等，供检查器、画布与提示使用 |
| `Value/FrequencyHz` | Value 为保留数值；FrequencyHz 独立记录交流源频率，幅值仍用 DisplayNumber/UnitSymbol；均保存到 DTO |
| `X/Y` | 元件中心的内部画布坐标 |
| `Width/Height` | 命中区域大小，不等同电气引脚间距 |
| `RotationDeg/Orientation` | 旋转角与方向标签；引脚位置由角度计算 |
| `IsSelected/Pins` | 选中状态与引脚集合 |

`GetPinWorldPosition(pin)` 使用相对坐标旋转后加元件中心；`HitTest()` 反向旋转测试点后检查局部矩形。`GetBounds()` 仍返回未旋转矩形，不能把它视为所有几何计算统一的旋转外接框。

`Clone()` 深复制引脚，服务于撤销快照。界面格式化属性通过 `NotifyPropertyChangedFor` 更新；修改通知不自动保证工程脏标志、撤销记录和参数语义正确。

### 2.2 引脚契约

`CircuitPin` 包含 `Id/Name/RelativeX/RelativeY/ConnectedNetId`，未连接的默认 NetId 是 -1。

| 类型 | 默认引脚 ID |
|---|---|
| 电阻、普通电容、电感 | `1/2` |
| 电解电容 | `+/-` |
| 电压源 | `pos/neg` |
| 电流源 | `in/out` |
| 二极管 | `A/K` |
| BJT | `B/C/E` |
| MOS/JFET | `G/D/S`，原生模型目前没有第四个 bulk 引脚 |
| 地 / VCC 端口 | `GND` / `VCC` |
| 变压器 | `P1/P2/S1/S2` |
| 四种受控源 | `IN1/IN2/OUT1/OUT2` |

`FindNetForPin()` 对电解电容的 `+/-` 与 `1/2/pos/neg` 做有限别名匹配；这不是任意 Web 端口名到原生端口名的映射。新增元件需同时核对默认引脚、绘图、提取器和求解策略。

### 2.3 导线与分支

`CircuitWire` 保存：`Id`、两端元件/引脚引用、`StartX/StartY/EndX/EndY`、`NetId`、`IsSelected`、`FlipCorner`、`CustomMid`、`Waypoints`。

- 普通端：ElementId 是元件 ID，PinName 是引脚 ID 或名称。
- WireTap 端：PinName 为 `WireTap`，ElementId 是父导线 ID。
- 自由端/纯母线：ElementId 可以为空或 `None`；不能要求所有导线都具有两个元件绑定。
- `GetPathPoints()` 从端点和 Waypoints 或默认拐角生成路径，并执行正交化、重复点和共线点简化。
- 线段拖动通过 `MoveSegmentFromBaseline()` 更新中间点，端点仍应跟随引脚；`ProjectTapPerpendicular()` 用于沿父线投影，并含 10 单位吸附。
- NetId 是提取后的派生值；持久化虽记录该值，求解应以重新提取结果为准。

## 3. 原生符号与参数

符号由 `AnalysisStudioViewModel.InitializeSymbols()` 的 C# 条目初始化，当前 **25 个条目**。它们包括同类电源别名和 FET 变体，不能与 `ElementType` 枚举数量或 Web 的 18 个定义混为一谈。

分类、检索、默认数值和单位来自 `CircuitSymbolItem`。画布和卡片分别绘制符号，并没有从 TypeScript 元件库或 SVG 文件自动生成 C# 符号。

单位选项由 `Models/EngineeringUnit.cs` 定义。`NetlistExtractor.ConvertToBaseSi()` 按元件类型转换电阻、电容、电感、电压和电流的常见前缀；各策略的 GetSiValue 包装现统一调用该转换函数。现已按元件类型校验单位并统一调用 ConvertToBaseSi；区分 kΩ/MΩ 等前缀大小写，非法单位返回错误。仍不是完整物理量代数系统。

工程另存 ParameterUnits 字典保存模板宏单位。加载宏参数时抑制对元件的回写，以已还原的元件值和单位为准；交流源 FrequencyHz 与幅值独立。文件层保存/加载/替换回归已通过，窗口选择器后的完整操作仍待验收。

## 4. 工程文件格式与生命周期

### 4.1 顶层字段

| JSON 字段 | 类型/默认值 | 用途 |
|---|---|---|
| `Version` | string，`1.2.0` | 新格式；读取接受 1.0.0/1.1.0/1.2.0，其余版本明确拒绝；1.0/1.1 缺少材料字段时按硅管处理 |
| `ProjectName/ProjectFileName` | string | 工程显示名称与文件名 |
| `Description` | string | 工程说明 |
| `CreatedAt/ModifiedAt` | DateTime | ViewModel 保留原 CreatedAt，保存时更新 ModifiedAt |
| `Parameters/ParameterUnits` | 数值/单位字典 | 模板宏与单位；另外保存 IsTeachingTemplate |
| `Elements` | `List<CircuitElementDto>` | 元件、几何、类型、显示值、单位、引脚 |
| `Wires` | `List<CircuitWireDto>` | 端点引用、坐标、网络和路径 |

元件 DTO 保存 Id、Name、DefinitionId、Category、Type、Material、DisplayNumber、UnitSymbol、X/Y、Width/Height、RotationDeg、Orientation、Pins。第三轮补入半导体材料，第五轮补入可选 `MirrorX`，**第六轮补入 `MosfetK`/`MosfetLambda`**，**第九轮补入 `BjtRbb`/`BjtRbe`**（旧版软件读取时忽略新字段；旧文件缺省 K=1 mA/V²、λ=0、BjtRbb/BjtRbe=0=自动；负数/非有限值在 `Validate()` 抛出 `InvalidDataException`）；此前已补入 Description、Value、FrequencyHz；选中状态不作为工程内容保存。

引脚 DTO 保存全部五个引脚字段；导线 DTO 保存除 IsSelected 之外的上述导线字段。Waypoints 转成二维 `float[]` 数组列表；读取前会拒绝无效路径数组，缺少引脚时生成该元件的默认引脚。

### 4.2 文件服务

`ProjectManagerService` 使用缩进 JSON、忽略属性名大小写、字符串枚举和 `UnsafeRelaxedJsonEscaping`。内容写入 UTF-8 文本；不是二进制原理图格式。

- 保存选择器：`.circuit` / `.json`。
- 打开选择器：`.circuit` / `.json`；读取后先探测 JSON 形状：Web/Python `CircuitGraph`（benchmark 包装或裸图）交给 `WebCircuitGraphImporter` 转换，其余按自有工程 DTO 严格加载。导入结果视为未保存工程（清空原路径、置脏标志），不会把 Web JSON 路径当作原生工程路径。
- SPICE 选择器：`.cir` / `.sp` / `.txt`，内容来自 `ToSpiceNetlist()`。
- 报告选择器：`PickExportReportFileAsync` 按格式提供 `.md` / `.tex` / `.docx` / `.html`，内容来自 `ReportExportService`，服务本身不写文件。
- `WindowHandleProvider` 在 `App.OnLaunched` 注册，供 WinRT 选择器关联窗口。
- `GetDefaultProjectsDirectory()` 可创建“文档/CircuitAssistant/Projects”，但当前保存流程由系统选择器决定路径，未统一使用该目录。

打开前先反序列化、校验并还原模型，成功后才替换编辑集合。校验包括版本、有限数值/几何、唯一 ID、引脚引用、父导线存在性及自引用。保存先写同目录临时文件并刷盘，再替换目标；失败时清理临时文件，不先截断原文件。

### 4.3 状态安全与兼容边界

新建时清理旧保存路径、历史、选择和结果；新建/打开/关闭及窗口关闭前通过“保存/不保存/取消”处理修改。保存捕获修订号，保存期间的新编辑继续保持未保存状态。集合和实体字段变更会标记工程修改，选择/派生 NetId 不计入用户编辑。编辑或切换工程会清空供报告导出使用的最近解缓存。

格式仍是自有 JSON，不能导入任意第三方 `.sch`。兼容读取 1.0.0/1.1.0 文件时，新字段采用默认值（材料按硅管、`MirrorX=false`、`MosfetK=1 mA/V²`、`MosfetLambda=0`、`BjtRbb=0`、`BjtRbe=0`）；没有任意旧版本迁移器。第二轮新增 Web/Python `CircuitGraph` 单向导入：`WebCircuitGraphImporter` 把 `definitionId`/端口/参数映射到原生 `CircuitElement`，用确定性网格自动布局（原图无坐标），每条 connection 生成一根开放端导线，由网表提取器的端点几何合并还原节点；未知定义、无法映射端口或缺少必需参数会汇总抛出 `InvalidDataException`。第三轮工程格式升为 1.2.0，新增元件材料字段；1.0/1.1 文件可直接读取。**第六轮 `examples/circuit-models/` 下的 29 个教学电路也是当前 1.2.0 原生工程**（含 `MosfetK`/`MosfetLambda` 字段），可直接由“打开工程”加载；它们由 `tools/CircuitModels.*` 生成并通过结构/分类/求解自检。`document/3-6题.circuit` 等用户工程同样按 1.2.0 加载，可用 `tests/CircuitModelChecks -- --file <path>` 诊断。**第七轮未改工程文件格式（仍为 1.2.0）**：`QPointInfo.Exact*` 与 `AnalysisStepGroup`/`StepGroups` 都是求解结果/UI 派生结构，不进入 `CircuitProjectData` 的序列化字段；旧工程可直接打开。**第九轮仍未改格式号（仍为 1.2.0）**：BJT 元件新增 `BjtRbb`/`BjtRbe` 可选字段，旧工程缺省 0=自动/按 Q 点计算，行为与第八轮一致；保存/读回由 `M2SolverChallenger` 10.8c 断言覆盖。

导入边界：
- 只支持 benchmark 包装与裸 `CircuitGraph` 两种形状；不支持任意第三方 EDA 文件。
- 四端 MOSFET 的第 4 端 bulk/body 折叠到 source（原生只有 G/D/S 三脚）。
- 非物理原型 benchmark seed（如 BJT 样例 VCEQ≈−10.6 V）可导入但不保证可解，需换真实教材参数。
- 视口和完整求解结果不保存；可变引脚集合/原地路径列表等非 UI 修改的历史覆盖仍需后续完善。

工程保存/替换/加载往返与非法输入回归已在第二轮完成（具体检查见第二轮修订记录）；第七轮未改工程文件格式或加载路径，未声称兼容任意第三方或旧版本格式。

## 5. 电气网表

`ElectricalNetlist` 保存 `Nets/Nodes/Branches/Transistors`，以及元件和导线字典；并非原生工程持久化格式。

- `ElectricalNet`：NetId、名称、Ground/Power 标志、引脚引用与导线 ID。
- `ElectricalNode`：求解节点，Index 0 为参考地；包含关联支路。
- `ElectricalBranch`：二端支路、正负节点与引脚、SI 值。
- `TransistorDevice`：B/C/E 或 G/D/S 对应节点，避免多端器件被简化成单条边。

提取顺序：登记引脚/导线 → WireTap 与受约束几何连接 → 引脚与导线连接 → 有保护的直接引脚贴合 → 合并地 → 参考地回退 → 编号命名 → 回写 NetId → 建立节点、支路和晶体管。

默认容差 8/5 单位，显式引脚链接还有 15 单位的有限回退；结果仍依赖几何，不是完全独立于布局的纯拓扑格式。母线识别还依赖 `_RAIL/_BUS` ID 约定。缺地时优先已有 NetId=0 的导线，再选源负极，最后选引脚最多的网络；自动设置 IsGround 后会由 Validate 提示 IMPLICIT_GROUND，而非假装用户已放置 GND。

## 6. 校验与基准

`Validate()` 诊断码包括 `NO_GROUND`、`BRANCH_SHORT_CIRCUIT`、`FLOATING_PIN`、`DEGREE_ZERO_PIN`、`POWER_SHORTED_TO_GROUND`，返回 Info/Warning/Error 列表。正式求解已接入 Validate；新增 INVALID_PARAMETER、IMPLICIT_GROUND、DISCONNECTED_NETWORK 等诊断。短路、悬空/分离网络、非法参数会阻止分析，自动参考地作为提示展示。策略另外检查支持的器件和拓扑。

原型保留 Zod 图校验、18 个元件定义、3 个 Benchmark 和三份 `generated/schema/*.json`；Python 使用 dataclass 和手动加载，不是由 JSON Schema 生成的 Pydantic 镜像。Benchmark 的图片引用仍为空。第二轮新增：

- **原生导入**：3 个 Web/Python benchmark 样例均可导入并还原 groundTruth 拓扑（元件/连接/节点分区一致，见 `tests/WebImportChecks` 172 项断言；第六轮把 diode/MOS 样例的端到端断言更新为新教学策略行为）。
- **原生 30 题基准**：`tests/NativeBenchmark30` 用 30 道独立手算教材题覆盖一阶/二阶/BJT/通用线性 MNA 2.0，`Total=30 Passed=30 Failed=0 Accuracy=100.00%`（第七轮 B08 共基极 `Re` 由 1 kΩ 调整为 2 kΩ，使教材估算法下仍处放大区；原因见[第七轮修订记录](technical-code-fixes-20261018-round7.md)）；这是原生 C# 引擎的本地基准，不等于 Web/Python 原型的 30 题目标或任意拓扑正确率。
- **29 个教学电路文件 + 单文件诊断**：`tests/CircuitModelChecks` 逐文件执行 反序列化 → Validate → ToModel → 提取网表 → 分类 → 求解，`Total=259 Passed=259 Failed=0`；`-- --file <path>` 可对任意原生 `.circuit`（含 `document/3-6题.circuit`、B07 变体）打印加载/分类/求解/全部步骤与 Values。`tests/NonlinearCircuitChecks` 对二极管/MOSFET/BJT 共集/多级预留/教材估算法与 DC/AC 相位做手算对拍与失败路径检查，**第八轮扩至 822/822**（新增 `BjtSourceResistanceChecks` 71 项），**第九轮扩至 `Total=913 Passed=913`**（新增 `BjtSmallSignalParameterChecks` 91 项：默认自动/自定义 rbb/题目给定 rbe，CE/CB/CC、NPN/PNP、`rbb_ohm`/`rbe_ohm`/`rbe_source` 键、非法值）；`tests/M2SolverChallenger` **160/160**（原 156 + 4 条 rbb/rbe 宏同步/默认自动/修改失效/保存读回；第八轮 Suite 7 已改为反射 `MathRendering` 新 API 并含保存保留工程名断言）；`tests/MathFormulaChecks`（411/411，公式解析/布局）与 `tests/DisplayRoundingChecks`（55/55，显示文本 ≤2 位小数）保持通过。4 个生成器的 `--verify` 结果为 7/7、6/6、10/10、6/6（D06 工程名恢复后从 6/7 回到 7/7）。**第九轮按用户要求不做 ui-capture 窗口验收**，仅控制台回归 + 构建。

原生与 Web 的格式互通仍是单向导入：没有原生工程导出为 `CircuitGraph` 的实现，也没有跨语言统一契约测试。

## 7. 结构化线性结果与报告数据契约

`Models/LinearAnalysisModels.cs` 定义 `LinearNetworkInfo`（供 `PedagogicalSolution.LinearNetwork` 与 `AnalysisResult` 使用）：

| 类型 | 主要字段 | 数据来源/语义 |
|---|---|---|
| `NodePotentialInfo` | NetName/NetId/VoltageV/Display/LatexFormula | MNA DC 电位或 AC 峰值幅值（Display 含相位） |
| `PortEquivalentInfo` | PortLabel/正负网/ResistanceOhm/HasOpenCircuitVoltage/Voc/Display | 任意端口 Req+Voc；Req 可能为 +∞ |
| `AcSweepPointInfo` | FrequencyHz/GainRatio/GainDb/PhaseDeg/Display | 10 Hz–10 MHz、61 点扫频 |
| `FrequencyResponseInfo` | 输入输出端口、增益/相位、截止/带宽、Points、BoundaryNote | AC 相量/频响摘要 |
| `LinearNetworkInfo` | ModelSummary/OperatingMode/MatrixDimension/上述集合/BoundaryNote | MnaLinearStrategy 填充 |

- 数值字段用于报告和程序消费，`Display` 为界面文本；不要把 Display 反解析成数值逻辑。
- `AnalysisResult` 通过 `ToAnalysisResult()` 映射 `LinearNetwork/FrequencyResponse/NodePotentials/PortEquivalents/AcSweepPoints`；XC/RL/RLC/BJT 专用策略不填这些集合。
- `ReportExportService` 只读 `ElectricalNetlist` + `PedagogicalSolution`（含 `LinearNetwork`），不重新求解、不修改数据；`AnalysisReportContent` 由 ViewModel 从最近一次成功/失败的求解缓存构造。
- `WebCircuitGraphImporter` 的输出是普通 `CircuitProjectData`，导入后数据层与手工绘制工程完全同构（无特殊标记）。

### 7.1 教学结果显示字段（第七轮新增/调整）

**`QPointInfo.ExactIbq/ExactIcq/ExactVceq`（`Models/AnalysisResult.cs`，`string` 类型）**：后端戴维南精确解，由 `BjtAmplifierStrategy` 在 DC 求解时写入（分压/固定/集电极反馈/共基/共集均填写）。UI 只显示教材估算法结果，这三个字段**不在 XAML 绑定**，仅供测试、诊断与报告内部使用；无精确解的策略（二极管/MOS/RC/RL/RLC/MNA）保持空字符串。它们是 UI 结果模型字段，不随工程文件持久化。

**`AnalysisStepGroup` / `AnalysisResult.StepGroups`（`Models/AnalysisResult.cs`）**：`ToAnalysisResult()` 按 `PedagogicalSolutionStep.Phase`（`SolutionPhase.None/DcEquivalent/AcEquivalent`）把步骤相邻归组，生成 `AnalysisStepGroup { Phase, Title, Subtitle, HasHeader, Steps }` 集合：
- BJT/MOS 为 2 组（DC 在前、AC 在后，组标题/说明来自 `SolutionPhaseInfo`）；
- RC/RL/RLC/MNA/二极管等 `None` 相位策略为单一无标题组（`HasHeader=false`），保持原平铺外观；
- 分组只是 UI 派生结构，**不写入工程文件**（工程仍只保存元件/导线/参数），也不改变 `TopologyType`/`SubtypeName`。

**步骤 `Values` 结构化键（第七轮调整）**：`PedagogicalSolutionStep.Values` 仍属于结构化契约但只存在于求解结果中（不随工程持久化）。BJT 第 1 步 `Values` 同时含：
- 教材估算显示链：`IBQ`/`ICQ`/`IEQ`/`VCEQ`（舍入后回代值）与 `UB`（分压偏置）；
- 后端精确诊断键：`IBQ_exact_uA`/`ICQ_exact_mA`/`UCE_exact_V`/`IEQ_exact_uA`，分压偏置另有 `Vth_V`/`Rth_ohm`/`UB_actual_V`；
- 其余步骤：`rbe`/`Av`/`Ri`/`Ro`/`fL`/`fH`/`BW`/`beta`/`VCC`，共集/共基另有 `hie`/`hib`/`hfb` 等；MOSFET 为 `ID/VGS/VDS/gm/Av/Ri/Ro`。
原先第六轮的 `UB_textbook_V`/`IBQ_textbookRounded_uA`/`EstimateDiffPercent`/`Rth_over_betaRe` 等"对照"键已随双口径显示删除（历史见[第六轮修订记录](technical-code-fixes-20261018-round6.md)）。

**`AcParametersInfo` 的 Rs/Aus 字段（第八轮新增，`Models/AnalysisResult.cs`）**：

| 字段 | 类型/默认值 | 语义 |
|---|---|---|
| `HasSourceResistance` | `bool`，默认 `false` | 拓扑是否识别到信号源内阻 Rs；控制实时指标卡与报告两行的可见性 |
| `SourceResistance` | `string`，默认 `"—"` | 教材格式化后的 Rs 显示文本（如 "50 Ω"） |
| `SourceVoltageGain` | `string`，默认 `"—"` | 教材格式化的源电压增益 Aus（`Av·Ri/(Rs+Ri)`；共基 `Au·ri'/(Rs+ri')`） |

这三个字段是 UI/报告展示用的求解结果字段，**不随工程文件序列化**；无 Rs 的电路保持默认值。第 1 步 `Values` 在识别到 Rs 时另含稳定键 `Rs_ohm`/`Aus`，无 Rs 时不写入（回归断言据此保护无 Rs 行为与第七轮一致）。

**BJT 小信号参数结果键（第九轮，`BjtAmplifierStrategy`）**：rbe/h_ie 步骤的 `Values` 新增稳定键 `rbb_ohm`（实际使用的 r_bb'，Ω）、`rbe_ohm`（实际使用的 r_be/h_ie，Ω）、`rbe_source`（字符串 `computed`|`given`）；共基保留 `hie`。它们是求解结果字段，不进入 `CircuitProjectData` 序列化；默认 `BjtRbb=0`/`BjtRbe=0` 时 `rbb_ohm` 取 200/50、`rbe_source=computed`，与第八轮数值一致。

**保存 `ProjectName` 语义（第八轮修复，`ViewModels/AnalysisStudioViewModel.cs`）**：`SaveProjectCoreAsync` 保存时保留工程标题 `ProjectName`，只有"未命名电路工程"或空标题才回退为 `Path.GetFileNameWithoutExtension(路径)`；`ProjectFileName` 仍更新为当前文件名。旧行为每次保存都强制 `ProjectName=文件名`，会改写教材示例标题（B07/D06）并导致生成器 `--verify` 的工程名校验失败。该修复不改变工程文件字段/版本（仍 1.2.0），只改变保存时写回的 `ProjectName` 值。

代码修改前的历史测试见[原审查报告](technical-document-review-20261017.md)。第九轮已运行构建和数据回归；当前实现变更、验证结果与待确认边界见[第九轮修订记录](technical-code-fixes-20261018-round9.md)、[第八轮修订记录](technical-code-fixes-20261018-round8.md)与[第七轮修订记录](technical-code-fixes-20261018-round7.md)。
