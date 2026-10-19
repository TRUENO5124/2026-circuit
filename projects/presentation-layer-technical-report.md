# 展示层实现报告：WinUI 3 / XAML / Win2D

> 更新：2026-10-18（第九轮能力增量：BJT 小信号参数 r_bb'/r_be(h_ie) 两处编辑框、0=自动提示、宏参数与元件双向同步、无效输入归零；第八轮：实时指标卡 Rs/Aus 两行、报告导出同步两行、B07 教学变体分组显示、窗口截图验收与 AC 卡 Rs/Aus 叠字修复；第七轮：Win2D 自绘公式框、DC/AC 分组卡片、教材估算法两位小数显示、Q 点不再双口径）；主客户端是 `apps/desktop-winui`。早期 React/HTML 方案完整保存在[历史副本](archive/2026-10-17-before-winui-doc-sync/presentation-layer-technical-report.md)。本文逐项说明当前界面、交互、公式、文件、报告导出与导入实现，所有“已实现”均不代替真实窗口验收。

## 1. 展示层边界

当前使用 XAML 定义页面和控件，C# 提供 MVVM 状态与事件处理，Win2D 负责原理图绘制。未在主软件嵌入 WebView、React、KaTeX 或 HTML 页面。

```text
App → MainWindow（Mica / 自定义标题栏 / Frame）
        → AnalysisStudioPage（XAML）
             ↔ AnalysisStudioViewModel（状态、集合、生成命令）
             ↔ CircuitCanvas（Win2D / 指针与键盘操作）
             → CircuitSymbolPreview（原生矢量符号）
             → MathFormulaView（Win2D CanvasControl 自绘公式 / 复制 LaTeX）
```

`MainWindow` 直接进入工作台。未接入导航的示例页、旧设置页、独立符号展示页及其 ViewModel 已于 2026-10-19 清理；当前客户端采用上述单工作台入口。

## 2. 控件与状态映射

| 旧方案概念 | 当前主客户端实现 | 代码位置（相对 `apps/desktop-winui/`） |
|---|---|---|
| React 页面/CSS | WinUI Page、Grid、资源、ThemeResource | `Views/AnalysisStudioPage.xaml`、`App.xaml` |
| Zustand Store | ObservableObject、生成属性/命令 | `ViewModels/AnalysisStudioViewModel.cs` |
| React Flow Node/Handle/Edge | CircuitElement/CircuitPin/CircuitWire + Win2D | `Models/CircuitElement.cs`、`Controls/CircuitCanvas.xaml.cs` |
| SVG/DOM 元件 | `SymbolGeometry` 纯矢量几何 → Win2D/XAML 渲染 | `Models/SymbolGeometry.cs`、`CircuitCanvas`、`CircuitSymbolPreview` |
| KaTeX HTML 输出 | Win2D `CanvasControl` 自绘真排版（`Math/` 解析+布局）+ 复制 LaTeX | `Controls/MathFormulaView.xaml/.cs`、`Math/*` |
| 数字显示格式 | `TeachingDisplay` 教材单位/两位小数 + `ScientificNotationFormatter` | `Services/TeachingDisplay.cs`、`Services/ScientificNotationFormatter.cs` |
| 任务提交/轮询 | 进程内 C# 策略调用 + 专用策略→MNA 回退 | `SolveCircuitAsync()` |
| Web 文件下载 | WinRT 文件选择器 + JSON/文本/报告写入 | `Services/ProjectManagerService.cs` |
| Web 报告导出 | 原生四格式报告（Markdown/LaTeX+Circuitikz/DOCX/HTML） | `Services/ReportExportService.cs` + 工程菜单命令 |
| 原型格式导入 | 打开工程时自动探测 Web/Python CircuitGraph | `Services/WebCircuitGraphImporter.cs` |

页面与画布使用双向依赖属性、集合通知和事件桥接；Loaded 重复触发时不会重复注册画布事件。ViewModel 负责工程/求解状态，画布仍负责大量选择和几何修改，页面代码负责快捷键、拖放与事件接线；不能写成完全没有 code-behind 的纯 MVVM。

## 3. 工作台布局与生命周期

`AnalysisStudioPage.xaml` 定义顶部工程菜单/操作、三栏工作区和状态区域：

