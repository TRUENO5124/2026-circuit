# 电路教学辅助系统 (Circuit Learning Assistant) · 开发交接文档

> **交接对象**：大创项目研发团队后续开发者  
> **交接日期**：2026 年 10 月  
> **项目定位**：面向高校《模拟电子技术基础》与电路理论教学的 Windows 11 原生桌面辅助系统与高精度仿真分析平台  
> **当前源码状态**：WinUI 3 原生工程已全部构建通过（0 警告、0 错误），交互逻辑与矢量符号库已全面对齐专业标准。

---

## 一、 项目背景与架构演进

### 1.1 研发痛点与目标
传统的电路仿真软件（Multisim、LTspice、Proteus 等）侧重于工程级网表求解，对于高校电路与模电教学存在明显痛点：
1. **缺乏教学分步推导**：无法展示戴维南定理、微变等效电路模型构建、时间常数法等教材统编解题步骤；
2. **大数值表示不合教学规范**：常出现 `1.2e-5` 等粗糙写法，缺乏规范的 LaTeX 科学计数法（如 $1.20 \times 10^{-5}$）；
3. **软件体积庞大且臃肿**：学习曲线陡峭。

本项目旨在打造一款轻量、现代化、符合教学大纲标准的原生电路教学辅助系统。

### 1.2 架构演进说明
- **初期版本**：基于 Web 技术栈（React Flow + Vite + Electron/Web 视图）。
- **当前版本**：已全面重构并迁移为微软原生 **Windows 11 WinUI 3 (Windows App SDK / .NET 9)** 应用程序，彻底清除了 Web 端残留，采用 Direct2D 硬件加速与 Fluent Design 现代设计语言。

---

## 二、 技术栈与开发环境配置

### 2.1 依赖环境要求
| 依赖项 | 推荐版本 | 说明 |
| :--- | :--- | :--- |
| **操作系统** | Windows 11 (或 Win10 1809+) | 需支持 Windows App SDK 运行库与 Mica 材质 |
| **.NET SDK** | .NET 9 SDK (9.0.305+) | 运行与编译 WinUI 3 客户端 |
| **IDE** | VS Code / Visual Studio 2022 | 推荐安装 C# Dev Kit 与 Windows App SDK 扩展 |
| **Python** | Python 3.10+ | 用于本地分析求解算法与 FastAPI 任务服务 |

### 2.2 核心 NuGet 包依赖 (`CircuitAssistant.csproj`)
- `Microsoft.WindowsAppSDK` (2.3.9+)：提供原生 Windows 控件、Mica 云母材质、Windowing 与系统主题；
- `Microsoft.Graphics.Win2D` (1.4.0)：Direct2D 硬件加速矢量图形渲染引擎；
- `CommunityToolkit.Mvvm` (8.4.0)：现代 MVVM 架构框架，使用 Source Generators 代码生成特性；
- `Microsoft.Windows.SDK.BuildTools`：原生 Windows 构建工具链。

### 2.3 常用启动与构建指令
在项目根目录下通过 PowerShell 执行：

```powershell
# 1. 一键编译并启动 WinUI 3 桌面端 (最常用)
.\start-winui.bat

# 2. 命令行直接调试运行
dotnet run --project apps/desktop-winui/CircuitAssistant.csproj

# 3. 重新编译原生桌面端
dotnet build apps/desktop-winui/CircuitAssistant.csproj

# 4. 运行 Python 分析引擎单元测试 (33 项自动化测试)
pytest services/engine/tests services/api/tests

# 5. 启动可选的后台 FastAPI 本地服务 (端口 3000)
.\start-api.bat
```

---

## 三、 核心源码目录与关键模块导引

所有的原生客户端代码集中在 `apps/desktop-winui/` 目录下，采用严格解耦的 MVVM 模式：

