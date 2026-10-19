# 电路教学辅助系统 · Circuit Learning Assistant

面向电路与模拟电子技术课程的 Windows 桌面应用，支持绘制电路、编辑器件参数、分析典型教学电路，并查看公式、代入过程和计算结果。项目以可解释的解题步骤为核心，帮助学生从电路图理解直流工作点、交流小信号模型与频率特性。

主客户端采用 **WinUI 3 / XAML / C# / .NET 9**，画布和公式使用 **Win2D** 绘制。网表提取、电路分类、教学求解和报告导出均在桌面进程内完成，日常使用无需启动 Python 服务。

> 当前处于预览阶段。最近的阶段发布为 **0.2.0-preview.1（2026-10-19，Windows x64）**，属于工作区快照，尚无对应 Git 标签。下文按当前源码说明功能；发布包范围与验证结果见[发布记录](projects/release-20261019-0.2.0-preview.1.md)。

## 可以做什么

| 功能 | 当前能力 |
| --- | --- |
| 电路编辑 | 符号检索与放置、参数检查器、导线与分支节点、拖拽、框选、旋转、水平镜像、缩放、撤销与重做 |
| 教学分析 | 典型 RC/RL、RLC、单管 BJT、二极管及单管 MOSFET/JFET 电路；具体范围见下表 |
| 线性网络分析 | 通用复数改进节点分析法（MNA），支持直流稳态、交流相量、受控源、端口戴维南等效与频率扫描 |
| 解题展示 | 电路识别结果、工作点、增益、输入/输出电阻及推导步骤；BJT/FET 按直流与交流等效电路分组 |
| 公式排版 | 原生绘制上下标、分式、根号和矩阵，支持自动换行与复制 LaTeX |
| 工程文件 | 新建、打开、保存与另存为原生 `.circuit` / `.json` 工程；支持 Web/Python `CircuitGraph` JSON 单向导入 |
| 报告导出 | Markdown、LaTeX（含 circuitikz 原理图）、Word `.docx`、HTML |
| 教学示例 | 29 个原生电路文件，覆盖二极管、BJT 和 MOSFET；其中 3 个多级电路用于编辑与展示 |

### 求解范围

| 电路类型 | 已实现 | 主要边界 |
| --- | --- | --- |
| RC / RL / RLC | 基础 RC/RL 暂态、串/并联 RLC 教学分析 | 任意拓扑的时域瞬态仿真尚未实现 |
| 通用线性网络 | R/C/L、独立电压/电流源、四种受控源、直流/交流求解、端口等效与扫频 | MNA 不接受半导体器件、变压器和 VCC 端口；矩阵变量上限 256 |
| 单管 BJT | NPN/PNP，四类偏置、共射、共基、共集（射极跟随器）；支持教材拓扑的信号源内阻与电解输入耦合电容 | 信号源内阻只识别交流源与输入耦合电容之间恰一只电阻的结构；饱和区 Q 点尚未重求解 |
| 二极管 | 半波/桥式整流、电容滤波、限幅、钳位、峰值检波与直流偏置 | 教学策略使用分段线性恒压降模型与解析公式，适用于已识别的教学拓扑 |
| 单管 MOSFET / JFET | 增强型/耗尽型 NMOS、PMOS 与 N/P 沟道 JFET；共源、带源极电阻或旁路电容的共源、共漏、共栅 | 暂不支持信号源串联内阻及源电压增益 Aus；FET 可变电阻区停止交流小信号推导；JFET 须提供有效 IDSS |
| 多级与混合非线性电路 | 可绘制、保存，已预留多级分析接口 | 多管级联及混合非线性网络尚未实现求解，会明确提示不支持 |

这里的 **FET 源极电阻**是管子源极支路中的电阻，与**信号源内阻**含义不同。前者已支持，后者目前仅在 BJT 的指定教学拓扑中支持。

BJT 可调整 β、硅/锗压降、基区体电阻 `r_bb′` 与输入小信号电阻 `r_be (h_ie)`；小信号参数设为 0 时使用默认值或按 Q 点计算，题目给定的 `r_be` 可直接采用。MOS 的 K、λ 与 JFET 的 IDSS 可在检查器中编辑并随工程保存。

教学显示遵循教材估算法：中间计算结果按工程单位最多保留两位小数，并将显示值代入下一步。器件工作区另以未舍入的物理解核验，BJT 检查 BE/BC 结条件，MOS/JFET 检查分段模型与直流负载线。模型假设、方程和适用条件详见[分析引擎技术报告](projects/analysis-engine-technical-report.md)。

## 快速开始

### 使用阶段发布包

已有 `CircuitAssistant-0.2.0-preview.1-win-x64.zip` 时，将整个 ZIP 解压到本地文件夹，双击 `CircuitAssistant.exe` 或包内的 `启动电路教学辅助系统.bat`。

