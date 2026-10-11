"""BJT DC bias operating-point analysis strategy.

Implements the textbook seven-step process from the technical report (§6.3):

  1. Draw DC equivalent circuit (capacitors → open, inductors → short, AC → off)
  2. Assume active region (V_BE ≈ 0.7 V for silicon NPN)
  3. KVL base-emitter loop → solve for I_B
  4. I_C = β · I_B
  5. KVL collector-emitter loop → solve for V_CE
  6. Verify active-region assumption (V_CE > V_CE(sat))
  7. Output operating point + LaTeX steps

Handles four common topologies:
  - Common-emitter with single base resistor to VCC
  - Common-emitter with voltage-divider bias (R_B1 to VCC, R_B2 to GND)
  - Common-emitter with emitter degeneration (R_E)
  - Common-collector (emitter follower)
"""

from __future__ import annotations

import sympy as sp

from ..component_defs import DEFINITIONS_BY_ID
from ..mna import _safe_get_param
from ..solver import SafeSolver
from ..types import (
    AnalysisResult,
    CircuitGraph,
    ComponentInstance,
    Equation,
    OperatingPoint,
    Step,
)
from .base import AnalysisStrategy


# ── Helpers ──────────────────────────────────────────────────────────


def _get_npn_bjt_defaults(bjt: ComponentInstance) -> tuple[float, float]:
    """Return (beta, vbe_on) for an NPN BJT."""
    beta = _safe_get_param(bjt, "beta", 100.0)
    vbe_on = _safe_get_param(bjt, "vbe_on", 0.7)
    return beta, vbe_on


def _get_pnp_bjt_defaults(bjt: ComponentInstance) -> tuple[float, float]:
    """Return (beta, |vbe_on|) for a PNP BJT."""
    beta = _safe_get_param(bjt, "beta", 100.0)
    vbe_on = _safe_get_param(bjt, "vbe_on", 0.7)
    return beta, vbe_on


# ── Topology analysis helpers ───────────────────────────────────────


def _node_for_component_port(
    graph: CircuitGraph, comp_id: str, port_id: str
) -> str | None:
    """Which node is connected to (comp_id, port_id)?"""
    for conn in graph.connections:
        if conn.component_id == comp_id and conn.port_id == port_id:
            return conn.node_id
    return None


def _component_at_node(
    graph: CircuitGraph, node_id: str, family: str
) -> ComponentInstance | None:
    """Find a component of *family* connected to *node_id*."""
    for comp in graph.components:
        definition = DEFINITIONS_BY_ID.get(comp.definition_id)
        if definition is None:
            continue
        if definition.family.value != family:
            continue
        for port_id in comp.ports:
            n = _node_for_component_port(graph, comp.id, port_id)
            if n == node_id:
                return comp
    return None


def _find_ground_node(graph: CircuitGraph) -> str:
    """Return the id of the reference (ground) node."""
    for node in graph.nodes:
        if node.kind.value == "reference":
            return node.id
    # Fallback: look for a ground component
    for comp in graph.components:
        if comp.definition_id == "reference.ground":
            for conn in graph.connections:
                if conn.component_id == comp.id and conn.port_id == "ref":
                    return conn.node_id
    # Last resort
    return graph.nodes[-1].id


def _connected_resistance(
    graph: CircuitGraph, node_a: str, node_b: str
) -> float | None:
    """If a resistor is directly between node_a and node_b, return its resistance (ohms).
    Returns None if no such resistor exists.
    """
    for comp in graph.components:
        if comp.definition_id != "passive.resistor":
            continue
        n_p = _node_for_component_port(graph, comp.id, "p")
        n_n = _node_for_component_port(graph, comp.id, "n")
        if (n_p == node_a and n_n == node_b) or (n_p == node_b and n_n == node_a):
            return _safe_get_param(comp, "resistance", 0.0)
    return None


def _dc_voltage_source_value(graph: CircuitGraph, node_a: str, node_b: str) -> float | None:
    """If a DC voltage source is between node_a (+) and node_b (-), return its voltage."""
    for comp in graph.components:
        if comp.definition_id != "source.v_dc":
            continue
        n_p = _node_for_component_port(graph, comp.id, "p")
        n_n = _node_for_component_port(graph, comp.id, "n")
        if n_p == node_a and n_n == node_b:
            return _safe_get_param(comp, "voltage", 0.0)
    return None


# ── Strategy ────────────────────────────────────────────────────────


