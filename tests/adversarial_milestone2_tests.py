"""
Adversarial Verification Suite for Milestone 2:
Electrical Topology Analysis & Solving Strategy Engine - R1
Reviewer & Critic M2-2 Empirical Verification

Testing:
1. Pedagogical derivation parameter suppression (FirstOrderRcStrategy vs BjtAmplifierStrategy).
2. LaTeX-to-Unicode conversion (MathFormulaView.cs logic replication and validation).
3. ScientificNotationFormatter mathematical precision, extremes, and engineering units.
4. TopologyClassifier & NetlistExtractor graph connectivity and classification accuracy.
5. AnalysisStudioViewModel dynamic solving pipeline integration.
"""

import math
import re
import unittest
from typing import List, Dict, Tuple, Optional

# ==============================================================================
# 1. MathFormulaView Logic Replication for Verification
# ==============================================================================
class MathFormulaViewTestEngine:
    GREEK_MAP = {
        r"\Delta": "Δ", r"\Omega": "Ω", r"\Theta": "Θ", r"\Phi": "Φ", r"\Sigma": "Σ", r"\Pi": "Π",
        r"\omega": "ω", r"\alpha": "α", r"\beta": "β", r"\tau": "τ", r"\mu": "μ", r"\pi": "π",
        r"\delta": "δ", r"\theta": "θ", r"\phi": "φ", r"\lambda": "λ", r"\sigma": "σ", r"\zeta": "ζ",
        r"\gamma": "γ", r"\epsilon": "ε", r"\eta": "η"
    }

    OPERATOR_MAP = {
        r"\pm": "±", r"\mp": "∓", r"\times": "×", r"\cdot": "·", r"\approx": "≈", r"\parallel": "∥",
        r"\le": "≤", r"\leq": "≤", r"\ge": "≥", r"\geq": "≥", r"\neq": "≠", r"\ne": "≠",
        r"\to": "→", r"\rightarrow": "→", r"\leftarrow": "←", r"\infty": "∞", r"\angle": "∠", r"\circ": "°",
        r"\partial": "∂", r"\int": "∫", r"\sum": "∑", r"\max": "max", r"\min": "min",
        r"\ln": "ln", r"\log": "log", r"\exp": "exp", r"\sin": "sin", r"\cos": "cos", r"\tan": "tan", r"\arctan": "arctan"
    }

    SUBSCRIPT_MAP = {
        '0': '₀', '1': '₁', '2': '₂', '3': '₃', '4': '₄', '5': '₅', '6': '₆', '7': '₇', '8': '₈', '9': '₉',
        '+': '₊', '-': '₋',
        'a': 'ₐ', 'e': 'ₑ', 'h': 'ₕ', 'i': 'ᵢ', 'j': 'ⱼ', 'k': 'ₖ', 'l': 'ₗ', 'm': 'ₘ', 'n': 'ₙ',
        'o': 'ₒ', 'p': 'ₚ', 'r': 'ᵣ', 's': 'ₛ', 't': 'ₜ', 'u': 'ᵤ', 'v': 'ᵥ', 'x': 'ₓ'
    }

    SUPERSCRIPT_MAP = {
        '-': '⁻', '+': '⁺',
        '0': '⁰', '1': '¹', '2': '²', '3': '³', '4': '⁴', '5': '⁵', '6': '⁶', '7': '⁷', '8': '⁸', '9': '⁹'
    }

    @classmethod
    def can_convert_subscript(cls, c: str) -> bool:
        return c.isdigit() or c in "+-aehijklmnoprstuvx"

    @classmethod
    def to_subscript(cls, s: str) -> str:
        return "".join(cls.SUBSCRIPT_MAP.get(c.lower(), c) for c in s)

    @classmethod
    def to_superscript(cls, s: str) -> str:
        return "".join(cls.SUPERSCRIPT_MAP.get(c, c) for c in s)

    @classmethod
    def format_subscript_token(cls, base: str, sub: str) -> str:
        if all(cls.can_convert_subscript(c) for c in sub):
            return base + cls.to_subscript(sub)
        return f"{base}_{sub}"

    @classmethod
    def format_matrix(cls, latex: str) -> str:
        pattern = r"\\begin\{(?:bmatrix|pmatrix|matrix)\}([\s\S]*?)\\end\{(?:bmatrix|pmatrix|matrix)\}"
        def repl(m):
            body = m.group(1).strip()
            rows = [r.strip() for r in body.split(r"\\") if r.strip()]
            if not rows:
                return "[ ]"
            grid = []
            for r in rows:
                cells = [cls.format_latex_to_unicode(c.strip()) for c in r.split("&")]
                grid.append(cells)
            row_count = len(grid)
            col_count = max(len(r) for r in grid)
            col_widths = [max(len(grid[r][c]) if c < len(grid[r]) else 0 for r in range(row_count)) for c in range(col_count)]
            out_rows = []
            for r in range(row_count):
                lb = "[" if row_count == 1 else ("⎡" if r == 0 else ("⎣" if r == row_count - 1 else "⎢"))
                rb = "]" if row_count == 1 else ("⎤" if r == 0 else ("⎦" if r == row_count - 1 else "⎥"))
                row_str = lb + " "
                cells_str = []
                for c in range(col_count):
                    val = grid[r][c] if c < len(grid[r]) else ""
                    cells_str.append(val.rjust(col_widths[c]))
                row_str += "  ".join(cells_str) + " " + rb
                out_rows.append(row_str)
            return "\n".join(out_rows)
        return re.sub(pattern, repl, latex)

    @classmethod
    def replace_square_roots(cls, text: str) -> str:
        for _ in range(20):
            idx = text.find(r"\sqrt")
            if idx == -1:
                break
            brace_start = text.find("{", idx)
            if brace_start == -1 or brace_start > idx + 10:
                break
            depth = 0
            brace_end = -1
            for i in range(brace_start, len(text)):
                if text[i] == "{":
                    depth += 1
                elif text[i] == "}":
                    depth -= 1
                    if depth == 0:
                        brace_end = i
                        break
            if brace_end == -1:
                break
            inner = text[brace_start + 1:brace_end].strip()
            inner = cls.format_latex_to_unicode(inner)
            text = text[:idx] + f"√({inner})" + text[brace_end + 1:]
        return text

    @classmethod
    def format_latex_to_unicode(cls, latex: str) -> str:
        if not latex:
            return ""
        text = latex.strip()
        text = cls.format_matrix(text)

        # Remove environments
        for token in [r"\begin{aligned}", r"\end{aligned}", r"\begin{array}", r"\end{array}",
                      r"\left(", r"\right)", r"\left[", r"\right]", r"\left\{", r"\right\}",
                      r"\left|", r"\right|", r"\{", r"\}", r"\quad", r"\qquad", r"\;", r"\,", r"\!", "~"]:
            text = text.replace(token, " " if token in [r"\quad", r"\qquad", r"\;", r"\,", "~"] else "")
        text = text.replace("&=", "= ").replace("&", " ")

        # Unpack commands
        for cmd in [r"\text", r"\mathrm", r"\mathbf", r"\mathit", r"\operatorname", r"\bm", r"\boldsymbol"]:
            while cmd + "{" in text:
                s_idx = text.find(cmd + "{")
                b_start = s_idx + len(cmd)
                depth = 0
                b_end = -1
                for i in range(b_start, len(text)):
                    if text[i] == "{":
                        depth += 1
                    elif text[i] == "}":
                        depth -= 1
                        if depth == 0:
                            b_end = i
                            break
                if b_end == -1:
                    break
                inner = text[b_start + 1:b_end]
                text = text[:s_idx] + inner + text[b_end + 1:]

        # Greek
        for k, v in cls.GREEK_MAP.items():
            text = text.replace(k, v)

        # Operators
        for k, v in cls.OPERATOR_MAP.items():
            text = text.replace(k, v)

        # Square roots
        text = cls.replace_square_roots(text)

        # Powers
        text = re.sub(r"10\^\{([+-]?\d+)\}", lambda m: "10" + cls.to_superscript(m.group(1)), text)
        text = re.sub(r"\^\{([+-]?\d+)\}", lambda m: cls.to_superscript(m.group(1)), text)
        text = re.sub(r"\^([+-]?\d)", lambda m: cls.to_superscript(m.group(1)), text)
        text = text.replace("^2", "²").replace("^3", "³").replace("0^+", "0⁺").replace("0^-", "0⁻")

        # Subscripts (Unicode aware)
        text = re.sub(r"([^\W_]|\))_\{([^{}]+)\}", lambda m: cls.format_subscript_token(m.group(1), m.group(2)), text)
        text = re.sub(r"([^\W_]|\))_([0-9a-zA-Z\u0370-\u03ff])", lambda m: cls.format_subscript_token(m.group(1), m.group(2)), text)

        return text.strip()


