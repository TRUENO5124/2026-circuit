"""
替换开题报告中图像处理相关内容为 YOLOv8 + PaddleOCR 方案。
保持原有字体（楷体）、字号（12pt）、行间距（固定值20pt）不变。
"""

import copy
from docx import Document
from docx.shared import Pt, Emu
from docx.oxml.ns import qn


def make_run(paragraph, text, font_name="楷体", font_size_pt=12, bold=False):
    """向段落中添加一个 run，保持与原文一致的格式。"""
    run = paragraph.add_run(text)
    run.font.name = font_name
    run.font.size = Pt(font_size_pt)
    run.font.bold = bold
    # 设置中文字体（东亚字体）
    rpr = run._element.get_or_add_rPr()
    rfonts = rpr.find(qn("w:rFonts"))
    if rfonts is None:
        rfonts = run._element.makeelement(qn("w:rFonts"), {})
        rpr.insert(0, rfonts)
    rfonts.set(qn("w:eastAsia"), font_name)
    return run


def clear_paragraph_runs(paragraph):
    """清除段落中的所有 run。"""
    for run in paragraph.runs:
        run._element.getparent().remove(run._element)


def set_paragraph_format(paragraph, line_spacing_emu=254000):
    """设置段落格式：行间距固定值。"""
    pf = paragraph.paragraph_format
    pf.line_spacing = Emu(line_spacing_emu)
    from docx.enum.text import WD_LINE_SPACING
    pf.line_spacing_rule = WD_LINE_SPACING.EXACTLY


def add_paragraph_after(paragraph, text, font_name="楷体", font_size_pt=12,
                        bold=False, line_spacing_emu=254000):
    """在指定段落后插入一个新段落，保持相同格式。"""
    new_p = paragraph._element.makeelement(qn("w:p"), {})
    paragraph._element.addnext(new_p)

    # 创建段落对象
    from docx.text.paragraph import Paragraph
    new_para = Paragraph(new_p, paragraph._element.getparent())

    # 设置段落格式
    set_paragraph_format(new_para, line_spacing_emu)

    # 设置段落级别的字体属性（从原文段落复制）
    ppr_src = paragraph._element.find(qn("w:pPr"))
    ppr_new = new_p.find(qn("w:pPr"))
    if ppr_new is None:
        ppr_new = new_p.makeelement(qn("w:pPr"), {})
        new_p.insert(0, ppr_new)

    # 复制原文段落的属性（缩进、对齐等）
    if ppr_src is not None:
        for ppr_prop in ppr_src:
            tag = ppr_prop.tag
            if tag != qn("w:rPr"):  # 不复制 run 属性
                existing = ppr_new.find(tag)
                if existing is not None:
                    ppr_new.remove(existing)
                ppr_new.append(copy.deepcopy(ppr_prop))

    # 添加文本 run
    make_run(new_para, text, font_name, font_size_pt, bold)

    return new_para


def replace_para_text(paragraph, new_text, font_name="楷体", font_size_pt=12, bold=False):
    """替换段落的文本内容，保留段落格式。"""
    clear_paragraph_runs(paragraph)
    make_run(paragraph, new_text, font_name, font_size_pt, bold)