该包自带 .NET 和 Windows App SDK 运行库。请保留解压后的完整目录，单独复制 EXE 无法运行。工作区中的发布产物路径、校验值和已知限制见[发布记录](projects/release-20261019-0.2.0-preview.1.md)；发布文件不随普通源码构建自动生成。

### 从源码运行

开发环境需要 Windows、**.NET 9 SDK**、Windows SDK 及 WinUI 构建工具。项目目标框架为 `net9.0-windows10.0.26100.0`，最低平台声明为 Windows 10 Build 17763；Windows 10 真机兼容性仍待验证。桌面端不依赖 Node.js 或 Python。

在仓库根目录执行：

```powershell
dotnet restore apps/desktop-winui/CircuitAssistant.csproj
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore
dotnet run --project apps/desktop-winui/CircuitAssistant.csproj --no-build
```

也可以使用根目录启动脚本：

```powershell
# 已有 Debug 程序时直接启动；缺少程序时自动构建
.\start-winui.bat

# 修改源码后，显式重新构建并启动
.\start-winui.bat --build
```

启动脚本固定使用 Windows x64 的 Debug 输出，普通启动不会检查源码是否更新。若构建提示程序文件被占用，请先保存工程并正常关闭正在运行的客户端，再重新构建。

### 完成一次教学分析

1. 启动后选择教学模板，或用“打开工程”载入下方的 `.circuit` 示例。
2. 在画布中选中器件，在右侧检查器调整阻值、电源、频率及半导体参数。
3. 点击“求解与理论分析”或按 `Ctrl+R`，查看识别结果、推导步骤与实时指标。
4. 修改参数后重新求解，比较工作点、增益等结果；需要保留时保存工程或导出报告。

求解基于电路快照在后台执行，支持取消并设有 15 秒上限；编辑电路后不会发布旧快照的结果。专用教学策略无法处理的纯线性网络会尝试 MNA 回退，半导体及多级电路保留相应的能力诊断。

## 教学电路示例

示例位于 [`examples/circuit-models/`](examples/circuit-models/README.md)，可直接通过桌面端“打开工程”载入。

| 分组 | 数量 | 可从这些例子开始 |
| --- | --- | --- |
| 二极管 D01–D07 | 7 | [半波整流](examples/circuit-models/D01-half-wave-rectifier.circuit)、[桥式整流与滤波](examples/circuit-models/D04-bridge-rectifier-filtered.circuit)、[双向限幅](examples/circuit-models/D06-double-limiter.circuit) |
| BJT B01–B16 | 16 | [分压偏置](examples/circuit-models/B02-voltage-divider-bias.circuit)、[PNP 放大器](examples/circuit-models/B06-pnp-voltage-divider-bias.circuit)、[带信号源内阻的共射放大器](examples/circuit-models/B07-common-emitter-bypassed.circuit)、[射极跟随器](examples/circuit-models/B10-common-collector.circuit) |
| MOSFET M01–M06 | 6 | [NMOS 共源](examples/circuit-models/M01-nmos-common-source.circuit)、[带源极电阻的共源](examples/circuit-models/M02-nmos-common-source-rs.circuit)、[PMOS 共源](examples/circuit-models/M05-pmos-common-source.circuit)、[共栅](examples/circuit-models/M06-nmos-common-gate.circuit) |

29 个文件中，**26 个可求解**；B12–B14 为多级电路，可打开、编辑与保存，求解会提示尚未支持。M01–M06 均为增强型 MOSFET 示例，耗尽型 MOS/JFET 的支持范围与参数要求见[实现记录](projects/fet-depletion-jfet-strategies-20261019.md)。

## 架构与目录

桌面端的主要数据流为：

```text
XAML / Win2D 工作台
  → 元件与导线模型 → 电路快照
  → NetlistExtractor 网表提取 → TopologyClassifier 分类
  → 教学求解策略 / 纯线性网络 MNA 回退
  → 教学步骤与指标 → 原生公式展示 / 分析报告导出
```

| 目录 | 职责 |
| --- | --- |
| `apps/desktop-winui/` | 主客户端：视图、MVVM、Win2D 控件、器件模型、C# 引擎、工程存储与报告服务 |
| `examples/circuit-models/` | 29 个原生教学电路及参数说明 |
| `tools/CircuitModels.*` | 教学电路模型构建库、生成器及文件一致性校验 |
| `tests/` | 原生 C# 控制台回归、数值审计、公式排版、工程导入导出与原型测试 |
| `packages/editor/` | 独立 React / React Flow / Zustand / KaTeX 编辑器原型 |
| `packages/schema/`、`packages/components/` | Web/Python 原型的数据契约与元件定义 |
| `services/engine/`、`services/api/` | 独立 Python/SymPy 引擎与 FastAPI 服务 |
| `benchmarks/examples/` | Web/Python 格式样例，可单向导入桌面端 |
| `ElectricalSymbolLibrary/` | 电气符号参考资源；桌面端符号由 C# 统一绘制 |
| `projects/` | 技术报告、开发规格、验证记录与历史归档 |
| `files/`、`document/` | 教材、项目材料与课题电路等资料 |

