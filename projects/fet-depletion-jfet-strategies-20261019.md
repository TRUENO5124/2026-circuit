# 耗尽型 MOS 与 JFET 教学策略补全（2026-10-19）

本次修改针对当前 WinUI 客户端的原生分析引擎。Python 服务中的 `mos_dc.py` / `mos_ac.py` 是另一套策略，本次未修改。

## 原因与原有范围

`nonlinear-engine-and-circuit-models-design-20261018.md` 第 1、5 节将完整教学求解限定为增强型 MOS：耗尽型模型可以识别，但策略主动拒绝；JFET 未实现 IDSS 参数化。原来的回归测试也明确要求拒绝这两类器件。这是上一轮实现范围的选择，不是平方律或策略架构无法扩展。

## 本次支持

增强型/耗尽型 NMOS、PMOS，以及 N/P 沟道 JFET，复用五种单管放大拓扑：共源、带源极电阻的共源、源极电阻旁路的共源、共漏、共栅。通用 `Jfet` 符号按 N 沟道处理。

| 器件 | DisplayNumber（V） | 第二器件参数 |
|---|---|---|
| N/P 增强 MOS | Vth 正/负 | MosfetK，A/V² |
| N/P 耗尽 MOS | VGS(off) 负/正 | MosfetK，A/V²；若题目给 IDSS，可先算 K=IDSS/VGS(off)² |
| N/P JFET | VGS(off) 负/正 | JfetIdss，A；必须填写有限正数 |

检查器新增 MOS 的 K、JFET 的 IDSS，以及 λ 输入框。例：IDSS=8 mA 时填 `0.008`。JFET 旧文件没有 IDSS 时保留 0，仍可打开编辑；点击求解会提示补参，不采用 MOS 默认 K。IDSS 随器件克隆及工程 JSON 保存、加载。

## 方程与物理条件

MOS 的 K 定义是 `ID=K·Vov²`，不再额外乘 1/2。

- N 沟道：`Vov=VGS−Vth`；P 沟道：`Vov=Vth−VGS`，电流取幅值。
- JFET：`ID=IDSS·(1−VGS/VGS(off))²`，因此 `K=IDSS/VGS(off)²`。
- 耗尽型 MOS 在 VGS=0 时导通，允许向增强方向偏置。
- JFET 采用 IG≈0 的前提是栅结反偏或零偏。N 沟道 VGS>0、P 沟道 VGS<0 时停止推导；校验使用舍入前的电流，避免把微小正偏隐藏为 0 V。随后通过正向漏源电压和饱和区判据检查漏端偏置。
- 带 Rs 时平方律方程有两个代数根，取 Vov>0 的物理解。λ=0 时使用数值稳定的较小根表达式；λ>0 时求解包含漏源电压反馈的电流方程。
- 开启 λ 时，电流包含 `(1+λ·|VDS|)`；饱和区跨导也包含同一因子，`gm=2K·Vov·(1+λ·|VDS|)`。

JFET 转移特性的参数依据：[onsemi AN-6609](https://www.onsemi.com/download/application-notes/pdf/an-6609.pdf)；器件结构及极性参考：[Vishay AN101](https://www.vishay.com/docs/70594/70594.pdf)。这里使用教材理想模型，不拟合实际器件的参数离散。

## 独立手算验证

统一采用 VDD=12 V、|VGS(off)|=4 V、K=0.5 mA/V²（等价 IDSS=8 mA）、Rd=500 Ω、Rg=1 MΩ。N 沟道 Rg 接地，P 沟道镜像接 VDD。有 Rs 的电路取 Rs=1 kΩ。

带 Rs 时，以 mA 为单位，`x=0.5(4−x)²`，代数根为 2 和 8；8 mA 对应负过驱动，必须排除。因此 ID=2 mA、|VGS|=2 V、Vov=2 V、gm=2 mS。

| 拓扑 | ID | VDS 幅值 | gm | Av | Ri | Ro |
|---|---|---|---|---|---|---|
| 共源，无 Rs | 8 mA | 8 V | 4 mS | −2 | 1 MΩ | 500 Ω |
| 共源，Rs 不旁路 | 2 mA | 9 V | 2 mS | −0.33 | 1 MΩ | 500 Ω |
| 共源，Rs 旁路 | 2 mA | 9 V | 2 mS | −1 | 1 MΩ | 500 Ω |
| 共漏 | 2 mA | 10 V | 2 mS | +0.67 | 1 MΩ | 333.33 Ω |
| 共栅 | 2 mA | 9 V | 2 mS | +1 | 333.33 Ω | 500 Ω |

上述五类逐一覆盖 N/P 耗尽 MOS、N/P JFET、通用 JFET，并通过分类器调用实际策略。还验证 IDSS 缺失/负值/NaN/无穷、夹断电压符号、JFET 正偏拒绝与耗尽 MOS 增强方向偏置、λ 电流方程及跨导、克隆与工程 JSON 往返。

## 延续的求解范围

沿用现有教学策略的单一正电源、单管及支持的偏置网络。截止或可变电阻区的放大工作点会报告失败并停止输出小信号增益；未新增这些区域的非线性重求解。增益和端口电阻沿用教材忽略 ro 的中频近似，步骤中注明；低频截止频率仍为估算，高频寄生、击穿、体效应和多管电路不在本次范围内。显示链继续遵循工程单位两位小数舍入后回代的现有约定。

## 验证命令

最终验证结果：非线性回归 1534/1534，引擎回归 306/306，显示舍入检查 55/55；WinUI x64 Debug 构建 0 警告、0 错误。未进行桌面交互或发布包验证。

```powershell
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
dotnet build apps/desktop-winui/CircuitAssistant.csproj -p:Platform=x64 --no-restore
```
