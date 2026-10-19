# 工程架构复核：WinUI 原生工作区

> 原工程评审：2026-05-29；源码对齐：2026-10-17。早期已通过的是当时的方案评审，不是当前全部功能验收。完整旧文见[修订前副本](archive/2026-10-17-before-winui-doc-sync/wzh-no-git-worktree-eng-review-20260529-021023.md)。

## 1. 架构决策逐项复核

| 原决策 | 当前事实与修订 |
|---|---|
| 类型化 CircuitGraph 唯一事实来源 | WinUI 编辑态采用 C# 元件/引脚/导线；运行时提取 ElectricalNetlist；工程另有 JSON DTO。Zod 只约束原型 |
| 视觉识别 | 后续阶段开发；当前无检测/OCR/照片自动生成拓扑链路 |
| React Flow 网页编辑器为主 UI | 当前主 UI 是 XAML + Win2D + MVVM；React 原型仍保留 |
| Lcapy/SymPy/PySpice 主引擎 | 当前主引擎是 C# 四策略；Python 独立使用 SymPy，未执行 Lcapy/PySpice |
| 多层 Benchmark | 原型保留该结构和 3 个样例；没有 30 题完整评测或原生格式适配 |
| KaTeX + 等效图 + PDF/Word/LaTeX | WinUI 是 Unicode 文本公式、复制和 SPICE 文本；自动等效图与完整报告待实现 |
| TS→JSON Schema→Pydantic | 当前无该自动镜像；Python dataclass 手动解析，原生 DTO 更是独立格式 |
| 数据驱动元件库 | 原生 25 条 C# 符号；原型 18 个 TS 定义；二者无自动生成桥接 |
| 插件式分析 | 原生分类器显式创建四个策略，不是动态加载插件；Python 单独注册六个策略 |
| 分层测试 + 30 题自动评测 | 原生四个控制台测试项目和原型检查已存在；30 题/真实窗口 E2E 未完成 |
| 分层百分比通过线 | 属于原计划目标，当前没有对应全量测量报告，不作为已达成指标 |
| 手机采集 + SQLite 任务 | 属于独立 Web/API 原型；不是 WinUI 主流程 |
| 超时/复杂度/PySpice 降级 | 原生有快照后台、校验、规模限制、15 秒等待上限和取消等待；尚不能强制终止所有同步计算；Python SafeSolver 是线程等待/SymPy 数值回退，不调用 PySpice |

当前架构图、接口与代码位置统一见 [PROJECT.md](../PROJECT.md)，不在此复制另一套可能漂移的模块图。

## 2. 代码组织与契约

主代码在 `apps/desktop-winui/{Controls,Views,ViewModels,Models,Engine,Services}`。旧路径 `apps/web`、`services/jobs`、`services/analysis`、`services/presentation`、`services/eval` 不存在；真实原型是 `packages/editor`、`services/api`、`services/engine`。

画布与 ViewModel 各有选择/导线跟随修改路径；模板、DTO 与网表也各有字段映射。扩展应核对这些接口的一致性，不能仅改一个绘制方法或一个数据模型。

正式求解已接入网表 Validate 与不支持器件拒绝；无效网络返回失败。分类器仍未专门产出 Disconnected/OpenCircuit 类型。策略名称不代表能力完整，详见[引擎报告](analysis-engine-technical-report.md)。

## 3. 原任务状态

| 原任务 | 状态 |
|---|---|
| T1/T2 模型与元件库 | 原型已建，原生独立实现已建，原生 DTO 校验已加入，双端互通未完成 |
| T3 分析与等效图 | 原生有限策略，自动等效图未实现 |
| T4 求解保护 | 两端均有有限保护；原生取消等待不等于强制终止计算 |
| T5 API/Worker | 原型已有，实现独立于桌面求解 |
| T6 编辑器 | WinUI 有基础交互与撤销，完整验收待补；Web 也已有撤销代码 |
| T7 手机上传 | 原型 QR/SSE 入口代码已存在，WinUI 未接入，手机浏览器链路待验收 |
| T8 图像识别 | 后续阶段开发 |
| T9 30 题评测 | 未完成，当前 3 题 |
| T10 公式/报告 | 原生 Unicode/复制，Web 有 HTML/LaTeX/Circuitikz/JSON；原生完整报告和等效图仍缺 |

## 4. 主要风险与后续顺序

1. **错误结果被当作成功**：已补充 Validate、适用范围检查、不支持器件拒绝、奇异性和统一失败展示；继续扩大数值与拓扑验收集。
2. **保存到旧路径**：已清理新建/切换工程状态、补充修改标志/提示/字段序列化与原子替换；继续完善迁移与交互事务。
3. **复杂交互状态不一致**：统一选择/导线更新路径，补框选、分支、快捷键、撤销与实际窗口验证。
4. **双实现能力混写**：文档明确 C# 与 Web/Python 范围；做转换和对照测试后才能承诺互通。
5. **覆盖目标超过证据**：建立原生教材测试集后再计算题目正确率；构建通过不表示工业标准或任意电路验收完成。

本轮已根据[首次审查](technical-document-review-20261017.md)修改本体代码；最新变更和验证结果见[修订记录](technical-code-fixes-20261017.md)，后续验收见[测试计划](wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md)。