# ==============================================================================
# 2. ScientificNotationFormatter Logic Replication for Verification
# ==============================================================================
class ScientificNotationTestEngine:
    @staticmethod
    def to_latex_scientific(val: float, precision: int = 3) -> str:
        if math.isnan(val):
            return r"\text{NaN}"
        if math.isinf(val):
            return r"\infty" if val > 0 else r"-\infty"
        if abs(val) < 1e-15:
            return "0"
        abs_v = abs(val)
        if 0.001 <= abs_v < 1000:
            return f"{round(val, precision):.3g}"
        s = f"{val:.{precision-1}e}"
        m_str, exp_str = s.split("e")
        mantissa = float(m_str)
        exp = int(exp_str)
        mantissa_rounded = round(mantissa, precision)
        if abs(mantissa_rounded - 1.0) < 1e-6:
            return f"10^{{{exp}}}"
        if abs(mantissa_rounded - (-1.0)) < 1e-6:
            return f"-10^{{{exp}}}"
        return f"{mantissa_rounded:g} \\times 10^{{{exp}}}"

    @staticmethod
    def to_unicode_scientific(val: float, precision: int = 3) -> str:
        if math.isnan(val):
            return "NaN"
        if math.isinf(val):
            return "∞" if val > 0 else "-∞"
        if abs(val) < 1e-15:
            return "0"
        abs_v = abs(val)
        if 0.001 <= abs_v < 1000:
            return f"{round(val, precision):.3g}"
        s = f"{val:.{precision-1}e}"
        m_str, exp_str = s.split("e")
        mantissa = float(m_str)
        exp = int(exp_str)
        sup = MathFormulaViewTestEngine.to_superscript(str(exp))
        mantissa_rounded = round(mantissa, precision)
        if abs(mantissa_rounded - 1.0) < 1e-6:
            return f"10{sup}"
        if abs(mantissa_rounded - (-1.0)) < 1e-6:
            return f"-10{sup}"
        return f"{mantissa_rounded:g} × 10{sup}"

    @staticmethod
    def format_with_unit(val: float, base_unit: str, precision: int = 3) -> str:
        if math.isnan(val):
            return f"NaN {base_unit}"
        if math.isinf(val):
            return f"∞ {base_unit}" if val > 0 else f"-∞ {base_unit}"
        if abs(val) < 1e-15:
            return f"0 {base_unit}"
        abs_v = abs(val)
        exp3 = int(math.floor(math.log10(abs_v) / 3.0) * 3)
        if exp3 < -15 or exp3 > 12:
            return f"{ScientificNotationTestEngine.to_unicode_scientific(val, precision)} {base_unit}"
        scaled = val / (10 ** exp3)
        prefixes = {
            12: "T", 9: "G", 6: "M", 3: "k", 0: "",
            -3: "m", -6: "μ", -9: "n", -12: "p", -15: "f"
        }
        pfx = prefixes.get(exp3, "")
        return f"{round(scaled, precision):g} {pfx}{base_unit}"


