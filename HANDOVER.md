# CircuitAssistant 开发交接

> 更新：2026-10-19（能力增量：耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 单管教学求解（共源、共源+Rs、共源旁路、共漏、共栅），JFET 用 Shockley 方程 ID=IDSS·(1−VGS/VGS(off))²、增强/耗尽 MOS 用 Level-1 平方律，MOS 的 K/λ 与 JFET 的 IDSS 检查器可编辑并随工程持久化；元器件工作区物理核验修订：无 Re 用含基极负载的偏置方程、BE/BC 结条件统一校验、MOS/JFET 分段模型 + 直流负载线、二极管/FET 非有限值归 Invalid，教学显示仍按教材估算法两位小数回代；阶段版本 0.2.0-preview.1 已发布，发布前全量控制台回归 20 组 / 4905 项断言全绿、构建 0 警告/0 错误、启动冒烟通过。第九轮：BJT 小信号参数 r_bb'/r_be(h_ie) 可编辑并持久化、检查器与参数设置两处编辑框、引擎读取实际参数；第八轮：BJT 信号源内阻 Rs/电解输入耦合电容/Aus、B07 教学变体、保存保留工程名；第七轮：BJT/MOS 教材估算法显示与两位小数、DC/AC 等效电路分组、Win2D 自绘公式框；第六轮：二极管/MOSFET 非线性教学求解、BJT 共集电极、多级预留接口与 29 个电路模型文件）。对象：接手当前 WinUI 代码的开发者。基线包含未提交改动；已按授权修改本体源码和文档，并执行隔离构建与自动化回归。最新验证结果与剩余边界见[工作区校验修订](projects/operating-region-validation-20261019.md)、[耗尽 MOS/JFET 实现记录](projects/fet-depletion-jfet-strategies-20261019.md)与[第九轮本体修订记录](projects/technical-code-fixes-20261018-round9.md)；0.2.0-preview.1 的发布校验与已知限制见发布目录 `RELEASE-NOTES.md`。**2026-10-19 发布验证为控制台回归 + 构建 + 启动冒烟，未做新的 ui-capture 窗口验收；第九轮按用户要求不做 ui-capture。**

## 1. 先确认运行对象

主项目是 `apps/desktop-winui/CircuitAssistant.csproj`。启动顺序为 `App.OnLaunched → MainWindow → AnalysisStudioPage`；不要在 `packages/editor` 中修改代码来修复原生窗口问题。后者是仍保留的 React 原型，`start-all.bat` 也只启动该原型与 Python API。

当前配置为 .NET 9、WinUI 3、Win2D 1.4.0、非打包桌面运行。主项目的其余 NuGet 引用使用浮动版本，不能根据旧文档假设固定 WindowsAppSDK/MVVM 版本。最低 Windows 平台声明是 Build 17763，兼容性仍需目标设备实测。

## 2. 构建与启动

```powershell
# 根目录首次准备
dotnet restore apps/desktop-winui/CircuitAssistant.csproj
# 修改源码后构建（若用户程序正在运行并锁定 bin/Debug，用隔离输出目录）
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
# 运行主客户端
dotnet run --project apps/desktop-winui/CircuitAssistant.csproj
```

`./start-winui.bat` 发现 `bin/Debug/net9.0-windows10.0.26100.0/win-x64/CircuitAssistant.exe` 就直接启动，只有找不到文件才调用 `dotnet run`；它不是每次重新构建的一键脚本。

若构建报告 exe 被占用，先保存用户工程并正常关闭已启动客户端，再重试；只有确认进程属于本次调试且无未保存数据时才结束它。不要把之前某次成功构建当成本轮验证。

## 3. 代码入口与维护位置