React 与 Python 模块保留用于独立原型开发。`start-all.bat` 启动 Web 原型和 API；桌面端的入口是 `start-winui.bat`。

## 开发验证

原生检查以 C# 控制台程序组织，使用 `dotnet run` 执行。以下命令覆盖常用的引擎、工程文件和报告检查：

```powershell
dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
dotnet run --project tests/WebImportChecks/WebImportChecks.csproj
dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj
dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
```

诊断单个电路文件：

```powershell
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "examples/circuit-models/B07-common-emitter-bypassed.circuit"
```

校验生成器与已保存教学文件的一致性：

```powershell
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify
```

[2026-10-19 清理后验证记录](projects/legacy-code-cleanup-20261019.md)记载：WinUI 构建 0 警告、0 错误，14 组原生回归共 **4876 项**通过，四组生成器另校验 **29 个文件**，启动器检查通过；另完成模板载入、求解、指标查看、保存对话框与关闭提示的真实窗口操作核验。完整套件、覆盖范围与人工验收项见[测试计划](projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md)。这些数字属于对应记录的验证结果，本 README 重写未重新执行上述构建或回归。

### 维护 Web / Python 原型

仅在开发独立原型时准备 Node.js、npm 与 Python 环境：

```powershell
npm ci
python -m pip install -r services/engine/requirements.txt -r services/api/requirements.txt
npm run check
```

使用 `.\start-api.bat` 与 `.\start-editor.bat` 启动，或通过 `.\start-all.bat` 一并启动。`npm run check` 检查 TypeScript、Python 引擎/API、Web 构建、基准样例及 Schema 导出，桌面端需另行构建与验证。

## 已知限制与后续方向

- 求解器面向已覆盖的教学拓扑与通用线性网络。可绘制的符号种类多于可求解范围，修改连接关系后应核对提取结果与诊断信息。
- 多级放大、混合非线性网络、任意拓扑瞬态仿真、大信号与失真分析尚待实现；BJT 饱和工作点重求解、FET 信号源内阻与 Aus 仍待补全。
- 工程采用自有 JSON 格式；Web/Python `CircuitGraph` 为单向导入，暂不支持 KiCad/Altium 等第三方原理图文件。
- 公式排版尚不绘制部分装饰符号和数组竖线，画布公式不能直接选中文字；可通过复制 LaTeX 获取公式源码。原生报告目前提供上述四种格式，PDF 导出仍待实现。
- 现有回归与窗口记录各有覆盖范围；Windows 10 真机、多 DPI、深浅主题、全部参数编辑器和完整自动化交互仍需补充验收。
- 图片识别与 OCR 保留为后续方向，当前优先完善软件本体；详见[视觉模块路线](projects/vision-roadmap.md)。

## 文档与分层技术报告

| 文档 | 阅读内容 |
| --- | --- |
| [展示层技术报告](projects/presentation-layer-technical-report.md) | WinUI / XAML / MVVM、Win2D 画布、工作台交互、参数检查器、公式与结果展示 |
| [数据层技术报告](projects/data-layer-technical-report.md) | 元件与导线模型、工程 JSON、网表契约、持久化、导入与数据校验 |
| [分析引擎技术报告](projects/analysis-engine-technical-report.md) | 网表提取、电路分类、教学策略、MNA、非线性模型、适用范围及后续演进路线 |
| [总体架构与接口](PROJECT.md) | 模块职责、架构关系与接口约定 |
| [开发与交接指南](HANDOVER.md) | 环境准备、修改入口、日常操作、调试与维护注意事项 |
| [教学电路索引](examples/circuit-models/README.md) | 29 个示例的用途、参数、求解预期及生成方式 |
| [测试计划与验收范围](projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md) | 回归命令、验证记录、覆盖边界与待验收项目 |
| [器件工作区校验修订](projects/operating-region-validation-20261019.md) | BJT 加载偏置、BE/BC 判据、MOS/JFET 负载线与异常状态处理 |
| [耗尽型 MOS / JFET 实现记录](projects/fet-depletion-jfet-strategies-20261019.md) | 单管拓扑、模型方程、参数及求解限制 |
| [阶段发布记录](projects/release-20261019-0.2.0-preview.1.md) | 发布构建、产物路径、校验与已知限制 |
| [代码清理与验证记录](projects/legacy-code-cleanup-20261019.md) | 旧代码清理范围、回归结果及真实窗口验收 |
| [开发文档总索引](projects/README.md) | 详细规格、历轮修订、视觉规划与历史归档入口 |

历轮设计与验证记录保留各自的时间基线；查阅时请结合当前源码与后续修订判断功能状态。
