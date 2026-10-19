# 软件本体代码修订记录（第二轮：能力增量）

> 日期：2026-10-17（Asia/Shanghai）  
> 性质：按[第一轮审查报告](technical-document-review-20261017.md)列出的"软件未能实现的能力"继续实现，并在实现后再次审查。  
> 后续：第三轮（2026-10-18）已按用户新要求增加单只 PNP 分析、硅/锗压降参数与标准元件符号，见[第三轮修订记录](technical-code-fixes-20261018-round3.md)；本文保留第二轮历史证据。
> 范围：WinUI 3 / C# 主软件本体、控制台回归工程与现行开发文档；未修改正式申报材料，未提交/推送 Git。  
> 结论：主项目构建 0 警告/0 错误；本轮新增 MNA 2.0、原生四格式报告、Web CircuitGraph 单向导入与 30 题本地基准；7 组常规 C# 自动化回归 811 项全部通过，30 题基准 30/30（round-trip 31/31 网表等价），独立数值审计 1359/1359，并据审计修复了 2 个低严重度问题。真实窗口交互、TeX 编译、Word 实机、外部 SPICE 对照仍未验收。  
> 审查结果：[第二轮审查报告](technical-document-review-20261017-round2.md)。

## 1. 本轮目标与取舍

第一轮修订记录第 6 节列出的剩余能力（任意网络 Req、完整动态/受控源、统一结构化结果、原生报告导出、等效图、30 题覆盖、真实可终止求解、第三方/Web 格式适配）是第二轮的对象。本轮按"先做可数值验证、可用自动化取证的引擎与文件能力，再做 UI 呈现"的顺序实施，原因：

- 求解正确性可被独立审计和 30 题手算基准证伪，收益最高、风险可控；
- 报告/导入是纯数据变换，可脱离窗口做逐字节/逐结构验证；
- 视觉识别、手机上传与完整 TeX 排版仍属后续阶段或外部工具链，本轮不扩张；
- 仍然不做 PNP/FET/非线性重求解、完整 ERC、任意真实拓扑正确率承诺，避免给出看似完整的工业 EDA 结论。

## 2. 通用线性网络 MNA 2.0

| 项目 | 内容 |
|---|---|
| 新增文件 | `apps/desktop-winui/Engine/Linear/LinearNetworkAnalyzer.cs` |
| 结构化契约 | `apps/desktop-winui/Models/LinearAnalysisModels.cs`（NodePotentialInfo / PortEquivalentInfo / AcSweepPointInfo / FrequencyResponseInfo / LinearNetworkInfo） |
| 重写策略 | `apps/desktop-winui/Engine/Strategies/MnaLinearStrategy.cs` |
| 分类文案 | `Engine/TopologyClassifier.cs` 只改 MNA 描述，不放宽分类与 FET/MOS 拒绝 |

能力：

- 器件：电阻、电容（含电解电容）、电感、独立 DC/AC 电压源与电流源、VCVS/VCCS/CCVS/CCCS、Ground；其余器件（二极管/变压器/BJT/MOS/JFET/VccPort）明确拒绝并列出 ID。
- 模式：DC 稳态（C 开路、L 0V 短路并保留电流变量）；AC 相量（C=jωC、L=jωL；AC 源取峰值幅值实数相量；DC 源置零：电压源短路/电流源开路）。相量参考为 cos(ωt)，输出为峰值幅值。
- 受控源符号约定（文件头注释与步骤 5 中也有）：VCVS `v(OUT1)−v(OUT2)=μ(v(IN1)−v(IN2))`；VCCS `I(OUT1→OUT2)=gm(v(IN1)−v(IN2))`；CCVS/CCCS 控制端为 0V 支路（`v(IN1)=v(IN2)`，`Ictrl=IN1→IN2`），CCVS `v(OUT1)−v(OUT2)=r·Ictrl`，CCCS `I(OUT1→OUT2)=β·Ictrl`；增益单位支持 倍/空、S、mS、μS/uS、Ω/kΩ/MΩ、mV/kV、mA/μA，未知单位按 ×1 并写入 BoundaryNote。
- 数值：行均衡 + 部分选主元复数高斯消元，回代后相对残差复核；奇异/病态/非有限返回中文失败原因；矩阵变量上限 256；建网/求解/扫频循环检查 `CancellationToken`。
- 任意端口等效：独立源置零 + 1 A 测试电流求 `Req`（可为 +∞）；`Voc` 为独立源保留时的 DC 开路电压；AC-only 网络可能无 DC 解。
- 频响：10 Hz–10 MHz、每十倍频程 10 点（固定 61 点），−3 dB 截止对数插值估计；输出端口为启发式（排除激励端口后取相量幅值最大的非地节点），不确定时 `FrequencyResponse=null` 并写 BoundaryNote。截止检测使用"窗口内最大值 ∪ 窗口外探针"的通带参考：窗口完全处于阻带或交叉点在窗口外时，三个截止/带宽字段返回 null、Display 写"未检测到（超出扫描范围）"、BoundaryNote 说明"扫频窗口未覆盖真实 −3 dB 截止频率，截止频率与带宽估计不可用"，不再输出伪截止值（该问题由独立审计发现后修复）。
- 激励端口：DC 与 AC 一致，**独立电压源优先**，仅当无电压源时才使用电流源；同类源按元件 Id 确定性排序，不再依赖元件命名顺序（该问题由独立审计发现后修复）。
- 策略输出：固定 5 步（矩阵方程/节点电位与支路电流/任意端口戴维南-诺顿表/AC 相量与频响/适用范围），填充 `LinearNetwork` 与 `AcParameters`，保留旧 DC `RawMnaLog` 文本格式；`ToAnalysisResult()` 映射节点电位、端口等效、扫频点与频响摘要到 UI。