```text
apps/desktop-winui/
├── MainWindow.xaml / .cs           # 应用主窗体：集成 Mica 云母材质、自定义原生标题栏 (TitleBar)
├── App.xaml / .cs                  # 应用程序生命周期入口、全局未捕获异常监控、转换器资源注册
├── Views/
│   ├── AnalysisStudioPage.xaml     # 核心三栏式工作台 UI (符号库、画布、属性检查器、推导抽屉)
│   └── AnalysisStudioPage.xaml.cs  # 视图事件监听、拖拽交互过渡
├── ViewModels/
│   └── AnalysisStudioViewModel.cs  # 核心 ViewModel：工程生命周期、选中状态机、双向参数同步、理论求解
├── Controls/
│   ├── CircuitCanvas.xaml / .cs    # Direct2D (Win2D) 原理图画布：矩阵坐标变换、交互拾取、拖拽位移、自适应导线
│   ├── CircuitSymbolPreview.xaml/.cs# 符号库卡片专用真实矢量贴图控件 (IEC/ANSI 纯矢量 Path 绘制)
│   └── MathFormulaView.xaml / .cs  # Cambria Math 原生数学公式渲染器 (LaTeX 转 MathML / 富文本排版)
├── Models/
│   ├── CircuitElement.cs           # 拓扑元件模型：坐标 (X,Y)、旋转角、引脚 Pins、精确反向旋转 HitTest 检测
│   ├── CircuitSymbolItem.cs        # 符号库卡片数据实体：元件族分类、标准、默认参数与量纲
│   ├── AnalysisResult.cs           # 仿真求解结果实体 (直流 Q 点、小信号参数、频响带宽、分步题解)
│   └── EngineeringUnit.cs          # 工程量纲单位定义 (电阻 kΩ/MΩ、电容 μF/nF/pF、频率 kHz/MHz 等)
└── Services/
    ├── CircuitApiService.cs        # 教学推导算法层：戴维南等效、微变等效推导、MNA 矩阵生成与本地备用求解器
    └── ScientificNotationFormatter.cs # 科学计数法格式化工具：统一将大数值转为 a × 10^b 规范格式
```

---

## 四、 核心功能机制解析 (重点交接点)

### 4.1 矢量电气符号库设计 (`CircuitSymbolPreview` & `ViewModel`)
- **元件族架构**：摒弃了之前硬编码具体电路实例参数的做法，严格遵循工业级规范，按通用族划分：
  - **无源器件**：电阻器（Resistor）、电容器（Capacitor）、电感器（Inductor）、变压器（Transformer）；
  - **激励电源**：直流独立电压源（DC Voltage）、交流信号源（AC Voltage）、直流电流源、交流电流源；
  - **半导体管**：NPN 晶体管、PNP 晶体管、N 沟道增强型 MOSFET、N 沟道 JFET、整流二极管；
  - **受控源与参考地**：四类线性受控源（VCVS、VCCS、CCVS、CCCS）及参考地（GND）。
