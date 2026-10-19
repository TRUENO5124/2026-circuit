# 教学电路模型工程文件（29 个原生 `.circuit`）

本目录保存 CircuitAssistant 原生 WinUI 客户端的 29 个教学电路工程文件，覆盖二极管大信号电路（D01–D07）、BJT 偏置与放大电路（B01–B16）和 MOSFET 放大电路（M01–M06）。文件为应用自有 `CircuitProjectData` JSON 格式（当前 `Version = "1.2.0"`），由 `tools/CircuitModels.*` 生成器按声明式电路规格生成并自检，不是 SPICE 网表，也不是 KiCad/Altium 等第三方 EDA 原理图。

> 第六轮（2026-10-18）新增；**第八轮（2026-10-18）更新了 B07 的教材变体语义与 D06 工程名；第九轮（2026-10-18）BJT 元件新增可编辑的 r_bb'/r_be(h_ie) 参数；2026-10-19 补全耗尽型 MOS 与 N/P 沟道 JFET 单管教学求解、MOS K/λ 与 JFET IDSS 可在检查器编辑，并完成元器件工作区物理核验修订、发布 0.2.0-preview.1**。另：`document/3-6题.circuit` 是用户提供的教材题3-12 PNP 分压偏置工程（不在这 29 个交付文件内），可用于验证本软件的 BJT 教材估算法显示口径（见下文）。生成器与校验命令见本文件末尾；能力边界以 [PROJECT.md](../../PROJECT.md)、[分析引擎报告](../../projects/analysis-engine-technical-report.md)、[耗尽 MOS/JFET 实现记录](../../projects/fet-depletion-jfet-strategies-20261019.md)、[工作区校验修订](../../projects/operating-region-validation-20261019.md)、[第九轮修订记录](../../projects/technical-code-fixes-20261018-round9.md)、[第八轮修订记录](../../projects/technical-code-fixes-20261018-round8.md)、[第七轮修订记录](../../projects/technical-code-fixes-20261018-round7.md) 与[第六轮修订记录](../../projects/technical-code-fixes-20261018-round6.md)为准。

## 打开方式

1. 启动原生客户端（`apps/desktop-winui/CircuitAssistant.csproj`，或已构建的 `start-winui.bat`）。
2. 菜单「工程管理 → 打开工程」或快捷键 `Ctrl+O`。
3. 选择本目录下任意 `.circuit` 文件；打开后可直接点「求解与理论分析」（`Ctrl+R`）查看推导步骤与指标。

Web/React 原型（`packages/editor`）不能直接读取 `.circuit` 文件；Web/Python 的 `CircuitGraph` JSON 只能通过「打开工程」单向导入为原生工程（见 `benchmarks/examples/`）。

## 文件索引

「预期分类」为当前分类契约（`TopologyClassifier`）给出的拓扑类型/子类型；「求解」表示打开后点击求解是否应得到成功结果。多级放大文件能打开、可编辑、可画，但求解会明确拒绝（见下节）。

### D 组：二极管非线性电路（7 个）

| 编号 | 文件 | 主要内容/知识点 | 预期分类 | 求解 |
|---|---|---|---|---|
| D01 | [D01-half-wave-rectifier.circuit](D01-half-wave-rectifier.circuit) | 半波整流（纯电阻负载）：峰值、平均值、有效值、反向峰值电压 PIV | `DiodeCircuit / HalfWaveRectifier` | 可解 |
| D02 | [D02-half-wave-rectifier-filtered.circuit](D02-half-wave-rectifier-filtered.circuit) | 半波整流 + 电容滤波：Vdc、纹波 Vr、纹波系数 | `DiodeCircuit / HalfWaveRectifierFiltered` | 可解 |
| D03 | [D03-bridge-rectifier.circuit](D03-bridge-rectifier.circuit) | 桥式全波整流：Vavg、Vrms、每只二极管 PIV、输出频率 2f | `DiodeCircuit / BridgeRectifier` | 可解 |
| D04 | [D04-bridge-rectifier-filtered.circuit](D04-bridge-rectifier-filtered.circuit) | 桥式整流 + 电容滤波：Vdc、纹波、PIV | `DiodeCircuit / BridgeRectifierFiltered` | 可解 |
| D05 | [D05-series-limiter.circuit](D05-series-limiter.circuit) | 串联限幅（上限幅）：门限电平、传输特性分段 | `DiodeCircuit / SeriesLimiter` | 可解 |
| D06 | [D06-double-limiter.circuit](D06-double-limiter.circuit) | 双向限幅（双二极管 + 正负偏置）：上/下限幅电平、输出摆幅 | `DiodeCircuit / DoubleLimiter` | 可解 |
| D07 | [D07-clamper.circuit](D07-clamper.circuit) | 二极管钳位：输出直流偏移、最大/最小值、峰峰值保持 | `DiodeCircuit / Clamper` | 可解 |

