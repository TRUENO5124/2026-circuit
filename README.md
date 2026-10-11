# 电路教学辅助系统 (Circuit Learning Assistant)

面向高校《模拟电子技术基础》课程与电路理论分析的原生桌面辅助系统与本地高精度求解引擎。系统采用微软原生 **Windows 11 WinUI 3** 现代架构，结合 **Direct2D 硬件加速矢量画布** 与 **符号微变等效分析引擎**，提供规范的电气符号库、交互式拓扑绘制、分步教学推导与频响特性分析。

---

## 核心特性与技术栈

### 1. 原生 WinUI 3 桌面端 (`apps/desktop-winui`)
- **平台与框架**：Windows 11 (Windows App SDK 最新版本，WinUI 3，.NET 9 / `net9.0-windows10.0.26100.0`)。
- **设计系统**：遵循 Fluent Design System 规范，采用 Mica（云母）背景材质、原生圆角卡片、标准 WinUI 控件与无级动画。
- **MVVM 架构**：结合 `CommunityToolkit.Mvvm` 库，采用 `[ObservableProperty]` 与 `[RelayCommand]` 等 Source Generators 特性，实现 UI (XAML) 与业务逻辑 (ViewModel) 完全解耦。
- **硬件加速矢量画布**：基于 `Microsoft.Graphics.Win2D` (Direct2D)，支持：
  - 40% ~ 300% 无级平滑滚轮缩放与画布平移（Pan & Zoom）；
  - 柔和点阵栅格（Dot Grid）与 Fluent 蓝电气连接导线；
  - **人机交互操作**：鼠标悬停智能光标反馈（箭头/手型/四向位移）、元器件点击选中（4 控制角点与高亮外框）、拖拽位移、`R` 键旋转 90°、`Delete` 键删除、`Esc` 取消选择；
  - 支持从左侧符号库直接拖拽（Drag & Drop）或点击一键放置元件。
- **数学与排版规范**：
  - 公式展示集成 Cambria Math 排版引擎，支持规范的 LaTeX / MathML 符号推导渲染；
  - 大数值统一采用规范科学计数法格式化（如 $1.20 \times 10^{5}$，彻底消除非规范的 `1.2e5` 字符表示）；
  - 自适应文本换行排版，彻底解决公式与题解文字溢出边框边界问题。

### 2. 仿真算法与本地服务 (`services/engine` & `services/api`)
- **理论分析引擎**：
  - 基于修改节点分析法 (MNA)、戴维南等效定理、晶体管微变等效模型与时间常数法；
  - 覆盖直流静态工作点（Q 点：$I_{BQ}, I_{CQ}, V_{CEQ}$ 及饱和/截止/放大区判断）；
  - 覆盖动态指标：电压放大倍数 $A_v$、输入电阻 $R_i$、输出电阻 $R_o$；
  - 覆盖频率特性：下限截止频率 $f_L$、上限截止频率 $f_H$ 与通频带带宽 $BW$。
- **教学规范推导**：
  - 严格对照国家统编模电教材规范，生成分步骤推导题解（定理方程、数值代入过程、最终结果与底层 MNA 矩阵日志）。
- **本地服务层**：FastAPI + SQLite 异步任务队列与健康检查接口。

---

## 工作区布局与交互设计

软件采用专业的三栏式工程布局架构：

```
┌─────────────────┬──────────────────────────────────┬─────────────────┐
│  矢量电气符号库  │        原理图矢量工作区画布       │ 属性检查与控制台 │
│  (Symbol Lib)   │        (Circuit Canvas)          │  (Inspector)    │
│                 │                                  │                 │
│ • 通用元件族分类 │ • 默认空白工程欢迎向导           │ • 当前选中元件  │
│ • 矢量真实贴图   │ • 模板选择 (经典分压式共射等)    │   参数即时修改  │
│ • 拖拽/点击放置 │ • 交互式元器件 (选中/拖拽/旋转)   │ • Q点与交流指标 │
│ • 模糊检索过滤   │ • 底部可折叠模电推导详解抽屉     │ • 分步公式推导  │
└─────────────────┴──────────────────────────────────┴─────────────────┘
```

