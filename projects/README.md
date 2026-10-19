# 开发文档索引

> 当前实现基线：2026-10-19 工作区（含耗尽型 MOS/JFET 扩展、元器件工作区校验修订与技术路线文档），包含尚未提交的 WinUI 代码。主软件采用 WinUI 3 / XAML / C#；HTML/React 与 Python 服务保留为独立原型。

> 第二轮（2026-10-17）能力增量已落地：MNA 2.0 通用线性网络求解、四格式原生分析报告、Web CircuitGraph 单向导入、30 题本地基准。第三轮（2026-10-18）新增 PNP 教学分析、硅/锗压降、按国际标准统一的元件符号矢量库与参数语义；第四轮修正电解电容负极曲板方向，并按 GB/T 4728 / IEC 60617 重绘四类 MOSFET 与两类 JFET；第五轮把 PNP 默认符号改为发射极在上/集电极在下，并为全部元件加入 ALT+X 水平镜像与节点（导线/WireTap/焊点）跟随。**第六轮（2026-10-18）新增二极管非线性教学求解（9 类子类型）、MOSFET 平方律教学求解（共源/共漏/共栅，NMOS/PMOS）、BJT 共集电极、多级放大预留接口与明确拒绝，并生成 29 个原生 `.circuit` 教学电路文件（索引见 [examples/circuit-models/README.md](../examples/circuit-models/README.md)）；MOS 的 K/λ 已持久化但检查器不提供编辑框。第七轮（2026-10-18）把显示层统一为教材估算法（两位小数舍入并回代、后端精确值 `Exact*` 不显示）、所有题型结果显示两位小数、BJT/MOS 推导按 DC/AC 等效电路分组，并用 Win2D 自绘公式框替换 Unicode 文本转写。第八轮（2026-10-18）支持 BJT 信号源内阻 Rs/电解输入耦合电容/Aus、修复保存工程名缺陷、同步 B07 教学变体与 M2 测试维护，并完成 B07 变体 + 普通模板的真实窗口静态截图验收（含 AC 指标卡 Rs/Aus 两行叠字 bug 修复）。第九轮（2026-10-18）让 BJT 小信号参数 r_bb'/r_be(h_ie) 可编辑并持久化（0=自动；题目给定 r_be 直接采用；检查器与参数设置两处编辑框；Nonlinear 913、M2 160），按用户要求不做 ui-capture 窗口验收。** 最新结果见[第九轮修订记录](technical-code-fixes-20261018-round9.md)、[第八轮修订记录](technical-code-fixes-20261018-round8.md)、[第七轮修订记录](technical-code-fixes-20261018-round7.md)、[第六轮修订记录](technical-code-fixes-20261018-round6.md)、[第五轮修订记录](technical-code-fixes-20261018-round5.md)、[第四轮修订记录](technical-code-fixes-20261018-round4.md)、[第三轮修订记录](technical-code-fixes-20261018-round3.md)、[第二轮修订记录](technical-code-fixes-20261017-round2.md)与[第二轮审查报告](technical-document-review-20261017-round2.md)；第一轮 280/324 项结果保留为历史证据。


