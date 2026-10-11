# 数据层实现方案技术论证报告

> **项目**: 面向电子电路学习的可解释分析辅助系统
> **阶段**: Issue I1 — 数据层建设
> **日期**: 2026 年 6 月
> **性质**: 技术方案论证文档

---

## 当前实现状态（2026-07-23 复核更新）

> 本文是技术方案论证，不等同于交付清单。当前事实以源码、`npm run check` 和 `technical-document-review-20260716.md` 为准。

- **已实现**：TypeScript/Zod Schema、18 类元件定义、图校验器、3 个 Benchmark、JSON Schema 导出。
- **部分实现**：Python 端可以消费同一 JSON 形状，但当前采用 dataclass + 手动解析，并非本文示例中的 Pydantic 对称模型或自动生成模型。
- **未实现**：TypeScript/Python 双向契约测试、真实教材图片引用、30 题 Benchmark。
- **已取消**：视觉识别模块不再接入当前项目；数据层只服务人工结构化录入、编辑、分析和评测流程。

---

## 目录

1. [摘要](#1-摘要)
2. [问题定义与设计约束](#2-问题定义与设计约束)
3. [核心方案：TypeScript + Zod Schema 作为事实来源](#3-核心方案typescript--zod-schema-作为事实来源)
4. [电路图数据模型设计论证](#4-电路图数据模型设计论证)
5. [元件定义体系设计论证](#5-元件定义体系设计论证)
6. [Benchmark 数据集设计论证](#6-benchmark-数据集设计论证)
7. [校验器设计论证](#7-校验器设计论证)
8. [跨语言互操作方案](#8-跨语言互操作方案)
9. [文献综述与相关研究](#9-文献综述与相关研究)
10. [风险分析与应对策略](#10-风险分析与应对策略)
11. [结论](#11-结论)
12. [参考文献](#12-参考文献)

---

## 1. 摘要

数据层是整个系统的基础，定义了电路图、元件实例、连接关系、参数值、评测基准等所有核心数据的类型约束和校验规则。当前的分析引擎层、编辑器层、展示层和评测系统都依赖同一套共享数据形状。

本文档论证了以下核心设计决策：

1. **采用 TypeScript + Zod 作为数据模型的事实来源**，通过 JSON Schema 实现跨语言互操作
2. **采用"节点-元件-连接"三元组**作为电路图的核心表示范式
3. **采用数据驱动的元件定义体系**，将端口、参数、模型变体、分析标签和教学标签统一编码
4. **采用多层标准答案的 Benchmark 结构**，支持从拓扑到方程到知识点的分层评测
5. **采用编译期类型安全 + 运行时校验的双重保障**机制

---

## 2. 问题定义与设计约束

### 2.1 数据层需要解决的核心问题

本系统的数据层当前服务于四个消费者：

| 消费者 | 数据需求 | 特殊约束 |
|--------|----------|----------|
| **分析引擎层** | 读取完整拓扑，输出分析结果 | 需要类型化的参数值（数值/字符串/布尔） |
| **编辑器层** | 读写拓扑，支持增量修改 | 需要高效的随机访问（按 ID 查找） |
| **展示层** | 读取分析结果，渲染 LaTeX | 需要结构化的方程和步骤 |
| **评测系统** | 读写 Benchmark，比对结果 | 需要多层标准答案和等价表达式 |

### 2.2 关键设计约束

| 约束 | 说明 |
|------|------|
| **跨语言互操作** | 前端 TypeScript + 后端 Python，数据结构必须可在两种语言中无损传递 |
| **编译期安全** | TypeScript 端必须在编译时捕获类型错误，不能依赖运行时才发现 |
| **运行时校验** | 外部输入（用户编辑、API 输入、Benchmark JSON）必须在运行时验证 |
| **可演进性** | 数据模型必须支持版本化，允许后续扩展新元件族、新参数、新分析类型 |
| **JSON 可序列化** | 所有数据必须可无损序列化为 JSON，用于文件存储和跨进程通信 |
| **人类可读** | Benchmark 文件必须可被人类直接阅读和编辑（JSON 格式） |

---

## 3. 核心方案：TypeScript + Zod Schema 作为事实来源

### 3.1 选型论证

在确定数据模型的技术栈时，我们比较了以下方案：

| 方案 | 类型安全 | 运行时校验 | 跨语言 | 学习成本 | 生态成熟度 |
|------|----------|-----------|--------|----------|-----------|
| **TypeScript + Zod** | ✅ 编译期 | ✅ 运行时 | ✅ JSON Schema 导出 | 中 | 高 |
| TypeScript + io-ts | ✅ 编译期 | ✅ 运行时 | ❌ 需手动导出 | 高 | 中 |
| TypeScript + JSON Schema 手写 | ❌ 无编译期 | ✅ 运行时 | ✅ 原生 | 低 | 高 |
| Python + Pydantic | ✅ 编译期(mypy) | ✅ 运行时 | ✅ JSON Schema 导出 | 低 | 高 |
| Protocol Buffers | ✅ 编译期 | ✅ 运行时 | ✅ 原生 | 高 | 高 |
| GraphQL Schema | ✅ 编译期 | ✅ 运行时 | ✅ 原生 | 高 | 中 |

**选择 TypeScript + Zod 的核心理由**：

1. **Schema-as-Source-of-Truth**: Zod 的 schema 定义同时是类型定义和运行时校验器，消除了"类型定义与校验逻辑不一致"的经典问题
2. **零成本类型推导**: `z.infer<typeof schema>` 从 schema 自动推导 TypeScript 类型，无需手动维护接口定义
3. **JSON Schema 导出**: 通过 `zod-to-json-schema` 可自动导出标准 JSON Schema，供 Python 端（Pydantic `model_json_schema()`）消费
4. **与前端天然集成**: 项目前端采用 TypeScript，Zod schema 可直接用于表单校验、API 响应校验等场景
5. **丰富的组合器**: Zod 提供 `.extend()`、`.merge()`、`.partial()`、`.pick()` 等组合器，支持 schema 的模块化组合和演进

### 3.2 Zod 在本项目中的应用模式

```typescript
// 定义 schema
export const circuitGraphSchema = z.object({
  id: idSchema,
  version: z.string().min(1),
  nodes: z.array(nodeSchema).min(1),
  components: z.array(componentInstanceSchema).min(1),
  connections: z.array(connectionSchema).min(1),
  // ...
}).strict();

// 自动推导类型
export type CircuitGraph = z.infer<typeof circuitGraphSchema>;

// 运行时校验
const result = circuitGraphSchema.safeParse(unknownInput);
if (!result.success) {
  console.error(result.error.issues);
}

// JSON Schema 导出（供 Python 端消费）
import { zodToJsonSchema } from "zod-to-json-schema";
const jsonSchema = zodToJsonSchema(circuitGraphSchema);
```

### 3.3 与 Pydantic 的对称设计

Python 端通过 JSON Schema 文件与 TypeScript 端共享数据契约：

```python
# Python 端：从 JSON Schema 生成 Pydantic model
from pydantic import BaseModel, create_model_from_schema
import json

with open("generated/schema/circuit-graph.schema.json") as f:
    schema = json.load(f)

# 或者手动定义对称的 Pydantic model
class ParameterValue(BaseModel):
    value: float | str | bool
    unit: str | None = None
    source: str | None = None

class CircuitGraph(BaseModel):
    id: str
    version: str
    nodes: list[Node]
    components: list[ComponentInstance]
    connections: list[Connection]
    # ...
```

---

## 4. 电路图数据模型设计论证

### 4.1 表示范式的选择

电路图的数字化表示有三种主流范式：

| 范式 | 描述 | 代表 | 优势 | 劣势 |
|------|------|------|------|------|
| **网表（Netlist）** | 以"网络"为中心，每个网络列出连接的元件引脚 | SPICE netlist | 紧凑、SPICE 直接消费 | 丢失几何信息，不易可视化 |
| **节点-元件-连接（NEC）** | 以"元件"为中心，显式定义节点、元件和它们之间的连接 | KiCad、本项目 | 结构清晰、可扩展、支持几何信息 | 冗余度高于网表 |
| **图（Graph）** | 以"图论"为中心，节点和边直接表示电路拓扑 | GNN 研究 | 数学形式化、适配 ML | 不直接支持多端口元件 |

**选择 NEC 范式的理由**：

1. **与教科书表示一致**: 教科书电路图天然以元件为视觉中心，NEC 的元件实例（ComponentInstance）直接对应图中的元件符号
2. **支持多端口元件**: BJT 有 3 个端口（C/B/E），MOSFET 有 4 个端口（D/G/S/B），NEC 通过 `ports` 数组自然支持
3. **支持参数化**: 每个元件实例携带独立的参数值，同一电阻定义（`passive.resistor`）可以有不同的阻值
4. **支持几何信息**: 节点和元件都可以携带 `position`，用于编辑器中的可视化布局
5. **可导出为网表**: NEC → 网表的转换是直接的（遍历连接表，按节点聚合引脚）
6. **与工业标准对齐**: KiCad 的 `.kicad_sch` 文件本质上就是 NEC 范式

### 4.2 核心实体关系

```text
CircuitGraph (电路图)
├── Node[] (节点) ───────────── 电路中的电气连接点
├── ComponentInstance[] (元件实例) ── 电路中的元件
│   ├── definitionId → ComponentDefinition (元件定义)
│   ├── ports: string[] (端口 ID 列表)
│   ├── parameters: Record<string, ParameterValue>
│   └── modelVariant: string (模型变体)
├── Connection[] (连接) ─────── 元件端口与节点之间的连接关系
│   ├── nodeId → Node
│   ├── componentId → ComponentInstance
│   └── portId → 端口 ID
├── GraphPort[] (图端口) ────── 电路的外部接口（输入/输出/探针）
└── ValidationIssue[] (校验问题)
```

**关键设计决策**：

- **连接是三元组**（nodeId, componentId, portId），而非二元组。这避免了"元件端口直接相连"的歧义——两个元件的端口通过共享同一个节点来建立电气连接，而非直接相连。
- **节点区分 electrical 和 reference 类型**。`reference` 类型节点（如地）不参与"悬空节点"检查，因为地节点在物理上不需要连接两个以上的端口。
- **元件实例引用元件定义**，而非内联定义。这使得同一个电阻定义可以被多个电阻实例共享，且定义的修改自动传播到所有实例。

### 4.3 参数值的来源追踪

每个参数值携带 `source` 字段，标记参数值的来源：

```typescript
source: z.enum(["user", "vision", "benchmark", "default"])
```

| 来源 | 含义 | 使用场景 |
|------|------|----------|
| `user` | 用户手动输入 | 编辑器中用户修改参数 |
| `vision` | 历史兼容保留值 | 当前没有模块写入；不代表视觉识别仍在交付范围 |
| `benchmark` | 评测集标准值 | Benchmark JSON 文件中的标准答案 |
| `default` | 元件定义的默认值 | 未指定参数时自动填充 |

这一设计使得系统可以追踪参数来源。当前实际流程使用 `user`、`benchmark` 和 `default`；`vision` 仅为已有 Schema 的兼容保留值。

---

## 5. 元件定义体系设计论证

### 5.1 元件族分类

本项目的元件族分类基于模拟电子技术课程的教学体系，而非工业标准（IEC/IEEE）的物理分类：

```typescript
componentFamilySchema = z.enum([
  "passive",           // 无源元件：电阻、电容、电感、变压器
  "independent_source", // 独立源：直流电压源、直流电流源、交流源
  "controlled_source",  // 受控源：VCVS、VCCS、CCVS、CCCS
  "diode",             // 二极管
  "bjt",               // 双极型晶体管：NPN、PNP
  "fet",               // 场效应管：MOSFET、JFET
  "reference"          // 参考元件：地
]);
```

**设计理由**：

1. **教学导向**: 这一分类直接对应模电课程的章节结构（无源元件→二极管→BJT→FET），降低学生的学习认知负担
2. **分析策略映射**: 每个元件族对应特定的分析策略（如 `bjt` → 直流工作点 + 小信号模型，`diode` → 分段线性/迭代）
3. **可扩展性**: 后续扩展电路分析基础题时，可以新增 `digital` 族；扩展模拟 IC 时，可以新增 `opamp` 族

### 5.2 元件定义的五维结构

每个元件定义包含五个维度的信息：

| 维度 | 字段 | 作用 | 消费者 |
|------|------|------|--------|
| **结构** | `ports` | 定义元件的端口（名称、角色、是否必需） | 验证器、编辑器、SPICE 导出 |
| **参数** | `parameters` | 定义元件的可配置参数（名称、单位、范围、默认值） | 验证器、编辑器、分析引擎 |
| **变体** | `allowedModelVariants` | 定义元件的等效电路模型变体 | 分析引擎（大信号/小信号） |
| **分析** | `analysisTags` | 标记元件参与的分析类型 | 分析策略选择器 |
| **教学** | `teachingTags` | 标记元件涉及的知识点 | 教学解释生成器 |

**示例：BJT NPN 的五维定义**

```typescript
{
  id: "bjt.npn",
  displayName: "NPN BJT",
  family: "bjt",
  // 结构维度：3 个端口
  ports: [
    { id: "collector", label: "Collector", role: "collector", required: true },
    { id: "base",      label: "Base",      role: "base",      required: true },
    { id: "emitter",   label: "Emitter",   role: "emitter",   required: true }
  ],
  // 参数维度：2 个参数
  parameters: [
    { id: "beta",    label: "Current Gain", quantity: "beta",             unit: "unitless", min: 0 },
    { id: "vbe_on",  label: "VBE On",       quantity: "voltage",         unit: "volt",     min: 0 }
  ],
  // 变体维度：大信号模型和混合 π 模型
  allowedModelVariants: ["npn_large_signal", "npn_hybrid_pi"],
  defaultModelVariant: "npn_large_signal",
  // 分析维度
  analysisTags: ["transistor", "dc_bias", "small_signal"],
  // 教学维度
  teachingTags: ["bjt", "common_emitter", "operating_point"]
}
```

### 5.3 端口角色体系

端口角色（`portRoleSchema`）的设计覆盖了模电课程中所有元件的引脚类型：

| 角色类别 | 角色值 | 对应元件 |
|----------|--------|----------|
| 通用二端口 | `positive`, `negative`, `passive` | 电阻、电容、电感、电压源、电流源 |
| 二极管 | `anode`, `cathode` | 二极管 |
| BJT | `collector`, `base`, `emitter` | NPN、PNP |
| FET | `drain`, `gate`, `source`, `bulk` | MOSFET、JFET |
| 变压器 | `primary`, `secondary` | 变压器 |
| 受控源控制端 | `control_positive`, `control_negative`, `sense_positive`, `sense_negative` | VCVS、VCCS、CCVS、CCCS |
| 参考 | `reference` | 地 |

### 5.4 模型变体机制

模型变体（`modelVariant`）是连接数据层与分析引擎的关键桥梁。同一个物理元件在不同的分析场景下需要使用不同的等效电路模型：

| 元件 | 变体 | 使用场景 |
|------|------|----------|
| 二极管 | `ideal_diode` | 理想二极管分析 |
| 二极管 | `constant_voltage_drop` | 恒压降模型（默认） |
| 二极管 | `shockley` | Shockley 方程精确分析 |
| BJT NPN | `npn_large_signal` | 直流工作点分析（默认） |
| BJT NPN | `npn_hybrid_pi` | 交流小信号分析 |
| MOSFET | `n_enhancement` | N 沟道增强型（默认） |
| MOSFET | `small_signal` | 交流小信号分析 |

分析引擎根据当前分析任务（直流/交流/瞬态）选择合适的模型变体。

---

## 6. Benchmark 数据集设计论证

### 6.1 多层标准答案的设计

本项目的 Benchmark 结构采用**多层标准答案**设计，每一层对应评测的一个维度：

```text
BenchmarkCase
├── problemText          ← 题目文本（人工输入）
├── imageRefs            ← 原始图片引用（教科书扫描件）
├── circuitGraph         ← 标准拓扑（人工标注的 Ground Truth）
│   ├── nodes
│   ├── components       ← 每个元件的类型、参数、模型变体
│   ├── connections
│   └── ports
└── groundTruth          ← 分析层标准答案
    ├── expectedComponentIds     ← 应识别出的元件 ID
    ├── expectedNodeIds          ← 应识别出的节点 ID
    ├── expectedConnectionCount  ← 应有的连接数
    ├── equations                ← 关键方程（LaTeX 格式）
    │   └── variables            ← 方程中的变量
    ├── expectedResults          ← 期望求解结果
    │   └── tolerance            ← 允许误差
    ├── acceptedEquivalentExpressions  ← 等价表达式
    └── teachingTags             ← 涉及的知识点
```

**设计理由**：

1. **分层评测**: 不同层次的错误（拓扑错误 vs 分析错误 vs 输出错误）可以被独立度量
2. **等价表达式**: 电路分析的最终表达式可以有多种等价形式（如 `V_C = V_CC - I_C * R_C` 和 `V_C = V_CC - beta * ((V_CC - V_BE) / RB) * RC`），`acceptedEquivalentExpressions` 允许多种正确答案
3. **容差机制**: 数值结果允许通过 `tolerance` 指定可接受的误差范围
4. **教学标签**: `teachingTags` 将每道题与知识点关联，支持后续的知识点覆盖率统计

### 6.2 Benchmark 的图片引用（视觉接入已取消）

Benchmark 的 `imageRefs` 字段设计了三层图片引用：

```typescript
imageRefSchema = z.object({
  id: idSchema,
  path: z.string().min(1),                    // 图片路径
  kind: z.enum(["original", "crop", "processed"]),  // 图片类型
  crop: z.object({                            // 可选裁剪区域
    x: z.number().nonnegative(),
    y: z.number().nonnegative(),
    width: z.number().positive(),
    height: z.number().positive()
  }).optional()
});
```

| 图片类型 | 用途 |
|----------|------|
| `original` | 教科书原始扫描件/照片 |
| `crop` | 裁剪后的电路图区域 |
| `processed` | 预处理后的标准化图像 |

这些字段可用于保存题目原图、局部裁剪或处理后的教学素材。当前 3 个 Benchmark 的 `imageRefs` 均为空，项目不运行检测、OCR 或图像到拓扑的评测。

### 6.3 首批 Benchmark 样例

首批 3 个 Benchmark 覆盖了模电课程的三个核心题型：

| 样例 | 题型 | 元件 | 核心知识点 |
|------|------|------|-----------|
| `diode-limiter` | 二极管限幅电路 | 二极管、电阻、电压源 | 分段线性分析、正向导通 |
| `bjt-common-emitter-bias` | BJT 共射静态工作点 | NPN BJT、电阻、电压源 | 直流偏置、基极电流、集电极电流 |
| `mos-common-source` | MOS 共源放大电路 | MOSFET、电阻、电容、电压源 | 小信号分析、增益、输入/输出电阻 |

---

## 7. 校验器设计论证

### 7.1 校验器的定位

校验器（`validateCircuitGraph`）是数据层的核心组件，其设计原则是：

1. **不阻断，只报告**: 校验器返回 `ValidationIssue[]`，不抛出异常。这允许系统在图像识别阶段产生"不完美"的候选拓扑，由用户确认后再进入分析。
2. **分层严重性**: `error`（必须修复）、`warning`（建议修复）、`info`（提示信息）
3. **路径定位**: 每个 issue 携带 `path` 字段，精确定位问题在数据结构中的位置

### 7.2 校验规则清单

| 校验项 | 严重性 | 代码 | 说明 |
|--------|--------|------|------|
| 重复节点 ID | error | `duplicate_node_id` | 节点 ID 必须全局唯一 |
| 重复元件 ID | error | `duplicate_component_id` | 元件 ID 必须全局唯一 |
| 重复连接 ID | error | `duplicate_connection_id` | 连接 ID 必须全局唯一 |
| 重复端口 ID | error | `duplicate_graph_port_id` | 图端口 ID 必须全局唯一 |
| 未知元件定义 | error | `unknown_component_definition` | 元件实例引用的定义必须存在 |
| 缺失必需端口 | error | `missing_required_port` | 元件实例必须包含定义中标记为必需的端口 |
| 非法端口 | error | `invalid_component_port` | 元件实例的端口必须在定义中声明 |
| 非法模型变体 | error | `invalid_model_variant` | 模型变体必须在允许列表中 |
| 缺失必需参数 | error | `missing_required_parameter` | 必需参数必须有值 |
| 参数类型错误 | error | `invalid_parameter_type` | 参数值类型必须与定义匹配 |
| 参数单位错误 | error | `invalid_parameter_unit` | 参数单位必须与定义匹配 |
| 参数越界 | error | `parameter_below_minimum` / `parameter_above_maximum` | 数值参数必须在范围内 |
| 参数值不允许 | error | `parameter_value_not_allowed` | 枚举参数值必须在允许列表中 |
| 未知参数 | warning | `unknown_parameter` | 非定义中的参数降级为 warning |
| 连接引用未知节点 | error | `connection_unknown_node` | 连接的节点必须存在 |
| 连接引用未知元件 | error | `connection_unknown_component` | 连接的元件必须存在 |
| 连接引用未知端口 | error | `connection_unknown_port` | 连接的端口必须在元件上 |
| 端口多次连接 | error | `port_connected_multiple_times` | 每个端口只能连接一个节点 |
| 端口未连接 | error | `unconnected_component_port` | 所有端口必须连接到节点 |
| 悬空节点 | error | `dangling_node` | 非参考节点至少连接 2 个端口 |
| 图端口引用未知节点 | error | `graph_port_unknown_node` | 图端口的节点必须存在 |

### 7.3 校验器的算法复杂度

校验器的时间复杂度为 **O(N)**，其中 N 是节点数 + 元件数 + 连接数。核心数据结构使用 `Map` 和 `Set` 实现 O(1) 查找：

```
构建 Map: O(N)
遍历连接: O(C)  C = 连接数
遍历元件端口: O(P)  P = 总端口数
遍历节点: O(N)
总计: O(N + C + P) ≈ O(N)
```

---

## 8. 跨语言互操作方案

### 8.1 数据流架构

```text
TypeScript (前端 + 数据层)                Python (分析引擎 + API 服务)
┌─────────────────────────┐              ┌─────────────────────────┐
│ Zod Schema (事实来源)    │              │ Pydantic Model (镜像)    │
│ - 编译期类型检查         │              │ - 运行时校验             │
│ - 运行时校验             │              │ - 类型提示               │
└───────────┬─────────────┘              └───────────▲─────────────┘
            │                                        │
            │ zod-to-json-schema                     │ 读取 JSON Schema
            ▼                                        │
┌─────────────────────────┐                          │
│ JSON Schema 文件         │ ─────────────────────────┘
│ generated/schema/*.json  │
└─────────────────────────┘
            │
            │ JSON 序列化/反序列化
            ▼
┌─────────────────────────┐
│ JSON 数据文件            │
│ benchmarks/examples/*.json │
│ jobs/*.json             │
└─────────────────────────┘
```

### 8.2 JSON Schema 导出流程

```typescript
// scripts/export-json-schema.ts
import { zodToJsonSchema } from "zod-to-json-schema";
import { circuitGraphSchema, componentDefinitionSchema, benchmarkCaseSchema } from "../packages/schema/src/index.js";
import fs from "fs";

const schemas = {
  "circuit-graph": circuitGraphSchema,
  "component-definition": componentDefinitionSchema,
  "benchmark-case": benchmarkCaseSchema
};

for (const [name, schema] of Object.entries(schemas)) {
  const jsonSchema = zodToJsonSchema(schema, { $refStrategy: "none" });
  fs.writeFileSync(
    `generated/schema/${name}.schema.json`,
    JSON.stringify(jsonSchema, null, 2)
  );
}
```

### 8.3 Python 端消费方式

```python
# Python 端通过 JSON Schema 验证输入
import json
from pydantic import BaseModel, TypeAdapter

# 方式 1: 手动定义对称的 Pydantic model
class CircuitGraph(BaseModel):
    id: str
    version: str
    nodes: list[Node]
    components: list[ComponentInstance]
    connections: list[Connection]
    # ...

# 方式 2: 从 JSON Schema 自动生成 (需要 pydantic v2 + jsonschema)
import jsonschema
with open("generated/schema/circuit-graph.schema.json") as f:
    schema = json.load(f)
jsonschema.validate(instance=data, schema=schema)
```

---

## 9. 文献综述与相关研究

### 9.1 电路图数字化表示的标准

电路图的数字化表示有悠久的标准化历史：

- **SPICE Netlist** (Nagel & Pederson, 1973): 最早的电路图数字化表示，以文本方式描述元件和连接。SPICE netlist 是"网表"范式的鼻祖，至今仍是 EDA 工具的事实标准交换格式。
- **EDIF (Electronic Design Interchange Format)** (IEEE 1481, 1999): 电子设计交换格式标准，支持网表、原理图和版图的统一表示。过于复杂，工业界实际使用率低。
- **KiCad S-Expression** (KiCad Team, 2013–present): KiCad EDA 的原理图文件格式，采用 S-Expression（类 Lisp 语法）存储元件实例、连线和属性。本项目的 NEC 范式与 KiCad 的数据模型高度相似。
- **Altium Designer** (Altium, 1990s–present): 商业 EDA 软件，采用专有的二进制格式存储原理图和 PCB。不开放格式规范。

### 9.2 电路图数据模型的学术研究

#### 图论表示

Circuit-GNN (He et al., 2019, ICML) 将电路表示为图（节点 = 元件/节点，边 = 连接），使用图神经网络进行电路性能预测。这一表示在 ML 领域被广泛采用，但丢失了多端口元件的端口语义。

#### 知识图谱表示

近期研究 (DAC/ICCAD 2022–2024) 探索了将电路表示为知识图谱的方法，其中元件和节点作为实体，连接关系和参数约束作为关系。这一表示适合复杂电路的推理，但序列化和存储效率低于 JSON。

#### 工业实践中的数据模型

| 工具/平台 | 数据模型 | 格式 | 本项目借鉴 |
|-----------|----------|------|-----------|
| KiCad | 元件实例 + 连线 + 节点 | S-Expression | NEC 三元组结构 |
| LTspice | 元件声明 + 网表 | SPICE netlist | 参数值的单位系统 |
| CircuitLab | 元件 + 端点 + 连线 | JSON API | Web 端数据传输模式 |
| ngspice | 网表 + 子电路 | SPICE netlist | 模型变体（.model）概念 |
| SymPy Electrical | 符号元件 + 方程 | Python 对象 | 符号参数（如 `R`, `V`） |

### 9.3 Zod 在 TypeScript 生态中的地位

Zod (Colin McDonnell, 2020–present) 是 TypeScript 生态中最流行的运行时校验库之一：

- **GitHub Stars**: 35K+ (截至 2026 年)
- **周下载量**: 10M+ (npm)
- **引用项目**: tRPC, Next.js, Prisma, Fastify, LangChain 等
- **核心优势**: 零依赖、类型推导、组合式 API、JSON Schema 导出

Zod 的核心创新是 **Schema-as-Type** 模式：一个 Zod schema 同时是 TypeScript 类型定义和运行时校验器，消除了传统方案中"接口定义"与"校验逻辑"分离导致的不一致问题。

### 9.4 Benchmark 设计的学术参考

本项目的多层标准答案设计参考了以下评测方法论：

- **SQuAD** (Rajpurkar et al., 2016): 问答评测的多层答案设计——每道题有多个可接受的答案表达式，与本项目的 `acceptedEquivalentExpressions` 概念一致
- **GLUE** (Wang et al., 2018): 多任务评测基准——不同子任务测试模型的不同能力，与本项目的分层评测（识别/分析/输出）概念一致
- **DeepPCB** (Tang et al., 2019): PCB 缺陷检测评测——每张图有精确的 bbox 标注和缺陷类型标注，与本项目的电路图标注规范概念一致

---

## 10. 风险分析与应对策略

| 编号 | 风险 | 概率 | 影响 | 应对策略 |
|------|------|------|------|----------|
| R1 | Zod schema 与 Pydantic model 不一致 | 中 | 高 | (1) JSON Schema 作为中间契约，两端都从 Schema 生成/校验；(2) 契约测试确保两端校验结果一致 |
| R2 | Schema 演进导致已有 Benchmark JSON 失效 | 中 | 中 | (1) 语义化版本号 (`version` 字段)；(2) 向后兼容的扩展策略（新增字段可选）；(3) 迁移脚本 |
| R3 | 元件定义遗漏导致分析引擎报错 | 低 | 中 | (1) 元件定义的"未知元件"降级为 warning 而非 error；(2) 编辑器允许手动添加自定义元件 |
| R4 | 端口角色枚举不完整 | 低 | 低 | 枚举值可扩展，新增角色不影响已有数据 |
| R5 | Benchmark 标准答案有误 | 中 | 高 | (1) 人工审核每道 Benchmark；(2) 双人独立标注 + 冲突解决；(3) 版本化管理，错误修复可追溯 |
| R6 | 性能瓶颈：大电路图校验慢 | 低 | 低 | 当前 O(N) 算法对 30 题 Benchmark 无性能问题；后续扩展到 100+ 元件时可引入增量校验 |

---

## 11. 结论

1. **TypeScript + Zod 是本项目数据层的最优选择**: 它提供了编译期类型安全和运行时校验的双重保障，通过 JSON Schema 导出实现 TypeScript ↔ Python 的跨语言互操作，且与前端 TypeScript 生态天然集成。

2. **NEC（节点-元件-连接）三元组是电路图的最优表示范式**: 它与教科书表示一致、支持多端口元件和参数化、可导出为 SPICE 网表、与 KiCad 等工业工具的数据模型对齐。

3. **数据驱动的元件定义体系是分析引擎可扩展性的关键**: 通过五维定义（结构/参数/变体/分析/教学），元件定义同时驱动菜单渲染、端口校验、参数校验、分析策略选择和教学解释生成。

4. **多层标准答案的 Benchmark 结构支持分层评测**: 拓扑、方程求解和展示输出可以分别记录与校验；当前不包含视觉识别指标。

5. **校验器的"报告不阻断"设计适配人工编辑工作流**: 不完整或不一致的结构化电路图可以通过 `ValidationIssue` 逐条反馈给用户，由用户修正后再进入分析。

---

## 12. 参考文献

[1] McDonnell, C. (2020–present). *Zod: TypeScript-first schema validation with static type inference*. GitHub Repository. https://github.com/colinhacks/zod

[2] Nagel, L. W., & Pederson, D. O. (1973). *SPICE (Simulation Program with Integrated Circuit Emphasis)*. EECS Department, University of California, Berkeley. UCB/ERL M382.

[3] KiCad Team. (2013–present). *KiCad EDA*. https://www.kicad.org/

[4] He, H., et al. (2019). Circuit-GNN: Graph Neural Networks for Distributed Circuit Design. *ICML 2019*. https://github.com/hehaodele/circuit-gnn

[5] Rajpurkar, P., Zhang, J., Lopyrev, K., & Liang, P. (2016). SQuAD: 100,000+ Questions for Machine Comprehension of Text. *EMNLP 2016*. arXiv:1606.05250.

[6] Wang, A., Singh, A., Michael, J., Hill, F., Levy, O., & Bowman, S. R. (2018). GLUE: A Multi-Task Benchmark and Analysis Platform for Natural Language Understanding. *ICLR 2019*. arXiv:1804.07461.

[7] Tang, S., et al. (2019). DeepPCB: A benchmark for PCB defect detection. https://github.com/tangsanli5201/DeepPCB

[8] IEEE. (1999). *IEEE Standard 1481: EDIF (Electronic Design Interchange Format) Version 4 0 0*.

[9] Pydantic Team. (2023–present). *Pydantic: Data validation using Python type annotations*. https://docs.pydantic.dev/

[10] Collet, Y., et al. (2023–present). *zod-to-json-schema: Convert Zod schemas to JSON Schema*. https://github.com/StefanTerdell/zod-to-json-schema

---

> **文档版本**: v1.0
> **最后更新**: 2026 年 6 月 12 日