### B 组：BJT 偏置与放大电路（16 个）

| 编号 | 文件 | 主要内容/知识点 | 预期分类 | 求解 |
|---|---|---|---|---|
| B01 | [B01-fixed-bias-common-emitter.circuit](B01-fixed-bias-common-emitter.circuit) | 固定偏置共射直流工作点 Q（IBQ/ICQ/VCEQ） | `BjtAmplifier / FixedBias` | 可解 |
| B02 | [B02-voltage-divider-bias.circuit](B02-voltage-divider-bias.circuit) | 分压偏置共射 Q 点（发射极电阻不旁路） | `BjtAmplifier / VoltageDividerSelfBias` | 可解 |
| B03 | [B03-voltage-divider-bypassed.circuit](B03-voltage-divider-bypassed.circuit) | 分压偏置共射 Q 点（带发射极旁路电容 Ce） | `BjtAmplifier / VoltageDividerSelfBias` | 可解 |
| B04 | [B04-collector-feedback-bias.circuit](B04-collector-feedback-bias.circuit) | 集电极反馈偏置 Q 点 | `BjtAmplifier / CollectorFeedbackBias` | 可解 |
| B05 | [B05-common-base-bias.circuit](B05-common-base-bias.circuit) | 共基极直流偏置与工作点 | `BjtAmplifier / CommonBase` | 可解 |
| B06 | [B06-pnp-voltage-divider-bias.circuit](B06-pnp-voltage-divider-bias.circuit) | PNP 分压偏置 Q 点（NPN 的镜像极性） | `BjtAmplifier / VoltageDividerSelfBias` | 可解 |
| B07 | [B07-common-emitter-bypassed.circuit](B07-common-emitter-bypassed.circuit) | 共射放大教学变体（分压偏置 + Ce；**信号源内阻 R6=50 Ω + 47 μF 电解输入耦合 C3**）：Q 点、Av/Ri/Ro、Rs、Aus、fL（τ1 含 Rs） | `BjtAmplifier / VoltageDividerSelfBias` | 可解（DC 2 + AC 4 步） |
| B08 | [B08-common-emitter-unbypassed.circuit](B08-common-emitter-unbypassed.circuit) | 共射放大（Re 不旁路）：负反馈对 Av/Ri 的影响 | `BjtAmplifier / VoltageDividerSelfBias` | 可解 |
| B09 | [B09-common-emitter-loaded.circuit](B09-common-emitter-loaded.circuit) | 共射 + 输出耦合电容与负载 RL：负载效应 | `BjtAmplifier / VoltageDividerSelfBias` | 可解 |
| B10 | [B10-common-collector.circuit](B10-common-collector.circuit) | 共集电极（射极跟随器）：0<Av<1、高 Ri、低 Ro | `BjtAmplifier / CommonCollector` | 可解 |
| B11 | [B11-common-base-amplifier.circuit](B11-common-base-amplifier.circuit) | 共基极放大：同相电压增益、低输入电阻 | `BjtAmplifier / CommonBase` | 可解 |
| B12 | [B12-rc-coupled-two-stage.circuit](B12-rc-coupled-two-stage.circuit) | 阻容耦合两级共射（2 只 BJT） | `MultiStageAmplifier / Cascade` | 明确拒绝（后续规划） |
| B13 | [B13-direct-coupled-two-stage.circuit](B13-direct-coupled-two-stage.circuit) | 直接耦合两级共射（2 只 BJT） | `MultiStageAmplifier / Cascade` | 明确拒绝（后续规划） |
| B14 | [B14-ce-cc-two-stage.circuit](B14-ce-cc-two-stage.circuit) | 共射–共集两级放大（2 只 BJT） | `MultiStageAmplifier / Cascade` | 明确拒绝（后续规划） |
| B15 | [B15-low-frequency-response.circuit](B15-low-frequency-response.circuit) | 耦合/旁路电容决定的下限截止频率（低频响应） | `BjtAmplifier / VoltageDividerSelfBias` | 可解（fL 为教学估算） |
| B16 | [B16-high-frequency-response.circuit](B16-high-frequency-response.circuit) | 单管共射高频响应（高频参数与上限频率估算） | `BjtAmplifier / VoltageDividerSelfBias` | 可解（高频为模型估算） |