| 区域 | 当前内容 |
|---|---|
| 左栏 | 默认宽 280（范围 240–360），类别筛选、文字搜索、符号列表 |
| 中栏 | 弹性宽度，欢迎内容或当前工程画布，工程名、视图操作及底部推导抽屉 |
| 右栏 | 默认宽 420（范围 360–520），求解按钮，参数/实时指标/解题推导 Pivot；实时指标新增 MNA 2.0 结构化结果卡（节点电位、任意端口戴维南表）与交流频响/扫频卡 |
| 状态区 | 操作状态、内部坐标和缩放等信息 |

构造时初始化符号库，编辑集合为空、HasActiveProject=false、结果为尚未分析。新建/打开后激活画布，不再生成旧固定共射预览。

目前是单个活动工程状态；标签样式不能视为完整多文档/多工程并行编辑。当前版本新建/打开/关闭均清理相应选择、路径、历史和结果，并在丢弃修改前显示保存选择；窗口关闭也接入同一流程，详见[数据层报告](data-layer-technical-report.md)。

顶部“工程管理”菜单第二轮新增：

- “打开工程 / 导入 Web CircuitGraph... (Ctrl+O)”：选择 `.circuit/.json` 后先探测 Web/Python `CircuitGraph`，导入时自动布局并置为未保存原生工程；
- “导出分步分析报告”子菜单：LaTeX + Circuitikz（`.tex`）、Word（`.docx`）、Markdown（`.md`）、HTML（`.html`，可浏览器打印为 PDF）。导出使用最近一次求解缓存，编辑电路或切换工程后缓存失效，需重新求解。

求解区在专用教学策略明确拒绝时自动回退到通用 MNA 2.0，并在结果摘要中注明回退原因；第六轮起二极管/MOSFET/多级教学电路**不**参与 MNA 回退，而是给出各自教学策略的明确失败或结果。“取消求解”现在会通过协作式取消令牌停止 MNA 扫频/求解循环（不保证强制终止任意同步运算）。

## 4. 元件库、参数与模板

原生 `InitializeSymbols()` 当前初始化 **25 个条目**，包含普通/电解电容、VCC 端口、电源别名、NPN/PNP、N/P 增强与耗尽 MOS、N/P JFET、四类受控源等。这里的条目数不是求解器覆盖数，Web 元件定义仍是另一套 18 类。第三轮起，符号卡片与画布统一由 `Models/SymbolGeometry.cs` 的同一套矢量图元渲染：

| 符号组 | 标准处理 |
|---|---|
| BJT NPN/PNP | 外圆 + 基极竖条 + 集电极/发射极斜线；发射极箭头为**实心三角形**，NPN 向外、PNP 向内（可区分） |
| JFET N/P | IEC 60617 风格：竖直沟道 + 栅极线 + 实心箭头（N 指向沟道、P 背离），漏/源引线从沟道上下端垂直引出，不再带外圆 |
| MOSFET 四类 | 栅极板与沟道分离；增强型沟道 3 段、耗尽型 1 段实线；衬底/源箭头 N 指向沟道、P 背离；不再带外圆 |
| VCC 端口 | Multisim 风格电源横杠 + 引线，无向上箭头/三角；文字标签由画布/卡片单独绘制 |
| GND / 二极管 / 电源 / 受控源 / 无源件 / 变压器 | GND 为 IEC 三横线；二极管三角+阴极竖线；电源圆+极性/方向；受控源菱形+控制端标记；R/C/L/变压器统一线宽并保持 IEC/ANSI 风格 |

元件参数检查器不再使用通用“元件数值”标签，而是按类型显示参数含义（`ParameterLabel`）：电阻 R、电容 C、电感 L、BJT β、增强型 MOS 阈值电压 Vth、耗尽型 MOS/JFET 夹断电压 VGS(off)、二极管正向压降 VF、受控源 μ/gm/rm/β 等；下方 `ParameterDescription` 解释物理意义（例如“Vth 是阈值电压，不是漏源电压或击穿电压”）。二极管与 BJT 另有“半导体材料”下拉框（硅管≈0.7 V / 锗管≈0.3 V），数据随工程保存（格式 1.2.0）。

搜索匹配名称、字幕、ID 和描述，类别为字符串过滤。点击添加或拖放添加时创建 CircuitElement 并初始化默认引脚，首次添加可先创建空白工程。卡片由 `CircuitSymbolPreview` 用同一矢量几何渲染，画布由 Win2D 用同一几何绘制；二者不再各自维护一套手绘符号。

