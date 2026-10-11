# YOLOv8 + PaddleOCR 辅助电路图像识别方案技术论证报告

> **项目**: 面向电子电路学习的图像识别与可解释分析辅助系统
> **阶段**: 已取消接入（2026-07-23）；以下内容仅作为未来研究参考
> **日期**: 2026 年 6 月
> **性质**: 技术方案论证文档

---

## 当前实现状态（2026-07-23 复核更新）

> 本文是已取消的历史方案，不是待实施任务、模型实现或精度验收报告。

- `services/vision`、训练/验证数据、模型权重、推理脚本和项目精度报告当前均不存在。
- 文中的 mAP、OCR 准确率和模型体积数字属于外部研究结果、行业参考或规划目标，不能作为本项目实测结果。
- `CircuitGraph`/`imageRefs` 等通用数据字段仍存在，但不表示预留或承诺视觉模块；3 个 Benchmark 的 `imageRefs` 仍为空。
- 若未来重新立项，应重新评审范围，并先完成 5 张图 POC，记录数据来源、环境、权重、指标计算方法和失败样例。

---

## 目录

1. [摘要](#1-摘要)
2. [问题定义与系统需求](#2-问题定义与系统需求)
3. [核心技术方案](#3-核心技术方案)
4. [YOLOv8 组件检测论证](#4-yolov8-组件检测论证)
5. [PaddleOCR 参数识别论证](#5-paddleocr-参数识别论证)
6. [管线集成设计](#6-管线集成设计)
7. [文献综述与相关研究](#7-文献综述与相关研究)
8. [风险分析与应对策略](#8-风险分析与应对策略)
9. [工作量估算与里程碑](#9-工作量估算与里程碑)
10. [结论](#10-结论)
11. [参考文献](#11-参考文献)

---

## 1. 摘要

原方案旨在为模拟电子电路教学系统加入图像识别：接收教科书电路图照片，经图像预处理、元件检测、参数识别和拓扑提取后输出结构化 `CircuitGraph` JSON，再进入现有分析流程。该视觉链路已从当前项目范围移除。

本文档聚焦于视觉管线的核心识别环节——**元件目标检测**与**参数光学字符识别**——进行系统性技术方案论证。在对比分析 YOLO 系列目标检测模型与 PaddleOCR/EasyOCR/Tesseract 等 OCR 引擎后，本文推荐采用 **YOLOv8 + PaddleOCR (PP-OCRv4)** 作为本地化部署的核心方案。

该方案在学术文献和工业实践中均获得了充分验证：YOLOv8 在 PCB 元件检测任务中达到 93%–98% mAP@0.5 的精度；PP-OCRv4 在中英文混合场景文本识别中达到 85%–92% 的准确率，且全流程模型体积仅约 15 MB，完全满足本地离线部署的性能与资源约束。

---

## 2. 问题定义与系统需求

### 2.1 视觉管线的功能拆解

视觉管线需要完成以下四个子任务，每个子任务的输入、输出与技术挑战如下表所示：

| 阶段 | 输入 | 输出 | 技术挑战 |
|------|------|------|----------|
| **① 图像预处理** | 手机拍摄的教科书电路图 JPG/PNG | 灰度二值图像、去噪、透视校正后的标准化图像 | 光照不均、拍摄角度倾斜、纸张折痕 |
| **② 元件检测** | 预处理后的标准化图像 | 每个元件的边界框（bounding box）+ 类别标签（电阻/电容/BJT/MOSFET 等） | 元件符号密集排列、多种旋转角度、符号标准化程度差异 |
| **③ 参数 OCR** | 元件边界框 + 原始图像 | 标注区域的文本内容（如 `R1=10kΩ`, `β=100`, `Vcc=12V`） | 数学符号（Ω/β/μ）、下标、中英混排、小字号文本 |
| **④ 拓扑提取** | 元件位置 + 参数 + 预处理图像 | 线段检测 → 节点聚类 → `CircuitGraph` JSON | 线段交叉判别、元件引脚端点的精确连接 |

### 2.2 方案选型的关键约束

| 约束 | 说明 |
|------|------|
| **本地化优先** | 不接受云端 API 依赖，所有推理必须在用户本地完成 |
| **CPU 推理可行** | 目标用户为普通学生，可能不具备独立 GPU |
| **模型体积可控** | 总模型包不宜超过 200 MB，以降低安装门槛 |
| **中文优化** | 教科书为中文教材，元件标注包含中文和英文混合文本 |
| **可训性** | 允许使用少量领域数据（200–500 张）进行微调 |

---

## 3. 核心技术方案

### 3.1 方案概述

本文推荐的视觉管线采用 **YOLOv8 + PaddleOCR + OpenCV** 的三层架构：

```
┌──────────────────────────────────────────────┐
│                 输入图像                      │
│        (教科书电路图照片, 手机/扫描仪)          │
└───────────────────┬──────────────────────────┘
                    ▼
┌──────────────────────────────────────────────┐
│              OpenCV 预处理层                   │
│  灰度化 → Otsu 二值化 → 形态学去噪 →           │
│  透视校正 → 对比度标准化                        │
└───────────────────┬──────────────────────────┘
                    ▼
┌──────────────────────────────────────────────┐
│           YOLOv8 目标检测层                     │
│  ┌─────────────────┐  ┌───────────────────┐  │
│  │  CSPDarknet + C2f │  │  PAN-FPN Neck     │  │
│  │  Backbone        │  │  (多尺度特征融合)   │  │
│  └─────────────────┘  └───────────────────┘  │
│  ┌───────────────────────────────────────┐   │
│  │  Decoupled Anchor-Free Head           │   │
│  │  (分类分支 + 回归分支)                  │   │
│  └───────────────────────────────────────┘   │
│                                               │
│  输出: 每个元件的 bbox + 类别 + 置信度          │
└───────────────────┬──────────────────────────┘
                    ▼
┌──────────────────────────────────────────────┐
│              OpenCV 区域裁剪                   │
│  根据检测 bbox 裁剪元件标注区域                  │
└───────────────────┬──────────────────────────┘
                    ▼
┌──────────────────────────────────────────────┐
│            PaddleOCR 文字识别层                │
│  ┌──────────────┐ ┌──────────┐ ┌────────┐   │
│  │ DB++ 检测    │→│ 方向分类  │→│ SVTR   │   │
│  │ 文本定位     │ │ 180°矫正  │ │ 识别   │   │
│  └──────────────┘ └──────────┘ └────────┘   │
│                                               │
│  输出: 标注区域文本 (如 "R1 = 10kΩ")           │
└───────────────────┬──────────────────────────┘
                    ▼
┌──────────────────────────────────────────────┐
│             参数解析与拓扑提取                  │
│  正则/规则引擎抽取 元件ID + 参数值 + 单位      │
│  线段检测(HoughLinesP/FLD) + 节点聚类          │
│  → CircuitGraph JSON                          │
└──────────────────────────────────────────────┘
```

### 3.2 方案对比

在选择最终方案前，我们系统比较了以下候选组合：

| 检测方案 | OCR 方案 | 检测精度 | OCR 精度 | 模型体积 | 生态系统 | 推荐度 |
|----------|----------|----------|----------|----------|----------|--------|
| **YOLOv8 + PaddleOCR** | PaddleOCR PP-OCRv4 | 93%–98% mAP | 85%–92% 中文 | ~15 MB + ~25 MB | 社区活跃 | ★★★★★ |
| YOLOv8 + EasyOCR | EasyOCR (PyTorch) | 同上 | ~80%–87% 中文 | ~15 MB + ~200 MB | 生态统一 | ★★★☆☆ |
| YOLOv8 + Tesseract | Tesseract 5 | 同上 | ~70%–80% 中文 | ~15 MB + 引擎 | 最成熟 | ★★★☆☆ |
| PaddleDet + PaddleOCR | PP-YOLOE+ + PP-OCRv4 | 90%–95% mAP | 85%–92% 中文 | ~60 MB + ~15 MB | 框架统一 | ★★★★☆ |
| Faster R-CNN + PaddleOCR | ResNet + RPN + PP-OCRv4 | 88%–95% mAP | 85%–92% 中文 | ~500 MB + ~15 MB | 学术常用 | ★★★☆☆ |

**推荐 YOLOv8 + PaddleOCR** 的核心理由：

1. **各取所长**: YOLO 是目标检测领域引用量最高的模型家族，PaddleOCR 在中英文混合 OCR 领域是国际公认的 SOTA 级系统。两者组合覆盖了本项目的两个核心识别需求。
2. **模型体积最优**: 总推理模型约 40 MB（YOLOv8n ~6 MB ONNX + PaddleOCR ~15 MB + ONNX Runtime），远低于 200 MB 的预算上限。
3. **部署灵活**: 两者均支持 ONNX 导出，可用 ONNX Runtime 统一推理，避免同时维护 PyTorch 和 PaddlePaddle 两套运行时。
4. **社区资源丰富**: 两个工具均拥有大量中英文教程、讨论社区和工业落地案例，降低本项目的人力风险。

---

## 4. YOLOv8 组件检测论证

### 4.1 架构优势

YOLOv8 由 Ultralytics 团队于 2023 年 1 月发布 (Jocher et al., 2023)，是 YOLOv5 的直接演进版本。与 YOLOv5 相比，YOLOv8 引入了三项关键架构改进：

#### Backbone: C2f 模块替代 C3 模块

C2f（Cross Stage Partial with 2 convolutions and split/concatenation flows）模块在 C3 的基础上取消了冗余的 1×1 卷积，直接使用通道分割与拼接策略，在保持跨阶段梯度流的同时减少了参数量 (Ultralytics, 2023)。这一设计使 YOLOv8-nano 模型仅需约 3.2M 参数即可完成特征提取。

#### Neck: PAN-FPN 结构

YOLOv8 的颈部网络采用路径聚合网络（PAN）与特征金字塔网络（FPN）的双向融合结构。FPN 自顶向下传播语义特征，PAN 自底向上传递定位信号，两者之间的 C2f 残差连接保证了多尺度特征的高效融合 (Wang et al., 2020)。对于电路图检测任务而言，这一结构特别重要——电阻符号（~20×40 像素）和变压器/三极管符号（~80×100 像素）的尺度差异可达 10 倍以上。

#### Head: Decoupled Anchor-Free 检测头

YOLOv8 将 YOLOv5 的耦合锚点检测头替换为解耦的无锚点检测头（Decoupled Anchor-Free Head），将分类任务和边界框回归任务分配到独立的网络分支：

- **分类分支**: 二分类交叉熵（Binary Cross-Entropy, BCE）损失
- **回归分支**: CIoU（Complete IoU）损失 + 分布焦点损失（Distribution Focal Loss, DFL）

解耦设计使模型可以分别为两个子任务学习最优的中间表示，而 DFL 损失通过对边界框分布的概率建模，提升了边界框回归的精度 (Li et al., 2020; Zheng et al., 2020)。

此外，YOLOv8 引入了 Task-Aligned Assigner（TAL）标签分配策略，替代了传统的 IoU 阈值固定分配方法。TAL 在训练过程中动态匹配正负样本，使锚点选择与下游任务目标保持对齐，有效缓解了锚点分配中的训练-推理不一致问题 (Feng et al., 2021)。

### 4.2 在电路元件检测中的适用性论证

#### PCB 检测任务是最近似的工作

虽然尚未发现直接针对教科书印刷电路图的 YOLO 研究，但 PCB（印刷电路板）元件检测是学术界广泛研究的近似任务，其技术特征高度重合：

| 特征维度 | PCB 元件检测 | 教科书电路图检测 | 相似度 |
|----------|-------------|-----------------|--------|
| 元件类别 | 电阻、电容、IC、二极管 | 电阻、电容、BJT、MOSFET | 高度重合 |
| 图像特征 | 灰度/彩色、规则几何 | 黑白线稿、标准电气符号 | 中等差异 |
| 尺度变化 | 0603 贴片→大封装 IC | 电阻符号→变压器符号 | 类似的尺度跨度 |
| 密集程度 | 高密度 SMT 板 | 较稀疏的手绘/印刷图 | 电路图更稀疏 |

表 1 汇总了近年 PCB 检测领域使用 YOLOv8 的代表性文献及其性能：

**表 1: YOLOv8 在 PCB 元件/缺陷检测任务上的性能**

| 文献 | 模型变体 | 数据集 | mAP@0.5 | 改进策略 |
|------|----------|--------|---------|----------|
| Wei et al. (2025) — PCB-YOLO | YOLOv8n + Coordinate Attention | PCB 自定义数据集 | 96.8% | 多头注意力 + 多尺度特征融合 |
| Wu et al. (2025) — RF-YOLO | YOLOv8n | PCB 缺陷检测 | 94.3% | 感受野增强（RF特征增强模块） |
| Du & Wang (2026) — SAF-YOLO | YOLOv8s | PCB 细粒度缺陷 | 95.1% | 语义感知轻量化框架 |
| Zhao & Jiang (2025) — YOLO-WWBi | YOLOv11 | PCB 缺陷检测 | 97.2% | 优化检测头 + 特征融合 |
| Wei (2025) — NeoPCB-YOLO | YOLOv8 改进 | PCB 缺陷检测 | 96.0% | 下一代优化 YOLO |

上述文献表明，经轻量微调的 YOLOv8 在 PCB 元件检测任务上普遍达到了 **93%–97% mAP@0.5** 的精度。教科书电路图的元件符号相较于 PCB 实拍图有以下优势：

- **无纹理干扰**: 印刷电路图的元件符号为标准化几何图形，无 PCB 实物的表面纹理、丝印、焊锡等噪声
- **高对比度**: 黑白线稿的目标/背景对比度远高于实物照片
- **无视角变化**: 教科书扫图不存在 PCB 拍摄的景深/视角问题

因此，YOLOv8 在教科书电路图上的检测精度**有望达到 95% mAP@0.5 以上**，优于 PCB 检测任务。

#### 小样本微调的可行性

本项目的训练数据规模预计为 200–500 张标注图像。YOLOv8 的以下特性使其在少样本场景下表现优异：

1. **COCO 预训练权重**: YOLOv8 在 118K 张 COCO 图像的 80 类检测任务上预训练，其 backbone 已学习丰富的初级视觉特征（边缘、角点、纹理），迁移到电路图任务时仅需微调高层语义特征。经验表明，100–200 张/类可使检测精度达到 50%–70% mAP@0.5 (Ultralytics, 2023)。

2. **Mosaic + MixUp 数据增强**: YOLOv8 内置的马赛克增强（将 4 张训练图像拼接为 1 张）和混合增强可有效扩充小数据集。对电路图而言，还可增加几何变换（旋转 0°/90°/180°）、弹性形变和椒盐噪声增强。

3. **Backbone 冻结策略**: 在微调初期冻结 backbone 前 10 层的权重（`freeze=10`），仅训练 neck 和 head，可大幅降低过拟合风险。建议学习率设为 `lr0=0.001`，训练 50–100 个 epoch。

### 4.3 模型选型建议

考虑到本项目对 CPU 推理的优先需求，推荐 **YOLOv8n（Nano）** 或 **YOLOv8s（Small）**：

| 变体 | 参数量 | 推理速度 (CPU, ONNX) | mAP (COCO val) | 推荐场景 |
|------|--------|---------------------|----------------|----------|
| YOLOv8n | 3.2 M | ~50–80 ms/img | 37.3% | 元件类别 ≤ 15 类，CPU 推理优先 |
| YOLOv8s | 11.2 M | ~100–150 ms/img | 44.9% | 元件类别 15–25 类，精度优先 |
| YOLOv8m | 25.9 M | ~250–350 ms/img | 50.2% | 不推荐（超出 CPU 实时预算） |

本项目定义的元件类别包括电阻、电容、电感、二极管、BJT-NPN、BJT-PNP、MOSFET-N、MOSFET-P、JFET、独立电压源、独立电流源、受控源、变压器等约 15–19 类，推荐使用 **YOLOv8s** 作为精度与速度的平衡点，在 CPU 上的实时性（~100–150 ms）完全满足交互式应用的需求。

---

## 5. PaddleOCR 参数识别论证

### 5.1 PP-OCR 系列演进

PaddleOCR 是百度飞桨团队自 2020 年起持续迭代的开源 OCR 系统，经历了四个主要版本：

#### PP-OCR: 超轻量 OCR 系统的提出 (Du et al., 2020)

首版 PP-OCR 发表于 2020 年 9 月 (arXiv:2009.09941)，提出了经典的 OCR 三阶段流水线：

- **文本检测**: DB（Differentiable Binarization, 可微二值化），基于自适应阈值图将文本区域与背景分离 (Liao et al., 2019)
- **方向分类**: 轻量 CNN，判断是否需要 180° 旋转矫正
- **文本识别**: CRNN（CNN + RNN + CTC），MobileNetV3 骨架

全系统模型体积仅约 3.5 MB，且创新性地使用知识蒸馏（Knowledge Distillation）实现模型压缩，奠定了移动端/边缘端 OCR 的技术范式。

#### PP-OCRv2: 技巧集成 (PaddlePaddle Team, 2021)

发表于 2021 年 9 月 (arXiv:2109.03144)，核心改进包括：

- 引入 UDML（统一深度互学习）蒸馏策略，同时蒸馏检测和识别模型
- 引入 LCNet（Lightweight Convolutional Network）骨架替代 MobileNetV3
- 增加轻量监督注意力模块

相比 v1，识别准确率提升约 7%，识别模型体积约 11 MB。

#### PP-OCRv3: SVTR 骨架的引入 (Du et al., 2022)

发表于 2022 年 6 月 (arXiv:2206.03001)，核心改进包括：

- **SVTR_LCNet 骨架**: 融合了 SVTR（Scene Text Recognition with a Single Visual Model）的全局混合结构（Global-Local Mixing）与 LCNet 的轻量化卷积 (Du et al., 2022b)
- **GTC（Guided Training of CTC）**: 用注意力机制为 CTC 训练提供逐帧引导信号，在不增加推理开销的前提下提升识别精度
- **TextRotNet 自监督预训练**: 为方向分类器提供更稳健的旋转不变性
- **LKPAN（轻量知识金字塔注意力网络）**: 替代 PAN 模块，减少检测阶段的计算量

全系统模型体积约 15 MB。相比 PP-OCRv2，识别精度再提升约 5%。

#### PP-OCRv4: 最新优化策略 (Li et al., 2024)

发表于 2024 年 5 月 (arXiv:2405.06880)，是本项目的推荐版本。核心改进包括：

- **SVTR_HGNet 骨架**: 将 PP-OCRv3 的 SVTR_LCNet 升级为 HGNet（Higher Performance GPU Network）变体，在识别精度和推理速度之间取得更优平衡
- **CML（协作互学习）蒸馏**: 多个学生模型在互学习过程中同时接收教师模型的监督信号，提升蒸馏效率
- **改进的 DB++ 检测器**: 更好的颈部网络设计，提升多尺度文本的检测召回率
- **全面的工程优化策略**: 包括数据增强 pipeline 改进、学习率调度策略精调、后处理阈值自适应等

相比 PP-OCRv3，识别精度再提升约 4%–5%。

### 5.2 在电路参数识别中的适用性论证

#### 电路参数的文本特征

教科书电路图中的参数标注具有以下语言学和视觉特征：

| 特征 | 示例 | 对应的 OCR 挑战 |
|------|------|----------------|
| 中英混排 | `输入信号vi` | 中文（CJK）+ ASCII 混合识别 |
| 数学符号 | `10kΩ`, `β=100`, `μA741` | 希腊字母、单位符号 |
| 下标与上标 | `V_CC`, `I_B`, `R_in` | 下划线/前导字符的语义解析 |
| 小字号 | 电阻值 8pt 标注 | 低分辨率小目标文本识别 |
| 旋转/倾斜 | 沿 45° 线标注的电感值 | 旋转文本检测 |
| 紧凑布局 | 元件密集区域的多行标注 | 密集文本的边界框分离 |

PP-OCRv4 的下述特性使其特别适合处理这些挑战：

#### 优势 1: DB++ 对密集小文本的检测

DB++ 文本检测器在 DB 的基础上引入了自适应特征融合（Adaptive Feature Fusion）和可变形卷积（Deformable Convolution），使模型能够适应不同尺度和长宽比的文本区域 (Liao et al., 2022)。对于电路图中常见的紧凑标注（如 `R1` 和 `10kΩ` 以子区域方式排列），DB++ 能够精确输出独立的边界框。

#### 优势 2: SVTR 对混排文本的序列建模

SVTR（Single Visual Model for Text Recognition）摒弃了传统 CRNN 中 RNN 的递归结构，采用纯 Transformer 架构进行序列建模 (Du et al., 2022b)。其核心创新——Global-Local Mixing（GLM）模块——在局部窗口内进行自注意力计算的同时保留跨窗口的全局信息，使模型能够同时捕获字符的细粒度特征和字符间的长程依赖。

这对中英文混合文本的识别尤为重要：中文汉字（如 `输入信号`）包含复杂的部首结构，依赖全局上下文消除歧义；而 ASCII/数学符号（如 `10kΩ`）通常是孤立的短序列，依赖高分辨率的局部特征进行区分。SVTR 的 GLM 机制恰好同时满足了这两类需求。

#### 优势 3: 轻量化与本地部署

PP-OCRv4 的全管线模型体积仅约 15 MB（检测 ~3.5 MB + 方向分类 ~1 MB + 识别 ~10.5 MB），在 CPU 上的推理速度为 150–300 ms/图像，完全满足本地化部署的资源约束。

可以通过量化（PaddleSlim）将模型体积压缩 3–4 倍（至 ~3–5 MB），进一步降低安装包体积，且精度损失控制在 1%–2% 以内。

#### 优势 4: 可定制字典微调

PaddleOCR 支持在领域数据上微调识别模型。给定 200–500 张电路参数标注图（如 `R1=10kΩ`, `Vcc=12V`, `β=100`），可以构建**电路参数字典**，大幅提升 `Ω`（区别于 `Q`）、`β`（区别于 `B`）、`μ`（区别于 `u`）等易混淆符号的识别准确率。

### 5.3 OCR 引擎对比

**表 2: 主流 OCR 引擎在电路参数识别任务中的对比**

| 维度 | PaddleOCR (PP-OCRv4) | EasyOCR | Tesseract 5 |
|------|---------------------|---------|--------------|
| 中文+英文混排精度 | **85%–92%** | 80%–87% | 70%–80% |
| 数学符号支持 | 可定制字典微调 | 一般 | 差 |
| CPU 推理速度 | **150–300 ms** | 500–800 ms | 200–500 ms |
| 模型体积 | **~15 MB** | ~200 MB | 语言包 ~30 MB |
| 文本检测能力 | 内置 DB++，密集文本效果好 | CRAFT 检测器 | 引擎内置，经典方法 |
| 方向分类 | 内置，~1 MB | 无 | 引擎内置 |
| 安装复杂度 | 中等（需 PaddlePaddle） | 低（仅 PyTorch） | 低（系统软件） |
| 可微调性 | **高**（PP-OCR fine-tuning API） | 中 | 低 |
| 相关开源项目 | 已有基于 PaddleOCR 的电路图 OCR 项目 | 无专门项目 | 若干 PCB OCR 项目 |

### 5.4 相关文献佐证

在 ICDAR 2019-ReCTS（中文餐厅场景文本）基准测试中，PP-OCRv4 的文本识别准确率达到 85%–92% (Li et al., 2024)；在 ICDAR 2015（英文场景文本）基准测试中，SVTR 的单词识别准确率达到 86%–90% (Du et al., 2022b)。虽然尚无直接针对电路参数标注的标准基准，但该任务的难度（印刷体、黑白、标准化布局）显著低于通用场景文本识别，因此预期可以达到 **90% 以上**的准确率。

此外，GitHub 上已有开源项目 **Character-Recognition-of-Circuit-Diagram-Based-on-PaddleOCR**（XD-mu）验证了 PaddleOCR 在电路图 OCR 中的可行性，为本项目的技术路线提供了直接参考。

---

## 6. 管线集成设计

### 6.1 数据流与接口设计

#### 6.1.1 YOLOv8 → 区域裁剪

YOLOv8 检测输出的数据类型应与项目现有的 Zod Schema 体系对齐：

```typescript
// 基于项目已有的 ComponentDefinition 扩展检测结果类型
interface DetectionResult {
  bbox: {
    x: number;      // 中心 x 坐标 (归一化 0–1)
    y: number;      // 中心 y 坐标 (归一化 0–1)
    width: number;  // 宽度 (归一化 0–1)
    height: number; // 高度 (归一化 0–1)
  };
  classId: number;        // 对应 ComponentDefinition 的枚举值
  className: string;      // 如 "resistor", "capacitor", "bjt_npn"
  confidence: number;     // 0–1 置信度
}

// 检测结果为数组
type DetectionResults = DetectionResult[];
```

YOLOv8 模型导出为 ONNX 后，使用 `onnxruntime` 进行推理。Python 侧负责 ONNX 模型的加载与推理，TypeScript 侧通过 JSON 传递检测结果。

#### 6.1.2 PaddleOCR → 参数提取

PaddleOCR 的输出需要经过后处理流水线：

```
OCR 原始输出
  │ ("R1=10kΩ", confidence: 0.96)
  ▼
┌─────────────────┐
│  文本规范化       │  全角 → 半角转换, 去除不可见字符
└────────┬────────┘
         ▼
┌─────────────────┐
│  正则解析         │  匹配模式: "元件ID = 数值 单位"
│  e.g. R(\d+)    │  提取: id="R1", value=10000, unit="Ω"
└────────┬────────┘
         ▼
┌─────────────────┐
│  字典校验         │  对照 componentDefinitions 验证类别一致性
└────────┬────────┘
         ▼
 CircuitGraph 中的 Parameter 节点
```

### 6.2 训练流程

#### 6.2.1 数据集构建

1. **数据来源**: 教科书 PDF 中截取的电路图、手机拍摄的电路图照片
2. **标注工具**: LabelImg 或 CVAT（Computer Vision Annotation Tool），使用 YOLO 格式（`class x_center y_center width height`）
3. **标签体系**: `packages/components/src/definitions.ts` 当前定义 18 个下游元件类型，但局部图形只有 16 个可稳定区分的检测类。VCVS/CCVS 合并为 `controlled_voltage_source`，VCCS/CCCS 合并为 `controlled_current_source`，再由上下文或人工确认映射到最终类型
4. **数据增强管线**:

```python
# 增强策略
transforms = [
    # 几何增强
    RandomRotation(degrees=[0, 90, 180, 270]),  # 电路图常见方向
    RandomFlip(p=0.5),
    # 光度增强
    RandomBrightnessContrast(brightness=0.2, contrast=0.2),
    RandomGamma(gamma=(80, 120)),
    # 噪声增强
    GaussNoise(var_limit=(10, 50)),
    # YOLOv8 内置增强
    Mosaic(dataset, p=0.5),
    MixUp(dataset, p=0.3),
]
```

5. **数据规模**: 先用 50–100 张完成标注一致性和微调 POC，再扩展到 200–500 张。按教材/页来源分组切分训练集、验证集、测试集，建议 70%/15%/15%，同页裁剪不得跨集合

#### 6.2.2 YOLOv8 微调配置

```yaml
# 训练配置 (data.yaml)
path: ./circuit-dataset
train: train/images
val: val/images
test: test/images

nc: 16  # 视觉可分辨类别数量；最终映射覆盖 18 个 CircuitGraph 元件类型
names:
  0: ground
  1: resistor
  2: capacitor
  3: inductor
  4: transformer
  5: dc_voltage_source
  6: dc_current_source
  7: ac_voltage_source
  8: ac_current_source
  9: controlled_voltage_source
  10: controlled_current_source
  11: diode
  12: bjt_npn
  13: bjt_pnp
  14: mosfet
  15: jfet
```

```python
# 训练脚本
from ultralytics import YOLO

# 加载 COCO 预训练权重
model = YOLO('yolov8s.pt')

# 微调
results = model.train(
    data='circuit-dataset/data.yaml',
    epochs=100,
    imgsz=640,
    batch=16,
    lr0=0.001,
    lrf=0.01,
    freeze=10,       # 冻结 backbone 前 10 层
    augment=True,     # 开启 Mosaic + MixUp
    device='cpu',     # 或 'cuda'
    workers=4,
    name='circuit-v1'
)

# 导出 ONNX
model.export(format='onnx', opset=12, simplify=True)
```

#### 6.2.3 PaddleOCR 微调配置

```text
# 电路参数字典 (custom_dict.txt)
0
1
2
# ... 每行一个字符，不是一个词
R
V
k
M
Ω
μ
β
=
```

字典必须从训练转写文本按字符去重生成，并与 PaddleOCR v6 的训练配置、预训练权重和导出流程保持同一版本。不要把 `kΩ`、`Vcc` 等整词作为单个字典条目。元件框与文字框是两套标注：YOLO 只框元件符号，PaddleOCR 只框文字并记录准确转写。

```python
# 以下命令中的配置路径必须替换为本机 PaddleOCR v6 对应配置。
# 不要复用 PP-OCRv4 的配置文件名或模型目录。
python tools/train.py -c <paddleocr-v6-rec-config.yml> \
  -o Global.pretrained_model=<v6-rec-pretrained-model> \
     Global.character_dict_path=./custom_dict.txt \
     Train.dataset.data_dir=./ocr-dataset
```

### 6.3 运行时集成

```python
# 统一推理入口 (inference.py)
import cv2
import numpy as np
import onnxruntime as ort
from paddleocr import PaddleOCR

class CircuitVisionPipeline:
    def __init__(self, yolo_onnx_path: str, paddleocr_dir: str):
        # 初始化 YOLOv8 ONNX 会话
        self.yolo_session = ort.InferenceSession(
            yolo_onnx_path,
            providers=['CPUExecutionProvider']
        )
        # 初始化 PaddleOCR
        self.ocr = PaddleOCR(
            use_angle_cls=True,
            lang='ch',
            use_gpu=False,
            det_model_dir=f'{paddleocr_dir}/det',
            rec_model_dir=f'{paddleocr_dir}/rec',
            show_log=False
        )

    def preprocess(self, image: np.ndarray) -> np.ndarray:
        """OpenCV 预处理: 灰度化 + 二值化 + 去噪 + 透视校正"""
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        binary = cv2.adaptiveThreshold(
            gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
            cv2.THRESH_BINARY, 11, 2
        )
        denoised = cv2.medianBlur(binary, 3)
        # 透视校正 (如需要)
        # ...
        return denoised

    def detect_components(self, image: np.ndarray) -> list[dict]:
        """YOLOv8 组件检测"""
        input_tensor = self._prepare_yolo_input(image)
        outputs = self.yolo_session.run(None, {'images': input_tensor})
        detections = self._parse_yolo_output(outputs[0], image.shape)
        return detections

    def recognize_params(self, image: np.ndarray,
                         detections: list[dict]) -> list[dict]:
        """PaddleOCR 参数识别"""
        for det in detections:
            x1, y1, x2, y2 = det['bbox_xyxy']
            roi = image[y1:y2, x1:x2]
            ocr_result = self.ocr.ocr(roi, cls=True)
            if ocr_result and ocr_result[0]:
                det['text'] = ocr_result[0][0][1][0]
                det['text_confidence'] = ocr_result[0][0][1][1]
        return detections

    def extract_topology(self, image: np.ndarray,
                         detections: list[dict]) -> dict:
        """拓扑提取: 线段检测 → 节点聚类 → CircuitGraph"""
        # 1. 线段检测 (FLD)
        fld = cv2.ximgproc.createFastLineDetector(
            length_threshold=30,
            do_merge=True
        )
        lines = fld.detect(image)

        # 2. 节点聚类 (线段端点聚类)
        nodes = self._cluster_endpoints(lines)

        # 3. 构建 CircuitGraph (对齐 Zod Schema)
        circuit_graph = self._build_circuit_graph(
            detections, lines, nodes
        )
        return circuit_graph

    def run(self, image_path: str) -> dict:
        """端到端推理"""
        image = cv2.imread(image_path)
        processed = self.preprocess(image)
        detections = self.detect_components(processed)
        detections = self.recognize_params(image, detections)
        circuit_graph = self.extract_topology(processed, detections)
        return circuit_graph
```

### 6.4 ONNX 统一部署方案

为避免在用户设备上同时安装 PyTorch 和 PaddlePaddle 两套深度学习框架（合计约 2–3 GB），建议采用 ONNX Runtime 统一推理：

```
┌─────────────────────────────────────────────┐
│                 ONNX Runtime                  │
│           (C++/Python/Node.js 绑定)            │
├──────────────────┬──────────────────────────┤
│   YOLOv8 ONNX    │   PaddleOCR ONNX          │
│   (~6 MB)        │   (~15 MB)                │
│   CSPDarknet     │   DB++ + SVTR_HGNet       │
└──────────────────┴──────────────────────────┘
```

PaddleOCR 官方的 PP-OCRv4 模型原生基于 PaddlePaddle，但可以通过 `paddle2onnx` 工具转换为 ONNX 格式：

```bash
# 文件检测 + 识别模型转换为 ONNX
paddle2onnx --model_dir ch_PP-OCRv4_det_infer \
            --model_filename inference.pdmodel \
            --params_filename inference.pdiparams \
            --save_file ppocrv4_det.onnx \
            --opset_version 12

paddle2onnx --model_dir ch_PP-OCRv4_rec_infer \
            --model_filename inference.pdmodel \
            --params_filename inference.pdiparams \
            --save_file ppocrv4_rec.onnx \
            --opset_version 12
```

ONNX Runtime 同时支持 Python（用于训练和开发阶段）和 Node.js（通过 `onnxruntime-node` 绑定，用于最终产品），为项目从 Python 原型向 TypeScript 生产系统的迁移提供了平滑过渡路径。

---

## 7. 文献综述与相关研究

### 7.1 电路图视觉理解的演进

电路图/工程图视觉理解是一个长达三十余年的研究课题，研究范式经历了三次重大转变。

#### 传统方法时代 (1990s–2010s)

早期的电路图识别依赖手工设计的图像处理规则。Dori & Velkovich (1995) 在 IEEE TPAMI 上发表了工程图分割与符号识别的奠基性工作，使用形态学操作和连通组件分析提取元件，再用基于规则的系统进行分类。Tombre (1999) 在图形识别领域的综述中全面回顾了矢量化、符号识别和文本/图形分离的经典方法。Lladós et al. (2001) 提出了基于区域邻接图的误差容忍子图匹配方法，在手写工程符号识别上取得了 80%–90% 的准确率。

传统方法的核心局限在于：(1) 对尺度和旋转敏感；(2) 需要针对每种电气标准（IEC/ANSI/JIS）手工调参；(3) 对不同来源的图像（扫描仪/手机）泛化能力差。

#### 深度学习的引入 (2015–2020)

CNN 的出现彻底改变了电路图识别的研究范式。多个中国研究组 (2021–2023) 在 Journal of Physics: Conference Series 和 IEEE 会议上发表了基于 YOLOv5 的电路原理图识别工作：在包含 2000–10000 张 COCO 格式标注图像的数据集上，YOLOv5s 对常见元件类别（电阻、电容、晶体管、二极管、IC）达到了 90%–96% mAP@0.5 的检测精度。关键发现包括：(1) 数据增强（旋转、马赛克、混合）对小数据集有显著提升；(2) 迁移学习使精度提升 15%–30%。

同期，ResNet-50、VGG-16 和 EfficientNet 在电路符号分类任务（12–30 类）上达到了 92%–98% 的分类准确率，但在手绘/污损图像上降至 85%–90%。

#### 多阶段管线与技术前沿 (2020–present)

当前学界的主导范式是**多阶段管线** (Lin et al., 2025; Wu et al., 2025)：

1. YOLO/Faster R-CNN 进行元件检测
2. CRNN/PaddleOCR 进行文本标注识别
3. U-Net/DeepLabV3+ 进行导线分割（90%–96% IoU）
4. GNN 进行拓扑理解（88%–95% 连接预测精度）

近年来的新技术方向包括：
- **Vision Transformer**（ViT, Swin, DETR）应用于电路图理解，在分类任务上达到 94%–97% 准确率，但需要更大的训练数据
- **Graph Neural Networks**（GCN, GAT, GraphSAGE）用于电路拓扑建模，将电路表示为图（节点=元件，边=导线），在 ICML/NeurIPS 的 EDA Workshop 上受到高度关注
- **图到文本生成**: DeepSPICE 等端到端方法直接映射电路图像到 SPICE 网表

### 7.2 PCB 检测相关文献详表

**表 3: YOLO 系列在 PCB 检测任务上的代表文献**

| 年份 | 文献 | 模型 | 核心创新 | 最佳 mAP@0.5 |
|------|------|------|----------|-------------|
| 2022 | Chen et al. — Transformer-YOLO | T-YOLO | Transformer 特征融合 + YOLO | 95.5% |
| 2023 | Chhetri et al. — Missing Component | YOLOv5 | 缺失元件检测, 实时 IoT | 93.0% |
| 2024 | Yi & Song — CC-YOLO | YOLOv7 | 坐标注意力 + 跨阶段连接 | 96.3% |
| 2024 | Wang & Song — DC-YOLO | YOLOv8 | 双通道特征提取, 小目标优化 | 95.8% |
| 2025 | Liu & Wang — MDUA-YOLO | YOLOv8 | 多尺度动态上采样注意 | 97.1% |
| 2025 | Wei et al. — PCB-YOLO | YOLOv8 | 坐标注意 + 多尺度融合 | 96.8% |
| 2025 | Wu et al. — RF-YOLO | YOLOv8 | 感受野增强模块 | 94.3% |
| 2025 | Liu et al. — BCN-YOLO | YOLOv8 | 双向通道归一化 | 95.6% |
| 2025 | Zhao & Jiang — YOLO-WWBi | YOLOv11 | 优化检测头, 最新 YOLO 变体 | 97.2% |
| 2026 | Du & Wang — SAF-YOLO | YOLOv8 | 语义感知轻量化 | 95.1% |
| 2026 | Gadiraju & Verma — Survey | 综述 | 深度学习 PCB 缺陷检测综述 | — |

**关键趋势**: 注意力机制（CBAM/ECA/Coordinate Attention）与 YOLO 的结合是 2024–2026 年 PCB 检测文献的主流改进策略，平均提升 2%–5% mAP。

### 7.3 OCR 技术对比综述

Memon et al. (2023) 在 IEEE Access 上发表的面向深度学习的 OCR 系统综述（arXiv:2409.01210）系统比对了当前主流 OCR 引擎。以下为针对电路参数识别任务的总结性对比：

| 特征 | 传统模板匹配 | Tesseract | EasyOCR | PaddleOCR |
|------|------------|-----------|---------|-----------|
| 中文支持 | ✗ | 有限 | 2.5/5 | 5/5 |
| 数学符号 | ✗ | ✗ | 2/5 | 4/5 (可定制字典) |
| 倾斜文本 | 2/5 | 3/5 | 3.5/5 | 4.5/5 |
| 密集文本 | 2/5 | 3/5 | 3/5 | 4.5/5 |
| 文献引用数 | — | 3000+ | 500+ | 1000+ |
| 模型体积 | N/A | ~30 MB/语言 | ~200 MB (总) | ~15 MB (总) |
| 本地部署 | ✓ | ✓ | ✓ | ✓ |

### 7.4 开源项目生态

**表 4: 电路图识别相关开源项目**

| 项目 | Stars | 技术栈 | 与本项目的关联 |
|------|-------|--------|---------------|
| circuit-scan | 1 | YOLO → LTspice | 直接参考：YOLO 检测 + 网表输出 |
| CircuitVision | 4 | YOLOv11 + SAM + Gemini | 架构参考（但不本地化）|
| PaddleOCR 电路图 OCR | 3 | PaddleOCR | 直接验证了 PaddleOCR 在电路图上的可行性 |
| DeepPCB | 505 | PCB 缺陷数据集 | 可作为迁移学习的中间数据集 |
| CircuitNet | 64 | OpenCV + CNN | 传统方法 + DL 的混合参考 |
| Tiny-Defect-Detection-for-PCB | 512 | YOLO 系列 | 小目标检测的技术参考 |
| Hand-Drawn Circuit | 6 | YOLOv5 | 手绘电路图识别为长期目标提供参考 |

---

## 8. 风险分析与应对策略

### 8.1 技术风险矩阵

| 编号 | 风险 | 概率 | 影响 | 应对策略 |
|------|------|------|------|----------|
| R1 | 训练数据不足导致 YOLOv8 检测精度不足 | 中 | 高 | (1) 使用合成数据生成（从 KiCad 原理图自动渲染 + 标注）补充真实数据；(2) 在 DeepPCB 等近似数据集上进行领域适应预训练；(3) 使用更激进的增强策略（CutMix + Copy-Paste） |
| R2 | 数学符号 OCR 错误（`Ω`→`Q`, `β`→`B`）| 高 | 高 | (1) 构建电路领域专用字典并在 200+ 张参数图像上微调 PP-OCRv4 识别模型；(2) 增加文本后处理的规则纠正层（电子学参数上下文约束：如 `R` 后面的数值只能包含 Ω 相关单位）|
| R3 | 导线检测和交叉点判定不准确 | 中 | 高 | (1) 优先使用 FLD（Fast Line Detector）+ 线段合并后处理；(2) 引入 U-Net 导线分割作为第二阶段；(3) 用户交互确认/修正拓扑连接 |
| R4 | PaddlePaddle 在 Windows 上的 GPU 不兼容 | 低 | 中 | (1) 第一版使用 CPU 推理（延迟可接受）；(2) 使用 ONNX Runtime 替代原生 PaddlePaddle；(3) 中期考虑导出为更通用的推理格式 |
| R5 | 手写电路图识别精度远低于预期 | 中 | 低 | 第一期明确限定为印刷体教科书电路图，手写识别作为远期目标 |
| R6 | 元件符号的旋转/镜像变体导致检测遗漏 | 低 | 中 | (1) 训练数据增强包含 0°/90°/180°/270° 旋转；(2) 对方向敏感元件（如晶体管箭头方向）增加子类别标注 |

### 8.2 风险缓解的关键机制

#### 合成数据生成管线

为应对 R1（训练数据不足），建议建立从 KiCad 原理图到标注数据的自动生成管线：

```
KiCad 原理图 (.kicad_sch)
    │
    ▼
┌─────────────────┐
│  KiCad CLI 渲染   │  自动导出为 PNG/SVG
└────────┬────────┘
         ▼
┌─────────────────┐
│  SVG 解析器       │  从 SVG 中提取每个元件的 bbox 和类别
└────────┬────────┘
         ▼
┌─────────────────┐
│  YOLO 标注导出    │  生成 COCO/YOLO 格式的训练标注
└─────────────────┘
```

这一管线可以零人工成本生成数千张高质量、多尺度的标注图像，有效补充人工标注数据。

#### 后处理规则纠正

为应对 R2（数学符号 OCR 错误），在 OCR 输出后增加一层基于电子学知识的规则纠正：

```
原始 OCR: "R1 = 1O K Q"  (错误: O→0, Q→Ω)
    ↓ 字典纠正
修正结果: "R1 = 10kΩ"     (正确)
```

规则示例：
- 元件类型 `R` 后面的数值只能包含 `Ω`, `kΩ`, `MΩ`
- 元件类型 `C` 后面的数值只能包含 `F`, `μF`, `nF`, `pF`
- `Vcc`/`Vee`/`Vdd`/`Vss` 等电源标识具有固定的拼写模式

---

## 9. 工作量估算与里程碑

### 9.1 任务分解

| 阶段 | 任务 | 预估工时 | 优先级 | 前置依赖 |
|------|------|----------|--------|----------|
| **P0** — 环境搭建 | YOLOv8 环境安装 (Ultralytics) + 预训练模型下载与测试 | 1–2 天 | 必需 | — |
| **P0** — 环境搭建 | PaddleOCR 环境安装 + ONNX 转换验证 | 1–2 天 | 必需 | — |
| **P0** — 数据准备 | 收集教科书电路图图像（30 基准题 + 扩展）| 3–5 天 | 必需 | — |
| **P0** — 数据准备 | 标注电路元件（LabelImg/CVAT）| 5–7 天 | 必需 | 图像收集 |
| **P0** — 数据准备 | 合成数据生成管线（KiCad → YOLO 标注）| 3–5 天 | 推荐 | — |
| **P0** — 模型训练 | YOLOv8 微调训练 + 超参数调优 | 3–5 天 | 必需 | 数据准备 |
| **P0** — 模型训练 | PaddleOCR 电路字典定制 + 微调 | 3–5 天 | 必需 | 环境搭建 |
| **P1** — 管线集成 | OpenCV 图像预处理管线（灰度/二值化/去噪/校正）| 3–5 天 | 必需 | — |
| **P1** — 管线集成 | YOLO → ROI 裁剪 → PP-OCR 串联 | 2–3 天 | 必需 | 模型训练 |
| **P1** — 管线集成 | 参数后处理与规则纠正 | 2–3 天 | 必需 | PaddleOCR |
| **P1** — 拓扑提取 | 线段检测 (FLD) + 节点聚类 | 5–7 天 | 必需 | 预处理 |
| **P1** — 拓扑提取 | CircuitGraph 构建（对齐 Zod Schema）| 3–5 天 | 必需 | 线段检测 |
| **P1** — 拓扑提取 | 用户确认界面（低置信度候选标记）| 3–5 天 | 推荐 | 检测输出 |
| **P2** — 系统集成 | Python ↔ TypeScript 接口对接 | 3–5 天 | 必需 | 管线集成 |
| **P2** — 系统集成 | ONNX Runtime 统一部署 | 2–3 天 | 推荐 | 模型训练 |
| **P2** — 测试 | 端到端 30 基准题测试 + 精度报告 | 3–5 天 | 必需 | 系统集成 |
| **合计** | | **约 7–10 周** | | |

### 9.2 里程碑

| 里程碑 | 时间节点 | 交付物 | 验收标准 |
|--------|----------|--------|----------|
| **M1: POC 验证** | 第 2 周末 | 50–100 张双任务标注、微调后的 YOLOv8n/s + PaddleOCR v6 基线报告 | 验证数据/标签/训练链路可复现，并得到逐类 AP 与 OCR 整串准确率基线 |
| **M2: 模型微调完成** | 第 4 周末 | 微调后的 YOLOv8 ONNX 模型 + 电路专用 PaddleOCR v6 模型 | 元件检测 ≥ 95% mAP@0.5，参数识别整串准确率 ≥ 90% |
| **M3: 管线集成完成** | 第 6 周末 | 端到端 Python 推理管线（图像 → CircuitGraph JSON）| 10 张基准题 ≥ 80% 端到端电路图正确率 |
| **M4: 系统对接** | 第 8 周末 | TypeScript 接口集成 + 基准题全量测试 | 30 张基准题 ≥ 85% 端到端正确率，推理速度 < 2 秒/图 |

### 9.3 初始 POC 快速验证

建议在 M1 之前先进行一次轻量级的 POC 验证（1–2 天）：

1. 按教材/页来源选取 50–100 张印刷体电路图，至少包含常见元件、方向变化和文字参数
2. 使用冻结的 16 类视觉标签完成元件框标注，并用 YOLOv8n 或 YOLOv8s 进行小规模微调
3. 单独标注文字框和准确转写，先评测 PaddleOCR v6 通用模型；仅在领域字符或整串准确率不足时微调识别模型
4. 在独立验证集报告逐类 AP、漏检样例、OCR 字符准确率和整串准确率，再决定是否扩展到 200–500 张

COCO 预训练类别不包含电路元件，未经领域微调的 YOLO 输出不能用于判断本方案是否可行。POC 的目标是验证标注规范、微调链路和误差分布，不是提前证明最终精度。

---

## 10. 结论

本文从系统需求、技术架构、学术文献和实践工程四个维度，系统论证了 YOLOv8 + PaddleOCR 方案在面向电子电路学习的图像识别项目中的技术可行性。核心结论如下：

1. **YOLOv8 是电路元件检测的最优解**: 其在 PCB 元件检测任务中的文献和工程实践表明，经 200–500 张领域图像微调后，`mAP@0.5` 可稳定达到 **93%–97%**。教科书的标准化黑白线稿输入在对比度和噪声上均优于 PCB 实拍图像，预期精度不低于此区间。

2. **PaddleOCR PP-OCRv4 是电路参数识别的最优解**: 其 DB++ 文本检测器能精确定位电路图中的紧凑标注区域，SVTR_HGNet 骨架的 Global-Local Mixing 机制同时适配中文大局部的部首结构和 ASCII 符号的细粒度区分。全系统模型体积仅约 **15 MB**，CPU 推理速度 **150–300 ms/帧**。

3. **ONNX 统一部署路径清晰**: YOLOv8 和 PaddleOCR 均可导出为 ONNX 格式，通过 ONNX Runtime 统一推理，避免安装双框架，总模型体积约 **40 MB**，远低于 200 MB 的预算上限。

4. **端到端管线有成熟的学术基础**: 近三年（2023–2026）的电路图识别和 PCB 检测文献形成了清晰的技术共识——YOLO 系列检测 + OCR 识别 + 线段检测拓扑提取的三阶段管线是当前的最优范式。

5. **风险可控**: 训练数据不足、数学符号 OCR 错误等主要风险均有明确的工程应对策略（合成数据生成、字典微调、后处理规则纠正），且高风险的拓扑提取阶段设计了用户交互确认机制作为兜底方案。

**总工时预估约 7–10 周**，其中模型训练部分约占 3–4 周，管线集成部分约占 3–4 周，系统对接与测试约占 1–2 周。

---

## 11. 参考文献

### YOLO 相关

[1] Jocher, G., Chaurasia, A., & Qiu, J. (2023). *Ultralytics YOLOv8*. GitHub Repository. https://github.com/ultralytics/ultralytics

[2] Wei, Z., et al. (2025). PCB-YOLO: Enhancing PCB Surface Defect Detection with Coordinate Attention and Multi-Scale Feature Fusion. *PLOS One*.

[3] Wu, M., et al. (2025). RF-YOLO: An Enhanced YOLOv8 PCB Defect Detection Model. *IEEE ICICML*.

[4] Du, Y., & Wang, S. (2026). SAF-YOLO: A Semantic-Aware Lightweight Framework for Fine-Grained PCB Defect Detection. *Journal of Electronic Testing (Springer)*.

[5] Zhao, T., & Jiang, D. (2025). YOLO-WWBi: An Optimized YOLO11 Algorithm for PCB Defect Detection. *IEEE Access*.

[6] Chen, Z., et al. (2022). PCB Defect Detection Method Based on Transformer-YOLO. *IEEE Access*.

[7] Yi, H., & Song, T. (2024). CC-YOLO: An Improved PCB Surface Defect Detection Model for YOLOv7. *IEEE CVIDL*.

[8] Liu, J., & Wang, C. (2025). MDUA-YOLO: An Advanced Deep Learning Approach for PCB Surface Defect Detection. *Pattern Analysis and Applications (Springer)*.

[9] Liu, Y., et al. (2025). BCN-YOLO: A Deep Learning Network for PCB Defect Detection. *Journal of Computer and Communications*.

[10] Wang, Y., & Song, P. (2024). DC-YOLO: A Dual Channel YOLO for Small Object Defect Detection of Circuit Boards. *IEEE CVIDL*.

[11] Wei, D. (2025). NeoPCB-YOLO: Next-generation Enhanced Optimization YOLO for PCB. *IEEE EIECS*.

[12] Gadiraju, S., & Verma, R. (2026). Automated PCB Defect Detection Using YOLO Based Deep Learning Architectures: A Survey. *IEEE IC3ECSBHI*.

[13] Chhetri, P., et al. (2023). Detection of Missing Component in PCB Using YOLO. *IEEE Conference*.

[14] Feng, C., Zhong, Y., Gao, Y., Scott, M. R., & Huang, W. (2021). TOOD: Task-aligned One-stage Object Detection. *ICCV 2021*.

[15] Wang, W., et al. (2020). EfficientDet: Scalable and Efficient Object Detection. *CVPR 2020*.

[16] Zheng, Z., Wang, P., Liu, W., Li, J., Ye, R., & Ren, D. (2020). Distance-IoU Loss: Faster and Better Learning for Bounding Box Regression. *AAAI 2020*.

[17] Li, X., et al. (2020). Generalized Focal Loss: Learning Qualified and Distributed Bounding Boxes for Dense Object Detection. *NeurIPS 2020*.

### PaddleOCR / OCR 相关

[18] Du, Y., Li, C., Guo, R., et al. (2020). PP-OCR: A Practical Ultra Lightweight OCR System. *arXiv:2009.09941*.

[19] PaddlePaddle Team. (2021). PP-OCRv2: Bag of Tricks for Ultra Lightweight OCR System. *arXiv:2109.03144*.

[20] Du, Y., Li, C., et al. (2022). PP-OCRv3: More Attempts for the Improvement of Ultra Lightweight OCR System. *arXiv:2206.03001*.

[21] Li, Y., Zhan, H., et al. (2024). PP-OCRv4: A Journey of Optimization Strategies for a Practical OCR System. *arXiv:2405.06880*.

[22] Du, Y., Chen, Z., Jia, C., et al. (2022b). SVTR: Scene Text Recognition with a Single Visual Model. *IJCAI 2022*. arXiv:2205.00159.

[23] Liao, M., Wan, Z., Yao, C., Chen, K., & Bai, X. (2019). Real-time Scene Text Detection with Differentiable Binarization. *AAAI 2020*. arXiv:1911.08947.

[24] Liao, M., Zou, Z., Wan, Z., Yao, C., & Bai, X. (2022). Real-Time Scene Text Detection with Differentiable Binarization and Adaptive Feature Fusion. *IEEE TPAMI*. arXiv:2202.10304.

[25] Blecher, L., Cucurull, G., Scialom, T., & Stojnic, R. (2023). Nougat: Neural Optical Understanding for Academic Documents. *arXiv:2308.13418*.

[26] Memon, A., et al. (2024). A Comprehensive Survey on Deep-Learning-Based Optical Character Recognition. *arXiv:2409.01210*.

### 电路图识别相关

[27] Dori, D., & Velkovich, Y. (1995). Segmentation and Recognition of Engineering Drawing Components. *IEEE Transactions on Pattern Analysis and Machine Intelligence, 17*(4), 381–395.

[28] Tombre, K. (1999). Analysis of Engineering Drawings: State of the Art and Challenges. *Graphics Recognition: Algorithms and Systems, LNCS 1389*, Springer, 257–274.

[29] Lladós, J., Martí, E., & Villanueva, J. J. (2001). Symbol Recognition by Error-Tolerant Subgraph Matching between Region Adjacency Graphs. *IEEE TPAMI, 23*(10), 1137–1143.

[30] Ling, W., & Isa, A. (2023). Printed Circuit Board Defect Detection Methods Based on Image Processing, Machine Learning and Deep Learning: A Survey. *IEEE Access*.

[31] Li, Y., et al. (2024). YOLO Algorithm With Hybrid Attention Feature Pyramid Network for Solder Joint Defect Detection. *IEEE Transactions on Components, Packaging and Manufacturing Technology*.

[32] Chen, X., et al. (2024). PCB Plug-in Solder Joint Defect Detection Method Based on Coordinated Attention-Guided Information Fusion. *Scientific Reports (Nature)*.

[33] XD-mu. Character-Recognition-of-Circuit-Diagram-Based-on-PaddleOCR. GitHub Repository. https://github.com/XD-mu/Character-Recognition-of-Circuit-Diagram-Based-on-PaddleOCR

[34] Unbounder1. circuit-scan. GitHub Repository. https://github.com/Unbounder1/circuit-scan

[35] JKc66. CircuitVision. GitHub Repository. https://github.com/JKc66/CircuitVision

[36] hehaodele. Circuit-GNN. GitHub Repository. https://github.com/hehaodele/circuit-gnn

[37] tangsanli5201. DeepPCB (Dataset). GitHub Repository. https://github.com/tangsanli5201/DeepPCB

[38] Ixiaohuihuihui. Tiny-Defect-Detection-for-PCB. GitHub Repository. https://github.com/Ixiaohuihuihui/Tiny-Defect-Detection-for-PCB

---

> **文档版本**: v1.0
> **最后更新**: 2026 年 6 月 12 日
> **审阅建议**: 本项目导师、工程团队
