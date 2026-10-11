# 电路学习辅助系统

面向模拟电子电路教学的本地原型。当前版本提供类型化电路数据、18 类元件定义、可视化拓扑编辑、5 种基础分析策略、本地 FastAPI 任务服务、手机上传后端，以及结构化步骤和 HTML 分析报告。

> 当前状态以 2026-07-23 的源码和自动化检查为准。视觉识别模块已取消接入，不再作为后续交付范围；30 题数据集、PDF/LaTeX/Circuitikz 导出等仍未完成。

## 环境

- Node.js 18+
- Python 3.10+
- Windows 可直接使用仓库内的 `.bat` 启动脚本

首次安装：

```powershell
npm install
python -m pip install -r services/engine/requirements.txt
python -m pip install -r services/api/requirements.txt
```

## 验证

运行完整检查：

```powershell
npm run check
```

该命令覆盖：

- TypeScript 类型检查和 15 项 Schema/元件测试
- 16 项分析引擎测试
- 16 项 API/Worker 测试
- 编辑器生产构建
- 3 个 Benchmark 校验
- 3 份 JSON Schema 导出

## 启动

Windows 一键启动：

```powershell
.\start-all.bat
```

也可以分别启动：

```powershell
python -m uvicorn services.api.main:app --host 127.0.0.1 --port 3000
npm run dev:editor
```

- 编辑器：http://localhost:5173
- API：http://localhost:3000
- API 文档：http://localhost:3000/docs
- 健康检查：http://localhost:3000/api/health

## 当前能力

| 模块 | 已实现 | 当前边界 |
|---|---|---|
| 数据层 | Zod Schema、18 类元件、图校验、JSON Schema、3 个 Benchmark | Python 端使用 dataclass 和手动解析，尚未从 JSON Schema 自动生成模型 |
| 分析引擎 | BJT DC/AC、MOS DC/AC、二极管策略，SymPy MNA 和安全求解器 | Lcapy/PySpice 尚未接入实际执行路径，题型覆盖仍有限 |
| API | FastAPI、SQLite 任务队列、Worker、会话、上传、SSE | 任务接口仅适合本地可信环境；未提供用户认证 |
| 编辑器 | React Flow 拓扑编辑、参数修改、校验、分析轮询、KaTeX、HTML 报告 | 尚无撤销/重做、桌面端扫码入口、PDF/LaTeX/Circuitikz 导出 |
| 视觉识别 | 当前阶段已取消接入；保留技术方案文档供研究参考 | `services/vision`、训练集、领域模型、推理管线和精度报告均不存在 |

## 目录

```text
packages/schema       TypeScript/Zod 数据契约
packages/components   元件定义库
packages/editor       React 电路编辑器
services/engine       Python 分析引擎
services/api          FastAPI 服务和 Worker
benchmarks/examples   当前 3 个基准样例
generated/schema      导出的 JSON Schema
projects              规格、技术论证和复核报告
```

技术文档的实现对照见 [技术文档复核报告](projects/technical-document-review-20260716.md)。
