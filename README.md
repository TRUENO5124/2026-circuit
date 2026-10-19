# 电路教学辅助系统（Circuit Learning Assistant）

面向电路与模拟电子技术教学的 Windows 原生桌面程序。当前主客户端采用 **WinUI 3 + XAML + C# / .NET 9**，画布与公式框由 Win2D 绘制，电路提取、分类及教学求解在 C# 进程内执行；专用教学策略覆盖 RC/RL、RLC、单只 NPN/PNP 的共射/共基/**共集电极**、**二极管非线性电路**与**增强型/耗尽型 NMOS/PMOS 及 N/P 沟道 JFET 单管放大电路**（共源/共源+Rs/共源旁路/共漏/共栅；MOS 用 Level-1 平方律，JFET 用 Shockley 方程，K/λ/IDSS 可在检查器编辑并随工程持久化），通用线性网络使用进程内复数 MNA 2.0 求解，分析报告由 C# 原生导出。多级放大只预留接口并明确拒绝（列入后续规划）。BJT 放大器支持**信号源内阻 Rs**（源与输入耦合电容之间恰一只电阻的教材拓扑）与**电解输入耦合电容**（按 +/– 引脚识别、DC 相极性核验），并给出源电压放大倍数 `Aus=Av·Ri/(Rs+Ri)`；BJT 的**小信号参数 r_bb'（基区体电阻）与 r_be(h_ie) 可调**（0=自动：r_bb' 共射/共集 200 Ω、共基 50 Ω，r_be 按 Q 点计算；给定 r_be 直接采用），随工程持久化。教学结果显示遵循教材口径：BJT/MOSFET 按**教材估算法**逐步舍入到两位小数并把显示值代入下一步；全部题型结果最多显示两位小数；BJT/MOSFET 推导按"直流等效电路（DC）→ 交流等效电路（AC）"分组；公式框为 Win2D 自绘的真排版（上下角标/分式/根号/矩阵/自动换行），后端戴维南精确值保留但不显示。

> 文档基线：2026-10-19 当前工作区（耗尽型 MOS/JFET 教学求解与元器件工作区物理核验修订后），包含尚未提交的代码；阶段版本 0.2.0-preview.1。React/HTML 编辑器及 Python/FastAPI 服务仍保留为独立原型；WinUI 工作台没有通过这些模块完成日常求解。

