# Round 8 增量验收：B07（分压式共射 + 信号源内阻 Rs + 电解耦合电容 C3）

> 目标：验证用户在 B07 基础上加“信号源内阻 R6=50 Ω + 电解耦合电容 C3=47 μF”后的电路能正常求解，
> 并验证 AC 指标卡中新增的“信号源内阻 Rs / 源电压增益 Aus”两行（布局修复后不重叠）。
> 采集脚本：`tools/ui-verify/Verify-Round8.ps1`、`tools/ui-verify/Verify-Round8Metrics.ps1`（底层库 `Capture-Ui.ps1`）。
> 环境：150% DPI，窗口固定 1920×1118 物理像素 @ (100,100)，`PrintWindow(flags=2)` 真实窗口截图。

## 1. 被测构建

| 用途 | exe | LastWriteTime |
| --- | --- | --- |
| 主验证（推导页 / 分组 / 公式 / 首次指标） | `apps/desktop-winui/bin/_verify/CircuitAssistant.exe` | 2026-10-18 18:19:27 |
| AC 卡布局修复后补拍（指标页） | 同上 | 2026-10-18 **18:30:46** |

输入电路：`C:\dsh_b07\b07.circuit`（= `examples/circuit-models/B07-common-emitter-bypassed.circuit` 最新版，
工程名“分压式偏置共射放大电路（Ce 旁路）”，13 元件 / 23 导线；含 vs、R6=50 Ω、C3=47 μF 电解电容、Ce 旁路等）。

## 2. 证据清单（按验收点）

### 2.1 求解成功 / 推导页分组（18:19:27 构建）

| 文件 | 说明 |
| --- | --- |
| `01_b07_derivation_top_full.png` | 推导页顶部整页（DC 分组标题“直流等效电路（DC 分析）”+ 步骤 1/2） |
| `02_b07_derivation_top_panel.png` | 右侧推导面板裁图（DC 分组区域） |
| `03_b07_step2_electrolytic_full.png` | **第 2 步“电解电容 C3 直流偏置与功能特性核验”**：正极 2.64 V / 负极 0 V，`U_cap=V(+)−V(−)≥0`，(Safe Forward Bias)，`U_C3 = 2.64 V − 0 V = 2.64 V ≥ 0 V`，结果条“极性正常（耐压裕量 ΔV = 2.64 V）”；顶部 Q 点结果条同时可见 |
| `04_b07_step2_electrolytic_region.png` | 第 2 步核验卡片裁图 |
| `04b_b07_qpoint_estimate_full.png` | **DC 第 1 步教材估算法数值链**：`U_B = 12 V×10 kΩ/(33 kΩ+10 kΩ) = 2.79 V`、`I_EQ = (2.79−0.7)/1 kΩ = 2.09 mA`、`I_BQ = 2.09 mA/(1+100) = 20.69 μA`、`I_CQ = 100×20.69 μA = 2.07 mA`、`U_CEQ = 12 V−2.07 mA×1 kΩ−2.09 mA×1 kΩ = 7.84 V`，结果条同值 |
| `05b_b07_ac_group_full.png` | **“交流等效电路（AC 分析）”分组标题** + 分组说明 + 第 3 步（rbe）起点（证明 DC 组 2 步之后进入 AC 组） |
| `07c_b07_av_formula_region_full.png` | 第 4 步“中频段交流电压放大倍数 Av 推导与求解”整页（标题 + 正文 + 公式框起点） |
| `07b_b07_av_step_full.png` | **第 4 步公式 + 代入 + 结果整页**（见下 08b） |
| `08b_b07_av_formula_region.png` | **第 4 步公式区裁图（验收关键）**：<br>· 符号公式：`A_v = −β(R_C‖R_L)/r_be`、`A_us = A_v·R_i/(R_s+R_i)`；<br>· 数值代入：`A_v = −100×(1 kΩ‖1 kΩ)/1.46 kΩ = −34.25`、`A_us = −34.25×1.23 kΩ/(50 Ω+1.23 kΩ) = −32.91`；<br>· 结果条：**`Av = -34.25，Aus = -32.91（Rs=50 Ω）`** |

### 2.2 实时指标（修复前 → 修复后）