1. **左侧栏（280px · 矢量电气符号库）**：
   - 严格还原 Web 端元件族（Component Families）架构，不再硬编码特定电路参数；
   - 涵盖 18 类标准电气符号：无源器件（电阻、电容、电感、变压器）、激励电源（DC/AC 电压源、DC/AC 电流源）、半导体管（NPN/PNP 晶体管、MOSFET、JFET、二极管）、受控源（VCVS、VCCS、CCVS、CCCS）以及系统参考地（GND）；
   - 每个卡片均内嵌真实高精度矢量符号预览（IEC / ANSI 规范图形）。
2. **中间栏（弹性伸缩 · 原理图设计画布）**：
   - **首次启动**：呈现清爽的「空白工作区」状态，用户可选择新建空白电路或载入经典教学工程模板；
   - **工程激活**：显示工程标签页与关闭工程按钮（`✕`），进入全功能交互画布，支持自适应导线拉线与元件实时拖拽排布；底部提供可展开/折叠的模电解题推导抽屉。
3. **右侧栏（420px · 属性检查器与求解控制台）**：
   - **未激活工程**：显示引导卡片与一键载入按钮；
   - **工程激活**：顶部提供「求解与理论分析」操作栏，下方设「⚙️ 参数设置」、「📊 实时指标」与「📘 解题推导」三联 Pivot 选项卡，选中的画布元件与参数面板双向实时绑定。

---

## 环境准备与快速启动

### 系统要求
- **操作系统**：Windows 11（推荐）或 Windows 10 1809+ (Build 17763 及以上)
- **开发工具**：.NET 9 SDK、Python 3.10+、Node.js 18+

### 一键启动桌面客户端 (推荐)
在项目根目录下双击运行：
```powershell
.\start-winui.bat
```
或通过命令行直接运行：
```powershell
dotnet run --project apps/desktop-winui/CircuitAssistant.csproj
```

### 启动本地 Python 分析引擎 (可选服务模式)
```powershell
.\start-api.bat
# 或执行：python -m uvicorn services.api.main:app --host 127.0.0.1 --port 3000
```
- API 地址：`http://localhost:3000`
- 接口文档：`http://localhost:3000/docs`

---

## 目录结构

```text
apps/desktop-winui/       WinUI 3 原生 Windows 桌面应用程序 (C# / .NET 9)
├── Assets/               应用高清图标与启动资源
├── Controls/             Direct2D 原理图画布 (CircuitCanvas) 与矢量预览控件
├── Helpers/              XAML 数据转换器与工具类
├── Models/               电路拓扑实体 (CircuitElement)、引脚、导线与分析结果模型
├── Services/             本地解析推导引擎与 REST API 通信服务
├── ViewModels/           MVVM 视图模型 (AnalysisStudioViewModel)
└── Views/                主工作台界面 (AnalysisStudioPage)

packages/                 跨平台数据模型与 Web 前端原型
├── components/           18 类通用元件族定义与参数约束 (TypeScript)
├── schema/               Zod 数据校验与 JSON Schema 生成契约
└── editor/               React Flow 原理图编辑原型

services/                 核心算法与后台服务
├── engine/               Python 模电求解器 (MNA、微变等效模型、符号推导)
└── api/                  FastAPI 异步任务服务与 SQLite 队列

benchmarks/examples/      教学电路验证用例 (分压式共射放大、MOS 共源等)
ElectricalSymbolLibrary/  GB/T 4728 · IEC 60617 / ANSI 矢量符号规范源
```

---

## 自动化测试与质量验证

在根目录下可执行全套工程验证：

```powershell
# 1. 验证 WinUI 3 桌面端构建 (0 警告，0 错误)
dotnet build apps/desktop-winui/CircuitAssistant.csproj

# 2. 验证 Python 模电求解与微变等效算法单元测试 (33 项全数通过)
pytest services/engine/tests services/api/tests

# 3. 验证 TypeScript 数据契约与元件定义
npm run check
```