| 要改的行为 | 位置（相对 `apps/desktop-winui/`） |
|---|---|
| 主窗口材质与标题栏 | `MainWindow.xaml/.cs` |
| 三栏布局、菜单、结果卡片、报告导出子菜单 | `Views/AnalysisStudioPage.xaml` |
| 页面事件、快捷键与画布事件桥接 | `Views/AnalysisStudioPage.xaml.cs` |
| 工程命令、筛选、选中、撤销历史、求解/MNA 回退、报告命令、保存语义（保留 ProjectName） | `ViewModels/AnalysisStudioViewModel.cs` |
| 元件渲染、布线、框选、拖动、缩放 | `Controls/CircuitCanvas.xaml/.cs` |
| 左栏矢量预览 | `Controls/CircuitSymbolPreview.xaml/.cs` |
| 引脚、旋转、镜像、路径、端点引用 | `Models/CircuitElement.cs` |
| 半导体材料（硅/锗）与参数语义（β/Vth/VGS(off)/VF） | `Models/CircuitElement.cs` |
| BJT 小信号参数（`BjtRbb` r_bb'、`BjtRbe` 给定 r_be/h_ie，0=自动；非法编辑归零） | `Models/CircuitElement.cs` |
| BJT 小信号参数持久化（DTO `BjtRbb`/`BjtRbe`、负数校验、旧工程缺省 0） | `Models/CircuitProjectData.cs` |
| BJT 小信号参数 UI 与宏参数（检查器 + 参数设置两处编辑框、`RbbNumber`/`RbeNumber` 双向同步） | `ViewModels/AnalysisStudioViewModel.cs`、`Views/AnalysisStudioPage.xaml` |
| FET 参数（`MosfetK`/`MosfetLambda`/`JfetIdss`）与检查器编辑框（K/λ 对 MOS，IDSS 对 JFET） | `Models/CircuitElement.cs`、`Views/AnalysisStudioPage.xaml` |
| FET 参数持久化（DTO `MosfetK`/`MosfetLambda`/`JfetIdss`、有限性校验、旧工程缺省） | `Models/CircuitProjectData.cs` |
| 标准元件符号矢量几何（画布与符号卡片共用） | `Models/SymbolGeometry.cs` |
| 工程 JSON 字段及还原（当前 1.2.0） | `Models/CircuitProjectData.cs` |
| 线性网络结构化结果契约 | `Models/LinearAnalysisModels.cs` |
| 文件选择器、保存/加载、报告保存 | `Services/ProjectManagerService.cs` |
| 报告四格式渲染（LaTeX/Circuitikz、DOCX、Markdown、HTML） | `Services/ReportExportService.cs` |
| Web/Python CircuitGraph 单向导入 | `Services/WebCircuitGraphImporter.cs` |
| 四种教学模板及空白工程 | `Services/CircuitTemplateFactory.cs` |
| 电气连通、网表、策略 | `Engine/` |
| 线性网络分析 | `Engine/Linear/LinearNetworkAnalyzer.cs` |
| 二极管器件模型（恒压降 + Shockley/Newton） | `Engine/Nonlinear/DiodeModel.cs` |
| 二极管教学策略（9 类子类型） | `Engine/Strategies/DiodeCircuitStrategy.cs` |
| MOSFET/JFET 器件模型（MOS Level-1 平方律、JFET Shockley 方程、K/λ/IDSS） | `Engine/Nonlinear/MosfetModel.cs` |
| MOSFET/JFET 教学策略（增强/耗尽 MOS 与 N/P JFET：共源/共源+Rs/共源旁路/共漏/共栅） | `Engine/Strategies/MosfetAmplifierStrategy.cs` |
| 多级放大预留接口与拒绝策略 | `Engine/MultiStage/MultiStageContracts.cs`、`Engine/Strategies/MultiStageAmplifierStrategy.cs` |
| BJT 策略（教材估算法显示 + DC/AC 步骤相位 + 共集/共基 + 信号源内阻 Rs/电解电容/Aus + r_bb'/r_be 参数解析） | `Engine/Strategies/BjtAmplifierStrategy.cs` |
| MOSFET/JFET 策略（平方律/Shockley + 教材估算法显示 + DC/AC 步骤相位；暂不支持源内阻） | `Engine/Strategies/MosfetAmplifierStrategy.cs` |
| 教学结果显示规范（单位选择/两位小数/回代值；AC 参数含 Rs/Aus 字段） | `Services/TeachingDisplay.cs`（数字格式另有 `Services/ScientificNotationFormatter.cs`）、`Models/AnalysisResult.cs` |
| 解题阶段与分组（`SolutionPhase`/`Step.Phase`/`ToAnalysisResult`→`StepGroups`） | `Engine/PedagogicalSolution.cs`、`Models/AnalysisResult.cs`、`Views/AnalysisStudioPage.xaml` |
| LaTeX 解析与二维排版（纯 C#，控制台可测） | `Math/MathNode.cs`、`Math/MathLatexParser.cs`、`Math/MathLayoutEngine.cs`、`Math/IMathTextMeasurer.cs`、`Math/EstimateMathTextMeasurer.cs` |
| 29 个教学电路生成器 | 仓库根 `tools/CircuitModels.Core`、`tools/CircuitModels.{Diode,BjtBias,BjtAmp,Mos}` |
| 29 个教学电路文件 | 仓库根 `examples/circuit-models/`（D01–D07、B01–B16、M01–M06，索引见该目录 README） |
| 公式框（Win2D 自绘、复制 LaTeX） | `Controls/MathFormulaView.xaml/.cs` |

