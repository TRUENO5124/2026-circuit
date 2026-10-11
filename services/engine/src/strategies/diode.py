"""Diode circuit analysis strategy.

Handles diode limiter, clamp, and rectifier circuits using piecewise-linear
analysis: assume diode state (on/off), solve the linear circuit, verify the
assumption against diode constraints.

For the constant-voltage-drop model (default):
  - Diode ON:  V_anode - V_cathode >= V_f (≈ 0.7 V for Si)
  - Diode OFF: I_D = 0, V_anode - V_cathode < V_f

Output includes the piecewise analysis, the breakpoint voltage, and the
transfer characteristic expression.
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


class DiodeAnalysisStrategy(AnalysisStrategy):
    """Piecewise-linear analysis for diode circuits."""

    def get_problem_type(self) -> str:
        return "diode_analysis"

    def get_teaching_tags(self) -> list[str]:
        return ["diode", "piecewise_linear", "limiter", "rectifier"]

    def can_handle(self, graph: CircuitGraph) -> bool:
        has_diode = any(
            DEFINITIONS_BY_ID.get(c.definition_id, None) is not None
            and DEFINITIONS_BY_ID[c.definition_id].family.value == "diode"
            for c in graph.components
        )
        # Diode circuits can coexist with AC sources — handle both
        return has_diode

    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        steps: list[Step] = []
        equations: list[Equation] = []

        diodes = self._find_diodes(graph)
        if not diodes:
            raise ValueError("No diodes found")

        diode = diodes[0]  # First stage: single-diode circuits
        anode_n = _node_for_port(graph, diode.id, "anode") or "A"
        cathode_n = _node_for_port(graph, diode.id, "cathode") or "K"

        vf = _safe_get_param(diode, "forward_voltage", 0.7)
        model = diode.model_variant or "constant_voltage_drop"

        VF = sp.Symbol("V_F")
        Vin = sp.Symbol("V_{in}")

        # ── Step 1: Identify circuit topology ───────────────────────
        steps.append(Step(
            description="识别二极管电路结构",
            formula=r"\text{二极管电路分析}",
            reasoning="确定二极管在电路中的连接方式（串联/并联/限幅/钳位）",
        ))

        # ── Step 2: Choose diode model ──────────────────────────────
        model_desc = {
            "ideal_diode": "理想二极管模型（V_F = 0，导通时短路，截止时开路）",
            "constant_voltage_drop": f"恒压降模型（V_F ≈ {vf} V）",
            "shockley": "Shockley 指数模型（I_D = I_S·(e^(V_D/V_T) - 1)）",
        }
        steps.append(Step(
            description="选择二极管模型",
            formula=rf"V_F \approx {vf}\,\text{{V}}",
            reasoning=model_desc.get(model, model_desc["constant_voltage_drop"]),
        ))

        # ── Step 3: Piecewise analysis ──────────────────────────────
        # Case A: Diode ON (V_D >= V_F)
        steps.append(Step(
            description="情况 1：二极管导通（V_D ≥ V_F）",
            formula=(
                rf"\text{{若 }} V_{{\text{{anode}}}} - V_{{\text{{cathode}}}} \geq {vf}\,\text{{V}}\text{{，}}"
                rf"\text{{则二极管导通，}} I_D > 0"
            ),
            reasoning="当阳极-阴极电压超过正向导通电压 V_F 时，二极管导通",
        ))

        steps.append(Step(
            description="情况 2：二极管截止（V_D < V_F）",
            formula=(
                rf"\text{{若 }} V_{{\text{{anode}}}} - V_{{\text{{cathode}}}} < {vf}\,\text{{V}}\text{{，}}"
                rf"\text{{则二极管截止，}} I_D = 0"
            ),
            reasoning="当阳极-阴极电压低于正向导通电压时，二极管截止",
        ))

        # ── Step 4: Transfer characteristic ─────────────────────────
        # Build the transfer characteristic based on topology
        # Detect series vs shunt diode
        input_node = None
        output_node = None
        for port in graph.ports:
            if port.role.value == "input":
                input_node = port.node_id
            elif port.role.value == "output":
                output_node = port.node_id

        # Determine if this is a limiter/clamp
        is_limiter = False
        if output_node:
            # Check if diode is between output and ground (shunt limiter)
            d_anode_n = _node_for_port(graph, diode.id, "anode")
            d_cathode_n = _node_for_port(graph, diode.id, "cathode")
            ground = _find_ground(graph)
            if (d_anode_n == output_node and d_cathode_n == ground) or \
               (d_cathode_n == output_node and d_anode_n == ground):
                is_limiter = True

        if is_limiter:
            # Positive limiter: diode cathode to ground, anode to output
            # V_out = V_in when diode OFF, V_out = V_F when diode ON
            if d_cathode_n == _find_ground(graph):
                # Positive limiter (diode anode at output, cathode to ground)
                steps.append(Step(
                    description="传输特性分析（正向限幅器）",
                    formula=(
                        r"V_{out} = \begin{cases}"
                        rf"V_{{in}}, & V_{{in}} < {vf}\,\text{{V}} \\"
                        rf"{vf}\,\text{{V}}, & V_{{in}} \geq {vf}\,\text{{V}}"
                        r"\end{cases}"
                    ),
                    reasoning="当输入电压低于 V_F 时二极管截止，输出跟随输入；"
                              "当输入电压达到 V_F 时二极管导通，输出电压被钳位在 V_F",
                ))
            else:
                # Negative limiter (diode cathode at output, anode to ground)
                steps.append(Step(
                    description="传输特性分析（负向限幅器）",
                    formula=(
                        r"V_{out} = \begin{cases}"
                        rf"V_{{in}}, & V_{{in}} > -{vf}\,\text{{V}} \\"
                        rf"-{vf}\,\text{{V}}, & V_{{in}} \leq -{vf}\,\text{{V}}"
                        r"\end{cases}"
                    ),
                    reasoning="当输入电压高于 -V_F 时二极管截止，输出跟随输入；"
                              "当输入电压低于 -V_F 时二极管导通，输出被钳位在 -V_F",
                ))

        equations.append(Equation(
            id="eq.diode_piecewise",
            latex=rf"\text{{导通条件: }} V_D \geq {vf}\,\text{{V}}",
            variables=["V_D", "V_F"],
        ))

        # ── Step 5: Summary ─────────────────────────────────────────
        steps.append(Step(
            description="总结",
            formula=r"\text{二极管电路分析的关键是判断二极管的导通/截止状态}",
            reasoning="先假设二极管状态 → 解线性电路 → 验证假设是否成立 → 若不成立则切换状态重新分析",
        ))

        return AnalysisResult(
            problem_type=self.get_problem_type(),
            steps=steps,
            equations=equations,
            teaching_tags=self.get_teaching_tags(),
            is_symbolic=True,
        )

    def _find_diodes(self, graph: CircuitGraph) -> list[ComponentInstance]:
        result: list[ComponentInstance] = []
        for comp in graph.components:
            d = DEFINITIONS_BY_ID.get(comp.definition_id)
            if d and d.family.value == "diode":
                result.append(comp)
        return result