> 第二轮（2026-10-17）已实现通用线性网络 MNA 2.0（受控源/动态元件/任意端口戴维南等效/交流相量频响）、原生四种格式分析报告导出、Web/Python CircuitGraph 单向导入与 30 题本地基准。第三轮（2026-10-18）新增单只 PNP 三极管四类教学偏置分析、硅/锗管压降参数（二极管 VF 与 BJT VBE/VEB）、按 IEC 60617/IEEE 315/Multisim 参考统一的元件符号矢量库，并明确 MOS 等参数含义；PNP 默认符号改为发射极在上、集电极在下，全部元件新增 ALT+X 水平镜像，引脚与导线/焊点节点跟随变换。第四、五轮修正符号与镜像细节。**第六轮（2026-10-18）**新增二极管非线性教学求解（半波/桥式整流、电容滤波、串联/双向限幅、钳位、峰值检波、直流偏置共 9 类子类型）、MOSFET 平方律教学求解（NMOS/PMOS 共源/共漏/共栅）、BJT 共集电极（射极跟随器）、多级放大预留接口与明确拒绝，并生成 29 个可直接打开求解的原生 `.circuit` 教学电路文件（`examples/circuit-models/`）；MOS 的 K/λ 已写入数据模型与工程文件（检查器编辑框与 JFET IDSS 于 2026-10-19 补齐）。第六轮补充的 BJT 分压偏置"教材估算法对照"已在后续轮次演进。**第七轮（2026-10-18）**按用户反馈完成四项修正：① 显示层改为**教材估算法**单一口径（每步两位小数舍入并代入下一步），删除"戴维南精确 vs 教材估算"并列显示，后端精确值只保留在 `QPointInfo.Exact*`/步骤 `Values`；② "结果最多两位小数"推广到**全部教学题型**（BJT/MOS + RC/RL/RLC/MNA/二极管/报告）；③ BJT/MOS 推导步骤按**直流等效电路（DC）→ 交流等效电路（AC）**分组（`SolutionPhase`/`StepGroups`）；④ 公式框重写为 **Win2D 自绘真排版**（上下角标/叠放分式/根号/矩阵/自动换行，基础字号 16，复制 LaTeX 保留），废弃 Unicode 下标转换与横向裁切。**第八轮（2026-10-18）**按用户反馈修复 BJT 教学策略对"信号源内阻 Rs + 电解输入耦合电容"拓扑的拒绝：`vs→R6=50 Ω→C3=47 μF 电解→基极` 的 B07 变体现在可正常求解，新增 `Aus=Av·Ri/(Rs+Ri)` 与 `τ1=(Rs+Ri)C1`，实时指标卡/报告在识别到 Rs 时显示"信号源内阻 Rs""源电压增益 Aus"，未覆盖电阻诊断改为列出元件/网名与支持位置；同时修复"保存工程时把 ProjectName 改写为文件名"的真实缺陷（教材示例不再被改名，D06 工程名已恢复），并维护 `M2SolverChallenger` Suite 7（公式框改 Win2D 后反射新 API）与保存保留工程名回归。**第九轮（2026-10-18）**按用户反馈把 BJT 小信号参数** r_bb' 与 r_be(h_ie) 变为可编辑、可持久化的教学参数**：元件/DTO 新增 `BjtRbb`/`BjtRbe`（Ω，0=自动/按 Q 点计算），引擎全部 BJT 子类型读取实际值（不再硬编码 200/50 Ω），题目给定 r_be 直接采用；UI 在"元件参数检查器（选中 BJT）"与"参数设置 → 三极管模型与信号源特性"两处新增编辑框，宏参数与元件双向同步；默认 0 时数值与上一轮完全一致；新增 `BjtSmallSignalParameterChecks` 与 M2 的宏同步/默认/失效/保存读回断言（当轮计数已被 2026-10-19 发布前实测取代）。**本轮按用户要求不做 ui-capture 窗口验收**（仅控制台回归 + 构建）。**2026-10-19** 补全**耗尽型 NMOS/PMOS 与 N/P 沟道 JFET 单管教学求解**（复用共源/共源+Rs/共源旁路/共漏/共栅五类拓扑；JFET 用 Shockley 方程 `ID=IDSS·(1−VGS/VGS(off))²`、K=IDSS/VGS(off)²，增强/耗尽 MOS 用 Level-1 平方律；检查器可编辑 MOS K/λ 与 JFET IDSS 并随工程持久化），并新增**元器件工作区物理核验**修订（无 Re 分压偏置改用含基极负载方程、BE/BC 结条件统一校验、MOS/JFET 分段模型 + 直流负载线、二极管/FET 非有限值归 Invalid，教学显示仍按教材估算法两位小数回代）；发布阶段版本 **0.2.0-preview.1**（Windows x64 自包含，构建 0 警告/0 错误、528 个运行文件、启动冒烟通过；全量控制台回归 20 组命令 / 4905 项断言通过）。视觉模块保留在后续规划，当前先完善软件本体。证据见[第九轮修订记录](projects/technical-code-fixes-20261018-round9.md)、[第八轮修订记录](projects/technical-code-fixes-20261018-round8.md)、[第七轮修订记录](projects/technical-code-fixes-20261018-round7.md)、[第六轮修订记录](projects/technical-code-fixes-20261018-round6.md)、[第五轮修订记录](projects/technical-code-fixes-20261018-round5.md)、[第三轮修订记录](projects/technical-code-fixes-20261018-round3.md)、[第二轮修订记录](projects/technical-code-fixes-20261017-round2.md)与[第二轮审查报告](projects/technical-document-review-20261017-round2.md)。

## 开发文档

完整入口见 [开发文档索引](projects/README.md)。