检查器绑定所选元件的数值、单位、坐标与方向；清单也可编辑属性。模板宏仍按固定元件 ID 同步部分参数。类别清单已补入“半导体管”，二极管可直接按类筛选。交流源新增 FrequencyHz 输入，不再借用幅值字段。

`CircuitTemplateFactory` 实际提供：

| 入口 | 模板 |
|---|---|
| `CreateBlankTemplate` | 空白 |
| `CreateCommonEmitterTemplate` | 分压偏置共射 |
| `CreateRcTransientTemplate` | 一阶 RC 暂态 |
| `CreateRcCoupledTemplate` | 阻容耦合放大 |
| `CreateCommonBaseTemplate` | 共基极 |

`.sch` 模板名是工程显示文件名；工厂直接生成元件和导线，不读取磁盘 `.sch`。共射轨道 Y=80/480 属于该模板，不代表所有布局均符合相同几何约束或已通过标准认证。

## 5. 画布坐标与渲染

Win2D 在 `Canvas_Draw` 读取集合绘制网格、导线、交点、器件及选中/悬停状态。器件符号来自 `SymbolGeometryLibrary.Build(type)` 的统一图元（线段/实心多边形/圆），画布只负责旋转、颜色、线宽与引脚圆点；半导体参数标签使用 `FormattedParameterDisplay`（β/Vth/VGS(off)/VF），不再只显示裸数值。世界到屏幕采用：

```text
screen = world × scale + translation
world  = (screen - translation) / scale
Matrix = CreateScale(scale) × CreateTranslation(translation)
```

初始缩放 1，平移 `(100,40)`；缩放钳在 0.4–3.0，滚轮正向乘 1.15、反向乘 0.85，围绕指针或视口中心调整平移。不是无级动画引擎。

元件、线段拖动使用 10 单位吸附。状态栏已改为内部坐标，不再标记 mil；仍没有 PCB 物理单位转换。

元件绘制与引脚世界坐标依据旋转角变换；命中使用反向旋转局部矩形。导线使用 `GetPathPoints()` 的正交路径和显式引用，并非旧版 `DrawCircuitWires()` 根据模板临时拼出全部线条。

## 6. 交互与快捷键

| 操作 | 当前实现 |
|---|---|
| 放置元件 | 点击/拖入左侧符号，创建默认引脚 |
| 选择与多选 | 元件/导线命中、框选，Ctrl/Shift 组合选择路径 |
| 元件拖拽 | 内部坐标移动并同步关联导线 |
| 旋转 | Space/R，每次 90° |
| 删除 | Delete/Backspace，移除元件及直接关联导线；分支悬空仍需验证 |
| 布线 | W 切换；引脚间连接、WireTap 分支；可通过选中导线操作引出分支 |
| 线段调整 | 按起始路径拖动线段、更新 Waypoints/正交路径 |
| 平移 | 右键或中键拖拽 |
| 缩放 | 滚轮或工具按钮；重置回初始视口 |
| 撤销/重做 | Ctrl+Z/Y；元素/导线克隆快照，上限 50 |
| 求解 | Ctrl+R |
| 文件 | Ctrl+O、Ctrl+S、Ctrl+Shift+S |

数值输入获得焦点时记录快照，撤销/重做后从元件同步模板宏；删除父导线会递归移除分支。撤销仍不记录全部 UI/非 UI 字段事务，不能承诺所有程序化修改可撤销。页面和控件均有部分快捷键处理；真实焦点和路由行为列入人工验收。

## 7. 母线、WireTap 与焊点

母线是 `Wires` 中的普通声明式导线，支线通过 `WireTap` 父导线 ID 建立引用；引脚引出的线、母线和子支线分别更新。画布与 ViewModel 中各自存在导线同步代码，修改一处并不能保证所有交互路径一致。

几何更新支持沿母线垂直投影和有限轮次的子线跟随；没有任意障碍物下的 A* 自动布线。嵌套分支与父线删除后的处理仍需扩展验收。

焊点以 `FillCircle(dot, 1.9f, ...)` 绘制，即直径 3.8 单位，且在引脚 5 单位净空内排除。应根据显式连接和分支判定解释电气交点，不能把所有线段的视觉相交视为短接。当前布局和颜色可随主题调整，尚无本版本的截图/高 DPI 视觉回归证据。

## 8. 求解结果与公式展示