### M 组：MOSFET 放大电路（6 个）

| 编号 | 文件 | 主要内容/知识点 | 预期分类 | 求解 |
|---|---|---|---|---|
| M01 | [M01-nmos-common-source.circuit](M01-nmos-common-source.circuit) | NMOS 共源（分压偏置）：Q 点、gm、Av、Ri、Ro | `MosfetAmplifier / CommonSource` | 可解 |
| M02 | [M02-nmos-common-source-rs.circuit](M02-nmos-common-source-rs.circuit) | NMOS 共源 + 源极电阻 Rs（无旁路）：负反馈降低增益 | `MosfetAmplifier / CommonSourceWithSourceResistance` | 可解 |
| M03 | [M03-nmos-common-source-bypassed.circuit](M03-nmos-common-source-bypassed.circuit) | NMOS 共源 + Rs 与旁路电容 Cs：直流有 Rs、交流短路 | `MosfetAmplifier / CommonSourceBypassed` | 可解 |
| M04 | [M04-nmos-common-drain.circuit](M04-nmos-common-drain.circuit) | NMOS 共漏（源极跟随器）：Av<1、高 Ri、低 Ro | `MosfetAmplifier / CommonDrain` | 可解 |
| M05 | [M05-pmos-common-source.circuit](M05-pmos-common-source.circuit) | PMOS 共源（Vth 为负、VDD 与 GND 镜像） | `MosfetAmplifier / CommonSource` | 可解 |
| M06 | [M06-nmos-common-gate.circuit](M06-nmos-common-gate.circuit) | NMOS 共栅：输入在源极、栅极交流接地、同相增益 | `MosfetAmplifier / CommonGate` | 可解 |

## 能求解 / 仅可画 / 明确拒绝

- **可求解（26 个）**：D01–D07、B01–B11、B15、B16、M01–M06。点击「求解与理论分析」后应看到教学推导步骤、Q 点/小信号参数或二极管关键量。二极管电路使用分段线性恒压降模型 + 教材解析公式（不是 SPICE 瞬态仿真）；增强/耗尽 MOSFET 使用 Level-1 平方律模型、JFET 使用 Shockley 方程（本目录 M01–M06 全部为增强型 MOSFET 示例）；BJT 使用混合 π 教学模型；BJT/MOS/JFET 的显示值为教材估算法两位小数（见下节），后端精确值不显示。
- **仅可画、可编辑、可打开（3 个）**：B12–B14。它们包含两只晶体管，分类器会识别为 `MultiStageAmplifier / "Cascade"`，求解返回明确失败，失败信息说明多级/级联分析已列入后续开发规划、当前只预留接口，并提示拆分为单管后分别求解。应用不会用第一只晶体管套用单管公式伪造结果。
- **JFET 与耗尽型 MOSFET（2026-10-19 起已支持教学求解）**：增强型/耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 均可按单管五类拓扑求解（共源、共源+Rs、共源旁路、共漏、共栅）；增强/耗尽 MOS 用 Level-1 平方律，JFET 用 Shockley 方程 `ID=IDSS·(1−VGS/VGS(off))²`、`K=IDSS/VGS(off)²`；检查器可编辑 MOS 的 K/λ 与 JFET 的 IDSS。仍保留的限制：JFET 栅结正偏、IDSS 缺失/非正数明确失败，可变电阻（三极管）区只报告分区并停止 AC 小信号推导（不输出伪增益），且仍限单管。本目录 M01–M06 仍全部为增强型 MOSFET 示例。

## 参数说明

### 二极管（D 组）

- 使用器件材料决定正向压降：硅管 `VF ≈ 0.7 V`、锗管 `VF ≈ 0.3 V`（`CircuitElement.ForwardVoltageDrop`）。D 组文件默认硅管（0.7 V）；可把器件材料改为锗管观察输出变化。
- 交流源峰值取 `DisplayNumber`（D 组为 10 V，D07 钳位为 5 V 峰值），频率取 `FrequencyHz`（D 组 50 Hz 教材场景）。
- 负载/限流电阻取 1 kΩ；滤波电容 100 μF；D06 双向限幅的偏置为 +2 V/−2 V；D07 钳位负载 10 kΩ、耦合电容 10 μF。