def main():
    doc = Document("files/面向电子电路学习的图像识别和可解释分析辅助系统-开题报告.docx")

    # ============================================================
    # 修改1: TABLE 3 研究方案 - 图像处理方法（段落4）
    # ============================================================
    table3 = doc.tables[3]
    cell = table3.rows[0].cells[0]

    # 段落4: 图像处理方法
    p4 = cell.paragraphs[4]
    clear_paragraph_runs(p4)
    set_paragraph_format(p4)

    make_run(p4, "（1）图像处理方法：")
    make_run(p4, "采用 YOLOv8 + PaddleOCR（PP-OCRv4）+ OpenCV 的三层架构实现电路图的自动识别与结构化提取。")
    make_run(p4, "具体流程如下：")
    make_run(p4, "① OpenCV 预处理层：")
    make_run(p4, "对输入图像进行灰度化、自适应二值化、形态学去噪和透视/倾斜校正，输出标准化的灰度二值图像。")
    make_run(p4, "② YOLOv8 目标检测层：")
    make_run(p4, "采用 YOLOv8s（Small）模型，在 COCO 预训练权重基础上，使用 200–500 张电路图标注数据进行微调训练。"
             "模型基于 CSPDarknet 骨架（C2f 模块）、PAN-FPN 多尺度特征融合颈部和 Decoupled Anchor-Free 检测头，"
             "可输出每个电路元件的边界框位置、类别（电阻/电容/BJT/MOSFET 等约 15–19 类）及置信度分数。"
             "该模型在 PCB 元件检测任务中已验证可达到 93%–97% mAP@0.5 的精度，预期在教科书标准化线稿上表现更优。"
             "推理模型约 11 MB（ONNX 格式），CPU 推理速度约 100–150 ms/帧。")
    make_run(p4, "③ PaddleOCR 参数识别层：")
    make_run(p4, "采用百度飞桨的 PP-OCRv4 系统（DB++ 文本检测器 + SVTR_HGNet 识别器），"
             "对 YOLOv8 检测到的元件边界框区域进行文本识别，提取参数标注（如 \"R1=10kΩ\"、\"β=100\"、\"Vcc=12V\"）。"
             "PP-OCRv4 对中英文混排文本的识别准确率达 85%–92%，全系统模型体积仅约 15 MB，CPU 推理速度 150–300 ms/帧。"
             "针对电路参数中的数学符号（Ω/β/μ等），可通过构建电路领域专用字典进行微调，进一步提升识别准确率。")
    make_run(p4, "④ 拓扑提取层：")
    make_run(p4, "结合 OpenCV 的快速线段检测器（FLD）和节点聚类算法，从预处理图像中提取导线段和连接节点，"
             "与元件检测结果共同构建结构化的 CircuitGraph JSON 数据，对齐项目数据层的 Zod Schema 定义。"
             "对于低置信度元件、疑似断线、未连接端口等问题，")
    make_run(p4, "系统将显式")
    make_run(p4, "提示给用户确认。")

    # ============================================================
    # 修改2: TABLE 3 第二阶段进度安排（段落9）
    # ============================================================
    p9 = cell.paragraphs[9]
    replace_para_text(
        p9,
        "第二阶段（2026年9-12月）：完成图像处理层开发，包括 OpenCV 预处理管线、YOLOv8 元件检测模型微调训练与 ONNX 导出、"
        "PaddleOCR 电路参数字典定制与识别模型微调、导线/节点检测与候选拓扑生成。"
    )

    # ============================================================
    # 修改3: TABLE 4 预期成果 - 图像处理层（段落3）
    # ============================================================
    table4 = doc.tables[4]
    cell4 = table4.rows[0].cells[0]

    p3 = cell4.paragraphs[2]  # 段落索引2是"（2）图像处理层"
    clear_paragraph_runs(p3)
    set_paragraph_format(p3, 254000)

    make_run(p3, "（2）图像处理层：基于 YOLOv8 + PaddleOCR（PP-OCRv4）+ OpenCV 的三层架构，"
             "包含图像预处理模块（灰度化、二值化、去噪、透视校正）、"
             "YOLOv8 元件目标检测模块（约 15–19 类电路元件，CPU 推理 ~100–150 ms/帧）、"
             "PaddleOCR 参数识别模块（中英文混排 + 数学符号，CPU 推理 ~150–300 ms/帧）、"
             "导线/节点/环路识别模块（基于 FLD 线段检测 + 节点聚类），"
             "生成带置信度的候选拓扑并提示可疑点。"
             "全部模型可通过 ONNX Runtime 统一推理部署，总模型体积约 40 MB。")

    # 保存
    output_path = "files/面向电子电路学习的图像识别和可解释分析辅助系统-开题报告.docx"
    doc.save(output_path)
    print(f"文档已保存至: {output_path}")
    print("替换完成。")


if __name__ == "__main__":
    main()