未实现（明确边界）：非线性器件、互感、运放、瞬态/时域仿真、任意拓扑自动建模、矩阵变量 256 以上的电路。

## 3. 求解调度、回退与取消

- `ICircuitStrategy.SolveAsync` 新增 `CancellationToken cancellationToken = default`；五个策略实现均在入口检查令牌，MNA 2.0 在建网/求解/扫频循环中检查。
- 工作台求解线程把令牌传入策略；取消按钮与 15 秒超时会取消令牌，迟到结果不发布；仍为协作式取消，不是进程级强制终止。
- 专用教学策略（RC/RL、RLC、BJT）明确拒绝时，若网络为线性，工作台回退到 `MnaLinearStrategy`，并在摘要中注明"专用教学策略不适用（原因），已回退到通用线性网络 MNA 2.0 直流稳态/交流相量分析"。回退不改变验证/规模上限，也不用于伪装 BJT/FET/二极管支持。
- 最近一次求解缓存（`PedagogicalSolution` + 提取后的 `ElectricalNetlist`）供报告导出；编辑电路、新建/打开/关闭工程、超时都会清空缓存，避免报告与界面结果不一致。
- 结果缓存不参与撤销/重做事务，不保存到工程文件。

## 4. 原生分析报告导出

| 项目 | 内容 |
|---|---|
| 服务 | `apps/desktop-winui/Services/ReportExportService.cs`（纯内存，不写文件、无 WinUI 依赖） |
| 文件选择器 | `Services/ProjectManagerService.PickExportReportFileAsync` |
| UI 入口 | 工程管理菜单 → "导出分步分析报告"（LaTeX+Circuitikz / Word / Markdown / HTML） |
| 输入契约 | `AnalysisReportContent { ProjectName, ProjectFilePath, GeneratedAt, ElectricalNetlist Netlist, PedagogicalSolution Solution }` |

四种格式由同一报告模型渲染，语义一致：报告信息表（项目名/时间/软件/电路标题/拓扑/成功失败/工程文件）→ 摘要或失败原因 → 适用范围与边界 → 关键指标（Q 点 4 项与 AC 6 项，空值"—"）→ 原理图（LaTeX 为自动 circuitikz，其余格式为节点映射与覆盖说明）→ 推导步骤（标题/方法/解释/公式/代入/结果）→ 结构化线性网络（节点电位、任意端口戴维南表、频响摘要与全部扫频点、BoundaryNote）→ SPICE 网表 → 导出说明。