- **矢量贴图实现**：[`CircuitSymbolPreview.xaml.cs`](file:///d:/BJTU/2026年大创/apps/desktop-winui/Controls/CircuitSymbolPreview.xaml.cs) 内置几何生成逻辑，通过 `AddPath`、`AddCircle`、`AddPolygon` 动态渲染标准电气图形，保证在任何 DPI 缩放级别下均绝对锐利。

### 4.2 画布交互与人机工程 (`CircuitCanvas.xaml.cs`)
- **坐标变换系统**：
  $$ScreenPos = (WorldPos + Translation) \times Scale$$
  画布通过 `ds.Transform = Matrix3x2.CreateTranslation(_translation) * Matrix3x2.CreateScale(_scale);` 实现无限画布与 40% ~ 300% 缩放平移。
- **精确碰撞检测 (`HitTest`)**：
  当元件存在旋转角（如 $90^\circ, 180^\circ$）时，`CircuitElement.cs` 中的 `HitTest` 使用局部逆变换矩阵：
  $$LocalPos = Transform(WorldPos - Center, Rotate(-\theta))$$
  彻底避免了传统 AABB 包围盒在旋转后误触或无法点中的缺陷。
- **交互逻辑与快捷键**：
  - **光标反馈**：元件上方自动呈现 `Hand`，抓取移动呈现 `SizeAll`，背景拖拽呈现 `Hand`；
  - **旋转 (`R` 键)**：顺时针旋转 90° 并自适应更新元件方向；
  - **删除 (`Delete` 键)**：移除当前选中元件；
  - **取消选中 (`Esc` 键)**：快速清空选中态；
  - **自适应导线拉线 (`DrawCircuitWires`)**：导线根据所连接的元件坐标自适应延展，元件移动时供电轨与地轨保持平滑连通。

### 4.3 双向参数同步与属性检查器
- 用户在画布上点击任意元器件，右侧面板顶部将自动弹出针对该元件的 **「当前选中」参数调整卡片**；
- 在检查器中微调数值或切换量纲单位，ViewModel 将自动更新该元件的实体属性，并在画布上实时反映；
- **注意代码规范**：所有 ViewModel 的属性更新必须赋值给自动生成的属性（首字母大写，如 `VccNumber`），切勿直接赋值给私有字段（如 `_vccNumber`），否则将跳过 PropertyChanged 通知导致 UI 脱靶。

### 4.4 模电规范分步推导与科学计数法排版
- **教学规范推导**：以典型共射电路为例，自动分步输出：
  1. 戴维南基极等效开路电压与等效电阻；
  2. 直流静态工作点 Q ($I_{BQ}, I_{CQ}, V_{CEQ}$) 并校验是否处于放大区；
  3. 晶体管小信号输入电阻 $r_{be} = r_{bb'} + (1+\beta)\frac{U_T}{I_{EQ}}$；
  4. 交流动态参数：电压放大倍数 $A_v$、输入电阻 $R_i$、输出电阻 $R_o$；
  5. 极点时间常数法推导下限截止频率 $f_L$ 与通频带 $BW$。
- **公式排版**：采用 Cambria Math 富文本渲染，外框卡片全部设置 `TextWrapping="Wrap"`，大数值通过 `ScientificNotationFormatter` 统一格式化为 KaTeX / MathML 幂次方形式，彻底杜绝边界文字溢出。

---

## 五、 后续开发建议与代办清单 (To-Do Roadmap)

后续接手的队友可以从以下方向进一步深化系统能力：

1. **原理图正交自动布线 (Orthogonal Auto-Routing)**
   - *当前现状*：针对预设教学模板（如共射电路、共源电路）已实现自适应动态母线拉线；
   - *演进方向*：对于用户自由放置的任意复杂网表拓扑，可引入 A* 寻路或曼哈顿正交寻路算法，实现任意两引脚间的智能避障自动走线。
2. **工程文件持久化保存与加载 (.json / .sch)**
   - *当前现状*：内存中已具备完整的 `CircuitElement` 与引脚网络结构；
   - *演进方向*：在顶部工具栏实现「保存工程」与「打开工程」，使用 `System.Text.Json` 序列化保存画布元件列表、坐标与参数。
3. **扩充教学电路模板库**
   - 在 `ViewModels/AnalysisStudioViewModel.cs` 的 `CreateNewProject` 中扩展更多经典模电实验：
     - 阻容耦合多级放大电路；
     - 差分放大电路（长尾式差分对）；
     - OCL / OTL 互补对称功率放大电路；
     - 负反馈放大电路（电压串联、电流并联等四种组态）。
4. **推导报告导出与分享**
   - 增加将解题推导抽屉内容导出为 PDF 或 Markdown 讲义文档的功能，便于教学打印与作业提交。

---

## 六、 常见问题与避坑指南 (FAQ)

### Q1: 编译报错 `MSB3027: 无法复制 apphost.exe 到 CircuitAssistant.exe，文件被占用`？
* **原因**：上一版的客户端仍在桌面或后台进程运行，占用了可执行文件。
* **解决**：在 PowerShell 执行强制结束命令：
  ```powershell
  Stop-Process -Name CircuitAssistant -Force
  ```
  然后再重新执行 `dotnet build`。

### Q2: 修改了 XAML，启动后样式没有生效或报错？
* **注意**：WinUI 3 XAML 与传统 Web CSS 不同，`HorizontalAlignment` 枚举仅有 `Left`、`Center`、`Right`、`Stretch`，不支持 `SpaceBetween`。若需实现两端对齐，请使用 `Grid` 设置多列或各自指定对齐方式。

### Q3: 右侧参数输入后画布上的数值没有变动？
* **排查**：检查 ViewModel 中是否对私有下划线字段进行了赋值。务必赋值给公共属性以触发 `OnPropertyChanged` 事件。

---

如有任何疑问，可参考项目根目录的 [`README.md`](file:///d:/BJTU/2026年大创/README.md) 以及各模块源码内详尽的行内注释。祝开发顺利！
