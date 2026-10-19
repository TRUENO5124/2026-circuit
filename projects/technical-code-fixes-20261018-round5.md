# 软件本体代码修订记录（第五轮：PNP 默认朝向、ALT+X 镜像与节点跟随）

> 日期：2026-10-18（Asia/Shanghai）
> 性质：按用户第五轮要求修改 WinUI 主软件。用户要求：① PNP 三极管默认符号改为发射极在上、集电极在下；② 所有元器件增加镜像功能，快捷键 `ALT+X`；③ 完成后审查，保证节点跟随元件符号变换。
> 范围：`Models/CircuitElement.cs`、`Models/SymbolGeometry.cs`、`Models/CircuitWireSync.cs`（新增）、`Models/CircuitProjectData.cs`、`Controls/CircuitCanvas.xaml/.cs`、`Views/AnalysisStudioPage.xaml/.cs`、`ViewModels/AnalysisStudioViewModel.cs`、`tests/SymbolChecks/*`、现行文档与测试工程链接。
> 结论：主项目构建 0 警告/0 错误；`SymbolChecks` 230/230 通过；其余常规回归全部通过（见第 4 节）。未提交/推送 Git。

## 1. 用户要求与处理

| 要求 | 处理 |
|---|---|
| PNP 默认发射极在上、集电极在下 | `SymbolGeometry.BuildBjt` 的 PNP 发射极箭头移到上方斜引线；`CircuitElement.InitializeDefaultPins` 的 PNP 引脚改为 E(20,-35)/C(20,35)，NPN 保持 C 上/E 下 |
| 所有元件支持镜像，`ALT+X` | `CircuitElement.MirrorX` + 统一“镜像 → 旋转 → 平移”变换；画布快捷条、检查器、元件卡、页面快捷键与画布快捷键均接入 |
| 节点跟随符号变换 | 引脚世界坐标统一由 `CircuitElement.GetPinWorldPosition()` 计算；新增 `Models/CircuitWireSync.cs` 并替换画布/ViewModel 两份重复的同步逻辑，直连导线、`WireTap` 分支、二级分支与焊点（由导线端点推导）一起重算 |

## 2. 关键实现

### 2.1 默认符号与引脚

- NPN：C=20,-35（上），E=20,35（下），发射极实心箭头指向外。
- PNP：E=20,-35（上），C=20,35（下），发射极实心箭头指向上方基极方向（向内）。
- 符号库卡片与画布共用 `SymbolGeometry`，因此卡片预览同步显示新朝向。

### 2.2 旋转/镜像统一变换

- `GetSymbolTransformMatrix()`（画布世界几何用）：`T(-c) · S · R · T(c)`。
- `GetPinLocalToWorldMatrix()`（中心相对引脚/几何用）：`S · R · T(c)`；两者对 `world = center + local` 严格等价。
- `MirrorX` 在旋转之前应用；`HitTest`、`GetPinWorldDirection` 使用同一顺序的逆/方向变换；文本标签不镜像，仅按镜像换到符号另一侧。

### 2.3 导线与节点同步

- `CircuitWireSync.SyncConnectedWiresToElement(wires, element)`：
  - Phase 1：直连引脚导线端点取变换后的引脚世界坐标；
  - Phase 2A：元件侧实引脚 → 父线 `WireTap` 分支，节点沿父线垂直投影；
  - Phase 2B：沿 `WireTap` 依赖链传播二级及更深分支（最多 3 轮，跳过 `_RAIL/_BUS` 纯母线）。
- 画布与 ViewModel 均调用该 helper；焊点在 `Canvas_Draw` 中由导线端点/交点计算，因此自动跟随。

### 2.4 持久化与兼容

- 工程 DTO 新增可选 `MirrorX`（旧文件缺省 false），版本仍为 `1.2.0`，读取兼容 `1.0.0/1.1.0`。
- 读取旧 PNP 工程时：若保存的 C 引脚 Y<0 或旧文件没有引脚坐标，则应用新默认引脚布局并重挂相关导线/`WireTap` 分支；旋转/镜像状态同时保留。
- 撤销/重做使用 `CircuitElement.Clone()`，已包含 `MirrorX`。

### 2.5 交互入口

- `Page.KeyboardAccelerator Key="X" Modifiers="Menu"`、画布 `KeyDown` 的 Alt+X 分支、画布快捷条“镜像 (Alt+X)”、检查器“镜像 (Alt+X)”、元件卡镜像按钮。
- 多选镜像为 all-or-nothing：只要选中项中存在未镜像元件，则全部镜像；否则全部取消镜像。

## 3. 审查与修复

独立审查重点核对 PNP 几何/引脚一致性、ALT+X 多处路由是否重复触发、镜像+旋转矩阵等价性、导线/`WireTap` 节点跟随、持久化/撤销/旧工程迁移与全仓库旧假设。

- 审查结论：三项用户需求均通过，无阻断问题。
- 审查中已修复：Page `KeyDown` 中不可达的 Alt+X 分支已删除，避免将来路由变化导致“镜像两次等于未镜像”；旧 PNP 空引脚坐标工程也纳入迁移并重挂导线；补充缺失 `MirrorX` 字段 JSON、无引脚旧工程、旋转+镜像迁移与 `WireTap` 重挂断言。
- 验证 ALT+X 路由的临时 WinUI 探针实测：焦点元素处理 `KeyDown` 时父级 accelerator 不触发；焦点元素不处理时 accelerator 只触发一次且 Page `KeyDown` 不再派发，不会双触发。

## 4. 验证

| 检查 | 结果 |
|---|---|
| `dotnet build apps/desktop-winui/CircuitAssistant.csproj --no-restore -p:OutDir=apps/desktop-winui/bin/_verify5` | 成功，0 警告/0 错误 |
| `dotnet run --project tests/SymbolChecks/SymbolChecks.csproj` | 230/230（新增镜像/旋转变换、节点跟随、持久化/迁移断言） |
| EngineV2Checks / ChallengerRunner / M3 / M3_4 / WebImportChecks / ReportExportChecks / NativeBenchmark30 / EngineAuditChecks | 303/303、131/131、32/32、9/9、168/168、110/110、30/30、1359/1359 |
| M2SolverChallenger | 155/155（本会话语义下需把 TMP/TEMP 指向工作区，规避沙箱对工作区外隐藏临时文件的写入限制） |

## 5. 剩余边界

- 未在真实窗口中做像素级/交互级验收；用户正在运行的旧客户端需重新构建并重启后才能看到新符号与镜像功能。
- 标签在“旋转 90°/270° + 镜像”组合下仍沿用既有的屏幕侧边布局启发式，只保证可读与不覆盖符号，不做逐角度像素级排版。
- `CircuitWireSync` 的最多 3 轮传播与 `_RAIL/_BUS` 后缀跳过沿用原实现的启发式；深层自定义 `WireTap` 链超过 3 层时不会继续向外传播。
- 镜像时若导线带手工走线（Waypoints ≤2），会被清空并按新端点重算，可能变回自动拐角；连通性不受影响。
- 拖拽多选时仍有独立的 `SyncExternalConnectedWires` 传播路径，与本 helper 语义重复，后续可统一。
