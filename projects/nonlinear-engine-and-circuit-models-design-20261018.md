# 非线性器件引擎与 29 个电路模型工程文件：设计与协作契约（2026-10-18）

> 性质：本轮（第六轮）实施契约。所有参与智能体必须先读本文再改代码。
> 目标：二极管/MOSFET 非线性求解 + BJT 共集电极 + 多级预留接口 + 29 个原生 `.circuit` 文件。
> 基线：`git refs/dsh-backup/pre-nonlinear-20261018`（本轮开始前的工作区快照，用于回滚对照）。

## 1. 本轮范围

| 项 | 状态 | 说明 |
|---|---|---|
| 二极管非线性求解 | 必做 | 恒压降分段线性模型 + Shockley/Newton 直流偏置模型；半波/桥式整流、电容滤波、串联/双向限幅、钳位电路族 |
| MOSFET 非线性求解 | 必做 | 平方律（Level-1）NMOS/PMOS，增强型；共源/共漏/共栅；Q 点、gm、Av、Ri、Ro、工作区 |
| BJT 共集电极 | 必做 | 分类子类型 `CommonCollector` + Q 点/中频 Av/Ri/Ro 与推导步骤 |
| 多级放大 | 只预留接口 | 分类为 `MultiStageAmplifier` 并明确拒绝；接口契约文件 + 文档列入后续规划，**不实现级联求解** |
| 29 个电路模型文件 | 必做 | D01–D07、B01–B16、M01–M06，原生 `.circuit`（`CircuitProjectData` JSON） |
| UI 参数编辑 | 本轮不做 | MOS 的 K/λ 已写入数据模型与工程文件，但检查器本轮不新增编辑框（文档记录限制） |
| JFET/耗尽型 MOS | 引擎模型可识别，求解不承诺 | 符号仍可画；策略对 JFET/耗尽型明确拒绝（本轮 M01–M06 只用增强型） |

## 2. 新增/修改文件与所有权

### 新增引擎文件（本契约固定路径与命名空间）

```
apps/desktop-winui/Engine/Nonlinear/DiodeModel.cs            ns CircuitAssistant.Engine.Nonlinear
apps/desktop-winui/Engine/Nonlinear/MosfetModel.cs           ns CircuitAssistant.Engine.Nonlinear
apps/desktop-winui/Engine/Strategies/DiodeCircuitStrategy.cs ns CircuitAssistant.Engine.Strategies
apps/desktop-winui/Engine/Strategies/MosfetAmplifierStrategy.cs ns CircuitAssistant.Engine.Strategies
apps/desktop-winui/Engine/MultiStage/MultiStageContracts.cs  ns CircuitAssistant.Engine.MultiStage
apps/desktop-winui/Engine/Strategies/MultiStageAmplifierStrategy.cs ns CircuitAssistant.Engine.Strategies
```

### 修改文件（归属唯一）