- Markdown：`$$...$$` 公式、管道表格、```spice 代码块。
- LaTeX：`ctexart + amsmath/amssymb + circuitikz + geometry`；公式原文进入 `\[...\]`；SPICE 用 verbatim；由 `ElectricalNetlist.Branches` 自动生成原理图（gnd=(0,0)、非地网确定性网格 n1..nN、R/C/L/D/V/I 与 vcvs/vccs/ccvs/cccs，标签特殊字符转义，无法绘制器件跳过并注明）。
- Word `.docx`：手写最小 OOXML（`ZipArchive + XDocument`），5 个 entry（Content_Types、_rels、word/document.xml、word/_rels、styles），段落 + 真实表格 + Heading 样式；XML 转义与中文 UTF-8。
- HTML：自包含、内联 CSS、无外部资源，`<code>/<pre>` 展示公式与 SPICE，文档注明浏览器打印可转 PDF。

边界：不直接生成 PDF；本机没有 TeX，`.tex` 未真实编译；DOCX 未在 Word 实机打开（用 zipfile/XML/python-docx 验证可解析）；circuitikz 目前不绘制 BJT/MOS/变压器/VccPort，以及当前 `NetlistExtractor` 不为 4 端受控源生成二端 Branch，因此受控源也会列入"原理图未覆盖器件"（绘制键已实现，待提取器补二端映射或后续增加四端绘制）。

## 5. Web/Python CircuitGraph 单向导入

| 项目 | 内容 |
|---|---|
| 适配器 | `apps/desktop-winui/Services/WebCircuitGraphImporter.cs`（System.Text.Json + Models，无 WinRT） |
| 入口 | "打开工程 / 导入 Web CircuitGraph"：读取 `.circuit/.json` 后自动探测 |
| 支持形状 | benchmark 包装 `{... circuitGraph: {...}}` 或裸 `{nodes, components, connections}` |

- `definitionId` 与端口映射到原生 `ElementType`/`CircuitPin`；参数按 unit token 统一到 SI，再写用户友好 `DisplayNumber/UnitSymbol/FrequencyHz`；未知定义、无法映射端口、缺少必需参数汇总抛 `InvalidDataException`。
- 原图无坐标：确定性格点自动布局（GND 在 (400,480)，其余节点按声明顺序网格，元件放前两个节点中点），每条 connection 生成一根开放端导线，由网表提取器的端点几何合并还原节点。
- 导入结果为普通 `CircuitProjectData`（与手工工程同构），置为未保存、不绑定原 JSON 路径，提示另存为原生工程；`Validate()` 自检失败会拒绝导入。
- 已知限制：四端 MOSFET 的 bulk 折叠到 source；`imageRefs`/ports 角色/metadata 忽略；自动布局不追求美观；BJT benchmark seed 参数非物理（见第 7 节）。

## 6. 结构化结果与 UI

- `AnalysisResult` 新增 `LinearNetwork`、`FrequencyResponse`、`NodePotentials`、`PortEquivalents`、`AcSweepPoints`，由 `PedagogicalSolution.ToAnalysisResult()` 填充；专用教学策略不填这些集合。
- 右栏"实时指标"新增两张卡：MNA 2.0 结构化结果（模型摘要、节点直流电位、任意端口戴维南表）与交流频响/扫频（工作频率增益、dB、相位、上下限截止/BW、61 点列表、边界说明），仅在有结构化数据时显示。
- `PortEquivalentInfo` 增加 `DetailDisplay` 便捷显示（Req + 可选 Voc）；`Req=∞` 由 `ResistanceDisplay` 处理，数值绑定不应直接显示 Infinity。
- 顶部工程菜单：报告导出子菜单；"打开工程"文案改为"打开工程 / 导入 Web CircuitGraph"；求解回退与取消状态写入状态栏/摘要。

## 7. 验证结果与证据

均在本工作区、当前工作区源码上执行；命令见第 10 节。

| 检查 | 本轮结果 | 覆盖 |
|---|---|---|
| WinUI 主项目构建（隔离 OutDir） | 0 警告、0 错误 | XAML、MVVM 生成器、全部 C#（含新分析器/报告/导入/UI） |
| M2SolverChallenger | 152/152 | 第一轮既有求解/格式/文件/ViewModel 回归，未弱化 |
| ChallengerRunner | 131/131 | 模板、网表、分类、WireTap、求解 |
| M3AdversarialStress | 32/32 | 位移/母线/父子线不变量及 80px 场景 |
| M3_4EmpiricalStress | 9/9 | 母线/80px 及 132 个位移组合 |
| EngineV2Checks | 209/209 | MNA 2.0 DC/AC、四种受控源、动态元件 DC、Req/戴维南、扫频截止、错误/取消/结构化结果，以及审计修复后的越界截止 + 激励源优先级回归（33 项） |
| ReportExportChecks | 110/110 | 四格式内容/转义/OOXML 结构/失败与空数据降级/文件名清洗 |
| WebImportChecks | 168/168 | 3 个 benchmark 导入、拓扑还原、单位/端口映射、BJT 可解变体、错误路径 |
| NativeBenchmark30 | 30/30（100%）+ round-trip 30/30 | 一阶 6、二阶 6、BJT 8、MNA 10；每题独立手算，1% 容差（扫频截止 2%）；网表带拓扑自检；`--roundtrip` 30/30（31/31 网表，1517 字段 ≤1e-9 等价） |
| EngineAuditChecks（独立审计） | 1359/1359 | 独立参照求解器随机网络交叉验证、KCL 残差、戴维南/相量/扫频/边界拒绝/结构化一致性/取消；本轮据此修复 2 个低严重度问题 |

C# 常规回归合计 **811 项自动化断言**（152+131+32+9+209+110+168）；独立审计另有 1359 项，30 题基准（每题多子断言）与 round-trip 31 对网表未并入断言总数。控制台回归不等于任意拓扑或窗口 E2E。

外部校验（报告）：用环境变量 `REPORT_EXPORT_ARTIFACTS_DIR` 落盘后由 Python `zipfile + ElementTree + python-docx` 验证：DOCX 5 个 XML entry well-formed、python-docx 可打开（53 段落/7 表格/Heading 样式）、document.xml 转义与中文正确；TeX 环境配对/花括号平衡/特殊字符转义；HTML 自包含无外链。样例产物在临时目录，未提交到仓库。

### 7.1 独立数值审计（EngineAuditChecks）

`tests/EngineAuditChecks` 由独立审计方实现，自带一套从零编写的复数 MNA 参考求解器（独立装置模型 + 缩放选主元高斯消元 + 反向误差残差门），期望值从不调用被测 `LinearNetworkAnalyzer` 获取；随机种子固定可复现（Base=20261017）。最终结果 `Total=1359 Passed=1359 Failed=0`（exit 0，`-t:Rebuild` 0 警告/0 错误）。

逐段覆盖：S0 参考求解器自检 19；S1 DC 解析基准 + 逐节点 KCL 128（含四种受控源与受控源双环）；S2 48 个随机阻性网络交叉验证 436（3–8 节点、0–2 独立源、0–2 受控源、R∈1e-3..1e7 Ω；DC 电位/KCL/每网络 4 端口 Req/Req 对称性/戴维南 Voc+Req/随机负载公式）；S3 任意端口 Req/戴维南/串并联解析/驱动点阻抗 52；S4 AC 相量交叉验证 207（RC/RL/RLC 串并联、四种受控源、混合源、极端频率、12 个随机 AC 网络×3 频点）；S5 61 点扫频与截止 87（含越界截止严格断言与窗口内对照组）；S6 边界/奇异/拒绝 135；S7 取消与规模 17；S8 结构化契约 235（NodePotentials/PortEquivalents/61 点频响/∞ 显示/失败不伪造/ToAnalysisResult 映射）；S9 NetlistExtractor 端到端 43。另做灵敏度自检：在引擎源码副本中翻转 VCCS 跨导符号后同一 harness 立即大量失败，证明断言非空转（副本已删除，仓库源码未改）。

审计发现并已修复的 2 个低严重度问题：

1. **扫频窗口外的截止频率误报**：RC 低通 fc≈0.1 Hz 被 10 Hz–10 MHz 窗口扫描时曾返回 `UpperCutoffHz≈14.13 Hz` 且提示文案与 10 Hz 处 −40 dB 矛盾。修复后使用"窗口内最大值 ∪ 窗口外探针"的通带参考；窗口完全处于阻带或交叉点在窗口外时截止/带宽字段为 null、Display 为"未检测到（超出扫描范围）"、BoundaryNote 说明原因。EngineV2Checks 与审计均含越界回归和"窗口内正常检出截止"对照组。
2. **DC 激励源端口依赖元件 Id 顺序**：混合独立源网络曾按 Id 序取到电流源并报告"无电压增益定义"，把电压源改名后结果变化。修复后 DC 与 AC 一致、电压源优先、同类按 Id 确定性排序；纯电流源网络保留电流方向语义。EngineV2Checks 8.3–8.5 与审计 8.3–8.5 覆盖两种 Id 顺序文本一致、纯电流源回退、AC 电压源优先。

两处修复未改变 `Models/LinearAnalysisModels.cs` 契约字段与类型；仅收紧越界场景语义（截止字段可为 null）与激励端口选择规则。

审计残余风险（未覆盖，如实记录）：NetlistExtractor 几何提取只审计 3 种手工布局（未 fuzz 随机布线/T 型 tap/GND 压线）；取消只验证"已取消 token"；极端参数 >1e7/1e12 与稠密 256 变量受控源网络条件数未系统验证；UI 层 512 元件限制属 WinUI ViewModel，审计的是引擎层变量上限；输出端口启发式在教学语义上未验证；随机网络跳过了参考判定为奇异的候选（奇异由 S6 定向覆盖）。

### 7.2 30 题基准的拓扑正确性

`tests/NativeBenchmark30` 的每个 fixture 在送引擎前强制执行 `VerifyTopology()`：引脚完整性（每引脚恰绑 1 网）、元件不短路（两端异网；BJT B/C/E 互异；受控源 IN1≠IN2、OUT1≠OUT2，允许共用 GND）、生产 `Validate()` 无 Error、支路/晶体管表与元件表一致、参考地唯一 NetId=0、`ConnectedBranches` 计数一致。另提供 `--topology` 审计模式逐题打印 net→引脚映射与元件参数，已与教材电路人工核对；负向验证（重复绑定引脚、改错期望值）分别得到 exit=1，恢复后 30/30。

`--roundtrip` 模式进一步证明基准电路可经生产导入链路回到原生工程：把 30 题 fixture 导出为 Web/Python `CircuitGraph` JSON，再用 `WebCircuitGraphImporter.Import` → `CircuitProjectData.ToModel()` → `NetlistExtractor` 还原，最后重新分类/求解并与 fixture 及手算期望比对。结果 RoundTripTotal=30 Passed=30 Failed=0、Netlists=31/31（M08 拆成电容/电感两个子电路）、NegativeChecks=2/2，31 对网表共 1517 个比较字段在 1e-9 相对容差内等价；负向注入（重复端口、几何重叠）均被检出。生成的可读 JSON 位于 `tests/NativeBenchmark30/generated/circuitgraph/`（31 个文件，可用 `--roundtrip` 重新生成），可直接用应用“打开工程 / 导入 Web CircuitGraph”打开为原生工程。这证明"基准 fixture → CircuitGraph JSON → 原生导入 → 求解"链路在教材场景下可用，但不代表自动布局对任意拓扑都不会产生几何吸附错误（见第 7.2 与审查报告残余风险）。

### 7.3 本轮一次工作区事故与恢复（透明记录）

本轮在批量脚本中误用了 Windows PowerShell 5.1 的 ANSI 默认编码，导致 5 个已修改 C# 文件（FirstOrderRcStrategy/SecondOrderRlcStrategy/BjtAmplifierStrategy/MnaLinearStrategy/TopologyClassifier）出现 UTF-8 乱码。恢复方式：使用前一会话在系统临时目录留下的修改前 ZIP 快照，加上当次会话记录的全部 apply_patch 与 Python 改写脚本按顺序回放，并对乱码字节做 DLL 元数据字符串精确比对。恢复后：

- 新编译程序集的用户字符串集合与损坏前程序集比对：损坏前 1835 条字符串缺失 **0** 条，仅新增本轮契约字段名 5 条；
- 5 个文件行数与损坏前一致，构建 0 警告/0 错误；
- 既有 324 项回归再次全部通过。

未覆盖用户其它文件；恢复脚本与证据目录已从工作区清理。

## 8. 文档同步

已同步：`README.md`、`PROJECT.md`、`HANDOVER.md`、`CLAUDE.md`、`专业名词解释.md`、三层 technical report、开发规格、测试计划、`projects/README.md`；第一轮审查/修订记录保留原文并添加第二轮链接；新增本记录与[第二轮审查报告](technical-document-review-20261017-round2.md)。未改写 `ORIGINAL_REQUEST.md`、申报/开题/PPT/PDF、第三方许可与历史归档。

## 9. 剩余边界（未实现/未验收）

- 器件/算法：PNP、多级 BJT、FET/MOS/JFET、二极管/变压器/非线性重求解；互感/运放；瞬态仿真与任意拓扑自动建模。
- 求解工程化：完整 ERC；真正可强制终止的长任务；任意真实拓扑正确率与外部 SPICE 数值对照。
- 结果与展示：完整数学排版（KaTeX/MathML 级）、应用内自动等效电路图、PDF 直接生成；真实任意端口选择 UI。
- 报告：`.tex` 未编译、DOCX 未 Word 实机打开、PDF 仅间接路径。
- 导入：仅 Web→原生单向；bulk 折叠；非物理 seed 需真实参数；无第三方 `.sch`。
- 验收：真实窗口交互、文件选择器全流程、高 DPI/主题/焦点、手机扫码、视觉识别（后续阶段）。

## 10. 复现命令

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj --verbosity quiet
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj --verbosity quiet
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj --verbosity quiet
dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj --verbosity quiet
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
dotnet run --project tests/WebImportChecks/WebImportChecks.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj -- --roundtrip
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
```

`dotnet build` 的 `-p:OutDir` 用于避免覆盖用户正在运行的 `bin/Debug` 程序；本轮未结束用户进程。以上控制台工程都不是 `dotnet test` 项目。
