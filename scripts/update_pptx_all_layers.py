"""
替换 PPTX 中数据层、分析引擎层、展示层相关内容。
保持原有字体（微软雅黑）、字号、加粗状态不变。
"""

import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8')

from pptx import Presentation
from lxml import etree

PPTX_NS = "http://schemas.openxmlformats.org/drawingml/2006/main"


def make_run_element(text, font_name="微软雅黑", font_size_pt=None, bold=None):
    r_elem = etree.SubElement(etree.Element("dummy"), f"{{{PPTX_NS}}}r")
    rPr = etree.SubElement(r_elem, f"{{{PPTX_NS}}}rPr")
    if font_size_pt is not None:
        rPr.set("sz", str(int(font_size_pt * 100)))
    if bold is not None:
        rPr.set("b", "1" if bold else "0")
    latin = etree.SubElement(rPr, f"{{{PPTX_NS}}}latin")
    latin.set("typeface", font_name)
    ea = etree.SubElement(rPr, f"{{{PPTX_NS}}}ea")
    ea.set("typeface", font_name)
    t_elem = etree.SubElement(r_elem, f"{{{PPTX_NS}}}t")
    t_elem.text = text
    t_elem.set("{http://www.w3.org/XML/1998/namespace}space", "preserve")
    return r_elem


def clear_paragraph_and_add_runs(para, run_specs):
    p_elem = para._p
    for r in list(p_elem.findall(f"{{{PPTX_NS}}}r")):
        p_elem.remove(r)
    for br in list(p_elem.findall(f"{{{PPTX_NS}}}br")):
        p_elem.remove(br)
    for spec in run_specs:
        r_elem = make_run_element(
            text=spec.get("text", ""),
            font_name=spec.get("font", "微软雅黑"),
            font_size_pt=spec.get("size"),
            bold=spec.get("bold"),
        )
        p_elem.append(r_elem)


prs = Presentation('files/面向电子电路学习的图像识别与可解释分析辅助系统.pptx')
slide9 = prs.slides[8]

# ============================================================
# shape[6]: 展示层内容 (P0 + P1)
# ============================================================
shape6 = slide9.shapes[6]
tf6 = shape6.text_frame

# P0
clear_paragraph_and_add_runs(tf6.paragraphs[0], [
    {"text": "• ", "bold": None},
    {"text": "React Flow 电路图编辑器", "bold": True},
    {"text": "（自定义元件节点、拖拽连线、参数编辑）", "bold": None},
    {"text": " • ", "bold": None},
    {"text": "KaTeX 公式渲染", "bold": True},
    {"text": "（~10ms/公式）", "bold": None},
])

# P1
clear_paragraph_and_add_runs(tf6.paragraphs[1], [
    {"text": "• 等效电路图展示 • 分步骤解题交互（展开/折叠/跳转）", "bold": None},
    {"text": " • ", "bold": None},
    {"text": "报告导出", "bold": True},
    {"text": "（PDF/Word/LaTeX）", "bold": None},
    {"text": " • ", "bold": None},
    {"text": "QR 码 + SSE", "bold": True},
    {"text": " 手机拍照上传联动", "bold": None},
])

# ============================================================
# shape[10]: 分析引擎层内容 (P0 + P1)
# ============================================================
shape10 = slide9.shapes[10]
tf10 = shape10.text_frame

# P0
clear_paragraph_and_add_runs(tf10.paragraphs[0], [
    {"text": "• ", "bold": None},
    {"text": "课程规则层", "bold": True},
    {"text": "：5 类题型策略（二极管/BJT 偏置/BJT 小信号/MOS 偏置/MOS 小信号）", "bold": None},
    {"text": " • ", "bold": True},
    {"text": "符号工具", "bold": True},
    {"text": "层：", "bold": None},
    {"text": "Lcapy", "bold": None},
    {"text": "（MNA 符号求解）+", "bold": None},
    {"text": "SymPy", "bold": None},
    {"text": "（化简/变换）+", "bold": None},
    {"text": "PySpice", "bold": None},
    {"text": "（数值校验）", "bold": None},
])

# P1
clear_paragraph_and_add_runs(tf10.paragraphs[1], [
    {"text": "• 三重保护：超时控制→复杂度限制→降级为数值求解", "bold": None},
    {"text": " • ", "bold": None},
    {"text": "输出结构化 AnalysisResult", "bold": None},
    {"text": "（等效电路、方程、步骤、知识点）", "bold": None},
])

# ============================================================
# shape[18]: 数据层内容 (P0)
# ============================================================
shape18 = slide9.shapes[18]
tf18 = shape18.text_frame

clear_paragraph_and_add_runs(tf18.paragraphs[0], [
    {"text": "• ", "bold": None},
    {"text": "Zod Schema", "bold": True},
    {"text": " 作为事实来源 • ", "bold": None},
    {"text": "NEC 三元组", "bold": True},
    {"text": " 电路图表示 • ", "bold": None},
    {"text": "19 元件五维定义", "bold": True},
    {"text": " • ", "bold": None},
    {"text": "JSON Schema", "bold": None},
    {"text": " 跨语言导出 • ", "bold": None},
    {"text": "校验器", "bold": None},
    {"text": "（报告不阻断）", "bold": None},
])

# 保存
prs.save('files/面向电子电路学习的图像识别与可解释分析辅助系统.pptx')
print('PPTX 更新完成。')