正式按钮调用 `SolveCircuitAsync → NetlistExtractor → TopologyClassifier → ICircuitStrategy`，专用策略拒绝时回退 `MnaLinearStrategy`。构造/重置不带固定共射预览；正式求解基于独立快照在后台执行并缓存最近解供报告导出。旧 HTTP/固定共射推导服务已于 2026-10-19 清理。

## 4. 日常操作与交互

1. 欢迎页可新建空白或加载模板；当前四种教学模板为共射、RC 暂态、阻容耦合、共基极。
2. 在左栏点击或拖入符号，在检查器编辑 `DisplayNumber/UnitSymbol`，在画布布线；四类受控源在“受控源”类别中。
3. 点击“求解与理论分析”或 `Ctrl+R`；结果进入右侧推导页。专用策略拒绝的**纯线性**网络自动回退 MNA 2.0，实时指标页会显示节点电位、任意端口戴维南表和频响扫频；二极管/MOSFET/JFET/多级教学电路不会回退成 MNA 近似结果。
4. 选中二极管或三极管后，检查器可切换“半导体材料”（硅管≈0.7 V / 锗管≈0.3 V）；BJT 用该压降计算 VBE/VEB，二极管用 VF 做恒压降/Shockley 模型。MOS 的数值字段已标注为阈值电压 Vth（NMOS 正值、PMOS 负值），JFET/耗尽型 MOS 标注为夹断电压 VGS(off)。MOS 的 K/λ 与 JFET 的 IDSS 已保存在工程 JSON（`MosfetK`/`MosfetLambda`/`JfetIdss`），并可在检查器直接编辑（默认 K=1 mA/V²、λ=0；JFET 必须填写有限正数 IDSS，缺失/非正数求解时明确失败）。
5. 使用工程菜单打开/导入、保存、另存为、导出 SPICE 或“导出分步分析报告”（Markdown/LaTeX/Word/HTML）。
6. 打开 Web/Python `CircuitGraph` JSON 时无需额外命令：同一“打开工程”入口探测后导入并提示另存为原生工程。也可以直接打开 `examples/circuit-models/` 下的 29 个原生 `.circuit` 教学电路（索引见该目录 README）；其中 B12–B14 为多级电路，可打开/编辑/画图，但求解会明确拒绝（后续规划）。
7. 求解期间可点击“取消求解”；取消通过令牌停止 MNA 循环并丢弃迟到结果。
8. **教学结果显示口径（第七轮）**：求解后 Q 点/AC 指标/摘要/推导步骤按**教材估算法**显示——分压偏置先算空载 `U_B`，再 `I_EQ→I_BQ→I_CQ→U_CEQ`；每个中间结果按显示单位最多两位小数（AwayFromZero）舍入，并把该显示值代入下一步；公式保留全部项（`(1+β)` 等）不化简。戴维南精确值只在后端（`QPointInfo.Exact*` 与第 1 步 `Values`），**界面不显示**，也不再出现"戴维南精确/教材估算"并列文案。题3-12（`document/3-6题.circuit`）显示链为 `U_B=11.63 V → I_EQ=53.85 μA → I_BQ=0.53 μA → I_CQ=53 μA → U_ECQ=11.82 V`。BJT/MOS 推导按"直流等效电路（DC）→ 交流等效电路（AC）"分组卡片展示；工作区以未舍入加载解判定；无 Re 或空载估算越界而加载解有效时，切换含基极负载方程、保留教学舍入；真实非放大区停止 AC，数值失败单独诊断。`tests/CircuitModelChecks -- --file <path>` 可对任意 `.circuit` 打印全部步骤/Values。
9. **BJT 信号源内阻 Rs/电解耦合电容/Aus（第八轮）**：`vs→Rs→输入耦合电解电容→基极/发射极`（源与输入耦合电容之间恰一只电阻）的教材拓扑可用；识别到 Rs 时实时指标卡与报告显示"信号源内阻 Rs""源电压增益 Aus"两行（`Aus=Av·Ri/(Rs+Ri)`，共基 `Au·ri'/(Rs+ri')`），fL 输入时间常数改为 `τ1=(Rs+Ri)·C1`；电解输入耦合电容在 DC 相追加极性核验步骤（反接给 Warning 与摘要告警）。示例见 `examples/circuit-models/B07-common-emitter-bypassed.circuit`（R6=50 Ω、C3=47 μF 电解）。MOSFET/JFET 放大器暂不支持 Rs。
10. **BJT 小信号参数 r_bb'/r_be(h_ie)（第九轮）**：编辑入口两处——① 右侧"元件参数检查器"（选中 BJT 时）的"基区体电阻 rbb′:"与"给定 rbe/hie:"；② "参数设置 → 三极管模型与信号源特性" Group 3 中 β 行下的同名两行宏参数。两者默认 **0=自动**（r_bb' 共射/共集 200 Ω、共基 50 Ω；r_be 按 Q 点公式计算），填正数即按题目给定值参与 Av/Ri/Ro/fL（给定 r_be 直接采用、不再二次舍入）。宏参数与项目中所有 BJT 元件双向同步；非法输入自动归零。r_bb'/r_be 随工程保存/读回（旧工程缺省 0=自动）。

