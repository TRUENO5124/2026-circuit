"""BJT AC small-signal analysis strategy.

Replaces the BJT with its hybrid-π model and computes:
  - Voltage gain Av
  - Input resistance Rin
  - Output resistance Rout
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


class BJTACSmallSignalStrategy(AnalysisStrategy):
    """Small-signal analysis for BJT amplifier circuits."""

    def get_problem_type(self) -> str:
        return "bjt_ac_small_signal"

    def get_teaching_tags(self) -> list[str]:
        return ["bjt", "small_signal", "hybrid_pi", "amplifier"]

    def can_handle(self, graph: CircuitGraph) -> bool:
        has_bjt = any(
            DEFINITIONS_BY_ID.get(c.definition_id, None) is not None
            and DEFINITIONS_BY_ID[c.definition_id].family.value == "bjt"
            for c in graph.components
        )
        has_ac = any(
            c.definition_id in ("source.v_ac_small_signal", "source.i_ac_small_signal")
            for c in graph.components
        )
        return has_bjt and has_ac

    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        steps: list[Step] = []
        equations: list[Equation] = []

        bjt = self._find_bjt(graph)
        ac_src = self._find_ac_source(graph)

        if bjt is None:
            raise ValueError("No BJT found")

        b_node = _node_for_port(graph, bjt.id, "base") or "B"
        c_node = _node_for_port(graph, bjt.id, "collector") or "C"
        e_node = _node_for_port(graph, bjt.id, "emitter") or "E"

        beta = _safe_get_param(bjt, "beta", 100.0)
        gm = sp.Symbol("g_m")
        r_pi = sp.Symbol("r_{\\pi}")

        # ── Step 1: DC sources off, caps → short ───────────────────
        steps.append(Step(
            description="绘制交流小信号等效电路",
            formula=r"\text{DC 电压源短路，DC 电流源开路，电容短路}",
            reasoning="小信号分析中，直流源置零（电压源→短路，电流源→开路），"
                      "耦合电容和旁路电容在信号频率下视为短路",
        ))

        # ── Step 2: Replace BJT with hybrid-π ──────────────────────
        steps.append(Step(
            description="用混合 π 模型替代 BJT",
            formula=(
                r"r_{\pi} = \frac{\beta}{g_m}, \quad "
                r"g_m = \frac{I_C}{V_T}, \quad "
                r"V_T \approx 26\,\text{mV}"
            ),
            reasoning="BJT 小信号混合 π 模型：基极-发射极为 r_π 电阻，"
                      "集电极-发射极为 g_m·v_be 受控电流源",
        ))
        equations.append(Equation(
            id="eq.hybrid_pi",
            latex=r"r_{\pi} = \frac{\beta}{g_m}, \quad g_m = \frac{I_C}{V_T}",
            variables=["r_\\pi", "\\beta", "g_m", "I_C", "V_T"],
        ))

        # ── Step 3: Identify topology and compute gain ──────────────
        # Common-emitter: input at base, output at collector, emitter at ground
        e_is_ground = any(
            conn.node_id for conn in graph.connections
            if conn.component_id == bjt.id and conn.port_id == "emitter"
        )
        # Check if emitter node is ground
        e_ground = False
        for node in graph.nodes:
            if node.id == e_node and node.kind.value == "reference":
                e_ground = True
                break
        if not e_ground:
            for comp in graph.components:
                if comp.definition_id == "reference.ground":
                    gnd_node = _node_for_port(graph, comp.id, "ref")
                    if gnd_node == e_node:
                        e_ground = True
                        break

        # Find collector resistor (between collector and VCC/ground)
        rc_val = 0.0
        for comp in graph.components:
            if comp.definition_id != "passive.resistor":
                continue
            n_p = _node_for_port(graph, comp.id, "p")
            n_n = _node_for_port(graph, comp.id, "n")
            if n_p == c_node or n_n == c_node:
                rc_val = _safe_get_param(comp, "resistance", 5000.0)
                break

        RC = sp.Symbol("R_C")

        if e_ground:
            # Common-emitter with bypassed emitter
            steps.append(Step(
                description="判断放大器组态：共射极（发射极交流接地）",
                formula=r"\text{共射放大电路}",
                reasoning="发射极通过旁路电容或直接接地，输入信号加在基极，输出取自集电极",
            ))

            # Av = -gm * RC
            Av = -gm * RC
            steps.append(Step(
                description="计算电压增益",
                formula=r"A_v = \frac{v_{out}}{v_{in}} = -g_m R_C",
                reasoning="共射极放大器的电压增益 Av = -gm·RC，负号表示输出与输入反相",
            ))
            equations.append(Equation(
                id="eq.ce_gain",
                latex=r"A_v = -g_m R_C",
                variables=["A_v", "g_m", "R_C"],
            ))

            # Input resistance: Rin = r_pi
            Rin = r_pi
            steps.append(Step(
                description="计算输入电阻",
                formula=r"R_{in} = r_{\pi} = \frac{\beta}{g_m}",
                reasoning="共射极放大器的输入电阻等于基极-发射极间的 r_π",
            ))
            equations.append(Equation(
                id="eq.ce_rin",
                latex=r"R_{in} = r_{\pi}",
                variables=["R_{in}", "r_{\\pi}"],
            ))

            # Output resistance: Rout = RC
            Rout = RC
            steps.append(Step(
                description="计算输出电阻",
                formula=r"R_{out} = R_C",
                reasoning="共射极放大器的输出电阻近似等于集电极电阻 R_C",
            ))

        else:
            # Common-emitter with emitter degeneration
            re_val = 0.0
            for comp in graph.components:
                if comp.definition_id != "passive.resistor":
                    continue
                n_p = _node_for_port(graph, comp.id, "p")
                n_n = _node_for_port(graph, comp.id, "n")
                if (n_p == e_node or n_n == e_node) and not (n_p == c_node or n_n == c_node):
                    re_val = _safe_get_param(comp, "resistance", 0.0)
                    break

            RE = sp.Symbol("R_E")

            steps.append(Step(
                description="判断放大器组态：共射极（含发射极负反馈电阻）",
                formula=r"\text{共射放大电路（射极退化）}",
                reasoning="发射极接有未旁路的电阻 R_E，引入串联-串联负反馈",
            ))

            # Av = -gm * RC / (1 + gm * RE)
            Av = -gm * RC / (1 + gm * RE)
            steps.append(Step(
                description="计算电压增益（含射极退化）",
                formula=r"A_v = -\frac{g_m R_C}{1 + g_m R_E}",
                reasoning="射极电阻 R_E 引入负反馈，电压增益降低为 -gm·RC/(1+gm·RE)；"
                          "当 gm·RE >> 1 时，Av ≈ -RC/RE",
            ))
            equations.append(Equation(
                id="eq.ce_degen_gain",
                latex=r"A_v = -\frac{g_m R_C}{1 + g_m R_E}",
                variables=["A_v", "g_m", "R_C", "R_E"],
            ))

            # Rin = r_pi + (beta+1) * RE  (ignoring base bias resistors)
            Rin = r_pi + (sp.Symbol("beta") + 1) * RE
            steps.append(Step(
                description="计算输入电阻",
                formula=r"R_{in} = r_{\pi} + (\beta + 1)R_E",
                reasoning="射极电阻 R_E 通过 β+1 倍反射到基极回路，增大了输入电阻",
            ))

            Rout = RC

        # ── Summary ──────────────────────────────────────────────────
        gain_expr = GainExpressions(
            voltage_gain=sp.latex(Av),
        )
        imp_expr = ImpedanceExpressions(
            input_resistance=sp.latex(Rin),
            output_resistance=sp.latex(Rout),
        )

        # Convert symbolic to numeric where possible
        if rc_val > 0:
            numeric_gain = sp.latex(Av.subs({RC: rc_val}))
            steps.append(Step(
                description="代入数值",
                formula=rf"A_v = {numeric_gain}",
                reasoning=f"将 R_C = {rc_val} Ω 代入增益表达式",
            ))

        return AnalysisResult(
            problem_type=self.get_problem_type(),
            steps=steps,
            equations=equations,
            gain_expressions=gain_expr,
            impedance_expressions=imp_expr,
            teaching_tags=self.get_teaching_tags(),
            is_symbolic=True,
        )

    def _find_bjt(self, graph: CircuitGraph) -> ComponentInstance | None:
        for comp in graph.components:
            d = DEFINITIONS_BY_ID.get(comp.definition_id)
            if d and d.family.value == "bjt":
                return comp
        return None

    def _find_ac_source(self, graph: CircuitGraph) -> ComponentInstance | None:
        for comp in graph.components:
            if comp.definition_id in ("source.v_ac_small_signal", "source.i_ac_small_signal"):
                return comp
        return None
