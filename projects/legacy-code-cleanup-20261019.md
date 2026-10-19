# 旧代码清理与运行验证（2026-10-19）

本次从当前工作区移出 12 个未被现行入口引用的源码文件、删除未使用的 `UnitPrefix` 枚举，并整理 31 个过程文件。主客户端仍为 WinUI 3，保留独立 React/Python 原型、工程格式兼容逻辑、教学电路、测试和历史验收证据。

## 清理范围

| 类别 | 文件与处理 |
|---|---|
| 旧 HTTP / 固定共射求解服务 | 移出 `apps/desktop-winui/Services/CircuitApiService.cs`；其中的旧接口、固定参数与固定求解日志不再编译 |
| WinUI 示例页 | 移出 `MainPage.xaml`、`MainPage.xaml.cs`、`ViewModels/MainPageViewModel.cs` |
| 未使用的窗口 ViewModel | 移出 `ViewModels/MainWindowViewModel.cs` |
| 未接入导航的旧页面 | 移出 `Views/SettingsPage.xaml/.xaml.cs`、`Views/SymbolLibraryPage.xaml/.xaml.cs` |
| React 无入口引用文件 | 移出 `components/layout/ShortcutsDialog.tsx`、`components/layout/StepCard.tsx`、`lib/validation.ts` |
| 未使用的类型 | 仅删除 `Models/EngineeringUnit.cs` 中的 `UnitPrefix`；保留正在使用的 `UnitOption` 和 `EngineeringUnits` |
| 临时与过程文件 | 移出根目录 9 个 `.tmp_*` 文件、`pdf_extract_utf8.txt`、`pdf_pages/` 内 12 张截图、`tools/ui-verify/_debug/` 内 8 个一次性脚本及误生成的 `tatus` |
| 现行说明同步 | 更新 PROJECT、HANDOVER、展示层报告及符号预览代码注释，移除已清理路径的当前实现说明 |
| 防止过程文件再次进入暂存区 | `.gitignore` 增加根目录 `.tmp_*`、PDF 提取产物及 `_debug` 目录规则 |

WinUI 表内未写完整前缀的路径相对于 `apps/desktop-winui/`；React 路径相对于 `packages/editor/src/`。12 个旧源码文件共 989 行，枚举另计。

## 可恢复归档与改动边界

- 所有 43 个移出文件按原相对路径保存在仓库根 `build/legacy-cleanup-20261019/backup/`，该目录受现有 `build/` 忽略规则保护。没有永久销毁这些文件。
- `build/legacy-cleanup-20261019/scope.json` 记录逐文件范围，`baseline-hashes.json` 记录清理前已跟踪工作区文件的 SHA256，`git-status-before.txt` 记录原暂存/工作区状态。修改的现有文件也各有清理前备份。
- 逐文件核验移出文件归档的完整性；核验清理范围外既有已跟踪文件的内容哈希保持一致。
- 仅对 42 个原先已跟踪或已暂存、现已移出的路径更新 Git 索引；其他原有暂存内容保留。移出的 30 个暂存新增过程文件从索引消失，12 个原有源码文件形成暂存删除。`tatus` 原为未跟踪文件。
- 没有提交、推送或重打发布 ZIP。日常启动路径 `start-winui.bat` 所指向的 Debug 程序已重新构建。

## 清理后验证

所有日志位于仓库根 `build/legacy-cleanup-20261019/verification/`。

| 验证 | 结果 |
|---|---|
| WinUI 主项目 `dotnet build --no-restore` | 通过，0 警告、0 错误 |
| MSBuild `Compile` / `Page` 项目求值 | 9 个移出的 WinUI 文件均已不在编译清单中 |
| 源码引用复查 | 现行应用、脚本、测试和工具源码中无已清理类型或模块的残余引用 |
| M2SolverChallenger | 166 / 166 |
| ChallengerRunner | 131 / 131 |
| M3AdversarialStress | 32 / 32 |
| M3_4EmpiricalStress | 9 / 9 |
| EngineV2Checks | 306 / 306 |
| ReportExportChecks | 110 / 110 |
| WebImportChecks | 172 / 172 |
| NativeBenchmark30 | 30 / 30 |
| EngineAuditChecks | 1359 / 1359 |
| SymbolChecks | 230 / 230 |
| NonlinearCircuitChecks | 1606 / 1606 |
| CircuitModelChecks | 259 / 259 |
| MathFormulaChecks | 411 / 411 |
| DisplayRoundingChecks | 55 / 55 |
| 四组教学电路生成器 `--verify` | Diode 7、BjtBias 6、BjtAmp 10、Mos 6，合计 29 / 29 |
| LauncherChecks | 通过：现有 EXE 直接启动、首次构建、显式重建等启动器逻辑 |
| `npm test` | 16 / 16 |
| `npm run build:editor` | TypeScript 检查与 Vite 生产构建通过；存在原有大体积 chunk 提示 |
| `npm run typecheck` | 通过 |

14 组原生回归共 4876 项通过，四组电路文件校验另有 29 项通过；启动器单独通过。上述验证使用清理后的源码重新编译运行。

## 真实窗口验收

使用 computer-use 技能启动新构建的 Debug EXE，检查以下行为：

1. 主窗口及欢迎页正常显示，标题栏图标正常；启动日志确认图标设置成功。
2. 从欢迎页载入“分压式偏置共射放大电路”，画布与参数检查器正常显示。
3. 点击“求解与理论分析”，状态显示“求解完成 · 晶体管共射极放大电路 · 5 个步骤”；DC/AC 推导分组与 Win2D 公式正常显示。
4. 切换实时指标：IBQ=20.1 μA、ICQ=2.01 mA、VCEQ=3.94 V、Av=-155.03、Ri=1.32 kΩ、Ro=3 kΩ；结果与推导页一致。辅助功能文本记录在 `ui-metrics.txt`。
5. “保存”能打开系统另存为对话框；本次取消，未写入或覆盖用户工程。系统 PickerHost 未被工具列为可单独定位窗口，因此没有宣称完成 GUI 保存/重开往返验收；工程持久化、导入与报告由上述回归套件覆盖。
6. 关闭窗口时出现未保存工程提示；仅放弃本次临时教学模板会话后正常退出。日志记录 `ProcessExit ExitCode: 0`。

本次证据支持清理后构建、现有回归及上述日常操作正常。未把启动冒烟或回归通过扩展为所有电路拓扑、操作系统版本及全部人工交互的保证。