| 文件 | 归属 | 内容 |
|---|---|---|
| `Engine/TopologyClassifier.cs` | engine-integration | 分类优先级、二极管/MOS/多级路由 |
| `Engine/Strategies/BjtAmplifierStrategy.cs` | engine-integration | `CommonCollector` 分支（DC + 小信号 + 步骤） |
| `Engine/PedagogicalSolution.cs` | **Lead** | 仅新增枚举值 `DiodeCircuit`、`MultiStageAmplifier`（已完成） |
| `Models/CircuitElement.cs`、`Models/CircuitProjectData.cs` | **Lead** | 新增 `MosfetK`、`MosfetLambda`（已完成） |
| `ViewModels/AnalysisStudioViewModel.cs` | engine-integration | `GetTopologyDisplayName` 增加新枚举分支（避免运行时落 `_`） |
| 全部 `tests/*/*.csproj` | engine-integration | 把 6 个新引擎文件显式链接进 10 个既有测试工程 |
| `tests/NonlinearCircuitChecks/**` | Lead + 三个引擎 owner | Lead 建工程骨架，三个 owner 各写一个检查文件 |
| `tools/CircuitModels.Core/**`、`tools/CircuitModels.Diode/**` | gen-core | 共享生成器 + 二极管 7 个电路定义 |
| `tools/CircuitModels.BjtBias/**` | circuits-bjt-bias | BJT 偏置/工作点 6 个电路定义（B01–B06） |
| `tools/CircuitModels.BjtAmp/**` | circuits-bjt-amp | BJT 放大/频率响应 10 个电路定义（B07–B16） |
| `tools/CircuitModels.Mos/**` | circuits-mos | MOS 6 个电路定义 |
| `examples/circuit-models/*.circuit` | 三个 generator 的产物（互不重名） | 29 个工程文件 |
| `tests/EngineV2Checks`、`WebImportChecks`、`EngineAuditChecks` 等既有断言 | verify-docs | 把"二极管/MOS 一律拒绝"的旧预期更新为新行为 |
| `PROJECT.md`、`HANDOVER.md`、`README.md`、`projects/*.md` | verify-docs + Lead | 本轮结果与边界 |

**硬规则：**任何 owner 不得编辑别人写作用域内的文件；需要跨域改动时先消息通知 Lead。

## 3. 分类契约（`TopologyClassifier.Classify`）

优先级（从高到低）：

1. 空网表 → `Unsupported/Empty`（保持现状）。
2. 晶体管总数 = BJT 数 + MOSFET 数：
   - **> 1（含 BJT+MOS 混合）** → `MultiStageAmplifier / "Cascade"`，策略 `MultiStageAmplifierStrategy`（返回明确失败，见 §7）。
   - **BJT 数 == 1** → `BjtAmplifier`；子类型按顺序：
     1. `CommonBase`（沿用现有 `IsBjtCommonBase` 判定）；
     2. **`CommonCollector`（新增）**：集电极网络直接接电源轨（C 与 VCC 之间无电阻）；发射极不直接接地且经 Re 有直流回路；基极为输入（有输入耦合电容或基极偏置网络）。
     3. 现有 `FixedBias / VoltageDividerSelfBias / CollectorFeedbackBias`。
   - **MOSFET 数 == 1** → `MosfetAmplifier`；子类型调用 `MosfetAmplifierStrategy.DetectSubtype(netlist)`（见 §5.2）。JFET/耗尽型仍返回 MosfetAmplifier + 明确拒绝策略，或由策略内部拒绝（二选一，但失败信息必须点名器件）。
3. 无晶体管且二极管数 ≥ 1 → `DiodeCircuit`；子类型调用 `DiodeCircuitStrategy.DetectSubtype(netlist)`（见 §4.2）。
4. 其余沿用现有 RC / RL / RLC / MNA 逻辑。

> 注意：`MnaLinearStrategy` 的"拒绝二极管/MOS/BJT"行为**保持不变**（直接调用 MNA 的负向测试继续有效）；本轮只改分类层与新增教学策略。

## 4. 二极管契约

### 4.1 `DiodeModel`（设备模型）

必须同时提供两类模型，供不同分析使用：

- **分段线性恒压降模型**（波形/大信号教学分析用）：导通压降 `VF = element.ForwardVoltageDrop`（硅 0.7 V / 锗 0.3 V）。导通判据 `vD ≥ VF`（考虑理想反向阻断）；反向电流 0。
- **Shockley 模型**（任意 R + 二极管 + 直流源的通用直流工作点用）：`iD = Is·(exp(vD/(n·Vt)) − 1)`，默认 `Is = 1e-12 A`、`n = 1`、`Vt = AnalysisParameters.ThermalVoltageUt`；必须做指数钳位（例如 `vD` 限制在 ±0.99 V 附近或对电流设上限），保证 Newton 不溢出；提供 `Current(vD)`、`Conductance(vD)`、`Region(vD)`。
- 单元自检要求：`Is`/`n`/钳位行为可被测试；反向漏电流量级合理；`Conductance` 与数值微分一致（相对误差 ≤ 1e-6）。

