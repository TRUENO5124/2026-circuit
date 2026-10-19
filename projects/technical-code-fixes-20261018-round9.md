# 软件本体代码修订记录（第九轮：BJT 小信号参数 r_bb'/r_be(h_ie) 可调——数据层、引擎、UI 与测试）

> 日期：2026-10-18（Asia/Shanghai）
> 性质：按用户第四轮反馈（第九轮）让 BJT 的基区体电阻 r_bb' 与给定 r_be/h_ie 成为可编辑、可持久化的教学参数。本轮只改代码/测试/文档，未提交或推送 Git。
> 用户反馈（原文要点）：模电题目经常直接给定三极管的 r_bb'（基区体电阻）与 r_be（h_ie）等小信号参数；旧软件把 r_bb' 硬编码为共射/共集 200 Ω、共基 50 Ω，r_be 只能由 Q 点公式算出，无法按题目给定值调节。希望这两类参数能在元件参数中编辑并参与推导。
> 旧实现问题：① `BjtAmplifierStrategy` 内联常量 200/50 Ω，rbe/hie 步骤文案与代入式也写死该常量；② 元件/DTO 没有 r_bb'/r_be 字段，无法保存与读回；③ UI 没有编辑入口，题目给定 r_be 时只能接受公式计算值。
> 范围：`Models/CircuitElement.cs`（`BjtRbb`/`BjtRbe`/`SupportsBjtSmallSignalParameters`/显示辅助）、`Models/CircuitProjectData.cs`（DTO 字段、序列化、校验、向后兼容）、`Engine/Strategies/BjtAmplifierStrategy.cs`（共射四类偏置/共基/共集、NPN/PNP 全部读取实际参数；`rbb_ohm`/`rbe_ohm`/`rbe_source` 稳定键；非法值明确失败）、`ViewModels/AnalysisStudioViewModel.cs`（`RbbNumber`/`RbeNumber` 宏参数与双向同步）、`Views/AnalysisStudioPage.xaml`（元件参数检查器 + 参数设置 Group 3 两处编辑框）、`tests/NonlinearCircuitChecks/BjtSmallSignalParameterChecks.cs`（新增 91 项）、`tests/M2SolverChallenger`（+4 项宏同步/默认/失效/保存读回）、现行文档与索引。
> 结论：主项目构建 **0 警告/0 错误**（含默认 Debug 重新构建）；非线性引擎回归 **913/913**（原 822 + 91 条 `BjtSmallSignalParameterChecks`）、`M2SolverChallenger` **160/160**（原 156 + 4 条 rbb/rbe 宏同步、默认自动、旧结果失效、保存读回）、29 个教学电路文件校验 **259/259**、公式排版 411/411、显示两位小数 55/55；其余回归全部通过（ChallengerRunner 131、M3 32、M3_4 9、EngineV2 306、报告 110、导入 172、30 题基准 30/30、独立审计 1359、符号 230）；4 个生成器 `--verify` 为 7/7、6/6、10/10、6/6。**默认值（0=自动）下所有数值与第八轮完全一致**。**本轮按用户要求不做 ui-capture 窗口验收**：只完成控制台回归 + 构建验证；r_bb'/r_be 编辑框的真实窗口外观/交互未做截图验收，第八轮的窗口截图证据不覆盖本轮新增的编辑器。

## 1. 用户要求与处理