| 操作 | 快捷键/方式 |
|---|---|
| 旋转 | `Space` 或 `R`，每次 90° |
| 水平镜像 | `Alt+X`（可反复切换；多选时统一镜像） |
| 删除元件/导线 | `Delete` / `Backspace` |
| 撤销/重做 | `Ctrl+Z` / `Ctrl+Y`，元件/导线快照上限 50 |
| 取消 | `Esc` |
| 布线模式 | `W` |
| 平移/缩放 | 右键或中键拖动；滚轮缩放 40%–300% |
| 打开/保存/另存为 | `Ctrl+O` / `Ctrl+S` / `Ctrl+Shift+S` |

文本输入框会屏蔽部分画布快捷键。页面与控件均有快捷键处理，新增键位时应一并核对事件路由，避免重复执行。框选、组合选择、WireTap、线段拖动和导线跟随有源码实现；本次未做真实窗口交互验收。

参数同步既有通用选中元件编辑，也保留按 `VCC/Rb1/.../Q1/vs` ID 匹配的模板宏；新增器件不能假设自动成为这些宏的一部分。通过生成的公共属性更新，以触发通知；但公共属性通知本身不能解决单位语义、历史记录或修改标志覆盖问题。

## 5. 工程文件、导入与导出

保存格式是自有 `CircuitProjectData` JSON，扩展名 `.circuit/.json`。当前写入 `1.2.0`，读取兼容 `1.0.0`/`1.1.0`（旧文件材料字段按硅管、镜像字段按 false）。记录元件/引脚、位置/旋转/镜像、单位、半导体材料、导线路径与父线引用，以及模板参数字典。PNP 默认符号在 2026-10-18 改为发射极在上、集电极在下；读取旧 PNP 工程时会按旧 C/E 引脚布局自动迁移并重挂相关导线，没有其他任意旧版本迁移器。**BJT 小信号参数（第九轮）**：BJT 元件 DTO 新增 `BjtRbb`/`BjtRbe`（Ω，缺省 0=自动/按 Q 点计算；负数/非有限值在 `Validate()` 拒绝），随工程保存/读回；旧工程缺省 0，行为与第八轮一致，工程格式仍 1.2.0。**保存语义（第八轮修复）**：保存时保留工程标题 `ProjectName`（只有"未命名电路工程"或空标题才回退为文件名），`ProjectFileName` 仍更新为当前文件名；旧行为每次保存都改写 `ProjectName`，会丢失教材示例标题并导致生成器 `--verify` 工程名校验失败（D06 已恢复为"双向限幅电路（双管双偏置）"）。