| 文件 | 说明 |
| --- | --- |
| `11_b07_metrics_full.png` / `12_b07_qpoint_region.png` / `13_b07_ac_metrics_region.png` | 18:19:27 构建：Q 点正确（20.69 μA/2.07 mA/7.84 V），但 **AC 卡 Rs/Aus 两行重叠**（Grid 缺一行 RowDefinition，Row=7 落到最后一行）——已归档到 `../round8-buggy-ac-card/` 作为 bug 证据 |
| `11b_b07_metrics_fixed.png` | **18:30:46 修复构建**：实时指标整页（B07） |
| `12b_b07_qpoint_fixed.png` | **Q 点面板**：`IBQ=20.69 μA`、`ICQ=2.07 mA`、`VCEQ=7.84 V`，放大区 (Active Region) |
| `13b_b07_ac_metrics_fixed.png` | **AC 指标卡（修复后）**：Av=−34.25、Ri=1.23 kΩ、Ro=1 kΩ、fL=41.7 Hz、fH=1.76 MHz（模型估算）、BW=1.76 MHz，最后两行 **“信号源内阻 Rs: 50 Ω”“源电压增益 Aus: -32.91” 行距正常不重叠**（UIA 实测两行 y=939/978，间距 39 px） |
| `14_normal_template_metrics_full.png` / `14b_normal_metrics_fixed.png` | **对照**：普通“分压式共射放大电路”模板（无 Rs）的实时指标；修复后构建的 `14b` 中 AC 卡只有 Av/Ri/Ro/fL/fH/BW，**不出现 Rs/Aus 两行**（UIA 断言匹配数=0） |

> `round8-buggy-ac-card/`：修复前构建的整套 round8 截图（含 AC 卡两行重叠 bug 现场），保留作 bug 证据。

## 3. 逐项结论（全部通过）

| 验收点 | 结果 | 证据 |
| --- | --- | --- |
| 求解成功、无报错 | ✅ “求解完成 · 晶体管共射极放大电路 · 6 个步骤”；“存在教学模型未覆盖的电阻支路”匹配数=0（18:19:27 与 18:30:46 两个构建各验证一次） | `01`、`round8b-summary.json` |
| DC 分组（2 步） | ✅ “直流等效电路（DC 分析）”标题；第 1 步 Q 点（教材估算法链 2.79 V→2.09 mA→20.69 μA→2.07 mA→7.84 V），第 2 步电解电容 C3 核验（2.64 V 正偏正常） | `01/02/03/04/04b` |
| AC 分组（4 步） | ✅ “交流等效电路（AC 分析）”标题；第 3 步 rbe=1.46 kΩ，第 4 步 Av/Aus，后续 2 步（Ri/Ro、频率特性）| `05b/07c/07b/08b` |
| 第 4 步公式 | ✅ 含 `A_us = A_v·R_i/(R_s+R_i)`，分式叠放/真角标 | `08b` |
| 第 4 步结果 | ✅ `Av = -34.25，Aus = -32.91（Rs=50 Ω）`（UIA 实测 -34.25/-32.91 元素存在） | `08b`、`13b` |
| Q 点数值 | ✅ IBQ=20.69 μA、ICQ=2.07 mA、VCEQ=7.84 V | `12b`、UIA 断言 |
| AC 卡新增两行 | ✅ “信号源内阻 Rs: 50 Ω”“源电压增益 Aus: -32.91”，两行不重叠（行距 39 px） | `13b`、round8b-summary |
| 普通模板不出现两行 | ✅ 无 Rs 模板：两行匹配数=0 | `14b` |
| 旧“未覆盖电阻支路”错误 | ✅ 不再出现（18:19:27/18:30:46 构建各确认） | 断言输出 |

## 4. 说明

1. 验证过程中一度出现“存在教学模型未覆盖的电阻支路”的误报，排查为**调试脚本自身 bug**（脚本变量 `$exePath` 被 dot-source 的 `Capture-Ui.ps1` 同名参数覆盖 → 实际启动的是无 Rs 支持的旧 Debug exe），**不是应用问题**；修正脚本后同一新构建连续多次求解均成功。
2. AC 卡布局 bug 由 `13_b07_ac_metrics_region.png` 发现（Rs/Aus 两行重叠），Lead 修复后本目录 `13b` 复验正常；旧图归档在 `../round8-buggy-ac-card/`。
3. 本目录其余被移出的失败/过程截图统一在 `../debug/round8-*`（调试用途，非验收证据）。
4. 未覆盖：逐像素自动 diff、深浅主题切换、其它 DPI 缩放。
