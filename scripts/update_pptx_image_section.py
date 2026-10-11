"""
替换 PPTX 中图像处理相关内容为 YOLOv8 + PaddleOCR 方案。
保持原有字体（微软雅黑）、字号、加粗状态、颜色不变。
"""

from pptx import Presentation
from pptx.util import Pt
from lxml import etree

PPTX_NS = "http://schemas.openxmlformats.org/drawingml/2006/main"


def make_run_element(text, font_name="微软雅黑", font_size_pt=None, bold=None):
    """创建一个 <a:r> 元素，保持与原文一致的格式。"""
    nsmap = {"a": PPTX_NS}
    r_elem = etree.SubElement(
        etree.Element("dummy"),
        f"{{{PPTX_NS}}}r",
    )

    # rPr
    rPr = etree.SubElement(r_elem, f"{{{PPTX_NS}}}rPr")
    if font_size_pt is not None:
        rPr.set("sz", str(int(font_size_pt * 100)))
    if bold is not None:
        rPr.set("b", "1" if bold else "0")

    # 字体
    latin = etree.SubElement(rPr, f"{{{PPTX_NS}}}latin")
    latin.set("typeface", font_name)
    ea = etree.SubElement(rPr, f"{{{PPTX_NS}}}ea")
    ea.set("typeface", font_name)

    # 文本
    t_elem = etree.SubElement(r_elem, f"{{{PPTX_NS}}}t")
    t_elem.text = text
    t_elem.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")

    return r_elem


def clear_paragraph_and_add_runs(para, run_specs):
    """清除段落的所有 run 并按 run_specs 列表添加新 run。

    run_specs: list of dict with keys:
      - text: str
      - font: str (default "微软雅黑")
      - size: float in pt (default None = inherit)
      - bold: bool or None (default None = inherit)
    """
    p_elem = para._p

    # 移除所有现有的 <a:r> 和 <a:br> 元素
    for r in list(p_elem.findall(f"{{{PPTX_NS}}}r")):
        p_elem.remove(r)
    for br in list(p_elem.findall(f"{{{PPTX_NS}}}br")):
        p_elem.remove(br)

    # 添加新 run
    for spec in run_specs:
        r_elem = make_run_element(
            text=spec.get("text", ""),
            font_name=spec.get("font", "微软雅黑"),
            font_size_pt=spec.get("size"),
            bold=spec.get("bold"),
        )
        p_elem.append(r_elem)


def main():
    prs = Presentation("files/面向电子电路学习的图像识别与可解释分析辅助系统.pptx")

    # ============================================================
    # 修改1: Slide 9 - 图像处理层内容 (shape[14], P0 + P1)
    # ============================================================
    slide9 = prs.slides[8]
    shape14 = slide9.shapes[14]
    tf = shape14.text_frame

    # 替换 P0 (原来: "• 手机照片预处理：... • 元件检测 + OCR 参数提取")
    p0 = tf.paragraphs[0]
    clear_paragraph_and_add_runs(p0, [
        {"text": "• ", "size": None, "bold": None},
        {"text": "OpenCV 预处理", "size": None, "bold": True},
        {"text": "：灰度化、二值化、去噪、透视/倾斜校正 ", "size": None, "bold": None},
        {"text": "• ", "size": None, "bold": None},
        {"text": "YOLOv8 元件检测", "size": None, "bold": True},
        {"text": "（~15–19 类，CPU ~100–150ms/帧）", "size": None, "bold": None},
        {"text": " + ", "size": None, "bold": None},
        {"text": "PaddleOCR 参数识别", "size": None, "bold": True},
        {"text": "（中英文+数学符号，~150–300ms/帧）", "size": None, "bold": None},
    ])

    # 替换 P1 (原来: "• 导线/节点/环路识别...")
    p1 = tf.paragraphs[1]
    clear_paragraph_and_add_runs(p1, [
        {"text": "• ", "size": None, "bold": None},
        {"text": "导线/节点识别", "size": None, "bold": True},
        {"text": "（FLD 线段检测 + 节点聚类）", "size": None, "bold": None},
        {"text": "，生成候选拓扑并提示可疑点", "size": None, "bold": None},
        {"text": " • ONNX Runtime 统一部署，总模型 ~40MB", "size": None, "bold": None},
    ])

    # ============================================================
    # 修改2: Slide 13 - 计划主线 (shape[7], P0)
    # ============================================================
    slide13 = prs.slides[12]
    shape7 = slide13.shapes[7]
    tf7 = shape7.text_frame
    p0_7 = tf7.paragraphs[0]
    clear_paragraph_and_add_runs(p0_7, [
        {"text": "数据标准先行 → YOLOv8 + PaddleOCR 生成候选拓扑 → 分析引擎完成课程化推导 → 展示层打通手机拍照与电脑解题联动",
         "size": 24, "bold": None},
    ])

    # 保存
    output_path = "files/面向电子电路学习的图像识别与可解释分析辅助系统.pptx"
    prs.save(output_path)
    print(f"已保存: {output_path}")


if __name__ == "__main__":
    main()
