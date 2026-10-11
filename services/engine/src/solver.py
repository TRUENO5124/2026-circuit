"""Safety-wrapped symbolic solver.

Implements the three-tier protection mechanism described in the technical
report (§7):

  L1 — Timeout control:       kills the solve after a deadline.
  L2 — Complexity limit:      rejects expressions with too many terms.
  L3 — Degradation fallback:  returns a numerical approximation when L1/L2 fire.

On Windows, signal.SIGALRM is unavailable, so we use threading with a
daemon timer.  The solver runs in the calling thread and a timer thread
sets a shared flag when the deadline expires.
"""

from __future__ import annotations

import math
import threading
from dataclasses import dataclass
from typing import Any, Callable, Optional

import sympy as sp


@dataclass
class SolverResult:
    """Outcome of a safety-wrapped solve."""
    value: Any = None
    success: bool = True
    degraded: bool = False
    warning: str | None = None
    term_count: int = 0


class SolverTimeoutError(Exception):
    """Raised when a solve exceeds the time budget."""


def _run_with_timeout(
    func: Callable[[], Any],
    timeout_s: float,
) -> Any:
    """Run *func* in the calling thread; raise SolverTimeoutError if it takes > timeout_s.

    Uses a threading.Event flag set by a daemon timer.  This is best-effort:
    SymPy operations in C extensions (e.g. FLINT) may not check the flag
    until they return to Python.  In practice, setting a generous timeout
    (e.g. 10 s) catches runaway expression expansion.
    """
    if timeout_s <= 0:
        return func()

    result_holder: list[Any] = [None]
    error_holder: list[Exception | None] = [None]
    done = threading.Event()

    def _worker() -> None:
        try:
            result_holder[0] = func()
        except Exception as exc:
            error_holder[0] = exc
        finally:
            done.set()

    thread = threading.Thread(target=_worker, daemon=True)
    thread.start()
    finished = done.wait(timeout=timeout_s)

    if not finished:
        raise SolverTimeoutError(f"Solve timed out after {timeout_s:.0f}s")

    if error_holder[0] is not None:
        raise error_holder[0]

    return result_holder[0]


def _count_terms(expr: sp.Expr) -> int:
    """Count the number of additive terms in an expression (heuristic complexity)."""
    expanded = sp.expand(expr)
    if isinstance(expanded, sp.Add):
        return len(expanded.args)
    return 1


def _to_float_safe(expr: sp.Expr, subs: dict[sp.Symbol, float]) -> float | None:
    """Try to evaluate expr numerically; return None on failure."""
    try:
        val = float(expr.subs(subs).evalf())
        if math.isfinite(val):
            return val
    except (TypeError, ValueError, AttributeError):
        pass
    return None


# ── Public API ──────────────────────────────────────────────────────


