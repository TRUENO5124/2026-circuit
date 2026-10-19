# CircuitAssistant 教学分析界面：改动前 / 改动后 窗口级视觉验证记录

> 维护者：ui-capture（Team Lead "lead" 的同事）
> 验证对象：`apps/desktop-winui/CircuitAssistant.exe`（未打包 WinUI 3 桌面应用）
> 证据目录：本文件所在目录（`tools/ui-verify/evidence/`），全部截图为真实窗口内容，
> 由 `GetWindowRect` + `PrintWindow(flags=2 / PW_RENDERFULLCONTENT)` 采集，非离屏渲染或拼图。

---

## 1. 环境与采集方法

| 项目 | 值 |
| --- | --- |
| 系统缩放 | 150%（DPI = 144） |
| 窗口物理尺寸 | **1920 × 1118** 物理像素（= 逻辑 1280 × 745，由 `SetWindowPos` 固定到屏幕 (100,100)，before/after 完全相同） |
| 采集方式 | PowerShell 5.1 + user32 `GetWindowRect` / `PrintWindow(2)` + System.Drawing 保存 PNG |
| 交互方式 | `SetForegroundWindow`(+AttachThreadInput/topmost 兜底) + `SetCursorPos` + `SendInput` 鼠标点击/滚轮；`System.Windows.Forms.SendKeys` / 剪贴板粘贴；UI Automation 定位按钮与 Tab 真实坐标 |
| 基线 exe | `bin\Debug\...\win-x64\CircuitAssistant.exe`（LastWriteTime **2026-10-18 04:59:41**） |
| 修复后 exe | `bin\_verify\CircuitAssistant.exe`（LastWriteTime **2026-10-18 17:23:28**，卡片头布局修复后构建） |
| 状态同步 | 每次关键动作后用 UIA 轮询状态文本 / 元素出现，再截图；窗口尺寸每次截图前校验，发现被最大化/最小化会自动复原 |

采集脚本：`tools/ui-verify/Capture-Ui.ps1`（底层能力）、`tools/ui-verify/Run-CaUiFlow.ps1`（整条流程 + 截图命名 + 裁剪 + JSONL 日志）。

---

## 2. 证据清单

### 2.1 `evidence/before/`（旧 exe，改动前基线）

| 文件 | 说明 |
| --- | --- |
| `01_welcome.png` | 启动后的空白工作区欢迎页 |
| `02_template_loaded.png` | 载入“分压式共射放大电路”教学模板后的画布 |
| `03_ce_derivation_full.png` | 求解后自动切到“解题推导”页（整页，滚动到顶） |
| `04_ce_derivation_panel.png` | 右侧推导面板裁图（旧版无 DC/AC 分组标题） |
| `04b_ce_formula_view_full.png` | 推导页滚动到第 1 步公式框时的整页截图 |
| `05_ce_formula_region.png` | **旧版公式框裁图：出现字面下划线 `_`（如 `V_th=V_CCR_b2 / (R_b1+R_b2)`）、长公式右侧被裁切、字号偏小** |
| `06_ce_metrics_full.png` | “实时指标”标签页整页（Q 点 + 交流指标） |
| `07_ce_qpoint_region.png` | 旧版 Q 点面板裁图（显示为后端精确值：IBQ=18.006 μA、ICQ=1.801 mA、VCEQ=4.78 V 等） |
| `07b_ce_derivation_drawer.png` | 底部“解题推导抽屉”展开时整页（旧公式控件） |
| `08_pnp_loaded.png` | 打开 `document/3-6题.circuit`（PNP 分压偏置；为绕开中文路径/输入法，用内容相同的 ASCII 副本打开） |
| `09_pnp_derivation_full.png` | PNP 求解后推导整页（旧版：无 DC/AC 分组，公式为 `_` 下划线单行文本） |
| `10_pnp_derivation_panel.png` | PNP 右面板裁图 |
| `10b_pnp_formula_view_full.png` | PNP 第 1 步公式框整页 |
| `11_pnp_formula_region.png` | PNP 公式区裁图（旧版：下划线角标，与 after-fixed 对照） |
| `12_pnp_metrics_full.png` | PNP “实时指标”整页（旧版为戴维南精确值口径：IBQ≈0.49 μA 等） |
| `13_pnp_qpoint_region.png` | PNP Q 点面板裁图（旧版精确值） |
| `14_mos_loaded.png` | 打开 M01 NMOS 共源教学模板（旧 exe 可以载入画布） |
| `15_mos_solve_failed.png` | **旧 exe 求解 MOS 被明确拒绝**：“无法完成求解：当前尚未实现 MOSFET/JFET 非线性求解，请使用已支持的教学电路。”（本轮新功能，before 无 MOS 推导页属预期） |