打开 `.circuit/.json` 时先探测 Web/Python `CircuitGraph` 形状：命中则交给 `WebCircuitGraphImporter` 单向转换（definitionId/端口映射、SI→工程单位、确定性自动布局、每连接一根导线），导入结果置为未保存工程、不绑定原 JSON 路径；否则按自有工程 DTO 严格校验版本、ID、引脚/父线引用、路径和有限数值。第三方 EDA 原理图（KiCad/Altium `.sch`）仍不支持。模板名称里的 `.sch` 是显示名，不表示磁盘上已有这些模板文件。

工程读写、SPICE 导出和四种格式报告导出已实现。报告由 `Services/ReportExportService.cs` 从最近一次求解缓存（`ElectricalNetlist` + `PedagogicalSolution`）生成 Markdown、LaTeX+Circuitikz、Word `.docx`、自包含 HTML；菜单“工程管理 → 导出分步分析报告”。编辑电路或切换/新建/打开工程会清空最近解缓存，需要重新求解才能导出。PDF 不直接生成：用浏览器打印 HTML，或用 xelatex 编译 `.tex`（本机无 TeX，未真实编译）。

`ToSpiceNetlist()` 只导出有限器件文本；没有调用 ngspice，没有完整模型卡，不能保证导出文件可直接仿真。公式上的复制按钮仅复制单条 LaTeX。

## 6. 求解与公式限制