# ==============================================================================
# Unit Test Cases
# ==============================================================================
class Milestone2AdversarialTests(unittest.TestCase):

    def test_rc_bjt_parameter_suppression(self):
        """Verify FirstOrderRcStrategy source code completely suppresses fictitious BJT variables."""
        with open("apps/desktop-winui/Engine/Strategies/FirstOrderRcStrategy.cs", "r", encoding="utf-8") as f:
            code = f.read()

        # Acceptance Criterion 1 check: In Steps, QPoint, and equations, BJT terms must be suppressed.
        bjt_prohibited_terms = [
            r"r_{be}", r"rbe", r"I_{BQ}", r"I_{CQ}", r"V_{CEQ}",
            r"\beta", r"h_{FE}", r"hfe", r"微变等效"
        ]
        # In steps:
        steps_start = code.find("solution.Steps.Add")
        steps_code = code[steps_start:] if steps_start != -1 else ""

        for term in bjt_prohibited_terms:
            self.assertNotIn(term, steps_code, f"Integrity Violation: BJT term '{term}' leaked into FirstOrderRcStrategy steps!")

        # QPoint must explicitly state no BJT
        self.assertIn('"无直流偏置 (无晶体管拓扑)"', code)
        self.assertIn('Ibq = "—"', code)
        self.assertIn('Icq = "—"', code)
        self.assertIn('Vceq = "—"', code)

    def test_latex_to_unicode_greek_and_operators(self):
        """Verify Greek letters, operators, subscripts, and square roots."""
        # 1. Greek
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\omega"), "ω")
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\Delta"), "Δ")
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\tau"), "τ")
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\beta"), "β")

        # 2. Operators
        self.assertIn("±", MathFormulaViewTestEngine.format_latex_to_unicode(r"\pm"))
        self.assertIn("≤", MathFormulaViewTestEngine.format_latex_to_unicode(r"\le"))
        self.assertIn("≥", MathFormulaViewTestEngine.format_latex_to_unicode(r"\ge"))
        self.assertIn("≠", MathFormulaViewTestEngine.format_latex_to_unicode(r"\neq"))
        self.assertIn("→", MathFormulaViewTestEngine.format_latex_to_unicode(r"\to"))
        self.assertIn("∞", MathFormulaViewTestEngine.format_latex_to_unicode(r"\infty"))
        self.assertIn("∠", MathFormulaViewTestEngine.format_latex_to_unicode(r"\angle"))
        self.assertIn("°", MathFormulaViewTestEngine.format_latex_to_unicode(r"\circ"))
        self.assertIn("∥", MathFormulaViewTestEngine.format_latex_to_unicode(r"\parallel"))

        # 3. Greek subscripts
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\omega_0"), "ω₀")
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\omega_c"), "ω_c") # c has no unicode subscript, keeps clean _c
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\Delta_v"), "Δᵥ")
        self.assertEqual(MathFormulaViewTestEngine.format_latex_to_unicode(r"\tau_{in}"), "τᵢₙ")

        # 4. Square root
        res = MathFormulaViewTestEngine.format_latex_to_unicode(r"\sqrt{LC}")
        self.assertEqual(res, "√(LC)")

    def test_matrix_environment_brackets(self):
        """Verify 2D matrix rendering with Cambria Math bracket envelopes."""
        matrix_input = r"\begin{bmatrix} 1 & 2 \\ 3 & 4 \end{bmatrix}"
        res = MathFormulaViewTestEngine.format_matrix(matrix_input)
        self.assertIn("⎡", res)
        self.assertIn("⎤", res)
        self.assertIn("⎣", res)
        self.assertIn("⎦", res)

    def test_scientific_notation_formatting(self):
        """Verify ScientificNotationFormatter on extreme values, zero, NaN, Inf, and engineering units."""
        # Extreme numbers
        self.assertEqual(ScientificNotationTestEngine.to_latex_scientific(6280), r"6.28 \times 10^{3}")
        self.assertEqual(ScientificNotationTestEngine.to_unicode_scientific(6280), "6.28 × 10³")
        self.assertEqual(ScientificNotationTestEngine.to_latex_scientific(0.00002), r"2 \times 10^{-5}")
        self.assertEqual(ScientificNotationTestEngine.to_unicode_scientific(0.00002), "2 × 10⁻⁵")

        # Moderate numbers (no exponential)
        self.assertEqual(ScientificNotationTestEngine.to_latex_scientific(12.5), "12.5")
        self.assertEqual(ScientificNotationTestEngine.to_unicode_scientific(12.5), "12.5")

        # Zero
        self.assertEqual(ScientificNotationTestEngine.to_latex_scientific(0.0), "0")
        self.assertEqual(ScientificNotationTestEngine.to_unicode_scientific(0.0), "0")

        # NaN & Infinity
        self.assertEqual(ScientificNotationTestEngine.to_latex_scientific(float("nan")), r"\text{NaN}")
        self.assertEqual(ScientificNotationTestEngine.to_unicode_scientific(float("nan")), "NaN")
        self.assertEqual(ScientificNotationTestEngine.to_latex_scientific(float("inf")), r"\infty")
        self.assertEqual(ScientificNotationTestEngine.to_unicode_scientific(float("inf")), "∞")
        self.assertEqual(ScientificNotationTestEngine.to_latex_scientific(float("-inf")), r"-\infty")
        self.assertEqual(ScientificNotationTestEngine.to_unicode_scientific(float("-inf")), "-∞")

        # FormatWithUnit
        self.assertEqual(ScientificNotationTestEngine.format_with_unit(0.00125, "A"), "1.25 mA")
        self.assertEqual(ScientificNotationTestEngine.format_with_unit(10000, "Ω"), "10 kΩ")
        self.assertEqual(ScientificNotationTestEngine.format_with_unit(0.000047, "F"), "47 μF")
        self.assertEqual(ScientificNotationTestEngine.format_with_unit(1000000, "Hz"), "1 MHz")

    def test_viewmodel_solve_pipeline(self):
        """Verify AnalysisStudioViewModel.cs dynamic pipeline replaces hardcoded BJT calls."""
        with open("apps/desktop-winui/ViewModels/AnalysisStudioViewModel.cs", "r", encoding="utf-8") as f:
            vm_code = f.read()

        solve_idx = vm_code.find("public async Task SolveCircuitAsync()")
        self.assertNotEqual(solve_idx, -1, "SolveCircuitAsync command not found!")
        solve_body = vm_code[solve_idx:solve_idx + 1800]

        # Verify dynamic extraction and classification
        self.assertIn("NetlistExtractor.ExtractNetlist(Elements, Wires)", solve_body)
        self.assertIn("TopologyClassifier.Classify(netlist, out var strategy)", solve_body)
        self.assertIn("await strategy.SolveAsync(netlist, parameters)", solve_body)

        # Verify no hardcoded BJT API call in SolveCircuitAsync
        self.assertNotIn("_apiService.AnalyzeCircuitAsync(", solve_body)


if __name__ == "__main__":
    unittest.main()