| 用户要求/问题 | 处理 | 状态 |
|---|---|---|
| 能按题目给定 r_bb'（基区体电阻） | `CircuitElement.BjtRbb`（Ω，0=自动：共射/共集 200 Ω、共基 50 Ω）持久化；BJT 策略读取 `rbb = BjtRbb>0 ? BjtRbb : (CommonBase?50:200)`；rbe/hie 步骤文案与代入式使用实际值，不再硬编码 | 已实现（913 项回归含自定义 rbb 用例） |
| 能按题目给定 r_be = h_ie | `CircuitElement.BjtRbe`（Ω，0=按 Q 点公式计算；>0 直接采用给定值）持久化；给定值原值参与 Av/Ri/Ro/fL 推导（不再二次舍入，显示经 `TeachingDisplay` ≤2 位）；步骤文案含"题目给定/直接采用" | 已实现（given 用例覆盖 CE/CB/CC） |
| 参数要能保存/打开 | DTO 新增 `BjtRbb`/`BjtRbe` 并随工程保存/读回；负数/非法值拒绝；旧工程缺省 0=自动（向后兼容，格式仍 1.2.0） | 已实现（M2 10.8c 保存读回断言） |
| UI 可编辑 | 两处入口：右侧"元件参数检查器"（选中 BJT 时）与"参数设置 → 三极管模型与信号源特性"Group 3；均含 rbb′（Ω，0=自动）与给定 rbe/hie（Ω，0=按 Q 点计算）及说明；宏参数变更同步到项目内 BJT 元件，选中元件修改回写宏 | 已实现（控制台级 ViewModel 断言；窗口外观未验收） |
| 默认行为不能变 | 所有默认 0（自动）路径保持第八轮数值：B07 变体仍为 Q 点 2.79 V/2.09 mA/20.69 μA/2.07 mA/7.84 V、Av=−34.25、Ri=1.23 kΩ、Aus=−32.91、fL=41.7 Hz | 已验证（B07/29 文件/既有回归） |
| 非法输入处理 | UI 侧 NaN/负数自动归零（0=自动）；后端非法数据（非有限/负数）明确失败，文案含"必须为有限非负数"，不抛异常、不输出 Q 点/交流参数/步骤 | 已实现（负向断言） |
| 本轮窗口验收 | 用户明确本轮不需要 ui-capture | **按用户要求不做**；仅控制台回归 + 构建，窗口截图验收未覆盖本轮新增编辑器 |

## 2. 关键实现

### 2.1 数据层（`Models/CircuitElement.cs`、`Models/CircuitProjectData.cs`）

- `CircuitElement` 新增：
  - `BjtRbb`（Ω，默认 `0`=自动）：0 时引擎按教学默认——共射/共集 200 Ω、共基 50 Ω；
  - `BjtRbe`（Ω，默认 `0`=按 Q 点计算）：>0 表示题目直接给定的 r_be=h_ie，引擎直接采用；
  - `SupportsBjtSmallSignalParameters`（仅 NPN/PNP 为 true，供 UI 显隐与宏同步）；
  - `BjtRbbDisplay`/`BjtRbeDisplay`（"自动（共射/共集 200 Ω，共基 50 Ω）"/"按 Q 点计算（r_be=r_bb'+(1+β)U_T/I_EQ）"等显示文本）；
  - `OnBjtRbbChanged`/`OnBjtRbeChanged`：`NaN`/非有限/负数自动归零（0=自动），空输入不会写坏工程。
- `CircuitProjectData.ElementDto` 新增 `BjtRbb`/`BjtRbe`：`From`/`ToModel` 双向映射；`Validate()` 对负数/非有限值抛 `InvalidDataException`，文案分别点名"基区体电阻 r_bb'"与"r_be/h_ie"。
- 工程格式仍 **1.2.0**：旧文件没有这两个字段，反序列化缺省为 0（自动/按 Q 点计算），行为与第八轮一致；不新增版本号。

### 2.2 引擎（`Engine/Strategies/BjtAmplifierStrategy.cs`）

- 入口统一校验并解析（共射四类偏置、共基、共集，NPN/PNP 全部适用）：

  ```text
  rbb      = BjtRbb > 0 ? BjtRbb : (CommonBase ? 50 Ω : 200 Ω)
  rbeGiven = BjtRbe > 0
  ```