- RC/RL 检查标准串联单回路与零初值假设，支持 RC 高/低通；不能用于任意网络 Req，复杂网络会回退 MNA 稳态/相量（不再给瞬态答案）。
- RLC 要求各一个 R/L/C 的标准串/并联拓扑；复杂二阶网络会回退 MNA 相量分析。
- BJT 只接受**单只 NPN 或 PNP** 模型（四类偏置、共基极与**共集电极**），缺少必要支路或工作点无效时停止；硅/锗 VBE 压降可选，已去掉缺失器件默认值及教材固定答案，部分高频仍为模型估算。含二极管等非线性器件的单 BJT 网络仍由 BJT 策略明确拒绝。显示采用**教材估算法**（每步两位小数舍入并回代，后端戴维南精确值不显示）；工作区按未舍入加载解核验；无 Re 或空载估算越界但加载解有效时，采用含负载方程的舍入教学链，不再把估算越界误报为实际饱和。推导按 DC/AC 等效电路分组（`SolutionPhase`/`StepGroups`）。**支持信号源内阻 Rs/电解输入耦合电容/Aus（第八轮）**：Rs 仅识别"交流源与输入耦合电容之间恰一只电阻"的拓扑（如 vs—R6—C3 电解—基极），串联多只/复杂源网络仍明确失败并列出元件/网名与支持位置；电解电容按 `+/-` 引脚取网，极性核验归 DC 相；识别到 Rs 时 `Aus=Av·Ri/(Rs+Ri)`（共基 `Au·ri'/(Rs+ri')`）与 `τ1=(Rs+Ri)C1` 生效，UI/报告显示 Rs/Aus 两行；无 Rs 时输出与第七轮一致。反接电解电容仍可求解但给出 Warning 步骤与摘要告警。
- **BJT 小信号参数 r_bb'/r_be(h_ie)（第九轮）**：元件参数 `BjtRbb`（Ω，0=自动：共射/共集 200 Ω、共基 50 Ω）与 `BjtRbe`（Ω，0=按 Q 点计算；>0 为题目给定 r_be=h_ie）。computed 仍按教材链 `r_be=RoundResistance(rbb+(1+β)U_T/I_EQ)` 两位小数舍入并回代；given 原值直接参与 Av/Ri/Ro/fL（不再二次舍入，属有意设计）。rbe/hie 步骤文案/公式/代入使用实际 rbb 值，不再硬编码 200/50；第 2 步（共基 h_ie 步）`Values` 含 `rbb_ohm`/`rbe_ohm`/`rbe_source`（`computed`|`given`）。非法后端值（非有限/负数）明确失败（含"必须为有限非负数"）；UI 编辑 NaN/负数自动归零。**本轮无窗口验收**：按用户要求不做 ui-capture，两处编辑框的真实窗口外观/交互未验收，控制台回归 + 构建即完整验证范围。
- **二极管电路**：支持半波/桥式整流、电容滤波、串联/双向限幅、钳位、峰值检波与二极管直流偏置（9 类子类型）；采用恒压降分段线性模型 + 教材解析公式，**不是 SPICE 瞬态**；直流偏置只支持源—电阻—二极管单一串联回路，含并联/分支时明确拒绝并说明原因。
- **MOSFET/JFET 电路**：支持增强型/耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 的共源、共源+Rs、共源旁路、共漏、共栅；增强/耗尽 MOS 用 Level-1 平方律，JFET 用 Shockley 方程 `ID=IDSS·(1−VGS/VGS(off))²`（K=IDSS/VGS(off)²）；JFET 栅结正偏、IDSS 缺失/非正数明确失败并点名器件；Q 点处于可变电阻（三极管）区只报告分区并停止 AC 小信号推导，不输出伪增益；显示沿用教材估算法两位小数舍入回代，工作区用分段模型 + 直流负载线以未舍入值核验。K/λ（MOS）与 IDSS（JFET）已持久化且检查器可直接编辑（默认 K=1 mA/V²、λ=0；JFET 必须填有限正数）。**MOSFET/JFET 放大器暂不支持信号源内阻 Rs/Aus（第八轮范围仅 BJT）**。
- **多级放大**：晶体管总数 >1（含 BJT+MOS 混合）分类为 `MultiStageAmplifier/"Cascade"`，`MultiStageAmplifierStrategy` 明确拒绝，消息说明"多级/后续开发规划/预留"并提示拆分为单管；`IMultiStageAnalysis.TryPlan` 只返回阶段规划，**级联求解未实现**。B12–B14 文件可打开但不会给出级联结果。
- **教材估算法显示（第七轮）**：BJT/MOS/JFET 的 Q 点、AC 指标、摘要、步骤与 LaTeX 代入式的数字都由教材手算链生成；每个显示值最多两位小数（AwayFromZero）并回代下一步；公式本身不化简。全部题型（BJT/MOS/JFET + RC/RL/RLC/MNA/二极管）与报告显示同规范；显示舍入不改变后端内部精度。后端 `QPointInfo.ExactIbq/ExactIcq/ExactVceq`（及第 1 步 `Values` 的 `*_exact_*`）仅供诊断/测试/报告内部，UI 不绑定。
- 通用 MNA 2.0 支持 R/C/L、独立 DC/AC 电压/电流源、四种受控源、任意端口 Req/戴维南与 10 Hz–10 MHz 扫频；不接受二极管/变压器/BJT/MOS/JFET/VccPort，矩阵变量上限 256，全程响应取消令牌。该直接拒绝行为第六轮未放宽。
- 公式框为 Win2D `CanvasControl` 自绘（`Math/` 解析+布局，基础字号 16）：真上下角标（0.72×）、上下叠放分式、根号、bmatrix/pmatrix/cases/array、希腊字母、`\mathbf`、CJK 回退；按宽度自动换行、内容自适应高度，复制 LaTeX 按钮保留。限制：array 列格式竖线不画、超宽原子整体等比缩小、Canvas 文本不可选择、`\overline/\vec/\tilde` 只保留内容不画装饰、未知命令降级显示；不是 KaTeX/MathML/完整 TeX。控制台只验证解析/布局几何；第七轮已用 Win32 截图在真实窗口核对代表公式/模板（共射、题3-12 PNP、MOS；见第七轮记录 3.5）；**第八轮已用最终构建在真实窗口（150% DPI）对 B07 变体与普通模板完成静态截图级验收（含 Rs/Aus 两行、DC/AC 分组；证据 `tools/ui-verify/evidence/round8/`），并据该验收修复 AC 指标卡 RowDefinition 7→8 行的叠字 bug**；**第九轮按用户要求不做 ui-capture 窗口验收**；未做逐像素自动比对/主题/其它 DPI/自动化交互。
- **维护注意（编码）**：本机 `pwsh` 实为 Windows PowerShell 5.1，`Get-Content`/`Set-Content` 默认 ANSI，直接管道回写 UTF-8 中文源码会破坏文件（曾出现 359 个 U+FFFD）。修改含中文的源码/文档请只用文件读写工具，或用显式 UTF-8 IO（读用 `Get-Content -Encoding UTF8`；写回用 `[System.IO.File]::WriteAllText(路径, 文本, New-Object System.Text.UTF8Encoding($false))`，避免 PS 5.1 的 `-Encoding UTF8` 附加 BOM）；改完检查是否出现 U+FFFD 替换字符并跑构建/回归。

