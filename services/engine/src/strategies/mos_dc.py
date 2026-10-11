"""MOSFET DC bias operating-point analysis strategy.

For an n-channel enhancement MOSFET:
  1. Assume saturation: I_D = (1/2)·k_n·(V_GS - V_TH)²
  2. Solve gate-source loop → V_GS
  3. Compute I_D
  4. Solve drain-source loop → V_DS
  5. Verify saturation: V_DS > V_GS - V_TH
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
    OperatingPoint,
    Step,
)
from .base import AnalysisStrategy


def _node_for_port(graph: CircuitGraph, comp_id: str, port_id: str) -> str | None:
    for conn in graph.connections:
        if conn.component_id == comp_id and conn.port_id == port_id:
            return conn.node_id
    return None


def _find_ground(graph: CircuitGraph) -> str | None:
    for node in graph.nodes:
        if node.kind.value == "reference":
            return node.id
    for comp in graph.components:
        if comp.definition_id == "reference.ground":
            return _node_for_port(graph, comp.id, "ref")
    return None


def _connected_resistance(graph: CircuitGraph, na: str, nb: str) -> float | None:
    for comp in graph.components:
        if comp.definition_id != "passive.resistor":
            continue
        n_p = _node_for_port(graph, comp.id, "p")
        n_n = _node_for_port(graph, comp.id, "n")
        if (n_p == na and n_n == nb) or (n_p == nb and n_n == na):
            return _safe_get_param(comp, "resistance", 0.0)
    return None


class MOSDCBiasStrategy(AnalysisStrategy):
    """DC operating-point analysis for MOSFET circuits without AC sources."""

    def get_problem_type(self) -> str:
        return "mos_dc_bias"

    def get_teaching_tags(self) -> list[str]:
        return ["mosfet", "dc_operating_point", "bias_analysis"]

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
        return has_mos and not has_ac

    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        steps: list[Step] = []
        equations: list[Equation] = []

        mos = self._find_mos(graph)
        if mos is None:
            raise ValueError("No MOSFET/JFET found")

        ground = _find_ground(graph) or "gnd"
        d_node = _node_for_port(graph, mos.id, "drain") or "D"
        g_node = _node_for_port(graph, mos.id, "gate") or "G"
        s_node = _node_for_port(graph, mos.id, "source") or "S"

        is_nmos = "n_" in mos.model_variant or mos.model_variant == "n_enhancement"
        vth = _safe_get_param(mos, "threshold_voltage", 1.5)

        # ── Step 1: DC equivalent ───────────────────────────────────
        steps.append(Step(
            description="绘制直流等效电路",
            formula=r"\text{电容开路，电感短路，AC 源置零}",
            reasoning="直流偏置分析中，将所有电容开路、电感短路",
        ))

        # ── Step 2: Assume saturation ───────────────────────────────
        steps.append(Step(
            description="假设 MOSFET 工作在饱和区",
            formula=(
                r"I_D = \frac{1}{2} k_n (V_{GS} - V_{TH})^2"
                if is_nmos
                else r"I_D = \frac{1}{2} k_p (V_{SG} - |V_{TH}|)^2"
            ),
            reasoning="增强型 MOSFET 在饱和区满足平方律关系",
        ))
        equations.append(Equation(
            id="eq.mos_saturation",
            latex=r"I_D = \frac{1}{2} k_n (V_{GS} - V_{TH})^2",
            variables=["I_D", "k_n", "V_GS", "V_TH"],
        ))

        # ── Step 3: Gate-source voltage ─────────────────────────────
        # For a typical common-source with gate voltage divider or direct bias
        # V_G is set by resistors; V_S = I_D * R_S if source resistor exists
        # Common case: gate directly biased (e.g. V_G = V_DD * R_G2/(R_G1+R_G2))

        vdd_val = 10.0
        for comp in graph.components:
            if comp.definition_id == "source.v_dc":
                vdd_val = _safe_get_param(comp, "voltage", 10.0)
                break

        VDD = sp.Symbol("V_{DD}")
        VTH = sp.Symbol("V_{TH}")
        ID = sp.Symbol("I_D")

        # Check for source resistor
        rs_val = _connected_resistance(graph, s_node, ground) or 0.0

        # Determine gate voltage
        # Simplification: if gate is connected through resistor divider, compute V_G
        # Otherwise assume V_G is set externally
        vg_set = False
        for comp in graph.components:
            if comp.definition_id == "source.v_dc":
                n_p = _node_for_port(graph, comp.id, "p")
                if n_p == g_node:
                    # Gate directly connected to VDD
                    vg_set = True
                    break

        steps.append(Step(
            description="确定栅源电压 V_GS",
            formula=(
                r"V_{GS} = V_G - I_D R_S"
                if rs_val > 0
                else r"V_{GS} = V_G"
            ),
            reasoning=(
                "栅极电压由偏置网络决定；源极电压 = I_D · R_S"
                if rs_val > 0
                else "源极接地，V_GS = V_G"
            ),
        ))

        # ── Step 4: Drain current ───────────────────────────────────
        # I_D = (V_DD - V_DS) / R_D  (from drain KVL)
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

        steps.append(Step(
            description="列漏极回路 KVL 方程",
            formula=r"V_{DD} = I_D R_D + V_{DS} + I_D R_S" if rs_val > 0
            else r"V_{DD} = I_D R_D + V_{DS}",
            reasoning="沿漏极电源回路列写基尔霍夫电压定律",
        ))
        equations.append(Equation(
            id="eq.mos_drain_kvl",
            latex=r"V_{DD} = I_D R_D + V_{DS}" + (r" + I_D R_S" if rs_val > 0 else ""),
            variables=["V_DD", "I_D", "R_D", "V_DS"],
        ))

        # ── Step 5: Operating point ─────────────────────────────────
        steps.append(Step(
            description="联立求解直流工作点",
            formula=r"\text{需要已知 } k_n \text{ 和 } V_{TH} \text{ 才能求解数值工作点}",
            reasoning="MOSFET 的 I_D-V_GS 关系是非线性的，需要器件参数 k_n 和 V_TH 的具体数值",
        ))

        # ── Step 6: Verify saturation ───────────────────────────────
        steps.append(Step(
            description="验证饱和区假设",
            formula=(
                r"V_{DS} > V_{GS} - V_{TH}"
                if is_nmos
                else r"V_{SD} > V_{SG} - |V_{TH}|"
            ),
            reasoning="若上述不等式成立，饱和区假设正确；否则器件工作在线性区",
        ))

        # Build result
        op = OperatingPoint(
            node_voltages={
                "drain": rf"V_{{DD}} - I_D R_D = {vdd_val} - I_D \cdot {rd_val}",
                "gate": "V_G",
                "source": f"I_D \\cdot {rs_val}" if rs_val > 0 else "0",
            },
            branch_currents={
                "I_D": r"\frac{1}{2} k_n (V_{GS} - V_{TH})^2",
            },
            regions={mos.id: "saturation (assumed)"},
        )

        return AnalysisResult(
            problem_type=self.get_problem_type(),
            steps=steps,
            equations=equations,
            operating_point=op,
            teaching_tags=self.get_teaching_tags(),
            is_symbolic=True,
        )

    def _find_mos(self, graph: CircuitGraph) -> ComponentInstance | None:
        for comp in graph.components:
            d = DEFINITIONS_BY_ID.get(comp.definition_id)
            if d and d.family.value == "fet":
                return comp
        return None