右栏展示 `AnalysisResult.QPoint/AcParameters/Steps/StepGroups/RawMnaLog`；推导步骤含标题、方法、解释、符号式、代入式和结果。底部抽屉可重复展示步骤。非晶体管策略（RC/RL/RLC/MNA）将 Q 点设为破折号/说明；二极管策略 Q 点标记“二极管非线性电路”且 AcParameters 为 null，MOSFET 策略映射 `Ibq="—"、Icq=ID、Vceq=VDS`，共集电极正常填 QPoint/AcParameters。

**教材估算法显示（第七轮）**：BJT/MOSFET 的 Q 点、AC 指标、摘要与步骤数字都按教材手算链生成，每个中间结果按显示单位最多两位小数（AwayFromZero）舍入并回代下一步；公式不化简（`(1+β)` 等保留）。因此 Q 点面板显示的是教材估算值（如题3-12 为 `0.53 μA / 53 μA / 11.82 V`），**不再并列显示“戴维南精确/教材估算”两种口径**。后端戴维南精确值只写入 `QPointInfo.ExactIbq/ExactIcq/ExactVceq` 与第 1 步 `Values`，XAML **不绑定**这些字段；若教材估算法把工作点算进饱和区（软分压器），求解明确失败并提示“饱和区”，不显示伪结果或后端精确值。全部教学题型与报告显示值最多两位小数。指标卡仍复用放大电路字段，名称与语义未完全泛化。

**信号源内阻 Rs/源电压增益 Aus 指标（第八轮）**：实时指标卡的 AC 参数区在 `AcParameters.HasSourceResistance=true` 时追加“信号源内阻 Rs:”与“源电压增益 Aus:”两行（默认隐藏；无 Rs 时 `SourceResistance`/`SourceVoltageGain` 保持“—”、`HasSourceResistance=false`），仅识别到 Rs 的 BJT（共射四类偏置/共基/共集）显示；识别不到 Rs 的拓扑界面与第七轮一致。AC 指标卡 Grid 为 **8 行** `RowDefinition`（Row 0–5 原 Av/Ri/Ro/fL/fH/BW，Row 6 Rs、Row 7 Aus）——**窗口验收首拍曾发现旧 7 行定义导致 Rs/Aus 两行叠字（bug 现场截图 `13_b07_ac_metrics_region.png`），补足 8 行后重拍 `11b/12b/13b/14b` 正常**。报告导出 `Services/ReportExportService.cs` 的 AC 参数表同步追加“信号源内阻 Rs”“源电压增益 Aus（含 Rs）”两行，四种格式共享同一报告模型。**MOSFET 放大器暂无 Rs/Aus 行**（第八轮范围仅 BJT）。**窗口截图验收**：最终构建（18:30:46）在真实窗口（Win32 `PrintWindow`，150% DPI）确认 B07 变体实时指标显示 Rs/Aus 两行且无叠字，普通模板（`14b_normal_metrics_fixed.png`）不显示两行；证据目录 `tools/ui-verify/evidence/round8/`。

**BJT 小信号参数编辑（第九轮）**：两处编辑入口（均绑定 `ViewModel.SelectedElement.BjtRbb`/`BjtRbe` 或宏参数 `RbbNumber`/`RbeNumber`）：

1. **右侧“元件参数检查器”卡**（选中 BJT、`SupportsBjtSmallSignalParameters` 为 true 时显示）：“基区体电阻 rbb′:”与“给定 rbe/hie:”两个 `NumberBox`（Ω），`Minimum=0`，旁注“Ω (0=自动)”/“Ω (0=计算)”，并显示说明“r_bb′ 自动：共射/共集 200 Ω、共基 50 Ω；给定 rbe(hie) 后直接采用，不再按 Q 点公式计算。”
2. **“参数设置”标签页 Group 3“三极管模型与信号源特性”**：在 β 行下新增同样的 rbb′/rbe(hie) 两行宏参数编辑框（0=自动/0=计算）与说明。

- 宏参数 `RbbNumber`/`RbeNumber` 变更时同步到项目内所有 BJT 元件；模板创建/打开工程时从第一个 BJT 元件刷新宏；选中 BJT 的元件参数变化会回写宏，两处显示保持一致。
- UI 编辑 NaN/负数自动归零（0=自动）；后端非法数据才触发明确失败（见引擎报告 6.1.2）。
- **验证范围**：第九轮按用户要求**不做 ui-capture 窗口验收**，只完成控制台回归 + 构建；两处编辑框在真实窗口中的外观、焦点与键鼠交互**未验收**，第七/八轮的窗口截图证据不覆盖本轮新增编辑器。

