# 元器件工作区校验与无发射极电阻修复（2026-10-19）

适用对象为 WinUI 原生 C# 引擎。保留每个中间量按工程单位最多两位小数舍入、再代入下一步的教学方式；物理工作区校验独立使用未舍入值。本轮没有修改 React/Python 原型，也没有实现任意非线性网络求解。

## 问题与根因

用户截图为共射极 NPN：VCC=12 V、Rb1=33 kΩ、Rb2=10 kΩ、Rc=1 kΩ、β=50，发射极电阻删除后发射极直接接地；输入仍有 50 Ω 源内阻和 47 μF 电解耦合电容，输出经 10 μF 电容接 1 kΩ 负载。

旧策略允许发射极直接接地，但分压偏置仍用 `IE=(UB−VBE)/Re`，Re=0 时产生无穷大及 NaN。后续将 `!IsFinite(...)` 与 `IB<=0` 合并为“截止区”，把公式适用性错误解释成器件物理状态。

按硅管 VBE=0.7 V，直流电容开路，基极回路 KCL 为：

```text
IB=(12−0.7)/33000−0.7/10000 = 272.424… μA
IC(active trial)=50·IB = 13.6212… mA
VCE(active trial)=12−IC·1000 = −1.6212… V
```

这些是放大区假设下的试算值。负管压降说明假设失效，不能把它当作实际 Q 点；该电路应报告饱和，而不是截止。原生回归重建了图中直流偏置、输入 Rs/电解电容与输出耦合/负载支路，并覆盖 PNP 镜像。

## BJT 方程与判断顺序

1. 电源、β、电阻及方程结果需满足参数/有限数校验。数值溢出、NaN、无穷大不能推断为截止。
2. 分压偏置先建立含基极负载的未舍入解：`Uth=VCC·Rb2/(Rb1+Rb2)`、`Rth=Rb1||Rb2`，NPN `IB=(Uth−VBE)/(Rth+(1+β)Re)`；PNP 用 `VCC−Uth−VEB` 为分子。Re=0 时方程仍成立。
3. 固定偏置、集电极反馈、共基极、共集电极沿各自实际 DC 回路计算未舍入候选。共集电极 VCE 不含 Rc 项。
4. 若正向驱动不足，报告截止。若 BE 导通而 BC 已正向偏置，停止放大区推导。NPN 要求 VC≥VB，即 VCE≥VBE；PNP 使用镜像电压幅值。边界仅用 1e-10 量级容差抵消浮点误差，不能仅凭 VCE>0.2 V 认定放大区。
5. 截止与饱和提示不提供放大区 AC 指标。饱和消息明确标注“放大区试算值，不是饱和工作点”，不再声称已求得饱和 Q 点。
6. 普通有效教材估算链保持原有显示值。如果 Re=0，或空载分压估算越界而加载解仍有效，切换含负载方程的教学链：Uth/Rth 舍入后求 IB，再求 IE、IC、VCE；推导与摘要明确说明切换原因。

教学舍入可以使极小正基极电流显示为 0 μA，或使临界管压降略跨显示边界。只要未舍入物理解满足条件，就不能据此宣称截止；若舍入使必要的 IE 或管压降非正、使后续公式不可用，则明确提示教学精度/计算链不足。

软分压器回归例：VCC=15 V、Rb1=120 kΩ、Rb2=60 kΩ、Rc=3 kΩ、Re=1 kΩ、β=80。旧空载估算 UCE≈−2.05 V；加载解 IB=4.3/121k A、UCE=3.59256… V，实际仍在放大区。新教学链显示 IB=35.54 μA、IE=2.88 mA、IC=2.84 mA、UCE=3.6 V，不再把教材估算失败当作真实饱和。

## 延伸至 MOS/JFET 与二极管

MOS/JFET 仍保留原有平方律/Shockley 转移关系的教学舍入链，新增独立 DC 物理核验：

```text
Vov = gateDrive − ID·Rs
|VDS| = VDD − ID·(Rd+Rs)
ID = I_device(Vov, |VDS|)
```

在 `0≤ID≤VDD/(Rd+Rs)` 内用二分法求器件分段电流与负载线的交点。器件电流包括饱和平方律、可变电阻区关系及现有 λ 因子；N/P 沟道以镜像量处理。工作区使用实际分压、未舍入电流/电压；JFET 栅结正偏仍明确拒绝。`|VDS|=Vov` 的边界统一归入饱和区，匹配器件模型。物理核验值保存为步骤 Values 中的 `ID_physical_A`、`VGS_physical_V`、`VDS_physical_V`、`Vov_physical_V`，不作为第二套 UI 教学结果并列显示。