- computed（`rbeGiven=false`，教材链）：`r_be = RoundResistance(rbb + (1+β)·U_T/I_EQ)`，仍按显示单位两位小数舍入并回代后续推导；步骤 Content/Latex/Substitution/Result 使用**实际 rbb 值**（不再出现硬编码 200/50），computed 文案注明"r_bb' 可由元件参数 rbb' 调节"。
- given（`rbeGiven=true`）：直接采用给定 r_be/h_ie 原值参与 Av/Ri/Ro/fL 推导，**不再二次舍入**（与 computed 的"显示值回代"语义不同，属有意设计）；步骤文案写明"题目给定/直接采用，不再用 r_bb' 公式计算；r_bb' 仅作记录"。
- 共基：`hie = rbeGiven ? 给定值 : RoundResistance(rbb + (1+β)U_T/I_EQ)`，`hib=hie/(1+β)`、`hfb=−β/(1+β)` 照旧；步骤 `Values` 新增稳定键 `rbb_ohm`/`rbe_ohm`/`rbe_source`（`computed`|`given`），共基保留 `hie`。
- 非法后端值（非有限/负数）：明确失败且文案含"必须为有限非负数（0 表示自动：r_bb' 取教材默认值、r_be 按 Q 点计算）"，不抛异常、不输出 Q 点/交流参数/步骤。
- 无 golden 值变化：`BjtRbb=0`/`BjtRbe=0` 的所有既有电路数值保持不变。

### 2.3 UI（`ViewModels/AnalysisStudioViewModel.cs`、`Views/AnalysisStudioPage.xaml`）

- 新增宏参数 `RbbNumber`/`RbeNumber`（默认 0=自动）：
  - 变更时 `SyncBjtSmallSignalToElements(rbb/rbe)` 同步到项目内所有 `SupportsBjtSmallSignalParameters` 元件；
  - 模板创建/打开工程时从第一个 BJT 元件刷新宏（`RbbNumber = bjt?.BjtRbb ?? 0`）；
  - 选中 BJT 的元件参数变化（`BjtRbb`/`BjtRbe` PropertyChanged）会回写宏，避免两处显示不一致；
  - 非法输入（NaN/负数）归零。
- `AnalysisStudioPage.xaml` 两处编辑框（均带 0=自动说明）：
  1. **右侧"元件参数检查器"卡**（`SelectedElement.SupportsBjtSmallSignalParameters` 时可见）："基区体电阻 rbb′:"、`NumberBox`（Ω，0=自动）、"给定 rbe/hie:"、`NumberBox`（Ω，0=计算），说明"r_bb′ 自动：共射/共集 200 Ω、共基 50 Ω；给定 rbe(hie) 后直接采用，不再按 Q 点公式计算。"
  2. **"参数设置"标签页 Group 3"三极管模型与信号源特性"**：在 β 行下新增同样的两行宏参数编辑框与说明（0=自动/0=计算）。
- 参数语义：r_bb'/r_be 是**每只 BJT 的元件参数**（教学模板只有一只 BJT 时即模板参数）；多只 BJT 时宏参数会同步到所有 BJT 元件。

### 2.4 测试

- 新增 `tests/NonlinearCircuitChecks/BjtSmallSignalParameterChecks.cs`（91 项）：默认自动（CE/CB/CC）、自定义 rbb（300/100/400 Ω）、题目给定 rbe（1500/1200/1500 Ω）、NPN/PNP、`rbe_source`/`rbb_ohm`/`rbe_ohm` 稳定键、步骤文案/公式/代入使用实际值且无硬编码 200/50、显示两位小数、非法值明确失败与错误路径；NonlinearCircuitChecks 工程总数 **913/913**。
- `tests/M2SolverChallenger` 新增 4 项：10.7b BJT 小信号参数默认自动、10.7c 宏参数同步到元件、10.7d 修改使旧结果失效、10.8c rbb/rbe 随工程保存并读回；总数 **160/160**。
- `tests/CircuitModelChecks` 259/259（29 文件默认参数行为不回归）；B07 `--file` 默认 rbb 自动 200，数值与第八轮一致（Q 点 2.79 V/2.09 mA/20.69 μA/2.07 mA/7.84 V、Av=−34.25、Ri=1.23 kΩ、Aus=−32.91、fL=41.7 Hz）。
- 4 个生成器 `--verify`：7/7、6/6、10/10、6/6（B07 默认参数不回归）。

## 3. 测试与验证