### 2.2 `evidence/after-fixed/`（17:23:28 修复构建，改动后）

| 文件 | 说明 |
| --- | --- |
| `01_welcome.png` / `02_template_loaded.png` | 欢迎页 / 教学模板载入（与 before 对照） |
| `03_ce_derivation_full.png` | CE 求解后“解题推导”整页：**DC 分组标题 + 两行式卡片头 + 真角标/叠放分式** |
| `04_ce_derivation_panel.png` | 右侧推导面板裁图（含“直流等效电路（DC 分析）”分组标题与说明） |
| `04b_ce_formula_view_full.png` | 滚动到第 1 步公式框的整页 |
| `05_ce_formula_region.png` | **第 1 步公式框裁图：真上下角标、分式上下叠放、自动换行无横向裁切、字号明显变大** |
| `06_ce_metrics_full.png` | CE “实时指标”整页：IBQ=20.1 μA、ICQ=2.01 mA、VCEQ=3.94 V、Av=-155.03、Ri=1.32 kΩ、Ro=3 kΩ、fL=31.92 Hz、fH=456.52 kHz、BW=456.49 kHz（均 ≤2 位小数） |
| `07_ce_qpoint_region.png` | CE Q 点面板裁图 |
| `07b_ce_derivation_drawer.png` | 底部“解题推导抽屉”整页（新公式控件，题3-12 教材公式排版正常） |
| `08_pnp_loaded.png` | 打开 `document/3-6题.circuit`（PNP 分压偏置，经 ASCII 副本 `t312.circuit` 打开，内容相同） |
| `09_pnp_derivation_full.png` | PNP 求解后推导整页：DC 分组标题 + 第 1 步（教材估算法） |
| `10_pnp_derivation_panel.png` | PNP 右侧推导面板裁图 |
| `10b_pnp_formula_view_full.png` | PNP 第 1 步公式 + 数值代入整页：U_B=11.63 V、I_EQ=53.85 μA … |
| `11_pnp_formula_region.png` | PNP 第 1 步公式框裁图：`U_B=V_CC·R_b2/(R_b1+R_b2)`、`I_EQ=(V_CC−U_B−V_EB)/R_E`、`I_BQ=I_EQ/(1+β)`、`I_CQ=βI_BQ`、`U_ECQ=V_CC−I_CQ·R_C−I_EQ·R_E` |
| `12_pnp_metrics_full.png` | PNP “实时指标”整页：**IBQ=0.53 μA、ICQ=53 μA、VCEQ=11.82 V**（PNP 方向说明 V_EC=+11.82 V）、Av=-1.11、Ri=13.45 kΩ、Ro=2 kΩ；无“戴维南/教材估算对照/11.1%”旧双口径文案 |
| `13_pnp_qpoint_region.png` | PNP Q 点面板裁图 |
| `14_mos_loaded.png` | 打开 `examples/circuit-models/M01-nmos-common-source.circuit`（NMOS 共源模板） |
| `15_mos_derivation_full.png` | MOS 求解后推导整页（7 步；DC/AC 分组） |
| `16_mos_derivation_panel.png` | MOS 推导面板裁图 |
| `16b_mos_formula_view_full.png` | MOS 第 1 步公式框整页 |
| `17_mos_formula_region.png` | MOS 公式区裁图（真角标 / 分式 / 平方律公式） |
| `18_mos_metrics_full.png` | MOS “实时指标”整页 |
| `19_mos_qpoint_region.png` | MOS Q 点面板裁图 |
| `capture-summary.json` / `flow-log.jsonl` | 每张截图的阶段、窗口几何、点击坐标、UIA 定位记录、时间戳 |

### 2.3 `evidence/after/`（首次 after 截图，**作为卡片头布局 bug 证据保留，不覆盖**）

| 文件 | 说明 |
| --- | --- |
| `03_ce_derivation_full.png` | 首次 after 截图：旧卡片头把“方法名”放第三列 Auto，方法名过长将标题列挤成 0 宽（标题逐字竖排、卡片头异常高），步骤正文被顶出可视区 |
| `04_ce_derivation_panel.png` / `05_ce_formula_region.png` | 裁图受该布局 bug 影响（公式区未正常进入视野） |
| 其余 `01/02/06/07/07b` | 同批次截图，作为 bug 现场留存 |