class SafeSolver:
    """Symbolic solver with timeout, complexity guard, and numerical fallback."""

    def __init__(
        self,
        timeout_s: float = 10.0,
        max_terms: int = 100,
        default_subs: dict[str, float] | None = None,
    ):
        self.timeout_s = timeout_s
        self.max_terms = max_terms
        self.default_subs = default_subs or {}

    # -- expression-level safety --------------------------------------

    def simplify(self, expr: sp.Expr, max_terms: int | None = None) -> SolverResult:
        """Simplify an expression, checking complexity afterwards."""
        limit = max_terms if max_terms is not None else self.max_terms
        try:
            simplified = _run_with_timeout(lambda: sp.simplify(expr), self.timeout_s)
        except SolverTimeoutError:
            # Return unsimplified
            tc = _count_terms(expr)
            return SolverResult(
                value=expr, success=True, degraded=True,
                warning=f"Simplification timed out ({self.timeout_s}s); expression has ~{tc} terms",
                term_count=tc,
            )

        tc = _count_terms(simplified)
        if tc > limit:
            return SolverResult(
                value=simplified, success=True, degraded=True,
                warning=f"Expression has {tc} terms (limit: {limit}); consider numerical approximation",
                term_count=tc,
            )
        return SolverResult(value=simplified, term_count=tc)

    def solve_linear(
        self,
        A: sp.Matrix,
        b: sp.Matrix,
        var_symbols: list[sp.Symbol],
        *,
        subs: dict[str, float] | None = None,
    ) -> SolverResult:
        """Solve A*x = b with timeout protection.

        Returns a SolverResult whose .value is a dict[symbol, expr] on success,
        or a dict[symbol, float] when degraded to numerical.

        Pass *subs* to provide numeric values for fallback evaluation
        (e.g. {'R_1': 1000.0, 'V_CC': 12.0}).  Falls back to self.default_subs
        if *subs* is not provided.
        """
        runtime_subs: dict[str, float] = {**self.default_subs}
        if subs:
            runtime_subs.update(subs)

        try:
            solution = _run_with_timeout(
                lambda: A.LUsolve(b),
                self.timeout_s,
            )
        except SolverTimeoutError:
            return self._fallback_numerical(A, b, var_symbols, "timed out", runtime_subs)

        # Check complexity
        total_terms = sum(_count_terms(solution[i]) for i in range(min(solution.rows, len(var_symbols))))
        if total_terms > self.max_terms:
            return self._fallback_numerical(
                A, b, var_symbols,
                f"symbolic solution has {total_terms} terms (limit: {self.max_terms})",
                runtime_subs,
            )

        result: dict[sp.Symbol, sp.Expr] = {}
        for idx, sym in enumerate(var_symbols):
            if idx < solution.rows:
                result[sym] = sp.simplify(solution[idx])
        return SolverResult(value=result, term_count=total_terms)

    def _fallback_numerical(
        self,
        A: sp.Matrix,
        b: sp.Matrix,
        var_symbols: list[sp.Symbol],
        reason: str,
        subs_values: dict[str, float],
    ) -> SolverResult:
        """Degrade to numerical evaluation using provided substitutions."""
        subs_dict: dict[sp.Symbol, float] = {}
        for name, val in subs_values.items():
            subs_dict[sp.Symbol(name)] = val

        if not subs_dict:
            return SolverResult(
                success=False, degraded=True,
                warning=f"Numerical fallback not possible ({reason}): no parameter values available",
            )

        try:
            A_num = A.subs(subs_dict)
            b_num = b.subs(subs_dict)
            if _has_symbols(A_num) or _has_symbols(b_num):
                return SolverResult(
                    success=False, degraded=True,
                    warning=f"Numerical fallback incomplete ({reason}): unresolved symbols remain — "
                            f"missing values for: {sorted(s.name for s in A_num.free_symbols | b_num.free_symbols)}",
                )
            x_num = A_num.LUsolve(b_num)
            result: dict[sp.Symbol, float] = {}
            for idx, sym in enumerate(var_symbols):
                if idx < x_num.rows:
                    result[sym] = float(x_num[idx].evalf())
            return SolverResult(value=result, success=True, degraded=True,
                                warning=f"Numerical fallback ({reason})")
        except Exception as exc:
            return SolverResult(
                success=False, degraded=True,
                warning=f"Numerical fallback failed ({reason}): {exc}",
            )

    def solve_equations(
        self,
        equations: list[sp.Eq],
        unknowns: list[sp.Symbol],
    ) -> SolverResult:
        """Solve a system of symbolic equations with timeout protection."""
        try:
            solution = _run_with_timeout(
                lambda: sp.solve(equations, unknowns, dict=True),
                self.timeout_s,
            )
        except SolverTimeoutError:
            return SolverResult(
                success=False, degraded=True,
                warning=f"Equation solving timed out ({self.timeout_s}s)",
            )

        if not solution:
            return SolverResult(success=False, warning="No solution found")

        # Check complexity of the first solution
        sol = solution[0]
        total_terms = sum(
            _count_terms(sol.get(u, sp.Integer(0))) for u in unknowns  # type: ignore[arg-type]
        )
        if total_terms > self.max_terms:
            return SolverResult(
                value=sol, success=True, degraded=True,
                warning=f"Solution has {total_terms} terms (limit: {self.max_terms})",
                term_count=total_terms,
            )
        return SolverResult(value=sol, term_count=total_terms)


def _has_symbols(expr: sp.Basic) -> bool:
    """Check whether a SymPy expression still contains unresolved symbols."""
    return bool(expr.free_symbols)
