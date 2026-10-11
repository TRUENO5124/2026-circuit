"""Linear circuit analysis strategy.

Handles passive linear circuits (resistors, capacitors, inductors,
independent DC/AC sources, controlled sources) using textbook Analog Electronics
and Circuit Analysis frequency response methods (参考《模拟电子技术基础》第五章):
- 正弦稳态激励与相量模型 (引入复数虚数单位 j)
- 复阻抗表示：Z_R = R, Z_C(jω) = 1/(jωC) = -j/(ωC), Z_L(jω) = jωL
- 复阻抗分压定理与回路方程
- 将电压传输函数 A_u(jω) 整理为作图的标准形式
- 求解中频电压增益 A_um、截止频率与截止角频率 (ω_H, f_H 或 ω_L, f_L)
- 判定增益函数属性 (高频增益函数、低频增益函数或高低频增益函数)
- 正弦稳态幅频特性与相频特性表达式 (波特图渐近线特性分析)
- 代入具体元件国际工程参数进行数值解与信号滤波评估

底层计算机 MNA (改进节点分析法) 矩阵与 LU 分解记录自动归档至项目日志:
services/engine/logs/mna_solver.log
"""

from __future__ import annotations

import datetime
import math
from pathlib import Path
from typing import Any
import sympy as sp

from ..component_defs import DEFINITIONS_BY_ID
from ..mna import MNABuilder, _safe_get_param
from ..types import (
    AnalysisResult,
    CircuitGraph,
    ComponentInstance,
    Equation,
    GainExpressions,
    ImpedanceExpressions,
    OperatingPoint,
    Step,
)
from .base import AnalysisStrategy

LOGS_DIR = Path(__file__).resolve().parent.parent.parent / "logs"


def _log_mna_execution(
    graph_id: str,
    all_nodes: list[str],
    num_branches: int,
    A_mat: sp.Matrix,
    z_vec: sp.Matrix,
    solved: dict[str, sp.Expr],
) -> None:
    """Log raw MNA matrix stamps and execution trace to a project log file."""
    try:
        LOGS_DIR.mkdir(parents=True, exist_ok=True)
        log_file = LOGS_DIR / "mna_solver.log"
        now_str = datetime.datetime.now().strftime("%Y-%m-%d %H:%M:%S")

        with open(log_file, "a", encoding="utf-8") as f:
            f.write("=" * 80 + "\n")
            f.write(f"[{now_str}] MNA Solver Execution Trace for Circuit '{graph_id}'\n")
            f.write(f"Non-ground Nodes ({len(all_nodes)}): {', '.join(all_nodes)}\n")
            f.write(f"Voltage Branch Variables: {num_branches}\n")
            f.write("MNA System Matrix A:\n")
            f.write(str(A_mat) + "\n\n")
            f.write("RHS Vector z:\n")
            f.write(str(z_vec) + "\n\n")
            f.write("Solved Nodal Variables via LU Decomposition:\n")
            for k, v in solved.items():
                f.write(f"  {k} = {v}\n")
            f.write("=" * 80 + "\n\n")
    except Exception:
        pass


def _format_sci_latex(val: float, precision: int = 3) -> str:
    """Format number into clean LaTeX scientific notation or decimal, never '1e+03'."""
    if val == 0:
        return "0"
    abs_v = abs(val)
    if 0.1 <= abs_v < 1000 and abs(val - round(val)) < 1e-6:
        return str(int(round(val)))
    if 0.01 <= abs_v < 1000:
        return f"{val:.{precision}g}"
    exp = int(math.floor(math.log10(abs_v)))
    mantissa = val / (10 ** exp)
    mantissa_str = f"{mantissa:.{precision}g}"
    if mantissa_str == "1":
        return f"10^{{{exp}}}"
    elif mantissa_str == "-1":
        return f"-10^{{{exp}}}"
    return f"{mantissa_str} \\times 10^{{{exp}}}"


def _format_sci_text(val: float, precision: int = 3) -> str:
    """Format number into clean text scientific notation or decimal, never '1e+03'."""
    if val == 0:
        return "0"
    abs_v = abs(val)
    if 0.1 <= abs_v < 1000 and abs(val - round(val)) < 1e-6:
        return str(int(round(val)))
    if 0.01 <= abs_v < 1000:
        return f"{val:.{precision}g}"
    exp = int(math.floor(math.log10(abs_v)))
    mantissa = val / (10 ** exp)
    mantissa_str = f"{mantissa:.{precision}g}"
    if mantissa_str == "1":
        return f"10^{exp}"
    elif mantissa_str == "-1":
        return f"-10^{exp}"
    return f"{mantissa_str}×10^{exp}"


def _node_for_port(graph: CircuitGraph, comp_id: str, port_id: str) -> str | None:
    for conn in graph.connections:
        if conn.component_id == comp_id and conn.port_id == port_id:
            return conn.node_id
    return None