### 4.2 `DiodeCircuitStrategy`

构造函数：`public DiodeCircuitStrategy(string subtype)`；`TopologyType => CircuitTopologyType.DiodeCircuit`；`SubtypeName => _subtype`。
`public static string? DetectSubtype(ElectricalNetlist netlist)`（分类器调用；有二极管时必须返回非 null）。

支持子类型（固定字符串，测试会断言）：

| 子类型 | 电路族 | 必须输出的关键量（`SummaryText` 或步骤 `ResultValue` 里可解析） |
|---|---|---|
| `HalfWaveRectifier` | 半波整流（电阻负载，无滤波电容） | 峰值 Vm、输出平均值 Vavg、有效值 Vrms、二极管承受反向峰值 PIV、输出频率 |
| `HalfWaveRectifierFiltered` | 半波整流 + 电容滤波 | Vdc 近似、纹波 Vr、纹波系数、PIV |
| `BridgeRectifier` | 桥式全波整流 | Vavg、Vrms、每只二极管 PIV、输出频率 2f |
| `BridgeRectifierFiltered` | 桥式 + 电容滤波 | Vdc、Vr、纹波系数、PIV |
| `SeriesLimiter` | 串联电阻 + 二极管并联到地（上限幅/下限幅，含可选 DC 偏置） | 门限电平、输出最大值/最小值、传输特性分段 |
| `DoubleLimiter` | 双向限幅（两只二极管 + 正负偏置） | 上/下限幅电平、输出摆幅 |
| `Clamper` | 二极管钳位（串联电容 + 并联二极管） | 输出直流偏移、输出最大值/最小值、峰峰值保持 |
| `PeakDetector` | 峰值/包络检波（含 RC） | 峰值输出电压、纹波、时间常数判据 |
| `DiodeBias` | 任意 R + 二极管 + 直流源的静态工作点（Newton） | 二极管电流 ID、导通压降 VD、状态（导通/截止） |

实现要求：
- AC 源峰值取 `branch.ValueSI`（V），频率取 `element.FrequencyHz`；`Vm` 要扣除二极管压降（恒压降模型）。
- 输入拓扑不匹配检测子类型时，返回 `PedagogicalSolution.Failure`，消息须含"二极管"与具体不支持原因（不抛异常）。
- 每个成功解必须输出 ≥ 4 个推导步骤（`Title/Content/LatexFormula/SubstitutionFormula/ResultValue/Values`），使用 `ScientificNotationFormatter` 统一数字格式（与 BJT 策略一致）。
- 结果映射：`QPoint = new QPointInfo { Ibq="—", Icq="—", Vceq="—", WorkingRegion="二极管非线性电路", IsActiveRegion=false }`；`AcParameters` 留 null。关键指标写进 `SummaryText`（一行）与步骤 `ResultValue`。
- 数值容差：与教材手算相对误差 ≤ 1%（测试用）。

## 5. MOSFET 契约

### 5.1 `MosfetModel`（平方律 Level-1）

- 器件参数：`Vth = element.DisplayNumber`（PMOS 存负值，如 −1.5 V；NMOS 正值）；`K = element.MosfetK`（A/V²，默认 1 mA/V²）；`λ = element.MosfetLambda`（1/V，0 表示 `ro=∞`）。
- 区域：截止（`|Vov| ≤ 0`）、可变电阻区、饱和区。
  - NMOS：`Vov = VGS − Vth`，`ID = K·Vov²·(1+λ·VDS)`（饱和）；三极管区 `ID = K·(2·Vov·VDS − VDS²)·(1+λ·VDS)`。
  - PMOS：用镜像量 `Vov = Vth − VGS`（导通时为正）、`VSD = VS − VD`，电流幅值同式；策略负责极性映射与符号展示（|VDS|、VSD）。
