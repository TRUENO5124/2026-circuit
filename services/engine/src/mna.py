"""Modified Nodal Analysis (MNA) linear circuit solver.

Builds the MNA matrix system  [G  B; C  D] [v; j] = [i; e]  for a linear
circuit and solves it symbolically using SymPy.

This module handles only LINEAR components (resistors, independent sources,
dependent sources, capacitors/inductors in s-domain).  Nonlinear devices
(BJT, diode, MOSFET) must be linearized by the course-rules layer before
calling into this module.

Reference:
  Ho, Ruehli & Brennan (1975) "The Modified Nodal Approach to Network Analysis"
  IEEE Trans. Circuits and Systems, CAS-22(6), 504–509.
"""

from __future__ import annotations

from dataclasses import dataclass, field
from typing import Optional

import sympy as sp


# ── Internal representation of a linear component ───────────────────


@dataclass
class LinearComponent:
    """A single linear element ready for MNA stamping.

    Each type maps to a specific MNA stamp pattern (see stamp() method).
    """

    kind: str  # "resistor", "voltage_source", "current_source", "vccs", "vcvs", "cccs", "ccvs"
    label: str
    nodes: tuple[int, int]  # (node_i, node_j) — non-negative indices; ground is excluded
    value: sp.Expr  # symbolic value (e.g. R, 1/R, gm, s*C)
    # For sources with a separate branch variable:
    branch_idx: int | None = None  # voltage-source branch row index
    # For controlled sources:
    ctrl_nodes: tuple[int, int] | None = None  # (ctrl_plus, ctrl_minus)
    ctrl_branch_idx: int | None = None  # for CCCS/CCVS: the controlling branch index

    def __post_init__(self) -> None:
        # Components that should NOT carry a branch variable:
        if self.kind in ("resistor", "current_source", "vccs", "cccs"):
            assert self.branch_idx is None, f"{self.kind} has no branch variable"
        # Components that MUST carry a branch variable (voltage constraint):
        if self.kind in ("voltage_source", "vcvs", "ccvs"):
            assert self.branch_idx is not None, f"{self.kind} needs a branch variable"


# ── MNA Builder ─────────────────────────────────────────────────────