### BJT（B 组）

- `DisplayNumber = β`（电流放大系数），默认 β = 100（B11 共基极取 β = 80 用于教学对比）。
- 硅管 `|VBE| ≈ 0.7 V`、锗管 `≈ 0.3 V`，由器件材料决定，不再硬编码。
- 偏置电源 VCC = 12 V（教材值）；B 组电阻为 51k/15k 分压（放大组）、100k/50k（偏置组）等 10 px 网格可读布局。
- B06 PNP 分压偏置为满足 PNP 放大区，Rb2 取 200 kΩ（而非 50 kΩ），已写入文件 `Description` 供教学核对；PNP 的 E 接 VCC、C 经 Rc 接 GND，电流方向与 NPN 相反。
- B10 共集电极：集电极直接接 VCC（无 Rc）、发射极经 Re = 2 kΩ 到地、输出经 C2 耦合到 10 kΩ 负载；这是分类器 `CommonCollector` 判定的教材拓扑。

### MOSFET/JFET（M 组，K/λ/IDSS）

- `DisplayNumber = Vth`（阈值电压）：NMOS 为 +1.5 V，PMOS（M05）存 −1.5 V。界面参数标签显示为 `Vth = … V`，不要误读为电源电压；耗尽型 MOS 与 JFET 的 `DisplayNumber` 则标注为夹断电压 `VGS(off)`（本目录 M01–M06 全部为增强型）。
- `MosfetK`（A/V²）：平方律参数 `K = ½·μ·Cox·(W/L)`，M01–M06 文件均写入 **2 mA/V²**（2e-3 A/V²），默认值 1 mA/V²。
- `MosfetLambda`（1/V）：沟道长度调制系数 λ，M 组文件均为 0（忽略沟道长度调制，`ro = ∞`）。
- 电源与偏置：VDD = 15 V（M05 PMOS 为 12 V）；M01 Rg1/Rg2 = 1000k/200k（VG = 2.5 V），M02/M03/M06 为 700k/300k，M04 为 1000k/1000k（VG = 7.5 V）；Rd = 3 kΩ、Rs = 1 kΩ（M04 Rs = 2.5 kΩ），输入 10 mV/1 kHz 交流源、耦合电容 10 μF、旁路电容 47 μF。
- K/λ 与 JFET 的 IDSS 均已作为元件字段持久化到 `CircuitProjectData` JSON（`MosfetK`/`MosfetLambda`/`JfetIdss`；旧版本软件读取时忽略新字段并保持兼容）；**检查器可直接编辑 MOS 的 K、λ 与 JFET 的 IDSS**，修改后随工程保存/读回。旧工程缺省 K=1 mA/V²、λ=0、JFET IDSS=0；JFET 求解前必须补填有限正数 IDSS（例如 8 mA 填 `0.008`），缺失/非正数会明确失败。
- 计算公式（饱和区）：增强/耗尽 MOS 用 `ID = K·Vov²·(1+λ·VDS)`、`gm = 2·K·Vov`、`gds = λ·K·Vov²`；N/P 沟道使用镜像量（`Vov = VGS − Vth` / `Vth − VGS`）。JFET 用 Shockley 方程 `ID = IDSS·(1−VGS/VGS(off))²`（换算 `K = IDSS/VGS(off)²`），栅结要求反偏或零偏，正偏停止推导。
- 2026-10-19 起耗尽型 MOSFET 与 N/P 沟道 JFET 也可教学求解（五类拓扑；JFET 用 Shockley 方程；K/λ/IDSS 可编辑并持久化）；仍限单管，且可变电阻（三极管）区只报告分区并停止 AC 推导。本目录 M01–M06 仍为增强型 MOSFET 示例。

## BJT/MOS 显示口径：教材估算法与两位小数（第七轮）