class BJTDCBiasStrategy(AnalysisStrategy):
    """DC operating-point analysis for circuits containing BJTs without AC sources."""

    def get_problem_type(self) -> str:
        return "bjt_dc_bias"

    def get_teaching_tags(self) -> list[str]:
        return ["bjt", "dc_operating_point", "bias_analysis"]

    def can_handle(self, graph: CircuitGraph) -> bool:
        # Must have a BJT
        has_bjt = any(
            DEFINITIONS_BY_ID.get(c.definition_id, None) is not None
            and DEFINITIONS_BY_ID[c.definition_id].family.value == "bjt"
            for c in graph.components
        )
        if not has_bjt:
            return False
        # Must NOT have an AC source (otherwise use AC strategy)
        has_ac = any(
            c.definition_id in ("source.v_ac_small_signal", "source.i_ac_small_signal")
            for c in graph.components
        )
        return not has_ac

    # ── Main entry ─────────────────────────────────────────────────

    def analyze(self, graph: CircuitGraph) -> AnalysisResult:
        solver = SafeSolver(timeout_s=10.0, max_terms=80)
        warnings: list[str] = []
        steps: list[Step] = []
        equations: list[Equation] = []

        # Locate key elements
        ground = _find_ground_node(graph)
        bjt = self._find_bjt(graph)
        if bjt is None:
            raise ValueError("No BJT found in circuit")

        is_pnp = bjt.definition_id == "bjt.pnp"
        beta, vbe_on = (_get_pnp_bjt_defaults(bjt) if is_pnp
                        else _get_npn_bjt_defaults(bjt))

        b_node = _node_for_component_port(graph, bjt.id, "base") or ""
        c_node = _node_for_component_port(graph, bjt.id, "collector") or ""
        e_node = _node_for_component_port(graph, bjt.id, "emitter") or ""

        # ── Step 1: DC equivalent circuit ──────────────────────────
        steps.append(Step(
            description="绘制直流等效电路",
            formula=r"\text{电容开路，电感短路，AC 源置零}",
            reasoning="在直流偏置分析中，电容视为开路（直流稳态电流为零），"
                      "电感视为短路（直流稳态电压为零），交流小信号源置零",
        ))

        # ── Step 2: Assume active region ────────────────────────────
        vbe_sign = "-" if is_pnp else ""
        steps.append(Step(
            description="假设 BJT 工作在放大区",
            formula=(
                rf"V_{{BE}} \approx {vbe_sign}{vbe_on}\,\text{{V}}"
                if is_pnp
                else rf"V_{{BE}} \approx {vbe_on}\,\text{{V}}"
            ),
            reasoning="硅管发射结正向导通电压约 0.7 V（放大区典型值）",
        ))
        equations.append(Equation(
            id="eq.vbe_assumption",
            latex=rf"V_{{BE}} \approx {vbe_on}\,\text{{V}}",
            variables=["V_BE"],
        ))

        # ── Step 3: Base current ───────────────────────────────────
        # Detect topology:
        #   Type A: single R_B from VCC to base, emitter to ground
        #   Type B: voltage divider (R_B1 VCC→base, R_B2 base→GND)
        #   Type C: emitter degeneration (R_E emitter→GND)

        r_between_vcc_base = _connected_resistance(graph, c_node, b_node)
        # Actually, let me think about this more carefully.
        # For the base bias, we need to find resistors connected to the base.
        # The standard approach:
        #   1. Find all DC paths from VCC/GND to base
        #   2. Compute Thevenin equivalent at base

        # Let me use a more direct approach: trace the base network
        ib_expr, ib_steps, ib_eqs = self._solve_base_current(
            graph, bjt, b_node, c_node, e_node, ground, beta, vbe_on, is_pnp, solver
        )
        steps.extend(ib_steps)
        equations.extend(ib_eqs)

        if ib_expr is None:
            return AnalysisResult(
                problem_type=self.get_problem_type(),
                steps=steps,
                equations=equations,
                teaching_tags=self.get_teaching_tags(),
                warnings=["Could not determine base current — circuit topology not recognized"],
                is_symbolic=False,
            )

        # ── Step 4: Collector current ───────────────────────────────
        ib_sym = sp.Symbol("I_B")
        ic_expr = beta * ib_sym
        steps.append(Step(
            description="求解集电极电流",
            formula=rf"I_C = \beta I_B = {sp.latex(ic_expr)}",
            reasoning="BJT 在放大区满足 I_C = β · I_B",
        ))
        equations.append(Equation(
            id="eq.ic_beta_ib",
            latex=r"I_C = \beta I_B",
            variables=["I_C", r"\beta", "I_B"],
        ))

        # ── Step 5: VCE ─────────────────────────────────────────────
        vce_expr, vce_steps = self._solve_vce(
            graph, bjt, c_node, e_node, ground, ic_expr, ib_sym, beta, is_pnp, solver
        )
        steps.extend(vce_steps)

        # ── Step 6: Verify active region ─────────────────────────────

        steps.append(Step(
            description="验证工作区假设",
            formula=r"V_{CE} > V_{CE(\text{sat})} \approx 0.2\,\text{V}",
            reasoning="若 V_CE > 0.2 V，则放大区假设成立；否则需重新假设饱和区分析。"
                      "（具体数值需代入实际元件参数验证）",
        ))

        # ── Build result ─────────────────────────────────────────────
        # node_voltages: actual voltages relative to ground
        # Express them in terms of I_B and the solved parameters
        I_B_sym = sp.Symbol("I_B")
        V_CC_sym = sp.Symbol("V_CC")
        V_BE_sym2 = sp.Symbol("V_BE")
        beta_sym = sp.Symbol("beta")

        # Locate R_C (collector resistor) and R_E (emitter resistor)
        rc_val = 0.0
        for comp in graph.components:
            if comp.definition_id == "passive.resistor":
                n_p = _node_for_component_port(graph, comp.id, "p")
                n_n = _node_for_component_port(graph, comp.id, "n")
                if (n_p == c_node or n_n == c_node) and n_p != e_node and n_n != e_node:
                    rc_val = _safe_get_param(comp, "resistance", 0.0)

        re_val = 0.0
        for comp in graph.components:
            if comp.definition_id == "passive.resistor":
                n_p = _node_for_component_port(graph, comp.id, "p")
                n_n = _node_for_component_port(graph, comp.id, "n")
                if (n_p == e_node or n_n == e_node) and (n_p == ground or n_n == ground):
                    re_val = _safe_get_param(comp, "resistance", 0.0)

        R_C_sym = sp.Symbol("R_C")
        R_E_sym = sp.Symbol("R_E")
        base_voltage = V_BE_sym2 + (beta_sym + 1) * I_B_sym * R_E_sym
        collector_voltage = V_CC_sym - beta_sym * I_B_sym * R_C_sym
        emitter_voltage = (beta_sym + 1) * I_B_sym * R_E_sym

        op = OperatingPoint(
            node_voltages={
                "base": sp.latex(base_voltage),
                "collector": sp.latex(collector_voltage),
                "emitter": sp.latex(emitter_voltage),
            },
            branch_currents={
                "I_B": sp.latex(ib_expr),
                "I_C": sp.latex(ic_expr),
                "I_E": sp.latex((beta_sym + 1) * I_B_sym),
            },
            regions={bjt.id: "active (assumed)"},
        )

        return AnalysisResult(
            problem_type=self.get_problem_type(),
            steps=steps,
            equations=equations,
            operating_point=op,
            teaching_tags=self.get_teaching_tags(),
            warnings=warnings,
            is_symbolic=True,
        )

    # ── Internal helpers ────────────────────────────────────────────

    def _find_bjt(self, graph: CircuitGraph) -> ComponentInstance | None:
        for comp in graph.components:
            definition = DEFINITIONS_BY_ID.get(comp.definition_id)
            if definition and definition.family.value == "bjt":
                return comp
        return None

    def _solve_base_current(
        self,
        graph: CircuitGraph,
        bjt: ComponentInstance,
        b_node: str,
        c_node: str,
        e_node: str,
        ground: str,
        beta: float,
        vbe_on: float,
        is_pnp: bool,
        solver: SafeSolver,
    ) -> tuple[sp.Expr | None, list[Step], list[Equation]]:
        """Determine the base current expression based on circuit topology."""

        # Find VCC node (connected to the positive side of the DC voltage source)
        vcc_node = ""
        vcc_val = 12.0
        for comp in graph.components:
            if comp.definition_id == "source.v_dc":
                vcc_val = _safe_get_param(comp, "voltage", 12.0)
                vcc_node = _node_for_component_port(graph, comp.id, "p") or ""
                break

        # Find resistors connected to base
        base_resistors: list[tuple[ComponentInstance, str, str]] = []
        # (resistor, node_connected_to_base, other_node)

        for comp in graph.components:
            if comp.definition_id != "passive.resistor":
                continue
            n_p = _node_for_component_port(graph, comp.id, "p")
            n_n = _node_for_component_port(graph, comp.id, "n")
            if n_p == b_node:
                base_resistors.append((comp, b_node, n_n or ""))
            elif n_n == b_node:
                base_resistors.append((comp, b_node, n_p or ""))

        # Find emitter resistor
        r_e_val = _connected_resistance(graph, e_node, ground)
        has_re = r_e_val is not None and r_e_val > 0

        # Build symbolic variables
        VCC = sp.Symbol("V_CC")
        VBE = sp.Symbol("V_BE")
        IB = sp.Symbol("I_B")
        Beta = sp.Symbol("beta")

        if is_pnp:
            # PNP: emitter at higher potential, current flows emitter → collector
            VBE_sym = -sp.Abs(VBE)
        else:
            VBE_sym = sp.Abs(VBE)

        steps: list[Step] = []
        eqs: list[Equation] = []

        # ── Topology detection ──────────────────────────────────────

        if len(base_resistors) == 1:
            # Single R_B from some node to base
            rb_comp, _, other = base_resistors[0]
            rb_val = _safe_get_param(rb_comp, "resistance", 100_000.0)
            RB = sp.Symbol("R_B")

            steps.append(Step(
                description="列基极回路 KVL 方程",
                formula=(
                    rf"V_{{CC}} = I_B R_B + V_{{BE}} + I_E R_E"
                    if has_re
                    else rf"V_{{CC}} = I_B R_B + V_{{BE}}"
                ),
                reasoning="沿基极-发射极回路列写基尔霍夫电压定律",
            ))

            if has_re:
                re_val_f = r_e_val or 0.0
                RE = sp.Symbol("R_E")
                # VCC = IB*RB + VBE + (beta+1)*IB*RE
                ib_eq = sp.Eq(VCC, IB * RB + VBE_sym + (Beta + 1) * IB * RE)
                ib_solution = sp.solve(ib_eq, IB)
                ib_expr = ib_solution[0] if ib_solution else None
                formula = r"I_B = \frac{V_{CC} - V_{BE}}{R_B + (\beta + 1)R_E}"
                eqs.append(Equation(
                    id="eq.ib_kvl_with_re",
                    latex=r"V_{CC} = I_B R_B + V_{BE} + (\beta + 1)I_B R_E",
                    variables=["V_CC", "I_B", "R_B", "V_BE", "beta", "R_E"],
                ))
            else:
                # VCC = IB * RB + VBE
                ib_eq = sp.Eq(VCC, IB * RB + VBE_sym)
                ib_solution = sp.solve(ib_eq, IB)
                ib_expr = ib_solution[0] if ib_solution else None
                formula = r"I_B = \frac{V_{CC} - V_{BE}}{R_B}"
                eqs.append(Equation(
                    id="eq.ib_kvl",
                    latex=r"V_{CC} = I_B R_B + V_{BE}",
                    variables=["V_CC", "I_B", "R_B", "V_BE"],
                ))

            steps.append(Step(
                description="求解基极电流",
                formula=f"I_B = {sp.latex(ib_expr)}" if ib_expr else "N/A",
                reasoning="由 KVL 方程解出 I_B",
            ))

            return ib_expr, steps, eqs

        elif len(base_resistors) == 2:
            # Voltage divider bias: R_B1 (VCC→base), R_B2 (base→GND)
            # Thevenin equivalent at base: V_BB = VCC * R_B2/(R_B1+R_B2), R_BB = R_B1||R_B2

            # Determine which resistor goes to VCC/GND
            r1: ComponentInstance | None = None
            r2: ComponentInstance | None = None
            for comp, _, other in base_resistors:
                if other == vcc_node or other == c_node:
                    r1 = comp
                elif other == ground:
                    r2 = comp

            if r1 is None or r2 is None:
                return None, steps, eqs

            rb1_val = _safe_get_param(r1, "resistance", 100_000.0)
            rb2_val = _safe_get_param(r2, "resistance", 100_000.0)
            RB1 = sp.Symbol("R_{B1}")
            RB2 = sp.Symbol("R_{B2}")

            steps.append(Step(
                description="求基极偏置网络的戴维南等效",
                formula=(
                    r"V_{BB} = V_{CC} \cdot \frac{R_{B2}}{R_{B1} + R_{B2}}, \quad "
                    r"R_{BB} = R_{B1} \parallel R_{B2}"
                ),
                reasoning="将基极偏置电阻网络等效为戴维南等效电路，"
                          "以简化基极回路的分析",
            ))
            eqs.append(Equation(
                id="eq.thevenin_vbb",
                latex=r"V_{BB} = V_{CC} \frac{R_{B2}}{R_{B1} + R_{B2}}",
                variables=["V_BB", "V_CC", "R_B1", "R_B2"],
            ))
            eqs.append(Equation(
                id="eq.thevenin_rbb",
                latex=r"R_{BB} = \frac{R_{B1} R_{B2}}{R_{B1} + R_{B2}}",
                variables=["R_BB", "R_B1", "R_B2"],
            ))

            # V_BB = I_B * R_BB + V_BE + I_E * R_E
            VBB = VCC * RB2 / (RB1 + RB2)
            RBB = RB1 * RB2 / (RB1 + RB2)

            steps.append(Step(
                description="列基极回路 KVL 方程（戴维南等效后）",
                formula=(
                    rf"V_{{BB}} = I_B R_{{BB}} + V_{{BE}} + I_E R_E"
                    if has_re
                    else rf"V_{{BB}} = I_B R_{{BB}} + V_{{BE}}"
                ),
                reasoning="对戴维南等效后的基极回路列写 KVL",
            ))

            if has_re:
                RE = sp.Symbol("R_E")
                ib_eq = sp.Eq(VBB, IB * RBB + VBE_sym + (Beta + 1) * IB * RE)
                ib_solution = sp.solve(ib_eq, IB)
                ib_expr = ib_solution[0] if ib_solution else None
                formula = r"I_B = \frac{V_{BB} - V_{BE}}{R_{BB} + (\beta + 1)R_E}"
            else:
                ib_eq = sp.Eq(VBB, IB * RBB + VBE_sym)
                ib_solution = sp.solve(ib_eq, IB)
                ib_expr = ib_solution[0] if ib_solution else None
                formula = r"I_B = \frac{V_{BB} - V_{BE}}{R_{BB}}"

            steps.append(Step(
                description="求解基极电流",
                formula=f"I_B = {sp.latex(ib_expr)}" if ib_expr else "N/A",
                reasoning="由戴维南等效后的 KVL 方程解出 I_B",
            ))

            return ib_expr, steps, eqs

        else:
            return None, steps, eqs

    def _solve_vce(
        self,
        graph: CircuitGraph,
        bjt: ComponentInstance,
        c_node: str,
        e_node: str,
        ground: str,
        ic_expr: sp.Expr,
        ib_sym: sp.Symbol,
        beta: float,
        is_pnp: bool,
        solver: SafeSolver,
    ) -> tuple[sp.Expr | None, list[Step]]:
        """Solve for V_CE using KVL around the collector-emitter loop."""
        steps: list[Step] = []

        VCC = sp.Symbol("V_CC")
        IC = sp.Symbol("I_C")
        IE = sp.Symbol("I_E")
        Beta = sp.Symbol("beta")

        # Find collector resistor
        rc_val = 0.0
        for comp in graph.components:
            if comp.definition_id != "passive.resistor":
                continue
            n_p = _node_for_component_port(graph, comp.id, "p")
            n_n = _node_for_component_port(graph, comp.id, "n")
            # Collector resistor: one end at collector, other at VCC
            if (n_p == c_node or n_n == c_node) and (n_p != e_node and n_n != e_node):
                rc_val = _safe_get_param(comp, "resistance", 0.0)
                break

        # Find emitter resistor
        re_val = _connected_resistance(graph, e_node, ground) or 0.0

        RC = sp.Symbol("R_C")
        RE = sp.Symbol("R_E")

        steps.append(Step(
            description="列集电极-发射极回路 KVL 方程",
            formula=(
                rf"V_{{CC}} = I_C R_C + V_{{CE}} + I_E R_E"
                if re_val > 0
                else rf"V_{{CC}} = I_C R_C + V_{{CE}}"
            ),
            reasoning="沿集电极电源回路列写基尔霍夫电压定律",
        ))

        VCE = sp.Symbol("V_{CE}")

        if re_val > 0:
            # VCC = IC*RC + VCE + IE*RE;  IE = (β+1)/β * IC ≈ IC
            ie_expr = (Beta + 1) / Beta * IC
            eq_vce = sp.Eq(VCC, IC * RC + VCE + ie_expr * RE)
        else:
            eq_vce = sp.Eq(VCC, IC * RC + VCE)

        vce_solutions = sp.solve(eq_vce, VCE)
        vce_expr = vce_solutions[0] if vce_solutions else None

        if vce_expr is not None:
            # Substitute IC = β·IB (via ic_expr)
            # vce_expr already in terms of IC, VCC, RC — we express symbolically
            pass

        steps.append(Step(
            description="求解集电极-发射极电压",
            formula=f"V_{{CE}} = {sp.latex(vce_expr)}" if vce_expr else "N/A",
            reasoning="由 KVL 方程解出 V_CE",
        ))

        return vce_expr, steps