> 以下为 2026-10-18 本工作区最终回归实测（Lead 最终全量复核；数字以最终回归为准）。`dotnet run` 控制台检查工程 + 构建为本轮完整验证范围；**本轮按用户要求不做 ui-capture 窗口验收**，窗口截图验收不覆盖本轮新增编辑器。

### 3.1 构建与测试数字

| 检查 | 命令 | 结果 |
|---|---|---|
| 主项目构建（含默认 Debug 重新构建） | `dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore` / `-p:OutDir=apps/desktop-winui/bin/_verify` | **成功，0 警告/0 错误** |
| 非线性引擎回归（含新增小信号参数检查） | `dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj` | **Total=913 Passed=913 Failed=0**（+91 条） |
| M2 回归（rbb/rbe 宏同步/默认/失效/保存读回） | `dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj` | **Total Tests=160, Passed=160, Failed=0, VERDICT: APPROVE**（+4 条） |
| 29 文件校验 | `dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj` | **Total=259 Passed=259 Failed=0** |
| 公式排版回归 | `dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj` | **Total=411 Passed=411 Failed=0** |
| 显示两位小数回归 | `dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj` | **Total=55 Passed=55 Failed=0** |
| 30 题本地基准 | `dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj` | **Total=30 Passed=30 Failed=0 Accuracy=100.00%** |
| 二极管生成器 | `dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify` | **7/7** |
| BJT 偏置生成器 | `dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify` | **6/6** |
| BJT 放大生成器 | `dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify` | **10/10** |
| MOS 生成器 | `dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify` | **6/6** |

### 3.2 既有控制台回归

| 检查 | 结果 |
|---|---|
| ChallengerRunner | **131/131** |
| M3AdversarialStress | **32/32** |
| M3_4EmpiricalStress | **9/9** |
| EngineV2Checks | **306/306** |
| ReportExportChecks | **110/110** |
| WebImportChecks | **172/172** |
| EngineAuditChecks（独立数值审计，未改） | **1359/1359** |
| SymbolChecks（未改） | **230/230** |

### 3.3 三组关键数值（教材两位小数链）

**① 默认自动（`BjtRbb=0`、`BjtRbe=0`）**

| 子类型/场景 | 参数与结果 |
|---|---|
| 共射分压（12 V、Rb1=100 kΩ、Rb2=50 kΩ、Rc=2.2 kΩ、Re=1 kΩ、β=100） | `rbb=200 Ω`（自动）、`rbe=995.76 Ω`、`Av=−2.16`、`Ri=25.12 kΩ` |
| 共基 | `rbb=50 Ω`（自动）、`hie=1640 Ω`、`Au=121.95` |
| 共集 | `rbb=200 Ω`（自动）、`rbe=1490 Ω`、`Ro=114.66 Ω` |

**② 自定义 r_bb'（`BjtRbe=0`）**

| 子类型 | 参数与结果 |
|---|---|
| 共射 | `rbb=300 Ω` → `rbe=1100 Ω`、`Av=−2.15`（`Ri=25.13 kΩ`） |
| 共基 | `rbb=100 Ω` → `hie=1690 Ω`（`Au=118.34`） |
| 共集 | `rbb=400 Ω` → `rbe=1690 Ω` |

**③ 题目给定 r_be/h_ie（`BjtRbb` 仍可自动，`rbe_source=given`）**

| 子类型 | 参数与结果 |
|---|---|
| 共射 | 给定 `rbe=1500 Ω` → `rbe_source=given`、`Av=−2.15`、`Ri=25.15 kΩ` |
| 共基 | 给定 `hie=1200 Ω` → `hib=11.88 Ω`、`Au=166.67` |
| 共集 | 给定 `rbe=1500 Ω` → `Ro=114.73 Ω` |