> 2026-10-19 更新：耗尽型 MOS 与 N/P JFET 单管教学求解已补全，检查器现可编辑 MOS K、JFET IDSS 与 λ，详见[实现记录](fet-depletion-jfet-strategies-20261019.md)。上方历轮说明是各轮当时的范围与测试记录。分析引擎报告新增[当前混合分析路线](analysis-engine-technical-report.md#11-技术路线一当前混合分析架构已实现)和[多管/正负双电源演进路线](analysis-engine-technical-report.md#12-技术路线二多管与正负双电源演进待实现)；后者为方案，尚未实现。

## 现行文档

| 文档 | 用途 |
|---|---|
| [项目 README](../README.md) | 环境、构建、启动、能力摘要及自动化命令 |
| [PROJECT](../PROJECT.md) | 架构图、组件职责、接口与支持矩阵 |
| [开发交接](../HANDOVER.md) | 修改入口、操作、保存/导入/导出与维护注意点 |
| [展示层](presentation-layer-technical-report.md) | XAML/MVVM、Win2D、交互、Win2D 公式排版、DC/AC 分组卡片、两位小数显示、Rs/Aus 指标行、BJT 小信号参数两处编辑框、报告/导入边界 |
| [数据层](data-layer-technical-report.md) | 编辑模型、原生引脚、工程 JSON、网表、校验、导入适配器、`QPointInfo.Exact*`/`StepGroups`/`AcParametersInfo` Rs/Aus、`BjtRbb`/`BjtRbe` 字段、保存命名语义与原型格式差异 |
| [分析引擎](analysis-engine-technical-report.md) | 提取/分类/教学策略（RC/RL、RLC、BJT 共射/共基/共集/Rs+电解电容/r_bb'·r_be 可调、二极管、MOSFET、多级预留）、教材估算法显示口径、DC/AC 相位、Aus、MNA 2.0、近似与不支持范围、Python 原型边界；第 11 节当前混合架构、第 12 节多管/双电源目标路线 |
| [产品设计](wzh-no-git-worktree-design-20260527-172528.md) | 当前工作流与历史产品愿景的变化 |
| [工程复核](wzh-no-git-worktree-eng-review-20260529-021023.md) | 原十三项架构决策、任务与当前源码对照 |
| [开发规格](spec-first-stage-local-circuit-analysis-20260601.md) | I1–I9、M1–M4 的当前状态及后续待办（第九轮已同步 r_bb'/r_be 参数与无窗口验收说明） |
| [测试计划](wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md) | 历轮已测结果和未执行的窗口/边界验收 |
| [第二轮本体修订记录](technical-code-fixes-20261017-round2.md) | 第二轮能力增量、验证结果、独立审计与剩余边界 |
| [第三轮本体修订记录](technical-code-fixes-20261018-round3.md) | PNP 教学分析、硅/锗压降、标准元件符号、参数语义与多级/混合非线性后续规划 |
| [第四轮本体修订记录](technical-code-fixes-20261018-round4.md) | 电解电容负极曲板方向修正、六类 FET 标准符号重绘与 SymbolChecks 180 项验证 |
| [第五轮本体修订记录](technical-code-fixes-20261018-round5.md) | PNP 默认发射极在上、全部元件 ALT+X 镜像、导线/WireTap/焊点节点跟随与旧 PNP 工程迁移 |
| [第六轮本体修订记录](technical-code-fixes-20261018-round6.md) | 二极管/MOSFET 非线性教学求解、BJT 共集电极、多级预留接口、29 个原生 `.circuit` 教学文件与最终回归数字 |
| [第七轮本体修订记录](technical-code-fixes-20261018-round7.md) | 教材估算法显示与两位小数、DC/AC 等效电路分组、Win2D 真排版公式框、全部题型两位小数、最终回归数字与已知边界 |
| [第八轮本体修订记录](technical-code-fixes-20261018-round8.md) | BJT 信号源内阻 Rs/电解耦合电容/Aus、B07 教学变体、保存保留工程名、M2 Suite 7 维护、B07 窗口截图验收与 AC 卡 Rs/Aus 叠字修复、最终回归数字 |
| [第九轮本体修订记录](technical-code-fixes-20261018-round9.md) | BJT 小信号参数 r_bb'/r_be(h_ie) 可调（数据层/引擎/UI/测试）、0=自动与题目给定语义、两处编辑框、913/160 回归数字与无窗口验收说明 |
| [元器件工作区校验修订](operating-region-validation-20261019.md) | BJT Re=0/软分压器与 BE/BC 判据，MOS/JFET 分段负载线，二极管/FET Invalid；保留教学舍入；1581 项非线性回归与构建结果 |
| [耗尽 MOS/JFET 实现记录](fet-depletion-jfet-strategies-20261019.md) | 全类型单管 FET、IDSS/K/λ 参数、Shockley 方程与栅结校验、1534/306/55 验证记录；双电源与多管仍待实现 |
| [29 个教学电路模型文件索引](../examples/circuit-models/README.md) | D01–D07/B01–B16/M01–M06 文件清单、打开方式、可解/仅画/多级拒绝、MOS K/λ 参数与已知边界 |
| [0.2.0-preview.1 发布记录](release-20261019-0.2.0-preview.1.md) | 当前发布产物（发布文件夹 / ZIP / SHA256 / 快捷方式）、发布构建口径（Release x64 自包含、关闭裁剪、WinAppSDK 自包含）、验证摘要与 0.1.0-preview.1 的替换记录 |
| [第二轮审查报告](technical-document-review-20261017-round2.md) | 第二轮实现后的能力核对、证据与遗留风险 |
| [第一轮本体代码修订记录](technical-code-fixes-20261017.md) | 第一轮已修改的缺陷与本轮验证结果（历史） |
| [第一轮审查报告](technical-document-review-20261017.md) | 原问题、文档对照与代码修改前的历史证据 |
| [后续视觉安排](vision-roadmap.md) | 视觉保留为后续开发，当前优先本体 |
| [专业名词](../专业名词解释.md) | 电路与现行软件架构术语，区分历史视觉研究 |
| [项目工作约定](../CLAUDE.md) | 主客户端、验证入口和文档维护规则 |

## 后续阶段方案、历史与原始输入

| 材料 | 当前用途 |
|---|---|
| [修订前完整文档归档](archive/2026-10-17-before-winui-doc-sync/README.md) | 保存此次替换前的三层论证、设计、规格和复核，不作为当前实现指南 |
| [2026-07 复核入口](technical-document-review-20260716.md) | 连接历史报告与本轮报告 |
| [YOLO/PaddleOCR 报告](yolo-paddleocr-circuit-recognition-technical-report.md) | 后续视觉开发的方案参考；不代表当前识别代码或精度结果 |
| [教材采集交接](textbook-image-collection-handoff-20260601.md) | 后续视觉阶段的数据采集参考，当前不启动采集 |
| [原始重构要求](../ORIGINAL_REQUEST.md) | 原始需求与验收目标，保留原意；不是已完成清单 |
| [最早工程评审](archive/wzh-no-git-worktree-eng-review-20260529-021023-original.md) | 历史原始评审 |
| [最早测试计划](archive/wzh-no-git-worktree-eng-review-test-plan-20260529-021023-original.md) | 历史原始测试目标 |
| `tasks-eng-review-20260529-021023.jsonl` | 当时任务记录，保留历史状态，不作为当前待办来源 |
| `files/`、`projects/archive/` 中申报书/开题/PPT/PDF | 正式申报、教学资料及历史备份，未作为当前开发手册重写 |
| `ElectricalSymbolLibrary/README.md`、`packages/editor/public/symbols/THIRD_PARTY_NOTICES.md` | 第三方资源说明和许可，保留原文；原生符号由 C# 绘制 |

内部 `.agents/teamwork` 与 `.omo` 等运行记录也不属于现行开发说明，未改写历史执行证据。

## 维护准则

1. 原生能力以当前 C# 执行路径为证据；Web/Python 原型单列说明。
2. 区分“源码已有实现”“自动化已测”“窗口已验收”“待开发”，避免互相替代。
3. 构建/测试数字注明日期；浮动 NuGet 引用不写成固定版本。
4. 数据格式变更同步模型/DTO/转换器/校验与测试，不假设 `.json` 或 `.sch` 后缀意味着兼容。
5. 发现文档所述功能缺失时更新边界和待办，不为了符合文档而在文档任务中修改软件。