- 输出：`Region(vov, vsd)`、`DrainCurrentMagnitude(...)`、`Transconductance`（饱和区 `gm = 2·K·Vov`）、`OutputConductance`（`gds = λ·K·Vov²`，λ=0 时 0 → ro=∞）。
- 耗尽型 MOS：模型可按同一平方律 + 负 Vth 支持，但本轮策略只对增强型给出完整教学解；非增强型明确拒绝并说明。
- JFET 明确拒绝（未实现 IDSS 参数化）。

### 5.2 `MosfetAmplifierStrategy`

构造函数：`public MosfetAmplifierStrategy(string subtype)`；`TopologyType => CircuitTopologyType.MosfetAmplifier`。
`public static string DetectSubtype(ElectricalNetlist netlist)`。

子类型（固定字符串）：

| 子类型 | 拓扑 | 求解输出 |
|---|---|---|
| `CommonSource` | 共源，源极直接接地，分压偏置 Rg1/Rg2，漏极 Rd，输入耦合 C1，输出耦合 C2 可带 RL | VG、VGS、ID、VDS、gm、Av=−gm·(Rd∥RL)、Ri=Rg1∥Rg2、Ro=Rd |
| `CommonSourceWithSourceResistance` | 共源 + 源极电阻 Rs（无旁路） | 解二次方程求 ID；Av=−gm·Rd/(1+gm·Rs)（忽略体效应）；Ri、Ro 同上 |
| `CommonSourceBypassed` | 共源 + Rs 且 Cs 旁路（直流有 Rs，交流短路） | DC 同上有 Rs；Av=−gm·(Rd∥RL) |
| `CommonDrain` | 共漏（源极跟随器，输出在源极，Rs 接地） | Av=gm·(Rs∥RL)/(1+gm·(Rs∥RL))、Ri=Rg1∥Rg2、Ro=Rs∥(1/gm) |
| `CommonGate` | 共栅（输入在源极，栅极交流接地，Rd 输出） | Av=+gm·(Rd∥RL)、Ri=1/gm（工程近似，可含 Rs）、Ro=Rd |

通用要求：
- 分压偏置电阻识别：`Rg1` 接 G–VDD、`Rg2` 接 G–GND（NMOS）；PMOS 镜像（G–VDD / G–GND 角色对调）。允许用单只 `Rg` 的简化偏置（若电路只有一只栅极电阻，则按对应子类型处理并在步骤注明）。
- 饱和区假设：若解出的 `VDS ≤ Vov`（NMOS，或 PMOS 的 VSD ≤ |Vov|）则明确失败（"工作点不在饱和区，当前教学模型不重求解"），不输出错误增益。
- 截止（`Vov ≤ 0`）明确失败。
- 结果映射：`QPoint = new QPointInfo { Ibq="—", Icq=ID 文本, Vceq=VDS 文本, WorkingRegion="饱和区 (Saturation)" 等 }`；`AcParameters` 填 `VoltageGain / InputResistance / OutputResistance`，`LowerCutoffFreq/UpperCutoffFreq/Bandwidth` 可填"未求解"或理论估算（须在步骤中标注"估算"）。
- 步骤 ≥ 5：偏置/栅极电位 → 平方律求 ID → VDS 与饱和区校验 → 小信号 gm（/gds、ro）→ 增益/输入输出电阻 → 可选低频估算。
- 数字格式统一用 `ScientificNotationFormatter`；`Values` 字典键名稳定（`ID`、`VGS`、`VDS`、`gm`、`Av`、`Ri`、`Ro`）。

## 6. BJT 共集电极契约（`BjtAmplifierStrategy` 增加 `CommonCollector`）