第七轮按用户口径把 BJT/MOSFET 的显示统一为**教材估算法**：分压偏置先按空载分压算 `U_B=VCC·R_b2/(R_b1+R_b2)`，再 `I_EQ=(U_B−V_BE)/R_E`、`I_BQ=I_EQ/(1+β)`、`I_CQ=β·I_BQ`、`U_CEQ`；每个中间结果按显示单位最多两位小数（AwayFromZero）舍入，并把舍入后的显示值代入下一步。公式本身不化简（`(1+β)` 保留、`I_CQ` 不近似为 `I_EQ`）。戴维南精确值只在后端保留（`QPointInfo.Exact*` 与步骤 `Values` 的 `*_exact_*` 键），界面不显示；原先第六轮的"戴维南精确 vs 教材估算"双口径对照已废弃（历史见[第六轮修订记录](../../projects/technical-code-fixes-20261018-round6.md)）。

`document/3-6题.circuit`（题3-12，PNP 分压偏置：VCC=12 V、RB1=15 kΩ、RB2=470 kΩ、RE=1.3 kΩ、RC=2 kΩ、锗管 VEB=0.3 V、β=100）的显示链与后端精确值：

| 量 | 界面显示（教材估算法，两位小数） | 后端精确值（不显示） |
|---|---|---|
| U_B | 11.63 V | Vth=11.6289 V；实际基极电位 ≈11.636 V |
| I_EQ | 53.85 μA | —（`IEQ_exact_uA` 保留） |
| I_BQ | 0.53 μA | 0.487767 μA |
| I_CQ | 53 μA | 0.0487767 mA |
| U_ECQ | 11.82 V | 11.8384 V |

实现要点：

- 第 1 步（DC 等效电路 Q 点）方法名为教材估算法；教学步骤为 5 步（DC 1 + AC 4），推导页按 DC/AC 两组展示。
- Q 点面板显示教材估算链值（如上表），摘要与第 1 步 `Values` 一致；不再出现"戴维南/精确解/11.1%"等双口径文案。
- 第 1 步 `Values` 的显示键（`IBQ/ICQ/IEQ/VCEQ/UB` 等）保存舍入后回代值；后端诊断键为 `IBQ_exact_uA/ICQ_exact_mA/UCE_exact_V/IEQ_exact_uA/Vth_V/Rth_ohm/UB_actual_V`。
- 若教材估算法把工作点算进饱和区（"软分压器"），求解明确失败并提示"饱和区"，不输出 Q 点/AC 参数/步骤，也不把后端精确解包装成成功结果。
- 全部教学题型与报告显示值最多两位小数（`tests/DisplayRoundingChecks` 55/55 验证）。

需要强调：显示值遵循教材手算口径（含舍入回代），与后端精确值可能有可见差异，这是教材工程近似与显示约定，不是软件算错。

## B07 教材变体：信号源内阻 Rs + 电解输入耦合电容（第八轮）

B07 文件现在是**用户手工布局的教学变体**：`vs → R6=50 Ω（信号源内阻 Rs） → C3=47 μF 电解输入耦合电容（正极接基极） → 基极`，参数 VCC=12 V、Rb1=33 kΩ、Rb2=10 kΩ、Rc=1 kΩ、Re=1 kΩ、RL=1 kΩ、Ce=47 μF、C2=10 μF、β=100。第八轮引擎已支持该拓扑，求解不再报"未覆盖电阻支路"：

- 步骤：**6 步 = 5 教学步 + 1 个 C3 电解极性核验步**，DC 2 步（Q 点 + 极性核验）+ AC 4 步，推导页按 DC/AC 两组展示。
- 教材估算法显示链（两位小数）：`U_B=2.79 V → I_EQ=2.09 mA → I_BQ=20.69 μA → I_CQ=2.07 mA → U_CEQ=7.84 V`；`rbe=1.46 kΩ、Av=−34.25、Ri=1.23 kΩ、Ro=1 kΩ`。
- 信号源内阻：`Rs=50 Ω`、`Aus=Av·Ri/(Rs+Ri)=−32.91`；实时指标卡与报告导出显示"信号源内阻 Rs""源电压增益 Aus"两行（无 Rs 的电路不显示）。
- 频率：`fL≈41.7 Hz`（`τ1=(Rs+Ri)·C1=1/[2π(50+1.23k)·47μF]→2.65 Hz`，与 `fl2=7.96 Hz`、`fl_e=40.85 Hz` 合成）；`fH≈1.76 MHz` 为模型估算。
- 后端戴维南精确值仍保留不显示（`IBQ≈19.24 μA、ICQ≈1.92 mA、U_CE≈8.13 V`）。
- 生成器 `tools/CircuitModels.BjtAmp/Definitions.cs` 的 B07 定义已同步为该变体语义（新增本地 `PolarizedCapacitor` helper），但生成器**未运行写盘模式**，保留用户手工布局；`--verify` 只读校验 10/10。