- 所有显示数字经 `TeachingDisplay` ≤2 位小数；`rbb_ohm`/`rbe_ohm`/`rbe_source` 为第 2 步（共基为 h_ie 步）`Values` 稳定键。
- 后端校验：`BjtRbb`/`BjtRbe` 非有限或负数 → 明确失败，文案含"必须为有限非负数"，不输出 Q 点/交流参数/步骤。
- 默认回归：B07 教学变体（默认 rbb 自动 200 Ω）与第八轮完全一致（Q 点 2.79 V/2.09 mA/20.69 μA/2.07 mA/7.84 V、Av=−34.25、Ri=1.23 kΩ、Aus=−32.91、fL=41.7 Hz）。

### 3.4 验证口径说明（本轮无窗口验收）

- 本轮验证范围 = **控制台回归 + 构建**（含默认 Debug 重新构建，0 警告/0 错误）。
- **用户明确本轮不需要 ui-capture 窗口测试**：未做真实窗口截图/交互验收；r_bb'/r_be 两处编辑框在真实窗口中的外观、焦点行为与鼠标/键盘交互**未验收**；第八轮的窗口截图证据（B07 变体/普通模板）不覆盖本轮新增编辑器。
- `BjtSmallSignalParameterChecks` 用测试网表构造元件参数，不覆盖真实窗口编辑/保存选择器路径；M2 的保存读回是进程内 DTO 往返。
- 数值断言对教材手算公式（容差 ≤1%）；不代表外部 SPICE 数值一致性。

## 4. 文档更新

本轮同步更新（保留第八轮及更早修订记录原文，不改写历史轮次）：

- 新增本文件：第九轮最终记录（用户要求、旧实现问题、关键实现、三组数值、测试数字、无窗口验收说明、文档更新、边界、复现命令）。
- `README.md`：能力摘要补"BJT r_bb'/r_be(h_ie) 可调（0=自动；题目给定 rbe 直接采用）"、测试数字 913/160、当前改动指向 round9、无窗口验收说明。
- `PROJECT.md`：BJT 元件数据字段、引擎参数解析规则、结果契约 Values 键、支持矩阵 BJT 行补"小信号参数可调"、UI 检查器说明、旧工程兼容。
- `CLAUDE.md`：当前架构段落补 BJT r_bb'/r_be 可调（0=自动默认、given 直接采用）、测试清单数字（Nonlinear 913、M2 160）。
- `HANDOVER.md`：维护入口/日常操作（两处编辑框位置）、求解限制（自动/给定舍入语义、非法值、旧工程兼容）、验证数字、无窗口验收说明。
- `projects/analysis-engine-technical-report.md`：6.1 BJT 补 r_bb'/r_be(h_ie) 解析规则与三子类型影响、Values 键、非法值失败；第 10 节数字（913/160）。
- `projects/presentation-layer-technical-report.md`：元件参数检查器（选中 BJT）与"参数设置→三极管模型与信号源特性"两处编辑框、0=自动提示、双向同步、无效输入归零；无窗口验收说明。
- `projects/data-layer-technical-report.md`：`BjtRbb`/`BjtRbe` 字段、DTO 持久化/校验、旧工程缺省 0 兼容、格式仍 1.2.0、M2 保存读回断言。
- `projects/wzh-no-git-worktree-eng-review-test-plan-20260529-021023.md`：第九轮命令/结果表/能力状态（参数可调；本轮无窗口验收）、M2 160/Nonlinear 913 数字。
- `专业名词解释.md`：新增/修订"基区体电阻 r_bb'""r_be/h_ie""自动/给定小信号参数"，更新 BJT 模型相关条目。
- `projects/README.md`：文档索引新增第九轮记录。
- `projects/spec-first-stage-local-circuit-analysis-20260601.md`：范围/验收/任务勾选补第九轮（rbb/rbe 可调、913/160、无窗口验收）。
- `examples/circuit-models/README.md`：注明 BJT 示例默认 rbb=0（自动）不变、可在检查器改为题目给定值；29 文件校验仍 259/259。

> 范围说明：`projects/wzh-no-git-worktree-eng-review-20260529-021023.md`、`projects/technical-document-review-*` 与 round1–8 修订记录保持历史原样（其中旧数字 822/156、"r_bb' 硬编码 200/50"等属历史）；现行口径以本文及上述现行文档为准。