- 识别（分类器）：C 直接接 VCC 轨道；E 经 Re 有直流通路且非直接接地（输出）；B 有输入耦合电容或偏置网络。
- DC（NPN，PNP 镜像）：
  - 固定偏置：`IB=(VCC−VBE)/(Rb+(1+β)·Re)`；分压：`Vth=VCC·Rb2/(Rb1+Rb2)`、`Rth=Rb1∥Rb2`、`IB=(Vth−VBE)/(Rth+(1+β)·Re)`。
  - `IE=(1+β)IB`、`VCE=VCC−IE·Re`；`rbe=rbb'+(1+β)·VT/IE`（沿用现有 `rbb'` 约定：200 Ω 共射、50 Ω 共基）。
  - 工作区：`VCE≤0.2 V` 饱和失败；`IB≤0` 截止失败（与现有 BJT 策略一致）。
- 小信号（中频）：
  - `Re' = Re∥RL`（无负载 RL=∞ → `Re'=Re`）
  - `Av = (1+β)·Re' / (rbe + (1+β)·Re')`（正、略小于 1）
  - `Ri = Rb∥[rbe + (1+β)·Re']`（Rb 为偏置等效：固定偏置 = Rb；分压 = Rb1∥Rb2）
  - `Ro = Re∥(rbe+Rb)/(1+β)`
- 步骤 ≥ 5，`QPoint`/`AcParameters` 正常填写；`A` 与 CE 区分显示"射极跟随器（共集电极）"。
- 题目 B10 文件必须能分类为 `BjtAmplifier/CommonCollector` 并成功求解。

## 7. 多级放大：只预留接口（Lead 实现）

- `MultiStageContracts.cs` 定义：
  - `public interface IMultiStageAnalysis { bool TryPlan(ElectricalNetlist netlist, out MultiStagePlan plan, out string error); }`
  - `MultiStagePlan`（`Stages: List<MultiStageStage>`；`TopologyNote`）；`MultiStageStage`（`DeviceId`, `DeviceType`, `SubtypeHint`）。
- `MultiStageAmplifierStrategy`：`TopologyType => CircuitTopologyType.MultiStageAmplifier`，`SubtypeName => "Cascade"`；`SolveAsync` 返回 `PedagogicalSolution.Failure(...)`，消息必须包含 **"多级"**、**"后续开发规划"**、**"预留"**，并说明当前只支持单管分析。
- 分类器对 ≥2 晶体管一律路由到此策略，**不得**用第一只管子套用单管公式。
- 文档（verify-docs）把多级实现顺序写进后续规划：单管全类型 → 多级级联 → 二极管非线性扩展 → 混合管 → 大信号/失真。

## 8. 工程文件生成器契约

### 8.1 目录与工程

```
tools/CircuitModels.Core/           # 共享源码（非独立 csproj；被 4 个生成器项目以 Compile Include 链接）
  CircuitSpec.cs
  SchematicBuilder.cs
  CircuitFileWriter.cs
  Verification.cs
tools/CircuitModels.Diode/          # csproj + Definitions.cs + Program.cs，输出 D01–D07
tools/CircuitModels.BjtBias/        # csproj + Definitions.cs + Program.cs，输出 B01–B06
tools/CircuitModels.BjtAmp/         # csproj + Definitions.cs + Program.cs，输出 B07–B16
tools/CircuitModels.Mos/            # csproj + Definitions.cs + Program.cs，输出 M01–M06
```

每个 csproj 要点：
- `net9.0-windows10.0.26100.0`、`OutputType=Exe`、`ImplicitUsings/Nullable enable`、`NoWarn MVVMTK0045`、`PackageReference CommunityToolkit.Mvvm 8.4.0`。
- 链接 Models：`CircuitElement.cs`、`EngineeringUnit.cs`、`AnalysisStep.cs`、`AnalysisResult.cs`、`LinearAnalysisModels.cs`、`CircuitProjectData.cs`；
  链接 Services：`ScientificNotationFormatter.cs`；
  链接 Engine 全目录：`<Compile Include="..\..\apps\desktop-winui\Engine\**\*.cs" Link="Engine\%(RecursiveDir)%(Filename)%(Extension)" />`；
  链接 Core：`<Compile Include="..\CircuitModels.Core\**\*.cs" Link="Core\%(RecursiveDir)%(Filename)%(Extension)" />`。