def _find_ground(graph: CircuitGraph) -> str | None:
    for node in graph.nodes:
        kind_str = getattr(node.kind, "value", str(node.kind))
        if kind_str == "reference":
            return node.id
    for comp in graph.components:
        if comp.definition_id == "reference.ground":
            return _node_for_port(graph, comp.id, "ref")
    return None


def _find_component_between_nodes(
    graph: CircuitGraph, n1: str | None, n2: str | None
) -> ComponentInstance | None:
    """Find component connected between node n1 and n2."""
    if not n1 or not n2:
        return None
    for c in graph.components:
        c_nodes = {
            conn.node_id for conn in graph.connections if conn.component_id == c.id
        }
        if n1 in c_nodes and n2 in c_nodes:
            return c
    return None


class LinearAnalysisStrategy(AnalysisStrategy):
    """Textbook-grade circuit analysis strategy for linear AC and DC networks."""

    def get_problem_type(self) -> str:
        return "linear_frequency_analysis"

    def get_teaching_tags(self) -> list[str]:
        return [
            "circuit_analysis",
            "phasor_analysis",
            "complex_impedance",
            "frequency_response",
            "transfer_function",
            "bode_plot",
            "rc_filter",
        ]

    def can_handle(self, graph: CircuitGraph) -> bool:
        has_nonlinear = any(
            DEFINITIONS_BY_ID.get(c.definition_id, None) is not None
            and getattr(
                DEFINITIONS_BY_ID[c.definition_id].family,
                "value",
                str(DEFINITIONS_BY_ID[c.definition_id].family),
            ) in ("diode", "bjt", "fet")
            for c in graph.components
        )
        if has_nonlinear:
            return False

        has_source = any(
            c.definition_id.startswith("source.") or c.definition_id.startswith("controlled.")
            for c in graph.components
        )
        return has_source

    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        steps: list[Step] = []
        equations: list[Equation] = []

        ground_node = _find_ground(graph)
        all_nodes = [n.id for n in graph.nodes if n.id != ground_node]

        # Detect AC / reactive components
        has_ac_source = any(
            c.definition_id in ("source.v_ac_small_signal", "source.i_ac_small_signal")
            for c in graph.components
        )
        has_capacitors = any(c.definition_id == "passive.capacitor" for c in graph.components)
        has_inductors = any(c.definition_id == "passive.inductor" for c in graph.components)
        is_reactive_or_ac = has_ac_source or has_capacitors or has_inductors

        # ── Step 1: 电路拓扑与正弦交流激励相量分析 ─────────────────────────
        ground_name = ground_node if ground_node else "参考地 (GND)"
        ac_source = next((c for c in graph.components if c.definition_id == "source.v_ac_small_signal"), None)
        dc_source = next((c for c in graph.components if c.definition_id == "source.v_dc"), None)

        if ac_source:
            vm_val = _safe_get_param(ac_source, "amplitude", 0.01)
            freq_val = _safe_get_param(ac_source, "frequency", 1000.0)
            omega_in_val = 2 * math.pi * freq_val
            steps.append(
                Step(
                    description="电路拓扑结构与输入正弦交流激励分析",
                    formula=(
                        f"\\begin{{aligned}} "
                        f"v_{{in}}(t) &= {_format_sci_latex(vm_val)}\\sin(2\\pi \\times {_format_sci_latex(freq_val)} t)\\text{{ V}} "
                        f"\\iff \\dot{{U}}_{{in}} = {_format_sci_latex(vm_val / math.sqrt(2))} \\angle 0^\\circ\\text{{ V}} \\\\ "
                        f"\\omega_{{in}} &= 2\\pi f = {_format_sci_latex(omega_in_val)}\\text{{ rad/s}},\\quad "
                        r"V_{\text{GND}} = 0\text{ V} "
                        f"\\end{{aligned}}"
                    ),
                    reasoning=(
                        f"选定基准参考接地节点 {ground_name}，设定基准电位为 0V。"
                        f"输入端由正弦交流电压源 {ac_source.label} 提供激励，幅值 V_m = {_format_sci_text(vm_val)}V，频率 f = {_format_sci_text(freq_val)}Hz，"
                        f"角频率 ω = 2πf ≈ {_format_sci_text(omega_in_val)} rad/s。"
                        f"电路在正弦交流稳态激励下工作，适用复频域相量分析法与频率响应特性分析。"
                    ),
                )
            )
        elif dc_source:
            vdc_val = _safe_get_param(dc_source, "voltage", 5.0)
            steps.append(
                Step(
                    description="直流线性网络拓扑与输入电压确定",
                    formula=f"V_{{S}} = {vdc_val}\\text{{ V}},\\quad V_{{\\text{{GND}}}} = 0\\text{{ V}}",
                    reasoning=f"选定基准参考接地端 {ground_name}（0V），由直流恒压源 {dc_source.label} 驱动线性阻性网络。",
                )
            )
        else:
            steps.append(
                Step(
                    description="电路拓扑结构与参考节点分析",
                    formula=r"V_{\text{GND}} = 0\text{ V}",
                    reasoning=f"识别基准参考节点为 {ground_name}，设定参考电位为 0V。",
                )
            )

        # ── Step 2: 相量模型转换与复阻抗表示 (引入复数虚数单位 j) ─────────
        s = sp.Symbol("s") if is_reactive_or_ac else None
        element_formulas: list[str] = []
        subs_dict: dict[sp.Symbol, float] = {}

        node_to_idx = {nid: idx for idx, nid in enumerate(all_nodes)}

        def get_node_idx(nid: str | None) -> int:
            if nid is None or nid == ground_node:
                return -1
            return node_to_idx.get(nid, -1)

        v_sources = [
            c for c in graph.components
            if c.definition_id in ("source.v_dc", "source.v_ac_small_signal")
        ]
        num_branches = len(v_sources)
        v_source_branch = {c.id: b_idx for b_idx, c in enumerate(v_sources)}

        mna = MNABuilder(num_nodes=len(all_nodes), num_branches=num_branches)

        # Resistors
        resistor_comps = [c for c in graph.components if c.definition_id == "passive.resistor"]
        for c in resistor_comps:
            r_sym = sp.Symbol(f"R_{{{c.label}}}")
            r_val = _safe_get_param(c, "resistance", 1000.0)
            subs_dict[r_sym] = r_val
            ni = get_node_idx(_node_for_port(graph, c.id, "p"))
            nj = get_node_idx(_node_for_port(graph, c.id, "n"))
            mna.add_resistor(c.label, ni, nj, r_sym)
            element_formulas.append(f"Z_{{{c.label}}} = {r_sym}")

        # Capacitors
        capacitor_comps = [c for c in graph.components if c.definition_id == "passive.capacitor"]
        for c in capacitor_comps:
            c_sym = sp.Symbol(f"C_{{{c.label}}}")
            c_val = _safe_get_param(c, "capacitance", 1e-6)
            subs_dict[c_sym] = c_val
            ni = get_node_idx(_node_for_port(graph, c.id, "p"))
            nj = get_node_idx(_node_for_port(graph, c.id, "n"))
            if is_reactive_or_ac:
                mna.add_conductance(c.label, ni, nj, s * c_sym)
                element_formulas.append(f"Z_{{{c.label}}}(j\\omega) = \\frac{{1}}{{j\\omega {c_sym}}} = -j\\frac{{1}}{{\\omega {c_sym}}}")
            else:
                element_formulas.append(f"Z_{{{c.label}}} \\to \\infty\\text{{ (直流开路)}}")

        # Inductors
        inductor_comps = [c for c in graph.components if c.definition_id == "passive.inductor"]
        for c in inductor_comps:
            l_sym = sp.Symbol(f"L_{{{c.label}}}")
            l_val = _safe_get_param(c, "inductance", 1e-3)
            subs_dict[l_sym] = l_val
            ni = get_node_idx(_node_for_port(graph, c.id, "p"))
            nj = get_node_idx(_node_for_port(graph, c.id, "n"))
            if is_reactive_or_ac:
                mna.add_conductance(c.label, ni, nj, 1 / (s * l_sym))
                element_formulas.append(f"Z_{{{c.label}}}(j\\omega) = j\\omega {l_sym}")
            else:
                mna.add_resistor(c.label, ni, nj, sp.Float(1e-6))
                element_formulas.append(f"Z_{{{c.label}}} \\to 0\\text{{ (直流短路)}}")

        # Voltage sources
        vin_sym = None
        for c in v_sources:
            ni = get_node_idx(_node_for_port(graph, c.id, "p"))
            nj = get_node_idx(_node_for_port(graph, c.id, "n"))
            b_idx = v_source_branch[c.id]
            v_sym = sp.Symbol(f"V_{{{c.label}}}")
            if vin_sym is None:
                vin_sym = v_sym
            v_val = (
                _safe_get_param(c, "amplitude", 1.0)
                if c.definition_id == "source.v_ac_small_signal"
                else _safe_get_param(c, "voltage", 5.0)
            )
            subs_dict[v_sym] = v_val
            mna.add_voltage_source(c.label, ni, nj, v_sym, branch_idx=b_idx)

        # Current sources
        for c in graph.components:
            if c.definition_id in ("source.i_dc", "source.i_ac_small_signal"):
                ni = get_node_idx(_node_for_port(graph, c.id, "p"))
                nj = get_node_idx(_node_for_port(graph, c.id, "n"))
                i_sym = sp.Symbol(f"I_{{{c.label}}}")
                i_val = _safe_get_param(c, "amplitude", 0.001)
                subs_dict[i_sym] = i_val
                mna.add_current_source(c.label, ni, nj, i_sym)

        steps.append(
            Step(
                description="相量模型转换与复阻抗表示（引入复数虚数单位 j）",
                formula=",\\quad ".join(element_formulas[:5]) if element_formulas else r"Z_R = R",
                reasoning=(
                    "在正弦交流频域分析中，引入复数虚数单位 j（表示相角超前 90°，即在复平面逆时针旋转 90°）："
                    "① 电阻呈现纯实数阻抗 Z_R = R；"
                    "② 电容呈现纯虚数容抗 Z_C(jω) = 1/(jωC) = -j/(ωC)，容抗模值与角频率成反比，低频时趋于无穷大（直流开路），极高频时趋于零（交流短路接地）；"
                    "③ 电感呈现纯虚数感抗 Z_L(jω) = jωL，感抗模值与角频率成正比。"
                ),
            )
        )

        # ── Solve MNA silently and log to disk ───────────────────────
        A_mat, z_vec = mna.build()
        solved = mna.solve()
        _log_mna_execution(graph.id, all_nodes, num_branches, A_mat, z_vec, solved)

        node_exprs = {nid: solved.get(f"V_{idx}", sp.Integer(0)) for idx, nid in enumerate(all_nodes)}

        # Identify input node and output node
        src_comp = v_sources[0] if v_sources else None
        vin_node = _node_for_port(graph, src_comp.id, "p") if src_comp else (all_nodes[0] if all_nodes else None)

        vout_node = None
        h_expr = None
        if vin_sym is not None and len(all_nodes) >= 1:
            for nid in reversed(all_nodes):
                if nid != vin_node:
                    vout_node = nid
                    v_cand = node_exprs[nid]
                    h_expr = sp.simplify(v_cand / vin_sym)
                    break

        if vout_node is None and len(all_nodes) >= 1:
            vout_node = all_nodes[-1]
            if vin_sym is not None:
                h_expr = sp.simplify(node_exprs[vout_node] / vin_sym)

        series_comp = _find_component_between_nodes(graph, vin_node, vout_node)
        shunt_comp = _find_component_between_nodes(graph, vout_node, ground_node)

        # Determine filter classification
        is_rc_lp = False
        is_rc_hp = False
        is_rl_lp = False
        is_rl_hp = False

        r_comp = None
        c_comp = None
        l_comp = None

        if series_comp and shunt_comp:
            if series_comp.definition_id == "passive.resistor" and shunt_comp.definition_id == "passive.capacitor":
                is_rc_lp = True
                r_comp, c_comp = series_comp, shunt_comp
            elif series_comp.definition_id == "passive.capacitor" and shunt_comp.definition_id == "passive.resistor":
                is_rc_hp = True
                c_comp, r_comp = series_comp, shunt_comp
            elif series_comp.definition_id == "passive.resistor" and shunt_comp.definition_id == "passive.inductor":
                is_rl_hp = True
                r_comp, l_comp = series_comp, shunt_comp
            elif series_comp.definition_id == "passive.inductor" and shunt_comp.definition_id == "passive.resistor":
                is_rl_lp = True
                l_comp, r_comp = series_comp, shunt_comp

        if not (is_rc_lp or is_rc_hp or is_rl_lp or is_rl_hp):
            if is_reactive_or_ac and resistor_comps and capacitor_comps:
                is_rc_lp = True
                r_comp = resistor_comps[0]
                c_comp = capacitor_comps[0]

        h_disp: str | None = None

        # ── Case A: RC Low-Pass Filter (一阶 RC 低通滤波电路) ─────────────
        if is_rc_lp and r_comp and c_comp:
            r_sym_str = f"R_{{{r_comp.label}}}"
            c_sym_str = f"C_{{{c_comp.label}}}"
            r_sym = sp.Symbol(r_sym_str)
            c_sym = sp.Symbol(c_sym_str)
            r_val = subs_dict.get(r_sym, 1000.0)
            c_val = subs_dict.get(c_sym, 1e-6)

            tau_val = r_val * c_val
            omega_h_val = 1.0 / tau_val if tau_val > 0 else 0.0
            fh_val = omega_h_val / (2 * math.pi)

            # Step 3: 复阻抗分压定理
            steps.append(
                Step(
                    description="应用复阻抗分压定理求解输出电压相量响应",
                    formula=(
                        f"\\dot{{U}}_{{out}}(j\\omega) = \\frac{{Z_{{{c_comp.label}}}(j\\omega)}}{{Z_{{{r_comp.label}}} + Z_{{{c_comp.label}}}(j\\omega)}} \\cdot \\dot{{U}}_{{in}}(j\\omega) = "
                        f"\\frac{{\\frac{{1}}{{j\\omega {c_sym_str}}}}}{{{r_sym_str} + \\frac{{1}}{{j\\omega {c_sym_str}}}}} \\cdot \\dot{{U}}_{{in}}(j\\omega) = "
                        f"\\frac{{1}}{{1 + j\\omega {r_sym_str} {c_sym_str}}} \\cdot \\dot{{U}}_{{in}}(j\\omega)"
                    ),
                    reasoning=(
                        f"电路为由串联电阻 {r_comp.label} 与对地并联电容 {c_comp.label} 组成的经典复阻抗分压网络。"
                        f"输出电压取自电容两端相对于参考地的相量电位差。根据电路分析分压定律，低频信号主要降落在高阻抗的电容两端，高频信号被电容容抗短路入地。"
                    ),
                )
            )

            # Step 4: 将 Au(jω) 整理为作图的标准形式 (严格对照 PDF 第 1~2 页)
            steps.append(
                Step(
                    description="将电压传输函数 Au(jω) 整理为作图的标准形式",
                    formula=(
                        f"A_u(j\\omega) = \\frac{{\\dot{{U}}_{{out}}(j\\omega)}}{{\\dot{{U}}_{{in}}(j\\omega)}} = "
                        f"\\frac{{1}}{{1 + j\\omega {r_sym_str} {c_sym_str}}} = "
                        f"\\frac{{A_{{um}}}}{{1 + j\\frac{{\\omega}}{{\\omega_H}}}} = "
                        f"\\frac{{A_{{um}}}}{{1 + j\\frac{{f}}{{f_H}}}},\\quad "
                        f"\\tau = {r_sym_str} {c_sym_str},\\quad "
                        f"\\omega_H = \\frac{{1}}{{\\tau}} = \\frac{{1}}{{{r_sym_str} {c_sym_str}}}"
                    ),
                    reasoning=(
                        "严格参考模电教材第五章标准作图形式，将传输函数因式分解为包含复数 j 与特征截止频率的单极点标准式。"
                        "其中 A_um 为中频电压放大倍数，τ = RC 为时间常数，ω_H = 1/τ 为上限截止角频率。"
                    ),
                )
            )
            equations.append(
                Equation(
                    id="eq.bode_canonical",
                    latex=f"A_u(j\\omega) = \\frac{{A_{{um}}}}{{1 + j\\frac{{\\omega}}{{\\omega_H}}}} = \\frac{{1}}{{1 + j\\omega {r_sym_str} {c_sym_str}}}",
                    variables=["A_u", "A_{um}", "j", "\\omega", "\\omega_H", r_sym_str, c_sym_str],
                )
            )

            # Step 5: 增益函数核心指标与类型判定 (严格对照 PDF 课后习题 3 问答模式)
            steps.append(
                Step(
                    description="频率响应核心指标求解与增益函数类型判定",
                    formula=(
                        r"A_{um} = 1\quad (20\lg |A_{um}| = 0\text{ dB}),\quad "
                        f"\\omega_H = \\frac{{1}}{{{r_sym_str} {c_sym_str}}},\\quad "
                        f"f_H = \\frac{{\\omega_H}}{{2\\pi}} = \\frac{{1}}{{2\\pi {r_sym_str} {c_sym_str}}},\\quad "
                        r"f_L = 0"
                        r"\implies A_u(j\omega)\text{ 属于高频增益函数（低通滤波特性）}"
                    ),
                    reasoning=(
                        "参考题解核心三问标准判定："
                        "① 中频电压增益：A_um = 1 (0 dB)；"
                        "② 截止频率：下限频率 f_L = 0，上限角频率 ω_H = 1/(RC)，上限频率 f_H = 1/(2πRC)；"
                        "③ 增益函数类型判定：由于下限频率为 0，高频受极点衰减，严格属于「高频增益函数」（一阶低通滤波网络）。"
                    ),
                )
            )
            equations.append(
                Equation(
                    id="eq.cutoff_freq",
                    latex=f"\\omega_H = \\frac{{1}}{{{r_sym_str} {c_sym_str}}},\\quad f_H = \\frac{{\\omega_H}}{{2\\pi}} = \\frac{{1}}{{2\\pi {r_sym_str} {c_sym_str}}}",
                    variables=["\\omega_H", "f_H", r_sym_str, c_sym_str],
                )
            )

            # Step 6: 正弦稳态幅频与相频特性分析 (渐近线波特图特性)
            steps.append(
                Step(
                    description="正弦稳态幅频与相频特性分析 (渐近线波特图特性)",
                    formula=(
                        f"|A_u(j\\omega)| = \\frac{{|A_{{um}}|}}{{\\sqrt{{1 + (\\omega/\\omega_H)^2}}}} = \\frac{{1}}{{\\sqrt{{1 + (f/f_H)^2}}}},\\quad "
                        f"20\\lg |A_u(j\\omega)| = -10\\lg\\left[1 + \\left(\\frac{{\\omega}}{{\\omega_H}}\\right)^2\\right]\\text{{ dB}},\\quad "
                        f"\\varphi(\\omega) = -\\arctan\\left(\\frac{{\\omega}}{{\\omega_H}}\\right) = -\\arctan\\left(\\frac{{f}}{{f_H}}\\right)"
                    ),
                    reasoning=(
                        "分析渐近线波特图三大特征区间："
                        "① 低频通带（f << f_H）：|A_u| ≈ 1 (0 dB)，输出与输入同相（φ ≈ 0°）；"
                        "② 半功率截止转折点（f = f_H）：幅值下降 3 dB（|A_u| = 1/√2 ≈ 0.707），相位滞后 φ = -45°；"
                        "③ 高频阻带衰减区（f >> f_H）：幅频渐近线以 -20 dB/dec (-6 dB/oct) 的固定斜率单调滚降，相角趋向于 -90°。"
                    ),
                )
            )

            # Step 7: 代入设定参数的数值计算与滤波评估
            vin_val = subs_dict.get(vin_sym, 0.01) if vin_sym else 0.01
            fin_val = _safe_get_param(ac_source, "frequency", 1000.0) if ac_source else 1000.0
            omega_in = 2 * math.pi * fin_val

            ratio = omega_in / omega_h_val if omega_h_val > 0 else 1.0
            mag_h = 1.0 / math.sqrt(1.0 + ratio ** 2)
            db_h = 20.0 * math.log10(mag_h) if mag_h > 0 else -99.0
            phase_deg = -math.degrees(math.atan(ratio))
            vout_mag = vin_val * mag_h

            r_disp = f"{_format_sci_text(r_val / 1e3)} k\\Omega" if r_val >= 1e3 else f"{_format_sci_text(r_val)} \\Omega"
            c_disp = f"{_format_sci_text(c_val * 1e6)} \\mu\\text{{F}}" if c_val < 1e-3 else f"{_format_sci_text(c_val * 1e3)} \\text{{mF}}"

            steps.append(
                Step(
                    description="代入设定参数的数值计算与滤波效果评估",
                    formula=(
                        f"\\begin{{aligned}} "
                        f"\\tau &= {_format_sci_latex(tau_val * 1e3)}\\text{{ ms}},\\quad "
                        f"\\omega_H = {_format_sci_latex(omega_h_val)}\\text{{ rad/s}},\\quad "
                        f"f_H = {_format_sci_latex(fh_val)}\\text{{ Hz}} \\\\ "
                        f"|A_u(jf_{{in}})| &= {mag_h:.4f}\\text{{ (}}{db_h:.1f}\\text{{ dB)}},\\quad "
                        f"\\varphi(f_{{in}}) = {phase_deg:.1f}^\\circ "
                        f"\\end{{aligned}}"
                    ),
                    reasoning=(
                        f"代入参数：电阻 R = {r_disp}，电容 C = {c_disp}，输入激励频率 f_in = {_format_sci_text(fin_val)}Hz，幅值 V_in = {_format_sci_text(vin_val * 1e3)}mV。"
                        f"计算得出截止频率 f_H = {_format_sci_text(fh_val)}Hz。当前信号频率与截止频率之比 f_in/f_H ≈ {_format_sci_text(fin_val / fh_val if fh_val > 0 else 1.0)}。"
                        f"{'当前信号处于通带区，信号几乎无损耗通过。' if fin_val < fh_val else '当前信号处于阻带衰减区，高频被大幅滤除并产生相应滞后相移。'}"
                        f"输出电压幅值约为 {_format_sci_text(vout_mag * 1e3)}mV，相位滞后 {abs(phase_deg):.1f}°。"
                    ),
                )
            )

            h_disp = f"A_u(j\\omega) = \\frac{{1}}{{1 + j\\omega {r_sym_str} {c_sym_str}}} = \\frac{{1}}{{1 + j\\frac{{\\omega}}{{\\omega_H}}}}"

        # ── Case B: RC High-Pass Filter (一阶 RC 高通滤波电路) ────────────
        elif is_rc_hp and r_comp and c_comp:
            r_sym_str = f"R_{{{r_comp.label}}}"
            c_sym_str = f"C_{{{c_comp.label}}}"
            r_sym = sp.Symbol(r_sym_str)
            c_sym = sp.Symbol(c_sym_str)
            r_val = subs_dict.get(r_sym, 1000.0)
            c_val = subs_dict.get(c_sym, 1e-6)

            tau_val = r_val * c_val
            omega_l_val = 1.0 / tau_val if tau_val > 0 else 0.0
            fl_val = omega_l_val / (2 * math.pi)

            steps.append(
                Step(
                    description="应用复阻抗分压定理求解输出电压相量响应",
                    formula=(
                        f"\\dot{{U}}_{{out}}(j\\omega) = \\frac{{Z_{{{r_comp.label}}}}}{{Z_{{{c_comp.label}}}(j\\omega) + Z_{{{r_comp.label}}}}} \\cdot \\dot{{U}}_{{in}}(j\\omega) = "
                        f"\\frac{{{r_sym_str}}}{{\\frac{{1}}{{j\\omega {c_sym_str}}} + {r_sym_str}}} \\cdot \\dot{{U}}_{{in}}(j\\omega) = "
                        f"\\frac{{j\\omega {r_sym_str} {c_sym_str}}}{{1 + j\\omega {r_sym_str} {c_sym_str}}} \\cdot \\dot{{U}}_{{in}}(j\\omega)"
                    ),
                    reasoning=(
                        f"电路为由串联电容 {c_comp.label} 与对地并联电阻 {r_comp.label} 组成的经典高通/隔直分压网络。"
                        f"输出电压取自电阻两端相对于参考地的相量电位差。低频时电容容抗极大阻断直流，高频时电容相当于短路，信号直接传输至输出端。"
                    ),
                )
            )

            steps.append(
                Step(
                    description="将电压传输函数 Au(jω) 整理为作图的标准形式",
                    formula=(
                        f"A_u(j\\omega) = \\frac{{\\dot{{U}}_{{out}}(j\\omega)}}{{\\dot{{U}}_{{in}}(j\\omega)}} = "
                        f"\\frac{{j\\omega {r_sym_str} {c_sym_str}}}{{1 + j\\omega {r_sym_str} {c_sym_str}}} = "
                        f"\\frac{{A_{{um}}\\left(j\\frac{{\\omega}}{{\\omega_L}}\\right)}}{{1 + j\\frac{{\\omega}}{{\\omega_L}}}} = "
                        f"\\frac{{A_{{um}}\\left(j\\frac{{f}}{{f_L}}\\right)}}{{1 + j\\frac{{f}}{{f_L}}}},\\quad "
                        f"\\tau = {r_sym_str} {c_sym_str},\\quad "
                        f"\\omega_L = \\frac{{1}}{{\\tau}} = \\frac{{1}}{{{r_sym_str} {c_sym_str}}}"
                    ),
                    reasoning=(
                        "严格参考模电教材第五章标准作图形式，将传输函数整理为标准零极点形式。"
                        "其中 A_um = 1 为中频增益，τ = RC 为时间常数，ω_L = 1/τ 为下限截止角频率。"
                    ),
                )
            )
            equations.append(
                Equation(
                    id="eq.bode_canonical",
                    latex=f"A_u(j\\omega) = \\frac{{A_{{um}}\\left(j\\frac{{\\omega}}{{\\omega_L}}\\right)}}{{1 + j\\frac{{\\omega}}{{\\omega_L}}}} = \\frac{{j\\omega {r_sym_str} {c_sym_str}}}{{1 + j\\omega {r_sym_str} {c_sym_str}}}",
                    variables=["A_u", "A_{um}", "j", "\\omega", "\\omega_L", r_sym_str, c_sym_str],
                )
            )

            steps.append(
                Step(
                    description="频率响应核心指标求解与增益函数类型判定",
                    formula=(
                        r"A_{um} = 1\quad (20\lg |A_{um}| = 0\text{ dB}),\quad "
                        f"\\omega_L = \\frac{{1}}{{{r_sym_str} {c_sym_str}}},\\quad "
                        f"f_L = \\frac{{\\omega_L}}{{2\\pi}} = \\frac{{1}}{{2\\pi {r_sym_str} {c_sym_str}}},\\quad "
                        r"f_H \to \infty"
                        r"\implies A_u(j\omega)\text{ 属于低频增益函数（高通隔直特性）}"
                    ),
                    reasoning=(
                        "参考题解核心三问标准判定："
                        "① 中频电压增益：A_um = 1 (0 dB)；"
                        "② 截止频率：下限角频率 ω_L = 1/(RC)，下限频率 f_L = 1/(2πRC)，上限频率 f_H → ∞；"
                        "③ 增益函数类型判定：由于直流与低频被零点隔断，中高频直通，严格属于「低频增益函数」（一阶高通滤波网络）。"
                    ),
                )
            )
            equations.append(
                Equation(
                    id="eq.cutoff_freq",
                    latex=f"\\omega_L = \\frac{{1}}{{{r_sym_str} {c_sym_str}}},\\quad f_L = \\frac{{\\omega_L}}{{2\\pi}} = \\frac{{1}}{{2\\pi {r_sym_str} {c_sym_str}}}",
                    variables=["\\omega_L", "f_L", r_sym_str, c_sym_str],
                )
            )

            steps.append(
                Step(
                    description="正弦稳态幅频与相频特性分析 (渐近线波特图特性)",
                    formula=(
                        f"|A_u(j\\omega)| = \\frac{{\\omega/\\omega_L}}{{\\sqrt{{1 + (\\omega/\\omega_L)^2}}}} = \\frac{{f/f_L}}{{\\sqrt{{1 + (f/f_L)^2}}}},\\quad "
                        f"\\varphi(\\omega) = 90^\\circ - \\arctan\\left(\\frac{{\\omega}}{{\\omega_L}}\\right) = 90^\\circ - \\arctan\\left(\\frac{{f}}{{f_L}}\\right)"
                    ),
                    reasoning=(
                        "渐近线波特图特性："
                        "① 低频阻带（f << f_L）：幅频以 +20 dB/dec 速率单调上升，相角超前趋于 +90°；"
                        "② 下限截止转折点（f = f_L）：幅值下降 3 dB（|A_u| = 1/√2 ≈ 0.707），相角超前 φ = +45°；"
                        "③ 高频通带（f >> f_L）：幅值保持恒定 |A_u| ≈ 1 (0 dB)，相角 φ ≈ 0°。"
                    ),
                )
            )

            vin_val = subs_dict.get(vin_sym, 0.01) if vin_sym else 0.01
            fin_val = _safe_get_param(ac_source, "frequency", 1000.0) if ac_source else 1000.0
            omega_in = 2 * math.pi * fin_val
            ratio = omega_in / omega_l_val if omega_l_val > 0 else 1.0
            mag_h = ratio / math.sqrt(1.0 + ratio ** 2)
            db_h = 20.0 * math.log10(mag_h) if mag_h > 0 else -99.0
            phase_deg = 90.0 - math.degrees(math.atan(ratio))
            vout_mag = vin_val * mag_h

            r_disp = f"{_format_sci_text(r_val / 1e3)} k\\Omega" if r_val >= 1e3 else f"{_format_sci_text(r_val)} \\Omega"
            c_disp = f"{_format_sci_text(c_val * 1e6)} \\mu\\text{{F}}" if c_val < 1e-3 else f"{_format_sci_text(c_val * 1e3)} \\text{{mF}}"

            steps.append(
                Step(
                    description="代入设定参数的数值计算与滤波效果评估",
                    formula=(
                        f"\\begin{{aligned}} "
                        f"\\tau &= {_format_sci_latex(tau_val * 1e3)}\\text{{ ms}},\\quad "
                        f"\\omega_L = {_format_sci_latex(omega_l_val)}\\text{{ rad/s}},\\quad "
                        f"f_L = {_format_sci_latex(fl_val)}\\text{{ Hz}} \\\\ "
                        f"|A_u(jf_{{in}})| &= {mag_h:.4f}\\text{{ (}}{db_h:.1f}\\text{{ dB)}},\\quad "
                        f"\\varphi(f_{{in}}) = {phase_deg:.1f}^\\circ "
                        f"\\end{{aligned}}"
                    ),
                    reasoning=(
                        f"代入参数：电阻 R = {r_disp}，电容 C = {c_disp}，输入激励频率 f_in = {_format_sci_text(fin_val)}Hz，幅值 V_in = {_format_sci_text(vin_val * 1e3)}mV。"
                        f"下限截止频率 f_L = {_format_sci_text(fl_val)}Hz。输出电压幅值约为 {_format_sci_text(vout_mag * 1e3)}mV，相位超前 {phase_deg:.1f}°。"
                    ),
                )
            )

            h_disp = f"A_u(j\\omega) = \\frac{{j\\omega {r_sym_str} {c_sym_str}}}{{1 + j\\omega {r_sym_str} {c_sym_str}}} = \\frac{{j\\frac{{\\omega}}{{\\omega_L}}}}{{1 + j\\frac{{\\omega}}{{\\omega_L}}}}"

        # ── Case C: General Linear DC or Other Linear Networks ─────────────
        else:
            steps.append(
                Step(
                    description="应用电路分析分压定律与回路方程",
                    formula=r" \\ ".join(f"V_{{{nid}}} = {sp.latex(node_exprs[nid])}" for nid in all_nodes[:3]),
                    reasoning="应用电路分析中的经典分压定律与基尔霍夫定律，求得各独立节点电位表达式。",
                )
            )
            steps.append(
                Step(
                    description="计算各支路电压与电流响应",
                    formula=r" \\ ".join(f"V_{{{nid}}} = {sp.latex(node_exprs[nid].subs(subs_dict))}\\text{{ V}}" for nid in all_nodes[:3]),
                    reasoning="将各支路电阻与电源数值代入节点方程，求解稳态工作点与电压分布。",
                )
            )
            h_disp = sp.latex(h_expr) if h_expr is not None else None

        # Construct OperatingPoint
        num_node_v: dict[str, str] = {}
        for nid, v_expr in node_exprs.items():
            num_v = v_expr.subs(subs_dict)
            if s is not None:
                num_v = num_v.subs({s: 0})
            try:
                flt_val = float(num_v)
                num_node_v[nid] = f"{flt_val:.4g} V"
            except (TypeError, ValueError):
                num_node_v[nid] = sp.latex(num_v)

        branch_latex: dict[str, str] = {}
        for c in v_sources:
            b_idx = v_source_branch[c.id]
            j_expr = solved.get(f"J_{b_idx}", sp.Integer(0))
            num_j = j_expr.subs(subs_dict)
            if s is not None:
                num_j = num_j.subs({s: 0})
            try:
                flt_j = float(num_j)
                branch_latex[f"I({c.label})"] = f"{flt_j:.4g} A"
            except (TypeError, ValueError):
                branch_latex[f"I({c.label})"] = sp.latex(num_j)

        op_point = OperatingPoint(
            node_voltages=num_node_v,
            branch_currents=branch_latex,
            regions={},
        )

        gain_exprs = None
        if h_disp is not None:
            gain_exprs = GainExpressions(
                voltage_gain=h_disp,
            )

        return AnalysisResult(
            problem_type="linear_frequency_analysis" if is_reactive_or_ac else "linear_dc_analysis",
            steps=steps,
            equations=equations,
            operating_point=op_point,
            gain_expressions=gain_exprs,
            teaching_tags=self.get_teaching_tags(),
            is_symbolic=True,
        )