第二轮新增“实时指标”页中的结构化卡：

- **通用线性网络结构化结果（MNA 2.0）**：模型摘要、节点直流电位列表、任意端口戴维南/诺顿等效表（含 Req 与 Voc 显示），仅当 `AnalysisResult.LinearNetwork` 非空时显示；
- **交流相量与频率响应扫描**：输入/输出端口标签、工作频率增益/幅值、增益 dB、相位、上下限截止与带宽，以及 61 个扫频点（频率/增益 dB/相位）滚动列表与边界说明。

这些卡片使用 `Models/LinearAnalysisModels.cs` 的结构化字段，不是从步骤文本解析。原生结果仍没有完整 DC/AC 等效 `CircuitGraph` 图元、节点电压热图或可交互频率扫描曲线，界面也未自动绘制等效电路图；等效图目前只出现在 LaTeX 报告自动生成的 circuitikz 原理图中。

**`MathFormulaView` 的当前实现（第七轮重写）**：

1. 把 LaTeX 字符串交给纯 C# `Math/MathLatexParser` 解析为 `MathNode` 语法树（支持 `\frac`、`\sqrt`（含 `\sqrt[n]`）、上下标、`\text/\mathrm/\mathbf/\mathit`、希腊字母、关系/二元运算符、`\left/\right`、间距、`\\` 换行、matrix/cases/array/aligned 与 `\hline`）；解析器永不抛异常，非法输入降级为可识别文本。
2. `Math/MathLayoutEngine` 按当前宽度做二维布局：真上下角标（0.72× 字号 + 基线偏移）、上下叠放分式、根号、矩阵/cases/array 网格、贪心自动换行（在运算符/标点处断行）；单一不可拆分原子超宽时整体等比缩小，不横向裁切。
3. `CanvasControl` 用 DirectWrite 自绘文本片元与线条/定界符几何；基础字号 `BaseFontSize=16f`（旧 13.5px 文本已废弃），控件高度随内容自适应，按可用宽度重新布局；数学字体候选 Cambria Math 等，CJK 回退 Microsoft YaHei UI 等，颜色跟随主题强调前景。
4. 右上角“LaTeX”按钮把原始公式放入系统剪贴板（保留）。

因此现在是**真正的二维公式排版**（不是 Unicode 转写/斜线分数/文本括号）；但仍有明确限制：array 列格式竖线不画、Canvas 自绘文本不可选择/复制局部文本、`\overline/\vec/\tilde` 等只保留内容不画装饰（`\hat/\dot/\ddot` 有装饰）、未知命令降级显示；不嵌入 KaTeX/MathML，也不是完整 TeX 排版引擎。`tests/MathFormulaChecks` 用确定性测量器验证解析/布局几何；2026-10-18 第七轮已用 Win32 `PrintWindow` 截图在真实窗口（150% DPI、1920×1118 物理像素）核对共射模板、题3-12 PNP 与 MOS 共源模板的公式排版/DC-AC 分组/两位小数（见[第七轮修订记录](technical-code-fixes-20261018-round7.md) 3.5），但逐像素自动比对、主题/其它高 DPI 与窗口尺寸组合仍未验收。

## 9. 文件、导出与手机上传

| 能力 | WinUI 主软件 | 保留的 Web 原型 |
|---|---|---|
| 保存/打开 | CircuitProjectData JSON，`.circuit/.json` | CircuitGraph JSON，另一套格式 |
| Web 格式导入 | `WebCircuitGraphImporter`：`.circuit/.json` 中探测 Web/Python CircuitGraph，自动布局并转换为原生工程（单向） | 自身格式 |
| SPICE 导出 | 工程菜单、文件选择器、有限器件文本 | 不作为原生功能来源 |
| 复制公式 | 可复制单条原始 LaTeX | KaTeX 展示/原型复制 |
| Markdown 报告 | 已提供（`*.md`） | 原型另有 JSON/HTML 导出 |
| LaTeX/Circuitikz 文件 | 已提供（`*.tex`，ctexart+circuitikz 自动原理图）；本机无 TeX，未真实编译 | ExportStudioModal 与生成/下载函数已实现；未编译验证 |
| Word 报告 | 已提供（`*.docx`，手写最小 OOXML，可用 python-docx 解析；未在 Word 实机打开验收） | 无 |
| HTML 报告 | 已提供（自包含，可浏览器打印为 PDF） | `exportAnalysisReport` 已实现 |
| PDF | 仍不直接生成：HTML 打印或外部 xelatex 编译 `.tex` | 有浏览器打印说明，无专用 PDF 实现 |
| QR/SSE 手机会话 | 未接入原生工作台 | MobileCaptureModal 已有二维码、倒计时、SSE 与图片接收代码 |