- 运行：`dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj`（生成 + 自检）；`-- --verify`（只读校验磁盘文件）。

### 8.2 声明式 API（三个生成器共用，必须严格按此实现）

```csharp
namespace CircuitModels;

public enum NetRail { None, HorizontalTop, HorizontalBottom, VerticalLeft, VerticalRight }

public sealed class ElementSpec
{
    public string Id { get; init; } = "";                 // 无 '.'、无 ':'，如 "V1"、"Q1"
    public ElementType Type { get; init; }
    public string Name { get; init; } = "";               // 画布显示名（中文）
    public string DefinitionId { get; init; } = "";       // 如 "passive.resistor"（沿用模板命名）
    public string Category { get; init; } = "";           // 无源器件/激励电源/半导体管/参考地
    public string Description { get; init; } = "";
    public double Number { get; init; }                   // DisplayNumber
    public string Unit { get; init; } = "";
    public float X { get; init; }
    public float Y { get; init; }
    public int RotationDeg { get; init; }
    public bool MirrorX { get; init; }
    public float Width { get; init; } = 80;
    public float Height { get; init; } = 50;
    public SemiconductorMaterial Material { get; init; } = SemiconductorMaterial.Silicon;
    public double FrequencyHz { get; init; } = 1000;
    public double MosfetK { get; init; } = 1e-3;
    public double MosfetLambda { get; init; }
}

public sealed class NetSpec
{
    public string Name { get; init; } = "";               // 逻辑网名；"GND" 必须存在
    public string[] Pins { get; init; } = [];             // "V1.pos" / "R1.1" / "Q1.B" / "GND.GND"
    public NetRail Rail { get; init; } = NetRail.None;
    public float RailCoordinate { get; init; }            // 水平轨=Y，垂直轨=X
    public float Margin { get; init; } = 20;
}

public sealed record ExpectedOutcome(
    CircuitTopologyType Topology, string Subtype, bool SolveSucceeds);

public sealed class CircuitSpec
{
    public string Id { get; init; } = "";                 // "D01"
    public string Title { get; init; } = "";              // 工程名（中文）
    public string FileName { get; init; } = "";           // "D01-half-wave-rectifier.circuit"
    public string Description { get; init; } = "";        // 知识点/教材来源说明
    public List<ElementSpec> Elements { get; init; } = [];
    public List<NetSpec> Nets { get; init; } = [];
    public ExpectedOutcome? Expected { get; init; }
}

public static class SchematicBuilder
{
    // 1) 生成 CircuitElement/CircuitWire（含默认引脚、世界坐标、WireTap 分支）
    // 2) 立即执行结构自检：CircuitProjectData.Validate + ToModel + NetlistExtractor.Extract，
    //    断言“声明网表分区 == 提取网表分区”（一一对应、GND 唯一、无浮空引脚）
    // 3) 校验失败时抛出 InvalidOperationException，消息包含电路 Id 与失败引脚/网名
    public static CircuitProjectData Build(CircuitSpec spec);

    public static void Write(CircuitProjectData data, string outputDirectory);

    public static void Generate(IEnumerable<CircuitSpec> specs, string outputDirectory);
}

public static class Verification
{
    // 读取输出目录内本组产出的文件，执行：加载(与 ProjectManagerService 相同 JSON 选项)
    // -> Validate -> ToModel -> NetlistExtractor.Extract -> TopologyClassifier.Classify
    // -> 与 Expected 比对 Topology/Subtype；SolveSucceeds=true 时 strategy.SolveAsync 必须成功，
    //    false 时必须失败且 SummaryText 非空。
    // 输出 [PASS]/[FAIL] 每文件一行 + 汇总，返回失败数（Shell 退出码非 0 表示失败）。
    public static int Verify(string outputDirectory, IEnumerable<string> fileNames);
}
```