### 2.4 `evidence/debug/`（开发/失败现场，非验收图）
- 文件对话框自动化调试截图（`dlg-*.png`、导航/粘贴/卡住现场）、旧版探针截图等。

---

## 3. 逐项对照结论（before → after-fixed）

| 验收项 | before（旧构建） | after-fixed（17:23:28 构建） | 结论 |
| --- | --- | --- | --- |
| ① 公式角标 | 字面 `_` 下划线原样显示（`V_th=V_CCR_b2 / (R_b1+R_b2)`），部分 Unicode 下标混排 | 真上下角标（Win2D 数学排版，`Math/` 解析器 0.72×），无任何 `_` | ✅ 通过 |
| ② 分式/根号 | 单行文本 `/` 表示除号 | 分式上下叠放、带水平分数线；√、矩阵/`cases` 环境按实现支持 | ✅ 通过（样本内分式均正确） |
| ③ 横向裁切 | 长公式右侧被裁（`R_th=` 后内容不可见，无横向滚动提示） | 按可用宽度自动换行，未见横向裁切 | ✅ 通过 |
| ④ 字号 | 13.5px，偏小 | 基础字号 16（+1.19×），观感明显变大 | ✅ 通过 |
| ⑤ DC/AC 分组 | 无分组标题，Steps 平铺 | “直流等效电路（DC 分析）”“交流等效电路（AC 分析）”分组标题出现；第 1 步归 DC，第 2 步起归 AC（CE/PNP/MOS 三例一致） | ✅ 通过 |
| ⑥ 步骤卡片头 | 单行式：方法名在第三列 Auto，长方法名把标题列挤为 0 宽（卡片头异常高、正文被顶出）；已单独留存 bug 证据 | 两行式：第一行“步骤号 + 标题”，第二行小字方法名（最多两行省略）；三例均正常 | ✅ 通过 |
| ⑦ 结果数值 | 后端精确值（IBQ=18.006 μA、VCEQ=4.78 V 等） | CE：IBQ=20.1 μA、ICQ=2.01 mA、VCEQ=3.94 V、Av=-155.03、Ri=1.32 kΩ、Ro=3 kΩ、fL=31.92 Hz、fH=456.52 kHz、BW=456.49 kHz；PNP：0.53 μA / 53 μA / 11.82 V；其余值均 ≤2 位小数 | ✅ 通过 |
| ⑧ 题3-12 教材估算法口径 | 旧逻辑为戴维南精确值（IBQ≈… 与教材估算法不一致） | Q 点与第 1 步显示教材估算法链：U_B=11.63 V → I_EQ=53.85 μA → I_BQ=0.53 μA → I_CQ=53 μA → U_ECQ=11.82 V；未出现“戴维南精确值/教材估算对照/11.1%”旧双口径文案 | ✅ 通过 |
| ⑨ 中文/CJK、豆腐块 | 中文正常；公式内文字为普通字体 | 中文正常，Win2D 排版 CJK 回退正常，未见豆腐块/乱码 | ✅ 通过 |
| ⑩ 布局/行距 | 步骤卡片头被挤压（bug） | 卡片头两行正常、正文/两个公式框/结果条正常显示；无元素重叠或明显错位；抽屉页排版正常 | ✅ 通过 |
| ⑪ 深浅色主题 | 未切换 | 未切换（应用无主题切换入口，仅跟随系统；未做强制切换验证） | ⚠️ 未覆盖 |

---

## 4. 未覆盖项 / 已知限制

1. **MOS 的 before 推导页无法采集（非采集失败）**：旧 exe（04:59 构建）尚未实现 MOSFET 非线性求解，点击求解会被明确拒绝（见 `before/15_mos_solve_failed.png`），因此 before 只有 MOS 载入截图；after-fixed 的 MOS 推导页（7 步、DC/AC 分组）为本轮新增能力。CE / PNP 的 before↔after 均已完整对照。PNP 的 before 采集过程中曾因旧 exe 的文件对话框自动化多次失败（失败现场在 `debug/moved-*`），最终使用同一套“剪贴板粘贴绝对路径 + UIA 点击打开”稳定路径成功采集。
2. **未做逐像素自动 diff**：before/after 窗口几何完全一致（1920×1118 @ (100,100)），截图可直接叠放比较，但结论基于人工读图，未写像素差异阈值脚本。
3. **主题切换 / 其它 DPI（100%、200%）未验证**：本机为 150% 单显示器；应用无浅/深色手动切换入口，未强制切换系统主题后重拍。
4. **交互断言有限**：流程以“UIA 状态文本变化 + 截图存在/尺寸校验”为通过判据，未对每个控件做严格的自动化断言（如内容文本与截图 OCR 比对）。
5. 采集期间窗口曾被外部最大化/最小化（非本脚本操作），流程已做几何自愈与截图尺寸校验；若后续复跑看到中间 `(window geometry … -> reset)` 日志属正常自愈。
6. PickerHost 文件对话框的“输入路径→打开”非常依赖焦点/输入法状态：本机中文输入法（搜狗）会吞掉 SendKeys ASCII 字符，最终采用“点文件名框 + 剪贴板粘贴绝对路径 + UIA 定位点击打开(O)”的稳定路径，已写入脚本。