Web 原型的这些功能不应继续写成全部未实现，也不能移植成 WinUI 已支持的能力。FastAPI `/capture/{session_id}` 已在第二轮设置 HTMLResponse，并对嵌入 URL 的会话与 token 编码。二维码和接口代码存在不等于手机全流程已验收。

报告导出的公开入口是 `ReportExportService`（纯内存、不写文件、无 WinUI 依赖），界面命令只负责选择保存位置与写盘；四种格式共享同一报告模型（报告信息、指标、原理图、推导步骤、结构化线性网络、SPICE）。AC 参数表在识别到信号源内阻 Rs 时包含“信号源内阻 Rs”“源电压增益 Aus（含 Rs）”两行（第八轮）。报告边界与实测结果见[第二轮修订记录](technical-code-fixes-20261017-round2.md)，第八轮 Rs/Aus 与保存命名变化见[第八轮修订记录](technical-code-fixes-20261018-round8.md)，第七轮显示口径与公式框变化见[第七轮修订记录](technical-code-fixes-20261018-round7.md)。

视觉识别计划后续开发，当前尚未接入；没有“手机上传→自动识别拓扑→原生求解”的链路。

## 10. 验证与维护要求

用户随后授权程序与测试。第九轮已运行主项目构建（0 警告 0 错误，含默认 Debug 重新构建）；非线性引擎回归 **913/913**（原 822 + 91 条小信号参数检查）、`M2SolverChallenger` **160/160**（原 156 + 4 条 rbb/rbe 宏同步/默认/失效/保存读回）、29 个教学电路文件校验 **259/259**、公式排版 411/411、显示两位小数 55/55、四个生成器 `--verify` 7/7、6/6、10/10、6/6、30 题基准 **30/30（Accuracy=100.00%）**；既有常规 C# 回归（ChallengerRunner 131、M3 32、M3_4 9、EngineV2 306、报告 110、导入 172、符号 230）与独立数值审计 1359/1359 全部通过（见[第九轮修订记录](technical-code-fixes-20261018-round9.md)）。符号几何由 SymbolChecks 从几何层验证、公式由 `MathFormulaChecks` 用确定性测量器验证解析/布局几何；第七轮用 Win32 `PrintWindow` 截图在真实窗口（150% DPI）对共射模板、题3-12 PNP 与 MOS 共源模板的公式排版、DC/AC 分组、两行式卡片头与两位小数显示做了静态抽查（见[第七轮修订记录](technical-code-fixes-20261018-round7.md) 3.5）；**第八轮已用最终构建在真实窗口对 B07 变体 + 普通模板做静态截图验收，确认实时指标 Rs/Aus 两行显示正常（并据首拍发现的叠字 bug 补足 AC 卡 8 行 RowDefinition）、普通模板不显示两行**，证据目录 `tools/ui-verify/evidence/round8/`（含 bug 现场 `13_b07_ac_metrics_region.png` 与修复后 `11b/12b/13b/14b`）。**第九轮按用户要求不做 ui-capture 窗口验收**：r_bb'/r_be 两处编辑框的真实窗口外观/焦点/键鼠交互未验收，第七/八轮截图不覆盖本轮新增编辑器；窗口验收仍为静态截图级，像素级自动比对、深浅主题、其它 DPI 组合、自动化交互与选择器交互仍未验收；用户进程运行旧构建时看不到新功能。控制台结果不能代替窗口操作证据。

扩展界面时应同步检查绘制与模型引脚、单选和框选路径、画布/ViewModel 两套跟随更新、状态通知/撤销/脏标志、工程序列化、`SolutionPhase`/`StepGroups` 映射、`AcParametersInfo` 的 Rs/Aus 字段绑定（`HasSourceResistance` 控制可见性）、BJT 小信号参数绑定（`BjtRbb`/`BjtRbe` 与 `RbbNumber`/`RbeNumber` 双向同步、0=自动）以及策略支持范围。新增结果卡时还应同步 `AnalysisResult` 映射与报告 renderer。详细待验收项见[测试计划](wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md)，发现项与证据见[第二轮审查报告](technical-document-review-20261017-round2.md)。