实现细节要求：
- JSON 序列化选项必须与 `Services/ProjectManagerService.cs` 一致：`WriteIndented=true`、`PropertyNameCaseInsensitive=true`、`Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping`、`new JsonStringEnumConverter()`；`Version` 保持 `"1.2.0"`（旧版软件仍能打开、忽略新增 MOS 字段）。
- 布局：坐标 10 px 网格对齐；元件旋转使用 `CircuitElement.GetPinWorldPosition`；水平电源轨 Y=80，GND 轨 Y=480 附近（可因电路规模调整）；导线只允许水平/垂直段；T 形分支必须用 `WireTap` 绑定父导线；跨线不得误连（以 NetlistExtractor 提取结果为准）。
- 输出目录：仓库根 `examples/circuit-models/`（相对当前工作目录；生成器内用 `Path.GetFullPath` 兜底）。
- 文件命名：`D01-half-wave-rectifier.circuit`、`B01-fixed-bias-common-emitter.circuit`、`M01-nmos-common-source.circuit` 等（见 §9 表）。

## 9. 29 个电路文件清单与预期

| 文件 | 内容 | 预期分类 | 求解 |
|---|---|---|---|
| `D01-half-wave-rectifier.circuit` | 半波整流，纯电阻负载 | DiodeCircuit/HalfWaveRectifier | ✅ |
| `D02-half-wave-rectifier-filtered.circuit` | 半波整流 + 电容滤波 | DiodeCircuit/HalfWaveRectifierFiltered | ✅ |
| `D03-bridge-rectifier.circuit` | 桥式全波整流 | DiodeCircuit/BridgeRectifier | ✅ |
| `D04-bridge-rectifier-filtered.circuit` | 桥式 + 电容滤波 | DiodeCircuit/BridgeRectifierFiltered | ✅ |
| `D05-series-limiter.circuit` | 串联限幅（单管） | DiodeCircuit/SeriesLimiter | ✅ |
| `D06-double-limiter.circuit` | 双向限幅（双管+双偏置） | DiodeCircuit/DoubleLimiter | ✅ |
| `D07-clamper.circuit` | 二极管钳位 | DiodeCircuit/Clamper | ✅ |
| `B01-fixed-bias-common-emitter.circuit` | 固定偏置共射 Q 点 | BjtAmplifier/FixedBias | ✅ |
| `B02-voltage-divider-bias.circuit` | 分压偏置共射 Q 点（无旁路） | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `B03-voltage-divider-bypassed.circuit` | 分压偏置共射 Q 点（带 Ce） | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `B04-collector-feedback-bias.circuit` | 集电极反馈偏置 Q 点 | BjtAmplifier/CollectorFeedbackBias | ✅ |
| `B05-common-base-bias.circuit` | 共基极直流偏置 | BjtAmplifier/CommonBase | ✅ |
| `B06-pnp-voltage-divider-bias.circuit` | PNP 分压偏置 Q 点 | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `B07-common-emitter-bypassed.circuit` | 共射放大（分压+Ce）Av/Ri/Ro | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `B08-common-emitter-unbypassed.circuit` | 共射放大（Re 不旁路） | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `B09-common-emitter-loaded.circuit` | 共射 + 输出耦合 RL | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `B10-common-collector.circuit` | 共集电极（射极跟随器） | BjtAmplifier/CommonCollector | ✅ |
| `B11-common-base-amplifier.circuit` | 共基极放大 | BjtAmplifier/CommonBase | ✅ |
| `B12-rc-coupled-two-stage.circuit` | 阻容耦合两级共射 | MultiStageAmplifier/Cascade | ❌（后续规划） |
| `B13-direct-coupled-two-stage.circuit` | 直接耦合两级共射 | MultiStageAmplifier/Cascade | ❌ |
| `B14-ce-cc-two-stage.circuit` | 共射–共集两级 | MultiStageAmplifier/Cascade | ❌ |
| `B15-low-frequency-response.circuit` | 耦合/旁路电容低频响应 | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `B16-high-frequency-response.circuit` | 共射高频响应（估算） | BjtAmplifier/VoltageDividerSelfBias | ✅ |
| `M01-nmos-common-source.circuit` | NMOS 共源分压偏置 | MosfetAmplifier/CommonSource | ✅ |
| `M02-nmos-common-source-rs.circuit` | NMOS 共源 + Rs | MosfetAmplifier/CommonSourceWithSourceResistance | ✅ |
| `M03-nmos-common-source-bypassed.circuit` | NMOS 共源 + Rs 旁路 | MosfetAmplifier/CommonSourceBypassed | ✅ |
| `M04-nmos-common-drain.circuit` | NMOS 共漏（源极跟随器） | MosfetAmplifier/CommonDrain | ✅ |
| `M05-pmos-common-source.circuit` | PMOS 共源 | MosfetAmplifier/CommonSource | ✅ |
| `M06-nmos-common-gate.circuit` | NMOS 共栅 | MosfetAmplifier/CommonGate | ✅ |