> **工程名保存修复（第八轮）**：旧版 `SaveProjectCoreAsync` 每次保存都把 `ProjectName` 改写为文件名，导致 B07/D06 等教材示例被改名、`CircuitModels.* --verify` 工程名校验失败。现仅"未命名电路工程"回退文件名，其余保留工程标题；D06 工作区 `ProjectName` 已恢复为"双向限幅电路（双管双偏置）"（B07 为"分压式偏置共射放大电路（Ce 旁路）"）。

## BJT 小信号参数：r_bb'/r_be(h_ie) 可调（第九轮）

第九轮起 BJT 元件新增两个可编辑、可持久化的教学参数（默认 0=自动，因此本目录 29 个文件**默认数值不变**）：

- **基区体电阻 r_bb'**（`BjtRbb`，Ω，0=自动）：0 时共射/共集取 200 Ω、共基取 50 Ω（教材默认）；填正数则按题目给定值参与 `r_be = r_bb' + (1+β)U_T/I_EQ`。
- **给定 r_be/h_ie**（`BjtRbe`，Ω，0=按 Q 点计算）：0 时按上式由 Q 点计算；填正数则**题目给定值直接采用**参与 Av/Ri/Ro/fL（不再二次舍入），步骤 `Values` 的 `rbe_source` 为 `given`。
- 编辑入口：右侧"元件参数检查器"（选中 BJT 时）以及"参数设置 → 三极管模型与信号源特性"Group 3；两处宏参数与元件双向同步，非法输入自动归零。
- 打开本目录任意 BJT 文件（如 B01/B07）后在检查器把 rbb′ 改为 300 Ω 或给定 rbe=1500 Ω 再求解，即可看到 rbe 步骤文案/公式与 Av/Ri 随之变化；默认不改则与上一轮完全一致。
- 29 个教学文件的 `tests/CircuitModelChecks` 校验仍为 **259/259**（默认参数不回归）；生成器 `--verify` 7/7、6/6、10/10、6/6。**第九轮按用户要求不做 ui-capture 窗口验收**（仅控制台回归 + 构建）。

## 单文件诊断（`--file`）

`tests/CircuitModelChecks` 支持直接检查任意原生 `.circuit` 文件（不要求属于 29 文件清单）：

```powershell
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "document/3-6题.circuit"
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "examples/circuit-models/B07-common-emitter-bypassed.circuit"
```

输出：工程信息、元件/导线数、网表校验、分类/子类型、求解结果、摘要、Q 点、交流参数（含 Rs/Aus，若识别到信号源内阻），以及全部推导步骤（内容/公式/代入/结果/结构化 Values）。用于快速排查"某个工程为什么这样求解"；B07 变体可直接验证 Rs/电解 C3/Aus。

## 教学用途

- 文件内每个元件的 `Description` 写明知识点，工程 `Description` 给出教材来源（如童诗白《模拟电子技术基础》相关章节），便于课堂演示与课后对照。
- 建议教学顺序：先 D 组观察二极管的大信号整流/限幅/钳位，再 B 组由偏置 Q 点到小信号指标（B02/B06 用于观察教材估算链的舍入回代；Q 点显示为教材估算法两位小数，后端精确值不显示），最后 M 组用平方律/Shockley 方程对比 BJT 与 MOSFET/JFET 的放大特性；B12–B14 用于说明"多级级联尚未实现、只预留接口"的边界。
- 所有数值都是可解教材参数，不是真实器件模型卡；结果用于教学推导，不应作为工业设计依据。

## 生成与校验

文件由以下生成器按声明式电路规格生成（每个生成器默认生成并自检，`-- --verify` 只读校验磁盘文件）：

```powershell
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj
dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
```

`tests/CircuitModelChecks` 会逐文件执行「反序列化 → Validate → ToModel → 提取网表 → 分类 → 求解」，并比对 29 个文件的预期分类与求解结果；文件数量不足、分类/子类型不符或可解文件求解失败都会报 `[FAIL]`。

第九轮（2026-10-18）生成器只读校验结果：Diode 7/7、BjtBias 6/6、BjtAmp 10/10（B07 新变体定义只读校验）、Mos 6/6；`tests/CircuitModelChecks` **Total=259 Passed=259 Failed=0**（29 个文件全部通过分类/子类型/求解/步骤数断言；默认 `BjtRbb`/`BjtRbe`=0 不回归）。**发布前实测（2026-10-19，0.2.0-preview.1）**：`tests/NonlinearCircuitChecks` **Total=1606 Passed=1606 Failed=0**（第九轮当轮为 913/913：原 822 + 第九轮 91 条 `BjtSmallSignalParameterChecks`；2026-10-19 新增耗尽型/JFET 与工作区校验检查）、`tests/M2SolverChallenger` **166/166**（第九轮当轮为 160/160：原 156 + 4 条 rbb/rbe 宏同步/默认自动/修改失效/保存读回；另含 Suite 7 反射 Win2D 公式管线与保存保留工程名）、公式排版 `tests/MathFormulaChecks` **411/411**、显示两位小数 `tests/DisplayRoundingChecks` **55/55**；全量控制台矩阵 **20 组命令 / 4905 项断言全部通过**，Release x64 自包含发布 **528 个运行文件**、构建 0 警告/0 错误、发布目录启动冒烟通过。这些数字只代表上述自动化断言通过，不代表真实窗口交互或外部 SPICE 数值一致性已验收；**第九轮与 2026-10-19 发布验证均未做新的 ui-capture 窗口验收**。

## 已知边界

1. 二极管波形电路使用**分段线性恒压降模型 + 解析公式**，不是 SPICE 瞬态仿真，也不覆盖非教材拓扑；纹波等指标是教材近似公式（容差按 1% 与手算公式对拍），不承诺与真实 SPICE 一致。
2. MOSFET 为**平方律 Level-1 模型**、JFET 为 **Shockley 方程**，忽略体效应/亚阈值/迁移率退化；工作点不满足饱和区假设（含可变电阻/三极管区）时只报告分区并停止 AC 小信号推导，不输出伪增益；JFET 栅结正偏明确失败。
3. BJT 共集电极已支持（B10）；多级放大仍未实现，仅预留 `IMultiStageAnalysis` 接口并明确拒绝（B12–B14）。
4. MOS 的 K/λ 与 JFET 的 IDSS 均已持久化到工程文件，并可在检查器/参数设置直接编辑；默认 K = 1 mA/V²、λ = 0，JFET 求解前须补填有限正数 IDSS。
5. 自动布局只保证拓扑正确与可读，不做避障/美观最优；文件坐标按 10 px 网格与水平/垂直导线生成。
6. 这些文件是原生 `.circuit` JSON，不能直接用于 ngspice 等外部仿真器；应用另提供有限范围的 SPICE 文本导出，但不含完整模型卡与仿真调用。
7. **显示口径**：Q 点/AC 指标显示教材估算法两位小数（显示值回代），后端戴维南精确值保留但不显示；软分压器若被估算法算进饱和区会明确失败，不输出伪结果。显示值与后端精确值可能有可见差异，属教材工程近似与显示约定，不是软件算错。
8. **信号源内阻 Rs（第八轮）**：只识别"交流源与输入耦合电容之间恰一只电阻"的教学拓扑（B07 变体）；串联多只电阻/更复杂源网络会明确失败并给出元件/网名诊断；MOSFET/JFET 放大器暂不支持 Rs/Aus。
9. **电解电容**：按 `+/-` 引脚识别，极性核验归 DC 相；反接仍求解但给 Warning 步骤与摘要告警。
10. **保存命名**：保存工程保留 `ProjectName`（仅"未命名电路工程"回退文件名）；B07/D06 的工程标题不再被保存改写。
11. **BJT 小信号参数（第九轮）**：`BjtRbb`/`BjtRbe` 默认 0=自动，因此本目录文件默认数值不变；题目给定 r_be(h_ie) 原值直接采用、不二次舍入（与 computed 显示值回代语义不同，属有意设计）；r_bb'/r_be 是每只 BJT 元件参数；旧工程缺省 0；UI 非法输入自动归零，后端非法值明确失败。
12. **无窗口验收**：第九轮与 2026-10-19 发布验证均只做控制台回归 + 构建（发布另含 Release 自包含发布与启动冒烟，0 警告/0 错误）；r_bb'/r_be、MOS K/λ 与 JFET IDSS 编辑框的真实窗口外观/交互未验收。