详细规则和扩展点见[引擎报告](projects/analysis-engine-technical-report.md)。

## 7. 验证与后续工作

[README](README.md) 提供构建与 C# 回归命令；**发布前实测（2026-10-19，0.2.0-preview.1）主项目 Debug/Release 构建 0 警告/0 错误（Release x64 自包含，528 个运行文件）**，非线性引擎回归 **1606/1606**（含第九轮 `BjtSmallSignalParameterChecks` 与 2026-10-19 工作区/耗尽型/JFET 检查）、`M2SolverChallenger` **166/166**（含第九轮宏同步/默认自动/修改失效/rbb-rbe 保存读回断言与后续新增回归）、29 个教学电路文件校验 **259/259**、公式排版 411/411、显示两位小数 55/55、四个生成器 `--verify` 7/7、6/6、10/10、6/6、30 题本地基准 **30/30（Accuracy=100.00%）**；既有常规 C# 回归 ChallengerRunner 131、M3 32、M3_4 9、EngineV2 **306**、报告 110、导入 **172**、符号/镜像/节点 230，另有独立数值审计 1359/1359；全量矩阵 **20 组命令 / 4905 项断言全部通过**，发布目录启动冒烟测试通过。**默认 0（自动）时数值与第八轮完全一致**：B07 教学变体仍为 `U_B=2.79 V → I_EQ=2.09 mA → I_BQ=20.69 μA → I_CQ=2.07 mA → U_CEQ=7.84 V`，`Av=−34.25、Ri=1.23 kΩ、Aus=−32.91、fL≈41.7 Hz`；自定义/给定参数示例见[第九轮修订记录](projects/technical-code-fixes-20261018-round9.md) 3.3。`CircuitModelChecks -- --file` 支持任意 `.circuit` 单文件诊断（含 B07 变体）。**真实窗口验收**：第七轮已用 Win32 `PrintWindow` 截图核对共射模板、题3-12 PNP、MOS 共源模板；第八轮对 B07 变体 + 普通模板做静态截图级验收（证据 `tools/ui-verify/evidence/round8/`，含 Rs/Aus 两行与 AC 卡叠字修复）。**2026-10-19 发布验证为控制台回归 + 构建 + 启动冒烟，未做新的 ui-capture 窗口验收**；第九轮按用户要求不做 ui-capture，r_bb'/r_be、MOS K/λ 与 JFET IDSS 编辑器的真实窗口外观/交互均未验收；第八轮截图不覆盖新增编辑器。窗口验收均为静态截图级，**未做**逐像素自动比对/深浅主题/其它 DPI/自动化交互断言；两组位移压力测试中的 80px 误并网场景通过；这些证据都不能代替真实画布拖拽/自动化交互验收。用户正在运行的客户端是旧构建，新功能需重新构建并重启后才能看到。