class MNABuilder:
    """Assembles and solves the MNA linear system for a circuit."""

    def __init__(self, num_nodes: int, num_branches: int = 0):
        """
        Args:
            num_nodes: Number of non-ground nodes (indexed 0 .. num_nodes-1).
            num_branches: Number of voltage-source (and inductor) branch variables.
        """
        self.num_nodes = num_nodes
        self.num_branches = num_branches
        self._components: list[LinearComponent] = []

    # -- registration -------------------------------------------------

    def add_resistor(self, label: str, ni: int, nj: int, resistance: sp.Expr) -> None:
        """Add a resistor between nodes ni and nj (conductance = 1/R)."""
        self._components.append(LinearComponent(
            kind="resistor", label=label, nodes=(ni, nj), value=1 / resistance,
        ))

    def add_conductance(self, label: str, ni: int, nj: int, conductance: sp.Expr) -> None:
        """Add a conductance directly (G = 1/R) between nodes ni and nj."""
        self._components.append(LinearComponent(
            kind="resistor", label=label, nodes=(ni, nj), value=conductance,
        ))

    def add_voltage_source(self, label: str, ni: int, nj: int, voltage: sp.Expr, branch_idx: int) -> None:
        """Add an independent voltage source: V(ni) - V(nj) = voltage."""
        self._components.append(LinearComponent(
            kind="voltage_source", label=label, nodes=(ni, nj),
            value=voltage, branch_idx=branch_idx,
        ))

    def add_current_source(self, label: str, ni: int, nj: int, current: sp.Expr) -> None:
        """Add an independent current source flowing from ni to nj."""
        self._components.append(LinearComponent(
            kind="current_source", label=label, nodes=(ni, nj), value=current,
        ))

    def add_vccs(self, label: str, ni: int, nj: int,
                 ctrl_plus: int, ctrl_minus: int, gm: sp.Expr) -> None:
        """Add a voltage-controlled current source: I_out = gm * (V(ctrl_plus) - V(ctrl_minus))."""
        self._components.append(LinearComponent(
            kind="vccs", label=label, nodes=(ni, nj),
            value=gm, ctrl_nodes=(ctrl_plus, ctrl_minus),
        ))

    def add_vcvs(self, label: str, ni: int, nj: int,
                 ctrl_plus: int, ctrl_minus: int, gain: sp.Expr, branch_idx: int) -> None:
        """Add a voltage-controlled voltage source: V(ni) - V(nj) = gain * (V(ctrl_plus) - V(ctrl_minus))."""
        self._components.append(LinearComponent(
            kind="vcvs", label=label, nodes=(ni, nj),
            value=gain, ctrl_nodes=(ctrl_plus, ctrl_minus), branch_idx=branch_idx,
        ))

    def add_cccs(self, label: str, ni: int, nj: int,
                 ctrl_branch: int, gain: sp.Expr) -> None:
        """Add a current-controlled current source: I_out = gain * I(ctrl_branch)."""
        self._components.append(LinearComponent(
            kind="cccs", label=label, nodes=(ni, nj),
            value=gain, ctrl_branch_idx=ctrl_branch,
        ))

    def add_ccvs(self, label: str, ni: int, nj: int,
                 ctrl_branch: int, gain: sp.Expr, branch_idx: int) -> None:
        """Add a current-controlled voltage source: V(ni)-V(nj) = gain * I(ctrl_branch)."""
        self._components.append(LinearComponent(
            kind="ccvs", label=label, nodes=(ni, nj),
            value=gain, ctrl_branch_idx=ctrl_branch, branch_idx=branch_idx,
        ))

    # -- assembly -----------------------------------------------------

    def _matrix_size(self) -> int:
        return self.num_nodes + self.num_branches

    def build(self) -> tuple[sp.Matrix, sp.Matrix]:
        """Assemble the MNA system: A * x = z.

        Returns (A, z) where A is (n+m)×(n+m) and z is (n+m)×1.
        x = [V_0, ..., V_{n-1}, J_0, ..., J_{m-1}]^T
        """
        size = self._matrix_size()
        n = self.num_nodes
        A = sp.zeros(size, size)
        z = sp.zeros(size, 1)

        for c in self._components:
            self._stamp(c, A, z, n)

        return A, z

    def _stamp(self, c: LinearComponent, A: sp.Matrix, z: sp.Matrix, n: int) -> None:
        """Dispatch to the correct stamp method."""
        method = getattr(self, f"_stamp_{c.kind}", None)
        if method is None:
            raise ValueError(f"Unknown component kind: {c.kind}")
        method(c, A, z, n)

    # -- stamp methods ------------------------------------------------

    def _stamp_resistor(self, c: LinearComponent, A: sp.Matrix, _z: sp.Matrix, _n: int) -> None:
        """G[i,i] += G;  G[j,j] += G;  G[i,j] -= G;  G[j,i] -= G"""
        i, j = c.nodes
        G = c.value
        if i >= 0:
            A[i, i] += G
        if j >= 0:
            A[j, j] += G
        if i >= 0 and j >= 0:
            A[i, j] -= G
            A[j, i] -= G

    def _stamp_voltage_source(self, c: LinearComponent, A: sp.Matrix, z: sp.Matrix, n: int) -> None:
        """B[i,k] += 1; B[j,k] -= 1; C[k,i] += 1; C[k,j] -= 1; e[k] = V"""
        i, j = c.nodes
        k = n + (c.branch_idx or 0)
        V = c.value
        if i >= 0:
            A[i, k] += 1
            A[k, i] += 1
        if j >= 0:
            A[j, k] -= 1
            A[k, j] -= 1
        z[k] = V

    def _stamp_current_source(self, c: LinearComponent, _A: sp.Matrix, z: sp.Matrix, _n: int) -> None:
        """I enters node i, leaves node j → RHS[i] += I; RHS[j] -= I"""
        i, j = c.nodes
        I_val = c.value
        if i >= 0:
            z[i] += I_val
        if j >= 0:
            z[j] -= I_val

    def _stamp_vccs(self, c: LinearComponent, A: sp.Matrix, _z: sp.Matrix, _n: int) -> None:
        """I_out = gm * V_ctrl injected INTO out_i, withdrawn FROM out_j.

        KCL at out_i: current LEAVING through VCCS = -gm*(V_cp - V_cm)
        KCL at out_j: current LEAVING through VCCS = +gm*(V_cp - V_cm)
        """
        out_i, out_j = c.nodes
        cp, cm = c.ctrl_nodes or (0, 0)
        gm = c.value
        # G[out_i, cp] = -gm; G[out_i, cm] = +gm  (leaving: -gm*V_cp + gm*V_cm)
        # G[out_j, cp] = +gm; G[out_j, cm] = -gm  (leaving: +gm*V_cp - gm*V_cm)
        pairs = [
            (out_i, cp, -gm), (out_i, cm, +gm),
            (out_j, cp, +gm), (out_j, cm, -gm),
        ]
        for row, col, val in pairs:
            if row >= 0 and col >= 0:
                A[row, col] += val

    def _stamp_vcvs(self, c: LinearComponent, A: sp.Matrix, z: sp.Matrix, n: int) -> None:
        """V_out = gain * V_ctrl  →  stamp into B/C with gain on control nodes."""
        out_i, out_j = c.nodes
        cp, cm = c.ctrl_nodes or (0, 0)
        k = n + (c.branch_idx or 0)
        gain = c.value
        if out_i >= 0:
            A[out_i, k] += 1
            A[k, out_i] += 1
        if out_j >= 0:
            A[out_j, k] -= 1
            A[k, out_j] -= 1
        if cp >= 0:
            A[k, cp] -= gain
        if cm >= 0:
            A[k, cm] += gain

    def _stamp_cccs(self, c: LinearComponent, A: sp.Matrix, _z: sp.Matrix, n: int) -> None:
        """I_out = gain * I_ctrl  →  I_out flows INTO out_i, OUT OF out_j.

        I_ctrl = J_{ctrl_branch} is one of the MNA unknown variables (column
        n + ctrl_branch_idx in A).  Stamp gain into the G block at the output
        node rows and the controlling branch column.
        """
        out_i, out_j = c.nodes
        ctrl_k = c.ctrl_branch_idx or 0
        ctrl_col = n + ctrl_k
        gain = c.value
        if out_i >= 0:
            A[out_i, ctrl_col] += gain
        if out_j >= 0:
            A[out_j, ctrl_col] -= gain

    def _stamp_ccvs(self, c: LinearComponent, A: sp.Matrix, z: sp.Matrix, n: int) -> None:
        """V_out = gain * I_ctrl  →  new branch variable J_{k} enforces:

        V(out_i) - V(out_j) - gain * J_{ctrl_branch} = 0
        """
        out_i, out_j = c.nodes
        ctrl_k = c.ctrl_branch_idx or 0
        ctrl_col = n + ctrl_k
        k = n + (c.branch_idx or 0)
        gain = c.value
        # B/C block — J_k enters/leaves output nodes
        if out_i >= 0:
            A[out_i, k] += 1
            A[k, out_i] += 1
        if out_j >= 0:
            A[out_j, k] -= 1
            A[k, out_j] -= 1
        # D block — constraint involving controlling branch current
        A[k, ctrl_col] -= gain
        # RHS for this constraint is zero (not gain * some symbol)
        z[k] = sp.Integer(0)

    # -- solving ------------------------------------------------------

    def solve(self) -> dict[str, sp.Expr]:
        """Build the MNA system and return node voltages.

        Returns dict with keys 'V_0', 'V_1', ... for each non-ground node,
        and 'J_0', 'J_1', ... for each branch variable.
        """
        A, z = self.build()
        x = A.LUsolve(z)
        result: dict[str, sp.Expr] = {}
        for i in range(self.num_nodes):
            result[f"V_{i}"] = sp.simplify(x[i])
        for j in range(self.num_branches):
            result[f"J_{j}"] = sp.simplify(x[self.num_nodes + j])
        return result

    def solve_for(self, node_indices: list[int]) -> dict[int, sp.Expr]:
        """Solve and return only the specified node voltages."""
        full = self.solve()
        return {i: full[f"V_{i}"] for i in node_indices}


# ── Convenience helpers ──────────────────────────────────────────────


def _safe_float(value) -> float:
    """Extract a float from a ParameterValue."""
    if hasattr(value, 'value'):
        v = value.value  # type: ignore[union-attr]
    else:
        v = value
    return float(v) if not isinstance(v, bool) else float(v)


def _safe_get_param(comp, param_id: str, default: float) -> float:
    """Get a numeric parameter value from a component instance, or default."""
    p = comp.parameters.get(param_id)
    if p is None:
        return default
    return _safe_float(p)