## 5. 已知边界与后续规划

1. **given 的舍入语义**：题目给定 r_be/h_ie 原值直接参与计算，不再按显示值二次舍入（computed 链条仍是"两位小数舍入并回代"）；这是有意设计，避免篡改题目参数。
2. **rbb/rbe 是每只 BJT 的元件参数**：教学模板只有一只 BJT 时即模板参数；多只 BJT 的电路（多级）不在本轮范围（多级求解仍未实现），ViewModel 宏参数会同步到所有 BJT 元件。
3. **旧工程兼容**：旧文件缺省 0=自动/按 Q 点计算；工程格式仍 1.2.0，无版本迁移器变更。
4. **非法值策略**：UI 编辑 NaN/负数自动归零（0=自动）；只有当非法值绕过 UI 写入后端数据时才明确失败（"必须为有限非负数"），不抛异常。
5. **本轮无窗口验收**：按用户要求不做 ui-capture；r_bb'/r_be 编辑框的真实窗口外观/焦点/键鼠行为未验收；控制台回归 + 构建为完整验证范围。
6. **MOSFET 放大器未提供同类参数**（r_bb'/r_be 仅 BJT 教学模型；MOS 用 gm/ro 模型）。
7. **fH/fL 仍教材近似**：rbb/rbe 变化会通过 rbe 影响 Av/Ri/Ro/fL，但高频仍是 Cπ/Cμ 模型估算。
8. **公式框/显示口径/DC-AC 分组沿用第七轮**，无回退；后端 `Exact*` 仍保留不显示。
9. **多级放大未实现**：≥2 晶体管仍明确拒绝，只预留 `IMultiStageAnalysis`。
10. **本轮未复跑** `npm run check`（独立 Web/Python 原型）与 `NativeBenchmark30 -- --roundtrip`（历史 31/31）。
11. **无 SPICE 对拍**：数值断言对教材手算公式，不是与 ngspice 等外部仿真器的一致性验证。
12. **后续规划**：MOSFET 小信号参数可调 → 多级级联求解 → 混合 BJT/MOS 非线性网络 → JFET/耗尽型求解 → 大信号/失真分析 → 公式框扩展 → 完整 ERC 与外部 SPICE 数值对照；视觉识别仍在软件本体完善之后。

## 6. 附录：复现命令

```powershell
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore
dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify
dotnet run --project tests/NonlinearCircuitChecks/NonlinearCircuitChecks.csproj
dotnet run --project tests/M2SolverChallenger/M2SolverChallenger.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj
dotnet run --project tests/CircuitModelChecks/CircuitModelChecks.csproj -- --file "examples/circuit-models/B07-common-emitter-bypassed.circuit"
dotnet run --project tests/MathFormulaChecks/MathFormulaChecks.csproj
dotnet run --project tests/DisplayRoundingChecks/DisplayRoundingChecks.csproj
dotnet run --project tests/NativeBenchmark30/NativeBenchmark30.csproj
dotnet run --project tests/ChallengerRunner/ChallengerRunner.csproj
dotnet run --project tests/M3AdversarialStress/M3AdversarialStress.csproj
dotnet run --project tests/M3_4EmpiricalStress/M3_4EmpiricalStress.csproj
dotnet run --project tests/EngineV2Checks/EngineV2Checks.csproj
dotnet run --project tests/ReportExportChecks/ReportExportChecks.csproj
dotnet run --project tests/WebImportChecks/WebImportChecks.csproj
dotnet run --project tests/EngineAuditChecks/EngineAuditChecks.csproj
dotnet run --project tests/SymbolChecks/SymbolChecks.csproj
dotnet run --project tools/CircuitModels.Diode/CircuitModels.Diode.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtBias/CircuitModels.BjtBias.csproj -- --verify
dotnet run --project tools/CircuitModels.BjtAmp/CircuitModels.BjtAmp.csproj -- --verify
dotnet run --project tools/CircuitModels.Mos/CircuitModels.Mos.csproj -- --verify
```