`npm run check` 仅覆盖独立 Web/Python 原型，第一轮通过 16 个 TS、17 个 Python 引擎、16 个 API 测试及相关构建检查；后续轮次未复跑。当前 Python 注册六个策略；旧文档写“五个”和“33 项”均已过时。

2026-10-19 耗尽型 MOS/JFET 与工作区校验修订见[耗尽 MOS/JFET 实现记录](projects/fet-depletion-jfet-strategies-20261019.md)、[工作区校验修订](projects/operating-region-validation-20261019.md)，第九轮修改见[第九轮代码修订记录](projects/technical-code-fixes-20261018-round9.md)，第八轮修改见[第八轮代码修订记录](projects/technical-code-fixes-20261018-round8.md)，第七轮修改见[第七轮代码修订记录](projects/technical-code-fixes-20261018-round7.md)，第六轮修改见[第六轮代码修订记录](projects/technical-code-fixes-20261018-round6.md)，审查结论见[第二轮审查报告](projects/technical-document-review-20261017-round2.md)。后续继续完善 MOSFET/JFET 源内阻/Aus 与小信号参数、多级级联、混合非线性、完整 ERC、公式排版扩展、PDF 与真实窗口验收；视觉安排在软件本体完善之后。完整待办和已验证范围见[当前规格](projects/spec-first-stage-local-circuit-analysis-20260601.md)。


### 2026-10-19 元器件工作区校验补充

教学舍入与逐步回代保留，工作区另以未舍入物理解核验。截图中删去 Re 并将射极接地的分压共射电路不再误报截止；无 Re 与空载估算失效采用含基极负载方程，BJT 统一核验 BE/BC 条件。MOS/JFET 用分段模型与 DC 负载线核验；二极管/FET 非有限状态独立为 Invalid。非线性回归 1606/1606、M2 166/166、教学文件 259/259、30 题基准 30/30，其余 C# 回归通过，WinUI 构建 0 警告/0 错误（发布前全量矩阵 20 组 / 4905 项断言全绿；阶段版本 0.2.0-preview.1 已发布，Windows x64 自包含发布目录 528 个运行文件，启动冒烟通过）。完整方程、测试、限制和独立输出目录见[本轮记录](projects/operating-region-validation-20261019.md)。未重启当前客户端；BJT 饱和 Q 点仍未重求解。