参数要求：
- 每个文件的元件数值必须是可解的教材参数（例如 BJT β=100、VCC=12 V、VBE=0.7 V；MOS Vth=1.5 V、K=1–2 mA/V²），**不许**出现截止/饱和/非物理参数。
- 交流信号源幅值取 mV 或 V 的合理值（如 10 mV / 5 V），频率 1 kHz（D 组按 50 Hz）。
- `Description` 写清教学知识点与教材来源（如"童诗白《模拟电子技术基础》第2章"），便于学生理解。
- B12–B14 虽然求解预期失败，但文件必须结构合法、可打开、可编辑，且元件参数合理。

## 10. 测试与验收

新增/更新测试：
- `tests/NonlinearCircuitChecks`（Lead 搭骨架；三个引擎 owner 各写一个检查文件）：
  - `DiodeChecks.cs`：9 类二极管场景（§4.2 表）的手算对拍（≤1%）、材料 Si/Ge 差异、失败路径。
  - `MosChecks.cs`：M01–M06 拓扑的手算对拍（ID/VGS/VDS/gm/Av/Ri/Ro ≤1%）、PMOS 极性、饱和区失败路径。
  - `BjtCcChecks.cs`：共集 Q 点/Av/Ri/Ro 手算对拍、与共射区分、失败路径。
  - `Program.cs` 汇总三个检查的 Passed/Failed，任一失败 exit 1。
- `tests/CircuitModelChecks`（verify-docs 或 Lead 后建）：读取 `examples/circuit-models/*.circuit`，逐文件加载→提取→分类→求解，断言 §9 的预期（29 个文件全在）。
- 既有 10 个控制台测试工程：更新其中"二极管/MOS 一律拒绝"的端到端断言（直接调用 MNA 的负向断言保持不变）；全部必须通过。

命令（Lead 最终执行）：
```
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj
dotnet run --project tools/CircuitModels.Bjt/CircuitModels.Bjt.csproj
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj
# 以及既有 10 个测试工程全量回归
```

## 11. 已知边界（必须写进文档/最终答复）

1. 二极管大信号波形分析采用**分段线性恒压降模型 + 解析公式**，不是 SPICE 瞬态仿真；不覆盖非教材拓扑。
2. MOSFET 为**平方律 Level-1 模型**，忽略体效应/亚阈值/迁移率退化；不覆盖非饱和区工作点（明确失败）。
3. 耗尽型 MOS 与 JFET 不承诺求解（明确拒绝）。
4. MOS 的 K/λ 已持久化到工程文件，但检查器本轮不提供编辑框；默认 K=1 mA/V²。
5. 多级放大仍未实现，仅预留接口并明确拒绝。
6. 波形分析的数值指标是教材近似公式（如滤波电容纹波），容差按 1% 对拍手算公式，不承诺与真实 SPICE 一致。
7. 自动布局只保证拓扑正确与可读，不做避障/美观最优。
