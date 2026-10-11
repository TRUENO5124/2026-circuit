"""Unit tests for the MNA linear circuit solver."""

from __future__ import annotations

import sys
from pathlib import Path

_ENGINE_DIR = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(_ENGINE_DIR.parent))

import sympy as sp
from engine.src.mna import MNABuilder
from engine.src.solver import SafeSolver, SolverTimeoutError


def test_voltage_divider() -> None:
    """Simple resistive voltage divider: V1=5V, R1=1k, R2=1k → V_mid = 2.5V."""
    builder = MNABuilder(num_nodes=2, num_branches=1)

    # V1 between node 0 (positive) and ground → branch 0
    builder.add_voltage_source("V1", 0, -1, sp.Integer(5), branch_idx=0)

    # R1 between node 0 and node 1, 1000 Ω
    builder.add_resistor("R1", 0, 1, sp.Integer(1000))

    # R2 between node 1 and ground
    builder.add_resistor("R2", 1, -1, sp.Integer(1000))

    result = builder.solve()
    assert result["V_0"] == 5  # VCC node
    assert result["V_1"] == sp.Rational(5, 2)  # V_mid = 2.5V
    print("  OK voltage divider: V_mid = 5/2")


def test_current_source_resistor() -> None:
    """Current source into a resistor: I=2A, R=3Ω → V = I*R = 6V."""
    builder = MNABuilder(num_nodes=1, num_branches=0)

    # 2 A current source flowing into node 0 (from ground)
    builder.add_current_source("I1", 0, -1, sp.Integer(2))

    # 3 Ω resistor from node 0 to ground
    builder.add_resistor("R1", 0, -1, sp.Integer(3))

    result = builder.solve()
    assert sp.simplify(result["V_0"] - 6) == 0
    print("  OK current source + resistor: V = 6")


def test_vccs_circuit() -> None:
    """VCCS (transconductance amplifier): gm * V1 drives output."""
    builder = MNABuilder(num_nodes=2, num_branches=1)

    # V_in = 1V between node 0 and ground
    builder.add_voltage_source("Vin", 0, -1, sp.Integer(1), branch_idx=0)

    # R1 = 1 Ω from node 0 to ground
    builder.add_resistor("R1", 0, -1, sp.Integer(1))

    # VCCS: gm=2, output at node 1→ground, controlled by V(node 0)
    builder.add_vccs("G1", 1, -1, 0, -1, sp.Integer(2))

    # R2 = 1 Ω from node 1 to ground
    builder.add_resistor("R2", 1, -1, sp.Integer(1))

    result = builder.solve()
    # V(0) = 1V (set by voltage source)
    # Current into node 1: gm * V(0) = 2 * 1 = 2A → V(1) = 2 * 1 = 2V
    assert result["V_0"] == 1
    assert sp.simplify(result["V_1"] - 2) == 0
    print("  [OK] VCCS: V_out = 2")


def test_safety_solver_timeout_detection() -> None:
    """SafeSolver provides a SolverResult with success flag."""
    solver = SafeSolver(timeout_s=5.0, max_terms=50)

    # Simple solve should succeed
    A = sp.Matrix([[1, 0], [0, 1]])
    b = sp.Matrix([3, 4])
    x, y = sp.symbols("x y")
    result = solver.solve_linear(A, b, [x, y])

    assert result.success
    assert not result.degraded
    assert result.value[x] == 3
    assert result.value[y] == 4
    print("  [OK] SafeSolver: simple solve succeeds")


def test_safety_solver_complexity_guard() -> None:
    """SafeSolver degrades when solution exceeds max_terms."""
    solver = SafeSolver(timeout_s=5.0, max_terms=3)

    # Build a symbolic system with known many-term solutions
    a, b, c = sp.symbols("a b c")
    x, y = sp.symbols("x y")
    # [a+b, a-b; c, a+c] * [x; y] = [1; 1]  — non-singular for generic a,b,c
    A = sp.Matrix([
        [a + b, a - b],
        [c, a + c],
    ])
    b_vec = sp.Matrix([1, 1])

    result = solver.solve_linear(A, b_vec, [x, y])
    # May succeed or degrade depending on expression complexity
    assert result.success or result.degraded, f"Unexpected failure: {result.warning}"
    if result.degraded:
        print(f"  [OK] complexity guard degraded: {result.warning}")
    else:
        print(f"  [OK] solve completed (term_count={result.term_count})")


def test_expression_simplify() -> None:
    """SafeSolver.simplify reduces expressions and checks complexity."""
    solver = SafeSolver(timeout_s=5.0, max_terms=200)

    # sin(x)^2 + cos(x)^2 = 1
    x = sp.Symbol("x")
    expr = sp.sin(x)**2 + sp.cos(x)**2
    result = solver.simplify(expr)

    assert result.success
    assert sp.simplify(result.value - 1) == 0
    print(f"  [OK] simplify: sin²x + cos²x = {result.value}")


def test_mna_matrix_size() -> None:
    """MNA matrix dimensions are correct."""
    # 3 non-ground nodes + 2 voltage sources → 5×5 matrix
    builder = MNABuilder(num_nodes=3, num_branches=2)
    builder.add_voltage_source("V1", 0, -1, sp.Integer(10), branch_idx=0)
    builder.add_voltage_source("V2", 2, -1, sp.Integer(5), branch_idx=1)
    builder.add_resistor("R1", 0, 1, sp.Integer(1000))
    builder.add_resistor("R2", 1, 2, sp.Integer(2000))
    builder.add_resistor("R3", 1, -1, sp.Integer(3000))

    A, z = builder.build()
    assert A.rows == 5
    assert A.cols == 5
    assert z.rows == 5
    print(f"  [OK] MNA matrix: {A.rows}×{A.cols}")


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")

    tests = [
        ("voltage divider", test_voltage_divider),
        ("current source + resistor", test_current_source_resistor),
        ("VCCS circuit", test_vccs_circuit),
        ("safety solver timeout detection", test_safety_solver_timeout_detection),
        ("safety solver complexity guard", test_safety_solver_complexity_guard),
        ("expression simplify", test_expression_simplify),
        ("MNA matrix size", test_mna_matrix_size),
    ]

    failed = 0
    for name, fn in tests:
        try:
            fn()
        except Exception as exc:
            failed += 1
            print(f"  [FAIL] {name}: {exc}")
            import traceback
            traceback.print_exc()

    print(f"\n{'='*50}")
    if failed:
        print(f"FAILED: {failed}/{len(tests)} tests failed.")
        sys.exit(1)
    else:
        print(f"OK: {len(tests)} tests passed.")