---

## 5. 脚本清单与用法

| 文件 | 说明 |
| --- | --- |
| `tools/ui-verify/Capture-Ui.ps1` | 可复用底层库：启动/关闭应用、等待窗口、固定窗口几何、`PrintWindow` 截图（重试+全黑检测）、`SetForegroundWindow`、`SendInput` 鼠标点击/双击/滚轮、`SendKeys`/剪贴板文本、UIA 元素/Tab 定位、外部窗口（PickerHost 对话框）定位与点击、截图裁剪、JSONL 日志。可 dot-source 复用，也可 `-Action` 直接 CLI 调用。 |
| `tools/ui-verify/Run-CaUiFlow.ps1` | 整条证据采集流程（参数化 before/after + evidence 子目录）：启动旧/新 exe → 固定几何 → CE 教学模板 → 求解 → 推导页 → 实时指标 → 打开 PNP 工程 → 求解 → 指标 → 打开 MOS 模板 → 求解 → 指标；自动裁图并写 `capture-summary.json` + `flow-log.jsonl`。 |
| `tools/ui-verify/_debug/*.ps1` | 开发期调试脚本（Tab/TabItem UIA 结构、PickerHost 对话框控件与粘贴/点击路径验证、置前与截图探针）——**仅开发用**，可由本记录复现对话框自动化方案。 |

常用命令（Windows PowerShell 5.1，需 `-ExecutionPolicy Bypass`）：

```powershell
cd D:\BJTU\2026年大创

# 改动前（旧 exe）
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-verify\Run-CaUiFlow.ps1 -Phase before -IncludeDrawer

# 改动后（bin\_verify 新构建；evidence 子目录可用 -EvidencePhase 区分，避免覆盖旧证据）
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-verify\Run-CaUiFlow.ps1 -Phase after -EvidencePhase after-fixed -IncludeDrawer

# 单独重拍某一段（示例：只拍 PNP）
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-verify\Run-CaUiFlow.ps1 -Phase after -EvidencePhase after-fixed -SkipCe -SkipMos -PnpProjectPath 'D:\BJTU\2026年大创\document\3-6题.circuit'

# 单步操作示例
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-verify\Capture-Ui.ps1 -Action Start
powershell -NoProfile -ExecutionPolicy Bypass -File .\tools\ui-verify\Capture-Ui.ps1 -Action Capture -Phase before -Name 01_welcome
```

> 注意：`Run-CaUiFlow.ps1` 默认 `-Phase before` 使用 `bin\Debug` 旧 exe，`-Phase after` 使用 `bin\_verify` 新 exe；`-EvidencePhase` 只改变截图输出子目录（默认与 Phase 同名），便于保留历史证据。

---

## 6. 临时文件清理说明（已完成）

- ASCII 路径测试副本 `C:\dsh_pnp\t312.circuit` 已在 Lead 清理工作区时移除；本机其它临时副本
  `document\t312.circuit`、`D:\WZH\Documents\t312.circuit`、`C:\Users\WZH\Documents\t312.circuit` 已由本脚本删除。
  `document\3-6题.circuit` 原件保留未动。
- `evidence/` 下 `before/`（18 张）、`after/`（卡片头 bug 现场，8 张）、`after-fixed/`（23 张）、
  `debug/`（文件对话框自动化调试/失败现场）、`capture-summary.json`、`flow-log.jsonl` 均为交付证据，**保留不删**。
- `_debug/` 调试脚本（Tab/TabItem UIA 结构、PickerHost 对话框控件与粘贴/点击路径验证等）保留在
  `tools/ui-verify/_debug/`，便于复现“中文路径 + 输入法”场景下的对话框自动化方案；如不需要可整体删除，
  不影响 `Capture-Ui.ps1` / `Run-CaUiFlow.ps1` 的独立运行。
- 所有 `CircuitAssistant.exe` / `PickerHost.exe` 残留进程已停止，未修改应用源码、测试源码或构建产物。