例如正过驱动 1 mV 被舍入为 0 V 时，提示“教学舍入精度不足”，不再声称器件实际截止。若物理解已位于三极管区，则报告分段模型/负载线的工作点并停止当前饱和区 AC 推导。

`MosfetRegion`、`DiodeRegion` 新增 `Invalid`，均追加在枚举尾部，保留已有枚举值。非有限端电压不再映射成 Cutoff/Off/On；MOS 电流计算遇无效工作区返回 NaN；二极管分段电流遇非有限电压或非法电阻也返回 NaN，不伪造零电流。二极管现有恒压降与 Shockley 两种模型及其适用范围保留。

## 代码与验证

主要实现：

- `apps/desktop-winui/Engine/Strategies/BjtAmplifierStrategy.cs`：偏置方程、共射/共基/共集统一工作区校验、加载方程教学推导。
- `apps/desktop-winui/Engine/Strategies/MosfetAmplifierStrategy.cs`：独立分段模型负载线核验、教学舍入诊断。
- `apps/desktop-winui/Engine/Nonlinear/MosfetModel.cs`、`DiodeModel.cs`：无效输入与物理状态分离。
- `tests/NonlinearCircuitChecks/OperatingRegionChecks.cs`：截图网络及镜像、放大/饱和/截止、集电结正偏、MOS 边界/舍入/负载线、二极管/FET 非有限值。
- `tests/NonlinearCircuitChecks/BjtDcAcPhaseChecks.cs`：将旧“软分压器必须报饱和”断言修订为包含基极负载后的独立手算验证。

新增第一组回归在修复前实际出现 13 项失败，包含截图截止误判与二极管/FET 无效数值误分类。修复后继续加入边界及负载线检查；既有 M2 的临界固定偏置与微电流回归用于保护浮点边界和教学舍入兼容性。

最终验证结果见本节末尾的实测记录。控制台回归和独立手算只证明对应电路与模型范围；本轮未操作运行中的窗口、未重新打包发布，也未进行实物验证。

## 保留的边界

- BJT 使用教材定压降与固定 β 的分区校验，不是 Ebers–Moll、Gummel–Poon 或完整 SPICE 器件模型。饱和区 Q 点尚未重求解。
- MOS/JFET 可在现有分段模型内核验/报告非饱和 DC 点，但三极管区 AC 放大推导仍未实现；未加入亚阈值、体效应、击穿、热反馈或器件离散性。
- 普通 BJT 教材估算与加载物理解仍可能有数值差异；本轮保留教学口径，并以加载解阻止错误工作区结论，不宣称所有显示值都是精确器件值。
- 现有单管、单电源、已识别支路的拓扑限制保留。多管、混合非线性网络及 Python 原型属于另一个范围。

## 实测验证结果（2026-10-19）

| 验证项 | 结果 |
|---|---:|
| NonlinearCircuitChecks | 1581/1581 |
| M2SolverChallenger | 160/160 |
| CircuitModelChecks（29 个教学文件） | 259/259 |
| NativeBenchmark30 | 30/30 |
| EngineV2Checks | 306/306 |
| EngineAuditChecks | 1359/1359 |
| DisplayRoundingChecks | 55/55 |
| MathFormulaChecks | 411/411 |
| ReportExportChecks | 110/110 |
| WebImportChecks | 172/172 |
| SymbolChecks | 230/230 |
| ChallengerRunner | 131/131 |
| M3AdversarialStress / M3_4EmpiricalStress | 32/32、9/9 |
| WinUI 主程序构建 | 0 警告、0 错误 |

运行方式为 `dotnet run --project tests/<名称>/<名称>.csproj`（控制台回归，不是 dotnet test）。构建命令为：

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=D:\BJTU\2026年大创\apps\desktop-winui\bin\_region_verify\
```

本次使用独立输出目录，未终止或重启用户正在运行的程序。当前窗口不会自动加载修改后的引擎。验证可执行文件为 `apps/desktop-winui/bin/_region_verify/CircuitAssistant.exe`，这仍是包含既有未提交工作的工作区构建，不是发布包。
