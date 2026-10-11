"""MOSFET AC small-signal analysis strategy.

Replaces the MOSFET with its small-signal model (VCCS + output resistance)
and computes voltage gain, input/output resistance.
"""

from __future__ import annotations

import sympy as sp

from ..component_defs import DEFINITIONS_BY_ID
from ..mna import _safe_get_param
from ..types import (
    AnalysisResult,
    CircuitGraph,
    ComponentInstance,
    Equation,
    GainExpressions,
    ImpedanceExpressions,
    Step,
)
from .base import AnalysisStrategy


def _node_for_port(graph: CircuitGraph, comp_id: str, port_id: str) -> str | None:
    for conn in graph.connections:
        if conn.component_id == comp_id and conn.port_id == port_id:
            return conn.node_id
    return None


class MOSACSmallSignalStrategy(AnalysisStrategy):
    """Small-signal analysis for MOSFET amplifier circuits."""

    def get_problem_type(self) -> str:
        return "mos_ac_small_signal"

    def get_teaching_tags(self) -> list[str]:
        return ["mosfet", "small_signal", "amplifier", "common_source"]

    def can_handle(self, graph: CircuitGraph) -> bool:
        has_mos = any(
            DEFINITIONS_BY_ID.get(c.definition_id, None) is not None
            and DEFINITIONS_BY_ID[c.definition_id].family.value == "fet"
            for c in graph.components
        )
        has_ac = any(
            c.definition_id in ("source.v_ac_small_signal", "source.i_ac_small_signal")
            for c in graph.components
        )
        return has_mos and has_ac

    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        steps: list[Step] = []
        equations: list[Equation] = []

        mos = self._find_mos(graph)
        if mos is None:
            raise ValueError("No MOSFET/JFET found")

        d_node = _node_for_port(graph, mos.id, "drain") or "D"
        g_node = _node_for_port(graph, mos.id, "gate") or "G"
        s_node = _node_for_port(graph, mos.id, "source") or "S"

        gm = _safe_get_param(mos, "transconductance", 0.002)
        gm_sym = sp.Symbol("g_m")

        # Find drain resistor
        rd_val = 0.0
        for comp in graph.components:
            if comp.definition_id != "passive.resistor":
                continue
            n_p = _node_for_port(graph, comp.id, "p")
            n_n = _node_for_port(graph, comp.id, "n")
            if (n_p == d_node or n_n == d_node) and not (
                n_p == s_node or n_n == s_node or n_p == g_node or n_n == g_node
            ):
                rd_val = _safe_get_param(comp, "resistance", 5000.0)
                break

        RD = sp.Symbol("R_D")

        # ── Step 1: Small-signal equivalent ─────────────────────────
        steps.append(Step(
            description="绘制交流小信号等效电路",
            formula=r"\text{DC 电压源短路，DC 电流源开路，电容短路}",
            reasoning="小信号分析中，直流源置零，耦合电容和旁路电容视为短路",
        ))

        # ── Step 2: MOSFET small-signal model ───────────────────────
        steps.append(Step(
            description="用 MOSFET 小信号模型替代晶体管",
            formula=(
                r"\text{MOSFET 小信号模型：栅极开路（} I_G = 0 \text{），}"
                r"\text{漏源间为 } g_m v_{gs} \text{ 受控电流源}"
            ),
            reasoning="MOSFET 栅极输入电流为零（低频），输出为压控电流源 g_m·v_gs",
        ))
        equations.append(Equation(
            id="eq.mos_ss_model",
            latex=r"I_D = g_m V_{GS}, \quad I_G = 0",
            variables=["I_D", "g_m", "V_GS", "I_G"],
        ))

        # ── Step 3: Determine topology ──────────────────────────────
        # Check if source is at ground
        s_ground = False
        for node in graph.nodes:
            if node.id == s_node and node.kind.value == "reference":
                s_ground = True
                break
        if not s_ground:
            for comp in graph.components:
                if comp.definition_id == "reference.ground":
                    gnd_n = _node_for_port(graph, comp.id, "ref")
                    if gnd_n == s_node:
                        s_ground = True
                        break

        if s_ground:
            # ── Common-source with source grounded ─────────────────
            steps.append(Step(
                description="判断放大器组态：共源极（源极交流接地）",
                formula=r"\text{共源放大电路}",
                reasoning="源极交流接地，输入信号加在栅极，输出取自漏极",
            ))

            Av = -gm_sym * RD
            steps.append(Step(
                description="计算电压增益",
                formula=r"A_v = \frac{v_{out}}{v_{in}} = -g_m R_D",
                reasoning="共源极放大器的电压增益 Av = -gm·RD，负号表示输出与输入反相",
            ))
            equations.append(Equation(
                id="eq.cs_gain",
                latex=r"A_v = -g_m R_D",
                variables=["A_v", "g_m", "R_D"],
            ))

            Rin = sp.Symbol(r"\infty")
            steps.append(Step(
                description="计算输入电阻",
                formula=r"R_{in} \to \infty",
                reasoning="MOSFET 栅极输入电流为零（低频），输入电阻理论无穷大；"
                          "实际由栅极偏置电阻决定",
            ))

            Rout = RD
            steps.append(Step(
                description="计算输出电阻",
                formula=r"R_{out} = R_D",
                reasoning="共源极放大器的输出电阻近似等于漏极电阻 R_D",
            ))

        else:
            # ── Common-source with source degeneration ──────────────
            # Find source resistor
            rs_val = 0.0
            for comp in graph.components:
                if comp.definition_id != "passive.resistor":
                    continue
                n_p = _node_for_port(graph, comp.id, "p")
                n_n = _node_for_port(graph, comp.id, "n")
                if (n_p == s_node or n_n == s_node) and not (n_p == d_node or n_n == d_node):
                    rs_val = _safe_get_param(comp, "resistance", 0.0)
                    break

            RS = sp.Symbol("R_S")

            steps.append(Step(
                description="判断放大器组态：共源极（含源极负反馈电阻）",
                formula=r"\text{共源放大电路（源极退化）}",
                reasoning="源极接有未旁路的电阻 R_S，引入串联-串联负反馈",
            ))

            Av = -gm_sym * RD / (1 + gm_sym * RS)
            steps.append(Step(
                description="计算电压增益（含源极退化）",
                formula=r"A_v = -\frac{g_m R_D}{1 + g_m R_S}",
                reasoning="源极电阻 R_S 引入负反馈，电压增益降低；"
                          "当 gm·RS >> 1 时，Av ≈ -RD/RS",
            ))
            equations.append(Equation(
                id="eq.cs_degen_gain",
                latex=r"A_v = -\frac{g_m R_D}{1 + g_m R_S}",
                variables=["A_v", "g_m", "R_D", "R_S"],
            ))

            Rin = sp.Symbol(r"\infty")
            Rout = RD

        # ── Step 4: Numerical evaluation ────────────────────────────
        if rd_val > 0:
            Av_numeric = Av.subs({gm_sym: gm, RD: rd_val})
            steps.append(Step(
                description="代入数值计算",
                formula=rf"A_v = {sp.latex(Av_numeric)} = {float(Av_numeric.evalf()):.4f}",
                reasoning=f"代入 g_m = {gm} S, R_D = {rd_val} Ω",
            ))

        gain_expr = GainExpressions(voltage_gain=sp.latex(Av))
        imp_expr = ImpedanceExpressions(
            input_resistance=sp.latex(Rin),
            output_resistance=sp.latex(Rout),
        )

        return AnalysisResult(
            problem_type=self.get_problem_type(),
            steps=steps,
            equations=equations,
            gain_expressions=gain_expr,
            impedance_expressions=imp_expr,
            teaching_tags=self.get_teaching_tags(),
            is_symbolic=True,
        )

    def _find_mos(self, graph: CircuitGraph) -> ComponentInstance | None:
        for comp in graph.components:
            d = DEFINITIONS_BY_ID.get(comp.definition_id)
            if d and d.family.value == "fet":
                return comp
        return None