- [当前架构与接口](PROJECT.md)
- [开发环境、操作与交接](HANDOVER.md)
- [WinUI 展示层](projects/presentation-layer-technical-report.md)
- [C# 数据模型与工程格式](projects/data-layer-technical-report.md)
- [C# 分析引擎与适用范围](projects/analysis-engine-technical-report.md)
- [2026-10-19 耗尽型 MOS/JFET 教学策略实现记录](projects/fet-depletion-jfet-strategies-20261019.md)
- [2026-10-19 元器件工作区校验修订](projects/operating-region-validation-20261019.md)
- [第九轮源码修订：BJT r_bb'/r_be(h_ie) 参数可调（数据层/引擎/UI/测试）](projects/technical-code-fixes-20261018-round9.md)
- [第八轮源码修订：BJT 信号源内阻 Rs/电解耦合电容/Aus、B07 教学变体、保存保留工程名](projects/technical-code-fixes-20261018-round8.md)
- [第七轮源码修订：教材估算法显示、两位小数、DC/AC 分组与 Win2D 公式排版](projects/technical-code-fixes-20261018-round7.md)
- [第六轮源码修订：二极管/MOSFET 非线性求解、BJT 共集电极与 29 个电路模型](projects/technical-code-fixes-20261018-round6.md)
- [29 个教学电路模型文件索引](examples/circuit-models/README.md)
- [第二轮源码修订、验证结果与剩余边界](projects/technical-code-fixes-20261017-round2.md)
- [第三轮源码修订：PNP、硅/锗压降与标准符号](projects/technical-code-fixes-20261018-round3.md)
- [第四轮源码修订：电解电容方向与六类 FET 标准符号](projects/technical-code-fixes-20261018-round4.md)
- [第五轮源码修订：PNP 默认朝向、ALT+X 镜像与节点跟随](projects/technical-code-fixes-20261018-round5.md)
- [第二轮审查报告](projects/technical-document-review-20261017-round2.md)
- [第一轮源码修订与验证](projects/technical-code-fixes-20261017.md)
- [第一轮逐项审查（代码修改前基线）](projects/technical-document-review-20261017.md)
- [后续视觉开发安排](projects/vision-roadmap.md)
- [测试计划与人工验收项](projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md)
- [专业名词解释](专业名词解释.md)

## 技术栈与运行环境

| 项目 | 当前源码配置 |
|---|---|
| 主客户端 | `apps/desktop-winui/CircuitAssistant.csproj`，`UseWinUI=true` |
| 目标框架 | `net9.0-windows10.0.26100.0` |
| 最低平台声明 | Windows 10 Build 17763；Windows 11 可使用主窗体声明的 Mica 材质 |
| 打包方式 | `WindowsPackageType=None`，当前为非打包桌面程序 |
| 图形 | `Microsoft.Graphics.Win2D` 固定为 `1.4.0` |
| MVVM | `CommunityToolkit.Mvvm`，`ObservableObject`、`ObservableProperty`、`RelayCommand` |
| 其余 NuGet 引用 | WindowsAppSDK、SDK.BuildTools、SDK.BuildTools.WinApp、MVVM 在主项目中使用 `Version="*"`，实际版本取决于恢复结果 |
| 工程存储 | `System.Text.Json`，`.circuit` 或 `.json` 文件 |

开发桌面端需 .NET 9 SDK、Windows SDK/WinUI 构建工具及对应运行库。Node.js、Python 是维护独立原型的依赖，不是原生求解路径的运行依赖。最低系统版本来自项目声明，本次未做 Windows 10 真机兼容性测试。

## 构建与启动

在项目根目录执行：

```powershell
dotnet restore apps/desktop-winui/CircuitAssistant.csproj
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore
dotnet run --project apps/desktop-winui/CircuitAssistant.csproj
```

日常启动直接双击 `start-winui.bat`，或执行 `./start-winui.bat`。已有构建产物时，脚本直接启动固定的 `win-x64/Debug` 可执行文件，不启动 PowerShell、不扫描源码，也不编译；首次没有可执行文件时才自动构建。修改代码后执行 `./start-winui.bat --build`，即可重新构建并启动。`start-all.bat` 启动的是 Web 原型和 API，不启动 WinUI。

## 当前功能与边界

- 三栏工作台：左侧符号检索与放置，中部 Win2D 画布，右侧参数、指标和解题步骤，底部可折叠推导抽屉。
- 25 个原生符号条目，包含电解电容、VCC 端口、四种受控源、NPN/PNP 三极管及多种 FET；可显示的符号种类不等于求解器全部支持。符号由 `Models/SymbolGeometry.cs` 统一按 IEC 60617 / GB/T 4728 / IEEE 315 / Multisim VCC 参考绘制：NPN 默认集电极在上/发射极在下，PNP 默认发射极在上/集电极在下且箭头向内；电解电容负极曲板凸向正极板；N/P JFET 无独立栅极条、栅极箭头指向/背离沟道且 D/S 经水平横线接沟道；四类 MOSFET 区分增强/耗尽与 N/P，栅极板与沟道分离、D/S 横线接沟道段中点、衬底箭头与源极短接；VCC 为电源横杠式符号。所有元件支持 `Alt+X` 水平镜像，旋转/镜像后引脚、导线端点与 WireTap 分支/焊点由统一模型同步。
- 半导体参数：BJT 的 β 与硅/锗 VBE（0.7 V / 0.3 V）可选；二极管 VF 可选硅/锗压降；MOS 参数明确标注为阈值电压 Vth（增强型）/夹断电压 VGS(off)（耗尽型），JFET 标注 VGS(off)。参数含义显示在检查器和画布标签中。
- 元件选中、拖拽、旋转、框选、导线绘制/选择/删除、线段调整、WireTap 分支与导线跟随；缩放范围 40%–300%。
- 工程新建、打开、保存、另存为；支持 `.circuit/.json` 的自有 JSON 格式（当前格式 1.2.0，兼容 1.0.0/1.1.0）。打开时可自动识别 Web/Python `CircuitGraph` JSON 并单向转换为原生工程（确定性自动布局、置为未保存），其他第三方原理图（如 KiCad/Altium `.sch`）仍不支持。
- 基于元件与导线快照的撤销/重做，上限 50 个历史快照；数值输入进入编辑时记录快照。工程切换清理历史；并非所有非 UI 字段修改均可撤销。
- 交流源幅值与 FrequencyHz 分开编辑/保存；新建、打开、关闭和窗口关闭前提供未保存处理。保存写同目录临时文件后替换，修改期间的新编辑不会被标记为已保存。
- 求解采用独立快照后台执行，先做网表与参数校验，提供取消求解和 15 秒上限；取消通过协作式取消令牌作用于策略与 MNA 求解/扫频循环。编辑后不发布旧结果。
- 空白工程及共射、RC 暂态、阻容耦合、共基极四种教学模板。
- C# 教学策略覆盖基础 RC/RL、串/并联 RLC、**单只 NPN 或 PNP** 的四类 BJT 偏置、共基极与**共集电极（射极跟随器）**；**二极管非线性电路**（半波/桥式整流、电容滤波、串联/双向限幅、钳位、峰值检波、二极管直流偏置共 9 类子类型）；**增强型/耗尽型 NMOS/PMOS 与 N/P 沟道 JFET** 共源/共源+Rs/共源旁路/共漏/共栅放大电路（MOS 用 Level-1 平方律，JFET 用 Shockley 方程；K/λ 与 IDSS 可在检查器编辑并随工程持久化）。专用策略拒绝的线性网络可回退到通用 MNA 2.0（直流稳态/交流相量），并在摘要注明回退原因。
- **教材估算法显示口径（第七轮）**：BJT/MOSFET 的 Q 点、AC 指标、摘要与推导数值都按教材手算方式生成——分压偏置先算空载 `U_B`，再 `I_EQ→I_BQ→I_CQ→U_CEQ`；**每个中间结果按显示单位最多两位小数（AwayFromZero）舍入，并把该显示值代入下一步**。公式本身不做代数化简（如 `(1+β)` 保留、`I_CQ=β·I_BQ` 不近似为 `I_EQ`）。戴维南精确值仅由后端保留在 `QPointInfo.ExactIbq/ExactIcq/ExactVceq` 与第 1 步 `Values`（诊断/测试用），**UI 不显示**。题3-12（`document/3-6题.circuit`）的教材估算链为 `U_B=11.63 V → I_EQ=53.85 μA → I_BQ=0.53 μA → I_CQ=53 μA → U_ECQ=11.82 V`，后端精确值 `IBQ=0.487767 μA`、`ICQ=0.0487767 mA`、`U_ECQ=11.8384 V`；工作区使用未舍入、计入基极负载的解核验；若无发射极电阻，或空载分压估算越界但加载解仍在放大区，则改用含负载偏置方程并保留教学舍入。真实截止/饱和与数值失败分开诊断，非放大区不输出小信号增益。见[工作区判定修订](projects/operating-region-validation-20261019.md)。
- **全部题型结果最多两位小数（第七轮）**：BJT/MOS + RC/RL/RLC/MNA/二极管及报告显示统一到同一规范（`Services/TeachingDisplay.cs`、`ScientificNotationFormatter` 默认 precision=2）；`tests/DisplayRoundingChecks` 55 项验证。
- **DC/AC 等效电路分组（第七轮）**：BJT/MOS 推导按直流等效电路（DC，求 Q 点；电解电容极性校核归 DC）→ 交流等效电路（AC，rbe/gm、Av、Ri/Ro、频率）分组展示；BJT 共射/共基 5 步、共集 6 步、MOS 7 步，UI 以分组卡片呈现（`SolutionPhase`/`StepGroups`），分类与判据不因相位改变。
- **BJT 信号源内阻 Rs/电解耦合电容/Aus（第八轮）**：识别 `交流源→(Rs=源与输入耦合电容之间的恰一只电阻)→输入耦合电容→基极/发射极` 的教学拓扑（B07 变体 R6=50 Ω、C3=47 μF 电解），共射四类偏置/共基/共集、NPN/PNP 支持；电解电容按 `+/-` 引脚识别并在 DC 相追加极性核验；给出 `Aus=Av·Ri/(Rs+Ri)`（共基 `Au·ri'/(Rs+ri')`）与 `τ1=(Rs+Ri)C1` 的 fL；实时指标卡与报告**仅在识别到 Rs 时**显示"信号源内阻 Rs""源电压增益 Aus"两行（AC 指标卡为 8 行 Grid，两行不重叠；窗口验收中曾发现 7 行导致的叠字 bug 并修复）；未覆盖电阻诊断列出元件 Id/网名与支持位置。**边界**：只识别"源与输入耦合电容之间恰一只电阻"；MOSFET/JFET 放大器暂不支持 Rs；串联多只/复杂源网络仍明确失败。窗口截图验收见 `tools/ui-verify/evidence/round8/`。
- **保存保留工程名（第八轮修复）**：保存工程时不再把 `ProjectName` 强制改写为文件名（旧缺陷会导致教材示例被改名、生成器 `--verify` 工程名校验失败）；仅"未命名电路工程"回退文件名，其余保留工程标题。D06 工程名已恢复为"双向限幅电路（双管双偏置）"。
- **BJT 小信号参数 r_bb'/r_be(h_ie) 可调（第九轮）**：BJT 元件新增 `BjtRbb`（Ω，0=自动：共射/共集 200 Ω、共基 50 Ω）与 `BjtRbe`（Ω，0=按 Q 点计算；>0 为题目给定 r_be=h_ie，直接采用）并随工程持久化（旧工程缺省 0=自动，格式仍 1.2.0）；共射四类偏置/共基/共集、NPN/PNP 全部读取实际参数，rbe 步骤文案/公式/代入不再硬编码 200/50；第 2 步 `Values` 新增 `rbb_ohm`/`rbe_ohm`/`rbe_source`（computed|given）。UI 两处编辑框：右侧"元件参数检查器（选中 BJT）"与"参数设置 → 三极管模型与信号源特性"，0=自动/0=计算，宏参数与元件双向同步；非法编辑自动归零，后端非法值明确失败。**默认 0 时数值与第八轮完全一致**（B07：Q 点 2.79 V/2.09 mA/20.69 μA/2.07 mA/7.84 V、Av=−34.25、Aus=−32.91、fL=41.7 Hz）。**边界**：给定 r_be 原值不再二次舍入（与 computed 显示值回代语义不同，属有意设计）；rbb/rbe 为每只 BJT 元件参数；**本轮按用户要求不做 ui-capture 窗口验收**（仅控制台回归 + 构建）。
- `examples/circuit-models/` 提供 29 个可直接用"打开工程"打开的原生 `.circuit` 教学电路文件：D01–D07 二极管、B01–B16 BJT、M01–M06 MOSFET；其中 26 个可求解，B12–B14 多级电路可打开/可画但**明确拒绝求解**。索引与参数说明见[电路模型 README](examples/circuit-models/README.md)。`tests/CircuitModelChecks -- --file <path>` 可对任意原生 `.circuit` 做加载→分类→求解诊断并打印全部步骤/Values。教学电路 Q 点同样为教材估算法两位小数显示，精确值仅后端保留。
- BJT 共集电极已支持（Q 点、0<Av<1、高 Ri、低 Ro，NPN/PNP 镜像）；多级放大电路与 NPN+PNP 混合管**仍未实现级联求解**，分类为 `MultiStageAmplifier/Cascade` 并明确拒绝，只预留 `IMultiStageAnalysis` 接口，已按用户要求列入后续项目规划（见[开发规格](projects/spec-first-stage-local-circuit-analysis-20260601.md)）。
- 二极管大信号分析采用**分段线性恒压降模型 + 教材解析公式**（不是 SPICE 瞬态仿真）；MOSFET 增强/耗尽型采用 **Level-1 平方律**、JFET 采用 **Shockley 方程**（忽略体效应/亚阈值/迁移率退化）；可变电阻（三极管）区只报告分区并停止 AC 小信号推导，不输出伪增益；JFET 栅结正偏、IDSS 缺失/非正数明确失败。
- MOS 的 K（A/V²）/λ（1/V）与 JFET 的 IDSS（A）已写入数据模型、工程文件与检查器编辑框（默认 K=1 mA/V²、λ=0，M 组文件为 K=2 mA/V²、λ=0；JFET 必须填写有限正数 IDSS，例如 8 mA 填 `0.008`），随工程保存/读回。
- 通用线性网络 MNA 2.0：R/C/L、独立 DC/AC 电压/电流源、VCVS/VCCS/CCVS/CCCS；DC 与 AC 相量求解、任意端口等效电阻/戴维南等效、10 Hz–10 MHz 扫频与 −3 dB 截止估计；矩阵变量上限 256。非线性/互感/运放不在范围内。截止频率不可测（扫频窗口未覆盖或窗口完全处于阻带）时返回“未检测到（超出扫描范围）”而不是伪值；DC/AC 激励端口一致采用独立电压源优先、同类按 Id 确定性排序。
- 右侧“实时指标”在 MNA 网络下显示节点电位、任意端口戴维南表和 61 点交流频响/扫频数据。
- **公式框为 Win2D 自绘真排版（第七轮）**：`MathFormulaView` 用 `CanvasControl` 绘制由 `Math/` 解析器与排版引擎生成的公式，支持真上下角标（0.72×）、上下叠放分式、根号、bmatrix/pmatrix/cases/array 矩阵、希腊字母、`\mathbf` 与 CJK 回退；按宽度自动换行、内容自适应高度（不再横向滚动裁切），基础字号 16，主题色跟随界面，右上角按钮可复制原始 LaTeX。限制：array 列格式竖线不画、单一超宽原子整体等比缩小、Canvas 文本不可选择、`\overline/\vec/\tilde` 等只保留内容不画装饰、未知命令降级显示；不嵌入 KaTeX/MathML，不是完整 TeX 排版引擎。
- 可导出有限器件范围的 SPICE 文本；没有接入 SPICE 仿真器。
- 原生分析报告导出：Markdown（`*.md`）、LaTeX + 自动 Circuitikz 原理图（`*.tex`）、Word 文档（`*.docx`，手写最小 OOXML）、自包含 HTML（`*.html`，可浏览器打印为 PDF）；报告包含指标、推导步骤、结构化线性网络与 SPICE。PDF 不直接生成，本机无 TeX，`.tex` 未真实编译。

快捷键：`Space/R` 旋转，`Delete/Backspace` 删除，`Esc` 取消，`W` 切换布线，`Ctrl+Z/Y` 撤销/重做，`Ctrl+R` 求解，`Ctrl+O` 打开，`Ctrl+S` 保存，`Ctrl+Shift+S` 另存为。

## 自动化检查

原生工程与 C# 回归测试：

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore
dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj
dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
dotnet run --project tests/WebImportChecks/WebImportChecks.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj -- --roundtrip
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
dotnet run --project tests/SymbolChecks/SymbolChecks.csproj
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "document/3-6题.circuit"
dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj
dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify
```

发布前实测（2026-10-19，0.2.0-preview.1）：主项目 Debug 构建 **0 警告、0 错误**，Release x64 自包含发布 **0 警告、0 错误**（528 个运行文件，发布目录启动冒烟通过）；全量控制台回归 **20 组命令 / 4905 项断言全部通过**——非线性引擎回归 **1606/1606**（含第九轮 `BjtSmallSignalParameterChecks` 与 2026-10-19 工作区/耗尽型/JFET 检查）、`M2SolverChallenger` **166/166**（含第九轮宏同步/默认自动/修改失效/rbb-rbe 保存读回断言与后续新增回归）、29 个教学电路文件校验 **259/259**、公式排版 411/411、显示两位小数 55/55；四个生成器 `--verify` 分别 **7/7、6/6、10/10、6/6（合计 29/29）**；30 题本地基准 **30/30（Accuracy=100.00%）**。既有常规 C# 回归：ChallengerRunner 131、M3 32、M3_4 9、EngineV2 **306**、报告 110、导入 **172**、符号/镜像/节点 230 项，独立数值审计 1359/1359、启动器检查 1/1。**BJT 小信号参数默认 0（自动）时数值与第八轮完全一致**：B07 变体仍为 `U_B=2.79 V → I_EQ=2.09 mA → I_BQ=20.69 μA → I_CQ=2.07 mA → U_CEQ=7.84 V`，`Av=−34.25、Ri=1.23 kΩ、Aus=−32.91、fL≈41.7 Hz`；自定义/给定参数的示例见[第九轮修订记录](projects/technical-code-fixes-20261018-round9.md) 3.3。这些控制台项目不是 WinUI 界面端到端测试，也不是 `dotnet test` 测试项目；**2026-10-19 发布验证为控制台回归 + 构建 + 启动冒烟，未做新的 ui-capture 窗口验收**（第九轮同样未做），r_bb'/r_be、MOS K/λ 与 JFET IDSS 编辑器的真实窗口外观/交互未覆盖；显示两位小数回归只扫描文本。

独立原型检查：

```powershell
npm ci
python -m pip install -r services/engine/requirements.txt -r services/api/requirements.txt
npm run check
```

第一轮 `npm run check` 曾通过：16 项 TypeScript 测试、17 项 Python 引擎测试、16 项 API/Worker 测试、Web 生产构建、3 个 Benchmark 校验、3 份 JSON Schema 导出。**第二轮未重新运行该命令**，因此不能作为第二轮通过证据；此命令也不检查 WinUI。

## 工作区模块

```text
apps/desktop-winui/  主客户端：XAML、控件、MVVM、Math 公式渲染、C# Engine（含 Linear MNA 2.0 与 Nonlinear/MultiStage 教学策略）、工程文件/报告/导入服务
packages/editor/    保留的 React + React Flow + Zustand + KaTeX Web 原型
packages/schema/    Web/Python 链路的 Zod 契约
packages/components/ Web 原型的 18 类元件定义
services/engine/    独立 Python/SymPy 分析引擎
services/api/       独立 FastAPI + SQLite + Worker 服务
benchmarks/examples/ Web/Python 格式的 3 个 JSON 样例；可被原生“打开工程”单向导入，但不是原生工程文件
examples/circuit-models/ 29 个原生教学电路 `.circuit` 文件（D01–D07、B01–B16、M01–M06），索引见该目录 README
tools/CircuitModels.* 声明式电路模型生成器（Core/Diode/BjtBias/BjtAmp/Mos），生成并自检上述 29 个文件
tests/              控制台回归：既有 M2/M3 套件、EngineV2Checks、ReportExportChecks、WebImportChecks、NativeBenchmark30、EngineAuditChecks、NonlinearCircuitChecks、CircuitModelChecks、MathFormulaChecks、DisplayRoundingChecks
ElectricalSymbolLibrary/ 电气符号参考资源，原生符号由 C# 绘制
projects/           当前开发文档、复核记录与历史归档
files/              教材、申报书、开题材料和演示材料
```

独立原型启动入口：`./start-api.bat`（监听 `0.0.0.0:3000`）、`./start-editor.bat`（Vite，5173）。WinUI 主工作台不调用该 API；残留 `CircuitApiService` 的 `127.0.0.1:8000/api/v1/analyze` 与现有 `/api/tasks/submit` 契约并不匹配。

当前改动与未完成能力见[耗尽 MOS/JFET 实现记录](projects/fet-depletion-jfet-strategies-20261019.md)、[工作区校验修订](projects/operating-region-validation-20261019.md)与[第九轮修订记录](projects/technical-code-fixes-20261018-round9.md)；阶段版本 0.2.0-preview.1 的发布校验与已知限制见发布目录 `RELEASE-NOTES.md`。历史测试不能作为当前版本的通过证据；真实窗口已有第七轮（共射模板/题3-12 PNP/MOS 模板）与**第八轮（B07 变体 + 普通模板对照，`tools/ui-verify/evidence/round8/`）**的静态截图级视觉验收；**第九轮与 2026-10-19 发布验证均未做新的 ui-capture**，r_bb'/r_be、MOS K/λ 与 JFET IDSS 编辑器的窗口外观/交互未验收，未做自动化交互/逐像素比对/其它 DPI 验收。
